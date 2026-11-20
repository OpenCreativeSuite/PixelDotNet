using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void CompositeStacksLayersBottomToTop()
    {
        PaintDocument document = new(4, 4);
        PaintLayer bottom = new("Bottom", 4, 4);
        PaintLayer top = new("Top", 4, 4);
        bottom.Fill(Rgba.FromRgb(255, 0, 0));
        top.Fill(Rgba.FromRgb(0, 0, 255));
        top.Opacity = 0.5f;
        document.Layers.Add(bottom);
        document.Layers.Add(top);
        document.MarkDirty();

        uint pixel = document.CompositeAt(2, 2);

        Assert.Equal(255, Rgba.A(pixel));
        Assert.InRange(Rgba.R(pixel), 126, 128);
        Assert.Equal(0, Rgba.G(pixel));
        Assert.InRange(Rgba.B(pixel), 127, 129);
    }

    [Fact]
    public void HiddenLayersAndMasksAreExcludedFromTheComposite()
    {
        PaintDocument document = new(4, 4);
        PaintLayer bottom = new("Bottom", 4, 4);
        PaintLayer top = new("Top", 4, 4);
        bottom.Fill(Rgba.FromRgb(255, 0, 0));
        top.Fill(Rgba.FromRgb(0, 0, 255));
        document.Layers.Add(bottom);
        document.Layers.Add(top);

        top.Visible = false;
        Assert.Equal(0xFFFF0000u, document.CompositeAt(1, 1));

        top.Visible = true;
        top.AddMask();
        Array.Fill(top.Mask!, (byte)0);
        Assert.Equal(0xFFFF0000u, document.CompositeAt(1, 1));

        Array.Fill(top.Mask!, (byte)255);
        document.MarkDirty();
        Assert.Equal(0xFF0000FFu, document.CompositeAt(1, 1));
    }

    [Fact]
    public void GroupVisibilityAndOpacityApplyToTheWholeGroup()
    {
        PaintDocument document = new(4, 4);
        PaintLayer bottom = new("Bottom", 4, 4);
        PaintLayer grouped = new("Grouped", 4, 4);
        LayerGroup group = new("Group");
        bottom.Fill(Rgba.FromRgb(255, 0, 0));
        grouped.Fill(Rgba.FromRgb(0, 0, 255));
        grouped.Group = group;
        document.Layers.Add(bottom);
        document.Layers.Add(grouped);
        document.Groups.Add(group);

        Assert.Equal(0xFF0000FFu, document.CompositeAt(1, 1));

        group.Visible = false;
        document.MarkDirty();
        Assert.Equal(0xFFFF0000u, document.CompositeAt(1, 1));

        group.Visible = true;
        group.Opacity = 0.5f;
        document.MarkDirty();
        uint half = document.CompositeAt(1, 1);
        Assert.InRange(Rgba.R(half), 126, 128);
        Assert.InRange(Rgba.B(half), 127, 129);
    }

    [Fact]
    public void AdjustmentLayersAffectLowerPixels()
    {
        PaintDocument document = new(4, 4);
        PaintLayer bottom = new("Bottom", 4, 4);
        bottom.Fill(Rgba.FromRgb(128, 128, 128));
        PaintLayer adjustment = new(
            "Levels",
            4,
            4,
            kind: LayerKind.Adjustment,
            adjustment: AdjustmentKind.Levels)
        {
            AdjustmentBlack = 128f,
            AdjustmentWhite = 255f,
        };
        document.Layers.Add(bottom);
        document.Layers.Add(adjustment);
        document.MarkDirty();

        uint pixel = document.CompositeAt(1, 1);
        Assert.Equal(0, Rgba.R(pixel));
        Assert.Equal(0, Rgba.G(pixel));
        Assert.Equal(0, Rgba.B(pixel));
        Assert.Equal(255, Rgba.A(pixel));
    }

    [Fact]
    public void RunsSegmentEachRowAndSkipTransparency()
    {
        PaintDocument document = new(4, 4);
        PaintLayer layer = new("Layer", 4, 4);
        layer.Fill(Rgba.FromRgb(0x11, 0x22, 0x33));
        document.Layers.Add(layer);
        document.MarkDirty();

        PaintRun[] runs = document.Runs;

        Assert.Equal(4, runs.Length);
        Assert.All(runs, run => Assert.Equal(4, run.Width));
        Assert.All(runs, run => Assert.Equal(0xFF112233u, run.Color));
        Assert.Equal([0, 1, 2, 3], runs.Select(run => run.Y));

        for (int i = 0; i < layer.Pixels.Length; i++)
        {
            layer.Pixels[i] = Rgba.Transparent;
        }

        document.MarkDirty();
        Assert.Empty(document.Runs);
    }

    [Fact]
    public void HistogramCountsLuminanceBuckets()
    {
        PaintDocument document = new(4, 4);
        PaintLayer layer = new("Layer", 4, 4);
        layer.Fill(Rgba.White);
        document.Layers.Add(layer);
        document.MarkDirty();

        int[] buckets = new int[64];
        document.BuildHistogram(buckets);

        Assert.Equal(16, buckets[63]);
        Assert.Equal(16, buckets.Sum());
    }

    [Fact]
    public void ActiveLayerClampsToTheStack()
    {
        PaintDocument document = TestHarness.OpaqueDocument();

        document.ActiveIndex = 99;
        Assert.Same(document.Layers[^1], document.Active);

        document.ActiveIndex = -5;
        Assert.Same(document.Layers[0], document.Active);
    }
}

