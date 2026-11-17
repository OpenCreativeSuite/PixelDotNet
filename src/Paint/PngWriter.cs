using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace PixelDotNet.Paint;

/// <summary>
/// Writes a composited document as an RGBA PNG without any external image library.
/// </summary>
/// <remarks>
/// PNG needs a zlib stream, not raw deflate, so the deflate output from
/// <see cref="DeflateStream"/> is wrapped with the two-byte zlib header and a
/// trailing Adler-32 checksum. Every scanline uses filter type 0 (none), which keeps
/// the encoder small and is perfectly valid.
/// </remarks>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes packed <c>0xAARRGGBB</c> pixels as an RGBA PNG.</summary>
    public static byte[] Encode(uint[] pixels, int width, int height)
    {
        byte[] raw = new byte[height * (1 + (width * 4))];
        int offset = 0;

        for (int y = 0; y < height; y++)
        {
            raw[offset++] = 0;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                uint color = pixels[row + x];
                raw[offset++] = (byte)Rgba.R(color);
                raw[offset++] = (byte)Rgba.G(color);
                raw[offset++] = (byte)Rgba.B(color);
                raw[offset++] = (byte)Rgba.A(color);
            }
        }

        using MemoryStream output = new();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;

        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", Zlib(raw));
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static byte[] Zlib(byte[] raw)
    {
        using MemoryStream output = new();
        output.WriteByte(0x78);
        output.WriteByte(0x01);

        using (DeflateStream deflate = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        uint adler = Adler32(raw);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);

        byte[] name = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        stream.Write(name, 0, 4);
        stream.Write(data, 0, data.Length);

        uint crc = 0xFFFFFFFF;
        crc = Crc(crc, name);
        crc = Crc(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        stream.Write(number);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Adler32(byte[] data)
    {
        const uint Modulus = 65521;
        uint a = 1;
        uint b = 0;

        foreach (byte value in data)
        {
            a = (a + value) % Modulus;
            b = (b + a) % Modulus;
        }

        return (b << 16) | a;
    }
}
