using System;

namespace PixelDotNet.Paint;

/// <summary>One-shot colour operations applied to a layer inside a selection.</summary>
public static class ColourTools
{
    /// <summary>Converts pixels to pure black or white around a luminance level.</summary>
    public static void Threshold(PaintLayer layer, Selection? selection, int level)
    {
        int limit = Math.Clamp(level, 0, 255);
        uint[] pixels = layer.Pixels;

        for (int i = 0; i < pixels.Length; i++)
        {
            if (!Included(layer, selection, i))
            {
                continue;
            }

            uint pixel = pixels[i];
            int alpha = Rgba.A(pixel);
            if (alpha == 0)
            {
                continue;
            }

            int luminance = Luminance(pixel);
            uint value = luminance >= limit ? 0xFFFFFFFFu : 0xFF000000u;
            pixels[i] = (value & 0x00FFFFFFu) | ((uint)alpha << 24);
        }
    }

    /// <summary>Maps luminance through a two-colour gradient.</summary>
    public static void GradientMap(PaintLayer layer, Selection? selection, uint low, uint high)
    {
        uint[] lookup = new uint[256];
        for (int i = 0; i < lookup.Length; i++)
        {
            float t = i / 255f;
            lookup[i] = Rgba.Pack(
                255,
                (int)MathF.Round(Rgba.R(low) + ((Rgba.R(high) - Rgba.R(low)) * t)),
                (int)MathF.Round(Rgba.G(low) + ((Rgba.G(high) - Rgba.G(low)) * t)),
                (int)MathF.Round(Rgba.B(low) + ((Rgba.B(high) - Rgba.B(low)) * t)));
        }

        uint[] pixels = layer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!Included(layer, selection, i))
            {
                continue;
            }

            uint pixel = pixels[i];
            int alpha = Rgba.A(pixel);
            if (alpha == 0)
            {
                continue;
            }

