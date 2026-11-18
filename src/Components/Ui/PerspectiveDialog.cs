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
            return Span(new());
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

        return Div(new() { ClassName = "resize-layer" },
            Div(new() { ClassName = "resize-panel style-panel perspective-panel" },
                Div(new() { ClassName = "resize-head" },
                    Span(new() { ClassName = "resize-head-icon" }, Ui.Icon(Icons.Move)),
                    Div(new() { ClassName = "resize-head-titles" },
                        Span(new() { ClassName = "resize-title" }, Text("Perspective Transform")),
                        Span(new() { ClassName = "resize-sub" }, Text(layer.Name))),
                    Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Cancel", () =>
                        {
                            editor.CancelPerspective();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Cancel", "")),
                Div(new() { ClassName = "style-body" },
                    Span(new() { ClassName = "resize-note" },
                        Text(editor.Selection is null
                            ? "Drag the corners by moving the sliders; the layer is transformed live."
                            : "Transforms the selected region; pixels outside it stay put.")),
                    Div(new() { ClassName = "style-rows" }, sliders)),
                Div(new() { ClassName = "resize-foot" },
                    Span(new() { ClassName = "resize-note" },
                        Text("Reset returns to the original pixels.")),
                    Div(new() { ClassName = "resize-actions" },
                        Ui.Button("Reset", "Clear all corner offsets", () =>
                        {
                            editor.ResetPerspective();
                            editor.Refresh?.Invoke();
                        }),
                        Ui.Button("Cancel", "Restore the original pixels", () =>
                        {
                            editor.CancelPerspective();
                            editor.Refresh?.Invoke();
                        }),
                        Ui.Button("Done", "Keep the transformed layer", () =>
                        {
                            editor.CommitPerspective();
                            editor.Refresh?.Invoke();
                        })))));
    }
}
