using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Geometry and paint controls for a shape layer.</summary>
public sealed class ShapeEditDialog : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsShapeEditOpen
            || editor.ShapeEditLayer is not { } layer
            || layer.Shape is not { } shape)
        {
            return _.H("span", new() { });
        }

        return _.H("div", new() { ClassName = "resize-layer" }, _.H("div", new() { ClassName = "resize-panel style-panel perspective-panel" }, _.H("div", new() { ClassName = "resize-head" }, _.H("span", new() { ClassName = "resize-head-icon" }, Ui.Icon(Icons.Rectangle)), _.H("div", new() { ClassName = "resize-head-titles" }, _.H("span", new() { ClassName = "resize-title" }, Text("Edit Shape")), _.H("span", new() { ClassName = "resize-sub" }, Text(layer.Name))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Cancel", () =>
                        {
                            editor.CancelShapeEdit();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Cancel", "")), _.H("div", new() { ClassName = "style-body" }, _.H("div", new() { ClassName = "style-rows" }, Show(shape.Kind != ShapeKind.Path,
                            Slider("X", shape.X, -4000f, 4000f, 1f, " px", editor, value =>
                            {
                                shape.X = value;
                            })), Show(shape.Kind != ShapeKind.Path,
                            Slider("Y", shape.Y, -4000f, 4000f, 1f, " px", editor, value =>
                            {
                                shape.Y = value;
                            })), Show(shape.Kind != ShapeKind.Path,
                            Slider("Width", shape.Width, 0f, 8000f, 1f, " px", editor, value =>
                            {
                                shape.Width = value;
                            })), Show(shape.Kind != ShapeKind.Path,
                            Slider("Height", shape.Height, 0f, 8000f, 1f, " px", editor, value =>
                            {
                                shape.Height = value;
                            })), Slider("Stroke", shape.StrokeWidth, 1f, 40f, 1f, " px", editor, value =>
                        {
                            shape.StrokeWidth = value;
                        }), _.H("div", new() { ClassName = "style-options" }, Ui.Toggle("Fill", "Fill the shape", shape.Fill, () =>
                            {
                                shape.Fill = !shape.Fill;
                                editor.TouchShape();
                                editor.Refresh?.Invoke();
                            }), Ui.Toggle("Outline", "Draw the outline", shape.Stroke, () =>
                            {
                                shape.Stroke = !shape.Stroke;
                                editor.TouchShape();
                                editor.Refresh?.Invoke();
                            })), Colors(editor, "Fill", shape.FillColor, color =>
                        {
                            shape.FillColor = color;
                            editor.TouchShape();
                        }), Colors(editor, "Stroke", shape.StrokeColor, color =>
                        {
                            shape.StrokeColor = color;
                            editor.TouchShape();
                        }))), _.H("div", new() { ClassName = "resize-foot" }, _.H("span", new() { ClassName = "resize-note" }, Text("Shape layers stay editable until rasterized.")), _.H("div", new() { ClassName = "resize-actions" }, Ui.Button("Cancel", "Restore the shape", () =>
                        {
                            editor.CancelShapeEdit();
                            editor.Refresh?.Invoke();
                        }), Show(shape.Kind == ShapeKind.Path,
                            Ui.Button("Edit Nodes", "Drag the path anchors and handles on the canvas", () =>
                            {
                                editor.CommitShapeEdit();
                                editor.StartPathNodeEdit();
                                editor.Refresh?.Invoke();
                            })), Ui.Button("Done", "Keep the shape", () =>
                        {
                            editor.CommitShapeEdit();
                            editor.Refresh?.Invoke();
                        })))));
    }

    /// <summary>Builds one shape property slider.</summary>
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
            editor.TouchShape();
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