using Donkeh.Testing;
using Donkeh.UILab;
using PixelDotNet.Components;
using PixelDotNet.Paint;
using static Donkeh.UILab._;

namespace PixelDotNet.Tests;

public sealed class SidePanelTests
{
    [Fact]
    public void EachTabShowsItsPanel()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<SidePanel>());

        Assert.Equal(5, page.FindAll(".panel-tab").Count);
        Assert.Contains("Layers", page.Find(".panel-tab.is-active").Text);

        session.OpenSideTab(1);
        TestHarness.Reload<SidePanel>(page, session);
        Assert.NotNull(page.TryFind(".color-head"));

        session.OpenSideTab(2);
        TestHarness.Reload<SidePanel>(page, session);
        Assert.NotNull(page.TryFind(".histogram-frame"));

        session.OpenSideTab(3);
        TestHarness.Reload<SidePanel>(page, session);
        Assert.NotNull(page.TryFind(".history-list"));

        session.OpenSideTab(4);
        TestHarness.Reload<SidePanel>(page, session);
        Assert.NotNull(page.TryFind(".effect-list"));
    }

    [Fact]
    public void ClickingATabOpensIt()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<SidePanel>());

        page.FindAll(".panel-tab")[2].Click();

        Assert.Equal(2, session.SideTab);
    }
}

public sealed class ColorPanelTests
{
    [Fact]
    public void PaletteSwatchesSetTheForegroundColour()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<ColorPanel>());

        Assert.Equal(16, page.FindAll(".swatch").Count);
        TestElement swatch = page.FindAll(".swatch")[1];
        string hex = swatch.GetAttribute("title")!;
        uint expected = Convert.ToUInt32(hex.TrimStart('#'), 16);

        swatch.Click();

        Assert.Equal(expected, session.Foreground & 0x00FFFFFF);
        TestHarness.Reload<ColorPanel>(page, session);
        Assert.Equal(hex, page.Find(".color-hex").Text);
    }

    [Fact]
    public void ChannelSlidersAndTargetsEditTheActiveColour()
    {
        PaintSession session = TestHarness.Session();
        session.SetForeground(10, 20, 30);
        var page = TestHarness.Render(session, C<ColorPanel>());

        page.FindAll(".slider-input")[0].Input("200");
        Assert.Equal(200, Rgba.R(session.Foreground));
        Assert.Equal(20, Rgba.G(session.Foreground));
        Assert.Equal(30, Rgba.B(session.Foreground));

        page.FindByText("Outline").Click();
        Assert.Equal(TextColorTarget.Outline, session.ColorTarget);

        page.FindByText("Shadow").Click();
        Assert.Equal(TextColorTarget.Shadow, session.ColorTarget);
        Assert.Contains("Shadow", session.ColorTargetLabel);
    }

    [Fact]
    public void SpectrumAndHueStripChangeTheColour()
    {
        PaintSession session = TestHarness.Session();
        session.SetForeground(255, 0, 0);
        var page = TestHarness.Render(session, C<ColorPanel>());

        bool handled = page.Dispatch(
            page.Find(".sv-square"),
            new PointerDownEvent(100, 60));
        uint afterSquare = session.Foreground;
        bool hueHandled = page.Dispatch(
            page.Find(".hue-strip"),
            new PointerDownEvent(100, 6));

        Assert.True(handled);
        Assert.True(hueHandled);
        Assert.NotEqual(0xFFFF0000u, afterSquare);
        Assert.NotEqual(afterSquare, session.Foreground);
    }

    [Fact]
    public void SwapResetAndPairSwatchesWork()
    {
        PaintSession session = TestHarness.Session();
        session.SetForeground(10, 20, 30);
        var page = TestHarness.Render(session, C<ColorPanel>());

        page.Click(".pair-back");
        Assert.Equal(0xFFFFFFFFu, session.Foreground);
        Assert.Equal(30, Rgba.B(session.Background));

        page.FindByText("Reset").Click();
        Assert.Equal(0xFF1A1A1AU, session.Foreground);
    }
}

