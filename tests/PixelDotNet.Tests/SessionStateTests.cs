using PixelDotNet.Help;
using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class SessionStateTests
{
    [Fact]
    public void ToolMetadataCoversEveryTool()
    {
        PaintTool[] tools = Enum.GetValues<PaintTool>();

        Assert.Equal(tools.Length, tools.Select(PaintSession.NameOf).Distinct().Count());
        Assert.Equal(tools.Length, tools.Select(PaintSession.ShortcutOf).Distinct().Count());
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(PaintSession.NameOf(tool))));
        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(PaintSession.ShortcutOf(tool))));
    }

    [Fact]
    public void SessionStartsOnTheActiveDocument()
    {
        PaintSession session = TestHarness.Session();

        Assert.Equal("Test", session.DocumentName);
        Assert.Equal("RGB/8", session.ColorMode);
        Assert.Single(session.Documents);
        Assert.False(session.CanCloseDocument);
        Assert.Equal(0, session.DocumentCount - 1);
        Assert.Equal("Ready", session.Status);
        Assert.Equal(PaintTool.Brush, session.Tool);
    }

    [Fact]
    public void SelectingAToolUpdatesStatusAndClosesPopups()
    {
        PaintSession session = TestHarness.Session();
        session.ToggleFontMenu();

        session.SelectTool(PaintTool.Eraser);

        Assert.Equal(PaintTool.Eraser, session.Tool);
        Assert.False(session.IsFontMenuOpen);
        Assert.Equal("Eraser tool", session.Status);

        session.SelectTool(PaintTool.Eraser);
        Assert.Equal("Eraser tool", session.Status);
    }

    [Fact]
    public void ColourChannelsAndSwapsResolveTheActiveTarget()
    {
        PaintSession session = TestHarness.Session();

        session.SetForeground(1, 2, 3);
        Assert.Equal(0xFF010203u, session.Foreground);
        Assert.Equal(Rgba.ToHsv(0xFF010203u).Hue, session.PickerHue, 4);

        session.SwapColors();
        Assert.Equal(0xFFFFFFFFu, session.Foreground);
        Assert.Equal(0xFF010203u, session.Background);

        session.ResetColors();
        Assert.Equal(0xFF1A1A1Au, session.Foreground);
        Assert.Equal(0xFFFFFFFFu, session.Background);
        Assert.Equal(TextColorTarget.Fill, session.ColorTarget);

        session.EditTextColour(TextColorTarget.Outline);
        Assert.Equal(1, session.SideTab);
        session.SetActiveColor(9, 8, 7);
        Assert.Equal(0xFF090807u, session.TextOutlineColor);
        Assert.Equal(0xFF090807u, session.ActiveColor);
        Assert.Equal(TextPreset.Custom, session.TextPreset);

        session.EditTextColour(TextColorTarget.Shadow);
        session.SetHsv(0.5f, 1f, 1f);
        Assert.NotEqual(0xFF090807u, session.TextShadowColor);

        session.OpenColourTab();
        Assert.Equal(TextColorTarget.Fill, session.ColorTarget);
        Assert.Equal(1, session.SideTab);
    }

    [Fact]
    public void ZoomMovesThroughFixedStops()
    {
        PaintSession session = TestHarness.Session();
        session.Zoom = 1f;

        session.ZoomIn();
        Assert.Equal(1.5f, session.Zoom, 3);
        Assert.Equal("150%", session.Status);

        session.ZoomOut();
        Assert.Equal(1f, session.Zoom, 3);

        session.Zoom = PaintSession.ZoomLevels[^1];
        session.ZoomIn();
        Assert.Equal(PaintSession.ZoomLevels[^1], session.Zoom);

        session.Zoom = PaintSession.ZoomLevels[0];
        session.ZoomOut();
        Assert.Equal(PaintSession.ZoomLevels[0], session.Zoom);

        session.ActualPixels();
        Assert.Equal(1f, session.Zoom);
        Assert.Equal("100%", session.Status);
    }

    [Fact]
    public void ViewTogglesAndSideTabsUpdateState()
    {
        PaintSession session = TestHarness.Session();
        bool grid = session.ShowGrid;
        bool rulers = session.ShowRulers;
        bool guides = session.ShowGuides;
        bool snapping = session.SnapToGuides;
        bool tooltips = session.ShowTooltips;

        session.ToggleGrid();
        session.ToggleRulers();
        session.ToggleGuides();
        session.ToggleSnapping();
        session.ToggleTooltips();

        Assert.Equal(!grid, session.ShowGrid);
        Assert.Equal(!rulers, session.ShowRulers);
        Assert.Equal(!guides, session.ShowGuides);
        Assert.Equal(!snapping, session.SnapToGuides);
        Assert.Equal(!tooltips, session.ShowTooltips);
        Assert.Equal("Tooltips on", session.Status);

        session.OpenSideTab(99);
        Assert.Equal(4, session.SideTab);
        session.OpenSideTab(-1);
        Assert.Equal(0, session.SideTab);
    }

    [Fact]
    public void GuidesCanBePlacedDraggedSnappedAndCleared()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.Document.Guides.Clear();

        session.PlaceGuide(horizontal: true, 12f);
        Assert.Single(session.Document.Guides);
        Assert.Equal(12, session.Document.Guides[0].Position);
        Assert.True(session.ShowGuides);

        session.PlaceGuide(horizontal: false, 15f);
        Assert.Equal(16, session.Document.Guides[1].Position);

        session.SnapToGuides = false;
        session.BeginGuideDrag(horizontal: true);
        Assert.Equal(3, session.Document.Guides.Count);
        session.UpdateGuideDrag(6f);
        Assert.Equal(6, session.Document.Guides[^1].Position);
        session.EndGuideDrag();
        Assert.Equal("Guide placed", session.Status);

        session.SnapToGuides = true;
        Assert.Equal((6, -1), session.SnapMove(10, 10, 6, 6, 5, 0));

        session.ClearGuides();
        Assert.Empty(session.Document.Guides);

        session.SnapToGuides = false;
        Assert.Equal((3, 4), session.SnapMove(10, 10, 6, 6, 3, 4));
    }

    [Fact]
    public void MenusOpenSwapAndClose()
    {
        PaintSession session = TestHarness.Session();

        session.ToggleMenu("File");
        Assert.Equal("File", session.OpenMenu);

        session.HoverMenu("Edit");
        Assert.Equal("Edit", session.OpenMenu);

        session.HoverMenu("Edit");
        Assert.Equal("Edit", session.OpenMenu);

        session.ToggleMenu("Edit");
        Assert.Null(session.OpenMenu);

        session.OpenMenu = "View";
        session.HoverMenu("Layer");
        Assert.Equal("Layer", session.OpenMenu);
        session.CloseMenu();
        Assert.Null(session.OpenMenu);
    }

    [Fact]
    public void TipsAndHelpOpenAndClose()
    {
        PaintSession session = TestHarness.Session();
        TipState tip = new("fa-tool", "Brush", "B", "tool.brush", TipPlacement.Auto, false, 10, 20, 30, 40);

        session.ShowTip(tip);
        Assert.Same(tip, session.Tip);
        Assert.Equal(1, session.TipGeneration);

        session.HideTip();
        Assert.Null(session.Tip);

        TipState chevron = tip with { ShowChevron = true };
        session.ShowTip(chevron);
        Assert.Null(session.Tip);

        HelpTopic topic = HelpCatalog.Find("tool.move");
        session.ShowHelp(topic);
        Assert.Same(topic, session.OpenHelp);
        Assert.Contains("Move", session.Status);

        session.CloseHelp();
        Assert.Null(session.OpenHelp);
    }

    [Fact]
    public void UndoRedoAndHistoryTravelAcrossOperations()
    {
        PaintSession session = TestHarness.Session();
        int start = session.Document.Layers.Count;

        session.AddLayer();
        Assert.Equal(start + 1, session.Document.Layers.Count);
        Assert.True(session.CanUndo);
        Assert.Equal(2, session.HistorySteps.Count);
        Assert.Equal(1, session.HistoryIndex);

        session.Undo();
        Assert.Equal(start, session.Document.Layers.Count);
        Assert.True(session.CanRedo);

        session.Redo();
        Assert.Equal(start + 1, session.Document.Layers.Count);
        Assert.False(session.CanRedo);

        session.GoToHistory(0);
        Assert.Equal(start, session.Document.Layers.Count);
        Assert.Equal(0, session.HistoryIndex);

        session.GoToHistory(1);
        Assert.Equal(start + 1, session.Document.Layers.Count);
        Assert.Equal(1, session.HistoryIndex);
    }
}

