using System;

namespace PixelDotNet.Paint;

/// <summary>Which text colour the colour panel is editing.</summary>
public enum TextColorTarget
{
    /// <summary>The glyph fill, using the ordinary foreground colour.</summary>
    Fill,

    /// <summary>The outline drawn around the glyphs.</summary>
    Outline,

    /// <summary>The shadow or glow behind the glyphs.</summary>
    Shadow,
}

/// <summary>A ready-made combination of outline and shadow settings.</summary>
public enum TextPreset
{
    /// <summary>Plain text with no effects.</summary>
    None,

    /// <summary>A solid outline around the fill.</summary>
    Outline,

    /// <summary>A soft drop shadow.</summary>
    Shadow,

    /// <summary>A centred soft halo.</summary>
    Glow,

    /// <summary>A bright neon halo plus a thin dark keyline.</summary>
    Neon,

    /// <summary>A hard offset shadow with an outline, for a sticker look.</summary>
    Retro,

    /// <summary>The user has changed a setting since the last preset.</summary>
    Custom,
}

/// <summary>The outline and shadow settings used when text is rasterised.</summary>
/// <param name="Outline">Whether the outline is drawn.</param>
/// <param name="OutlineWidth">Outline thickness in document pixels.</param>
/// <param name="OutlineColor">Opaque colour of the outline.</param>
/// <param name="Shadow">Whether the shadow is drawn.</param>
/// <param name="ShadowOffsetX">Horizontal shadow offset in pixels.</param>
/// <param name="ShadowOffsetY">Vertical shadow offset in pixels.</param>
/// <param name="ShadowBlur">Shadow softness in pixels; zero is a hard edge.</param>
/// <param name="ShadowOpacity">Shadow opacity in the 0-100 range.</param>
/// <param name="ShadowColor">Opaque colour of the shadow.</param>
public readonly record struct TextEffectStyle(
    bool Outline,
    float OutlineWidth,
    uint OutlineColor,
    bool Shadow,
    int ShadowOffsetX,
    int ShadowOffsetY,
    float ShadowBlur,
    int ShadowOpacity,
    uint ShadowColor)
{
    /// <summary>Gets whether an outline will actually be drawn.</summary>
    public bool HasOutline => Outline && OutlineWidth >= 0.5f;

    /// <summary>Gets whether a shadow will actually be drawn.</summary>
    public bool HasShadow => Shadow && ShadowOpacity > 0;

    /// <summary>Gets the shadow colour with its opacity folded into the alpha channel.</summary>
    public uint ShadowTint =>
        Rgba.WithAlpha(Math.Clamp(ShadowOpacity, 0, 100) * 255 / 100, ShadowColor);

    /// <summary>Gets the extra margin the effects need around the glyph coverage.</summary>
    public int Padding
    {
        get
        {
            float padding = 0f;
            if (HasOutline)
            {
                padding += OutlineWidth;
            }

            if (HasShadow)
            {
                padding += ShadowBlur + MathF.Max(MathF.Abs(ShadowOffsetX), MathF.Abs(ShadowOffsetY));
            }

            return (int)MathF.Ceiling(padding) + 2;
        }
    }
}

/// <summary>The coverage layers produced for one line of styled text.</summary>
/// <param name="Fill">Coverage of the glyph fill, row major.</param>
/// <param name="Outline">Coverage of the outline, or null when disabled.</param>
/// <param name="Shadow">Coverage of the shadow, or null when disabled.</param>
/// <param name="Width">Layer width in pixels.</param>
/// <param name="Height">Layer height in pixels.</param>
/// <param name="OriginX">Layer left, relative to the text origin.</param>
/// <param name="OriginY">Layer top, relative to the text origin (the baseline).</param>
public sealed record TextArtwork(
    byte[] Fill,
    byte[]? Outline,
    byte[]? Shadow,
    int Width,
    int Height,
    int OriginX,
    int OriginY);
