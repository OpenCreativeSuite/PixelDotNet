using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class TypographyTests
{
    [Fact]
    public void FontCatalogExposesSafeDefaults()
    {
        TextFont builtIn = TextFonts.At(0);

        Assert.False(string.IsNullOrWhiteSpace(FontCatalog.Root));
        Assert.Null(FontCatalog.PreviewPath(builtIn));
        Assert.Null(FontCatalog.SamplePreview(builtIn, "Sample"));
        Assert.NotNull(Catalog());

        Assert.Throws<InvalidOperationException>(() => FontCatalog.Import(string.Empty));
    }

    [Fact]
    public void TextRasterizerMeasuresRendersAndOverlaysWhenAFontIsAvailable()
    {
        TextFont font = TextFonts.At(0);
        string regular = font.FilePath(font.Regular);
        if (!File.Exists(regular))
        {
            // Headless CI images without desktop fonts still exercise the fallback
            // paths through the higher-level text-layer tests.
            return;
        }

        Assert.True(TextRasterizer.CanRender(font, bold: false, italic: false));
        Assert.True(TextRasterizer.MeasureGlyphs(font, "Hi", 24f, false, false, out float[] advances));
        Assert.Equal(2, advances.Length);
        Assert.All(advances, advance => Assert.True(advance > 0f));

        Assert.True(TextRasterizer.Render(
            font,
            "Hi",
            24f,
            false,
            false,
            TextAlign.Left,
            out byte[] coverage,
            out int width,
            out int height,
            out _,
            out _));
        Assert.Equal(width * height, coverage.Length);
        Assert.Contains(coverage, value => value > 0);

        TextEffectStyle style = new(
            Outline: true,
            OutlineWidth: 2f,
            OutlineColor: 0xFF000000,
            Shadow: true,
            ShadowOffsetX: 2,
            ShadowOffsetY: 2,
            ShadowBlur: 3f,
            ShadowOpacity: 60,
            ShadowColor: 0xFF112233);
        Assert.True(TextRasterizer.RenderStyled(
            font,
            "Hi",
            24f,
            false,
            false,
            TextAlign.Center,
            style,
            out TextArtwork? artwork));
        Assert.NotNull(artwork);
        Assert.NotNull(artwork!.Outline);
        Assert.NotNull(artwork.Shadow);
        Assert.Equal(artwork.Width * artwork.Height, artwork.Fill.Length);

        PaintLayer layer = new("Layer", 64, 64);
        TextRasterizer.Overlay(
            layer,
            null,
            artwork.Fill,
            artwork.Width,
            artwork.Height,
            artwork.Width * 0.5f,
            artwork.Height * 0.5f,
            0f,
            Rgba.FromRgb(255, 0, 0));

        Assert.Contains(layer.Pixels, pixel => Rgba.A(pixel) > 0 && Rgba.R(pixel) > 0);
    }

    private static IReadOnlyList<TextFont> Catalog()
    {
        try
        {
            return FontCatalog.LoadCached();
        }
        catch (Exception error)
        {
            Assert.Fail(error.ToString());
            return [];
        }
    }
}
