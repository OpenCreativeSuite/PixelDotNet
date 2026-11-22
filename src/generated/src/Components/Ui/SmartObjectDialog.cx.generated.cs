using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Position, scale, rotation and mirroring for a smart object.</summary>
public sealed class SmartObjectDialog : Component
{
    private static readonly string[] Labels =
    [
        "X",
        "Y",
        "Scale X",
        "Scale Y",
        "Rotation",
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsSmartTransformOpen || editor.SmartTransformLayer is not { } layer)
        {
            return _.H("span", new() { });
        }

        Node[] sliders = new Node[Labels.Length];
        for (int i = 0; i < sliders.Length; i++)
        {
            int index = i;
            float min = index switch
            {
                2 or 3 => 1f,
                4 => -180f,
                _ => -4000f,
            };
            float max = index switch
            {
                2 or 3 => 800f,
                4 => 180f,
                _ => 4000f,
            };
            string suffix = index is 2 or 3 ? " %" : index == 4 ? " deg" : " px";

            sliders[i] = Ui.Slider(
                Labels[i],
                editor.SmartValue(index),
                min,
                max,
                1f,
                suffix,
                value =>
                {
                    editor.SetSmartValue(index, Ui.Parse(value, min, max));
                    editor.Refresh?.Invoke();
                });
        }

        return _.H("div", new() { ClassName = "resize-layer" }, _.H("div", new() { ClassName = "resize-panel style-panel perspective-panel" }, _.H("div", new() { ClassName = "resize-head" }, _.H("span", new() { ClassName = "resize-head-icon" }, Ui.Icon(Icons.SmartObject)), _.H("div", new() { ClassName = "resize-head-titles" }, _.H("span", new() { ClassName = "resize-title" }, Text("Smart Object")), _.H("span", new() { ClassName = "resize-sub" }, Text(layer.Name))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Cancel", () =>
                        {
                            editor.CancelSmartTransform();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Cancel", "")), _.H("div", new() { ClassName = "style-body" }, _.H("span", new() { ClassName = "resize-note" }, Text("Repeated transforms reuse the original source pixels.")), _.H("div", new() { ClassName = "style-rows" }, sliders), _.H("div", new() { ClassName = "style-options" }, Ui.Toggle("Flip X", "Mirror horizontally", editor.SmartFlipX, () =>
                        {
                            editor.ToggleSmartFlipX();
                            editor.Refresh?.Invoke();
                        }), Ui.Toggle("Flip Y", "Mirror vertically", editor.SmartFlipY, () =>
                        {
                            editor.ToggleSmartFlipY();
                            editor.Refresh?.Invoke();
                        }))), _.H("div", new() { ClassName = "resize-foot" }, _.H("span", new() { ClassName = "resize-note" }, Text("Only .pxd projects keep smart objects.")), _.H("div", new() { ClassName = "resize-actions" }, Ui.Button("Reset", "Return to the identity transform", () =>
                        {
                            editor.ResetSmartTransform();
                            editor.Refresh?.Invoke();
                        }), Ui.Button("Cancel", "Restore the previous transform", () =>
                        {
                            editor.CancelSmartTransform();
                            editor.Refresh?.Invoke();
                        }), Ui.Button("Done", "Keep the transform", () =>
                        {
                            editor.CommitSmartTransform();
                            editor.Refresh?.Invoke();
                        })))));
    }
}