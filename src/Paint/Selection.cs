using System;

namespace PixelDotNet.Paint;

/// <summary>How a selection's boundary is drawn and resized.</summary>
public enum SelectionShape
{
    /// <summary>An axis-aligned rectangle.</summary>
    Rectangle,

    /// <summary>An ellipse inscribed in the selection bounds.</summary>
    Ellipse,

    /// <summary>An arbitrary mask produced by the wand or lasso.</summary>
    Mask,
}

/// <summary>
/// A document-space selection. Painting, filling, gradients, adjustments and
/// clipboard operations are clipped to it.
/// </summary>
/// <remarks>
/// Rectangular selections carry no mask, so the common case stays a cheap bounds
/// check. Ellipses and freehand shapes store one coverage byte per pixel of
/// <see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, <see cref="Height"/>.
/// The mask is never mutated after construction, which lets undo and document
/// tabs share a selection reference safely.
/// </remarks>
public sealed class Selection
{
    private readonly byte[]? _mask;

    /// <summary>Creates a selection from an inclusive-exclusive rectangle.</summary>
    public Selection(int x, int y, int width, int height)
        : this(x, y, width, height, SelectionShape.Rectangle, null)
    {
    }

    private Selection(int x, int y, int width, int height, SelectionShape shape, byte[]? mask)
    {
        X = x;
        Y = y;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
        Shape = shape;
        _mask = mask;
    }

    /// <summary>Gets the left edge in document pixels.</summary>
    public int X { get; }

    /// <summary>Gets the top edge in document pixels.</summary>
    public int Y { get; }

    /// <summary>Gets the width in document pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in document pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the right edge, exclusive.</summary>
    public int Right => X + Width;

    /// <summary>Gets the bottom edge, exclusive.</summary>
    public int Bottom => Y + Height;

    /// <summary>Gets whether the selection covers no pixels.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Gets how the selection is drawn.</summary>
    public SelectionShape Shape { get; }

    /// <summary>Gets the per-pixel mask, or null for a rectangular selection.</summary>
    public byte[]? Mask => _mask;

    /// <summary>Gets whether the Move tool may resize this selection from its handles.</summary>
    public bool CanResize => Shape is SelectionShape.Rectangle or SelectionShape.Ellipse;

    /// <summary>Builds a selection from two dragged corners, clamped to the document.</summary>
    public static Selection FromPoints(float ax, float ay, float bx, float by, int documentWidth, int documentHeight)
    {
        (int left, int top, int right, int bottom) = BoundsFromPoints(ax, ay, bx, by, documentWidth, documentHeight);
        return new Selection(left, top, right - left, bottom - top);
    }

    /// <summary>Builds an elliptical selection from two dragged corners.</summary>
    public static Selection EllipseFromPoints(float ax, float ay, float bx, float by, int documentWidth, int documentHeight)
    {
        (int left, int top, int right, int bottom) = BoundsFromPoints(ax, ay, bx, by, documentWidth, documentHeight);
        return Ellipse(left, top, right - left, bottom - top);
    }

    /// <summary>Builds an elliptical selection inscribed in a rectangle.</summary>
    public static Selection Ellipse(int x, int y, int width, int height) =>
        new(x, y, width, height, SelectionShape.Ellipse, CreateEllipseMask(width, height));

    /// <summary>Wraps an arbitrary mask whose bounds have already been trimmed.</summary>
    public static Selection FromMask(int x, int y, int width, int height, byte[] mask) =>
        new(x, y, width, height, SelectionShape.Mask, mask);

    /// <summary>Returns whether a document pixel is inside the selection.</summary>
    public bool Contains(int x, int y)
    {
        if (x < X || y < Y || x >= Right || y >= Bottom)
        {
            return false;
        }

        return _mask is null || _mask[((y - Y) * Width) + (x - X)] != 0;
    }

    /// <summary>
    /// Returns a copy shifted by a document offset. The mask is relative to the
    /// bounds, so it moves with them without being rewritten.
    /// </summary>
    public Selection Shifted(int dx, int dy) =>
        new(X + dx, Y + dy, Width, Height, Shape, _mask);

    /// <summary>Builds the coverage mask for an ellipse inscribed in a rectangle.</summary>
    public static byte[] CreateEllipseMask(int width, int height)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        byte[] mask = new byte[width * height];
        if (width == 0 || height == 0)
        {
            return mask;
        }

        float radiusX = width * 0.5f;
        float radiusY = height * 0.5f;
        for (int row = 0; row < height; row++)
        {
            float dy = (row + 0.5f - radiusY) / radiusY;
            float dySquared = dy * dy;
            int rowStart = row * width;
            for (int column = 0; column < width; column++)
            {
                float dx = (column + 0.5f - radiusX) / radiusX;
                if ((dx * dx) + dySquared <= 1f)
                {
                    mask[rowStart + column] = 255;
                }
            }
        }

        return mask;
    }

    /// <summary>Rounds and clamps two dragged corners into document bounds.</summary>
    private static (int Left, int Top, int Right, int Bottom) BoundsFromPoints(
        float ax,
        float ay,
        float bx,
        float by,
        int documentWidth,
        int documentHeight)
    {
        int left = Math.Clamp((int)MathF.Round(MathF.Min(ax, bx)), 0, documentWidth);
        int top = Math.Clamp((int)MathF.Round(MathF.Min(ay, by)), 0, documentHeight);
        int right = Math.Clamp((int)MathF.Round(MathF.Max(ax, bx)), 0, documentWidth);
        int bottom = Math.Clamp((int)MathF.Round(MathF.Max(ay, by)), 0, documentHeight);
        return (left, top, right, bottom);
    }
}
