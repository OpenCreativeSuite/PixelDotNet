using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class AdjustmentTests
{
    [Fact]
    public void LevelsAdjustmentMapsThroughTheLut()
    {
        PaintLayer layer = new("Layer", 4, 4, kind: LayerKind.Adjustment, adjustment: AdjustmentKind.Levels)
        {
            AdjustmentBlack = 64f,
            AdjustmentWhite = 192f,
            AdjustmentGamma = 1f,
        };
        uint[] composite = [0xFF000000, 0xFF404040, 0xFF808080, 0xFFC0C0C0, 0xFFFFFFFF];

        AdjustmentOps.Apply(composite, layer);

        Assert.Equal(0, Rgba.R(composite[0]));
        Assert.InRange(Rgba.R(composite[1]), 0, 1);
        Assert.InRange(Rgba.R(composite[2]), 127, 128);
        Assert.InRange(Rgba.R(composite[3]), 254, 255);
        Assert.Equal(255, Rgba.R(composite[4]));
    }

    [Fact]
    public void CurvesAdjustmentUsesTheEditablePoints()
    {
        PaintLayer layer = new("Layer", 4, 4, kind: LayerKind.Adjustment, adjustment: AdjustmentKind.Curves);
        layer.AdjustmentCurve.Clear();
        layer.AdjustmentCurve.Add((0f, 0f));
        layer.AdjustmentCurve.Add((1f, 0.5f));
        uint[] composite = [0xFFFFFFFF];

        AdjustmentOps.Apply(composite, layer);

        Assert.InRange(Rgba.R(composite[0]), 127, 128);
    }

    [Fact]
    public void AdjustmentOpacityMaskAndClippingScaleTheEffect()
    {
        PaintLayer layer = new("Layer", 4, 4, kind: LayerKind.Adjustment, adjustment: AdjustmentKind.Levels)
        {
            AdjustmentBlack = 0f,
            AdjustmentWhite = 128f,
            Opacity = 0.5f,
        };
        layer.AddMask();
        Array.Fill(layer.Mask!, (byte)255);
        uint[] pixels = [0xFF808080];

        AdjustmentOps.ApplyToPixels(pixels, layer);

        Assert.InRange(Rgba.R(pixels[0]), 190, 192);

        layer.ClipToBelow = true;
        uint[] clipped = [0x40808080];
        AdjustmentOps.ApplyToPixels(clipped, layer);
        Assert.Equal(0x40, Rgba.A(clipped[0]));
    }
}

public sealed class EffectTests
{
    [Fact]
    public void EveryEffectChangesTheLayerWithinTheSelectionOnly()
    {
        PaintLayer layer = new("Layer", 16, 16);
        TestHarness.FillPattern(layer);
        uint[] source = layer.CapturePixels();
        Selection selection = new(2, 2, 6, 6);

        foreach (EffectKind effect in Effects.All)
        {
            TestHarness.FillPattern(layer);
            uint[] before = layer.CapturePixels();
            EffectOps.Apply(layer, source, selection, effect, Effects.RangeOf(effect).Default);

            Assert.True(
                TestHarness.Differs(before, layer.Pixels),
                $"{effect} should modify pixels inside the selection. " +
                $"before={before[(2 * 16) + 2]:X8} after={layer.At(2, 2):X8}");
            Assert.Equal(before[0], layer.At(0, 0));
            Assert.Equal(before[(15 * 16) + 15], layer.At(15, 15));
        }
    }

    [Fact]
    public void PixelateAveragesEachCell()
    {
        PaintLayer layer = new("Layer", 8, 8);
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                layer.Put(x, y, ((x + y) % 2 == 0) ? Rgba.FromRgb(0, 0, 0) : Rgba.FromRgb(255, 255, 255));
            }
        }

        uint[] source = layer.CapturePixels();
        EffectOps.Apply(layer, source, null, EffectKind.Pixelate, 4f);

        Assert.Equal(layer.At(0, 0), layer.At(3, 3));
        Assert.Equal(layer.At(4, 0), layer.At(7, 3));
        Assert.InRange(Rgba.R(layer.At(0, 0)), 100, 155);
    }

    [Fact]
    public void PosterizeQuantizesToTheRequestedLevels()
    {
        PaintLayer layer = new("Layer", 1, 1);
        layer.Put(0, 0, Rgba.FromRgb(128, 128, 128));
        uint[] source = layer.CapturePixels();

        EffectOps.Apply(layer, source, null, EffectKind.Posterize, 4f);

        Assert.Equal(170, Rgba.R(layer.At(0, 0)));
    }

    [Fact]
    public void EffectMetadataCoversEveryKind()
    {
        Assert.Equal(10, Effects.All.Length);
        Assert.Equal(Effects.All.Length, Effects.All.Distinct().Count());
        Assert.DoesNotContain(EffectKind.None, Effects.All);

        foreach (EffectKind effect in Effects.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(Effects.NameOf(effect)));
            (float min, float max, float value, string suffix) = Effects.RangeOf(effect);
            Assert.True(min <= value && value <= max);
            Assert.False(string.IsNullOrWhiteSpace(suffix));
            Assert.False(string.IsNullOrWhiteSpace(Effects.AmountLabel(effect)));
        }
    }
}
