using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class RgbaTests
{
    [Fact]
    public void ChannelsPackInArgbOrder()
    {
        uint color = Rgba.Pack(0x12, 0x34, 0x56, 0x78);

        Assert.Equal(0x12, Rgba.A(color));
        Assert.Equal(0x34, Rgba.R(color));
        Assert.Equal(0x56, Rgba.G(color));
        Assert.Equal(0x78, Rgba.B(color));
        Assert.Equal(0xFF123456u, Rgba.FromRgb(0x12, 0x34, 0x56));
    }

    [Fact]
    public void AlphaHelpersPreserveTheColourBits()
    {
        Assert.Equal(0x80112233u, Rgba.WithAlpha(0x80, 0xFF112233));
        Assert.Equal(0xFF112233u, Rgba.Opaque(0x00112233));
    }

    [Theory]
    [InlineData(0f, 1f, 1f, 0xFF0000u)]
    [InlineData(1f / 3f, 1f, 1f, 0x00FF00u)]
    [InlineData(2f / 3f, 1f, 1f, 0x0000FFu)]
    [InlineData(0f, 0f, 0f, 0x000000u)]
    public void HsvBuildsTheExpectedPrimary(float hue, float saturation, float value, uint expectedRgb)
    {
        uint color = Rgba.FromHsv(hue, saturation, value);

        Assert.InRange(Math.Abs(Rgba.R(color) - Rgba.R(expectedRgb)), 0, 1);
        Assert.InRange(Math.Abs(Rgba.G(color) - Rgba.G(expectedRgb)), 0, 1);
        Assert.InRange(Math.Abs(Rgba.B(color) - Rgba.B(expectedRgb)), 0, 1);
    }

    [Fact]
    public void HsvRoundTripsASaturatedColour()
    {
        uint original = Rgba.FromRgb(0x33, 0x66, 0xCC);
        (float hue, float saturation, float value) = Rgba.ToHsv(original);
        uint roundTrip = Rgba.FromHsv(hue, saturation, value);

        Assert.InRange(Math.Abs(Rgba.R(original) - Rgba.R(roundTrip)), 0, 1);
        Assert.InRange(Math.Abs(Rgba.G(original) - Rgba.G(roundTrip)), 0, 1);
        Assert.InRange(Math.Abs(Rgba.B(original) - Rgba.B(roundTrip)), 0, 1);
    }

    [Fact]
    public void OverCompositesStraightAlpha()
    {
        uint red = 0xFFFF0000;
        uint blue = 0xFF0000FF;

        Assert.Equal(blue, Rgba.Over(red, blue, 255));
        Assert.Equal(red, Rgba.Over(red, blue, 0));

        uint half = Rgba.Over(red, blue, 128);
        Assert.Equal(255, Rgba.A(half));
        Assert.Equal(127, Rgba.R(half));
        Assert.Equal(0, Rgba.G(half));
        Assert.Equal(128, Rgba.B(half));
    }

    [Fact]
    public void BlendModesMixChannels()
    {
        uint grey = Rgba.FromRgb(128, 128, 128);

        Assert.Equal(64u, (uint)Rgba.R(Rgba.Blend(grey, grey, 254, BlendMode.Multiply)));
        Assert.Equal(191u, (uint)Rgba.R(Rgba.Blend(grey, grey, 254, BlendMode.Screen)));
        Assert.Equal(128u, (uint)Rgba.R(Rgba.Blend(grey, grey, 254, BlendMode.Overlay)));
        Assert.Equal(64u, (uint)Rgba.R(Rgba.Blend(Rgba.FromRgb(64, 64, 64), grey, 254, BlendMode.Darken)));
        Assert.Equal(192u, (uint)Rgba.R(Rgba.Blend(Rgba.FromRgb(192, 192, 192), grey, 254, BlendMode.Lighten)));
        Assert.Equal(254u, (uint)Rgba.R(Rgba.Blend(Rgba.FromRgb(64, 64, 64), Rgba.FromRgb(200, 200, 200), 254, BlendMode.LinearDodge)));
    }

    [Fact]
    public void LerpInterpolatesAndClamps()
    {
        uint black = Rgba.FromRgb(0, 0, 0);
        uint white = Rgba.FromRgb(255, 255, 255);

        Assert.Equal(128u, (uint)Rgba.R(Rgba.Lerp(black, white, 0.5f)));
        Assert.Equal(0u, (uint)Rgba.R(Rgba.Lerp(black, white, -1f)));
        Assert.Equal(255u, (uint)Rgba.R(Rgba.Lerp(black, white, 2f)));
    }
}
