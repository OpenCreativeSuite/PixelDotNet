using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class SessionPaintingTests
{
    [Fact]
    public void BrushPaintsTheActiveLayerAndUndoRestoresIt()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Brush;
        session.SetForeground(255, 0, 0);
        session.BrushSize = 4f;

        session.PointerDown(5f, 5f);
        session.PointerUp(5f, 5f);

        uint painted = session.Document.Active.At(5, 5);
        Assert.Equal(255, Rgba.R(painted));
        Assert.Equal(255, Rgba.A(painted));
        Assert.Equal(0u, session.Document.Active.At(0, 0));

        session.Undo();
        Assert.Equal(0u, session.Document.Active.At(5, 5));
    }

    [Fact]
    public void EraserRemovesAlphaFromTheBackground()
    {
        PaintSession session = TestHarness.Session();
        session.SelectLayer(0);
        session.Document.Active.Fill(Rgba.FromRgb(255, 0, 0));
        session.Tool = PaintTool.Eraser;
        session.BrushSize = 4f;

        session.PointerDown(4f, 4f);
        session.PointerUp(4f, 4f);

        Assert.True(Rgba.A(session.Document.Active.At(4, 4)) < 255);
        Assert.Equal(255, Rgba.A(session.Document.Active.At(20, 20)));
    }

    [Fact]
    public void BucketFillsTheConnectedRegionAndEyedropperSamplesTheComposite()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Bucket;
        session.SetForeground(0, 255, 0);
        session.Tolerance = 0;

        session.PointerDown(1f, 1f);
        Assert.Equal(0xFF00FF00u, session.Document.Active.At(20, 20));

        session.Deselect();
        session.Tool = PaintTool.Eyedropper;
        session.PointerDown(20f, 20f);
        Assert.Equal(0xFF00FF00u, session.Foreground);
    }

    [Fact]
    public void MarqueeEllipseAndLassoCreateSelections()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Marquee;
        session.PointerDown(2f, 2f);
        session.PointerMove(10f, 8f);
        session.PointerUp(10f, 8f);

        Assert.NotNull(session.Selection);
        Assert.Equal(2, session.Selection!.X);
        Assert.Equal(2, session.Selection.Y);
        Assert.Equal(8, session.Selection.Width);
        Assert.Equal(6, session.Selection.Height);
        Assert.Equal("Selection 8 x 6", session.Status);

        session.Tool = PaintTool.EllipseSelect;
        session.PointerDown(1f, 1f);
        session.PointerMove(9f, 9f);
        session.PointerUp(9f, 9f);
        Assert.Equal(SelectionShape.Ellipse, session.Selection!.Shape);

        session.Tool = PaintTool.Lasso;
        session.PointerDown(2f, 2f);
        session.PointerMove(10f, 2f);
        session.PointerMove(10f, 10f);
        session.PointerMove(2f, 10f);
        session.PointerUp(2f, 2f);

        Assert.NotNull(session.Selection);
        Assert.Equal(SelectionShape.Mask, session.Selection!.Shape);
        Assert.Empty(session.LassoPoints);
    }

    [Fact]
    public void MagicWandAndQuickSelectionSelectSimilarPixels()
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

        session.Tool = PaintTool.MagicWand;
        session.MagicTolerance = 0;
        session.MagicContiguous = true;
        session.PointerDown(2f, 2f);

        Assert.NotNull(session.Selection);
        Assert.Equal(0, session.Selection!.X);
        Assert.Equal(8, session.Selection.Width);
        Assert.Equal(16, session.Selection.Height);

        session.Deselect();
        session.Tool = PaintTool.QuickSelect;
        session.BrushSize = 4f;
        session.PointerDown(2f, 2f);
        session.PointerUp(2f, 2f);

        Assert.NotNull(session.Selection);
        Assert.True(session.Selection!.Contains(2, 2));
    }

    [Fact]
    public void SelectionMoveResizesAndMovesPixels()
    {
        PaintSession session = TestHarness.Session();
        session.Document.Active.Fill(Rgba.FromRgb(255, 0, 0));
        session.SelectAll();
        session.Tool = PaintTool.Move;

        session.PointerDown(4f, 4f);
        session.PointerMove(8f, 6f);
        session.PointerUp(8f, 6f);

        Assert.Equal(0xFFFF0000u, session.Document.Active.At(10, 6));
        Assert.Equal(0u, session.Document.Active.At(1, 1));
    }

    [Fact]
    public void FillClearCopyCutAndPasteOperateOnTheSelection()
    {
        PaintSession session = TestHarness.Session();
        session.SetForeground(10, 20, 30);
        session.Deselect();
        session.FillSelection();
        Assert.Equal(0xFF0A141Eu, session.Document.Active.At(0, 0));

        session.SelectAll();
        session.ClearSelectedPixels();
        Assert.Equal(0u, session.Document.Active.At(0, 0));

        session.Document.Active.Fill(Rgba.FromRgb(255, 0, 0));
        session.SelectRect(0, 0, 8, 8);
        session.Copy();
        Assert.True(session.HasClipboard);

        int layers = session.Document.Layers.Count;
        session.Paste();
        Assert.Equal(layers + 1, session.Document.Layers.Count);
        Assert.Equal(0xFFFF0000u, session.Document.Active.At(0, 0));

        session.SelectRect(0, 0, 4, 4);
        session.Cut();
        Assert.Equal(0u, session.Document.Active.At(1, 1));
    }

    [Fact]
    public void CopyMergedCapturesTheVisibleComposite()
    {
        PaintSession session = TestHarness.Session();
        session.Document.Active.Fill(Rgba.FromRgb(0, 255, 0));
        session.SelectRect(0, 0, 4, 4);

        session.CopyMerged();

        Assert.True(session.HasClipboard);
        Assert.Contains("Copied merged", session.Status);
    }

    [Fact]
    public void RepeatedClicksKeepTheLastCursorPosition()
    {
        PaintSession session = TestHarness.Session();
        session.Tool = PaintTool.Eyedropper;

        session.PointerMove(7f, 9f);
        Assert.Equal(7, session.CursorX);
        Assert.Equal(9, session.CursorY);

        session.PointerDown(3f, 4f);
        Assert.Equal(3, session.CursorX);
        Assert.Equal(4, session.CursorY);
    }
}

