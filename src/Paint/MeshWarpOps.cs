using System;

namespace PixelDotNet.Paint;

/// <summary>Three-by-three mesh warp over a layer or selection.</summary>
public static class MeshWarpOps
{
    /// <summary>Gets the number of control points per axis.</summary>
    public const int Points = 3;

    /// <summary>Warps the selected region through a grid of movable control points.</summary>
    /// <param name="layer">Layer to edit in place.</param>
    /// <param name="source">Unmodified source pixels.</param>
    /// <param name="selection">Region to warp, or null for the whole layer.</param>
    /// <param name="offsets">Two offsets per control point, row major.</param>
    /// <returns><see langword="false"/> when a cell is degenerate.</returns>
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

        if (rectW <= 0 || rectH <= 0 || offsets.Length < Points * Points * 2)
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

        float cellWidth = rectW / (Points - 1f);
        float cellHeight = rectH / (Points - 1f);
        bool wrote = false;

        for (int row = 0; row < Points - 1; row++)
        {
            for (int column = 0; column < Points - 1; column++)
            {
                int tl = ((row * Points) + column) * 2;
                int tr = ((row * Points) + column + 1) * 2;
                int br = (((row + 1) * Points) + column + 1) * 2;
                int bl = (((row + 1) * Points) + column) * 2;

                float sx0 = rectX + (column * cellWidth);
                float sy0 = rectY + (row * cellHeight);

                float[] quad =
                [
                    sx0 + offsets[tl], sy0 + offsets[tl + 1],
                    sx0 + cellWidth + offsets[tr], sy0 + offsets[tr + 1],
                    sx0 + cellWidth + offsets[br], sy0 + cellHeight + offsets[br + 1],
                    sx0 + offsets[bl], sy0 + cellHeight + offsets[bl + 1],
                ];

                if (!PerspectiveOps.TryInvert(quad, out float[] inverse))
                {
                    continue;
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
                    int targetRow = y * width;
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

                        float sampleX = sx0 + (u * cellWidth);
                        float sampleY = sy0 + (v * cellHeight);

                        if (selection is not null)
                        {
                            int sourceX = Math.Clamp((int)MathF.Floor(sampleX), 0, width - 1);
                            int sourceY = Math.Clamp((int)MathF.Floor(sampleY), 0, height - 1);
                            if (!selection.Contains(sourceX, sourceY))
                            {
                                continue;
                            }
                        }

                        uint color = PerspectiveOps.Sample(source, width, height, sampleX, sampleY);
                        if (Rgba.A(color) == 0)
                        {
                            continue;
                        }

                        layer.Pixels[targetRow + x] = color;
                        wrote = true;
                    }
                }
            }
        }

        return wrote;
    }
}
