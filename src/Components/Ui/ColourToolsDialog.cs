using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>One-shot colour operations: balance, threshold, gradient map, replace and split.</summary>
public sealed class ColourToolsDialog : Component
{
    private static readonly string[] BalanceLabels =
    [
        "Shadows: cyan / red",
        "Shadows: magenta / green",
        "Shadows: yellow / blue",
        "Midtones: cyan / red",
        "Midtones: magenta / green",
        "Midtones: yellow / blue",
        "Highlights: cyan / red",
        "Highlights: magenta / green",
        "Highlights: yellow / blue",
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsColourToolsOpen)
        {
            return Span(new());
        }

        return Div(new() { ClassName = "resize-layer" },
            Div(new() { ClassName = "resize-panel style-panel colour-tools-panel" },
                Div(new() { ClassName = "resize-head" },
                    Span(new() { ClassName = "resize-head-icon" }, Ui.Icon(Icons.Palette)),
                    Div(new() { ClassName = "resize-head-titles" },
                        Span(new() { ClassName = "resize-title" }, Text("Colour Tools")),
                        Span(new() { ClassName = "resize-sub" },
                            Text("Apply to the active layer or selection."))),
                    Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close", () =>
                        {
                            editor.CloseColourToolsDialog();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Close", "")),
                Div(new() { ClassName = "style-body" },
                    Section(
                        "Colour Balance",
                        "Apply",
                        "Shift shadows, midtones and highlights on each axis.",
                        editor.ApplyColourBalance,
                        BalanceRows(editor)),
                    Section(
                        "Threshold",
                        "Apply",
                        "Turn the layer into pure black and white at the chosen level.",
                        editor.ApplyThreshold,
                        Ui.Slider("Level", editor.ThresholdLevel, 0f, 255f, 1f, string.Empty, value =>
                        {
                            editor.SetThresholdLevel(value);
                            editor.Refresh?.Invoke();
                        })),
                    Section(
                        "Gradient Map",
                        "Apply",
                        "Map luminance between two colours.",
                        editor.ApplyGradientMap,
                        Div(new() { ClassName = "style-rows" },
                            Colors(editor, "Dark", editor.GradientLow, editor.SetGradientLow),
                            Colors(editor, "Light", editor.GradientHigh, editor.SetGradientHigh))),
                    Section(
                        "Replace Colour",
                        "Apply",
                        "Replace pixels close to one colour; tolerance widens the match.",
                        editor.ApplyColourReplace,
                        Div(new() { ClassName = "style-rows" },
                            Colors(editor, "From", editor.ReplaceFrom, editor.SetReplaceFrom, includeDocument: true),
                            Colors(editor, "To", editor.ReplaceTo, editor.SetReplaceTo, includeDocument: true),
                            Ui.Slider("Tolerance", editor.ReplaceTolerance, 0f, 255f, 1f, string.Empty, value =>
                            {
                                editor.SetReplaceTolerance(value);
                                editor.Refresh?.Invoke();
                            }))),
                    Div(new() { ClassName = "style-section" },
                        Div(new() { ClassName = "style-section-head" },
                            Span(new() { ClassName = "style-section-title" }, Text("Split Channels")),
                            Ui.Button("Split", "Create red, green and blue layers", () =>
                            {
                                editor.ApplySplitChannels();
                                editor.Refresh?.Invoke();
                            })),
                        Span(new() { ClassName = "resize-note" },
                            Text("Adds three grayscale layers above the active layer.")))),
                Div(new() { ClassName = "resize-foot" },
                    Span(new() { ClassName = "resize-note" },
                        Text("Each action is undoable and respects the selection.")),
                    Div(new() { ClassName = "resize-actions" },
                        Ui.Button("Done", "Close the colour tools", () =>
                        {
                            editor.CloseColourToolsDialog();
                            editor.Refresh?.Invoke();
                        })))));
    }

    /// <summary>Builds a titled section with an Apply button.</summary>
    private static Node Section(
        string title,
        string action,
        string note,
        Action apply,
        Node content) =>
        Div(new() { ClassName = "style-section" },
            Div(new() { ClassName = "style-section-head" },
                Span(new() { ClassName = "style-section-title" }, Text(title)),
                Ui.Button(action, "Apply " + title, apply)),
            Span(new() { ClassName = "resize-note" }, Text(note)),
            content);

    /// <summary>Builds the nine colour-balance sliders.</summary>
    private static Node BalanceRows(PaintSession editor)
    {
        Node[] rows = new Node[BalanceLabels.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            int index = i;
            rows[i] = Ui.Slider(
                BalanceLabels[i],
                editor.BalanceValue(index),
                -100f,
                100f,
                1f,
                string.Empty,
                value =>
                {
                    editor.SetBalanceValue(index, Ui.Parse(value, -100f, 100f));
                    editor.Refresh?.Invoke();
                });
        }

        return Div(new() { ClassName = "style-rows" }, rows);
    }

    /// <summary>Builds a labelled row of colour swatches.</summary>
    private static Node Colors(
        PaintSession editor,
        string label,
        uint current,
        Action<uint> pick,
        bool includeDocument = false) =>
        Div(new() { ClassName = "style-swatches" },
            Span(new() { ClassName = "resize-label" }, Text(label)),
            Each(new uint[]
            {
                0xFF000000,
                0xFFFFFFFF,
                0xFFE05454,
                0xFF4CBB6C,
                0xFF3D7BE0,
                0xFFFFD84D,
            }, color => (Node)Ui.Swatch(
                current == color ? "style-swatch is-on" : "style-swatch",
                color,
                "Use this colour",
                () => pick(color),
                18,
                18)),
            Show(includeDocument,
                Ui.Swatch(
                    current == editor.Foreground ? "style-swatch is-on" : "style-swatch",
                    editor.Foreground,
                    "Use the foreground colour",
                    () => pick(editor.Foreground),
                    18,
                    18)),
            Show(includeDocument,
                Ui.Swatch(
                    current == editor.Background ? "style-swatch is-on" : "style-swatch",
                    editor.Background,
                    "Use the background colour",
                    () => pick(editor.Background),
                    18,
                    18)));
}