public sealed class AdjustPanelTests
{
    [Fact]
    public void RasterPanelShowsAllSections()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<AdjustPanel>());

        string[] titles = page.FindAll(".section-title").Select(title => title.Text).ToArray();
        Assert.Contains("Histogram", titles);
        Assert.Contains("Tone", titles);
        Assert.Contains("Levels", titles);
        Assert.Contains("Curves", titles);
        Assert.Contains("Adjustment Layer", titles);
    }

    [Fact]
    public void ToneSlidersUpdateTheLayerAndResetRestoresIt()
    {
        PaintSession session = TestHarness.Session(8, 8);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(128, 128, 128));
        var page = TestHarness.Render(session, C<AdjustPanel>());

        page.FindAll(".slider-input")[0].Input("50");
        Assert.Equal(50f, session.AdjustBrightness);
        Assert.True(Rgba.R(layer.At(0, 0)) > 128);

        page.FindAll(".section")[1].FindAll(".btn").First(button => button.Text == "Reset").Click();
        Assert.Equal(0f, session.AdjustBrightness);
        Assert.Equal(128, Rgba.R(layer.At(0, 0)));
    }

    [Fact]
    public void CurveEditorAddsAndRemovesPoints()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<AdjustPanel>());
        Assert.Equal(2, session.CurvePoints.Count);

        page.PointerDown(".curve-editor");
        Assert.True(session.CurvePoints.Count >= 3);

        page.Click("[title='Return the tone curve to a straight line']");
        Assert.Equal(2, session.CurvePoints.Count);
    }

    [Fact]
    public void InvertAndDesaturateButtonsEditTheLayer()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(255, 0, 0));
        var page = TestHarness.Render(session, C<AdjustPanel>());

        page.FindByText("Invert").Click();
        Assert.Equal(0xFF00FFFFu, layer.At(0, 0));

        page.FindByText("Mono").Click();
        uint grey = layer.At(0, 0);
        Assert.Equal(Rgba.R(grey), Rgba.B(grey));
    }

    [Fact]
    public void AdjustmentLayerButtonsSwitchThePanelMode()
    {
        PaintSession session = TestHarness.Session(8, 8);
        session.Document.Active.Fill(Rgba.FromRgb(128, 128, 128));
        var page = TestHarness.Render(session, C<AdjustPanel>());

        page.FindByText("Levels Layer").Click();
        Assert.NotNull(session.ActiveAdjustmentLayer);

        page = TestHarness.Render(session, C<AdjustPanel>());
        Assert.Contains(
            page.FindAll(".section-title"),
            title => title.Text == "Levels Adjustment");

        page.FindAll(".slider-input")[0].Input("100");
        Assert.Equal(100f, session.ActiveAdjustmentLayer!.AdjustmentBlack);

        session.AddAdjustmentLayer(AdjustmentKind.Curves);
        page = TestHarness.Render(session, C<AdjustPanel>());
        Assert.Contains(
            page.FindAll(".section-title"),
            title => title.Text == "Curves Adjustment");
    }
}

public sealed class EffectsPanelTests
{
    [Fact]
    public void EffectButtonsPreviewAndApply()
    {
        PaintSession session = TestHarness.Session(16, 16);
        TestHarness.FillPattern(session.Document.Active);
        var page = TestHarness.Render(session, C<EffectsPanel>());

        Assert.Equal(10, page.FindAll(".effect-list .btn").Count);

        page.Click("[title='Preview Vignette']");
        Assert.Equal(EffectKind.Vignette, session.Effect);

        page = TestHarness.Render(session, C<EffectsPanel>());
        Assert.NotNull(page.TryFind(".slider-input"));
        page.FindByText("Apply").Click();
        Assert.Equal(EffectKind.Vignette, session.LastEffect);

        page = TestHarness.Render(session, C<EffectsPanel>());
        page.FindByText("Repeat Vignette").Click();
        Assert.Contains("Vignette", session.Status);
    }

