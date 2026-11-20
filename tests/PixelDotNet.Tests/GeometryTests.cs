using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class ShapeGeometryTests
{
    [Fact]
    public void ShapeCloneAndCopyAreIndependent()
    {
        ShapeData source = new()
        {
            Kind = ShapeKind.Path,
            X = 1,
            Y = 2,
            Width = 3,
            Height = 4,
            Fill = true,
            Stroke = false,
            StrokeWidth = 6,
            FillColor = 0xFF112233,
            StrokeColor = 0xFF445566,
            Closed = false,
            Points = [new PathNode(1, 2), new PathNode(3, 4)],
        };

        ShapeData clone = source.Clone();
        clone.Points![0] = new PathNode(9, 9);

        Assert.Equal(1f, source.Points[0].X);
        Assert.Equal(ShapeKind.Path, clone.Kind);
        Assert.False(clone.Stroke);
        Assert.False(clone.Closed);

        ShapeData copy = new();
        copy.CopyFrom(source);
        Assert.Equal(3f, copy.Width);
        Assert.Equal(0xFF112233u, copy.FillColor);
    }

    [Fact]
    public void PathNodeReportsPulledHandles()
    {
        PathNode corner = new(10, 20);
        Assert.False(corner.HasHandles);

        PathNode curved = new(10, 20)
        {
            OutX = 14,
            OutY = 20,
        };
        Assert.True(curved.HasHandles);
    }

    [Fact]
    public void FlattenProducesTwentyStepsPerSegment()
    {
        List<PathNode> nodes = [new(0, 0), new(10, 0), new(10, 10)];

        Assert.Equal(60, ShapeOps.Flatten(nodes, closed: true).Count);
        Assert.Equal(41, ShapeOps.Flatten(nodes, closed: false).Count);

        ShapeData shape = new() { Points = nodes };
        Assert.Equal(60, ShapeOps.Flatten(shape).Count);
    }

    [Fact]
    public void RectangleAndEllipseShapesRenderTheirInterior()
    {
        PaintLayer rectangle = new("Rect", 16, 16);
        rectangle.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Rectangle,
            X = 2,
            Y = 2,
            Width = 12,
            Height = 12,
            Fill = true,
            FillColor = Rgba.FromRgb(10, 20, 30),
            Stroke = false,
        });

        Assert.Equal(0xFF0A141Eu, rectangle.At(8, 8));

        PaintLayer ellipse = new("Ellipse", 16, 16);
        ellipse.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Ellipse,
            X = 0,
            Y = 0,
            Width = 16,
            Height = 16,
            Fill = true,
            FillColor = Rgba.FromRgb(40, 50, 60),
            Stroke = false,
        });

        Assert.NotEqual(0u, ellipse.At(8, 8));
        Assert.Equal(0u, ellipse.At(0, 0));
    }

    [Fact]
    public void LineAndPathShapesDrawStrokes()
    {
        PaintLayer line = new("Line", 16, 16);
        line.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Line,
            X = 0,
            Y = 0,
            Width = 15,
            Height = 15,
            Fill = false,
            Stroke = true,
            StrokeWidth = 2,
            StrokeColor = Rgba.White,
        });
        Assert.NotEqual(0u, line.At(8, 8));

        PaintLayer path = new("Path", 16, 16);
        path.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Path,
            X = 0,
            Y = 0,
            Width = 15,
            Height = 15,
            Fill = true,
            FillColor = Rgba.FromRgb(255, 0, 0),
            Stroke = true,
            StrokeColor = Rgba.FromRgb(0, 255, 0),
            StrokeWidth = 1,
            Points = [new PathNode(1, 1), new PathNode(14, 1), new PathNode(1, 14)],
            Closed = true,
        });

        Assert.NotEqual(0u, path.At(3, 3));
        Assert.NotEqual(0u, path.At(2, 1));
    }
}