public sealed class PaintLayerTests
{
    [Fact]
    public void MaskLifecycleKeepsTheBufferSizedToTheLayer()
    {
        PaintLayer layer = new("Layer", 4, 3);
        Assert.False(layer.HasMask);
        Assert.Equal(255, layer.MaskAt(0));

        layer.AddMask();
        Assert.True(layer.HasMask);
        Assert.Equal(12, layer.Mask!.Length);
        Assert.Equal(255, layer.MaskAt(5));

        byte[] replacement = new byte[12];
        replacement[0] = 7;
        layer.AdoptMask(replacement);
        Assert.Equal(7, layer.MaskAt(0));

        layer.AdoptMask(new byte[1]);
        Assert.Equal(7, layer.MaskAt(0));

        Assert.NotNull(layer.CaptureMask());
        layer.RemoveMask();
        Assert.False(layer.HasMask);
    }

    [Fact]
    public void PixelsCanBeCapturedAdoptedAndFilled()
    {
        PaintLayer layer = new("Layer", 4, 4);
        layer.Fill(Rgba.FromRgb(1, 2, 3));
        uint[] snapshot = layer.CapturePixels();

        layer.Fill(Rgba.FromRgb(9, 9, 9));
        layer.Adopt(snapshot);

        Assert.Equal(0xFF010203u, layer.At(0, 0));
        Assert.Equal(0xFF010203u, layer.At(2, 2));

        layer.Adopt(new uint[1]);
        Assert.Equal(0xFF010203u, layer.At(0, 0));
    }

    [Fact]
    public void SmartObjectsRenderTheirSourceWithTransforms()
    {
        PaintLayer layer = new("Smart", 8, 8);
        uint[] source =
        [
            0xFFFF0000u, 0xFF0000FFu,
            0xFFFF0000u, 0xFF0000FFu,
        ];

        layer.AdoptSmart(source, 2, 2, 0f, 0f, 1f, 1f, 0f, false, false);

        Assert.True(layer.IsSmartObject);
        Assert.Equal(0xFFFF0000u, layer.At(0, 0));
        Assert.Equal(0xFF0000FFu, layer.At(1, 0));

        layer.SmartFlipX = true;
        layer.RefreshSmart();
        Assert.Equal(0xFF0000FFu, layer.At(0, 0));

        layer.SmartFlipX = false;
        layer.RefreshSmart();
        layer.RasterizeSmart();
        Assert.False(layer.IsSmartObject);
        Assert.Null(layer.SmartSource);
    }

