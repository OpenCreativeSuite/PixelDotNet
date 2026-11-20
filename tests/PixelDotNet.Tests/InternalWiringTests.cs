using System.Reflection;
using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Components;
using PixelDotNet.Paint;
using static Donkeh.UILab._;

namespace PixelDotNet.Tests;

public sealed class UiFormatterTests
{
    [Fact]
    public void NumbersFormatWithoutTrailingNoise()
    {
        Assert.Equal("12.5", Ui.Number(12.5f));
        Assert.Equal("1.235", Ui.Number(1.23456f));
        Assert.Equal("0", Ui.Number(0f));
        Assert.Equal("13", Ui.Format(12.6f));
        Assert.Equal("-4", Ui.Format(-3.6f));
    }

    [Fact]
    public void ParsingClampsAndFallsBackToTheMinimum()
    {
        Assert.Equal(5f, Ui.Parse("5", 0f, 10f));
        Assert.Equal(10f, Ui.Parse("99", 0f, 10f));
        Assert.Equal(2f, Ui.Parse("-3", 2f, 10f));
        Assert.Equal(2f, Ui.Parse("not a number", 2f, 10f));
    }

    [Fact]
    public void ColoursAndSizesFormatForMarkup()
    {
        Assert.Equal("#102030", Ui.Hex(0xFF102030));
        Assert.Equal(0x10, Ui.Red(0xFF102030));
        Assert.Equal(0x20, Ui.Green(0xFF102030));
        Assert.Equal(0x30, Ui.Blue(0xFF102030));

        Dictionary<string, string> size = Ui.Size(64, 48);
        Assert.Equal("64", size["width"]);
        Assert.Equal("48", size["height"]);
    }

    [Fact]
    public void LayoutConstantsStaySharedWithTheCanvas()
    {
        Assert.Equal(30f, CanvasSurface.RulerSize);
        Assert.Equal(250f, Viewport.CanvasPadding);
    }
}

public sealed class ViewportPanTests
{
    [Fact]
    public void MiddleButtonDragScrollsTheViewport()
    {
        PaintSession session = TestHarness.Session(2000, 1500);
        var page = TestHarness.Render(session, C<Viewport>());
        TestElement viewportElement = page.Find(".viewport");
        Ref<ElementNode> reference = new() { Value = viewportElement.Element };
        ViewportPan.Attach(reference);

        try
        {
            SetStaticProperty("Middle", true);
            SetPointer(100f, 100f);
            Assert.True(ViewportPan.Handle());
            Assert.NotNull(InputState.CursorOverride);

            float beforeX = viewportElement.Element.ScrollOffsetX;
            float beforeY = viewportElement.Element.ScrollOffsetY;
            SetPointer(140f, 130f);

            Assert.True(ViewportPan.Handle());
            Assert.Equal(beforeX - 40f, viewportElement.Element.ScrollOffsetX);
            Assert.Equal(beforeY - 30f, viewportElement.Element.ScrollOffsetY);

            SetStaticProperty("Middle", false);
            Assert.False(ViewportPan.Handle());
            Assert.Null(InputState.CursorOverride);
        }
        finally
        {
            SetStaticProperty("Middle", false);
            ViewportPan.Handle();
            ViewportPan.Attach(new Ref<ElementNode>());
        }
    }

    [Fact]
    public void ANewPressDiscardsAStaleDragOrigin()
    {
        PaintSession session = TestHarness.Session(2000, 1500);
        var page = TestHarness.Render(session, C<Viewport>());
        Ref<ElementNode> reference = new() { Value = page.Find(".viewport").Element };
        ViewportPan.Attach(reference);

        try
        {
            SetStaticProperty("Middle", true);
            SetStaticProperty("PressSequence", 10001);
            SetPointer(10f, 10f);
            Assert.True(ViewportPan.Handle());

            SetStaticProperty("PressSequence", 10002);
            SetPointer(50f, 50f);
            Assert.True(ViewportPan.Handle());

            float offsetX = reference.Value!.ScrollOffsetX;
            SetPointer(70f, 50f);
            Assert.True(ViewportPan.Handle());
            Assert.Equal(offsetX - 20f, reference.Value!.ScrollOffsetX);
        }
        finally
        {
            SetStaticProperty("Middle", false);
            ViewportPan.Handle();
            ViewportPan.Attach(new Ref<ElementNode>());
        }
    }

    [Fact]
    public void CanvasCursorClassFollowsTheTool()
    {
        PaintSession session = TestHarness.Session();

        session.Tool = PaintTool.Move;
        var page = TestHarness.Render(session, C<Viewport>());
        Assert.True(page.Find(".paint-canvas").HasClass("is-move"));

        session.Tool = PaintTool.Zoom;
        page = TestHarness.Render(session, C<Viewport>());
        Assert.True(page.Find(".paint-canvas").HasClass("is-zoom"));

        session.Tool = PaintTool.Text;
        page = TestHarness.Render(session, C<Viewport>());
        Assert.True(page.Find(".paint-canvas").HasClass("is-text"));

        session.Tool = PaintTool.Brush;
        page = TestHarness.Render(session, C<Viewport>());
        Assert.True(page.Find(".paint-canvas").HasClass("is-draw"));
    }

    [Fact]
    public void WheelModifiersZoomAndNudgeTheBrush()
    {
        PaintSession session = TestHarness.Session();
        session.Zoom = 1f;
        session.BrushSize = 10f;
        var page = TestHarness.Render(session, C<Viewport>());

        try
        {
            SetStaticProperty("Control", true);
            page.Scroll(".paint-canvas", 0, 1);
            Assert.Equal(1.5f, session.Zoom, 3);

            SetStaticProperty("Control", false);
            SetStaticProperty("Alt", true);
            page.Scroll(".paint-canvas", 0, 1);
            Assert.Equal(13f, session.BrushSize);
        }
        finally
        {
            SetStaticProperty("Control", false);
            SetStaticProperty("Alt", false);
        }
    }

    private static void SetPointer(float x, float y) =>
        typeof(InputState)
            .GetMethod("SetPointer", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [x, y]);

    private static void SetStaticProperty(string name, object? value) =>
        typeof(InputState)
            .GetProperty(name, BindingFlags.Static | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(null, [value]);
}
