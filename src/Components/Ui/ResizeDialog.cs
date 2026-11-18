using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>The Image Size / Canvas Size modal.</summary>
public sealed class ResizeDialog : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsResizeOpen)
        {
            return Span(new());
        }

        bool canvas = editor.ResizeIsCanvas;
        string title = canvas ? "Canvas Size" : "Image Size";
        string summary = canvas
            ? "Change the document bounds; existing pixels keep their scale."
            : "Scale every layer, mask and guide to a new pixel size.";

        return Div(new() { ClassName = "resize-layer" },
            Div(new() { ClassName = "resize-panel" },
                Div(new() { ClassName = "resize-head" },
                    Span(new() { ClassName = "resize-head-icon" },
                        Ui.Icon(canvas ? Icons.Marquee : Icons.Document)),
                    Div(new() { ClassName = "resize-head-titles" },
                        Span(new() { ClassName = "resize-title" }, Text(title)),
                        Span(new() { ClassName = "resize-sub" }, Text(summary))),
                    Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close", () =>
                        {
                            editor.CloseResizeDialog();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Close", "")),
                Div(new() { ClassName = "resize-body" },
                    Div(new() { ClassName = "resize-current" },
                        Text($"Current: {editor.ResizeOriginalWidth} × {editor.ResizeOriginalHeight} px")),
                    Field("Width", editor.ResizeWidthText, editor.ResizeSetWidth),
                    Field("Height", editor.ResizeHeightText, editor.ResizeSetHeight),
                    Ui.Toggle(
                        "Keep aspect ratio",
                        "Link the width and height",
                        editor.ResizeKeepAspect,
                        () => editor.ToggleResizeAspect()),
                    Show(canvas, Anchors(editor)),
                    Show(editor.ResizeError.Length > 0,
                        Div(new() { ClassName = "resize-error" },
                            Text(editor.ResizeError)))),
                Div(new() { ClassName = "resize-foot" },
                    Span(new() { ClassName = "resize-note" },
                        Text(canvas ? "Guides follow the anchor." : "Undo restores the previous size.")),
                    Div(new() { ClassName = "resize-actions" },
                        Ui.Button("Cancel", "Close without resizing", () =>
                        {
                            editor.CloseResizeDialog();
                            editor.Refresh?.Invoke();
                        }),
                        Ui.Button("Resize", "Apply the new size", () =>
                        {
                            editor.ApplyResize();
                            editor.Refresh?.Invoke();
                        })))));
    }

    /// <summary>Builds one labelled pixel field.</summary>
    private static Node Field(string label, string value, Action<string> onInput) =>
        Div(new() { ClassName = "resize-field" },
            Span(new() { ClassName = "resize-label" }, Text(label)),
            Input(new()
            {
                ClassName = "text-field resize-input",
                Type = "text",
                Value = value,
                Placeholder = "pixels",
                OnInput = onInput,
            }),
            Span(new() { ClassName = "resize-unit" }, Text("px")));

    /// <summary>Builds the 3x3 anchor picker used by canvas resize.</summary>
    private static Node Anchors(PaintSession editor) =>
        Div(new() { ClassName = "resize-anchor-block" },
            Span(new() { ClassName = "resize-label" }, Text("Anchor")),
            Div(new() { ClassName = "resize-anchor-grid" },
                AnchorRow(
                    editor,
                    ResizeAnchor.TopLeft,
                    ResizeAnchor.Top,
                    ResizeAnchor.TopRight),
                AnchorRow(
                    editor,
                    ResizeAnchor.Left,
                    ResizeAnchor.Center,
                    ResizeAnchor.Right),
                AnchorRow(
                    editor,
                    ResizeAnchor.BottomLeft,
                    ResizeAnchor.Bottom,
                    ResizeAnchor.BottomRight)));

    /// <summary>Builds one row of three anchor buttons.</summary>
    private static Node AnchorRow(
        PaintSession editor,
        ResizeAnchor first,
        ResizeAnchor second,
        ResizeAnchor third) =>
        Div(new() { ClassName = "resize-anchor-row" },
            AnchorButton(editor, first),
            AnchorButton(editor, second),
            AnchorButton(editor, third));

    /// <summary>Builds one anchor choice.</summary>
    private static Node AnchorButton(PaintSession editor, ResizeAnchor anchor) =>
        Button(new()
        {
            ClassName = editor.ResizeAnchor == anchor
                ? "resize-anchor is-on"
                : "resize-anchor",
            Title = "Anchor " + anchor,
            OnClick = () => editor.SetResizeAnchor(anchor),
        }, Span(new() { ClassName = "resize-anchor-dot" }));
}
