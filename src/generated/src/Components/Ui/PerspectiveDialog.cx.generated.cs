using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Four-corner perspective transform with a live canvas preview.</summary>
public sealed class PerspectiveDialog : Component
{
    private static readonly string[] Labels =
    [
        "Top left X",
        "Top left Y",
        "Top right X",
        "Top right Y",
        "Bottom right X",
        "Bottom right Y",
        "Bottom left X",
        "Bottom left Y",
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsPerspectiveOpen || editor.PerspectiveLayer is not { } layer)
        {
            return _.H("span", new() { });
        }

        Node[] sliders = new Node[Labels.Length];
        for (int i = 0; i < sliders.Length; i++)
        {
            int index = i;
            sliders[i] = Ui.Slider(
                Labels[i],
                editor.PerspectiveValue(index),
                -300f,
                300f,
                1f,
                " px",
                value =>
                {
                    editor.SetPerspectiveValue(index, Ui.Parse(value, -300f, 300f));
                    editor.Refresh?.Invoke();
                });
        }

        return _.H("div", new() { className = "resize-layer" }, _.H("div", new() { className = "resize-panel style-panel perspective-panel" }, _.H("div", new() { className = "resize-head" }, _.H("span", new() { className = "resize-head-icon" }, Ui.Icon(Icons.Move)), _.H("div", new() { className = "resize-head-titles" }, _.H("span", new() { className = "resize-title" }, Text("Perspective Transform")), _.H("span", new() { className = "resize-sub" }, Text(layer.Name))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Cancel", () =>
                        {
                            editor.CancelPerspective();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Cancel", "")), _.H("div", new() { className = "style-body" }, _.H("span", new() { className = "resize-note" }, Text(editor.Selection is null
                            ? "Drag the corners by moving the sliders; the layer is transformed live."
                            : "Transforms the selected region; pixels outside it stay put.")), _.H("div", new() { className = "style-rows" }, sliders)), _.H("div", new() { className = "resize-foot" }, _.H("span", new() { className = "resize-note" }, Text("Reset returns to the original pixels.")), _.H("div", new() { className = "resize-actions" }, Ui.Button("Reset", "Clear all corner offsets", () =>
                        {
                            editor.ResetPerspective();
                            editor.Refresh?.Invoke();
                        }), Ui.Button("Cancel", "Restore the original pixels", () =>
                        {
                            editor.CancelPerspective();
                            editor.Refresh?.Invoke();
                        }), Ui.Button("Done", "Keep the transformed layer", () =>
                        {
                            editor.CommitPerspective();
                            editor.Refresh?.Invoke();
                        })))));
    }
}