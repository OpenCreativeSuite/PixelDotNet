using System;
using System.Collections.Generic;

namespace PixelDotNet.Paint;

/// <summary>Selection builders that do not depend on the editor session.</summary>
public static class SelectionOps
{
    /// <summary>
    /// Selects the connected colour region around a seed pixel on the active
    /// layer. The comparison is per channel including alpha.
    /// </summary>
    public static Selection? MagicWand(
        PaintDocument document,
        int seedX,
        int seedY,
        int tolerance,
        bool contiguous)
    {
        int width = document.Width;
        int height = document.Height;
        if (seedX < 0 || seedY < 0 || seedX >= width || seedY >= height)
        {
            return null;
        }

        uint[] source = document.Active.Pixels;
        int seed = (seedY * width) + seedX;
        uint target = source[seed];
        int targetR = Rgba.R(target);
        int targetG = Rgba.G(target);
        int targetB = Rgba.B(target);
        int targetA = Rgba.A(target);
        int limit = Math.Clamp(tolerance, 0, 255);
        byte[] mask = new byte[width * height];

        if (contiguous)
        {
            bool[] seen = new bool[mask.Length];
            Stack<int> pending = new();
            seen[seed] = true;
            pending.Push(seed);

            while (pending.Count > 0)
            {
                int index = pending.Pop();
                if (!Matches(source[index], targetR, targetG, targetB, targetA, limit))
                {
                    continue;
                }

                mask[index] = 255;
                int x = index % width;
                int y = index / width;

                if (x > 0)
                {
                    Visit(index - 1);
                }

                if (x < width - 1)
                {
                    Visit(index + 1);
                }

                if (y > 0)
                {
                    Visit(index - width);
                }

                if (y < height - 1)
                {
                    Visit(index + width);
                }
            }

            void Visit(int index)
            {
                if (!seen[index])
                {
                    seen[index] = true;
                    pending.Push(index);
                }
            }
        }
        else
        {
            for (int index = 0; index < source.Length; index++)
            {
                if (Matches(source[index], targetR, targetG, targetB, targetA, limit))
                {
                    mask[index] = 255;
                }
            }
        }

        return Trim(mask, 0, 0, width, height);
    }

    /// <summary>
    /// Fills a freehand polygon into a selection. Rows are filled from their edge
    /// crossings, so the cost follows the outline rather than the covered area.
    /// </summary>
    public static Selection? FromPolygon(IReadOnlyList<(float X, float Y)> points, int documentWidth, int documentHeight)
    {
        if (points.Count < 3 || documentWidth <= 0 || documentHeight <= 0)
        {
            return null;
        }

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        for (int index = 0; index < points.Count; index++)
        {
            minX = MathF.Min(minX, points[index].X);
            minY = MathF.Min(minY, points[index].Y);
            maxX = MathF.Max(maxX, points[index].X);
            maxY = MathF.Max(maxY, points[index].Y);
        }

        int left = Math.Clamp((int)MathF.Floor(minX), 0, documentWidth);
        int top = Math.Clamp((int)MathF.Floor(minY), 0, documentHeight);
        int right = Math.Clamp((int)MathF.Ceiling(maxX), 0, documentWidth);
        int bottom = Math.Clamp((int)MathF.Ceiling(maxY), 0, documentHeight);
        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        byte[] mask = new byte[width * height];
        float[] crossings = new float[points.Count];

        for (int row = 0; row < height; row++)
        {
            float scanY = top + row + 0.5f;
            int count = 0;

            for (int index = 0; index < points.Count; index++)
            {
                (float x1, float y1) = points[index];
                (float x2, float y2) = points[(index + 1) % points.Count];
                if ((y1 <= scanY && y2 > scanY) || (y2 <= scanY && y1 > scanY))
                {
                    crossings[count++] = x1 + ((scanY - y1) * (x2 - x1) / (y2 - y1));
                }
            }

            Array.Sort(crossings, 0, count);
            int rowStart = row * width;
            for (int index = 0; index + 1 < count; index += 2)
            {
                int columnStart = Math.Clamp(
                    (int)MathF.Ceiling(crossings[index] - left - 0.5f),
                    0,
                    width);
                int columnEnd = Math.Clamp(
                    (int)MathF.Ceiling(crossings[index + 1] - left - 0.5f),
                    columnStart,
                    width);

                for (int column = columnStart; column < columnEnd; column++)
                {
                    mask[rowStart + column] = 255;
                }
            }
        }

        return Trim(mask, left, top, width, height);
    }

    /// <summary>Crops a full-size mask to the tight bounds of its set pixels.</summary>
    private static Selection? Trim(byte[] mask, int originX, int originY, int width, int height)
    {
        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;

        for (int row = 0; row < height; row++)
        {
            int rowStart = row * width;
            for (int column = 0; column < width; column++)
            {
                if (mask[rowStart + column] == 0)
                {
                    continue;
                }

                minX = Math.Min(minX, column);
                maxX = Math.Max(maxX, column);
                minY = Math.Min(minY, row);
                maxY = Math.Max(maxY, row);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return null;
        }

        int trimmedWidth = maxX - minX + 1;
        int trimmedHeight = maxY - minY + 1;
        if (trimmedWidth == width && trimmedHeight == height)
        {
            return Selection.FromMask(originX, originY, width, height, mask);
        }

        byte[] trimmed = new byte[trimmedWidth * trimmedHeight];
        for (int row = 0; row < trimmedHeight; row++)
        {
            Array.Copy(
                mask,
                ((minY + row) * width) + minX,
                trimmed,
                row * trimmedWidth,
                trimmedWidth);
        }

        return Selection.FromMask(originX + minX, originY + minY, trimmedWidth, trimmedHeight, trimmed);
    }

    /// <summary>Compares a pixel with a target colour per channel.</summary>
    private static bool Matches(uint color, int r, int g, int b, int a, int tolerance) =>
        Math.Abs(Rgba.R(color) - r) <= tolerance
        && Math.Abs(Rgba.G(color) - g) <= tolerance
        && Math.Abs(Rgba.B(color) - b) <= tolerance
        && Math.Abs(Rgba.A(color) - a) <= tolerance;
}