public sealed class SessionDocumentTests
{
    [Fact]
    public void NewDocumentsAreAddedAsTabsAndCanBeSwitched()
    {
        PaintSession session = TestHarness.Session();
        session.Zoom = 2f;

        session.NewDocument(64, 48);

        Assert.Equal(2, session.DocumentCount);
        Assert.True(session.CanCloseDocument);
        Assert.Equal(64, session.Document.Width);
        Assert.Equal(48, session.Document.Height);
        Assert.Equal(1, session.Documents[1].Index);
        Assert.True(session.Documents[1].IsActive);

        session.SwitchDocument(0);
        Assert.Equal(32, session.Document.Width);
        Assert.Equal(2f, session.Zoom);

        session.NextDocument();
        Assert.Equal(1, session.Documents.Single(document => document.IsActive).Index);

        session.PreviousDocument();
        Assert.Equal(0, session.Documents.Single(document => document.IsActive).Index);
    }

    [Fact]
    public void ClosingTabsKeepsOneDocumentOpen()
    {
        PaintSession session = TestHarness.Session();
        session.NewDocument(64, 48);
        session.NewDocument(20, 20);

        session.CloseDocument(0);
        Assert.Equal(2, session.DocumentCount);
        Assert.Equal(1, session.Documents.Single(document => document.IsActive).Index);

        session.CloseActiveDocument();
        Assert.Single(session.Documents);
        Assert.False(session.CanCloseDocument);

        session.CloseActiveDocument();
        Assert.Single(session.Documents);
    }

