using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class ColourToolsTests
{
    [Fact]
    public void ThresholdProducesBlackOrWhiteWhileKeepingAlpha()
    {
        PaintLayer layer = new("Layer", 4, 1);
        layer.Put(0, 0, Rgba.Pack(128, 200, 200, 200));
        layer.Put(1, 0, Rgba.Pack(64, 40, 40, 40));
        layer.Put(2, 0, Rgba.FromRgb(255, 255, 255));
        layer.Put(3, 0, Rgba.Transparent);

        ColourTools.Threshold(layer, null, 128);

        Assert.Equal(0x80FFFFFFu, layer.At(0, 0));
        Assert.Equal(0x40000000u, layer.At(1, 0));
        Assert.Equal(0xFFFFFFFFu, layer.At(2, 0));
        Assert.Equal(0u, layer.At(3, 0));
    }

    [Fact]
    public void GradientMapMapsLuminanceBetweenTwoColours()
    {
        PaintLayer layer = new("Layer", 3, 1);
        layer.Put(0, 0, Rgba.FromRgb(0, 0, 0));
        layer.Put(1, 0, Rgba.FromRgb(128, 128, 128));
        layer.Put(2, 0, Rgba.FromRgb(255, 255, 255));

        ColourTools.GradientMap(layer, null, Rgba.FromRgb(255, 0, 0), Rgba.FromRgb(0, 0, 255));

        Assert.Equal(0xFFFF0000u, layer.At(0, 0));
        Assert.InRange(Rgba.R(layer.At(1, 0)), 127, 128);
        Assert.InRange(Rgba.B(layer.At(1, 0)), 127, 128);
        Assert.Equal(0xFF0000FFu, layer.At(2, 0));
    }

    [Fact]
    public void ReplaceColourOnlyTouchesPixelsInsideTolerance()
    {
        PaintLayer layer = new("Layer", 3, 1);
        layer.Put(0, 0, Rgba.FromRgb(250, 10, 10));
        layer.Put(1, 0, Rgba.FromRgb(10, 250, 10));
        layer.Put(2, 0, Rgba.Transparent);

        ColourTools.ReplaceColour(layer, null, Rgba.FromRgb(255, 0, 0), Rgba.FromRgb(0, 0, 255), 10);

        Assert.Equal(0xFF0000FFu, layer.At(0, 0));
        Assert.Equal(0xFF0AFA0Au, layer.At(1, 0));
        Assert.Equal(0u, layer.At(2, 0));
    }

    [Fact]
    public void ColourBalanceShiftsMidtonesAndChannelMixCanSwapChannels()
    {
        PaintLayer balance = new("Balance", 1, 1);
        balance.Put(0, 0, Rgba.FromRgb(128, 128, 128));
        float[] offsets = new float[9];
        offsets[3] = 100f;
        ColourTools.ColourBalance(balance, null, offsets);
        Assert.True(Rgba.R(balance.At(0, 0)) > Rgba.G(balance.At(0, 0)));

        PaintLayer mix = new("Mix", 1, 1);
        mix.Put(0, 0, Rgba.FromRgb(0, 0, 255));
        float[] channels = [0f, 0f, 100f, 0f, 100f, 0f, 0f, 0f, 100f];
        ColourTools.ChannelMix(mix, null, channels);
        Assert.Equal(0xFFFF00FFu, mix.At(0, 0));
    }

    [Fact]
    public void SelectiveColourUsesTheSelectedRange()
    {
        PaintLayer layer = new("Layer", 1, 1);
        layer.Put(0, 0, Rgba.FromRgb(255, 0, 0));
        float[] offsets = new float[36];
        offsets[0] = 50f;

        ColourTools.SelectiveColour(layer, null, offsets);

        Assert.InRange(Rgba.R(layer.At(0, 0)), 126, 128);
        Assert.Equal(0, Rgba.G(layer.At(0, 0)));
    }

    [Fact]
    public void RemoveRedEyeNeutralisesRedPupils()
    {
        PaintLayer layer = new("Layer", 2, 1);
        layer.Put(0, 0, Rgba.FromRgb(240, 30, 30));
        layer.Put(1, 0, Rgba.FromRgb(200, 200, 30));

        ColourTools.RemoveRedEye(layer, null, 1f);

        Assert.Equal(30, Rgba.R(layer.At(0, 0)));
        Assert.Equal(200, Rgba.R(layer.At(1, 0)));
    }

    [Fact]
    public void SplitChannelsAddsThreeGreyscaleLayers()
    {
        PaintDocument document = new(2, 2);
        PaintLayer source = new("Source", 2, 2);
        source.Fill(Rgba.FromRgb(255, 64, 32));
        document.Layers.Add(source);
        document.ActiveIndex = 0;

        ColourTools.SplitChannels(document, source, null);

        Assert.Equal(4, document.Layers.Count);
        Assert.Equal("Red Channel", document.Layers[1].Name);
        Assert.Equal("Green Channel", document.Layers[2].Name);
        Assert.Equal("Blue Channel", document.Layers[3].Name);
        Assert.Equal(0xFFFFFFFFu, document.Layers[1].At(0, 0));
        Assert.Equal(0xFF404040u, document.Layers[2].At(0, 0));
        Assert.Equal(0xFF202020u, document.Layers[3].At(0, 0));
        Assert.Equal(3, document.ActiveIndex);
    }

    [Fact]
    public void ColourToolsRespectSelections()
    {
        PaintLayer layer = new("Layer", 2, 1);
        layer.Fill(Rgba.FromRgb(200, 200, 200));

        ColourTools.Threshold(layer, new Selection(0, 0, 1, 1), 128);

        Assert.Equal(0xFFFFFFFFu, layer.At(0, 0));
        Assert.Equal(0xFFC8C8C8u, layer.At(1, 0));
    }
}
