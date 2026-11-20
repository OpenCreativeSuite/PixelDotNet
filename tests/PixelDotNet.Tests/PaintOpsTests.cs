using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class PaintOpsTests
{
    [Fact]
    public void FloodFillRespectsToleranceAndSelection()
    {
        PaintLayer layer = new("Layer", 8, 8);
        layer.Fill(Rgba.Transparent);

        PaintOps.FloodFill(layer, 1, 1, Rgba.FromRgb(255, 0, 0), 0, 255, null);
        Assert.Equal(0xFFFF0000u, layer.At(0, 0));
        Assert.Equal(0xFFFF0000u, layer.At(7, 7));

        PaintLayer barrier = new("Barrier", 8, 8);
        barrier.Fill(Rgba.Transparent);
        for (int y = 0; y < 8; y++)
        {
            barrier.Put(4, y, 0xFF000000);
        }

        PaintOps.FloodFill(barrier, 6, 4, Rgba.FromRgb(0, 255, 0), 0, 255, null);
        Assert.Equal(0x00000000u, barrier.At(0, 0));
        Assert.Equal(0xFF00FF00u, barrier.At(6, 4));

        Selection clipped = new(0, 0, 2, 2);
        PaintLayer selected = new("Selected", 8, 8);
        PaintOps.FloodFill(selected, 0, 0, Rgba.FromRgb(0, 0, 255), 0, 255, clipped);
        Assert.Equal(0xFF0000FFu, selected.At(1, 1));
        Assert.Equal(0u, selected.At(3, 3));
    }

    [Fact]
    public void BlendAndDiscPaintPixelsWithCoverage()
    {
        PaintLayer layer = new("Layer", 16, 16);
        PaintOps.Blend(layer, 1, 1, Rgba.FromRgb(255, 0, 0), 128);
        Assert.Equal(255, Rgba.R(layer.At(1, 1)));
        Assert.Equal(128, Rgba.A(layer.At(1, 1)));

        PaintOps.Disc(layer, 8f, 8f, 3f, Rgba.FromRgb(0, 255, 0));
        Assert.Equal(255, Rgba.A(layer.At(8, 8)));
        Assert.Equal(0, Rgba.A(layer.At(0, 0)));

        PaintLayer clipped = new("Clipped", 16, 16);
        PaintOps.Disc(clipped, 2f, 2f, 6f, Rgba.FromRgb(0, 0, 255), selection: new Selection(0, 0, 4, 4));
        Assert.Equal(0u, clipped.At(7, 7));
    }

    [Fact]
    public void FilledAndOutlinedShapesDifferAtTheCentre()
    {
        PaintLayer filled = new("Filled", 16, 16);
        uint[] blank = new uint[256];
        PaintOps.Shape(filled, blank, 2f, 2f, 14f, 14f, ellipse: false, filled: true, 2f, Rgba.FromRgb(255, 0, 0), 255, null);
        Assert.Equal(0xFFFF0000u, filled.At(8, 8));

        PaintLayer outlined = new("Outlined", 16, 16);
        PaintOps.Shape(outlined, blank, 2f, 2f, 14f, 14f, ellipse: false, filled: false, 2f, Rgba.FromRgb(0, 255, 0), 255, null);
        Assert.Equal(0u, outlined.At(8, 8));
        Assert.NotEqual(0u, outlined.At(2, 8));
    }

    [Fact]
    public void EllipseShapeFillsTheCentreAndNotTheCorner()
    {
        PaintLayer layer = new("Layer", 16, 16);
        PaintOps.Shape(layer, new uint[256], 0f, 0f, 16f, 16f, ellipse: true, filled: true, 1f, Rgba.White, 255, null);

        Assert.NotEqual(0u, layer.At(8, 8));
        Assert.Equal(0u, layer.At(0, 0));
    }

    [Fact]
    public void LinearGradientInterpolatesFromStartToEnd()
    {
        PaintLayer layer = new("Layer", 8, 8);
        PaintOps.LinearGradient(
            layer,
            new uint[64],
            0f,
            0f,
            7f,
            0f,
            Rgba.FromRgb(255, 0, 0),
            Rgba.FromRgb(0, 0, 255),
            255,
            null);

        Assert.True(Rgba.R(layer.At(0, 0)) > Rgba.B(layer.At(0, 0)));
        Assert.True(Rgba.B(layer.At(7, 0)) > Rgba.R(layer.At(7, 0)));
    }

    [Fact]
    public void TranslateMovesPixelsAndDropsTheEdges()
    {
        PaintLayer layer = new("Layer", 4, 4);
        layer.Put(1, 1, Rgba.FromRgb(1, 2, 3));
        uint[] source = layer.CapturePixels();

        PaintOps.Translate(layer, source, 2, 0);

        Assert.Equal(0xFF010203u, layer.At(3, 1));
        Assert.Equal(0u, layer.At(1, 1));
    }

    [Fact]
    public void InvertAndDesaturateRespectTheSelection()
    {
        PaintLayer layer = new("Layer", 4, 4);
        layer.Fill(Rgba.FromRgb(255, 0, 0));

        PaintOps.Invert(layer, new Selection(0, 0, 2, 2));
        Assert.Equal(0xFF00FFFFu, layer.At(0, 0));
        Assert.Equal(0xFFFF0000u, layer.At(3, 3));

        PaintOps.Desaturate(layer, new Selection(0, 0, 2, 2));
        uint grey = layer.At(0, 0);
        Assert.Equal(Rgba.R(grey), Rgba.G(grey));
        Assert.Equal(Rgba.G(grey), Rgba.B(grey));
        Assert.Equal(0xFFFF0000u, layer.At(3, 3));
    }

    [Fact]
    public void LevelsAndCurvesRemapTone()
    {
        PaintLayer levels = new("Levels", 4, 4);
        levels.Fill(Rgba.FromRgb(128, 128, 128));
        PaintOps.Levels(levels, null, 128f, 255f, 1f);
        Assert.Equal(0, Rgba.R(levels.At(1, 1)));

        PaintLayer curve = new("Curve", 4, 4);
        curve.Fill(Rgba.White);
        PaintOps.Curves(curve, null, [(0f, 0f), (1f, 0.5f)]);
        Assert.InRange(Rgba.R(curve.At(1, 1)), 127, 128);

        PaintLayer identity = new("Identity", 4, 4);
        identity.Fill(Rgba.FromRgb(10, 20, 30));
        PaintOps.Curves(identity, null, [(0f, 0f), (1f, 1f)]);
        Assert.Equal(0xFF0A141Eu, identity.At(1, 1));
    }

    [Fact]
    public void RetoneBrightensOpaquePixelsAndLeavesTransparentOnesAlone()
    {
        PaintLayer layer = new("Layer", 4, 4);
        layer.Fill(Rgba.FromRgb(128, 128, 128));
        layer.Put(0, 0, Rgba.Transparent);
        uint[] source = layer.CapturePixels();

        PaintOps.Retone(layer, source, null, 0.5f, 0f, 0f, 1f, 0f);

        Assert.True(Rgba.R(layer.At(1, 1)) > 150);
        Assert.Equal(0u, layer.At(0, 0));
    }

    [Fact]
    public void DrawingHelpersFillRectanglesGradientsAndRidges()
    {
        PaintLayer layer = new("Layer", 8, 8);
        PaintOps.Rect(layer, 1, 1, 3, 2, Rgba.FromRgb(10, 20, 30));
        Assert.Equal(0xFF0A141Eu, layer.At(1, 1));
        Assert.Equal(0xFF0A141Eu, layer.At(3, 2));
        Assert.Equal(0u, layer.At(5, 5));

        PaintOps.VerticalGradient(layer, 0, 8, Rgba.FromRgb(0, 0, 0), Rgba.FromRgb(255, 255, 255));
        Assert.True(Rgba.R(layer.At(0, 7)) > Rgba.R(layer.At(0, 0)));

        PaintLayer ridge = new("Ridge", 8, 8);
        PaintOps.Ridge(ridge, 4f, 1f, 0f, 1f, Rgba.FromRgb(1, 2, 3));
        Assert.Equal(0xFF010203u, ridge.At(0, 7));
    }
}
