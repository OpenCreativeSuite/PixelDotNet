using System;
using Donkeh.UILab;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Bottom bar: zoom, document facts, tool state and the last message.</summary>
public sealed class StatusBar : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        PaintDocument document = editor.Document;
        string position = editor.CursorX >= 0 ? $"{editor.CursorX}, {editor.CursorY}" : "—";

        return _.H("div", new() { className = "statusbar" }, _.H("div", new() { className = "status-zoom" }, Ui.Tip(editor, Ui.IconButton(Icons.Minus, "Zoom out", editor.ZoomOut, className: "status-btn"),
                    Icons.Minus, "Zoom Out", "status.zoomOut", "Ctrl+-", TipPlacement.Above), Ui.Tip(editor, _.H("button", new() { className = "status-item is-strong status-zoom-reset", Title = "Reset to actual pixels (100%)", OnClick = editor.ActualPixels }, Text(Ui.Format(editor.Zoom * 100f) + "%")),
                Icons.Zoom, "Actual Pixels", "status.zoomReset", "Ctrl+0", TipPlacement.Above), Ui.Tip(editor, Ui.IconButton(Icons.Plus, "Zoom in", editor.ZoomIn, className: "status-btn"),
                    Icons.Plus, "Zoom In", "status.zoomIn", "Ctrl++", TipPlacement.Above)), _.H("span", new() { className = "status-sep" }), _.H("span", new() { className = "status-item" }, Text($"{document.Width} × {document.Height} px")), _.H("span", new() { className = "status-sep" }), _.H("span", new() { className = "status-item is-muted" }, Text(editor.ColorMode)), _.H("span", new() { className = "status-sep" }), _.H("span", new() { className = "status-item" }, Text($"{PaintSession.NameOf(editor.Tool)} | {position}")), Show(editor.Selection is not null, _.H("fragment", new() { }, _.H("span", new() { className = "status-sep" }), _.H("span", new() { className = "status-item is-muted" }, Text($"Selection {editor.Selection?.Width ?? 0} × {editor.Selection?.Height ?? 0}")))), _.H("div", new() { className = "status-spacer" }), _.H("span", new() { className = "status-message" }, Text(editor.Status)));
    }
}