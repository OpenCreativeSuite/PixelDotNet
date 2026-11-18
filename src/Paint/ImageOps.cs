using System;

namespace PixelDotNet.Paint;

/// <summary>Shared pixel operations that do not depend on a document.</summary>
public static class ImageOps
{
    /// <summary>Scales a pixel buffer with alpha-correct bilinear sampling.</summary>
    /// <param name="source">Source pixels, row major.</param>
    /// <param name="sourceWidth">Source width in pixels.</param>
    /// <param name="sourceHeight">Source height in pixels.</param>
    /// <param name="width">Destination width.</param>
    /// <param name="height">Destination height.</param>
    /// <returns>A new buffer of <paramref name="width"/> × <paramref name="height"/>.</returns>
    public static uint[] Scale(
        uint[] source,
        int sourceWidth,
        int sourceHeight,
        int width,
        int height)
    {
        uint[] destination = new uint[width * height];
        float scaleX = sourceWidth / (float)width;
        float scaleY = sourceHeight / (float)height;

        for (int y = 0; y < height; y++)
        {
            float sampleY = ((y + 0.5f) * scaleY) - 0.5f;
            for (int x = 0; x < width; x++)
            {
                float sampleX = ((x + 0.5f) * scaleX) - 0.5f;
                destination[(y * width) + x] = SampleBilinear(
                    source,
                    sourceWidth,
                    sourceHeight,
                    sampleX,
                    sampleY);
            }
        }

        return destination;
    }

    /// <summary>Samples four premultiplied neighbours and unpacks the result.</summary>
    private static uint SampleBilinear(uint[] source, int width, int height, float x, float y)
    {
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;

        int x1 = Math.Clamp(x0 + 1, 0, width - 1);
        int y1 = Math.Clamp(y0 + 1, 0, height - 1);
        x0 = Math.Clamp(x0, 0, width - 1);
        y0 = Math.Clamp(y0, 0, height - 1);

        uint p00 = source[(y0 * width) + x0];
        uint p10 = source[(y0 * width) + x1];
        uint p01 = source[(y1 * width) + x0];
        uint p11 = source[(y1 * width) + x1];

        float w00 = (1f - fx) * (1f - fy);
        float w10 = fx * (1f - fy);
        float w01 = (1f - fx) * fy;
        float w11 = fx * fy;

        float alpha = (Rgba.A(p00) * w00) + (Rgba.A(p10) * w10)
            + (Rgba.A(p01) * w01) + (Rgba.A(p11) * w11);
        if (alpha <= 0.5f)
        {
            return 0;
        }

        float red = (Rgba.R(p00) * Rgba.A(p00) * w00) + (Rgba.R(p10) * Rgba.A(p10) * w10)
            + (Rgba.R(p01) * Rgba.A(p01) * w01) + (Rgba.R(p11) * Rgba.A(p11) * w11);
        float green = (Rgba.G(p00) * Rgba.A(p00) * w00) + (Rgba.G(p10) * Rgba.A(p10) * w10)
            + (Rgba.G(p01) * Rgba.A(p01) * w01) + (Rgba.G(p11) * Rgba.A(p11) * w11);
        float blue = (Rgba.B(p00) * Rgba.A(p00) * w00) + (Rgba.B(p10) * Rgba.A(p10) * w10)
            + (Rgba.B(p01) * Rgba.A(p01) * w01) + (Rgba.B(p11) * Rgba.A(p11) * w11);

        int a = Math.Clamp((int)MathF.Round(alpha), 0, 255);
        int r = Math.Clamp((int)MathF.Round(red / alpha), 0, 255);
        int g = Math.Clamp((int)MathF.Round(green / alpha), 0, 255);
        int b = Math.Clamp((int)MathF.Round(blue / alpha), 0, 255);
        return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }
}
