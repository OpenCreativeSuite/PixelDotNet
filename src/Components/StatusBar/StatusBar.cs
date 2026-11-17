using System;
using Donkeh.UILab;
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

        return Div(new() { ClassName = "statusbar" },
            Div(new() { ClassName = "status-zoom" },
                Ui.IconButton(Icons.Minus, "Zoom out", editor.ZoomOut, className: "status-btn"),
                Button(new()
                {
                    ClassName = "status-item is-strong status-zoom-reset",
                    Title = "Reset to actual pixels (100%)",
                    OnClick = editor.ActualPixels,
                }, Text(Ui.Format(editor.Zoom * 100f) + "%")),
                Ui.IconButton(Icons.Plus, "Zoom in", editor.ZoomIn, className: "status-btn")),
            Span(new() { ClassName = "status-sep" }),
            Span(new() { ClassName = "status-item" }, Text($"{document.Width} × {document.Height} px")),
            Span(new() { ClassName = "status-sep" }),
            Span(new() { ClassName = "status-item is-muted" }, Text(editor.ColorMode)),
            Span(new() { ClassName = "status-sep" }),
            Span(new() { ClassName = "status-item" }, Text($"{PaintSession.NameOf(editor.Tool)} · {position}")),
            Show(editor.Selection is not null, F(
                Span(new() { ClassName = "status-sep" }),
                Span(new() { ClassName = "status-item is-muted" },
                    Text($"Selection {editor.Selection?.Width ?? 0} × {editor.Selection?.Height ?? 0}")))),
            Div(new() { ClassName = "status-spacer" }),
            Span(new() { ClassName = "status-message" }, Text(editor.Status)));
    }
}
