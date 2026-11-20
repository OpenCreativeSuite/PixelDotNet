using PixelDotNet.Paint;

namespace PixelDotNet.Tests;

public sealed class TransformTests
{
    [Fact]
    public void PerspectiveIdentityPreservesTheImage()
    {
        PaintLayer layer = new("Layer", 8, 8);
        TestHarness.FillPattern(layer);
        uint[] source = layer.CapturePixels();
        float[] offsets = new float[8];

        bool applied = PerspectiveOps.Transform(layer, source, null, offsets);

        Assert.True(applied);
        Assert.Equal(source[(3 * 8) + 3], layer.At(3, 3));
        Assert.Equal(source[(7 * 8) + 7], layer.At(7, 7));
    }

    [Fact]
    public void PerspectiveOffsetsMoveContent()
    {
        PaintLayer layer = new("Layer", 8, 8);
        layer.Fill(Rgba.FromRgb(255, 0, 0));
        layer.Put(3, 4, Rgba.FromRgb(0, 0, 255));
        uint[] source = layer.CapturePixels();
        float[] offsets = [2f, 0f, 2f, 0f, 2f, 0f, 2f, 0f];

        Assert.True(PerspectiveOps.Transform(layer, source, null, offsets));
        Assert.Equal(0xFF0000FFu, layer.At(5, 4));
    }

    [Fact]
    public void PerspectiveRespectsTheSelectionBounds()
    {
        PaintLayer layer = new("Layer", 8, 8);
        TestHarness.FillPattern(layer);
        uint[] source = layer.CapturePixels();
        Selection selection = new(0, 0, 4, 4);
        float[] offsets = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];

        Assert.True(PerspectiveOps.Transform(layer, source, selection, offsets));
        Assert.Equal(source[(6 * 8) + 6], layer.At(6, 6));
    }

    [Fact]
    public void PerspectiveRejectsADegenerateQuad()
    {
        PaintLayer layer = new("Layer", 4, 4);
        uint[] source = layer.CapturePixels();
        float[] offsets =
        [
            0f, 0f,
            -4f, 0f,
            -4f, -4f,
            0f, -4f,
        ];

        Assert.False(PerspectiveOps.Transform(layer, source, null, offsets));
    }

    [Fact]
    public void MeshWarpIdentityKeepsPixels()
    {
        PaintLayer layer = new("Layer", 9, 9);
        TestHarness.FillPattern(layer);
        uint[] source = layer.CapturePixels();
        float[] offsets = new float[MeshWarpOps.Points * MeshWarpOps.Points * 2];

        bool wrote = MeshWarpOps.Transform(layer, source, null, offsets);

        Assert.True(wrote);
        Assert.Equal(source[(4 * 9) + 4], layer.At(4, 4));
    }

    [Fact]
    public void MeshWarpMovesPixelsWithTheControlPoint()
    {
        PaintLayer layer = new("Layer", 9, 9);
        layer.Fill(Rgba.FromRgb(255, 0, 0));
        uint[] source = layer.CapturePixels();
        float[] offsets = new float[18];
        offsets[8] = 3f;
        offsets[9] = 3f;

        Assert.True(MeshWarpOps.Transform(layer, source, null, offsets));
        Assert.True(TestHarness.Differs(source, layer.Pixels));
    }

    [Fact]
    public void MeshWarpRejectsShortOffsetArrays()
    {
        PaintLayer layer = new("Layer", 4, 4);
        Assert.False(MeshWarpOps.Transform(layer, layer.CapturePixels(), null, new float[2]));
    }

    [Fact]
    public void ImageScaleUpsamplesAndDownsamples()
    {
        uint[] source = [0xFFFF0000u];
        uint[] up = ImageOps.Scale(source, 1, 1, 2, 2);

        Assert.Equal(4, up.Length);
        Assert.All(up, pixel => Assert.Equal(0xFFFF0000u, pixel));

        uint[] twoByTwo = [0xFFFF0000u, 0xFF0000FFu, 0xFFFF0000u, 0xFF0000FFu];
        uint[] down = ImageOps.Scale(twoByTwo, 2, 2, 1, 1);

        Assert.Single(down);
        Assert.InRange(Rgba.R(down[0]), 100, 155);
        Assert.InRange(Rgba.B(down[0]), 100, 155);
    }
}
