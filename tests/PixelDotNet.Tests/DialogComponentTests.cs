using Donkeh.Testing;
using PixelDotNet.Components;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab._;

namespace PixelDotNet.Tests;

public sealed class LayerMenuTests
{
    [Fact]
    public void LayerMenuListsRowActionsAndRunsThem()
    {
        PaintSession session = TestHarness.Session();
        int layers = session.Document.Layers.Count;
        session.OpenLayerMenu(session.Document.ActiveIndex, null, 20f, 30f);

        var page = TestHarness.Render(session, C<LayerMenu>());

        Assert.NotEmpty(page.FindAll(".file-menu-item"));
        Assert.Contains(page.FindAll(".file-menu-label"), label => label.Text == "Duplicate Layer");

        page.FindByText("Duplicate Layer").Click();

        Assert.Equal(layers + 1, session.Document.Layers.Count);
        Assert.False(session.IsLayerMenuOpen);
    }

    [Fact]
    public void GroupMenuAndEmptyMenuHaveTheirOwnActions()
    {
        PaintSession session = TestHarness.Session();
        session.GroupActiveLayer();
        LayerGroup group = session.Document.Groups[0];
        session.OpenLayerMenu(-1, group, 10f, 10f);

        var page = TestHarness.Render(session, C<LayerMenu>());
        page.FindByText("Collapse Group").Click();

        Assert.False(group.Expanded);

        session.OpenLayerMenu(-1, null, 10f, 10f);
        page = TestHarness.Render(session, C<LayerMenu>());
        int layers = session.Document.Layers.Count;
        page.FindByText("New Layer").Click();

        Assert.Equal(layers + 1, session.Document.Layers.Count);
    }
}

public sealed class FileDialogTests
{
    [Fact]
    public void SaveDialogShowsFormatsAndFixesTheExtension()
    {
        PaintSession session = TestHarness.Session();
        session.ShowSaveDialog();
        var page = TestHarness.Render(session, C<FileDialog>());

        Assert.NotNull(page.TryFind(".file-panel"));
        Assert.Equal(4, page.FindAll(".file-formats .btn").Count);
        Assert.EndsWith(".png", page.Find(".file-name").Value);

        page.FindByText("JPEG").Click();
        Assert.Equal(ImageFormat.Jpeg, session.FileDialogFormat);
        Assert.EndsWith(".jpg", session.FileDialogName);

        page.Input(".file-name", "poster");
        Assert.Equal("poster", session.FileDialogName);

        page.Click("[title='Close the file browser']");
        Assert.False(session.IsFileDialogOpen);
    }

    [Fact]
    public void OpenDialogListsRowsAndSelectsFiles()
    {
        using TempDirectory temp = new();
        File.WriteAllBytes(
            temp.File("photo.png"),
            ImageExporter.Encode([0xFFFF0000, 0xFF00FF00], 2, 1, ImageFormat.Png));

        PaintSession session = TestHarness.Session();
        session.ShowOpenDialog();
        session.FileDialogNavigate(temp.Path);
        var page = TestHarness.Render(session, C<FileDialog>());

        Assert.Single(page.FindAll(".file-row"));
        Assert.Equal(string.Empty, page.Find(".file-name").Value);
        page.Find(".file-row").Click();

        Assert.Equal("photo.png", session.FileDialogName);
        Assert.EndsWith("photo.png", session.FileDialogSelected);
    }

    [Fact]
    public void FileContextMenuAndPromptRender()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session();
        session.ShowSaveDialog();
        session.FileDialogNavigate(temp.Path);
        session.OpenFileContextMenu(null, 20f, 20f);
        var page = TestHarness.Render(session, C<FileDialog>());

        Assert.NotNull(page.TryFind(".file-menu"));
        Assert.Contains(page.FindAll(".file-menu-label"), label => label.Text == "New folder");

        page.FindByText("New folder").Click();
        Assert.True(session.IsFilePromptOpen);

