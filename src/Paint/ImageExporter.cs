using System;
using System.IO;
using StbImageWriteSharp;

namespace PixelDotNet.Paint;

/// <summary>Raster formats the document can be written as.</summary>
public enum ImageFormat
{
    /// <summary>Lossless RGBA with an alpha channel.</summary>
    Png,

    /// <summary>Lossy, no alpha; transparent pixels are matted onto white.</summary>
    Jpeg,

    /// <summary>Uncompressed 24-bit BGR.</summary>
    Bmp,

    /// <summary>Uncompressed 32-bit BGRA that keeps the alpha channel.</summary>
    Tga,
}

/// <summary>Encodes the composited document into the supported image formats.</summary>
/// <remarks>
/// PNG uses the in-house writer so the common case has no external dependency;
/// the other formats go through StbImageWriteSharp, which takes RGBA bytes.
/// </remarks>
public static class ImageExporter
{
    /// <summary>Gets the file extension, including the dot, for a format.</summary>
    public static string Extension(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Tga => ".tga",
        _ => ".png",
    };

    /// <summary>Gets the display name of a format.</summary>
    public static string NameOf(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "JPEG",
        ImageFormat.Bmp => "BMP",
        ImageFormat.Tga => "TGA",
        _ => "PNG",
    };

    /// <summary>Encodes packed pixels as a file-ready byte array.</summary>
    public static byte[] Encode(uint[] pixels, int width, int height, ImageFormat format)
    {
        if (format == ImageFormat.Png)
        {
            return PngWriter.Encode(pixels, width, height);
        }

        bool withAlpha = format == ImageFormat.Tga;
        byte[] data = ToBytes(pixels, withAlpha);
        using MemoryStream output = new();
        ImageWriter writer = new();

        switch (format)
        {
            case ImageFormat.Jpeg:
                writer.WriteJpg(data, width, height, ColorComponents.RedGreenBlue, output, 92);
                break;

            case ImageFormat.Bmp:
                writer.WriteBmp(data, width, height, ColorComponents.RedGreenBlue, output);
                break;

            default:
                writer.WriteTga(data, width, height, ColorComponents.RedGreenBlueAlpha, output);
                break;
        }

        return output.ToArray();
    }

    /// <summary>Converts packed pixels to RGB or RGBA bytes, matting onto white when needed.</summary>
    private static byte[] ToBytes(uint[] pixels, bool withAlpha)
    {
        int stride = withAlpha ? 4 : 3;
        byte[] data = new byte[pixels.Length * stride];
        int offset = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            uint color = pixels[i];
            int alpha = Rgba.A(color);
            int red = Rgba.R(color);
            int green = Rgba.G(color);
            int blue = Rgba.B(color);

            if (!withAlpha && alpha < 255)
            {
                // Formats without alpha expect the colour already flattened.
                red = ((red * alpha) + (255 * (255 - alpha)) + 127) / 255;
                green = ((green * alpha) + (255 * (255 - alpha)) + 127) / 255;
                blue = ((blue * alpha) + (255 * (255 - alpha)) + 127) / 255;
            }

            data[offset++] = (byte)red;
            data[offset++] = (byte)green;
            data[offset++] = (byte)blue;

            if (withAlpha)
            {
                data[offset++] = (byte)alpha;
            }
        }

        return data;
    }
}
