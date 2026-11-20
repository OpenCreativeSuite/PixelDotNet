using Donkeh.Testing;
using PixelDotNet.Components;
using PixelDotNet.Paint;
using static Donkeh.UILab._;

namespace PixelDotNet.Tests;

public sealed class ToolRailTests
{
    [Fact]
    public void RailGroupsEveryToolAndSelectsOnClick()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<ToolRail>());

        Assert.Equal(8, page.FindAll(".rail-group").Count);
        Assert.Equal(7, page.FindAll(".rail-sep").Count);
        Assert.Equal(Enum.GetValues<PaintTool>().Length, page.FindAll(".tool").Count);

        page.Click("[title^='Eraser']");
        Assert.Equal(PaintTool.Eraser, session.Tool);

        page.Hover("[title^='Eraser']");
        Assert.NotNull(session.Tip);
        Assert.Equal("Eraser", session.Tip!.Title);
        page.Unhover();
    }
}

public sealed class IconsTests
{
    [Fact]
    public void EveryToolHasAGlyph()
    {
        foreach (PaintTool tool in Enum.GetValues<PaintTool>())
        {
            string glyph = Icons.ForTool(tool);
            Assert.StartsWith("fa-", glyph);
        }

        Assert.True(Icons.IsRegular(PaintTool.Rectangle));
        Assert.True(Icons.IsRegular(PaintTool.Ellipse));
        Assert.True(Icons.IsRegular(PaintTool.EllipseSelect));
        Assert.False(Icons.IsRegular(PaintTool.Brush));

        Assert.Equal("fa-paintbrush", Icons.ForTool(PaintTool.Brush));
        Assert.Equal("fa-stamp", Icons.ForTool(PaintTool.CloneStamp));
    }
}

public sealed class MenuBarTests
{
    [Fact]
    public void MenubarRendersTitlesAndBrand()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<MenuBar>());

        Assert.Equal(7, page.FindAll(".menu-title").Count);
        Assert.Equal("PixelDotNet", page.Find(".brand-name").Text);
    }

    [Fact]
    public void MenuTitlesToggleAndSweepHover()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<MenuBar>());
        TestElement file = page.FindAll(".menu-title").First(title => title.Text == "File");

        file.Click();
        Assert.Equal("File", session.OpenMenu);

        page.Hover(page.FindAll(".menu-title").First(title => title.Text == "Edit"));
        Assert.Equal("Edit", session.OpenMenu);
    }

    [Fact]
    public void OpenMenuShowsRowsDividersAndShortcuts()
    {
        PaintSession session = TestHarness.Session();
        session.OpenMenu = "File";
        var page = TestHarness.Render(session, C<MenuBar>());

        Assert.NotEmpty(page.FindAll(".menu-list .menu-item"));
        Assert.NotEmpty(page.FindAll(".menu-sep"));
        Assert.Contains(
            page.FindAll(".menu-item-label"),
            label => label.Text == "New Small Document");

        page.FindByText("New Small Document").Click();

        Assert.Null(session.OpenMenu);
        Assert.Equal(2, session.DocumentCount);
        Assert.Equal(480, session.Document.Width);
        Assert.Equal(360, session.Document.Height);
    }

    [Fact]
    public void EveryMenuItemRunsWithoutThrowing()
    {
        string[] titles = ["File", "Edit", "Image", "Layer", "Select", "View", "Help"];

        foreach (string title in titles)
        {
            int count;
            PaintSession probe = TestHarness.Session();
            probe.OpenMenu = title;
            var probePage = TestHarness.Render(probe, C<MenuBar>());
            count = probePage.FindAll(".menu-item").Count;
            Assert.True(count > 0, $"{title} should have menu rows.");

            for (int index = 0; index < count; index++)
            {
                PaintSession session = TestHarness.Session();
                session.OpenMenu = title;
                var page = TestHarness.Render(session, C<MenuBar>());

                page.FindAll(".menu-item")[index].Click();
            }
        }
    }

    [Fact]
    public void CheckedRowsReflectViewState()
    {
        PaintSession session = TestHarness.Session();
        session.OpenMenu = "View";
        session.ShowGrid = true;
        var page = TestHarness.Render(session, C<MenuBar>());

        TestElement row = page.FindAll(".menu-item")
            .First(item => item.Find(".menu-item-label").Text == "Toggle Grid");
        Assert.NotEmpty(row.FindAll(".fa-check"));

        page.Find("[title='Choose where to save the image']").Click();
        Assert.True(session.IsFileDialogOpen);
    }
}

public sealed class OptionsBarTests
{
    public static IEnumerable<object[]> Tools() =>
        Enum.GetValues<PaintTool>().Select(tool => new object[] { tool });