public sealed class TextGeometryTests
{
    [Fact]
    public void TextCloneAndCopyCarryEverySetting()
    {
        TextData source = new()
        {
            Content = "Hello",
            FontIndex = 2,
            Size = 42f,
            Bold = true,
            Italic = true,
            Align = TextAlign.Center,
            Color = 0xFF112233,
            X = 4f,
            Y = 5f,
            Rotation = 12f,
            Outline = true,
            OutlineWidth = 2f,
            OutlineColor = 0xFF445566,
            Shadow = true,
            ShadowX = 2,
            ShadowY = 3,
            ShadowBlur = 4f,
            ShadowOpacity = 50,
            ShadowColor = 0xFF778899,
            PathLayer = "Path",
            PathOffset = 0.25f,
            PathClosed = false,
            LetterSpacing = 1.5f,
            Path = [new PathNode(0, 0), new PathNode(1, 1)],
        };

        TextData clone = source.Clone();
        clone.Path![0] = new PathNode(9, 9);

        Assert.Equal(0f, source.Path[0].X);
        Assert.Equal("Hello", clone.Content);
        Assert.True(clone.HasPath);

        TextData copy = new();
        copy.CopyFrom(source);
        Assert.Equal(42f, copy.Size);
        Assert.Equal(TextAlign.Center, copy.Align);
        Assert.Equal(0.25f, copy.PathOffset);
    }

    [Fact]
    public void EffectStyleDerivesPaddingAndShadowTint()
    {
        TextEffectStyle style = new(
            Outline: true,
            OutlineWidth: 3f,
            OutlineColor: 0xFF000000,
            Shadow: true,
            ShadowOffsetX: 4,
            ShadowOffsetY: 5,
            ShadowBlur: 6f,
            ShadowOpacity: 50,
            ShadowColor: 0xFF112233);

        Assert.True(style.HasOutline);
        Assert.True(style.HasShadow);
        Assert.Equal(16, style.Padding);
        Assert.Equal(0x7F112233u, style.ShadowTint);

        TextEffectStyle none = new(false, 0f, 0, false, 0, 0, 0f, 0, 0);
        Assert.False(none.HasOutline);
        Assert.False(none.HasShadow);
    }

    [Fact]
    public void ResolvePathPrefersALiveLayerThenTheFallbackCopy()
    {
        PaintDocument document = new(16, 16);
        PaintLayer pathLayer = new("Guide", 16, 16);
        pathLayer.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Path,
            Points = [new PathNode(0, 0), new PathNode(15, 0), new PathNode(15, 15)],
            Closed = false,
        });
        document.Layers.Add(pathLayer);

        TextData text = new()
        {
            PathLayer = "Guide",
            Path = [new PathNode(0, 0), new PathNode(1, 1)],
            PathClosed = true,
        };

        TextOps.TextPathSource? live = TextOps.ResolvePath(document, text);
        Assert.NotNull(live);
        Assert.Equal(3, live!.Value.Nodes.Count);
        Assert.False(live.Value.Closed);

        TextData fallback = new()
        {
            Path = [new PathNode(0, 0), new PathNode(2, 2)],
            PathClosed = true,
        };
        TextOps.TextPathSource? copy = TextOps.ResolvePath(document, fallback);
        Assert.NotNull(copy);
        Assert.Equal(2, copy!.Value.Nodes.Count);
        Assert.True(copy.Value.Closed);

        Assert.Null(TextOps.ResolvePath(null, new TextData()));
    }

    [Fact]
    public void EmptyTextClearsTheLayerCache()
    {
        PaintLayer layer = new("Text", 16, 16);
        layer.Fill(Rgba.White);
        layer.AdoptText(new TextData { Content = string.Empty, Size = 20f });

        Assert.True(layer.IsTextLayer);
        Assert.All(layer.Pixels, pixel => Assert.Equal(0u, pixel));
    }

    [Fact]
    public void TextFontTableClampsIndicesAndResolvesPaths()
    {
        Assert.NotEmpty(TextFonts.All);
        Assert.Same(TextFonts.All[0], TextFonts.At(-100));
        Assert.Same(TextFonts.All[^1], TextFonts.At(999));

        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Fonts");
        string absolute = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Absolute.ttf");
        TextFont font = new("Test", "regular.ttf", "bold.ttf", "italic.ttf", "bolditalic.ttf", folder);
        Assert.True(font.IsDownloaded);
        Assert.Equal(System.IO.Path.Combine(folder, "regular.ttf"), font.FilePath(font.Regular));
        Assert.Equal(absolute, font.FilePath(absolute));
    }
}
