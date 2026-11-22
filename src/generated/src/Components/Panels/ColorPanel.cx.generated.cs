using System;
using System.Collections.Generic;
using Donkeh.LayoutLab;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>
/// Colour panel: foreground/background pair, a saturation/value square, a hue strip,
/// RGB sliders and a fixed palette.
/// </summary>
/// <remarks>
/// The square is drawn as a grid of small cells because the canvas has no arbitrary
/// gradient primitive; four-pixel cells keep that to about two thousand rectangles
/// while still reading as a smooth ramp. The marker is painted last so it always
/// sits above the colour field.
/// </remarks>
public sealed class ColorPanel : Component
{
    private const int SquareWidth = 288;
    private const int SquareHeight = 132;
    private const int SquareCell = 4;
    private const int HueWidth = 288;
    private const int HueHeight = 12;
    private const int PerRow = 8;

    private static readonly uint[] Palette =
    [
        0xFF1A1A1A, 0xFF3F3F3F, 0xFF7F7F7F, 0xFFBFBFBF,
        0xFFF2F2F2, 0xFFE0483E, 0xFFE8883C, 0xFFE8C63C,
        0xFF5FBF56, 0xFF3AA79A, 0xFF3E7FCF, 0xFF3F4FA8,
        0xFF7B54C8, 0xFFC053A8, 0xFF8B5A3C, 0xFFFFFFFF,
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        uint active = editor.ActiveColor;
        int red = Ui.Red(active);
        int green = Ui.Green(active);
        int blue = Ui.Blue(active);
        float hue = editor.PickerHue;

        List<Node> rows = [];
        for (int start = 0; start < Palette.Length; start += PerRow)
        {
            List<Node> cells = [];
            int end = Math.Min(start + PerRow, Palette.Length);
            for (int i = start; i < end; i++)
            {
                uint swatch = Palette[i];
                cells.Add(Ui.Swatch("swatch", swatch, Ui.Hex(swatch), () => editor.SetActiveColor(swatch), 30, 20));
            }

            rows.Add(_.H("div", new() { ClassName = "swatch-row" }, cells.ToArray()));
        }

        return F(
            _.H("div", new() { ClassName = "color-head" }, _.H("div", new() { ClassName = "pair" }, Ui.Swatch("pair-back", editor.Background, "Background colour — click to swap", editor.SwapColors, 30, 30), Ui.Swatch("pair-front", editor.Foreground, "Foreground colour — click to reset", editor.ResetColors, 30, 30)), _.H("div", new() { ClassName = "color-info" }, _.H("span", new() { ClassName = editor.ColorTarget == TextColorTarget.Fill ? "color-target is-fill" : "color-target" }, Text(editor.ColorTarget == TextColorTarget.Fill ? "Foreground" : editor.ColorTargetLabel)), _.H("span", new() { ClassName = "color-hex" }, Text(Ui.Hex(active))), _.H("span", new() { ClassName = "color-sub" }, Text($"R {red}  G {green}  B {blue}")))),
            _.H("div", new() { ClassName = "color-targets" }, TargetButton(editor, TextColorTarget.Fill, "Fill"), TargetButton(editor, TextColorTarget.Outline, "Outline"), TargetButton(editor, TextColorTarget.Shadow, "Shadow")),
            _.H("div", new() { ClassName = "color-actions-row" }, _.H("button", new() { ClassName = "btn btn-ghost", Title = "Swap foreground and background (X)", OnClick = editor.SwapColors }, Ui.Icon(Icons.Swap, "btn-icon"), Text("Swap")), _.H("button", new() { ClassName = "btn btn-ghost", Title = "Default black and white (D)", OnClick = editor.ResetColors }, Ui.DefaultsGlyph(), Text("Reset"))),

            Ui.Section("Spectrum",
                _.H("div", new() { ClassName = "spectrum" }, Canvas(new CanvasProps
                    {
                        ClassName = "sv-square",
                        Attributes = Ui.Size(SquareWidth, SquareHeight),
                        OnPointerDown = (x, y) => PickSaturation(editor, hue, x, y),
                        OnDraw = context => DrawSquare(context, editor),
                    }), Canvas(new CanvasProps
                    {
                        ClassName = "hue-strip",
                        Attributes = Ui.Size(HueWidth, HueHeight),
                        OnPointerDown = (x, _) => PickHue(editor, x),
                        OnDraw = context => DrawHue(context, editor),
                    }))),

            Ui.Section("Channels",
                Ui.Slider("Red", red, 0f, 255f, 1f, string.Empty,
                    value => editor.SetActiveColor((int)Ui.Parse(value, 0f, 255f), green, blue)),
                Ui.Slider("Green", green, 0f, 255f, 1f, string.Empty,
                    value => editor.SetActiveColor(red, (int)Ui.Parse(value, 0f, 255f), blue)),
                Ui.Slider("Blue", blue, 0f, 255f, 1f, string.Empty,
                    value => editor.SetActiveColor(red, green, (int)Ui.Parse(value, 0f, 255f)))),

            Ui.Section("Swatches", _.H("div", new() { ClassName = "swatches" }, rows.ToArray())));
    }

