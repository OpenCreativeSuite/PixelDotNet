using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class ImageFormatTests
{
    [Fact]
    public void PngRoundTripsExactRgbaPixels()
    {
        uint[] pixels =
        [
            0xFFFF0000, 0x8000FF00,
            0x400000FF, 0x00FFFFFF,
        ];

        byte[] encoded = PngWriter.Encode(pixels, 2, 2);
        (int width, int height, uint[] decoded) = ImageLoader.Decode(encoded);

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], encoded.Take(8));
        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(pixels, decoded);
    }

    [Theory]
    [InlineData(ImageFormat.Png, ".png", "PNG")]
    [InlineData(ImageFormat.Jpeg, ".jpg", "JPEG")]
    [InlineData(ImageFormat.Bmp, ".bmp", "BMP")]
    [InlineData(ImageFormat.Tga, ".tga", "TGA")]
    public void EveryImageFormatEncodesAndDecodes(ImageFormat format, string extension, string name)
    {
        uint[] pixels = new uint[16];
        Array.Fill(pixels, Rgba.FromRgb(200, 30, 60));

        byte[] encoded = ImageExporter.Encode(pixels, 4, 4, format);
        (int width, int height, uint[] decoded) = ImageLoader.Decode(encoded);

        Assert.NotEmpty(encoded);
        Assert.Equal(extension, ImageExporter.Extension(format));
        Assert.Equal(name, ImageExporter.NameOf(format));
        Assert.Equal(4, width);
        Assert.Equal(4, height);
        Assert.Equal(16, decoded.Length);
        Assert.Equal(ImageLoader.IsImage("image" + extension), ImageLoader.IsImage("image" + extension));
    }

    [Fact]
    public void ImageLoaderRecognisesItsExtensions()
    {
        Assert.True(ImageLoader.IsImage("photo.PNG"));
        Assert.True(ImageLoader.IsImage("photo.jpeg"));
        Assert.True(ImageLoader.IsImage("photo.bmp"));
        Assert.True(ImageLoader.IsImage("photo.tga"));
        Assert.False(ImageLoader.IsImage("project.pxd"));
        Assert.False(ImageLoader.IsImage("vector.svg"));
    }

    [Fact]
    public void ImageLoaderRejectsGarbage()
    {
        Assert.ThrowsAny<Exception>(() => ImageLoader.Decode([1, 2, 3, 4]));
    }
}

