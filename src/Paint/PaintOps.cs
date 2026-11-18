using System;
using System.Collections.Generic;

namespace PixelDotNet.Paint;

/// <summary>Pixel operations that do not depend on the editor session.</summary>
public static class PaintOps
{
    /// <summary>
    /// Flood-fills the connected region around a seed pixel, matching colour within
    /// a per-channel tolerance and stopping at the selection edge.
    /// </summary>
    public static void FloodFill(PaintLayer layer, int seedX, int seedY, uint color, int tolerance, int alpha, Selection? selection)
    {
        if (!layer.Contains(seedX, seedY) || alpha <= 0 || !Inside(selection, seedX, seedY))
        {
            return;
        }

        int width = layer.Width;
        int height = layer.Height;
        uint target = layer.At(seedX, seedY);
        int targetR = Rgba.R(target);
        int targetG = Rgba.G(target);
        int targetB = Rgba.B(target);
        int targetA = Rgba.A(target);
        uint paint = Rgba.Opaque(color);

        bool[] seen = new bool[width * height];
        Stack<int> pending = new();
        pending.Push((seedY * width) + seedX);

        while (pending.Count > 0)
        {
            int index = pending.Pop();
            if (seen[index])
            {
                continue;
            }

            seen[index] = true;
            int x = index % width;
            int y = index / width;

            if (!Inside(selection, x, y))
            {
                continue;
            }

            uint existing = layer.Pixels[index];
            if (!Matches(existing, targetR, targetG, targetB, targetA, tolerance))
            {
                continue;
            }

            layer.Pixels[index] = Rgba.Over(existing, paint, alpha);

            if (x > 0 && !seen[index - 1])
            {
                pending.Push(index - 1);
            }

            if (x < width - 1 && !seen[index + 1])
            {
                pending.Push(index + 1);
            }

            if (y > 0 && !seen[index - width])
            {
                pending.Push(index - width);
            }

            if (y < height - 1 && !seen[index + width])
            {
                pending.Push(index + width);
            }
        }
    }

    /// <summary>Paints one pixel with source-over blending inside the selection.</summary>
    public static void Blend(PaintLayer layer, int x, int y, uint color, int alpha, Selection? selection = null)
    {
        if (!layer.Contains(x, y) || alpha <= 0 || !Inside(selection, x, y))
        {
            return;
        }

        int index = (y * layer.Width) + x;
        layer.Pixels[index] = Rgba.Over(layer.Pixels[index], color, alpha);
    }

    /// <summary>Paints a filled disc with a one-pixel antialiased edge.</summary>
    public static void Disc(PaintLayer layer, float centerX, float centerY, float radius, uint color, int alpha = 255, Selection? selection = null)
    {
        float size = MathF.Max(0.5f, radius);
        int minX = Math.Max(0, (int)MathF.Floor(centerX - size - 1f));
        int maxX = Math.Min(layer.Width - 1, (int)MathF.Ceiling(centerX + size + 1f));
        int minY = Math.Max(0, (int)MathF.Floor(centerY - size - 1f));
        int maxY = Math.Min(layer.Height - 1, (int)MathF.Ceiling(centerY + size + 1f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - centerX;
                float dy = y + 0.5f - centerY;
                float coverage = size + 0.5f - MathF.Sqrt((dx * dx) + (dy * dy));
                if (coverage <= 0f)
                {
                    continue;
                }

                Blend(layer, x, y, color, (int)(Math.Clamp(coverage, 0f, 1f) * alpha), selection);
            }
        }
    }

