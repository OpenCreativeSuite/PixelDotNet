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
            return _.H("span", new() { });
        }

        return _.H("div", new() { className = "mesh-warp-bar" }, _.H("span", new() { className = "mesh-warp-title" }, Text("Mesh Warp")), _.H("span", new() { className = "mesh-warp-layer", Title = "Layer being mesh-warped" }, Ui.Icon(Icons.Flatten, "mesh-warp-layer-icon"), _.H("span", new() { className = "mesh-warp-layer-name" }, Text(editor.MeshWarpLayer?.Name ?? "None"))), _.H("span", new() { className = editor.MeshWarpLayerEmpty ? "mesh-warp-note is-warning" : "mesh-warp-note" }, Text(editor.MeshWarpLayerEmpty
                    ? "This layer is empty. Pick one with pixels."
                    : "Drag the nine points on the canvas.")), Ui.Button("Reset", "Clear every point offset", () =>
            {
                editor.ResetMeshWarp();
                editor.Refresh?.Invoke();
            }), Ui.Button("Cancel", "Restore the original pixels", () =>
            {
                editor.CancelMeshWarp();
                editor.Refresh?.Invoke();
            }), Ui.Button("Done", "Keep the warped layer", () =>
            {
                editor.CommitMeshWarp();
                editor.Refresh?.Invoke();
            }));
    }
}