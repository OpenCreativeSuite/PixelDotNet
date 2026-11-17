using System;

namespace PixelDotNet.Paint;

/// <summary>
/// Pixel filters behind the Effects panel. Every filter reads the snapshot the
/// preview started from and writes only inside the selection, so dragging a slider
/// never compounds and the pixels outside the marquee are untouched.
/// </summary>
/// <remarks>
/// Blur-like filters work on premultiplied colour. Blurring straight alpha would
/// drag the colour of fully transparent pixels into the visible ones, which shows
/// up as dark fringes around cut-outs.
/// </remarks>
public static class EffectOps
{
    /// <summary>Applies one effect to a layer from a snapshot.</summary>
    public static void Apply(PaintLayer layer, uint[] source, Selection? selection, EffectKind effect, float amount)
    {
        Array.Copy(source, layer.Pixels, source.Length);

        switch (effect)
        {
            case EffectKind.GaussianBlur:
                Blur(layer, source, selection, Math.Max(1, (int)MathF.Round(amount)), passes: 3);
                break;

            case EffectKind.MotionBlur:
                MotionBlur(layer, source, selection, Math.Max(1, (int)MathF.Round(amount)));
                break;

            case EffectKind.Fragment:
                Fragment(layer, source, selection, Math.Max(1, (int)MathF.Round(amount)));
                break;

            case EffectKind.Pixelate:
                Pixelate(layer, source, selection, Math.Max(2, (int)MathF.Round(amount)));
                break;

            case EffectKind.Sharpen:
                Sharpen(layer, source, selection, Math.Clamp(amount, 0f, 100f) / 100f);
                break;

            case EffectKind.Emboss:
                Emboss(layer, source, selection, Math.Clamp(amount, 0f, 100f) / 100f);
                break;

            case EffectKind.EdgeDetect:
                EdgeDetect(layer, source, selection, Math.Clamp(amount, 0f, 100f) / 100f);
                break;

            case EffectKind.AddNoise:
                AddNoise(layer, source, selection, Math.Clamp(amount, 0f, 100f) / 100f);
                break;

            case EffectKind.Posterize:
                Posterize(layer, source, selection, Math.Clamp((int)MathF.Round(amount), 2, 32));
                break;

            case EffectKind.Vignette:
                Vignette(layer, source, selection, Math.Clamp(amount, 0f, 100f) / 100f);
                break;

            default:
                break;
        }
    }

