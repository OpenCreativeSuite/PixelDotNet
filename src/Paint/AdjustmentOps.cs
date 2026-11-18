using System;
using System.Collections.Generic;

namespace PixelDotNet.Paint;

/// <summary>Applies non-destructive adjustment layers to a pixel buffer.</summary>
public static class AdjustmentOps
{
    /// <summary>
    /// Applies an adjustment layer's effect to the composite below it. Opacity,
    /// masks and clipping all scale the effect per pixel.
    /// </summary>
    public static void Apply(uint[] composite, PaintLayer layer)
    {
        byte[] lut = BuildLut(layer);
        int opacity = (int)MathF.Round(Math.Clamp(layer.Opacity, 0f, 1f) * 255f);
        byte[]? mask = layer.Mask;

        for (int index = 0; index < composite.Length; index++)
        {
            uint color = composite[index];
            if (Rgba.A(color) == 0)
            {
                continue;
            }

            int strength = opacity;
            if (mask is not null)
            {
                strength = strength * mask[index] / 255;
            }

            if (layer.ClipToBelow)
            {
                strength = strength * Rgba.A(color) / 255;
            }

            if (strength <= 0)
            {
                continue;
            }

            composite[index] = Mix(color, lut, strength);
        }
    }

    /// <summary>Applies an adjustment layer directly to another layer's pixels.</summary>
    public static void ApplyToPixels(uint[] pixels, PaintLayer layer)
    {
        byte[] lut = BuildLut(layer);
        int opacity = (int)MathF.Round(Math.Clamp(layer.Opacity, 0f, 1f) * 255f);
        byte[]? mask = layer.Mask;

        for (int index = 0; index < pixels.Length; index++)
        {
            uint color = pixels[index];
            if (Rgba.A(color) == 0)
            {
                continue;
            }

            int strength = opacity;
            if (mask is not null)
            {
                strength = strength * mask[index] / 255;
            }

            if (strength <= 0)
            {
                continue;
            }

            pixels[index] = Mix(color, lut, strength);
        }
    }

    /// <summary>Mixes an adjusted colour back into the original by strength.</summary>
    private static uint Mix(uint color, byte[] lut, int strength)
    {
        int alpha = Rgba.A(color);
        int red = Rgba.R(color);
        int green = Rgba.G(color);
        int blue = Rgba.B(color);

        if (strength >= 255)
        {
            return Rgba.Pack(alpha, lut[red], lut[green], lut[blue]);
        }

        int r = red + ((lut[red] - red) * strength / 255);
        int g = green + ((lut[green] - green) * strength / 255);
        int b = blue + ((lut[blue] - blue) * strength / 255);
        return Rgba.Pack(alpha, r, g, b);
    }

    /// <summary>Builds a 256-entry tone map for the layer's adjustment.</summary>
    private static byte[] BuildLut(PaintLayer layer)
    {
        if (layer.Adjustment == AdjustmentKind.Curves)
        {
            return BuildCurveLut(layer.AdjustmentCurve);
        }

        float black = Math.Clamp(layer.AdjustmentBlack, 0f, 254f);
        float white = Math.Clamp(layer.AdjustmentWhite, black + 1f, 255f);
        float exponent = 1f / Math.Clamp(layer.AdjustmentGamma, 0.05f, 10f);
        byte[] lut = new byte[256];
        float span = white - black;

        for (int value = 0; value < 256; value++)
        {
            float t = Math.Clamp((value - black) / span, 0f, 1f);
            lut[value] = (byte)MathF.Round(MathF.Pow(t, exponent) * 255f);
        }

        return lut;
    }

    /// <summary>Builds a LUT for a piecewise linear tone curve.</summary>
    private static byte[] BuildCurveLut(IReadOnlyList<(float X, float Y)> points)
    {
        byte[] lut = new byte[256];
        if (points.Count < 2)
        {
            for (int i = 0; i < 256; i++)
            {
                lut[i] = (byte)i;
            }

            return lut;
        }

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

        return lut;
    }
}