    [Fact]
    public void DemoArtBuildsAndResetsToTheSample()
    {
        PaintSession session = TestHarness.Session();
        session.LoadDemo();

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal("Moonlight", session.DocumentName);
        Assert.Equal(768, session.Document.Width);
        Assert.Equal(576, session.Document.Height);
        Assert.NotEmpty(session.Document.Layers);
    }

    [Fact]
    public void ImageResizeScalesPixelsAndCanBeUndone()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.Document.Active.Fill(Rgba.FromRgb(255, 0, 0));

        session.ShowImageResizeDialog();
        Assert.True(session.IsResizeOpen);
        Assert.False(session.ResizeIsCanvas);
        Assert.Equal("32", session.ResizeWidthText);
        Assert.Equal("24", session.ResizeHeightText);

        session.ResizeSetWidth("64");
        Assert.Equal("48", session.ResizeHeightText);

        session.ApplyResize();

        Assert.False(session.IsResizeOpen);
        Assert.Equal(64, session.Document.Width);
        Assert.Equal(48, session.Document.Height);
        Assert.Equal(2, session.Document.Layers.Count);
        Assert.Equal(0xFFFF0000u, session.Document.Active.At(0, 0));

        session.Undo();
        Assert.Equal(32, session.Document.Width);
        Assert.Equal(24, session.Document.Height);
    }

    [Fact]
    public void CanvasResizeAnchorsExistingPixels()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.Document.Active.Put(0, 0, Rgba.FromRgb(1, 2, 3));

        session.ShowCanvasResizeDialog();
        Assert.True(session.ResizeIsCanvas);
        session.ToggleResizeAspect();
        session.ResizeSetWidth("40");
        session.ResizeSetHeight("30");
        session.SetResizeAnchor(ResizeAnchor.TopLeft);
        session.ApplyResize();

        Assert.Equal(40, session.Document.Width);
        Assert.Equal(30, session.Document.Height);
        Assert.Equal(0xFF010203u, session.Document.Active.At(0, 0));
    }

    [Fact]
    public void ResizeValidationReportsProblemsWithoutChangingTheDocument()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShowImageResizeDialog();

        session.ResizeSetWidth("abc");
        session.ApplyResize();
        Assert.Contains("whole number", session.ResizeError);
        Assert.Equal(32, session.Document.Width);

        session.ResizeSetWidth("32");
        session.ResizeSetHeight("24");
        session.ApplyResize();
        Assert.Contains("already that size", session.ResizeError);
    }

    [Fact]
    public void ResizeDialogClosesWithoutApplying()
    {
        PaintSession session = TestHarness.Session();

        session.ShowImageResizeDialog();
        session.CloseResizeDialog();

        Assert.False(session.IsResizeOpen);
        Assert.Equal(32, session.Document.Width);
        Assert.Equal(string.Empty, session.ResizeError);
    }
}
