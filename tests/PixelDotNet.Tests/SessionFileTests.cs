using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class SessionFileTests
{
    [Fact]
    public void SaveDialogWritesPngAndRenamesTheDocument()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session(8, 6);
        session.Document.Active.Fill(Rgba.FromRgb(255, 0, 0));

        session.ShowSaveDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("output");
        session.ConfirmFileDialog();

        string path = temp.File("output.png");
        Assert.True(File.Exists(path));
        Assert.False(session.IsFileDialogOpen);
        Assert.Equal("output.png", session.DocumentName);
        Assert.Equal(string.Empty, session.FileDialogError);

        (int width, int height, uint[] pixels) = ImageLoader.Load(path);
        Assert.Equal(8, width);
        Assert.Equal(6, height);
        Assert.Equal(0xFFFF0000u, pixels[0]);
    }

    [Fact]
    public void SaveProjectWritesAndReopensALayeredDocument()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session(8, 6);
        session.AddLayer();

        session.ShowSaveProjectDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("project");
        session.ConfirmFileDialog();

        string path = temp.File("project.pxd");
        Assert.True(File.Exists(path));
        Assert.Equal("project.pxd", session.DocumentName);

        session.ShowOpenProjectDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("project.pxd");
        session.ConfirmFileDialog();

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal(8, session.Document.Width);
        Assert.Equal(3, session.Document.Layers.Count);
    }

    [Fact]
    public void SvgExportWritesAVectorFile()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session(8, 6);

        session.ShowSaveSvgDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("drawing");
        session.ConfirmFileDialog();

        string path = temp.File("drawing.svg");
        Assert.True(File.Exists(path));
        Assert.Contains("<svg", File.ReadAllText(path));
        Assert.False(session.IsFileDialogOpen);
    }

    [Fact]
    public void OpenDialogLoadsAnImageIntoANewTab()
    {
        using TempDirectory temp = new();
        uint[] pixels = new uint[4];
        Array.Fill(pixels, Rgba.FromRgb(3, 2, 1));
        File.WriteAllBytes(temp.File("input.png"), ImageExporter.Encode(pixels, 2, 2, ImageFormat.Png));

        PaintSession session = TestHarness.Session();
        int tabs = session.DocumentCount;
        session.ShowOpenDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("input.png");
        session.ConfirmFileDialog();

        Assert.Equal(tabs + 1, session.DocumentCount);
        Assert.Equal(2, session.Document.Width);
        Assert.Equal(2, session.Document.Height);
        Assert.Equal(0xFF030201u, session.Document.Active.At(0, 0));
        Assert.Equal("input", session.DocumentName);
    }

    [Fact]
    public void OpenSvgDialogImportsShapeLayers()
    {
        using TempDirectory temp = new();
        File.WriteAllText(temp.File("vector.svg"), """
            <svg xmlns="http://www.w3.org/2000/svg" width="12" height="10">
              <rect x="1" y="1" width="4" height="4" fill="#ff0000"/>
            </svg>
            """);

        PaintSession session = TestHarness.Session();
        session.ShowOpenSvgDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("vector.svg");
        session.ConfirmFileDialog();

        Assert.Equal(2, session.DocumentCount);
        Assert.Equal(12, session.Document.Width);
        Assert.Equal(10, session.Document.Height);
        Assert.Contains(session.Document.Layers, layer => layer.IsShapeLayer);
    }

    [Fact]
    public void OpenDialogReportsMissingFilesAndFolders()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session();
        session.ShowOpenDialog();
        session.FileDialogNavigate(temp.Path);
        session.FileDialogSetName("missing.png");
        session.ConfirmFileDialog();

        Assert.Contains("File not found", session.FileDialogError);
        Assert.True(session.IsFileDialogOpen);

        session.FileDialogNavigate(temp.File("no-such-folder"));
        Assert.Contains("Folder not found", session.FileDialogError);
    }

    [Fact]
    public void FormatSelectionUpdatesTheSaveName()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session();
        session.ShowSaveDialog();
        session.FileDialogNavigate(temp.Path);

        session.FileDialogSetFormat(ImageFormat.Jpeg);
        Assert.EndsWith(".jpg", session.FileDialogTarget);

        session.FileDialogSetFormat(ImageFormat.Bmp);
        Assert.EndsWith(".bmp", session.FileDialogTarget);

        session.FileDialogSetName("custom.txt");
        session.FileDialogSetFormat(ImageFormat.Tga);
        Assert.Equal("custom.tga", session.FileDialogName);
        Assert.EndsWith("custom.tga", session.FileDialogTarget);
    }

    [Fact]
    public void FileRowsNavigateSelectAndUseTheContextClipboard()
    {
        using TempDirectory temp = new();
        using TempDirectory other = new();
        File.WriteAllBytes(temp.File("one.png"), ImageExporter.Encode([0xFFFF0000], 1, 1, ImageFormat.Png));
        File.WriteAllBytes(temp.File("two.png"), ImageExporter.Encode([0xFF00FF00], 1, 1, ImageFormat.Png));
        Directory.CreateDirectory(temp.File("Folder"));

        PaintSession session = TestHarness.Session();
        session.ShowOpenDialog();
        session.FileDialogNavigate(temp.Path);

        FileEntry folder = session.FileDialogEntries.Single(entry => entry.IsDirectory);
        session.FileDialogActivate(folder);
        Assert.Equal(temp.File("Folder"), session.FileDialogFolder);

        session.FileDialogGoUp();
        Assert.Equal(temp.Path, session.FileDialogFolder);

        FileEntry first = session.FileDialogEntries.First(entry => !entry.IsDirectory);
        session.FileDialogActivate(first);
        Assert.Equal(first.FullPath, session.FileDialogSelected);

        session.OpenFileContextMenu(first, 10f, 10f);
        session.FileMenuCopy();
        Assert.True(session.HasFileClipboard);
        Assert.False(session.FileClipboardIsCut);

        session.OpenFileContextMenu(null, 10f, 10f);
        session.FileMenuPaste();
        Assert.Equal(3, session.FileDialogEntries.Count(entry => !entry.IsDirectory));

        session.OpenFileContextMenu(first, 10f, 10f);
        session.FileMenuDelete();
        Assert.False(File.Exists(first.FullPath));

        session.OpenFileContextMenu(null, 10f, 10f);
        session.FileMenuNewFolder();
        session.FilePromptSetValue("Made Here");
        session.FilePromptConfirm();
        Assert.True(Directory.Exists(temp.File("Made Here")));

        FileEntry made = session.FileDialogEntries.Single(entry => entry.Name == "Made Here");
        session.OpenFileContextMenu(made, 10f, 10f);
        session.FileMenuRename();
        session.FilePromptSetValue("Renamed");
        session.FilePromptConfirm();
        Assert.True(Directory.Exists(temp.File("Renamed")));
        Assert.False(Directory.Exists(temp.File("Made Here")));
    }

    [Fact]
    public void CuttingAFileMovesItOnPaste()
    {
        using TempDirectory temp = new();
        string path = temp.File("cut.png");
        File.WriteAllBytes(path, ImageExporter.Encode([0xFFFF0000], 1, 1, ImageFormat.Png));

        PaintSession session = TestHarness.Session();
        session.ShowOpenDialog();
        session.FileDialogNavigate(temp.Path);
        FileEntry entry = session.FileDialogEntries.Single(item => item.Name == "cut.png");

        session.OpenFileContextMenu(entry, 0f, 0f);
        session.FileMenuCut();
        Assert.True(session.FileClipboardIsCut);

        session.OpenFileContextMenu(null, 0f, 0f);
        session.FileMenuPaste();

        Assert.False(File.Exists(path));
        Assert.Contains(
            session.FileDialogEntries,
            item => item.Name != "cut.png" && item.Name.EndsWith(".png"));
        Assert.False(session.HasFileClipboard);
    }

    [Fact]
    public void FilePromptValidatesNames()
    {
        using TempDirectory temp = new();
        PaintSession session = TestHarness.Session();
        session.ShowSaveDialog();
        session.FileDialogNavigate(temp.Path);
        session.OpenFileContextMenu(null, 0f, 0f);
        session.FileMenuNewFolder();
        session.FilePromptSetValue(string.Empty);
        session.FilePromptConfirm();

        Assert.Contains("Enter a name", session.FileDialogError);
        Assert.True(session.IsFilePromptOpen);

        session.FilePromptSetValue("bad/name");
        session.FilePromptConfirm();
        Assert.Contains("not allowed", session.FileDialogError);

        session.FilePromptCancel();
        Assert.False(session.IsFilePromptOpen);
    }

    [Fact]
    public void BatchDialogRunsThroughTheSession()
    {
        using TempDirectory source = new();
        using TempDirectory output = new();
        File.WriteAllBytes(
            source.File("photo.png"),
            ImageExporter.Encode(new uint[16], 4, 4, ImageFormat.Png));

        PaintSession session = TestHarness.Session();
        session.ShowBatchDialog();
        session.BatchSetSource(source.Path);
        session.BatchSetOutput(output.Path);
        session.BatchSetScale("50");
        session.BatchSetFormat(ImageFormat.Png);
        session.BatchSetSuffix("-web");
        session.BatchRefreshCount();

        Assert.Equal(1, session.BatchFound);
        Assert.Contains("1 image", session.BatchStatus);

        session.RunBatch();

        Assert.True(File.Exists(output.File("photo-web.png")));
        Assert.Contains("Processed 1", session.BatchStatus);

        session.ToggleBatchOverwrite();
        Assert.True(session.BatchOverwrite);

        session.CloseBatchDialog();
        Assert.False(session.IsBatchOpen);
    }
}
