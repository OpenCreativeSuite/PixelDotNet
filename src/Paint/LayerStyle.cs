using System;

namespace PixelDotNet.Paint;

/// <summary>
/// Non-destructive effects drawn around a layer's pixels.
/// </summary>
/// <remarks>
/// Styles are applied from the layer's effective alpha (opacity and mask
/// included). They are recomputed when the composite refreshes, so editing the
/// layer updates its shadow, stroke and glow automatically.
/// </remarks>
public sealed class LayerStyle
{
    /// <summary>Gets or sets whether a drop shadow is drawn.</summary>
    public bool DropShadow { get; set; }

    /// <summary>Gets or sets the drop shadow colour.</summary>
    public uint DropShadowColor { get; set; } = 0xFF000000;

    /// <summary>Gets or sets the drop shadow opacity in the 0-1 range.</summary>
    public float DropShadowOpacity { get; set; } = 0.7f;

    /// <summary>Gets or sets the shadow offset in pixels.</summary>
    public float DropShadowDistance { get; set; } = 8f;

    /// <summary>Gets or sets the shadow direction in degrees, 0 = right, 90 = down.</summary>
    public float DropShadowAngle { get; set; } = 45f;

    /// <summary>Gets or sets the shadow blur radius in pixels.</summary>
    public float DropShadowBlur { get; set; } = 6f;

    /// <summary>Gets or sets whether a stroke is drawn.</summary>
    public bool Stroke { get; set; }

    /// <summary>Gets or sets the stroke colour.</summary>
    public uint StrokeColor { get; set; } = 0xFFFFFFFF;

    /// <summary>Gets or sets the stroke opacity in the 0-1 range.</summary>
    public float StrokeOpacity { get; set; } = 1f;

    /// <summary>Gets or sets the stroke width in pixels.</summary>
    public float StrokeWidth { get; set; } = 3f;

    /// <summary>Gets or sets whether the stroke is drawn inside the layer edge.</summary>
    public bool StrokeInside { get; set; }

    /// <summary>Gets or sets whether an outer glow is drawn.</summary>
    public bool OuterGlow { get; set; }

    /// <summary>Gets or sets the glow colour.</summary>
    public uint OuterGlowColor { get; set; } = 0xFFFFE680;

    /// <summary>Gets or sets the glow opacity in the 0-1 range.</summary>
    public float OuterGlowOpacity { get; set; } = 0.8f;

    /// <summary>Gets or sets the glow radius in pixels.</summary>
    public float OuterGlowSize { get; set; } = 10f;

    /// <summary>Gets whether any style is enabled.</summary>
    public bool HasAny => DropShadow || Stroke || OuterGlow;

    /// <summary>Copies every setting from another style.</summary>
    public void CopyFrom(LayerStyle source)
    {
        DropShadow = source.DropShadow;
        DropShadowColor = source.DropShadowColor;
        DropShadowOpacity = source.DropShadowOpacity;
        DropShadowDistance = source.DropShadowDistance;
        DropShadowAngle = source.DropShadowAngle;
        DropShadowBlur = source.DropShadowBlur;

        Stroke = source.Stroke;
        StrokeColor = source.StrokeColor;
        StrokeOpacity = source.StrokeOpacity;
        StrokeWidth = source.StrokeWidth;
        StrokeInside = source.StrokeInside;

        OuterGlow = source.OuterGlow;
        OuterGlowColor = source.OuterGlowColor;
        OuterGlowOpacity = source.OuterGlowOpacity;
        OuterGlowSize = source.OuterGlowSize;
    }

    /// <summary>Returns an independent copy of this style.</summary>
    public LayerStyle Clone()
    {
        LayerStyle copy = new();
        copy.CopyFrom(this);
        return copy;
    }
}