    [Theory]
    [MemberData(nameof(Tools))]
    public void EveryToolHasAnOptionsRow(PaintTool tool)
    {
        PaintSession session = TestHarness.Session();
        session.Tool = tool;
        var page = TestHarness.Render(session, C<OptionsBar>());

        Assert.NotEmpty(page.FindAll(".options-group"));
        Assert.Equal(PaintSession.NameOf(tool), page.Find(".tool-badge-name").Text);
        Assert.Equal(PaintSession.ShortcutOf(tool), page.Find(".tool-badge-key").Text);
        Assert.NotEmpty(page.FindAll(".color-block"));
    }

    [Fact]
    public void BrushFieldsUpdateSessionValues()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Brush;
        var page = TestHarness.Render(session, C<OptionsBar>());
        var fields = page.FindAll(".field-input");

        fields[0].Input("40");
        fields[1].Input("50");
        fields[2].Input("25");

        Assert.Equal(40f, session.BrushSize);
        Assert.Equal(0.5f, session.Hardness);
        Assert.Equal(0.25f, session.Opacity);
    }

    [Fact]
    public void ToolSpecificControlsReactToInteraction()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Marquee;
        var page = TestHarness.Render(session, C<OptionsBar>());

        page.FindByText("Select all").Click();
        Assert.NotNull(session.Selection);
        page.FindByText("Deselect").Click();
        Assert.Null(session.Selection);

        session.Tool = PaintTool.MagicWand;
        session.MagicTolerance = 10;
        page = TestHarness.Render(session, C<OptionsBar>());
        page.FindAll(".field-input")[0].Input("64");
        Assert.Equal(64, session.MagicTolerance);

        session.Tool = PaintTool.DodgeBurn;
        session.DodgeMode = true;
        page = TestHarness.Render(session, C<OptionsBar>());
        page.FindByText("Burn").Click();
        Assert.False(session.DodgeMode);
    }

    [Fact]
    public void TextOptionsEditTheComposition()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Text;
        var page = TestHarness.Render(session, C<OptionsBar>());

        page.Input(".text-field", "Hello");
        Assert.Equal("Hello", session.TextContent);

        page.FindAll(".field-input")[0].Input("32");
        Assert.Equal(32f, session.TextSize);

        page.Click("[title='Choose a font']");
        Assert.True(session.IsFontMenuOpen);

        page.FindByText("B").Click();
        Assert.True(session.TextBold);

        page.FindByText("FX").Click();
        Assert.True(session.IsTextFxOpen);
    }

    [Fact]
    public void FontImportRowReplacesTheTextOptions()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Text;
        session.OpenFontAdd();
        var page = TestHarness.Render(session, C<OptionsBar>());

        Assert.NotEmpty(page.FindAll(".font-url"));
        page.Input(".font-url", "https://cdnfonts.test/font");
        Assert.Equal("https://cdnfonts.test/font", session.FontUrl);

        page.FindByText("Cancel").Click();
        Assert.False(session.IsFontAddOpen);
    }

    [Fact]
    public void ColourControlsSwapAndReset()
    {
        PaintSession session = TestHarness.Session();
        uint foreground = session.Foreground;
        uint background = session.Background;
        var page = TestHarness.Render(session, C<OptionsBar>());

        page.Click("[title='Background colour — click to swap']");
        Assert.Equal(background, session.Foreground);
        Assert.Equal(foreground, session.Background);

        page.Click("[title='Default black and white (D)']");
        Assert.Equal(0xFF1A1A1AU, session.Foreground);
        Assert.Equal(0xFFFFFFFFu, session.Background);
    }
}

public sealed class FontPickerTests
{
    [Fact]
    public void PickerListsFiltersAndAppliesFonts()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Text;
        session.ToggleFontMenu();
        var page = TestHarness.Render(session, C<FontPicker>());

        Assert.NotEmpty(page.FindAll(".font-row"));
        Assert.Equal(TextFonts.All.Length, page.FindAll(".font-row").Count);

        page.Input(".font-search", "Arial");
        Assert.Equal("Arial", session.FontSearch);
        Assert.True(session.FontMatchCount >= 1);

        page.FindAll(".font-row")[0].Click();
        Assert.False(session.IsFontMenuOpen);
        Assert.NotEmpty(session.Font.Name);

        page.Find(".font-popup-add").Click();
        Assert.True(session.IsFontAddOpen);
    }

    [Fact]
    public void EmptySearchShowsTheEmptyState()
    {
        PaintSession session = TestHarness.Session();
        session.ToggleFontMenu();
        session.SetFontSearch("no-such-family-xyz");
        var page = TestHarness.Render(session, C<FontPicker>());

        Assert.Equal(0, session.FontMatchCount);
        Assert.NotEmpty(page.FindAll(".font-popup-empty"));
        Assert.Contains("no matches", page.Find(".font-popup-count").Text);
    }
}

