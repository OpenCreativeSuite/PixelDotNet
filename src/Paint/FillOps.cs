using System;

namespace PixelDotNet.Paint;

/// <summary>Selection fills that reconstruct content from nearby pixels.</summary>
public static class FillOps
{
    /// <summary>
    /// Fills the selected pixels with the nearest unselected pixel and smooths
    /// the result, which removes small objects without leaving a hard hole.
    /// </summary>
    /// <param name="layer">Layer to edit.</param>
    /// <param name="selection">Region to fill; must not be empty.</param>
    public static void ContentAware(PaintLayer layer, Selection? selection)
    {
        if (selection is null)
        {
            return;
        }

        int width = layer.Width;
        int height = layer.Height;
        int count = 0;
        bool[] hole = new bool[width * height];

        int left = Math.Max(0, selection.X);
        int top = Math.Max(0, selection.Y);
        int right = Math.Min(width, selection.Right);
        int bottom = Math.Min(height, selection.Bottom);

        for (int y = top; y < bottom; y++)
        {
            int row = y * width;
            for (int x = left; x < right; x++)
            {
                if (selection.Contains(x, y))
                {
                    hole[row + x] = true;
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return;
        }

        uint[] source = layer.CapturePixels();
        int[] distance = new int[width * height];
        int[] nearest = new int[width * height];

        for (int i = 0; i < hole.Length; i++)
        {
            if (hole[i])
            {
                distance[i] = int.MaxValue / 4;
                nearest[i] = -1;
            }
            else
            {
                distance[i] = 0;
                nearest[i] = i;
            }
        }

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int index = row + x;
                if (!hole[index])
                {
                    continue;
                }

                Relax(index, x - 1, y, 1);
                Relax(index, x, y - 1, 1);
                Relax(index, x - 1, y - 1, 2);
                Relax(index, x + 1, y - 1, 2);
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            int row = y * width;
            for (int x = width - 1; x >= 0; x--)
            {
                int index = row + x;
                if (!hole[index])
                {
                    continue;
                }

                Relax(index, x + 1, y, 1);
                Relax(index, x, y + 1, 1);
                Relax(index, x + 1, y + 1, 2);
                Relax(index, x - 1, y + 1, 2);
            }
        }

        for (int i = 0; i < hole.Length; i++)
        {
            if (hole[i])
            {
                layer.Pixels[i] = nearest[i] >= 0 ? source[nearest[i]] : Rgba.Transparent;
            }
        }

        Smooth(layer, hole, width, height, 3);

        void Relax(int index, int sampleX, int sampleY, int weight)
        {
            if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height)
            {
                return;
            }

            int sample = (sampleY * width) + sampleX;
            if (nearest[sample] < 0)
            {
                return;
            }

            int candidate = distance[sample] + weight;
            if (candidate < distance[index])
            {
                distance[index] = candidate;
                nearest[index] = nearest[sample];
            }
        }
    }

    /// <summary>Softens the filled area so the boundary transition is less visible.</summary>
    private static void Smooth(
        PaintLayer layer,
        bool[] hole,
        int width,
        int height,
        int iterations)
    {
        uint[] work = layer.CapturePixels();
        uint[] scratch = new uint[work.Length];

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            Array.Copy(work, scratch, work.Length);

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    int index = row + x;
                    if (!hole[index])
                    {
                        continue;
                    }

                    int red = 0;
                    int green = 0;
                    int blue = 0;
                    int alpha = 0;
                    int samples = 0;

                    AddSample(x - 1, y);
                    AddSample(x + 1, y);
                    AddSample(x, y - 1);
                    AddSample(x, y + 1);

                    if (samples > 0)
                    {
                        work[index] = Rgba.Pack(
                            (byte)(alpha / samples),
                            red / samples,
                            green / samples,
                            blue / samples);
                    }

                    void AddSample(int sx, int sy)
                    {
                        if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                        {
                            return;
                        }

                        uint color = scratch[(sy * width) + sx];
                        red += Rgba.R(color);
                        green += Rgba.G(color);
                        blue += Rgba.B(color);
                        alpha += Rgba.A(color);
                        samples++;
                    }
                }
            }
        }

        Array.Copy(work, layer.Pixels, work.Length);
    }
}
