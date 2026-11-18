using System;

namespace PixelDotNet.Paint;

/// <summary>Four-corner perspective transform for a layer or selection.</summary>
public static class PerspectiveOps
{
    /// <summary>
    /// Warps the selected region (or the whole layer) so its corners move by the
    /// supplied offsets.
    /// </summary>
    /// <param name="layer">Layer to edit in place.</param>
    /// <param name="source">Unmodified source pixels.</param>
    /// <param name="selection">Region to warp, or null for the whole layer.</param>
    /// <param name="offsets">
    /// Eight corner offsets in document pixels: top-left X/Y, top-right X/Y,
    /// bottom-right X/Y, bottom-left X/Y.
    /// </param>
    /// <returns><see langword="false"/> when the quad is degenerate.</returns>
    public static bool Transform(
        PaintLayer layer,
        uint[] source,
        Selection? selection,
        float[] offsets)
    {
        int width = layer.Width;
        int height = layer.Height;
        int rectX = Math.Clamp(selection?.X ?? 0, 0, width);
        int rectY = Math.Clamp(selection?.Y ?? 0, 0, height);
        int rectW = Math.Clamp(selection?.Width ?? width, 0, width - rectX);
        int rectH = Math.Clamp(selection?.Height ?? height, 0, height - rectY);

        if (rectW <= 0 || rectH <= 0)
        {
            return false;
        }

        for (int y = rectY; y < rectY + rectH; y++)
        {
            int row = y * width;
            for (int x = rectX; x < rectX + rectW; x++)
            {
                if (selection is null || selection.Contains(x, y))
                {
                    layer.Pixels[row + x] = Rgba.Transparent;
                }
            }
        }

        float left = rectX;
        float top = rectY;
        float right = rectX + rectW;
        float bottom = rectY + rectH;

        float[] quad =
        [
            left + offsets[0], top + offsets[1],
            right + offsets[2], top + offsets[3],
            right + offsets[4], bottom + offsets[5],
            left + offsets[6], bottom + offsets[7],
        ];

        if (!TryInvert(quad, out float[] inverse))
        {
            return false;
        }

        int minX = Math.Clamp(
            (int)MathF.Floor(MathF.Min(MathF.Min(quad[0], quad[2]), MathF.Min(quad[4], quad[6]))),
            0,
            width - 1);
        int maxX = Math.Clamp(
            (int)MathF.Ceiling(MathF.Max(MathF.Max(quad[0], quad[2]), MathF.Max(quad[4], quad[6]))),
            0,
            width - 1);
        int minY = Math.Clamp(
            (int)MathF.Floor(MathF.Min(MathF.Min(quad[1], quad[3]), MathF.Min(quad[5], quad[7]))),
            0,
            height - 1);
        int maxY = Math.Clamp(
            (int)MathF.Ceiling(MathF.Max(MathF.Max(quad[1], quad[3]), MathF.Max(quad[5], quad[7]))),
            0,
            height - 1);

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * width;
            for (int x = minX; x <= maxX; x++)
            {
                float denominator = (inverse[6] * x) + (inverse[7] * y) + inverse[8];
                if (MathF.Abs(denominator) < 1e-6f)
                {
                    continue;
                }

                float u = ((inverse[0] * x) + (inverse[1] * y) + inverse[2]) / denominator;
                float v = ((inverse[3] * x) + (inverse[4] * y) + inverse[5]) / denominator;
                if (u < -0.001f || u > 1.001f || v < -0.001f || v > 1.001f)
                {
                    continue;
                }

                float sampleX = rectX + (u * rectW);
                float sampleY = rectY + (v * rectH);

                if (selection is not null)
                {
                    int sourceX = Math.Clamp((int)MathF.Floor(sampleX), 0, width - 1);
                    int sourceY = Math.Clamp((int)MathF.Floor(sampleY), 0, height - 1);
                    if (!selection.Contains(sourceX, sourceY))
                    {
                        continue;
                    }
                }

                uint color = Sample(source, width, height, sampleX, sampleY);
                if (Rgba.A(color) == 0)
                {
                    continue;
                }

                layer.Pixels[row + x] = color;
            }
        }