            uint mapped = lookup[Luminance(pixel)];
            pixels[i] = (mapped & 0x00FFFFFFu) | ((uint)alpha << 24);
        }
    }

    /// <summary>Replaces pixels close to one colour with another.</summary>
    public static void ReplaceColour(
        PaintLayer layer,
        Selection? selection,
        uint from,
        uint to,
        int tolerance)
    {
        int limit = Math.Clamp(tolerance, 0, 255);
        uint[] pixels = layer.Pixels;

        for (int i = 0; i < pixels.Length; i++)
        {
            if (!Included(layer, selection, i))
            {
                continue;
            }

            uint pixel = pixels[i];
            if (Rgba.A(pixel) == 0)
            {
                continue;
            }

            if (Math.Abs(Rgba.R(pixel) - Rgba.R(from)) > limit
                || Math.Abs(Rgba.G(pixel) - Rgba.G(from)) > limit
                || Math.Abs(Rgba.B(pixel) - Rgba.B(from)) > limit)
            {
                continue;
            }

            pixels[i] = (to & 0x00FFFFFFu) | ((uint)Rgba.A(pixel) << 24);
        }
    }

    /// <summary>Shifts channels independently in shadows, midtones and highlights.</summary>
    /// <param name="layer">Layer to edit.</param>
    /// <param name="selection">Optional selection bounds.</param>
    /// <param name="balance">Nine offsets: three per tone range, cyan-red, magenta-green, yellow-blue.</param>
    public static void ColourBalance(PaintLayer layer, Selection? selection, float[] balance)
    {
        if (balance.Length < 9)
        {
            return;
        }

        uint[] pixels = layer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!Included(layer, selection, i))
            {
                continue;
            }

            uint pixel = pixels[i];
            int alpha = Rgba.A(pixel);
            if (alpha == 0)
            {
                continue;
            }

            float luminance = Luminance(pixel) / 255f;
            float shadows = (1f - luminance) * (1f - luminance);
            float highlights = luminance * luminance;
            float midtones = 1f - MathF.Abs((luminance * 2f) - 1f);

            int red = Rgba.R(pixel);
            int green = Rgba.G(pixel);
            int blue = Rgba.B(pixel);

            red = Clamp(red + (int)MathF.Round(
                (balance[0] * shadows) + (balance[3] * midtones) + (balance[6] * highlights)));
            green = Clamp(green + (int)MathF.Round(
                (balance[1] * shadows) + (balance[4] * midtones) + (balance[7] * highlights)));
            blue = Clamp(blue + (int)MathF.Round(
                (balance[2] * shadows) + (balance[5] * midtones) + (balance[8] * highlights)));

            pixels[i] = Rgba.Pack(alpha, red, green, blue);
        }
    }

    /// <summary>Mixes source channels into each output channel.</summary>
    /// <param name="layer">Layer to edit.</param>
    /// <param name="selection">Optional selection bounds.</param>
    /// <param name="mix">
    /// Nine percentages: red from RGB, green from RGB, blue from RGB. 100 keeps the
    /// channel unchanged and 0 removes it.
    /// </param>
    public static void ChannelMix(PaintLayer layer, Selection? selection, float[] mix)
    {
        if (mix.Length < 9)
        {
            return;
        }

        uint[] pixels = layer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!Included(layer, selection, i))
            {
                continue;
            }

            uint pixel = pixels[i];
            int alpha = Rgba.A(pixel);
            if (alpha == 0)
            {
                continue;
            }

            int red = Rgba.R(pixel);
            int green = Rgba.G(pixel);
            int blue = Rgba.B(pixel);

            int outRed = Clamp((int)MathF.Round(
                (red * mix[0] / 100f) + (green * mix[1] / 100f) + (blue * mix[2] / 100f)));
            int outGreen = Clamp((int)MathF.Round(
                (red * mix[3] / 100f) + (green * mix[4] / 100f) + (blue * mix[5] / 100f)));
            int outBlue = Clamp((int)MathF.Round(
                (red * mix[6] / 100f) + (green * mix[7] / 100f) + (blue * mix[8] / 100f)));

            pixels[i] = Rgba.Pack(alpha, outRed, outGreen, outBlue);
        }
    }

    /// <summary>Applies CMYK-style offsets to nine selective-colour ranges.</summary>
    /// <param name="layer">Layer to edit.</param>
    /// <param name="selection">Optional selection bounds.</param>
    /// <param name="offsets">
    /// Thirty-six values: four offsets (cyan, magenta, yellow, black) for each of
    /// red, yellow, green, cyan, blue, magenta, whites, neutrals and blacks.
    /// </param>
    public static void SelectiveColour(PaintLayer layer, Selection? selection, float[] offsets)
    {
        const int Ranges = 9;
        if (offsets.Length < Ranges * 4)
        {
            return;
        }

        uint[] pixels = layer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!Included(layer, selection, i))
            {
                continue;
            }

            uint pixel = pixels[i];
            int alpha = Rgba.A(pixel);
            if (alpha == 0)
            {
                continue;
            }

            int red = Rgba.R(pixel);
            int green = Rgba.G(pixel);
            int blue = Rgba.B(pixel);

            float adjustCyan = 0f;
            float adjustMagenta = 0f;
            float adjustYellow = 0f;
            float adjustBlack = 0f;

            for (int range = 0; range < Ranges; range++)
            {
                float weight = RangeWeight(range, red, green, blue);
                if (weight <= 0f)
                {
                    continue;
                }

                adjustCyan += offsets[(range * 4) + 0] * weight;
                adjustMagenta += offsets[(range * 4) + 1] * weight;
                adjustYellow += offsets[(range * 4) + 2] * weight;
                adjustBlack += offsets[(range * 4) + 3] * weight;
            }

            int outRed = Clamp(red
                - (int)MathF.Round(adjustCyan * 2.55f)
                - (int)MathF.Round(adjustBlack * 2.55f));
            int outGreen = Clamp(green
                - (int)MathF.Round(adjustMagenta * 2.55f)
                - (int)MathF.Round(adjustBlack * 2.55f));
            int outBlue = Clamp(blue
                - (int)MathF.Round(adjustYellow * 2.55f)
                - (int)MathF.Round(adjustBlack * 2.55f));

            pixels[i] = Rgba.Pack(alpha, outRed, outGreen, outBlue);
        }
    }

    /// <summary>Computes how strongly a pixel belongs to a selective-colour range.</summary>
    private static float RangeWeight(int range, int red, int green, int blue)
    {
        float r = red / 255f;
        float g = green / 255f;
        float b = blue / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float saturation = max <= 0f ? 0f : (max - min) / max;
        float luminance = (0.299f * r) + (0.587f * g) + (0.114f * b);

        if (range <= 5)
        {
            float hue = Hue(red, green, blue);
            float target = range * 60f;
            float distance = MathF.Abs(hue - target);
            distance = MathF.Min(distance, 360f - distance);
            return saturation * MathF.Max(0f, 1f - (distance / 60f));
        }

        return range switch
        {
            6 => Math.Clamp((luminance - 0.72f) / 0.28f, 0f, 1f) * (1f - saturation),
            7 => (1f - saturation) * (1f - MathF.Abs((luminance * 2f) - 1f)),
            _ => Math.Clamp((0.28f - luminance) / 0.28f, 0f, 1f),
        };
    }

    /// <summary>Returns the hue of a colour in degrees.</summary>
    private static float Hue(int red, int green, int blue)
    {
        int max = Math.Max(red, Math.Max(green, blue));
        int min = Math.Min(red, Math.Min(green, blue));
        int delta = max - min;
        if (delta == 0)
        {
            return 0f;
        }

        float hue;
        if (max == red)
        {
            hue = 60f * ((green - blue) / (float)delta);
        }
        else if (max == green)
        {
            hue = 60f * (2f + ((blue - red) / (float)delta));
        }
        else
        {
            hue = 60f * (4f + ((red - green) / (float)delta));
        }

        return hue < 0f ? hue + 360f : hue;
    }

    /// <summary>Neutralises red pupils inside the selection.</summary>
    /// <param name="layer">Layer to edit.</param>
    /// <param name="selection">Region to scan.</param>
    /// <param name="strength">Blend amount from 0 to 1.</param>
    public static void RemoveRedEye(PaintLayer layer, Selection? selection, float strength)
    {
        float amount = Math.Clamp(strength, 0f, 1f);
        if (amount <= 0f)
        {
            return;
        }

        uint[] pixels = layer.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!Included(layer, selection, i))
            {
                continue;
            }

            uint pixel = pixels[i];
            int alpha = Rgba.A(pixel);
            if (alpha == 0)
            {
                continue;
            }

            int red = Rgba.R(pixel);
            int green = Rgba.G(pixel);
            int blue = Rgba.B(pixel);
            if (red < 60 || red < green + 24 || red < blue + 24)
            {
                continue;
            }

            int neutral = (green + blue) / 2;
            int target = (int)MathF.Round(red + ((neutral - red) * amount));
            pixels[i] = Rgba.Pack(alpha, target, green, blue);
        }
    }

    /// <summary>Splits the active layer into red, green and blue grayscale layers.</summary>
    /// <param name="document">Document receiving the layers.</param>
    /// <param name="source">Layer to split.</param>
    /// <param name="selection">Optional selection mask.</param>
    public static void SplitChannels(PaintDocument document, PaintLayer source, Selection? selection)
    {
        int width = document.Width;
        int height = document.Height;
        string[] names = ["Red Channel", "Green Channel", "Blue Channel"];
        int insertAt = Math.Clamp(document.ActiveIndex + 1, 0, document.Layers.Count);

        for (int channel = 0; channel < 3; channel++)
        {
            PaintLayer layer = new(names[channel], width, height)
            {
                Group = source.Group,
            };

            for (int i = 0; i < layer.Pixels.Length; i++)
            {
                if (!Included(source, selection, i))
                {
                    continue;
                }

                uint pixel = source.Pixels[i];
                int alpha = Rgba.A(pixel);
                if (alpha == 0)
                {
                    continue;
                }

                int value = channel switch
                {
                    0 => Rgba.R(pixel),
                    1 => Rgba.G(pixel),
                    _ => Rgba.B(pixel),
                };

                layer.Pixels[i] = Rgba.Pack(alpha, value, value, value);
            }

            document.Layers.Insert(insertAt, layer);
            insertAt++;
        }

        document.ActiveIndex = Math.Clamp(insertAt - 1, 0, document.Layers.Count - 1);
    }

    /// <summary>Checks whether a pixel index is inside the layer and selection.</summary>
    private static bool Included(PaintLayer layer, Selection? selection, int index)
    {
        int width = layer.Width;
        int x = index % width;
        int y = index / width;
        return selection is null || selection.Contains(x, y);
    }

    /// <summary>Rec. 601 luminance.</summary>
    private static int Luminance(uint color) =>
        ((Rgba.R(color) * 77) + (Rgba.G(color) * 150) + (Rgba.B(color) * 29)) >> 8;

    /// <summary>Clamps a channel value.</summary>
    private static int Clamp(int value) => Math.Clamp(value, 0, 255);
}