internal static class SessionSelectionExtensions
{
    /// <summary>Creates a rectangular selection through the marquee tool.</summary>
    public static void SelectRect(this PaintSession session, int x, int y, int width, int height)
    {
        PaintTool tool = session.Tool;
        session.Tool = PaintTool.Marquee;
        session.PointerDown(x, y);
        session.PointerMove(x + width, y + height);
        session.PointerUp(x + width, y + height);
        session.Tool = tool;
    }
}

public sealed class SessionLayerTests
{
    [Fact]
    public void AddDuplicateMergeFlattenAndDeleteMaintainTheStack()
    {
        PaintSession session = TestHarness.Session();
        int start = session.Document.Layers.Count;

        session.AddLayer();
        Assert.Equal(start + 1, session.Document.Layers.Count);
        Assert.Equal(start, session.Document.ActiveIndex);

        session.Document.Active.Fill(Rgba.FromRgb(1, 2, 3));
        session.DuplicateLayer();
        Assert.Equal(start + 2, session.Document.Layers.Count);
        Assert.Equal(0xFF010203u, session.Document.Active.At(0, 0));

        session.DeleteActiveLayer();
        Assert.Equal(start + 1, session.Document.Layers.Count);

        session.Document.Layers[0].Fill(Rgba.White);
        session.MergeDown();
        Assert.Equal(start, session.Document.Layers.Count);
        Assert.Equal(0xFF010203u, session.Document.Active.At(0, 0));

        session.FlattenImage();
        Assert.Single(session.Document.Layers);
        Assert.Equal("Background", session.Document.Active.Name);

        session.DeleteActiveLayer();
        Assert.Single(session.Document.Layers);
        Assert.Contains("only layer", session.Status);
    }

    [Fact]
    public void GroupsCanBeCreatedToggledAndUngrouped()
    {
        PaintSession session = TestHarness.Session();
        session.AddLayer();

        session.GroupActiveLayer();
        Assert.Single(session.Document.Groups);
        LayerGroup group = session.Document.Active.Group!;
        Assert.Equal("Group 1", group.Name);
        Assert.True(group.Expanded);

        session.ToggleGroupExpanded(group);
        Assert.False(group.Expanded);

        session.ToggleGroupVisibility(group);
        Assert.False(group.Visible);

        session.SetGroupOpacity(group, 0.25f);
        Assert.Equal(0.25f, group.Opacity);

        session.UngroupActiveLayer();
        Assert.Empty(session.Document.Groups);
        Assert.Null(session.Document.Active.Group);
    }

    [Fact]
    public void UngroupGroupLeavesEveryMemberInPlace()
    {
        PaintSession session = TestHarness.Session();
        session.GroupActiveLayer();
        LayerGroup group = session.Document.Groups[0];

        session.UngroupGroup(group);

        Assert.Empty(session.Document.Groups);
        Assert.All(session.Document.Layers, layer => Assert.Null(layer.Group));
    }