        return true;
    }

    /// <summary>Builds the inverse of the unit-square to quad homography.</summary>
    private static bool TryInvert(float[] quad, out float[] inverse)
    {
        float dx1 = quad[2] - quad[4];
        float dx2 = quad[6] - quad[4];
        float dx3 = quad[0] - quad[2] + quad[4] - quad[6];
        float dy1 = quad[3] - quad[5];
        float dy2 = quad[7] - quad[5];
        float dy3 = quad[1] - quad[3] + quad[5] - quad[7];
        float determinant = (dx1 * dy2) - (dx2 * dy1);

        float a;
        float b;
        float c;
        float d;
        float e;
        float f;
        float g;
        float h;

        if (MathF.Abs(determinant) < 1e-7f)
        {
            a = quad[2] - quad[0];
            b = quad[6] - quad[0];
            c = quad[0];
            d = quad[3] - quad[1];
            e = quad[7] - quad[1];
            f = quad[1];
            g = 0f;
            h = 0f;
        }
        else
        {
            g = ((dx3 * dy2) - (dx2 * dy3)) / determinant;
            h = ((dx1 * dy3) - (dx3 * dy1)) / determinant;
            a = quad[2] - quad[0] + (g * quad[2]);
            b = quad[6] - quad[0] + (h * quad[6]);
            c = quad[0];
            d = quad[3] - quad[1] + (g * quad[3]);
            e = quad[7] - quad[1] + (h * quad[7]);
            f = quad[1];
        }

        float det = (a * ((e * 1f) - (f * h)))
            - (b * ((d * 1f) - (f * g)))
            + (c * ((d * h) - (e * g)));

        if (MathF.Abs(det) < 1e-9f)
        {
            inverse = [];
            return false;
        }

        float inv = 1f / det;
        inverse =
        [
            ((e * 1f) - (f * h)) * inv,
            ((c * h) - (b * 1f)) * inv,
            ((b * f) - (c * e)) * inv,
            ((f * g) - (d * 1f)) * inv,
            ((a * 1f) - (c * g)) * inv,
            ((c * d) - (a * f)) * inv,
            ((d * h) - (e * g)) * inv,
            ((b * g) - (a * h)) * inv,
            ((a * e) - (b * d)) * inv,
        ];

        return true;
    }

    /// <summary>Bilinear sample with premultiplied alpha.</summary>
    private static uint Sample(uint[] source, int width, int height, float x, float y)
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
            return Rgba.Transparent;
        }

        float red = (Rgba.R(p00) * Rgba.A(p00) * w00) + (Rgba.R(p10) * Rgba.A(p10) * w10)
            + (Rgba.R(p01) * Rgba.A(p01) * w01) + (Rgba.R(p11) * Rgba.A(p11) * w11);
        float green = (Rgba.G(p00) * Rgba.A(p00) * w00) + (Rgba.G(p10) * Rgba.A(p10) * w10)
            + (Rgba.G(p01) * Rgba.A(p01) * w01) + (Rgba.G(p11) * Rgba.A(p11) * w11);
        float blue = (Rgba.B(p00) * Rgba.A(p00) * w00) + (Rgba.B(p10) * Rgba.A(p10) * w10)
            + (Rgba.B(p01) * Rgba.A(p01) * w01) + (Rgba.B(p11) * Rgba.A(p11) * w11);

        return Rgba.Pack(
            (int)MathF.Round(alpha),
            Math.Clamp((int)MathF.Round(red / alpha), 0, 255),
            Math.Clamp((int)MathF.Round(green / alpha), 0, 255),
            Math.Clamp((int)MathF.Round(blue / alpha), 0, 255));
    }
}
