using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Editing controls for an editable text layer.</summary>
public sealed class TextEditDialog : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsTextEditOpen
            || editor.TextEditLayer is not { } layer
            || layer.Text is not { } text)
        {
            return _.H("span", new() { });
        }

        return _.H("div", new() { ClassName = "resize-layer" }, _.H("div", new() { ClassName = "resize-panel style-panel perspective-panel" }, _.H("div", new() { ClassName = "resize-head" }, _.H("span", new() { ClassName = "resize-head-icon" }, Ui.Icon(Icons.Text)), _.H("div", new() { ClassName = "resize-head-titles" }, _.H("span", new() { ClassName = "resize-title" }, Text("Edit Text")), _.H("span", new() { ClassName = "resize-sub" }, Text(layer.Name))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Cancel", () =>
                        {
                            editor.CancelTextEdit();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Cancel", "")), _.H("div", new() { ClassName = "style-body" }, _.H("div", new() { ClassName = "style-rows" }, Ui.TextField(
                            text.Content,
                            "Text",
                            value =>
                            {
                                text.Content = value;
                                editor.TouchTextLayer();
                            },
                            editor.BeginTyping,
                            editor.EndTyping,
                            null,
                            "text-edit-" + editor.TextEditIndex), _.H("div", new() { ClassName = "text-font" }, Ui.Button("<", "Previous font", () =>
                            {
                                text.FontIndex = WrapFont(text.FontIndex - 1);
                                editor.TouchTextLayer();
                                editor.Refresh?.Invoke();
                            }), _.H("span", new() { ClassName = "resize-note" }, Text(TextFonts.At(text.FontIndex).Name)), Ui.Button(">", "Next font", () =>
                            {
                                text.FontIndex = WrapFont(text.FontIndex + 1);
                                editor.TouchTextLayer();
                                editor.Refresh?.Invoke();
                            })), Slider("Size", text.Size, 8f, 400f, 1f, " px", editor, value =>
                        {
                            text.Size = value;
                        }), Slider("Rotation", text.Rotation, -180f, 180f, 1f, " deg", editor, value =>
                        {
                            text.Rotation = value;
                        }), Show(!text.HasPath,
                            Slider("X", text.X, -4000f, 8000f, 1f, " px", editor, value =>
                            {
                                text.X = value;
                            })), Show(!text.HasPath,
                            Slider("Y", text.Y, -4000f, 8000f, 1f, " px", editor, value =>
                            {
                                text.Y = value;
                            })), Show(text.HasPath,
                            Slider("Path offset", text.PathOffset * 100f, 0f, 100f, 1f, " %", editor, value =>
                            {
                                text.PathOffset = value / 100f;
                            })), Show(text.HasPath,
                            Slider("Spacing", text.LetterSpacing, -10f, 40f, 1f, " px", editor, value =>
                            {
                                text.LetterSpacing = value;
                            })), _.H("div", new() { ClassName = "style-options" }, Ui.Toggle("Bold", "Bold text", text.Bold, () =>
                            {
                                text.Bold = !text.Bold;
                                editor.TouchTextLayer();
                                editor.Refresh?.Invoke();
                            }), Ui.Toggle("Italic", "Italic text", text.Italic, () =>
                            {
                                text.Italic = !text.Italic;
                                editor.TouchTextLayer();
                                editor.Refresh?.Invoke();
                            })), Show(text.HasPath && !string.IsNullOrEmpty(text.PathLayer),
                            _.H("span", new() { ClassName = "resize-note" }, Text("Follows path: " + text.PathLayer))), Colors(editor, "Fill", text.Color, color =>
                        {
                            text.Color = color;
                            editor.TouchTextLayer();
                        }))), _.H("div", new() { ClassName = "resize-foot" }, _.H("span", new() { ClassName = "resize-note" }, Text("Text stays editable until rasterized.")), _.H("div", new() { ClassName = "resize-actions" }, Ui.Button("Cancel", "Restore the text", () =>
                        {
                            editor.CancelTextEdit();
                            editor.Refresh?.Invoke();
                        }), Ui.Button("Done", "Keep the text", () =>
                        {
                            editor.CommitTextEdit();
                            editor.Refresh?.Invoke();
                        })))));
    }

    /// <summary>Wraps a font index around the available family list.</summary>
    private static int WrapFont(int index)
    {
        int count = TextFonts.All.Length;
        return ((index % count) + count) % count;
    }

    /// <summary>Builds one text property slider.</summary>
    private static Node Slider(
        string label,
        float value,
        float min,
        float max,
        float step,
        string suffix,
        PaintSession editor,
        Action<float> apply) =>
        Ui.Slider(label, value, min, max, step, suffix, text =>
        {
            apply(Ui.Parse(text, min, max));
            editor.TouchTextLayer();
            editor.Refresh?.Invoke();
        });

    /// <summary>Builds a row of preset colour swatches.</summary>
    private static Node Colors(
        PaintSession editor,
        string label,
        uint current,
        Action<uint> pick) =>
        _.H("div", new() { ClassName = "style-swatches" }, _.H("span", new() { ClassName = "resize-label" }, Text(label)), Each(new uint[]
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
                18)), Ui.Swatch(
                current == editor.Foreground ? "style-swatch is-on" : "style-swatch",
                editor.Foreground,
                "Use the foreground colour",
                () => pick(editor.Foreground),
                18,
                18), Ui.Swatch(
                current == editor.Background ? "style-swatch is-on" : "style-swatch",
                editor.Background,
                "Use the background colour",
                () => pick(editor.Background),
                18,
                18));
}