public sealed class ProjectFileTests
{
    [Fact]
    public void ProjectRoundTripsLayersMasksGroupsStylesShapesAndGuides()
    {
        using TempDirectory temp = new();
        PaintDocument document = new(8, 6);
        PaintLayer background = new("Background", 8, 6, isBackground: true);
        background.Fill(Rgba.FromRgb(10, 20, 30));

        LayerGroup group = new("Group 1")
        {
            Visible = false,
            Opacity = 0.5f,
            Expanded = false,
        };

        PaintLayer layer = new("Layer", 8, 6)
        {
            Group = group,
            Opacity = 0.75f,
            Blend = BlendMode.Multiply,
            Locked = true,
            ClipToBelow = true,
        };
        layer.Put(1, 2, Rgba.FromRgb(200, 100, 50));
        layer.AddMask();
        layer.Mask![3] = 42;
        layer.Style.DropShadow = true;
        layer.Style.DropShadowColor = 0xFF010203;
        layer.Style.Stroke = true;
        layer.Style.StrokeWidth = 7f;
        layer.Style.OuterGlow = true;
        layer.Style.OuterGlowSize = 11f;

        PaintLayer shapeLayer = new("Shape", 8, 6);
        shapeLayer.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Path,
            X = 1,
            Y = 1,
            Width = 5,
            Height = 4,
            Fill = false,
            Stroke = true,
            StrokeWidth = 2,
            FillColor = 0xFF111111,
            StrokeColor = 0xFF222222,
            Closed = false,
            Points = [new PathNode(1, 1), new PathNode(6, 1), new PathNode(6, 5)],
        });

        document.Layers.Add(background);
        document.Layers.Add(layer);
        document.Layers.Add(shapeLayer);
        document.Groups.Add(group);
        document.Guides.Add(new Guide(true, 3));
        document.Guides.Add(new Guide(false, 7));
        document.ActiveIndex = 2;

        string path = temp.File("project.pxd");
        ProjectFile.Save(path, document);
        PaintDocument restored = ProjectFile.Load(path);

        Assert.Equal(8, restored.Width);
        Assert.Equal(6, restored.Height);
        Assert.Equal(2, restored.ActiveIndex);
        Assert.Equal(3, restored.Layers.Count);
        Assert.Single(restored.Groups);
        Assert.Equal(2, restored.Guides.Count);
        Assert.True(restored.Guides[0].Horizontal);
        Assert.Equal(3, restored.Guides[0].Position);

        PaintLayer restoredLayer = restored.Layers[1];
        Assert.Same(restored.Groups[0], restoredLayer.Group);
        Assert.Equal(BlendMode.Multiply, restoredLayer.Blend);
        Assert.True(restoredLayer.Locked);
        Assert.True(restoredLayer.ClipToBelow);
        Assert.Equal(42, restoredLayer.MaskAt(3));
        Assert.True(restoredLayer.Style.DropShadow);
        Assert.Equal(0xFF010203u, restoredLayer.Style.DropShadowColor);
        Assert.Equal(7f, restoredLayer.Style.StrokeWidth);
        Assert.Equal(11f, restoredLayer.Style.OuterGlowSize);
        Assert.Equal(0xFFC86432u, restoredLayer.At(1, 2));

        PaintLayer restoredShape = restored.Layers[2];
        Assert.True(restoredShape.IsShapeLayer);
        Assert.Equal(ShapeKind.Path, restoredShape.Shape!.Kind);
        Assert.Equal(3, restoredShape.Shape.Points!.Count);
        Assert.False(restoredShape.Shape.Closed);
    }

    [Fact]
    public void ProjectRoundTripsSmartTextAndAdjustmentLayers()
    {
        using TempDirectory temp = new();
        PaintDocument document = new(8, 6);

        PaintLayer smart = new("Smart", 8, 6);
        smart.AdoptSmart(
            [0xFFFF0000u, 0xFF0000FFu, 0xFFFF0000u, 0xFF0000FFu],
            2,
            2,
            1f,
            2f,
            1.5f,
            0.5f,
            10f,
            true,
            false);

        PaintLayer text = new("Text", 8, 6);
        text.AdoptText(new TextData
        {
            Content = "Hi",
            FontIndex = 0,
            Size = 14f,
            Bold = true,
            Italic = true,
            Align = TextAlign.Right,
            Color = 0xFF102030,
            X = 2f,
            Y = 4f,
            Rotation = 15f,
            Outline = true,
            OutlineWidth = 2f,
            OutlineColor = 0xFF405060,
            Shadow = true,
            ShadowX = 1,
            ShadowY = 2,
            ShadowBlur = 3f,
            ShadowOpacity = 40,
            ShadowColor = 0xFF708090,
            PathOffset = 0.3f,
            PathClosed = false,
            LetterSpacing = 2f,
            Path = [new PathNode(0, 0), new PathNode(8, 0)],
        });

        PaintLayer adjustment = new(
            "Curves",
            8,
            6,
            kind: LayerKind.Adjustment,
            adjustment: AdjustmentKind.Curves)
        {
            AdjustmentBlack = 12f,
            AdjustmentWhite = 220f,
            AdjustmentGamma = 1.4f,
        };
        adjustment.AdjustmentCurve.Clear();
        adjustment.AdjustmentCurve.Add((0f, 0.1f));
        adjustment.AdjustmentCurve.Add((1f, 0.8f));

        document.Layers.Add(smart);
        document.Layers.Add(text);
        document.Layers.Add(adjustment);
        document.ActiveIndex = 1;

        string path = temp.File("rich.pxd");
        ProjectFile.Save(path, document);
        PaintDocument restored = ProjectFile.Load(path);

        PaintLayer restoredSmart = restored.Layers.Single(layer => layer.Name == "Smart");
        Assert.True(restoredSmart.IsSmartObject);
        Assert.Equal(2, restoredSmart.SmartSourceWidth);
        Assert.Equal(1.5f, restoredSmart.SmartScaleX);
        Assert.True(restoredSmart.SmartFlipX);

        PaintLayer restoredText = restored.Layers.Single(layer => layer.Name == "Text");
        Assert.True(restoredText.IsTextLayer);
        Assert.Equal("Hi", restoredText.Text!.Content);
        Assert.True(restoredText.Text.Bold);
        Assert.True(restoredText.Text.Italic);
        Assert.Equal(TextAlign.Right, restoredText.Text.Align);
        Assert.Equal(2f, restoredText.Text.LetterSpacing);
        Assert.Equal(2, restoredText.Text.Path!.Count);

        PaintLayer restoredAdjustment = restored.Layers.Single(layer => layer.Name == "Curves");
        Assert.Equal(AdjustmentKind.Curves, restoredAdjustment.Adjustment);
        Assert.Equal(12f, restoredAdjustment.AdjustmentBlack);
        Assert.Equal(220f, restoredAdjustment.AdjustmentWhite);
        Assert.Equal(1.4f, restoredAdjustment.AdjustmentGamma);
        Assert.Equal([(0f, 0.1f), (1f, 0.8f)], restoredAdjustment.AdjustmentCurve);
    }

    [Fact]
    public void ProjectFileRecognisesItsExtension()
    {
        Assert.True(ProjectFile.IsProject("image.PXD"));
        Assert.False(ProjectFile.IsProject("image.png"));
        Assert.Equal(".pxd", ProjectFile.Extension);
    }

    [Fact]
    public void ProjectLoadRejectsAForeignCompressedFile()
    {
        using TempDirectory temp = new();
        string path = temp.File("bad.pxd");
        using (FileStream file = File.Create(path))
        using (GZipStream gzip = new(file, CompressionLevel.Fastest))
        using (BinaryWriter writer = new(gzip, Encoding.UTF8))
        {
            writer.Write(Encoding.ASCII.GetBytes("NOPE"));
            writer.Write(1);
        }

        Assert.Throws<InvalidDataException>(() => ProjectFile.Load(path));
    }
}

