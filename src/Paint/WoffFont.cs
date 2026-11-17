using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PixelDotNet.Paint;

/// <summary>
/// Converts WOFF 1.0 fonts into plain sfnt (TrueType/OpenType) data.
/// </summary>
/// <remarks>
/// <para>
/// cdnfonts serves most of its catalogue as WOFF, which is a zlib-compressed
/// wrapper around a normal sfnt font. StbTrueType only reads the inner font, so
/// downloading a remote family means rebuilding the table directory with the
/// decompressed tables.
/// </para>
/// <para>
/// WOFF2 uses a different (Brotli-based) layout and is rejected with a clear
/// error. Remote CSS that already points at a TTF/OTF is passed through
/// untouched.
/// </para>
/// </remarks>
public static class WoffFont
{
    private const int HeaderSize = 44;
    private const int DirectoryEntrySize = 20;

    /// <summary>Gets whether the bytes start with the WOFF 1.0 signature.</summary>
    public static bool IsWoff(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && data[0] == (byte)'w'
        && data[1] == (byte)'O'
        && data[2] == (byte)'F'
        && data[3] == (byte)'F';

    /// <summary>Gets whether the bytes start with the WOFF2 signature.</summary>
    public static bool IsWoff2(ReadOnlySpan<byte> data) =>
        data.Length >= 4
        && data[0] == (byte)'w'
        && data[1] == (byte)'O'
        && data[2] == (byte)'F'
        && data[3] == (byte)'2';

    /// <summary>Returns sfnt bytes for the supplied font file.</summary>
    /// <param name="data">The downloaded bytes, WOFF or bare sfnt.</param>
    /// <returns>TrueType/OpenType bytes StbTrueType can open.</returns>
    /// <exception cref="InvalidDataException">The WOFF structure is invalid.</exception>
    /// <exception cref="NotSupportedException">The file is WOFF2.</exception>
    public static byte[] ToSfnt(byte[] data)
    {
        if (IsWoff2(data))
        {
            throw new NotSupportedException("WOFF2 fonts are not supported yet. Pick a family that offers WOFF.");
        }

        if (!IsWoff(data))
        {
            return data;
        }

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("The WOFF header is truncated.");
        }

        uint flavor = ReadUInt32(data, 4);
        int tableCount = ReadUInt16(data, 12);
        if (tableCount <= 0 || tableCount > 512)
        {
            throw new InvalidDataException("The WOFF has an implausible table count.");
        }

        int directoryEnd = HeaderSize + (tableCount * DirectoryEntrySize);
        if (directoryEnd > data.Length)
        {
            throw new InvalidDataException("The WOFF table directory is truncated.");
        }

        List<Table> tables = new(tableCount);
        for (int index = 0; index < tableCount; index++)
        {
            int entry = HeaderSize + (index * DirectoryEntrySize);
            string tag = Encoding.ASCII.GetString(data, entry, 4);
            uint offset = ReadUInt32(data, entry + 4);
            uint compressedLength = ReadUInt32(data, entry + 8);
            uint originalLength = ReadUInt32(data, entry + 12);
            uint checksum = ReadUInt32(data, entry + 16);

            if (compressedLength > int.MaxValue || originalLength > int.MaxValue)
            {
                throw new InvalidDataException($"WOFF table '{tag}' is too large.");
            }

            long end = (long)offset + compressedLength;
            if (offset < directoryEnd || end > data.Length)
            {
                throw new InvalidDataException($"WOFF table '{tag}' points outside the file.");
            }

            byte[] bytes = compressedLength == originalLength
                ? data.AsSpan((int)offset, (int)compressedLength).ToArray()
                : Inflate(data, (int)offset, (int)compressedLength, (int)originalLength);

            tables.Add(new Table(tag, checksum, bytes));
        }

        tables.Sort(static (left, right) => string.CompareOrdinal(left.Tag, right.Tag));
        return BuildSfnt(flavor, tables);
    }

    /// <summary>Rebuilds an sfnt header, table directory and 4-byte aligned tables.</summary>
    private static byte[] BuildSfnt(uint flavor, List<Table> tables)
    {
        int count = tables.Count;
        int power = 1;
        int selector = 0;
        while ((power * 2) <= count)
        {
            power *= 2;
            selector++;
        }

        int searchRange = power * 16;
        int rangeShift = (count * 16) - searchRange;
        int offset = Align4(12 + (count * 16));

        foreach (Table table in tables)
        {
            table.Offset = offset;
            offset = Align4(offset + table.Data.Length);
        }

        byte[] output = new byte[offset];
        WriteUInt32(output, 0, flavor);
        WriteUInt16(output, 4, (ushort)count);
        WriteUInt16(output, 6, (ushort)searchRange);
        WriteUInt16(output, 8, (ushort)selector);
        WriteUInt16(output, 10, (ushort)rangeShift);

        int record = 12;
        foreach (Table table in tables)
        {
            Encoding.ASCII.GetBytes(table.Tag, output.AsSpan(record, 4));
            WriteUInt32(output, record + 4, table.Checksum);
            WriteUInt32(output, record + 8, (uint)table.Offset);
            WriteUInt32(output, record + 12, (uint)table.Data.Length);
            table.Data.CopyTo(output, table.Offset);
            record += 16;
        }

        return output;
    }

    /// <summary>Inflates one WOFF table, tolerating raw-deflate streams.</summary>
    private static byte[] Inflate(byte[] data, int offset, int compressedLength, int originalLength)
    {
        try
        {
            return Inflate(data, offset, compressedLength, originalLength, skipZlibHeader: false);
        }
        catch (InvalidDataException)
        {
            return Inflate(data, offset, compressedLength, originalLength, skipZlibHeader: true);
        }
    }

    private static byte[] Inflate(byte[] data, int offset, int compressedLength, int originalLength, bool skipZlibHeader)
    {
        int start = offset + (skipZlibHeader ? 2 : 0);
        int length = compressedLength - (skipZlibHeader ? 2 : 0);
        if (length <= 0)
        {
            throw new InvalidDataException("The WOFF table stream is empty.");
        }

        byte[] output = new byte[originalLength];
        using MemoryStream input = new(data, start, length, writable: false);
        using Stream inflater = skipZlibHeader
            ? new DeflateStream(input, CompressionMode.Decompress)
            : new ZLibStream(input, CompressionMode.Decompress);

        int written = 0;
        while (written < originalLength)
        {
            int read = inflater.Read(output, written, originalLength - written);
            if (read <= 0)
            {
                break;
            }

            written += read;
        }

        if (written != originalLength)
        {
            throw new InvalidDataException("The WOFF table did not inflate to its declared size.");
        }

        return output;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static ushort ReadUInt16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static void WriteUInt16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset, 2), value);

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);

    /// <summary>One reconstructed sfnt table.</summary>
    private sealed class Table(string tag, uint checksum, byte[] data)
    {
        public string Tag { get; } = tag;

        public uint Checksum { get; } = checksum;

        public byte[] Data { get; } = data;

        public int Offset { get; set; }
    }
}
