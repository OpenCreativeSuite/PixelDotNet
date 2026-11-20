using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class SelectionTests
{
    [Fact]
    public void RectangleContainsOnlyItsBounds()
    {
        Selection selection = new(2, 3, 4, 5);

        Assert.Equal(6, selection.Right);
        Assert.Equal(8, selection.Bottom);
        Assert.False(selection.IsEmpty);
        Assert.True(selection.Contains(2, 3));
        Assert.True(selection.Contains(5, 7));
        Assert.False(selection.Contains(1, 3));
        Assert.False(selection.Contains(6, 7));
        Assert.True(selection.CanResize);
    }

    [Fact]
    public void DraggedPointsClampToTheDocument()
    {
        Selection selection = Selection.FromPoints(3.4f, 4.6f, 10.2f, 2.1f, 8, 8);

        Assert.Equal(3, selection.X);
        Assert.Equal(2, selection.Y);
        Assert.Equal(8, selection.Right);
        Assert.Equal(5, selection.Bottom);
    }

    [Fact]
    public void EllipseMaskExcludesTheCorners()
    {
        Selection selection = Selection.Ellipse(0, 0, 9, 9);

        Assert.Equal(SelectionShape.Ellipse, selection.Shape);
        Assert.True(selection.Contains(4, 4));
        Assert.False(selection.Contains(0, 0));
        Assert.Equal(81, selection.Mask!.Length);
        Assert.Equal(SelectionShape.Ellipse, selection.Shifted(2, 2).Shape);
    }

    [Fact]
    public void MaskSelectionsTrimAndShift()
    {
        byte[] mask = new byte[16];
        mask[5] = 255;
        Selection selection = Selection.FromMask(10, 20, 4, 4, mask);
        Selection shifted = selection.Shifted(-3, 7);

        Assert.Equal(SelectionShape.Mask, selection.Shape);
        Assert.True(selection.Contains(11, 21));
        Assert.False(selection.Contains(10, 20));
        Assert.Equal(7, shifted.X);
        Assert.Equal(27, shifted.Y);
        Assert.True(shifted.Contains(8, 28));
    }

    [Fact]
    public void MagicWandCanBeContiguousOrGlobal()
    {
        uint[] pixels = new uint[16];
        for (int y = 0; y < 4; y++)
        {
            pixels[(y * 4) + 0] = 0xFFFF0000;
            pixels[(y * 4) + 1] = 0xFFFF0000;
            pixels[(y * 4) + 2] = 0xFF0000FF;
            pixels[(y * 4) + 3] = 0xFF0000FF;
        }

        Selection contiguous = SelectionOps.FromPixels(pixels, 4, 4, 0, 0, 0, contiguous: true)!;
        Assert.Equal(0, contiguous.X);
        Assert.Equal(2, contiguous.Width);
        Assert.Equal(4, contiguous.Height);

        pixels[15] = 0xFFFF0000;
        Selection global = SelectionOps.FromPixels(pixels, 4, 4, 0, 0, 0, contiguous: false)!;
        Assert.Equal(4, global.Width);
        Assert.Equal(4, global.Height);
        Assert.True(global.Contains(3, 3));
    }

    [Fact]
    public void MagicWandReturnsNullOutsideTheCanvas()
    {
        uint[] pixels = new uint[16];
        Assert.Null(SelectionOps.FromPixels(pixels, 4, 4, -1, 0, 0, true));
        Assert.Null(SelectionOps.FromPixels(pixels, 4, 4, 4, 0, 0, true));
    }

    [Fact]
    public void CombineSupportsUnionAndSubtraction()
    {
        Selection left = new(0, 0, 4, 4);
        Selection right = new(3, 0, 4, 4);

        Selection union = SelectionOps.Combine(left, right, subtract: false, 8, 4)!;
        Assert.Equal(0, union.X);
        Assert.Equal(7, union.Width);
        Assert.True(union.Contains(6, 0));

        Selection difference = SelectionOps.Combine(left, right, subtract: true, 8, 4)!;
        Assert.Equal(0, difference.X);
        Assert.Equal(3, difference.Width);
        Assert.False(difference.Contains(3, 0));

        Assert.Same(left, SelectionOps.Combine(left, null, false, 8, 4));
        Assert.Null(SelectionOps.Combine(null, right, true, 8, 4));
    }

    [Fact]
    public void PolygonFillsItsInterior()
    {
        Selection? selection = SelectionOps.FromPolygon(
            [(1f, 1f), (7f, 1f), (1f, 7f)],
            8,
            8);

        Assert.NotNull(selection);
        Assert.True(selection!.Contains(2, 2));
        Assert.False(selection.Contains(6, 6));
        Assert.Null(SelectionOps.FromPolygon([(1f, 1f), (2f, 2f)], 8, 8));
    }
}
