using PixelDotNet.Components;
using PixelDotNet.Help;
using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class ViewportShortcutTests
{
    [Fact]
    public void BareLetterKeysSelectEveryTool()
    {
        Dictionary<string, PaintTool> map = new()
        {
            ["V"] = PaintTool.Move,
            ["M"] = PaintTool.Marquee,
            ["Q"] = PaintTool.EllipseSelect,
            ["F"] = PaintTool.Lasso,
            ["W"] = PaintTool.MagicWand,
            ["A"] = PaintTool.QuickSelect,
            ["B"] = PaintTool.Brush,
            ["E"] = PaintTool.Eraser,
            ["C"] = PaintTool.CloneStamp,
            ["H"] = PaintTool.HealingBrush,
            ["J"] = PaintTool.DodgeBurn,
            ["L"] = PaintTool.Line,
            ["R"] = PaintTool.Rectangle,
            ["O"] = PaintTool.Ellipse,
            ["P"] = PaintTool.Pen,
            ["G"] = PaintTool.Gradient,
            ["K"] = PaintTool.Bucket,
            ["I"] = PaintTool.Eyedropper,
            ["T"] = PaintTool.Text,
            ["Z"] = PaintTool.Zoom,
        };

        PaintSession session = TestHarness.Session();
        foreach ((string key, PaintTool tool) in map)
        {
            Viewport.HandleShortcut(session, key);
            Assert.Equal(tool, session.Tool);
        }
    }

    [Fact]
    public void TypingAndModalEditorsOwnTheKeyboard()
    {
        PaintSession session = TestHarness.Session();
        session.BeginTyping();
        Viewport.HandleShortcut(session, "E");
        Assert.Equal(PaintTool.Brush, session.Tool);
        session.EndTyping();

        session.ToggleFontMenu();
        Viewport.HandleShortcut(session, "b");
        Assert.Contains("b", session.FontSearch);
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsFontMenuOpen);

        session.CreateRectangleShape();
        session.ShowShapeEditDialog();
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsShapeEditOpen);

        session.ShowPerspectiveDialog();
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsPerspectiveOpen);

        session.ShowMeshWarpDialog();
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsMeshWarpOpen);

        session.ShowColourToolsDialog();
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsColourToolsOpen);

        session.ShowLayerStyleDialog(session.Document.ActiveIndex);
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsLayerStyleOpen);
    }

    [Fact]
    public void EscapeClosesMenusTipsHelpAndFileBrowsers()
    {
        PaintSession session = TestHarness.Session();
        session.OpenLayerMenu(session.Document.ActiveIndex, null, 4f, 4f);
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsLayerMenuOpen);

        session.ShowTip(new TipState(
            Icons.Brush, "Brush", "B", "tool.brush", TipPlacement.Auto, false, 0, 0, 10, 10));
        Viewport.HandleShortcut(session, "Escape");
        Assert.Null(session.Tip);

        session.ShowHelp(HelpCatalog.Find("tool.brush"));
        Viewport.HandleShortcut(session, "Escape");
        Assert.Null(session.OpenHelp);

        session.ToggleTextFx();
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsTextFxOpen);

        session.ShowBatchDialog();
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsBatchOpen);

        session.ShowSaveDialog();
        session.OpenFileContextMenu(null, 0f, 0f);
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsFileMenuOpen);
        Assert.True(session.IsFileDialogOpen);

        session.FileMenuNewFolder();
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsFilePromptOpen);
        Assert.True(session.IsFileDialogOpen);

        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsFileDialogOpen);
    }

    [Fact]
    public void ResizeAndBatchDialogsUseEnterToApply()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShowImageResizeDialog();
        session.ResizeSetWidth("32");
        session.ResizeSetHeight("24");
        Viewport.HandleShortcut(session, "Return");
        Assert.Contains("already that size", session.ResizeError);

        session.ShowBatchDialog();
        session.BatchSetScale("0");
        Viewport.HandleShortcut(session, "Enter");
        Assert.Contains("scale", session.BatchStatus);
    }

    [Fact]
    public void FileBrowserEnterConfirmsTheDialog()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session(4, 4);
        session.ShowSaveDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("keyboard");

        Viewport.HandleShortcut(session, "Return");

        Assert.True(File.Exists(temp.File("keyboard.png")));
        Assert.False(session.IsFileDialogOpen);
    }

    [Fact]
    public void PathAndPenEditingKeysFinishOrCancelGestures()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.Tool = PaintTool.Pen;
        session.PointerDown(2f, 2f);
        session.PointerDown(10f, 2f);
        session.PointerDown(10f, 10f);
        session.PointerDown(2f, 10f);
        Viewport.HandleShortcut(session, "Return");
        Assert.False(session.IsPenActive);
        Assert.True(session.Document.Active.IsShapeLayer);

        session.StartPathNodeEdit();
        session.PointerDown(2f, 2f);
        Assert.True(session.SelectedPathNode >= 0);
        Viewport.HandleShortcut(session, "Delete");
        Assert.Equal(3, session.Document.Active.Shape!.Points!.Count);
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsPathNodeEditing);

        session.Tool = PaintTool.Pen;
        session.PointerDown(4f, 12f);
        session.PointerDown(12f, 12f);
        Viewport.HandleShortcut(session, "Escape");
        Assert.False(session.IsPenActive);
    }

    [Fact]
    public void CommandShortcutsEditTheDocument()
    {
        PaintSession session = TestHarness.Session();
        int layers = session.Document.Layers.Count;

        Viewport.HandleShortcut(session, "Ctrl+Shift+N");
        Assert.Equal(layers + 1, session.Document.Layers.Count);
        Viewport.HandleShortcut(session, "Ctrl+Z");
        Assert.Equal(layers, session.Document.Layers.Count);
        Viewport.HandleShortcut(session, "Ctrl+Y");
        Assert.Equal(layers + 1, session.Document.Layers.Count);
        Viewport.HandleShortcut(session, "Ctrl+Z");
        Viewport.HandleShortcut(session, "Ctrl+Shift+Z");
        Assert.Equal(layers + 1, session.Document.Layers.Count);

        session.Document.Active.Fill(Rgba.FromRgb(255, 0, 0));
        Viewport.HandleShortcut(session, "Ctrl+I");
        Assert.Equal(0xFF00FFFFu, session.Document.Active.At(0, 0));
        Viewport.HandleShortcut(session, "Ctrl+Shift+U");
        Assert.Equal(Rgba.R(session.Document.Active.At(0, 0)), Rgba.B(session.Document.Active.At(0, 0)));

        Viewport.HandleShortcut(session, "Ctrl+J");
        Assert.Equal(layers + 2, session.Document.Layers.Count);
        Viewport.HandleShortcut(session, "Ctrl+E");
        Assert.Equal(layers + 1, session.Document.Layers.Count);

        session.SelectAll();
        Viewport.HandleShortcut(session, "Ctrl+C");
        Assert.True(session.HasClipboard);
        Viewport.HandleShortcut(session, "Ctrl+V");
        Assert.Equal(layers + 2, session.Document.Layers.Count);
        Viewport.HandleShortcut(session, "Ctrl+X");
        Assert.Equal("Cut", session.Status.Split(' ')[0]);

        Viewport.HandleShortcut(session, "Ctrl+D");
        Assert.Null(session.Selection);
        session.SelectAll();
        Viewport.HandleShortcut(session, "Delete");
        Assert.Equal(0u, session.Document.Active.At(0, 0));
    }

    [Fact]
    public void DocumentAndViewShortcutsStepState()
    {
        PaintSession session = TestHarness.Session();
        session.Zoom = 2f;
        Viewport.HandleShortcut(session, "Ctrl+0");
        Assert.Equal(1f, session.Zoom);
        Viewport.HandleShortcut(session, "Ctrl+Equal");
        Assert.Equal(1.5f, session.Zoom, 3);
        Viewport.HandleShortcut(session, "Ctrl+Minus");
        Assert.Equal(1f, session.Zoom);

        session.SetForeground(1, 2, 3);
        Viewport.HandleShortcut(session, "X");
        Assert.Equal(1, Rgba.R(session.Background));
        Viewport.HandleShortcut(session, "D");
        Assert.Equal(0xFF1A1A1AU, session.Foreground);

        session.BrushSize = 10f;
        Viewport.HandleShortcut(session, "LeftBracket");
        Assert.Equal(8f, session.BrushSize);
        Viewport.HandleShortcut(session, "RightBracket");
        Assert.Equal(10f, session.BrushSize);

        Viewport.HandleShortcut(session, "Ctrl+N");
        Assert.Equal(2, session.DocumentCount);
        Viewport.HandleShortcut(session, "Tab");
        Assert.Equal(0, session.Documents.Single(document => document.IsActive).Index);
        Viewport.HandleShortcut(session, "Ctrl+Shift+Tab");
        Assert.Equal(1, session.Documents.Single(document => document.IsActive).Index);

        Viewport.HandleShortcut(session, "Ctrl+W");
        Assert.Single(session.Documents);

        Viewport.HandleShortcut(session, "Ctrl+O");
        Assert.True(session.IsFileDialogOpen);
        Viewport.HandleShortcut(session, "Escape");
        Viewport.HandleShortcut(session, "Ctrl+S");
        Assert.Equal(FileDialogMode.Save, session.FileDialogMode);
    }

    [Fact]
    public void ZoomShortcutAliasesWork()
    {
        PaintSession session = TestHarness.Session();
        session.Zoom = 1f;

        Viewport.HandleShortcut(session, "Ctrl+Number0");
        Assert.Equal(1f, session.Zoom);

        Viewport.HandleShortcut(session, "Ctrl+Plus");
        Assert.Equal(1.5f, session.Zoom, 3);

        Viewport.HandleShortcut(session, "Ctrl+Shift+Equal");
        Assert.Equal(2f, session.Zoom, 3);
    }
}

internal static class SessionShortcutTestExtensions
{
    /// <summary>Creates a rectangle shape layer ready for the shape editor tests.</summary>
    public static void CreateRectangleShape(this PaintSession session)
    {
        session.ShapeAsLayer = true;
        session.Tool = PaintTool.Rectangle;
        session.PointerDown(2f, 2f);
        session.PointerMove(12f, 10f);
        session.PointerUp(12f, 10f);
    }
}