        page = TestHarness.Render(session, C<FileDialog>());
        Assert.NotNull(page.TryFind(".file-prompt"));
        page.Input(".file-prompt-input", "Created");
        page.FindByText("Apply").Click();

        Assert.True(Directory.Exists(temp.File("Created")));
        Assert.False(session.IsFilePromptOpen);
    }
}

public sealed class BatchDialogTests
{
    [Fact]
    public void BatchDialogEditsOptionsAndRuns()
    {
        using TempDirectory source = new();
        using TempDirectory output = new();
        File.WriteAllBytes(
            source.File("image.png"),
            ImageExporter.Encode(new uint[16], 4, 4, ImageFormat.Png));

        PaintSession session = TestHarness.Session();
        session.ShowBatchDialog();
        session.BatchSetSource(source.Path);
        session.BatchSetOutput(output.Path);
        var page = TestHarness.Render(session, C<BatchDialog>());

        Assert.NotNull(page.TryFind(".batch-panel"));
        Assert.Equal(5, page.FindAll(".batch-formats .btn").Count);

        page.FindByText("JPEG").Click();
        Assert.Equal(ImageFormat.Jpeg, session.BatchFormat);

        page.FindAll(".resize-input").First(field => field.Value == "100").Input("50");
        page.FindAll(".resize-input").First(field => field.Value == "-edited").Input("-out");
        Assert.Equal("50", session.BatchScaleText);
        Assert.Equal("-out", session.BatchSuffix);

        page.FindByText("Run").Click();
        Assert.Contains("Processed 1", session.BatchStatus);
        Assert.True(File.Exists(output.File("image-out.jpg")));

        page.FindByText("Close").Click();
        Assert.False(session.IsBatchOpen);
    }

    [Fact]
    public void BatchValidationReportsAnInvalidScale()
    {
        PaintSession session = TestHarness.Session();
        session.ShowBatchDialog();
        session.BatchSetScale("0");
        var page = TestHarness.Render(session, C<BatchDialog>());

        page.FindByText("Run").Click();

        Assert.Contains("scale", session.BatchStatus);
    }
}

public sealed class ResizeDialogTests
{
    [Fact]
    public void ImageResizeFieldsStayInSync()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShowImageResizeDialog();
        var page = TestHarness.Render(session, C<ResizeDialog>());

        Assert.Equal("Image Size", page.Find(".resize-title").Text);
        Assert.Contains("Current: 32 × 24 px", page.Find(".resize-current").Text);

        page.FindAll(".resize-input")[0].Input("64");
        Assert.Equal("48", session.ResizeHeightText);

        page.FindByText("Keep aspect ratio").Click();
        Assert.False(session.ResizeKeepAspect);

        page.FindByText("Cancel").Click();
        Assert.False(session.IsResizeOpen);
    }

    [Fact]
    public void CanvasDialogShowsAnchorGridAndApplies()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShowCanvasResizeDialog();
        var page = TestHarness.Render(session, C<ResizeDialog>());

        Assert.Equal("Canvas Size", page.Find(".resize-title").Text);
        Assert.Equal(9, page.FindAll(".resize-anchor").Count);

        page.Find("[title='Anchor BottomRight']").Click();
        Assert.Equal(ResizeAnchor.BottomRight, session.ResizeAnchor);

        page.FindAll(".resize-input")[0].Input("40");
        page.FindByText("Resize").Click();

        Assert.Equal(40, session.Document.Width);
        Assert.False(session.IsResizeOpen);
    }
}