    /// <summary>
    /// Draws a rectangle or ellipse between two dragged corners, either filled or
    /// outlined. Every pixel is recomputed from <paramref name="source"/> so the
    /// tools can preview a shape while the pointer moves.
    /// </summary>
    public static void Shape(
        PaintLayer layer,
        uint[] source,
        float ax,
        float ay,
        float bx,
        float by,
        bool ellipse,
        bool filled,
        float strokeWidth,
        uint color,
        int alpha,
        Selection? selection)
    {
        float left = MathF.Min(ax, bx);
        float top = MathF.Min(ay, by);
        float right = MathF.Max(ax, bx);
        float bottom = MathF.Max(ay, by);
        float halfWidth = (right - left) * 0.5f;
        float halfHeight = (bottom - top) * 0.5f;

        int minX = Math.Max(0, (int)MathF.Floor(left));
        int maxX = Math.Min(layer.Width - 1, (int)MathF.Ceiling(right));
        int minY = Math.Max(0, (int)MathF.Floor(top));
        int maxY = Math.Min(layer.Height - 1, (int)MathF.Ceiling(bottom));
        if (halfWidth <= 0f || halfHeight <= 0f || minX > maxX || minY > maxY)
        {
            return;
        }

        float centerX = (left + right) * 0.5f;
        float centerY = (top + bottom) * 0.5f;
        float edge = MathF.Max(0.5f, strokeWidth * 0.5f);
        uint paint = Rgba.Opaque(color);
        int width = layer.Width;
        float scale = MathF.Min(halfWidth, halfHeight);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int index = (y * width) + x;

                // Signed distance in pixels: negative inside the shape.
                float distance;
                if (ellipse)
                {
                    float nx = (x + 0.5f - centerX) / halfWidth;
                    float ny = (y + 0.5f - centerY) / halfHeight;
                    distance = (MathF.Sqrt((nx * nx) + (ny * ny)) - 1f) * scale;
                }
                else
                {
                    float dx = MathF.Abs(x + 0.5f - centerX) - halfWidth;
                    float dy = MathF.Abs(y + 0.5f - centerY) - halfHeight;
                    float outside = MathF.Sqrt((MathF.Max(dx, 0f) * MathF.Max(dx, 0f)) + (MathF.Max(dy, 0f) * MathF.Max(dy, 0f)));
                    distance = outside + MathF.Min(MathF.Max(dx, dy), 0f);
                }

                float coverage = filled
                    ? 0.5f - distance
                    : edge - MathF.Abs(distance) + 0.5f;

                if (coverage <= 0f || !Inside(selection, x, y))
                {
                    layer.Pixels[index] = source[index];
                    continue;
                }

                layer.Pixels[index] = Rgba.Over(source[index], paint, (int)(Math.Clamp(coverage, 0f, 1f) * alpha));
            }
        }
    }

    /// <summary>
    /// Paints a linear gradient between two dragged points, recomputed from
    /// <paramref name="source"/> so it can preview during the drag.
    /// </summary>
    public static void LinearGradient(
        PaintLayer layer,
        uint[] source,
        float ax,
        float ay,
        float bx,
        float by,
        uint from,
        uint to,
        int alpha,
        Selection? selection)
    {
        float dx = bx - ax;
        float dy = by - ay;
        float lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared < 1f || alpha <= 0)
        {
            return;
        }

        int width = layer.Width;
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;
                if (!Inside(selection, x, y))
                {
                    layer.Pixels[index] = source[index];
                    continue;
                }

                float t = (((x + 0.5f - ax) * dx) + ((y + 0.5f - ay) * dy)) / lengthSquared;
                uint color = Rgba.Lerp(from, to, t);
                layer.Pixels[index] = Rgba.Over(source[index], color, alpha);
            }
        }
    }

    /// <summary>Copies a layer's pixels shifted by a document-space offset.</summary>
    public static void Translate(PaintLayer layer, uint[] source, int dx, int dy)
    {
        int width = layer.Width;
        int height = layer.Height;
        Array.Clear(layer.Pixels);

        for (int y = 0; y < height; y++)
        {
            int targetY = y + dy;
            if (targetY < 0 || targetY >= height)
            {
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                int targetX = x + dx;
                if (targetX < 0 || targetX >= width)
                {
                    continue;
                }

                layer.Pixels[(targetY * width) + targetX] = source[(y * width) + x];
            }
        }
    }

    /// <summary>
    /// Rebuilds the layer from a snapshot with tone, hue and saturation applied
    /// inside the selection. Used by the live sliders in the adjustment panel.
    /// </summary>
    public static void Retone(
        PaintLayer layer,
        uint[] source,
        Selection? selection,
        float brightness,
        float contrast,
        float hueShift,
        float saturation,
        float lightness)
    {
        int width = layer.Width;
        float contrastPoint = 128f + (brightness * 100f);
        float contrastFactor = (259f * ((contrast * 255f) + 255f)) / (255f * (259f - (contrast * 255f)));

        for (int index = 0; index < source.Length; index++)
        {
            int x = index % width;
            int y = index / width;
            int alpha = Rgba.A(source[index]);

            if (alpha == 0 || !Inside(selection, x, y))
            {
                layer.Pixels[index] = source[index];
                continue;
            }

            (float hue, float saturationValue, float value) = Rgba.ToHsv(source[index]);
            int r = HsvChannel(hue, saturationValue, value, 0);
            int g = HsvChannel(hue, saturationValue, value, 1);
            int b = HsvChannel(hue, saturationValue, value, 2);

            layer.Pixels[index] = Rgba.Pack(alpha, Tone(r), Tone(g), Tone(b));
        }

        int HsvChannel(float hue, float saturationValue, float value, int channel)
        {
            uint hsv = Rgba.FromHsv(hue + hueShift, saturationValue * saturation, Math.Clamp(value + lightness, 0f, 1f));
            return channel switch
            {
                0 => Rgba.R(hsv),
                1 => Rgba.G(hsv),
                _ => Rgba.B(hsv),
            };
        }

        int Tone(int channel) =>
            (int)Math.Clamp((((channel - 128f) * contrastFactor) + contrastPoint), 0f, 255f);
    }

    /// <summary>Inverts the active colours inside the selection.</summary>
    public static void Invert(PaintLayer layer, Selection? selection)
    {
        int width = layer.Width;
        for (int index = 0; index < layer.Pixels.Length; index++)
        {
            int x = index % width;
            int y = index / width;
            if (!Inside(selection, x, y))
            {
                continue;
            }

            uint color = layer.Pixels[index];
            if (Rgba.A(color) == 0)
            {
                continue;
            }

            layer.Pixels[index] = Rgba.Pack(Rgba.A(color), 255 - Rgba.R(color), 255 - Rgba.G(color), 255 - Rgba.B(color));
        }
    }

    /// <summary>Removes saturation inside the selection.</summary>
    public static void Desaturate(PaintLayer layer, Selection? selection)
    {
        int width = layer.Width;
        for (int index = 0; index < layer.Pixels.Length; index++)
        {
            int x = index % width;
            int y = index / width;
            if (!Inside(selection, x, y))
            {
                continue;
            }

            uint color = layer.Pixels[index];
            if (Rgba.A(color) == 0)
            {
                continue;
            }

            int luminance = ((Rgba.R(color) * 77) + (Rgba.G(color) * 150) + (Rgba.B(color) * 29)) >> 8;
            layer.Pixels[index] = Rgba.Pack(Rgba.A(color), luminance, luminance, luminance);
        }
    }

    /// <summary>
    /// Replaces everything below a wavy horizon with one colour. Used to build the
    /// sample artwork's mountain ridges.
    /// </summary>
    public static void Ridge(PaintLayer layer, float baseY, float amplitude, float phase, float frequency, uint color)
    {
        for (int x = 0; x < layer.Width; x++)
        {
            float t = (float)x / layer.Width;
            float wave = MathF.Sin(((t * frequency) + phase) * MathF.PI * 2f);
            float secondary = MathF.Sin(((t * frequency * 2.7f) + phase) * MathF.PI * 2f) * 0.28f;
            int top = (int)MathF.Round(baseY + ((wave + secondary) * amplitude));

            for (int y = Math.Max(0, top); y < layer.Height; y++)
            {
                layer.Put(x, y, Rgba.Opaque(color));
            }
        }
    }

    /// <summary>Fills a vertical band with a linear gradient.</summary>
    public static void VerticalGradient(PaintLayer layer, int top, int bottom, uint from, uint to)
    {
        int span = Math.Max(1, bottom - top - 1);
        for (int y = Math.Max(0, top); y < Math.Min(layer.Height, bottom); y++)
        {
            uint color = Rgba.Lerp(from, to, (float)(y - top) / span);
            for (int x = 0; x < layer.Width; x++)
            {
                layer.Put(x, y, color);
            }
        }
    }

    /// <summary>Fills a rectangle.</summary>
    public static void Rect(PaintLayer layer, int x, int y, int width, int height, uint color)
    {
        int right = Math.Min(layer.Width, x + width);
        int bottom = Math.Min(layer.Height, y + height);
        for (int row = Math.Max(0, y); row < bottom; row++)
        {
            for (int column = Math.Max(0, x); column < right; column++)
            {
                layer.Put(column, row, Rgba.Opaque(color));
            }
        }
    }

    /// <summary>Returns whether a pixel is inside the selection, or there is no selection.</summary>
    private static bool Inside(Selection? selection, int x, int y) => selection is null || selection.Contains(x, y);

    /// <summary>Compares a pixel with a target colour per channel.</summary>
    private static bool Matches(uint color, int r, int g, int b, int a, int tolerance) =>
        Math.Abs(Rgba.R(color) - r) <= tolerance
        && Math.Abs(Rgba.G(color) - g) <= tolerance
        && Math.Abs(Rgba.B(color) - b) <= tolerance
        && Math.Abs(Rgba.A(color) - a) <= tolerance;

    /// <summary>Applies black point, white point and midtone gamma through a LUT.</summary>
    public static void Levels(PaintLayer layer, Selection? selection, float black, float white, float gamma)
    {
        float low = Math.Clamp(black, 0f, 254f);
        float high = Math.Clamp(white, low + 1f, 255f);
        float exponent = 1f / Math.Clamp(gamma, 0.05f, 10f);
        byte[] lut = BuildLevelsLut(low, high, exponent);

        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                uint color = layer.Pixels[(y * layer.Width) + x];
                layer.Pixels[(y * layer.Width) + x] = Rgba.Pack(
                    Rgba.A(color),
                    lut[Rgba.R(color)],
                    lut[Rgba.G(color)],
                    lut[Rgba.B(color)]);
            }
        }
    }

    /// <summary>Builds a 256-entry lookup table for the Levels controls.</summary>
    private static byte[] BuildLevelsLut(float black, float white, float exponent)
    {
        byte[] lut = new byte[256];
        float span = white - black;

        for (int value = 0; value < 256; value++)
        {
            float t = Math.Clamp((value - black) / span, 0f, 1f);
            lut[value] = (byte)MathF.Round(MathF.Pow(t, exponent) * 255f);
        }

        return lut;
    }

    /// <summary>Applies a piecewise linear tone curve through the supplied points.</summary>
    public static void Curves(PaintLayer layer, Selection? selection, IReadOnlyList<(float X, float Y)> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        byte[] lut = new byte[256];
        for (int value = 0; value < 256; value++)
        {
            float x = value / 255f;
            int segment = 0;

            while (segment < points.Count - 2 && points[segment + 1].X < x)
            {
                segment++;
            }

            (float x0, float y0) = points[segment];
            (float x1, float y1) = points[segment + 1];
            float span = MathF.Max(0.0001f, x1 - x0);
            float t = Math.Clamp((x - x0) / span, 0f, 1f);
            lut[value] = (byte)MathF.Round(Math.Clamp(y0 + ((y1 - y0) * t), 0f, 1f) * 255f);
        }

        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                uint color = layer.Pixels[(y * layer.Width) + x];
                layer.Pixels[(y * layer.Width) + x] = Rgba.Pack(
                    Rgba.A(color),
                    lut[Rgba.R(color)],
                    lut[Rgba.G(color)],
                    lut[Rgba.B(color)]);
            }
        }
    }
}
