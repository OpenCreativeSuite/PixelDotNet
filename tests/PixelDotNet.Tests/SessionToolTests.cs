using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class SessionToolTests
{
    [Fact]
    public void CloneStampCopiesFromTheSampledSource()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        TestHarness.FillPattern(layer);
        uint sampled = layer.At(1, 1);

        session.Tool = PaintTool.CloneStamp;
        session.BrushSize = 4f;
        session.SetCloneSource(1f, 1f);
        Assert.True(session.HasCloneSource);

        session.PointerDown(6f, 6f);
        session.PointerUp(6f, 6f);

        Assert.Equal(sampled, layer.At(6, 6));
    }

    [Fact]
    public void CloneStampRequiresASource()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.CloneStamp;
        session.PointerDown(4f, 4f);

        Assert.Contains("Ctrl-click", session.Status);
        Assert.Equal(0u, session.Document.Active.At(4, 4));

        session.Tool = PaintTool.HealingBrush;
        session.PointerDown(4f, 4f);
        Assert.Contains("Ctrl-click", session.Status);
    }

    [Fact]
    public void CloneAndHealingSecondaryActionSetsTheSource()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.HealingBrush;

        session.SecondaryAction(3f, 5f);

        Assert.Equal(3, session.CloneSourceX);
        Assert.Equal(5, session.CloneSourceY);
        Assert.Contains("Healing Brush source", session.Status);
    }

    [Fact]
    public void HealingBrushKeepsDestinationTone()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(80, 80, 80));
        layer.Put(2, 2, Rgba.FromRgb(200, 200, 200));

        session.Tool = PaintTool.HealingBrush;
        session.BrushSize = 3f;
        session.SetCloneSource(2f, 2f);
        session.PointerDown(9f, 9f);
        session.PointerUp(9f, 9f);

        uint healed = layer.At(9, 9);
        Assert.InRange(Rgba.R(healed), 100, 190);
    }

    [Fact]
    public void DodgeBurnLightensAndDarkens()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(100, 100, 100));
        session.Tool = PaintTool.DodgeBurn;
        session.BrushSize = 6f;
        session.Opacity = 1f;

        session.DodgeMode = true;
        session.PointerDown(5f, 5f);
        session.PointerUp(5f, 5f);
        Assert.True(Rgba.R(layer.At(5, 5)) > 100);

        layer.Fill(Rgba.FromRgb(100, 100, 100));
        session.DodgeMode = false;
        session.PointerDown(5f, 5f);
        session.PointerUp(5f, 5f);
        Assert.True(Rgba.R(layer.At(5, 5)) < 100);
    }

    [Fact]
    public void LineRectangleEllipseAndGradientRasterize()
    {
        PaintSession session = TestHarness.Session(32, 24);

        session.Tool = PaintTool.Line;
        session.SetForeground(255, 0, 0);
        session.BrushSize = 2f;
        session.PointerDown(2f, 2f);
        session.PointerMove(20f, 20f);
        session.PointerUp(20f, 20f);
        Assert.NotEqual(0u, session.Document.Active.At(11, 11));

        session.AddLayer();
        session.Tool = PaintTool.Rectangle;
        session.FillShapes = true;
        session.PointerDown(3f, 3f);
        session.PointerMove(15f, 12f);
        session.PointerUp(15f, 12f);
        Assert.NotEqual(0u, session.Document.Active.At(8, 8));

        session.AddLayer();
        session.Tool = PaintTool.Ellipse;
        session.PointerDown(2f, 2f);
        session.PointerMove(18f, 18f);
        session.PointerUp(18f, 18f);
        Assert.NotEqual(0u, session.Document.Active.At(10, 10));

        session.AddLayer();
        session.Tool = PaintTool.Gradient;
        session.SetForeground(255, 0, 0);
        session.Background = Rgba.FromRgb(0, 0, 255);
        session.PointerDown(0f, 0f);
        session.PointerMove(31f, 0f);
        session.PointerUp(31f, 0f);
        Assert.True(Rgba.R(session.Document.Active.At(0, 0)) > Rgba.B(session.Document.Active.At(0, 0)));
        Assert.True(Rgba.B(session.Document.Active.At(31, 0)) > Rgba.R(session.Document.Active.At(31, 0)));
    }

    [Fact]
    public void MoveToolTranslatesALayerWithNoSelection()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        layer.Put(2, 3, Rgba.FromRgb(1, 2, 3));

        session.SnapToGuides = false;
        session.Tool = PaintTool.Move;
        session.PointerDown(0f, 0f);
        session.PointerMove(4f, 2f);
        session.PointerUp(4f, 2f);

        Assert.Equal(0xFF010203u, layer.At(6, 5));
        Assert.Equal(0u, layer.At(2, 3));
    }

    [Fact]
    public void MoveToolResizesASelectionFromItsHandle()
    {
        PaintSession session = TestHarness.Session(24, 24);
        session.SelectRect(4, 4, 8, 8);
        Assert.Equal(4, session.Selection!.X);

        session.Tool = PaintTool.Move;
        session.PointerDown(4f, 4f);
        session.PointerMove(2f, 2f);
        session.PointerUp(2f, 2f);

        Assert.Equal(2, session.Selection!.X);
        Assert.Equal(2, session.Selection.Y);
        Assert.Equal(10, session.Selection.Width);
        Assert.Equal(10, session.Selection.Height);
    }

    [Fact]
    public void ZoomToolAndSecondaryActionStepTheZoom()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Zoom;
        session.Zoom = 1f;

        session.PointerDown(4f, 4f);
        Assert.Equal(1.5f, session.Zoom, 3);

        session.SecondaryAction(4f, 4f);
        Assert.Equal(1f, session.Zoom, 3);
    }

    [Fact]
    public void WheelHelpersNudgeTheBrushAndZoom()
    {
        PaintSession session = TestHarness.Session();
        session.BrushSize = 10f;

        session.NudgeBrushSize(2f);
        Assert.Equal(16f, session.BrushSize);
        session.NudgeBrushSize(-100f);
        Assert.Equal(1f, session.BrushSize);
        session.NudgeBrushSize(100f);
        Assert.Equal(120f, session.BrushSize);

        session.Zoom = 1f;
        session.ZoomByWheel(1f);
        Assert.Equal(1.5f, session.Zoom, 3);
        session.ZoomByWheel(-1f);
        Assert.Equal(1f, session.Zoom, 3);
        session.ZoomByWheel(0f);
        Assert.Equal(1f, session.Zoom, 3);
    }

    [Fact]
    public void QuickSelectionSubtractRemovesTheRegion()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                layer.Put(x, y, x < 8 ? Rgba.FromRgb(255, 0, 0) : Rgba.FromRgb(0, 0, 255));
            }
        }

        session.Tool = PaintTool.QuickSelect;
        session.BrushSize = 4f;
        session.PointerDown(2f, 2f);
        session.PointerUp(2f, 2f);
        Assert.NotNull(session.Selection);

        session.ToggleQuickSubtract();
        Assert.True(session.QuickSubtract);
        session.PointerDown(2f, 2f);
        session.PointerUp(2f, 2f);

        Assert.Null(session.Selection);
    }

    [Fact]
    public void EyedropperSamplesAndRightClickAlsoSamples()
    {
        PaintSession session = TestHarness.Session(8, 8);
        session.Document.Active.Fill(Rgba.FromRgb(4, 5, 6));
        session.Tool = PaintTool.Brush;

        session.SecondaryAction(3f, 3f);
        Assert.Equal(0xFF040506u, session.Foreground);
        Assert.Contains("Picked", session.Status);
    }

    [Fact]
    public void PaintingIsBlockedOnLockedAdjustmentAndVectorLayers()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Brush;

        session.Document.Active.Locked = true;
        session.PointerDown(4f, 4f);
        Assert.Equal("Layer is locked", session.Status);

        session.Document.Active.Locked = false;
        session.AddAdjustmentLayer(AdjustmentKind.Levels);
        session.PointerDown(4f, 4f);
        Assert.Contains("Select a raster layer", session.Status);

        PaintSession vector = TestHarness.Session(32, 24);
        vector.ShapeAsLayer = true;
        vector.Tool = PaintTool.Rectangle;
        vector.PointerDown(2f, 2f);
        vector.PointerMove(10f, 10f);
        vector.PointerUp(10f, 10f);
        vector.Tool = PaintTool.Brush;
        vector.PointerDown(4f, 4f);
        Assert.Contains("Rasterize", vector.Status);
    }

    [Fact]
    public void MaskEditingBlocksToolsThatCannotPaintTheMask()
    {
        PaintSession session = TestHarness.Session();
        session.ToggleLayerMask();
        session.Tool = PaintTool.Bucket;

        session.PointerDown(4f, 4f);

        Assert.Contains("Editing layer mask", session.Status);
    }
}