    [Fact]
    public void LayersCanBeReorderedMovedIntoGroupsAndBackOut()
    {
        PaintSession session = TestHarness.Session(8, 8);
        session.Document.Layers.Clear();
        session.Document.Layers.Add(new PaintLayer("A", 8, 8));
        session.Document.Layers.Add(new PaintLayer("B", 8, 8));
        session.Document.Layers.Add(new PaintLayer("C", 8, 8));
        session.Document.ActiveIndex = 0;

        session.MoveLayerTo(0, 2, above: true);
        Assert.Equal(["B", "C", "A"], session.Document.Layers.Select(layer => layer.Name));
        Assert.Equal(2, session.Document.ActiveIndex);

        session.GroupActiveLayer();
        LayerGroup group = session.Document.Groups[0];
        session.MoveLayerIntoGroup(0, group);
        PaintLayer moved = session.Document.Layers.Single(candidate => candidate.Name == "B");
        Assert.Same(group, moved.Group);
        Assert.True(group.Expanded);

        int movedIndex = session.Document.Layers.IndexOf(moved);
        session.MoveLayerToRoot(movedIndex);
        PaintLayer remaining = session.Document.Layers.Single(candidate => candidate.Name == "A");
        session.MoveLayerToRoot(session.Document.Layers.IndexOf(remaining));
        Assert.Null(moved.Group);
        Assert.Null(remaining.Group);
        Assert.Empty(session.Document.Groups);
    }

    [Fact]
    public void SelectGroupTargetsTheTopLayerInThatGroup()
    {
        PaintSession session = TestHarness.Session(8, 8);
        session.Document.Layers.Clear();
        PaintLayer bottom = new("Grouped 1", 8, 8);
        PaintLayer middle = new("Ungrouped", 8, 8);
        PaintLayer top = new("Grouped 2", 8, 8);
        LayerGroup group = new("Group");
        bottom.Group = group;
        top.Group = group;
        session.Document.Layers.Add(bottom);
        session.Document.Layers.Add(middle);
        session.Document.Layers.Add(top);

        session.SelectGroup(group);

        Assert.Equal(2, session.Document.ActiveIndex);
    }

    [Fact]
    public void LayerMasksCanBeAddedEditedAndRemoved()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer layer = session.Document.Active;

        session.ToggleLayerMask();
        Assert.True(layer.HasMask);
        Assert.True(session.MaskEditing);

        session.ToggleMaskEditing(session.Document.ActiveIndex);
        Assert.False(session.MaskEditing);
        session.ToggleMaskEditing(session.Document.ActiveIndex);
        Assert.True(session.MaskEditing);

        session.Tool = PaintTool.Eraser;
        session.BrushSize = 4f;
        session.PointerDown(4f, 4f);
        session.PointerUp(4f, 4f);

        Assert.True(layer.MaskAt((4 * layer.Width) + 4) < 255);
        Assert.Equal(0u, layer.At(4, 4));

        session.ToggleLayerMask();
        Assert.False(layer.HasMask);
        Assert.False(session.MaskEditing);
    }

    [Fact]
    public void ClippingAndLocksAreSessionLevelProperties()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer layer = session.Document.Active;

        session.ToggleClipToBelow();
        Assert.True(layer.ClipToBelow);

        session.ToggleLock(session.Document.ActiveIndex);
        Assert.True(layer.Locked);

        session.SetLayerOpacity(0.4f);
        Assert.Equal(0.4f, layer.Opacity);

        session.SetLayerBlend(BlendMode.Screen);
        Assert.Equal(BlendMode.Screen, layer.Blend);
        Assert.Equal("Screen", session.Status);
    }

    [Fact]
    public void LayerContextMenuActionsRunAgainstTheTargetRow()
    {
        PaintSession session = TestHarness.Session();
        int count = session.Document.Layers.Count;

        session.OpenLayerMenu(session.Document.ActiveIndex, null, 12f, 20f);
        Assert.True(session.IsLayerMenuOpen);
        session.LayerMenuAction("duplicate");

        Assert.False(session.IsLayerMenuOpen);
        Assert.Equal(count + 1, session.Document.Layers.Count);
        Assert.Contains("copy", session.Document.Active.Name);

        session.GroupActiveLayer();
        LayerGroup group = session.Document.Groups[0];
        session.OpenLayerMenu(-1, group, 12f, 20f);
        session.LayerMenuAction("expand");
        Assert.False(group.Expanded);

        session.OpenLayerMenu(-1, group, 12f, 20f);
        session.LayerMenuAction("ungroup");
        Assert.Empty(session.Document.Groups);
    }

    [Fact]
    public void EmptyLayerMenuCanStillCreateALayer()
    {
        PaintSession session = TestHarness.Session();
        int count = session.Document.Layers.Count;

        session.OpenLayerMenu(-1, null, 10f, 10f);
        session.LayerMenuAction("newLayer");

        Assert.Equal(count + 1, session.Document.Layers.Count);
    }

    [Fact]
    public void TogglingVisibilityAndLockIsPerLayer()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer first = session.Document.Layers[0];
        PaintLayer second = session.Document.Layers[1];

        session.ToggleLayer(0);
        session.ToggleLock(1);

        Assert.False(first.Visible);
        Assert.True(second.Locked);
        Assert.False(first.Locked);
    }
}