    /// <summary>Builds one colour-target tab for the fill, outline or shadow.</summary>
    private static Node TargetButton(PaintSession editor, TextColorTarget target, string label) =>
        _.H("button", new() { ClassName = editor.ColorTarget == target ? "btn is-on" : "btn", Title = "Edit the " + label.ToLowerInvariant() + " colour", OnClick = () =>
            {
                editor.EditTextColour(target);
                editor.Refresh?.Invoke();
            } }, Text(label));

    // DrawSquare, DrawHue, PickSaturation, PickHue unchanged (omitted here for brevity? no, must include)
    /// <summary>Draws the saturation/value square and the current colour marker.</summary>
    private static void DrawSquare(CanvasContext context, PaintSession editor)
    {
        int columns = SquareWidth / SquareCell;
        int rows = SquareHeight / SquareCell;

        for (int row = 0; row < rows; row++)
        {
            float value = 1f - (row / (float)(rows - 1));
            for (int column = 0; column < columns; column++)
            {
                float saturation = column / (float)(columns - 1);
                context.FillRect(
                    column * SquareCell,
                    row * SquareCell,
                    SquareCell + 0.5f,
                    SquareCell + 0.5f,
                    Rgba.FromHsv(editor.PickerHue, saturation, value));
            }
        }

        (float _, float markerSaturation, float markerValue) = Rgba.ToHsv(editor.ActiveColor);
        float x = markerSaturation * (SquareWidth - 1);
        float y = (1f - markerValue) * (SquareHeight - 1);

        context.FillEllipse(new Rect(x - 6f, y - 6f, 12f, 12f), 0xCC000000);
        context.FillEllipse(new Rect(x - 5f, y - 5f, 10f, 10f), 0xFFFFFFFF);
        context.FillEllipse(new Rect(x - 3.5f, y - 3.5f, 7f, 7f), Rgba.Opaque(editor.ActiveColor));
    }

    /// <summary>Draws the hue strip and the current hue marker.</summary>
    private static void DrawHue(CanvasContext context, PaintSession editor)
    {
        const int Segments = 90;
        float width = HueWidth / (float)Segments;

        for (int i = 0; i < Segments; i++)
        {
            context.FillRect(
                i * width,
                0f,
                width + 0.5f,
                HueHeight,
                Rgba.FromHsv(i / (float)Segments, 1f, 1f));
        }

        float x = Math.Clamp(editor.PickerHue, 0f, 0.999f) * HueWidth;
        context.FillRect(x - 2f, -1f, 4f, HueHeight + 2f, 0xCC000000);
        context.FillRect(x - 0.75f, 0f, 1.5f, HueHeight, 0xFFFFFFFF);
    }

    /// <summary>Reads a click in the square as saturation and value.</summary>
    private static void PickSaturation(PaintSession editor, float hue, float x, float y)
    {
        float saturation = Math.Clamp(x / SquareWidth, 0f, 1f);
        float value = 1f - Math.Clamp(y / SquareHeight, 0f, 1f);
        editor.SetHsv(hue, saturation, value);
    }

    /// <summary>Reads a click in the hue strip as a hue rotation.</summary>
    private static void PickHue(PaintSession editor, float x)
    {
        (float _, float saturation, float value) = Rgba.ToHsv(editor.ActiveColor);
        editor.SetHsv(Math.Clamp(x / HueWidth, 0f, 0.999f), saturation, value);
    }
}