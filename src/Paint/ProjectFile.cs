using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PixelDotNet.Paint;

/// <summary>
/// Reads and writes the layered project format (<c>.pxd</c>).
/// </summary>
/// <remarks>
/// The format keeps everything the editor needs to resume work: layer pixels,
/// masks, display settings, adjustment layers, groups and guides. Data is
/// GZip-compressed, so a typical document stays reasonably small.
/// </remarks>
public static class ProjectFile
{
    /// <summary>Gets the project file extension, including the dot.</summary>
    public const string Extension = ".pxd";

    private const string Signature = "PXDP";
    private const int Version = 3;

    /// <summary>Gets whether a file name has the project extension.</summary>
    public static bool IsProject(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes a document to a project file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="document">Document to serialise.</param>
    public static void Save(string path, PaintDocument document)
    {
        using FileStream file = File.Create(path);
        using GZipStream compression = new(file, CompressionLevel.Optimal);
        using BinaryWriter writer = new(compression, Encoding.UTF8);

        writer.Write(Encoding.ASCII.GetBytes(Signature));
        writer.Write(Version);
        writer.Write(document.Width);
        writer.Write(document.Height);
        writer.Write(document.ActiveIndex);

        Dictionary<LayerGroup, int> groupIndices = [];
        writer.Write(document.Groups.Count);
        for (int i = 0; i < document.Groups.Count; i++)
        {
            LayerGroup group = document.Groups[i];
            groupIndices[group] = i;
            writer.Write(group.Name);
            writer.Write(group.Visible);
            writer.Write(group.Opacity);
            writer.Write(group.Expanded);
        }

        writer.Write(document.Layers.Count);
        foreach (PaintLayer layer in document.Layers)
        {
            writer.Write(layer.Name);
            writer.Write(layer.IsBackground);
            writer.Write((int)layer.Kind);
            writer.Write((int)layer.Adjustment);
            writer.Write(layer.Visible);
            writer.Write(layer.Opacity);
            writer.Write((int)layer.Blend);
            writer.Write(layer.Locked);
            writer.Write(layer.ClipToBelow);
            writer.Write(layer.Group is not null && groupIndices.TryGetValue(layer.Group, out int groupIndex)
                ? groupIndex
                : -1);

            byte[]? mask = layer.Mask;
            writer.Write(mask is not null);
            if (mask is not null)
            {
                writer.Write(mask.Length);
                writer.Write(mask);
            }

            writer.Write(layer.AdjustmentBlack);
            writer.Write(layer.AdjustmentWhite);
            writer.Write(layer.AdjustmentGamma);
            writer.Write(layer.AdjustmentCurve.Count);
            foreach ((float x, float y) in layer.AdjustmentCurve)
            {
                writer.Write(x);
                writer.Write(y);
            }

            LayerStyle style = layer.Style;
            writer.Write(style.DropShadow);
            writer.Write(style.DropShadowColor);
            writer.Write(style.DropShadowOpacity);
            writer.Write(style.DropShadowDistance);
            writer.Write(style.DropShadowAngle);
            writer.Write(style.DropShadowBlur);
            writer.Write(style.Stroke);
            writer.Write(style.StrokeColor);
            writer.Write(style.StrokeOpacity);
            writer.Write(style.StrokeWidth);
            writer.Write(style.StrokeInside);
            writer.Write(style.OuterGlow);
            writer.Write(style.OuterGlowColor);
            writer.Write(style.OuterGlowOpacity);
            writer.Write(style.OuterGlowSize);

            writer.Write(layer.IsSmartObject);
            if (layer.IsSmartObject && layer.SmartSource is { } smart)
            {
                writer.Write(layer.SmartSourceWidth);
                writer.Write(layer.SmartSourceHeight);
                writer.Write(layer.SmartX);
                writer.Write(layer.SmartY);
                writer.Write(layer.SmartScaleX);
                writer.Write(layer.SmartScaleY);
                writer.Write(layer.SmartRotation);
                writer.Write(layer.SmartFlipX);
                writer.Write(layer.SmartFlipY);

                byte[] smartBytes = new byte[smart.Length * sizeof(uint)];
                Buffer.BlockCopy(smart, 0, smartBytes, 0, smartBytes.Length);
                writer.Write(smartBytes.Length);
                writer.Write(smartBytes);
            }

            byte[] pixels = new byte[layer.Pixels.Length * sizeof(uint)];
            Buffer.BlockCopy(layer.Pixels, 0, pixels, 0, pixels.Length);
            writer.Write(pixels.Length);
            writer.Write(pixels);
        }

        writer.Write(document.Guides.Count);
        foreach (Guide guide in document.Guides)
        {
            writer.Write(guide.Horizontal);
            writer.Write(guide.Position);
        }
    }

    /// <summary>Reads a project file into a new document.</summary>
    /// <param name="path">Project file to read.</param>
    /// <returns>The restored document.</returns>
    /// <exception cref="InvalidDataException">The file is not a supported project.</exception>
    public static PaintDocument Load(string path)
    {
        using FileStream file = File.OpenRead(path);
        using GZipStream compression = new(file, CompressionMode.Decompress);
        using BinaryReader reader = new(compression, Encoding.UTF8);

        byte[] signature = reader.ReadBytes(Signature.Length);
        if (signature.Length != Signature.Length
            || Encoding.ASCII.GetString(signature) != Signature)
        {
            throw new InvalidDataException("Not a PixelDotNet project file.");
        }

        int version = reader.ReadInt32();
        if (version < 1 || version > Version)
        {
            throw new InvalidDataException("Unsupported project version " + version + ".");
        }

        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        if (width < 1 || height < 1 || width > 8192 || height > 8192)
        {
            throw new InvalidDataException("The project has an invalid canvas size.");
        }

        PaintDocument document = new(width, height);
        int activeIndex = reader.ReadInt32();

        int groupCount = reader.ReadInt32();
        if (groupCount < 0 || groupCount > 10000)
        {
            throw new InvalidDataException("The project has an invalid group count.");
        }

        LayerGroup[] groups = new LayerGroup[groupCount];
        for (int i = 0; i < groupCount; i++)
        {
            LayerGroup group = new(reader.ReadString())
            {
                Visible = reader.ReadBoolean(),
                Opacity = reader.ReadSingle(),
                Expanded = reader.ReadBoolean(),
            };

            groups[i] = group;
            document.Groups.Add(group);
        }

        int layerCount = reader.ReadInt32();
        if (layerCount < 1 || layerCount > 10000)
        {
            throw new InvalidDataException("The project has an invalid layer count.");
        }

        int expectedPixels = width * height;
        for (int i = 0; i < layerCount; i++)
        {
            string name = reader.ReadString();
            bool isBackground = reader.ReadBoolean();
            LayerKind kind = (LayerKind)reader.ReadInt32();
            AdjustmentKind adjustment = (AdjustmentKind)reader.ReadInt32();

            PaintLayer layer = new(name, width, height, isBackground, kind, adjustment)
            {
                Visible = reader.ReadBoolean(),
                Opacity = reader.ReadSingle(),
                Blend = (BlendMode)reader.ReadInt32(),
                Locked = reader.ReadBoolean(),
                ClipToBelow = reader.ReadBoolean(),
            };

            int groupIndex = reader.ReadInt32();
            if (groupIndex >= 0 && groupIndex < groups.Length)
            {
                layer.Group = groups[groupIndex];
            }

            if (reader.ReadBoolean())
            {
                int maskLength = reader.ReadInt32();
                if (maskLength != expectedPixels)
                {
                    throw new InvalidDataException("A layer mask has the wrong size.");
                }

                layer.AdoptMask(reader.ReadBytes(maskLength));
            }

            layer.AdjustmentBlack = reader.ReadSingle();
            layer.AdjustmentWhite = reader.ReadSingle();
            layer.AdjustmentGamma = reader.ReadSingle();

            int curveCount = reader.ReadInt32();
            if (curveCount < 0 || curveCount > 4096)
            {
                throw new InvalidDataException("An adjustment curve is invalid.");
            }

            if (curveCount > 0)
            {
                layer.AdjustmentCurve.Clear();
                for (int point = 0; point < curveCount; point++)
                {
                    layer.AdjustmentCurve.Add((reader.ReadSingle(), reader.ReadSingle()));
                }
            }

            if (version >= 2)
            {
                LayerStyle style = layer.Style;
                style.DropShadow = reader.ReadBoolean();
                style.DropShadowColor = reader.ReadUInt32();
                style.DropShadowOpacity = reader.ReadSingle();
                style.DropShadowDistance = reader.ReadSingle();
                style.DropShadowAngle = reader.ReadSingle();
                style.DropShadowBlur = reader.ReadSingle();
                style.Stroke = reader.ReadBoolean();
                style.StrokeColor = reader.ReadUInt32();
                style.StrokeOpacity = reader.ReadSingle();
                style.StrokeWidth = reader.ReadSingle();
                style.StrokeInside = reader.ReadBoolean();
                style.OuterGlow = reader.ReadBoolean();
                style.OuterGlowColor = reader.ReadUInt32();
                style.OuterGlowOpacity = reader.ReadSingle();
                style.OuterGlowSize = reader.ReadSingle();
            }

            if (version >= 3 && reader.ReadBoolean())
            {
                int smartWidth = reader.ReadInt32();
                int smartHeight = reader.ReadInt32();
                float smartX = reader.ReadSingle();
                float smartY = reader.ReadSingle();
                float smartScaleX = reader.ReadSingle();
                float smartScaleY = reader.ReadSingle();
                float smartRotation = reader.ReadSingle();
                bool smartFlipX = reader.ReadBoolean();
                bool smartFlipY = reader.ReadBoolean();
                int smartBytes = reader.ReadInt32();

                if (smartWidth < 1
                    || smartHeight < 1
                    || smartBytes != smartWidth * smartHeight * sizeof(uint))
                {
                    throw new InvalidDataException("A smart object has an invalid source size.");
                }

                byte[] smartData = reader.ReadBytes(smartBytes);
                uint[] smartPixels = new uint[smartWidth * smartHeight];
                Buffer.BlockCopy(smartData, 0, smartPixels, 0, smartBytes);
                layer.AdoptSmart(
                    smartPixels,
                    smartWidth,
                    smartHeight,
                    smartX,
                    smartY,
                    smartScaleX,
                    smartScaleY,
                    smartRotation,
                    smartFlipX,
                    smartFlipY);
            }

            int pixelBytes = reader.ReadInt32();
            if (pixelBytes != expectedPixels * sizeof(uint))
            {
                throw new InvalidDataException("A layer has the wrong pixel size.");
            }

            byte[] pixels = reader.ReadBytes(pixelBytes);
            Buffer.BlockCopy(pixels, 0, layer.Pixels, 0, pixels.Length);
            document.Layers.Add(layer);
        }

        document.ActiveIndex = Math.Clamp(activeIndex, 0, document.Layers.Count - 1);

        int guideCount = reader.ReadInt32();
        if (guideCount < 0 || guideCount > 10000)
        {
            throw new InvalidDataException("The project has an invalid guide count.");
        }

        for (int i = 0; i < guideCount; i++)
        {
            document.Guides.Add(new Guide(reader.ReadBoolean(), reader.ReadInt32()));
        }

        document.MarkDirty();
        return document;
    }
}
