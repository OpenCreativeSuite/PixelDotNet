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
            return _.H("span", new() { });
        }

        return _.H("div", new() { className = "resize-layer" }, _.H("div", new() { className = "resize-panel style-panel colour-tools-panel" }, _.H("div", new() { className = "resize-head" }, _.H("span", new() { className = "resize-head-icon" }, Ui.Icon(Icons.Palette)), _.H("div", new() { className = "resize-head-titles" }, _.H("span", new() { className = "resize-title" }, Text("Colour Tools")), _.H("span", new() { className = "resize-sub" }, Text("Apply to the active layer or selection."))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close", () =>
                        {
                            editor.CloseColourToolsDialog();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Close", "")), _.H("div", new() { className = "style-body" }, Section(
                        "Colour Balance",
                        "Apply",
                        "Shift shadows, midtones and highlights on each axis.",
                        editor.ApplyColourBalance,
                        BalanceRows(editor)), Section(
                        "Threshold",
                        "Apply",
                        "Turn the layer into pure black and white at the chosen level.",
                        editor.ApplyThreshold,
                        Ui.Slider("Level", editor.ThresholdLevel, 0f, 255f, 1f, string.Empty, value =>
                        {
                            editor.SetThresholdLevel(value);
                            editor.Refresh?.Invoke();
                        })), Section(
                        "Gradient Map",
                        "Apply",
                        "Map luminance between two colours.",
                        editor.ApplyGradientMap,
                        _.H("div", new() { className = "style-rows" }, Colors(editor, "Dark", editor.GradientLow, editor.SetGradientLow), Colors(editor, "Light", editor.GradientHigh, editor.SetGradientHigh))), Section(
                        "Replace Colour",
                        "Apply",
                        "Replace pixels close to one colour; tolerance widens the match.",
                        editor.ApplyColourReplace,
                        _.H("div", new() { className = "style-rows" }, Colors(editor, "From", editor.ReplaceFrom, editor.SetReplaceFrom, includeDocument: true), Colors(editor, "To", editor.ReplaceTo, editor.SetReplaceTo, includeDocument: true), Ui.Slider("Tolerance", editor.ReplaceTolerance, 0f, 255f, 1f, string.Empty, value =>
                            {
                                editor.SetReplaceTolerance(value);
                                editor.Refresh?.Invoke();
                            }))), Section(
                        "Red-Eye Removal",
                        "Apply",
                        "Neutralise red pupils inside the selection.",
                        editor.ApplyRedEye,
                        Ui.Slider("Strength", editor.RedEyeStrength, 0f, 100f, 1f, " %", value =>
                        {
                            editor.SetRedEyeStrength(value);
                            editor.Refresh?.Invoke();
                        })), Section(
                        "Channel Mixer",
                        "Apply",
                        "Each output channel is a weighted sum of the source channels.",
                        editor.ApplyChannelMix,
                        MixRows(editor)), Section(
                        "Selective Colour",
                        "Apply",
                        "Pick a colour range, then shift its cyan, magenta, yellow or black.",
                        editor.ApplySelectiveColour,
                        SelectiveRows(editor)), _.H("div", new() { className = "style-section" }, _.H("div", new() { className = "style-section-head" }, _.H("span", new() { className = "style-section-title" }, Text("Split Channels")), Ui.Button("Split", "Create red, green and blue layers", () =>
                            {
                                editor.ApplySplitChannels();
                                editor.Refresh?.Invoke();
                            })), _.H("span", new() { className = "resize-note" }, Text("Adds three grayscale layers above the active layer.")))), _.H("div", new() { className = "resize-foot" }, _.H("span", new() { className = "resize-note" }, Text("Each action is undoable and respects the selection.")), _.H("div", new() { className = "resize-actions" }, Ui.Button("Done", "Close the colour tools", () =>
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
        _.H("div", new() { className = "style-section" }, _.H("div", new() { className = "style-section-head" }, _.H("span", new() { className = "style-section-title" }, Text(title)), Ui.Button(action, "Apply " + title, apply)), _.H("span", new() { className = "resize-note" }, Text(note)), content);

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

        return _.H("div", new() { className = "style-rows" }, rows);
    }

    /// <summary>Builds the nine channel-mixer sliders.</summary>
    private static Node MixRows(PaintSession editor)
    {
        string[] labels =
        [
            "Red: from red",
            "Red: from green",
            "Red: from blue",
            "Green: from red",
            "Green: from green",
            "Green: from blue",
            "Blue: from red",
            "Blue: from green",
            "Blue: from blue",
        ];

        Node[] rows = new Node[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            rows[i] = Ui.Slider(
                labels[i],
                editor.ChannelMixValue(index),
                -200f,
                200f,
                1f,
                " %",
                value =>
                {
                    editor.SetChannelMixValue(index, Ui.Parse(value, -200f, 200f));
                    editor.Refresh?.Invoke();
                });
        }

        Node[] all = [.. rows, Ui.Button("Reset", "Restore the identity mix", () =>
        {
            editor.ResetChannelMix();
            editor.Refresh?.Invoke();
        })];
        return _.H("div", new() { className = "style-rows" }, all);
    }

    /// <summary>Builds the selective-colour range chips and CMYK sliders.</summary>
    private static Node SelectiveRows(PaintSession editor)
    {
        string[] ranges = ["Reds", "Yellows", "Greens", "Cyans", "Blues", "Magentas", "Whites", "Neutrals", "Blacks"];
        string[] channels = ["Cyan", "Magenta", "Yellow", "Black"];

        Node[] sliders = new Node[channels.Length];
        for (int i = 0; i < channels.Length; i++)
        {
            int index = i;
            sliders[i] = Ui.Slider(
                channels[i],
                editor.SelectiveValue(index),
                -100f,
                100f,
                1f,
                " %",
                value =>
                {
                    editor.SetSelectiveValue(index, Ui.Parse(value, -100f, 100f));
                    editor.Refresh?.Invoke();
                });
        }

        Node[] chips = new Node[ranges.Length];
        for (int i = 0; i < ranges.Length; i++)
        {
            int index = i;
            chips[i] = _.H("button", new() { className = editor.SelectiveRange == index ? "btn is-on" : "btn", Title = "Edit " + ranges[i], OnClick = () =>
                {
                    editor.SelectSelectiveRange(index);
                    editor.Refresh?.Invoke();
                } }, Text(ranges[i]));
        }

        Node[] all = new Node[sliders.Length + 2];
        all[0] = _.H("div", new() { className = "style-chips" }, chips);
        Array.Copy(sliders, 0, all, 1, sliders.Length);
        all[^1] = Ui.Button("Reset", "Clear every selective offset", () =>
        {
            editor.ResetSelectiveColour();
            editor.Refresh?.Invoke();
        });

        return _.H("div", new() { className = "style-rows" }, all);
    }

    /// <summary>Builds a labelled row of colour swatches.</summary>
    private static Node Colors(
        PaintSession editor,
        string label,
        uint current,
        Action<uint> pick,
        bool includeDocument = false) =>
        _.H("div", new() { className = "style-swatches" }, _.H("span", new() { className = "resize-label" }, Text(label)), Each(new uint[]
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
                18)), Show(includeDocument,
                Ui.Swatch(
                    current == editor.Foreground ? "style-swatch is-on" : "style-swatch",
                    editor.Foreground,
                    "Use the foreground colour",
                    () => pick(editor.Foreground),
                    18,
                    18)), Show(includeDocument,
                Ui.Swatch(
                    current == editor.Background ? "style-swatch is-on" : "style-swatch",
                    editor.Background,
                    "Use the background colour",
                    () => pick(editor.Background),
                    18,
                    18)));
}