using System;
using System.Collections.Generic;

namespace PixelDotNet.Paint;

/// <summary>Geometry and paint settings for a non-destructive shape layer.</summary>
public sealed class ShapeData
{
    /// <summary>Gets or sets the shape primitive.</summary>
    public ShapeKind Kind { get; set; }

    /// <summary>Gets or sets the left edge in document pixels.</summary>
    public float X { get; set; }

    /// <summary>Gets or sets the top edge in document pixels.</summary>
    public float Y { get; set; }

    /// <summary>Gets or sets the width in document pixels.</summary>
    public float Width { get; set; }

    /// <summary>Gets or sets the height in document pixels.</summary>
    public float Height { get; set; }

    /// <summary>Gets or sets whether the interior is filled.</summary>
    public bool Fill { get; set; } = true;

    /// <summary>Gets or sets the outline width in pixels.</summary>
    public float StrokeWidth { get; set; } = 2f;

    /// <summary>Gets or sets the fill colour.</summary>
    public uint FillColor { get; set; }

    /// <summary>Gets or sets the outline colour.</summary>
    public uint StrokeColor { get; set; }

    /// <summary>Gets or sets the polygon points for a path shape.</summary>
    public List<(float X, float Y)>? Points { get; set; }

    /// <summary>Returns an independent copy of this shape.</summary>
    public ShapeData Clone() => new()
    {
        Kind = Kind,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Fill = Fill,
        StrokeWidth = StrokeWidth,
        FillColor = FillColor,
        StrokeColor = StrokeColor,
        Points = Points is null ? null : [.. Points],
    };

    /// <summary>Copies every setting from another shape.</summary>
    public void CopyFrom(ShapeData source)
    {
        Kind = source.Kind;
        X = source.X;
        Y = source.Y;
        Width = source.Width;
        Height = source.Height;
        Fill = source.Fill;
        StrokeWidth = source.StrokeWidth;
        FillColor = source.FillColor;
        StrokeColor = source.StrokeColor;
        Points = source.Points is null ? null : [.. source.Points];
    }
}

/// <summary>The shape primitives a shape layer can hold.</summary>
public enum ShapeKind
{
    /// <summary>An axis-aligned rectangle.</summary>
    Rectangle,

    /// <summary>An ellipse inscribed in the bounds.</summary>
    Ellipse,

    /// <summary>A straight line from top-left to bottom-right of the bounds.</summary>
    Line,

    /// <summary>A closed polygon defined by explicit points.</summary>
    Path,
}

/// <summary>Rasterises shape-layer geometry into its pixel cache.</summary>
public static class ShapeOps
{
    /// <summary>Rebuilds a shape layer's pixels from its geometry.</summary>
    public static void Render(PaintLayer layer)
    {
        if (layer.Shape is not { } shape)
        {
            return;
        }

        Array.Clear(layer.Pixels);
        float left = MathF.Min(shape.X, shape.X + shape.Width);
        float top = MathF.Min(shape.Y, shape.Y + shape.Height);
        float right = left + MathF.Abs(shape.Width);
        float bottom = top + MathF.Abs(shape.Height);
        float stroke = MathF.Max(1f, shape.StrokeWidth);

        if (shape.Kind == ShapeKind.Path && shape.Points is { Count: >= 2 } points)
        {
            if (shape.Fill)
            {
                FillPolygon(layer, points, shape.FillColor);
            }

            for (int i = 0; i < points.Count; i++)
            {
                (float x1, float y1) = points[i];
                (float x2, float y2) = points[(i + 1) % points.Count];
                DrawLine(layer, x1, y1, x2, y2, stroke, shape.StrokeColor);
            }

            return;
        }

        if (shape.Kind == ShapeKind.Line)
        {
            DrawLine(layer, left, top, right, bottom, stroke, shape.StrokeColor);
            return;
        }

        bool ellipse = shape.Kind == ShapeKind.Ellipse;

        if (shape.Fill)
        {
            uint[] blank = new uint[layer.Pixels.Length];
            PaintOps.Shape(
                layer,
                blank,
                left,
                top,
                right,
                bottom,
                ellipse,
                filled: true,
                stroke,
                shape.FillColor,
                255,
                null);
        }

        PaintOps.Shape(
            layer,
            layer.Pixels,
            left,
            top,
            right,
            bottom,
            ellipse,
            filled: false,
            stroke,
            shape.StrokeColor,
            255,
            null);
    }

    /// <summary>Fills a closed polygon with a scanline pass.</summary>
    private static void FillPolygon(
        PaintLayer layer,
        IReadOnlyList<(float X, float Y)> points,
        uint color)
    {
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach ((float _, float y) in points)
        {
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
        }

        int top = Math.Max(0, (int)MathF.Floor(minY));
        int bottom = Math.Min(layer.Height - 1, (int)MathF.Ceiling(maxY));
        List<float> crossings = [];

        for (int y = top; y <= bottom; y++)
        {
            float scanY = y + 0.5f;
            crossings.Clear();

            for (int i = 0; i < points.Count; i++)
            {
                (float x1, float y1) = points[i];
                (float x2, float y2) = points[(i + 1) % points.Count];

                if ((y1 <= scanY && y2 > scanY) || (y2 <= scanY && y1 > scanY))
                {
                    float t = (scanY - y1) / (y2 - y1);
                    crossings.Add(x1 + ((x2 - x1) * t));
                }
            }

            if (crossings.Count < 2)
            {
                continue;
            }

            crossings.Sort();
            int row = y * layer.Width;

            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                int start = Math.Max(0, (int)MathF.Ceiling(crossings[i] - 0.5f));
                int end = Math.Min(layer.Width - 1, (int)MathF.Floor(crossings[i + 1] - 0.5f));

                for (int x = start; x <= end; x++)
                {
                    layer.Pixels[row + x] = Rgba.Over(layer.Pixels[row + x], color, 255);
                }
            }
        }
    }

    /// <summary>Draws a thick line with soft coverage at the edges.</summary>
    private static void DrawLine(
        PaintLayer layer,
        float x1,
        float y1,
        float x2,
        float y2,
        float thickness,
        uint color)
    {
        float radius = MathF.Max(0.5f, thickness * 0.5f);
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(x1, x2) - radius - 1f));
        int maxX = Math.Min(layer.Width - 1, (int)MathF.Ceiling(MathF.Max(x1, x2) + radius + 1f));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(y1, y2) - radius - 1f));
        int maxY = Math.Min(layer.Height - 1, (int)MathF.Ceiling(MathF.Max(y1, y2) + radius + 1f));

        float dx = x2 - x1;
        float dy = y2 - y1;
        float lengthSquared = (dx * dx) + (dy * dy);

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * layer.Width;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f - x1;
                float py = y + 0.5f - y1;
                float t = lengthSquared <= 0.0001f
                    ? 0f
                    : Math.Clamp(((px * dx) + (py * dy)) / lengthSquared, 0f, 1f);
                float nearestX = x1 + (dx * t);
                float nearestY = y1 + (dy * t);
                float distance = MathF.Sqrt(
                    ((x + 0.5f - nearestX) * (x + 0.5f - nearestX))
                    + ((y + 0.5f - nearestY) * (y + 0.5f - nearestY)));
                float coverage = Math.Clamp(radius - distance + 0.5f, 0f, 1f);
                if (coverage <= 0f)
                {
                    continue;
                }

                layer.Pixels[row + x] = Rgba.Over(
                    layer.Pixels[row + x],
                    color,
                    (int)(coverage * 255f));
            }
        }
    }
}
