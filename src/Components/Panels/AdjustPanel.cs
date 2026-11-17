using System;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>
/// Live adjustments for the active layer: a histogram, tone sliders and the
/// one-shot colour operations.
/// </summary>
/// <remarks>
/// The sliders rebuild the layer from the snapshot taken on the first change, so
/// dragging never compounds and the result is exactly the slider values applied to
/// the original pixels.
/// </remarks>
public sealed class AdjustPanel : Component
{
    private const int HistogramWidth = 288;
    private const int HistogramHeight = 68;

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        bool locked = editor.Document.Active.Locked;

        return F(
            Ui.Section("Histogram",
                Div(new() { ClassName = "histogram-frame" },
                    Canvas(new CanvasProps
                    {
                        ClassName = "histogram",
                        Attributes = Ui.Size(HistogramWidth, HistogramHeight),
                        OnDraw = context => DrawHistogram(context, editor),
                    }),
                    Div(new() { ClassName = "histogram-ramp" }))),

            Ui.Section("Tone",
                Ui.Slider("Brightness", editor.AdjustBrightness, -100f, 100f, 1f, string.Empty, value =>
                {
                    editor.AdjustBrightness = Ui.Parse(value, -100f, 100f);
                    Apply(editor);
                }),
                Ui.Slider("Contrast", editor.AdjustContrast, -100f, 100f, 1f, string.Empty, value =>
                {
                    editor.AdjustContrast = Ui.Parse(value, -100f, 100f);
                    Apply(editor);
                }),
                Div(new() { ClassName = "section-split" }),
                Ui.Slider("Hue", editor.AdjustHue, -180f, 180f, 1f, "°", value =>
                {
                    editor.AdjustHue = Ui.Parse(value, -180f, 180f);
                    Apply(editor);
                }),
                Ui.Slider("Saturation", editor.AdjustSaturation, -100f, 100f, 1f, string.Empty, value =>
                {
                    editor.AdjustSaturation = Ui.Parse(value, -100f, 100f);
                    Apply(editor);
                }),
                Ui.Slider("Lightness", editor.AdjustLightness, -100f, 100f, 1f, string.Empty, value =>
                {
                    editor.AdjustLightness = Ui.Parse(value, -100f, 100f);
                    Apply(editor);
                }),
                Div(new() { ClassName = "row" },
                    Ui.Button("Reset", "Return the sliders to neutral", editor.ResetAdjustments, editor.HasAdjustments))),

            Ui.Section("Colour",
                Div(new() { ClassName = "row" },
                    Ui.ButtonIcon(Icons.Invert, "Invert", "Invert the layer or the marquee", editor.Invert, !locked),
                    Ui.ButtonIcon(Icons.Desaturate, "Mono", "Remove colour from the layer or the marquee", editor.Desaturate, !locked)),
                Div(new() { ClassName = "row" },
                    Ui.ButtonIcon(Icons.Flatten, "Flatten Image", "Merge every visible layer into one", editor.FlattenImage))),

            Span(new() { ClassName = "panel-note" },
                Text("Sliders edit the active layer, or only the marquee when there is a selection.")));
    }

    /// <summary>Pushes a slider change into the layer and re-renders the panels.</summary>
    private static void Apply(PaintSession editor)
    {
        editor.TouchAdjustments();
        editor.Refresh?.Invoke();
    }

    /// <summary>Draws the composited luminance histogram with a square-root scale.</summary>
    private static void DrawHistogram(CanvasContext context, PaintSession editor)
    {
        const int Buckets = 64;
        const float Width = HistogramWidth;
        const float Height = HistogramHeight;

        context.Clear(0xFF1A1C20);
        int[] counts = editor.Histogram;

        int peak = 1;
        for (int i = 0; i < Buckets; i++)
        {
            peak = Math.Max(peak, counts[i]);
        }

        float columnWidth = Width / Buckets;
        for (int i = 0; i < Buckets; i++)
        {
            float height = (Height - 6f) * MathF.Sqrt(counts[i] / (float)peak);
            if (height < 0.75f)
            {
                continue;
            }

            // Brighter tones on the right, matching the ramp under the plot.
            int shade = 96 + (i * 150 / (Buckets - 1));
            context.FillRect(i * columnWidth, Height - height - 2f, columnWidth + 0.5f, height, Rgba.FromRgb((byte)shade, (byte)shade, (byte)Math.Min(255, shade + 8)));
        }

        for (int i = 1; i < 4; i++)
        {
            context.FillRect(0f, (Height / 4f * i), Width, 1f, 0x14FFFFFF);
        }
    }
}