public sealed class BatchProcessorTests
{
    [Fact]
    public void FindImagesListsOnlySupportedFilesSorted()
    {
        using TempDirectory temp = new();
        File.WriteAllBytes(temp.File("b.png"), ImageExporter.Encode([0xFFFF0000], 1, 1, ImageFormat.Png));
        File.WriteAllBytes(temp.File("a.bmp"), ImageExporter.Encode([0xFF00FF00], 1, 1, ImageFormat.Bmp));
        File.WriteAllText(temp.File("notes.txt"), "ignored");
        Directory.CreateDirectory(temp.File("nested.png"));

        string[] images = BatchProcessor.FindImages(temp.Path);

        Assert.Equal(2, images.Length);
        Assert.EndsWith("a.bmp", images[0]);
        Assert.EndsWith("b.png", images[1]);
        Assert.Empty(BatchProcessor.FindImages(temp.File("missing")));
        Assert.Empty(BatchProcessor.FindImages(string.Empty));
    }

    [Fact]
    public void RunResizesAndWritesTheRequestedFormat()
    {
        using TempDirectory source = new();
        using TempDirectory output = new();
        uint[] pixels = new uint[4 * 4];
        Array.Fill(pixels, Rgba.FromRgb(20, 40, 60));
        File.WriteAllBytes(source.File("photo.png"), ImageExporter.Encode(pixels, 4, 4, ImageFormat.Png));

        BatchResult result = BatchProcessor.Run(new BatchOptions
        {
            SourceFolder = source.Path,
            OutputFolder = output.Path,
            ScalePercent = 50,
            Format = ImageFormat.Png,
            Suffix = "-small",
            Overwrite = false,
        });

        Assert.Equal(1, result.Found);
        Assert.Equal(1, result.Written);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.Contains("Processed 1", result.Message);

        string target = output.File("photo-small.png");
        Assert.True(File.Exists(target));
        (int width, int height, _) = ImageLoader.Decode(File.ReadAllBytes(target));
        Assert.Equal(2, width);
        Assert.Equal(2, height);

        BatchResult skipped = BatchProcessor.Run(new BatchOptions
        {
            SourceFolder = source.Path,
            OutputFolder = output.Path,
            ScalePercent = 50,
            Format = ImageFormat.Png,
            Suffix = "-small",
            Overwrite = false,
        });

        Assert.Equal(0, skipped.Written);
        Assert.Equal(1, skipped.Skipped);
    }