public sealed class ColourToolsDialogTests
{
    [Fact]
    public void EveryColourSectionRendersAndApplies()
    {
        PaintSession session = TestHarness.Session(8, 8);
        session.Document.Active.Fill(Rgba.FromRgb(128, 128, 128));
        session.ShowColourToolsDialog();
        var page = TestHarness.Render(session, C<ColourToolsDialog>());

        Assert.True(page.FindAll(".style-section").Count >= 8);
        Assert.Equal(
            ["Colour Balance", "Threshold", "Gradient Map", "Replace Colour", "Red-Eye Removal", "Channel Mixer", "Selective Colour"],
            page.FindAll(".style-section-title").Take(7).Select(title => title.Text));

        TestElement threshold = page.FindAll(".style-section")[1];
        threshold.Find(".slider-input").Input("200");
        Assert.Equal(200, session.ThresholdLevel);
        threshold.Find(".style-section-head .btn").Click();
        Assert.Equal(0, Rgba.R(session.Document.Active.At(0, 0)));

        page.FindByText("Split").Click();
        Assert.Equal(5, session.Document.Layers.Count);

        page.FindAll(".btn").First(button => button.Text == "Done").Click();
        Assert.False(session.IsColourToolsOpen);
    }
}

public sealed class LayerStyleDialogTests
{
    [Fact]
    public void StyleSectionsToggleAndEditValues()
    {
        PaintSession session = TestHarness.Session();
        PaintLayer layer = session.Document.Active;
        session.ShowLayerStyleDialog(session.Document.ActiveIndex);
        var page = TestHarness.Render(session, C<LayerStyleDialog>());

        Assert.Equal(3, page.FindAll(".style-section").Count);
        Assert.Contains(layer.Name, page.Find(".resize-sub").Text);

        page.FindAll(".style-section .style-section-head .btn")[0].Click();
        Assert.True(layer.Style.DropShadow);

        page = TestHarness.Render(session, C<LayerStyleDialog>());
        page.FindAll(".slider-input")[0].Input("50");
        Assert.Equal(0.5f, layer.Style.DropShadowOpacity);

        page.FindAll(".style-swatch")[0].Click();
        Assert.True(layer.Style.DropShadowColor != 0);
        Assert.True(layer.Style.HasAny);

        page.FindAll(".btn").First(button => button.Text == "Done").Click();
        Assert.False(session.IsLayerStyleOpen);
    }
}

public sealed class MeshWarpBarTests
{
    [Fact]
    public void EmptyLayerShowsAWarningAndActionsCloseTheDialog()
    {
        PaintSession session = TestHarness.Session();
        session.ShowMeshWarpDialog();
        var page = TestHarness.Render(session, C<MeshWarpBar>());

        Assert.Equal("Mesh Warp", page.Find(".mesh-warp-title").Text);
        Assert.NotNull(page.TryFind(".mesh-warp-note.is-warning"));
        Assert.Equal(session.Document.Active.Name, page.Find(".mesh-warp-layer-name").Text);

        page.FindByText("Cancel").Click();
        Assert.False(session.IsMeshWarpOpen);
    }

    [Fact]
    public void ResetAndDoneKeepThePreviewedPixels()
    {
        PaintSession session = TestHarness.Session(16, 16);
        TestHarness.FillPattern(session.Document.Active);
        session.ShowMeshWarpDialog();
        var page = TestHarness.Render(session, C<MeshWarpBar>());

        Assert.Null(page.TryFind(".mesh-warp-note.is-warning"));
        page.FindByText("Reset").Click();
        page.FindByText("Done").Click();

        Assert.False(session.IsMeshWarpOpen);
    }
}

public sealed class PerspectiveDialogTests
{
    [Fact]
    public void PerspectiveSlidersPreviewResetAndCommit()
    {
        PaintSession session = TestHarness.Session(16, 16);
        TestHarness.FillPattern(session.Document.Active);
        session.ShowPerspectiveDialog();
        var page = TestHarness.Render(session, C<PerspectiveDialog>());

        Assert.Equal(8, page.FindAll(".slider-input").Count);
        page.FindAll(".slider-input")[0].Input("20");
        Assert.Equal(20f, session.PerspectiveValue(0));

        page.FindByText("Reset").Click();
        Assert.Equal(0f, session.PerspectiveValue(0));

        page.FindAll(".slider-input")[2].Input("10");
        page.FindByText("Done").Click();
        Assert.False(session.IsPerspectiveOpen);
    }

