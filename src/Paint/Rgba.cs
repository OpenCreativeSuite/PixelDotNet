using System;

namespace PixelDotNet.Paint;

/// <summary>How a layer is mixed into the layers below it.</summary>
public enum BlendMode
{
    /// <summary>Plain source-over.</summary>
    Normal,

    /// <summary>Darkens by multiplying the channels.</summary>
    Multiply,

    /// <summary>Lightens by screening the channels.</summary>
    Screen,

    /// <summary>Multiplies the darks and screens the lights.</summary>
    Overlay,

    /// <summary>Keeps the darker of the two channels.</summary>
    Darken,

    /// <summary>Keeps the lighter of the two channels.</summary>
    Lighten,

    /// <summary>Adds the channels and clips.</summary>
    LinearDodge,
}

/// <summary>
/// Colour helpers for the framework's packed <c>0xAARRGGBB</c> layout.
/// </summary>
/// <remarks>
/// Raster layers keep straight (non-premultiplied) alpha so an erased pixel on an
/// upper layer simply lets the pixel below show through. The compositor keeps that
/// alpha as well, so a document with nothing behind the paint stays transparent and
/// the canvas can draw its checker grid through it.
/// </remarks>
public static class Rgba
{
    /// <summary>The opaque white the compositor starts from.</summary>
    public const uint White = 0xFFFFFFFF;

    /// <summary>A fully transparent pixel.</summary>
    public const uint Transparent = 0x00000000;

    /// <summary>Packs four 0-255 channels into <c>0xAARRGGBB</c>.</summary>
    public static uint Pack(int a, int r, int g, int b) =>
        ((uint)(a & 0xFF) << 24) | ((uint)(r & 0xFF) << 16) | ((uint)(g & 0xFF) << 8) | (uint)(b & 0xFF);

    /// <summary>Builds an opaque colour from three 0-255 channels.</summary>
    public static uint FromRgb(int r, int g, int b) => Pack(255, r, g, b);

    /// <summary>Builds an opaque colour from hue, saturation and value in the 0-1 range.</summary>
    public static uint FromHsv(float hue, float saturation, float value)
    {
        float h = ((hue % 1f) + 1f) % 1f * 6f;
        float s = Math.Clamp(saturation, 0f, 1f);
        float v = Math.Clamp(value, 0f, 1f);
        int sector = (int)h;
        float f = h - sector;
        float p = v * (1f - s);
        float q = v * (1f - (s * f));
        float t = v * (1f - (s * (1f - f)));

        (float r, float g, float b) = sector switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };

        return FromRgb((int)(r * 255f), (int)(g * 255f), (int)(b * 255f));
    }

    /// <summary>Splits a colour into hue, saturation and value in the 0-1 range.</summary>
    public static (float Hue, float Saturation, float Value) ToHsv(uint color)
    {
        float r = R(color) / 255f;
        float g = G(color) / 255f;
        float b = B(color) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;

        float hue = 0f;
        if (delta > 0.0001f)
        {
            if (max == r)
            {
                hue = ((g - b) / delta) % 6f;
            }
            else if (max == g)
            {
                hue = ((b - r) / delta) + 2f;
            }
            else
            {
                hue = ((r - g) / delta) + 4f;
            }

            hue /= 6f;
            if (hue < 0f)
            {
                hue += 1f;
            }
        }

        return (hue, max <= 0f ? 0f : delta / max, max);
    }

    /// <summary>Reads the alpha channel.</summary>
    public static int A(uint c) => (int)((c >> 24) & 0xFF);

    /// <summary>Reads the red channel.</summary>
    public static int R(uint c) => (int)((c >> 16) & 0xFF);

    /// <summary>Reads the green channel.</summary>
    public static int G(uint c) => (int)((c >> 8) & 0xFF);

    /// <summary>Reads the blue channel.</summary>
    public static int B(uint c) => (int)(c & 0xFF);

    /// <summary>Replaces the alpha channel and keeps the colour.</summary>
    public static uint WithAlpha(int a, uint rgb) => ((uint)(a & 0xFF) << 24) | (rgb & 0x00FFFFFFu);

    /// <summary>Forces a colour to be opaque.</summary>
    public static uint Opaque(uint rgb) => 0xFF000000u | (rgb & 0x00FFFFFFu);

    /// <summary>
    /// Composites <paramref name="src"/> over <paramref name="dst"/> with straight
    /// alpha. Both operands may be transparent; the result carries the real alpha.
    /// </summary>
    public static uint Over(uint dst, uint src, int srcAlpha)
    {
        int sa = Math.Clamp(srcAlpha, 0, 255);
        if (sa == 0)
        {
            return dst;
        }

        if (sa >= 255)
        {
            return Opaque(src);
        }

        int da = A(dst);
        int outA = sa + (da * (255 - sa) / 255);
        if (outA <= 0)
        {
            return Transparent;
        }

        int back = da * (255 - sa) / 255;
        int r = ((R(src) * sa) + (R(dst) * back)) / outA;
        int g = ((G(src) * sa) + (G(dst) * back)) / outA;
        int b = ((B(src) * sa) + (B(dst) * back)) / outA;
        return Pack(outA, r, g, b);
    }

    /// <summary>
    /// Mixes a source colour into an opaque destination with a layer blend mode,
    /// then applies the coverage. The destination always stays opaque.
    /// </summary>
    public static uint Blend(uint dst, uint src, int alpha, BlendMode mode)
    {
        int a = Math.Clamp(alpha, 0, 255);
        if (a <= 0)
        {
            return dst;
        }

        if (mode == BlendMode.Normal || a >= 255)
        {
            return Over(dst, src, a);
        }

        int sr = R(src);
        int sg = G(src);
        int sb = B(src);
        int dr = R(dst);
        int dg = G(dst);
        int db = B(dst);

        int br = Mix(sr, dr, mode);
        int bg = Mix(sg, dg, mode);
        int bb = Mix(sb, db, mode);

        return Pack(
            255,
            ((br * a) + (dr * (255 - a))) / 255,
            ((bg * a) + (dg * (255 - a))) / 255,
            ((bb * a) + (db * (255 - a))) / 255);
    }

    /// <summary>Interpolates two colours in sRGB space.</summary>
    public static uint Lerp(uint from, uint to, float t)
    {
        float k = Math.Clamp(t, 0f, 1f);
        return Pack(
            255,
            (int)MathF.Round(R(from) + ((R(to) - R(from)) * k)),
            (int)MathF.Round(G(from) + ((G(to) - G(from)) * k)),
            (int)MathF.Round(B(from) + ((B(to) - B(from)) * k)));
    }

    /// <summary>Mixes one channel pair for a blend mode.</summary>
    private static int Mix(int source, int destination, BlendMode mode) => mode switch
    {
        BlendMode.Multiply => source * destination / 255,
        BlendMode.Screen => 255 - ((255 - source) * (255 - destination) / 255),
        BlendMode.Overlay => destination < 128
            ? 2 * source * destination / 255
            : 255 - (2 * (255 - source) * (255 - destination) / 255),
        BlendMode.Darken => Math.Min(source, destination),
        BlendMode.Lighten => Math.Max(source, destination),
        BlendMode.LinearDodge => Math.Min(255, source + destination),
        _ => source,
    };
}