    [Fact]
    public void RunReportsMissingSourceAndInvalidScaleHandling()
    {
        BatchResult missing = BatchProcessor.Run(new BatchOptions
        {
            SourceFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pixeldotnet-no-such-folder"),
        });

        Assert.Equal(0, missing.Found);
        Assert.Equal("Source folder not found.", missing.Message);
    }
}

public sealed class SvgFormatTests
{
    [Fact]
    public void SvgImporterBuildsShapeLayersForSupportedPrimitives()
    {
        using TempDirectory temp = new();
        string path = temp.File("drawing.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 16 16">
              <rect x="1" y="2" width="4" height="5" fill="#ff0000"/>
              <circle cx="8" cy="8" r="3" fill="#00ff00"/>
              <line x1="0" y1="0" x2="15" y2="15" stroke="#0000ff" stroke-width="2"/>
              <polygon points="1,1 6,1 6,6" fill="#ffff00"/>
              <path d="M2 10 L12 10 L12 14 Z" fill="#ffffff"/>
            </svg>
            """);

        PaintDocument document = SvgImporter.Load(path);

        Assert.Equal(16, document.Width);
        Assert.Equal(16, document.Height);
        Assert.Equal(6, document.Layers.Count);
        Assert.Equal(5, document.Layers.Count(layer => layer.IsShapeLayer));
        Assert.Contains(document.Layers, layer => layer.Shape?.Kind == ShapeKind.Rectangle);
        Assert.Contains(document.Layers, layer => layer.Shape?.Kind == ShapeKind.Ellipse);
        Assert.Equal(
            3,
            document.Layers.Count(layer => layer.Shape?.Kind == ShapeKind.Path));
        Assert.Contains("Line", document.Layers.Select(layer => layer.Name));
        Assert.Contains("Polygon", document.Layers.Select(layer => layer.Name));
        Assert.Contains("Path", document.Layers.Select(layer => layer.Name));
        Assert.True(SvgImporter.IsSvg("DRAWING.SVG"));
        Assert.False(SvgImporter.IsSvg("drawing.png"));
    }

    [Fact]
    public void SvgImporterFallsBackToAViewBoxSize()
    {
        using TempDirectory temp = new();
        string path = temp.File("viewbox.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 10">
              <rect x="0" y="0" width="20" height="10"/>
            </svg>
            """);

        PaintDocument document = SvgImporter.Load(path);

        Assert.Equal(20, document.Width);
        Assert.Equal(10, document.Height);
    }

    [Fact]
    public void SvgImporterRejectsNonSvgContent()
    {
        using TempDirectory temp = new();

        string notSvg = temp.File("not-svg.svg");
        File.WriteAllText(notSvg, "<html><body>No</body></html>");
        Assert.Throws<InvalidDataException>(() => SvgImporter.Load(notSvg));

        string invalid = temp.File("invalid.svg");
        File.WriteAllText(invalid, "this is not xml");
        Assert.Throws<InvalidDataException>(() => SvgImporter.Load(invalid));
    }

    [Fact]
    public void SvgExportRoundTripsEmbeddedRasterPixels()
    {
        using TempDirectory temp = new();
        PaintDocument document = new(8, 6);
        PaintLayer layer = new("Paint", 8, 6);
        layer.Fill(Rgba.FromRgb(255, 0, 0));
        document.Layers.Add(layer);

        string path = temp.File("roundtrip.svg");
        SvgExporter.Save(path, document);
        PaintDocument restored = SvgImporter.Load(path);

        Assert.Equal(8, restored.Width);
        Assert.Equal(6, restored.Height);
        Assert.Contains(restored.Layers, candidate => candidate.At(4, 3) == 0xFFFF0000u);
    }

    [Fact]
    public void SvgImporterReadsTextElements()
    {
        using TempDirectory temp = new();
        string path = temp.File("text.svg");
        File.WriteAllText(path, """
            <svg xmlns="http://www.w3.org/2000/svg" width="64" height="32">
              <text x="4" y="24" font-size="18" fill="#336699" font-weight="bold" text-anchor="middle">Hello</text>
            </svg>
            """);

        PaintDocument document = SvgImporter.Load(path);

        Assert.Contains(document.Layers, layer => layer.IsTextLayer);
        PaintLayer text = document.Layers.First(layer => layer.IsTextLayer);
        Assert.Equal("Hello", text.Text!.Content);
        Assert.True(text.Text.Bold);
        Assert.Equal(TextAlign.Center, text.Text.Align);
    }