    [Fact]
    public void CancelRestoresTheLayer()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        TestHarness.FillPattern(layer);
        uint[] original = layer.CapturePixels();
        session.ShowPerspectiveDialog();
        var page = TestHarness.Render(session, C<PerspectiveDialog>());

        page.FindAll(".slider-input")[0].Input("15");
        page.FindAll(".btn").First(button => button.Text == "Cancel").Click();

        Assert.False(session.IsPerspectiveOpen);
        Assert.Equal(original, layer.Pixels);
    }
}

public sealed class ShapeEditDialogTests
{
    [Fact]
    public void ShapeControlsEditAndCommitGeometry()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShapeAsLayer = true;
        session.Tool = PaintTool.Rectangle;
        session.PointerDown(2f, 2f);
        session.PointerMove(12f, 10f);
        session.PointerUp(12f, 10f);
        PaintLayer layer = session.Document.Active;
        session.ShowShapeEditDialog();
        var page = TestHarness.Render(session, C<ShapeEditDialog>());

        Assert.Equal("Edit Shape", page.Find(".resize-title").Text);
        Assert.Equal(5, page.FindAll(".slider-input").Count);

        page.FindAll(".slider-input")[2].Input("5");
        Assert.Equal(5f, layer.Shape!.Width);

        page.FindByText("Outline").Click();
        Assert.False(layer.Shape.Stroke);

        page.FindByText("Done").Click();
        Assert.False(session.IsShapeEditOpen);
    }

    [Fact]
    public void CancelRestoresTheShape()
    {
        PaintSession session = TestHarness.Session(32, 24);
        session.ShapeAsLayer = true;
        session.Tool = PaintTool.Ellipse;
        session.PointerDown(2f, 2f);
        session.PointerMove(12f, 10f);
        session.PointerUp(12f, 10f);
        PaintLayer layer = session.Document.Active;
        session.ShowShapeEditDialog();
        var page = TestHarness.Render(session, C<ShapeEditDialog>());

        page.FindAll(".slider-input")[2].Input("4");
        Assert.Equal(4f, layer.Shape!.Width);
        page.FindByText("Cancel").Click();

        Assert.Equal(10f, layer.Shape!.Width);
        Assert.False(session.IsShapeEditOpen);
    }
}

public sealed class SmartObjectDialogTests
{
    [Fact]
    public void TransformSlidersFlipsAndResetUpdateTheLayer()
    {
        PaintSession session = TestHarness.Session(16, 16);
        PaintLayer layer = session.Document.Active;
        layer.Fill(Rgba.FromRgb(10, 20, 30));
        session.ConvertToSmartObject();
        session.ShowSmartTransformDialog();
        var page = TestHarness.Render(session, C<SmartObjectDialog>());

        Assert.Equal(5, page.FindAll(".slider-input").Count);
        page.FindAll(".slider-input")[0].Input("5");
        Assert.Equal(5f, layer.SmartX);

        page.FindByText("Flip X").Click();
        Assert.True(layer.SmartFlipX);

        page.FindByText("Reset").Click();
        Assert.Equal(0f, layer.SmartX);
        Assert.False(layer.SmartFlipX);

        page.FindByText("Done").Click();
        Assert.False(session.IsSmartTransformOpen);
    }
}

public sealed class TextEditDialogTests
{
    [Fact]
    public void TextControlsEditTheLayer()
    {
        PaintSession session = TestHarness.Session(64, 48);
        PaintLayer textLayer = CreateTextLayer(session);
        session.ShowTextEditDialog();
        var page = TestHarness.Render(session, C<TextEditDialog>());

        Assert.Equal("Edit Text", page.Find(".resize-title").Text);
        page.Input(".text-field", "Edited");
        Assert.Equal("Edited", textLayer.Text!.Content);

        page.FindAll(".slider-input")[0].Input("30");
        Assert.Equal(30f, textLayer.Text.Size);

        page.FindByText("Bold").Click();
        Assert.True(textLayer.Text.Bold);

        page.FindByText("Done").Click();
        Assert.False(session.IsTextEditOpen);
    }

