using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Floating controls for the interactive mesh warp.</summary>
public sealed class MeshWarpBar : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsMeshWarpOpen)
        {
            return Span(new());
        }

        return Div(new() { ClassName = "mesh-warp-bar" },
            Span(new() { ClassName = "mesh-warp-title" }, Text("Mesh Warp")),
            Span(new() { ClassName = "mesh-warp-layer", Title = "Layer being mesh-warped" },
                Ui.Icon(Icons.Flatten, "mesh-warp-layer-icon"),
                Span(new() { ClassName = "mesh-warp-layer-name" },
                    Text(editor.MeshWarpLayer?.Name ?? "None"))),
            Span(new()
            {
                ClassName = editor.MeshWarpLayerEmpty
                    ? "mesh-warp-note is-warning"
                    : "mesh-warp-note",
            },
                Text(editor.MeshWarpLayerEmpty
                    ? "This layer is empty. Pick one with pixels."
                    : "Drag the nine points on the canvas.")),
            Ui.Button("Reset", "Clear every point offset", () =>
            {
                editor.ResetMeshWarp();
                editor.Refresh?.Invoke();
            }),
            Ui.Button("Cancel", "Restore the original pixels", () =>
            {
                editor.CancelMeshWarp();
                editor.Refresh?.Invoke();
            }),
            Ui.Button("Done", "Keep the warped layer", () =>
            {
                editor.CommitMeshWarp();
                editor.Refresh?.Invoke();
            }));
    }
}
