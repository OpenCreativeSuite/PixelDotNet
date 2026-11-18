using System;
using System.IO;
using StbImageSharp;

namespace PixelDotNet.Paint;

/// <summary>Decodes image files into packed <c>0xAARRGGBB</c> pixels.</summary>
/// <remarks>
/// StbImageSharp covers PNG, JPEG, BMP and TGA, which is the same set the
/// exporter writes. Decoding always asks for RGBA so alpha survives the trip
/// through the compositor.
/// </remarks>
public static class ImageLoader
{
    /// <summary>Gets the file extensions the loader understands, including the dot.</summary>
    public static string[] Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".tga"];

    /// <summary>Gets whether a file name has a supported image extension.</summary>
    public static bool IsImage(string path) =>
        Array.Exists(
            Extensions,
            extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads an image file into a packed pixel buffer.</summary>
    /// <param name="path">Image file to decode.</param>
    /// <returns>The decoded size and pixels.</returns>
    /// <exception cref="InvalidDataException">The file is not a decodable image.</exception>
    public static (int Width, int Height, uint[] Pixels) Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ImageResult image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);

        if (image.Width <= 0 || image.Height <= 0 || image.Data is null)
        {
            throw new InvalidDataException("The file is not a supported image.");
        }

        uint[] pixels = new uint[image.Width * image.Height];
        byte[] data = image.Data;
        int offset = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            int red = data[offset++];
            int green = data[offset++];
            int blue = data[offset++];
            int alpha = data[offset++];
            pixels[i] = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
        }

        return (image.Width, image.Height, pixels);
    }
}