public sealed class TextFxPanelTests
{
    [Fact]
    public void PresetsAndTogglesChangeTheTextEffects()
    {
        PaintSession session = TestHarness.Session();
        session.ToggleTextFx();
        var page = TestHarness.Render(session, C<TextFxPanel>());

        Assert.Equal(6, page.FindAll(".text-fx-presets .btn").Count);

        page.FindByText("Neon").Click();
        Assert.Equal(TextPreset.Neon, session.TextPreset);
        Assert.True(session.TextOutline);
        Assert.True(session.TextShadow);

        page.FindAll(".text-fx-switch .btn")[0].Click();
        Assert.False(session.TextOutline);
        page.FindAll(".text-fx-switch .btn")[0].Click();
        Assert.True(session.TextOutline);

        page.Click("[title='Close text effects']");
        Assert.False(session.IsTextFxOpen);
    }

    [Fact]
    public void SlidersEditOutlineShadowAndOpacity()
    {
        PaintSession session = TestHarness.Session();
        session.SetTextOutline(true);
        session.SetTextShadow(true);
        session.ToggleTextFx();
        var page = TestHarness.Render(session, C<TextFxPanel>());
        var sliders = page.FindAll(".slider-input");

        Assert.True(sliders.Count >= 5);
        sliders[0].Input("10");
        Assert.Equal(10f, session.TextOutlineWidth);

        sliders[1].Input("12");
        Assert.Equal(12, session.TextShadowX);

        sliders[3].Input("20");
        Assert.Equal(20f, session.TextShadowBlur);
    }
}

public sealed class StatusBarTests
{
    [Fact]
    public void StatusBarShowsZoomDocumentAndCursorFacts()
    {
        PaintSession session = TestHarness.Session(64, 48);
        session.PointerMove(7f, 8f);
        session.Deselect();
        session.SelectAll();

        var page = TestHarness.Render(session, C<StatusBar>());

        Assert.Contains("100%", page.Find(".status-zoom-reset").Text);
        Assert.Contains("64 × 48 px", page.FindAll(".status-item").Select(item => item.Text));
        Assert.Contains(page.FindAll(".status-item"), item => item.Text.Contains("64, 48") || item.Text.Contains("7, 8"));
        Assert.Contains("Selection 64 × 48", page.FindAll(".status-item").Select(item => item.Text));
        Assert.Equal("Selected all", page.Find(".status-message").Text);
    }

    [Fact]
    public void ZoomButtonsAndResetChangeTheZoom()
    {
        PaintSession session = TestHarness.Session();
        var page = TestHarness.Render(session, C<StatusBar>());

        page.FindAll(".status-btn")[1].Click();
        Assert.Equal(1.5f, session.Zoom, 3);

        page.Find(".status-zoom-reset").Click();
        Assert.Equal(1f, session.Zoom);

        page.FindAll(".status-btn")[0].Click();
        Assert.Equal(0.6667f, session.Zoom, 3);
    }
}

public sealed class ViewportTests
{
    [Fact]
    public void ViewportRendersTabsCanvasAndRulers()
    {
        PaintSession session = TestHarness.Session(64, 48);
        var page = TestHarness.Render(session, C<Viewport>());

        Assert.Single(page.FindAll(".doctab"));
        Assert.NotNull(page.TryFind(".viewport"));
        Assert.NotNull(page.TryFind(".paint-canvas"));
        Assert.Equal("64", page.Find(".paint-canvas").GetAttribute("width"));
        Assert.Equal("48", page.Find(".paint-canvas").GetAttribute("height"));
        Assert.NotNull(page.TryFind(".ruler-top"));
        Assert.NotNull(page.TryFind(".ruler-left"));
        Assert.Contains("width:594px", page.Find(".canvas-slot").GetAttribute("style"));
    }

    [Fact]
    public void TabsSwitchAndCloseDocuments()
    {
        PaintSession session = TestHarness.Session();
        session.NewDocument(20, 20);
        var page = TestHarness.Render(session, C<Viewport>());

        Assert.Equal(2, page.FindAll(".doctab").Count);
        Assert.Equal(2, page.FindAll(".doctab-close").Count);

        page.FindAll(".doctab-select")[0].Click();
        Assert.Equal(0, session.Documents.Single(document => document.IsActive).Index);

        page.FindAll(".doctab-close")[1].Click();
        Assert.Single(session.Documents);
    }

    [Fact]
    public void RulersPlaceGuides()
    {
        PaintSession session = TestHarness.Session(64, 48);
        session.Document.Guides.Clear();
        var page = TestHarness.Render(session, C<Viewport>());

        page.PointerDown(".ruler-top");
        page.PointerDown(".ruler-left");

        Assert.Equal(2, session.Document.Guides.Count);
    }

    [Fact]
    public void CanvasForwardsPointerInputToTheSession()
    {
        PaintSession session = TestHarness.Session(64, 48);
        session.Tool = PaintTool.Brush;
        session.SetForeground(255, 0, 0);
        session.BrushSize = 8f;
        var page = TestHarness.Render(session, C<Viewport>());

        page.PointerDown(".paint-canvas");
        page.PointerUp(".paint-canvas");

        Assert.Contains(
            session.Document.Layers,
            layer => layer.Pixels.Any(pixel => Rgba.A(pixel) > 0));
    }
}