    [Fact]
    public void CancelRestoresTheText()
    {
        PaintSession session = TestHarness.Session(64, 48);
        PaintLayer textLayer = CreateTextLayer(session);
        session.ShowTextEditDialog();
        var page = TestHarness.Render(session, C<TextEditDialog>());

        page.Input(".text-field", "Changed");
        page.FindByText("Cancel").Click();

        Assert.Equal("Test", textLayer.Text!.Content);
    }

    private static PaintLayer CreateTextLayer(PaintSession session)
    {
        session.Tool = PaintTool.Text;
        session.TextAsLayer = true;
        session.BeginText(4, 30);
        session.SetText("Test");
        session.SetTextSize(20f);
        session.ApplyText();
        return session.Document.Active;
    }
}

public sealed class TooltipOverlayTests
{
    [Fact]
    public void TooltipShowsTitleHintAndHelpChevron()
    {
        PaintSession session = TestHarness.Session();
        session.ShowTooltips = true;
        session.ShowTip(new TipState(
            Icons.Brush,
            "Brush",
            "B",
            "tool.brush",
            TipPlacement.Right,
            ShowChevron: true,
            X: 10,
            Y: 20,
            Width: 30,
            Height: 30));

        var page = TestHarness.Render(session, C<TooltipOverlay>());

        Assert.NotNull(page.TryFind(".tip-float"));
        Assert.Equal("Brush", page.Find(".tip-title").Text);
        Assert.Equal("B", page.Find(".tip-key").Text);
        Assert.Contains("width:200px", page.Find(".tip-float").GetAttribute("style"));

        page.Find(".tip-more").Click();
        Assert.NotNull(session.OpenHelp);
        Assert.Equal("Brush", session.OpenHelp!.Title);
    }

    [Fact]
    public void CompactTipsHaveNoChevron()
    {
        PaintSession session = TestHarness.Session();
        session.ShowTip(new TipState(
            Icons.Brush,
            "Brush",
            string.Empty,
            "tool.brush",
            TipPlacement.Auto,
            ShowChevron: false,
            X: 10,
            Y: 20,
            Width: 30,
            Height: 30));

        var page = TestHarness.Render(session, C<TooltipOverlay>());

        Assert.Contains("width:140px", page.Find(".tip-float").GetAttribute("style"));
        Assert.Null(page.TryFind(".tip-more"));
    }
}

public sealed class HelpModalTests
{
    [Fact]
    public void HelpModalShowsTheArticleAndCloses()
    {
        PaintSession session = TestHarness.Session();
        HelpTopic topic = HelpCatalog.Find("tool.move");
        session.ShowHelp(topic);
        var page = TestHarness.Render(session, C<HelpModal>());

        Assert.Equal(topic.Title, page.Find(".help-title").Text);
        Assert.Equal(topic.Shortcut, page.Find(".help-key").Text);
        Assert.Equal(topic.Summary, page.Find(".help-summary").Text);
        Assert.Equal(topic.HowItWorks.Length, page.FindAll(".help-line").Count);
        Assert.Equal(topic.Tips.Length, page.FindAll(".help-tip").Count);

        page.FindByText("Got it").Click();
        Assert.Null(session.OpenHelp);
    }

    [Fact]
    public void UnknownHelpIdsUseTheGenericArticle()
    {
        HelpTopic topic = HelpCatalog.Find("no.such.topic");

        Assert.Equal("Help", topic.Title);
        Assert.NotEmpty(topic.HowItWorks);
        Assert.NotEmpty(topic.Tips);
    }
}