    [Fact]
    public void MakeSmartCapturesTheCurrentPixels()
    {
        PaintLayer layer = new("Layer", 4, 4);
        layer.Fill(Rgba.FromRgb(12, 34, 56));

        layer.MakeSmart();

        Assert.True(layer.IsSmartObject);
        Assert.Equal(16, layer.SmartSource!.Length);
        Assert.Equal(4, layer.SmartSourceWidth);
        Assert.Equal(4, layer.SmartSourceHeight);
        Assert.Equal(0xFF0C2238u, layer.SmartSource[0]);
    }

    [Fact]
    public void ShapeAndTextLayersKeepEditableDataUntilRasterized()
    {
        PaintLayer shapeLayer = new("Shape", 16, 16);
        shapeLayer.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Rectangle,
            X = 2,
            Y = 2,
            Width = 8,
            Height = 8,
            Fill = true,
            FillColor = Rgba.FromRgb(10, 20, 30),
        });

        Assert.True(shapeLayer.IsShapeLayer);
        Assert.Equal(0xFF0A141Eu, shapeLayer.At(5, 5));
        shapeLayer.RasterizeShape();
        Assert.False(shapeLayer.IsShapeLayer);
        Assert.Null(shapeLayer.Shape);

        PaintLayer textLayer = new("Text", 32, 32);
        textLayer.AdoptText(new TextData
        {
            Content = "A",
            Size = 24,
            Color = Rgba.FromRgb(255, 255, 255),
            X = 2,
            Y = 24,
        });

        Assert.True(textLayer.IsTextLayer);
        Assert.NotNull(textLayer.Text);
        textLayer.RasterizeText();
        Assert.False(textLayer.IsTextLayer);
        Assert.Null(textLayer.Text);
    }

    [Fact]
    public void AdjustmentSettingsCopyToAnotherLayer()
    {
        PaintLayer source = new("Source", 4, 4, kind: LayerKind.Adjustment, adjustment: AdjustmentKind.Curves);
        source.AdjustmentBlack = 10f;
        source.AdjustmentWhite = 200f;
        source.AdjustmentGamma = 1.5f;
        source.AdjustmentCurve.Clear();
        source.AdjustmentCurve.Add((0f, 0.1f));
        source.AdjustmentCurve.Add((1f, 0.9f));
        PaintLayer target = new("Target", 4, 4);

        target.CopyAdjustmentFrom(source);

        Assert.Equal(AdjustmentKind.Curves, target.Adjustment);
        Assert.Equal(10f, target.AdjustmentBlack);
        Assert.Equal(200f, target.AdjustmentWhite);
        Assert.Equal(1.5f, target.AdjustmentGamma);
        Assert.Equal([(0f, 0.1f), (1f, 0.9f)], target.AdjustmentCurve);
    }

    [Fact]
    public void LayerStyleCopiesEverySetting()
    {
        LayerStyle source = new()
        {
            DropShadow = true,
            DropShadowColor = 0xFF112233,
            DropShadowOpacity = 0.4f,
            DropShadowDistance = 12f,
            DropShadowAngle = 30f,
            DropShadowBlur = 9f,
            Stroke = true,
            StrokeColor = 0xFF445566,
            StrokeOpacity = 0.5f,
            StrokeWidth = 6f,
            StrokeInside = true,
            OuterGlow = true,
            OuterGlowColor = 0xFF778899,
            OuterGlowOpacity = 0.6f,
            OuterGlowSize = 14f,
        };

        LayerStyle copy = source.Clone();

        Assert.True(copy.HasAny);
        Assert.Equal(source.DropShadowColor, copy.DropShadowColor);
        Assert.Equal(source.DropShadowOpacity, copy.DropShadowOpacity);
        Assert.Equal(source.StrokeWidth, copy.StrokeWidth);
        Assert.Equal(source.StrokeInside, copy.StrokeInside);
        Assert.Equal(source.OuterGlowSize, copy.OuterGlowSize);
    }
}