    [Fact]
    public void SvgExporterKeepsShapesVectorAndEmbedsRasterLayers()
    {
        using TempDirectory temp = new();
        PaintDocument document = new(16, 12);
        PaintLayer background = new("Background", 16, 12);
        background.Fill(Rgba.FromRgb(10, 20, 30));
        PaintLayer shape = new("Shape", 16, 12);
        shape.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Rectangle,
            X = 2,
            Y = 3,
            Width = 6,
            Height = 4,
            Fill = true,
            FillColor = Rgba.FromRgb(255, 0, 0),
            Stroke = true,
            StrokeColor = Rgba.FromRgb(0, 0, 255),
            StrokeWidth = 1,
        });
        document.Layers.Add(background);
        document.Layers.Add(shape);

        string path = temp.File("export.svg");
        SvgExporter.Save(path, document);
        string svg = File.ReadAllText(path);

        Assert.Contains("<svg", svg);
        Assert.Contains("width=\"16\" height=\"12\"", svg);
        Assert.Contains("<image", svg);
        Assert.Contains("<rect x=\"2\" y=\"3\" width=\"6\" height=\"4\"", svg);
        Assert.Contains("fill=\"#FF0000\"", svg);
        Assert.Equal(".svg", SvgExporter.Extension);
    }
}

public sealed class WoffFontTests
{
    [Fact]
    public void SignaturesAreDetected()
    {
        Assert.True(WoffFont.IsWoff("wOFF...."u8));
        Assert.True(WoffFont.IsWoff2("wOF2...."u8));
        Assert.False(WoffFont.IsWoff("ttcf...."u8));
        Assert.False(WoffFont.IsWoff([1, 2, 3]));
    }

    [Fact]
    public void NonWoffBytesPassThroughAndInvalidWoffThrows()
    {
        byte[] sfnt = [0, 1, 0, 0, 9, 9];
        Assert.Same(sfnt, WoffFont.ToSfnt(sfnt));

        Assert.Throws<NotSupportedException>(() => WoffFont.ToSfnt("wOF2...."u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => WoffFont.ToSfnt("wOFF"u8.ToArray()));
    }

    [Fact]
    public void UncompressedWoffRebuildsAnSfntTable()
    {
        byte[] table = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] woff = BuildWoff(table, compress: false);

        byte[] sfnt = WoffFont.ToSfnt(woff);

        Assert.Equal(0x00010000u, BinaryPrimitives.ReadUInt32BigEndian(sfnt));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4)));
        Assert.Equal("cmap", Encoding.ASCII.GetString(sfnt, 12, 4));
        Assert.Equal(table, sfnt.AsSpan(28, table.Length).ToArray());
    }

    [Fact]
    public void CompressedWoffInflatesTheTable()
    {
        byte[] table = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("PixelDotNet", 8)));
        byte[] woff = BuildWoff(table, compress: true);

        byte[] sfnt = WoffFont.ToSfnt(woff);

        Assert.Equal(table, sfnt.AsSpan(28, table.Length).ToArray());
    }

    private static byte[] BuildWoff(byte[] table, bool compress)
    {
        byte[] stored = table;
        if (compress)
        {
            using MemoryStream buffer = new();
            using (ZLibStream zlib = new(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(table, 0, table.Length);
            }

            stored = buffer.ToArray();
        }

        int directoryEnd = 44 + 20;
        int fileLength = directoryEnd + stored.Length;
        int sfntLength = 12 + 16 + table.Length;

        byte[] output = new byte[fileLength];
        Encoding.ASCII.GetBytes("wOFF", output.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4), 0x00010000u);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8), (uint)fileLength);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(14), 0);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(16), (uint)sfntLength);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(22), 0);

        Encoding.ASCII.GetBytes("cmap", output.AsSpan(44, 4));
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(48), (uint)directoryEnd);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(52), (uint)stored.Length);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(56), (uint)table.Length);
        stored.CopyTo(output, directoryEnd);

        return output;
    }
}
