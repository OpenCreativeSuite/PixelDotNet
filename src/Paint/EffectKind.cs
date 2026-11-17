using System;

namespace PixelDotNet.Paint;

/// <summary>A one-shot filter the Effects panel can preview on the active layer.</summary>
public enum EffectKind
{
    /// <summary>No effect selected.</summary>
    None,

    /// <summary>Smooths the image with a gaussian approximation.</summary>
    GaussianBlur,

    /// <summary>Smears pixels along the horizontal axis.</summary>
    MotionBlur,

    /// <summary>Averages four offset copies of the image.</summary>
    Fragment,

    /// <summary>Quantises the image into square blocks.</summary>
    Pixelate,

    /// <summary>Increases local contrast along edges.</summary>
    Sharpen,

    /// <summary>Turns edges into a raised grey relief.</summary>
    Emboss,

    /// <summary>Draws the image as glowing edges on black.</summary>
    EdgeDetect,

    /// <summary>Adds deterministic film grain.</summary>
    AddNoise,

    /// <summary>Reduces each channel to a small number of steps.</summary>
    Posterize,

    /// <summary>Darkens the corners of the layer.</summary>
    Vignette,
}

/// <summary>Names and parameter ranges for the effects, shared by the menu and the panel.</summary>
public static class Effects
{
    /// <summary>Every effect, in the order the panel lists them.</summary>
    public static readonly EffectKind[] All =
    [
        EffectKind.GaussianBlur,
        EffectKind.MotionBlur,
        EffectKind.Fragment,
        EffectKind.Pixelate,
        EffectKind.Sharpen,
        EffectKind.Emboss,
        EffectKind.EdgeDetect,
        EffectKind.AddNoise,
        EffectKind.Posterize,
        EffectKind.Vignette,
    ];

    /// <summary>Gets the display name of an effect.</summary>
    public static string NameOf(EffectKind effect) => effect switch
    {
        EffectKind.GaussianBlur => "Gaussian Blur",
        EffectKind.MotionBlur => "Motion Blur",
        EffectKind.Fragment => "Fragment",
        EffectKind.Pixelate => "Pixelate",
        EffectKind.Sharpen => "Sharpen",
        EffectKind.Emboss => "Emboss",
        EffectKind.EdgeDetect => "Edge Detect",
        EffectKind.AddNoise => "Add Noise",
        EffectKind.Posterize => "Posterize",
        EffectKind.Vignette => "Vignette",
        _ => "None",
    };

    /// <summary>Gets the slider range, default and suffix for an effect's single amount.</summary>
    public static (float Min, float Max, float Default, string Suffix) RangeOf(EffectKind effect) => effect switch
    {
        EffectKind.GaussianBlur => (1f, 24f, 6f, " px"),
        EffectKind.MotionBlur => (1f, 40f, 12f, " px"),
        EffectKind.Fragment => (1f, 20f, 6f, " px"),
        EffectKind.Pixelate => (2f, 48f, 10f, " px"),
        EffectKind.Sharpen => (0f, 100f, 50f, " %"),
        EffectKind.Emboss => (0f, 100f, 60f, " %"),
        EffectKind.EdgeDetect => (0f, 100f, 100f, " %"),
        EffectKind.AddNoise => (0f, 100f, 25f, " %"),
        EffectKind.Posterize => (2f, 32f, 8f, " levels"),
        EffectKind.Vignette => (0f, 100f, 60f, " %"),
        _ => (0f, 100f, 50f, " %"),
    };

    /// <summary>Gets the slider label for an effect's amount.</summary>
    public static string AmountLabel(EffectKind effect) => effect switch
    {
        EffectKind.GaussianBlur or EffectKind.MotionBlur or EffectKind.Fragment or EffectKind.Pixelate => "Radius",
        EffectKind.Posterize => "Levels",
        _ => "Amount",
    };
}
