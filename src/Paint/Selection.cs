using System;

namespace PixelDotNet.Paint;

/// <summary>
/// A rectangular document-space selection. Painting, filling, gradients and
/// adjustments are clipped to it.
/// </summary>
public sealed class Selection
{
    /// <summary>Creates a selection from an inclusive-exclusive rectangle.</summary>
    public Selection(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
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

    /// <summary>Gets whether the rectangle covers no pixels.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Builds a selection from two dragged corners, clamped to the document.</summary>
    public static Selection FromPoints(float ax, float ay, float bx, float by, int documentWidth, int documentHeight)
    {
        int left = Math.Clamp((int)MathF.Round(MathF.Min(ax, bx)), 0, documentWidth);
        int top = Math.Clamp((int)MathF.Round(MathF.Min(ay, by)), 0, documentHeight);
        int right = Math.Clamp((int)MathF.Round(MathF.Max(ax, bx)), 0, documentWidth);
        int bottom = Math.Clamp((int)MathF.Round(MathF.Max(ay, by)), 0, documentHeight);
        return new Selection(left, top, right - left, bottom - top);
    }

    /// <summary>Returns whether a document pixel is inside the selection.</summary>
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
}
