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
            Span(new() { ClassName = "mesh-warp-note" },
                Text("Drag the nine points on the canvas.")),
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