    /// <summary>Gaussian approximation: three box-blur passes on premultiplied pixels.</summary>
    private static void Blur(PaintLayer layer, uint[] source, Selection? selection, int radius, int passes)
    {
        int width = layer.Width;
        int height = layer.Height;
        int count = width * height;
        radius = Math.Clamp(radius, 1, Math.Max(1, Math.Min(width, height) / 2));

        int[] alpha = new int[count];
        int[] red = new int[count];
        int[] green = new int[count];
        int[] blue = new int[count];
        int[] scratch = new int[count];

        Premultiply(source, alpha, red, green, blue);

        for (int pass = 0; pass < passes; pass++)
        {
            BoxBlur(alpha, scratch, width, height, radius);
            BoxBlur(red, scratch, width, height, radius);
            BoxBlur(green, scratch, width, height, radius);
            BoxBlur(blue, scratch, width, height, radius);
        }

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (Inside(selection, x, y))
                {
                    int index = row + x;
                    layer.Pixels[index] = Unpremultiply(alpha[index], red[index], green[index], blue[index]);
                }
            }
        }
    }

    /// <summary>Horizontal smear built from the same premultiplied buffers.</summary>
    private static void MotionBlur(PaintLayer layer, uint[] source, Selection? selection, int distance)
    {
        int width = layer.Width;
        int height = layer.Height;
        int count = width * height;
        distance = Math.Clamp(distance, 1, Math.Max(1, width / 2));

        int[] alpha = new int[count];
        int[] red = new int[count];
        int[] green = new int[count];
        int[] blue = new int[count];
        int[] scratch = new int[count];

        Premultiply(source, alpha, red, green, blue);
        BoxBlurHorizontal(alpha, scratch, width, height, distance);
        BoxBlurHorizontal(red, scratch, width, height, distance);
        BoxBlurHorizontal(green, scratch, width, height, distance);
        BoxBlurHorizontal(blue, scratch, width, height, distance);

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (Inside(selection, x, y))
                {
                    int index = row + x;
                    layer.Pixels[index] = Unpremultiply(alpha[index], red[index], green[index], blue[index]);
                }
            }
        }
    }

    /// <summary>Averages four copies of the image offset in the cardinal directions.</summary>
    private static void Fragment(PaintLayer layer, uint[] source, Selection? selection, int offset)
    {
        int width = layer.Width;
        int height = layer.Height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                Accumulate(source, width, height, x - offset, y, out int alpha, out int red, out int green, out int blue);
                Accumulate(source, width, height, x + offset, y, out int a2, out int r2, out int g2, out int b2);
                Accumulate(source, width, height, x, y - offset, out int a3, out int r3, out int g3, out int b3);
                Accumulate(source, width, height, x, y + offset, out int a4, out int r4, out int g4, out int b4);

                layer.Pixels[(y * width) + x] = Unpremultiply(
                    (alpha + a2 + a3 + a4) / 4,
                    (red + r2 + r3 + r4) / 4,
                    (green + g2 + g3 + g4) / 4,
                    (blue + b2 + b3 + b4) / 4);
            }
        }
    }

    /// <summary>Fills square cells with their average colour.</summary>
    private static void Pixelate(PaintLayer layer, uint[] source, Selection? selection, int cell)
    {
        int width = layer.Width;
        int height = layer.Height;

        for (int cellY = 0; cellY < height; cellY += cell)
        {
            int bottom = Math.Min(cellY + cell, height);

            for (int cellX = 0; cellX < width; cellX += cell)
            {
                int right = Math.Min(cellX + cell, width);
                long total = 0;
                long alpha = 0;
                long red = 0;
                long green = 0;
                long blue = 0;

                for (int y = cellY; y < bottom; y++)
                {
                    int row = y * width;
                    for (int x = cellX; x < right; x++)
                    {
                        uint color = source[row + x];
                        int sa = Rgba.A(color);
                        alpha += sa;
                        red += (long)Rgba.R(color) * sa;
                        green += (long)Rgba.G(color) * sa;
                        blue += (long)Rgba.B(color) * sa;
                        total++;
                    }
                }

                if (total == 0)
                {
                    continue;
                }

                int averageAlpha = (int)(alpha / total);
                uint average = averageAlpha <= 0
                    ? Rgba.Transparent
                    : Rgba.Pack(
                        averageAlpha,
                        Math.Clamp((int)(red / alpha), 0, 255),
                        Math.Clamp((int)(green / alpha), 0, 255),
                        Math.Clamp((int)(blue / alpha), 0, 255));

                for (int y = cellY; y < bottom; y++)
                {
                    int row = y * width;
                    for (int x = cellX; x < right; x++)
                    {
                        if (Inside(selection, x, y))
                        {
                            layer.Pixels[row + x] = average;
                        }
                    }
                }
            }
        }
    }

    /// <summary>Unsharp mask: pushes every channel away from its four neighbours.</summary>
    private static void Sharpen(PaintLayer layer, uint[] source, Selection? selection, float strength)
    {
        int width = layer.Width;
        int height = layer.Height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                uint color = At(source, width, height, x, y);
                int alpha = Rgba.A(color);
                if (alpha == 0)
                {
                    continue;
                }

                uint up = At(source, width, height, x, y - 1);
                uint down = At(source, width, height, x, y + 1);
                uint left = At(source, width, height, x - 1, y);
                uint right = At(source, width, height, x + 1, y);

                layer.Pixels[(y * width) + x] = Rgba.Pack(
                    alpha,
                    Channel(Rgba.R(color), (Rgba.R(up) + Rgba.R(down) + Rgba.R(left) + Rgba.R(right)) / 4f),
                    Channel(Rgba.G(color), (Rgba.G(up) + Rgba.G(down) + Rgba.G(left) + Rgba.G(right)) / 4f),
                    Channel(Rgba.B(color), (Rgba.B(up) + Rgba.B(down) + Rgba.B(left) + Rgba.B(right)) / 4f));
            }
        }

        int Channel(int value, float neighbours) =>
            Math.Clamp((int)MathF.Round(value + ((value - neighbours) * strength)), 0, 255);
    }

    /// <summary>Directional relief from a luminance Difference kernel.</summary>
    private static void Emboss(PaintLayer layer, uint[] source, Selection? selection, float mix)
    {
        int width = layer.Width;
        int height = layer.Height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                uint color = At(source, width, height, x, y);
                int alpha = Rgba.A(color);
                if (alpha == 0)
                {
                    continue;
                }

                int value =
                    -Luminance(At(source, width, height, x - 1, y - 1))
                    - Luminance(At(source, width, height, x, y - 1))
                    - Luminance(At(source, width, height, x - 1, y))
                    + Luminance(At(source, width, height, x, y))
                    + Luminance(At(source, width, height, x + 1, y))
                    + Luminance(At(source, width, height, x, y + 1))
                    + Luminance(At(source, width, height, x + 1, y + 1))
                    + 128;

                int grey = Math.Clamp(value, 0, 255);
                layer.Pixels[(y * width) + x] = Rgba.Pack(
                    alpha,
                    Blend(Rgba.R(color), grey, mix),
                    Blend(Rgba.G(color), grey, mix),
                    Blend(Rgba.B(color), grey, mix));
            }
        }
    }

    /// <summary>Sobel edges drawn white on black.</summary>
    private static void EdgeDetect(PaintLayer layer, uint[] source, Selection? selection, float mix)
    {
        int width = layer.Width;
        int height = layer.Height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                uint color = At(source, width, height, x, y);
                int alpha = Rgba.A(color);
                if (alpha == 0)
                {
                    continue;
                }

                int topLeft = Luminance(At(source, width, height, x - 1, y - 1));
                int top = Luminance(At(source, width, height, x, y - 1));
                int topRight = Luminance(At(source, width, height, x + 1, y - 1));
                int left = Luminance(At(source, width, height, x - 1, y));
                int right = Luminance(At(source, width, height, x + 1, y));
                int bottomLeft = Luminance(At(source, width, height, x - 1, y + 1));
                int bottom = Luminance(At(source, width, height, x, y + 1));
                int bottomRight = Luminance(At(source, width, height, x + 1, y + 1));

                int gx = (topRight + (right * 2) + bottomRight) - (topLeft + (left * 2) + bottomLeft);
                int gy = (bottomLeft + (bottom * 2) + bottomRight) - (topLeft + (top * 2) + topRight);
                int magnitude = Math.Min(255, (int)(MathF.Sqrt((gx * gx) + (gy * gy)) / 2f));

                layer.Pixels[(y * width) + x] = Rgba.Pack(
                    alpha,
                    Blend(Rgba.R(color), magnitude, mix),
                    Blend(Rgba.G(color), magnitude, mix),
                    Blend(Rgba.B(color), magnitude, mix));
            }
        }
    }

    /// <summary>Stable per-pixel grain, so the live preview does not shimmer.</summary>
    private static void AddNoise(PaintLayer layer, uint[] source, Selection? selection, float amount)
    {
        int width = layer.Width;
        int height = layer.Height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                uint color = At(source, width, height, x, y);
                int alpha = Rgba.A(color);
                if (alpha == 0)
                {
                    continue;
                }

                int grain = (int)(Hash(x, y) % 255) - 127;
                int delta = (int)MathF.Round(grain * amount);

                layer.Pixels[(y * width) + x] = Rgba.Pack(
                    alpha,
                    Math.Clamp(Rgba.R(color) + delta, 0, 255),
                    Math.Clamp(Rgba.G(color) + delta, 0, 255),
                    Math.Clamp(Rgba.B(color) + delta, 0, 255));
            }
        }
    }

    /// <summary>Snaps each channel to a small number of evenly spaced steps.</summary>
    private static void Posterize(PaintLayer layer, uint[] source, Selection? selection, int levels)
    {
        int width = layer.Width;
        int height = layer.Height;
        int steps = levels - 1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                uint color = At(source, width, height, x, y);
                layer.Pixels[(y * width) + x] = Rgba.Pack(
                    Rgba.A(color),
                    Quantize(Rgba.R(color), steps),
                    Quantize(Rgba.G(color), steps),
                    Quantize(Rgba.B(color), steps));
            }
        }

        static int Quantize(int value, int steps) =>
            (int)MathF.Round(MathF.Round(value * steps / 255f) * 255f / steps);
    }

    /// <summary>Darkens pixels towards the corners.</summary>
    private static void Vignette(PaintLayer layer, uint[] source, Selection? selection, float strength)
    {
        int width = layer.Width;
        int height = layer.Height;
        float centerX = (width - 1) * 0.5f;
        float centerY = (height - 1) * 0.5f;
        float maxDistance = (centerX * centerX) + (centerY * centerY);
        if (maxDistance <= 0f)
        {
            return;
        }

        for (int y = 0; y < height; y++)
        {
            float dy = y - centerY;

            for (int x = 0; x < width; x++)
            {
                if (!Inside(selection, x, y))
                {
                    continue;
                }

                float dx = x - centerX;
                float falloff = 1f - (strength * (((dx * dx) + (dy * dy)) / maxDistance));
                if (falloff < 0f)
                {
                    falloff = 0f;
                }

                uint color = source[(y * width) + x];
                layer.Pixels[(y * width) + x] = Rgba.Pack(
                    Rgba.A(color),
                    (int)(Rgba.R(color) * falloff),
                    (int)(Rgba.G(color) * falloff),
                    (int)(Rgba.B(color) * falloff));
            }
        }
    }

    /// <summary>Blurs every channel of a premultiplied buffer horizontally and vertically.</summary>
    private static void BoxBlur(int[] data, int[] scratch, int width, int height, int radius)
    {
        BoxBlurHorizontal(data, scratch, width, height, radius);
        BoxBlurVertical(scratch, data, width, height, radius);
    }

    /// <summary>One horizontal box pass.</summary>
    private static void BoxBlurHorizontal(int[] data, int[] result, int width, int height, int radius)
    {
        int window = (radius * 2) + 1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            int sum = 0;

            for (int offset = -radius; offset <= radius; offset++)
            {
                sum += data[row + Math.Clamp(offset, 0, width - 1)];
            }

            for (int x = 0; x < width; x++)
            {
                result[row + x] = sum / window;
                sum += data[row + Math.Clamp(x + radius + 1, 0, width - 1)];
                sum -= data[row + Math.Clamp(x - radius, 0, width - 1)];
            }
        }
    }

    /// <summary>One vertical box pass.</summary>
    private static void BoxBlurVertical(int[] data, int[] result, int width, int height, int radius)
    {
        int window = (radius * 2) + 1;

        for (int x = 0; x < width; x++)
        {
            int sum = 0;

            for (int offset = -radius; offset <= radius; offset++)
            {
                sum += data[(Math.Clamp(offset, 0, height - 1) * width) + x];
            }

            for (int y = 0; y < height; y++)
            {
                result[(y * width) + x] = sum / window;
                sum += data[(Math.Clamp(y + radius + 1, 0, height - 1) * width) + x];
                sum -= data[(Math.Clamp(y - radius, 0, height - 1) * width) + x];
            }
        }
    }

    /// <summary>Splits packed pixels into premultiplied channel buffers.</summary>
    private static void Premultiply(uint[] source, int[] alpha, int[] red, int[] green, int[] blue)
    {
        for (int i = 0; i < source.Length; i++)
        {
            uint color = source[i];
            int sa = Rgba.A(color);
            alpha[i] = sa;
            red[i] = Rgba.R(color) * sa;
            green[i] = Rgba.G(color) * sa;
            blue[i] = Rgba.B(color) * sa;
        }
    }

    /// <summary>Adds one premultiplied pixel to running channel sums.</summary>
    private static void Accumulate(
        uint[] source,
        int width,
        int height,
        int x,
        int y,
        out int alpha,
        out int red,
        out int green,
        out int blue)
    {
        uint color = source[(Math.Clamp(y, 0, height - 1) * width) + Math.Clamp(x, 0, width - 1)];
        int sa = Rgba.A(color);
        alpha = sa;
        red = Rgba.R(color) * sa;
        green = Rgba.G(color) * sa;
        blue = Rgba.B(color) * sa;
    }

    /// <summary>Turns premultiplied sums back into a straight-alpha pixel.</summary>
    private static uint Unpremultiply(int alpha, int red, int green, int blue)
    {
        if (alpha <= 0)
        {
            return Rgba.Transparent;
        }

        int a = Math.Min(255, alpha);
        return Rgba.Pack(
            a,
            Math.Clamp(red / a, 0, 255),
            Math.Clamp(green / a, 0, 255),
            Math.Clamp(blue / a, 0, 255));
    }

    /// <summary>Reads a pixel with edge clamping.</summary>
    private static uint At(uint[] source, int width, int height, int x, int y) =>
        source[(Math.Clamp(y, 0, height - 1) * width) + Math.Clamp(x, 0, width - 1)];

    /// <summary>Perceptual luminance of a packed pixel.</summary>
    private static int Luminance(uint color) =>
        ((Rgba.R(color) * 77) + (Rgba.G(color) * 150) + (Rgba.B(color) * 29)) >> 8;

    /// <summary>Mixes from one channel value towards another.</summary>
    private static int Blend(int from, int to, float mix) =>
        Math.Clamp((int)MathF.Round(from + ((to - from) * mix)), 0, 255);

    /// <summary>A deterministic 32-bit hash used for grain.</summary>
    private static uint Hash(int x, int y)
    {
        uint hash = (uint)((x * 73856093) ^ (y * 19349663));
        hash ^= hash >> 13;
        hash *= 1274126177u;
        return hash ^ (hash >> 16);
    }

    /// <summary>Returns whether a pixel may be written.</summary>
    private static bool Inside(Selection? selection, int x, int y) => selection is null || selection.Contains(x, y);
}