    [Fact]
    public void LockedLayersDisableTheEffectButtons()
    {
        PaintSession session = TestHarness.Session();
        session.Document.Active.Locked = true;
        var page = TestHarness.Render(session, C<EffectsPanel>());

        Assert.All(page.FindAll(".effect-list .btn"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.Contains("locked", page.Find(".panel-note").Text);
    }

    [Fact]
    public void AmountSliderUpdatesThePreview()
    {
        PaintSession session = TestHarness.Session(16, 16);
        TestHarness.FillPattern(session.Document.Active);
        session.SelectEffect(EffectKind.Pixelate);
        var page = TestHarness.Render(session, C<EffectsPanel>());

        page.Input(".slider-input", "4");

        Assert.Equal(4f, session.EffectAmount);
        Assert.True(session.HasEffectPreview);
    }
}

public sealed class HistoryPanelTests
{
    [Fact]
    public void HistoryRowsTravelThroughTheUndoStack()
    {
        PaintSession session = TestHarness.Session();
        session.AddLayer();
        session.AddLayer();
        var page = TestHarness.Render(session, C<HistoryPanel>());

        Assert.Equal(3, page.FindAll(".history-item").Count);
        Assert.True(page.FindAll(".history-item")[^1].HasClass("is-active"));

        page.FindAll(".history-item")[0].Click();
        Assert.Equal(0, session.HistoryIndex);
        Assert.Equal(2, session.Document.Layers.Count);
    }
}

public sealed class LayersPanelTests
{
    [Fact]
    public void LayerRowsSelectTheActiveLayer()
    {
        PaintSession session = TestHarness.Session();
        session.AddLayer();
        var page = TestHarness.Render(session, C<LayersPanel>());

        Assert.Equal(3, page.FindAll(".layer").Count);
        page.FindAll(".layer")[^1].Click();

        Assert.Equal(0, session.Document.ActiveIndex);
    }

    [Fact]
    public void LayerActionButtonsAddDuplicateGroupAndMask()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<LayersPanel>());
        int start = session.Document.Layers.Count;

        page.Click("[title='New layer']");
        Assert.Equal(start + 1, session.Document.Layers.Count);

        page.Click("[title='Duplicate the active layer']");
        Assert.Equal(start + 2, session.Document.Layers.Count);

        page.Click("[title='Put the active layer in a new group']");
        Assert.Single(session.Document.Groups);

        page.Click("[title='Add a layer mask']");
        Assert.True(session.Document.Active.HasMask);
        Assert.True(session.MaskEditing);

        TestHarness.Reload<LayersPanel>(page, session);
        page.Click("[title='Edit or remove the layer mask']");
        Assert.False(session.MaskEditing);

        page.Click("[title='Take the active layer out of its group']");
        Assert.Empty(session.Document.Groups);

        page.Click("[title='Delete the active layer']");
        Assert.Equal(start + 1, session.Document.Layers.Count);
    }

    [Fact]
    public void BlendStepperOpacityClipAndVisibilityWork()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer layer = session.Document.Active;
        var page = TestHarness.Render(session, C<LayersPanel>());

        Assert.Equal("Normal", page.Find(".stepper-value").Text);
        page.FindAll(".stepper-btn")[1].Click();
        Assert.Equal(BlendMode.Multiply, layer.Blend);

        page.FindByText("Below").Click();
        Assert.True(layer.ClipToBelow);

        page.Input(".prop-range", "50");
        Assert.Equal(0.5f, layer.Opacity);

        page.Click("[title='Hide the layer']");
        Assert.False(layer.Visible);

        page.Click("[title='Lock the layer']");
        Assert.True(layer.Locked);
    }

    [Fact]
    public void GroupRowsShowMembersAndCanBeSelected()
    {
        PaintSession session = TestHarness.Session();
        session.GroupActiveLayer();
        session.AddLayer();
        LayerGroup group = session.Document.Groups[0];

        var page = TestHarness.Render(session, C<LayersPanel>());

        Assert.Single(page.FindAll(".layer-group"));
        Assert.Contains(page.FindAll(".layer"), row => row.HasClass("is-child"));
        Assert.Contains("layer", page.Find(".layer-group .layer-meta").Text);

        page.Find(".layer-group").Click();
        Assert.Equal(2, session.Document.ActiveIndex);

        page.Click("[title='Collapse the group']");
        Assert.False(group.Expanded);

        page.Click("[title='Hide the group']");
        Assert.False(group.Visible);
    }

    [Fact]
    public void LayerStyleButtonOpensTheStyleEditor()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<LayersPanel>());

        page.Find(".layer [title='Layer style']").Click();

        Assert.True(session.IsLayerStyleOpen);
    }

    [Fact]
    public void AShapedAndTextLayerShowTheirKindInTheMetaRow()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShapeAsLayer = true;
        session.Tool = PaintTool.Rectangle;
        session.PointerDown(2f, 2f);
        session.PointerMove(12f, 10f);
        session.PointerUp(12f, 10f);

        var page = TestHarness.Render(session, C<LayersPanel>());

        Assert.Contains(
            page.FindAll(".layer-meta").Select(meta => meta.Text),
            text => text.Contains("Rectangle shape"));
    }
}
