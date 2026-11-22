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
            return _.H("span", new() { });
        }

        bool canvas = editor.ResizeIsCanvas;
        string title = canvas ? "Canvas Size" : "Image Size";
        string summary = canvas
            ? "Change the document bounds; existing pixels keep their scale."
            : "Scale every layer, mask and guide to a new pixel size.";

        return _.H("div", new() { ClassName = "resize-layer" }, _.H("div", new() { ClassName = "resize-panel" }, _.H("div", new() { ClassName = "resize-head" }, _.H("span", new() { ClassName = "resize-head-icon" }, Ui.Icon(canvas ? Icons.Marquee : Icons.Document)), _.H("div", new() { ClassName = "resize-head-titles" }, _.H("span", new() { ClassName = "resize-title" }, Text(title)), _.H("span", new() { ClassName = "resize-sub" }, Text(summary))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close", () =>
                        {
                            editor.CloseResizeDialog();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Close", "")), _.H("div", new() { ClassName = "resize-body" }, _.H("div", new() { ClassName = "resize-current" }, Text($"Current: {editor.ResizeOriginalWidth} × {editor.ResizeOriginalHeight} px")), Field("Width", editor.ResizeWidthText, editor.ResizeSetWidth), Field("Height", editor.ResizeHeightText, editor.ResizeSetHeight), Ui.Toggle(
                        "Keep aspect ratio",
                        "Link the width and height",
                        editor.ResizeKeepAspect,
                        () => editor.ToggleResizeAspect()), Show(canvas, Anchors(editor)), Show(editor.ResizeError.Length > 0,
                        _.H("div", new() { ClassName = "resize-error" }, Text(editor.ResizeError)))), _.H("div", new() { ClassName = "resize-foot" }, _.H("span", new() { ClassName = "resize-note" }, Text(canvas ? "Guides follow the anchor." : "Undo restores the previous size.")), _.H("div", new() { ClassName = "resize-actions" }, Ui.Button("Cancel", "Close without resizing", () =>
                        {
                            editor.CloseResizeDialog();
                            editor.Refresh?.Invoke();
                        }), Ui.Button("Resize", "Apply the new size", () =>
                        {
                            editor.ApplyResize();
                            editor.Refresh?.Invoke();
                        })))));
    }

    /// <summary>Builds one labelled pixel field.</summary>
    private static Node Field(string label, string value, Action<string> onInput) =>
        _.H("div", new() { ClassName = "resize-field" }, _.H("span", new() { ClassName = "resize-label" }, Text(label)), _.H("input", new() { ClassName = "text-field resize-input", Type = "text", Value = value, Placeholder = "pixels", OnInput = onInput }), _.H("span", new() { ClassName = "resize-unit" }, Text("px")));

    /// <summary>Builds the 3x3 anchor picker used by canvas resize.</summary>
    private static Node Anchors(PaintSession editor) =>
        _.H("div", new() { ClassName = "resize-anchor-block" }, _.H("span", new() { ClassName = "resize-label" }, Text("Anchor")), _.H("div", new() { ClassName = "resize-anchor-grid" }, AnchorRow(editor, ResizeAnchor.TopLeft, ResizeAnchor.Top, ResizeAnchor.TopRight), AnchorRow(editor, ResizeAnchor.Left, ResizeAnchor.Center, ResizeAnchor.Right), AnchorRow(editor, ResizeAnchor.BottomLeft, ResizeAnchor.Bottom, ResizeAnchor.BottomRight)));

    /// <summary>Builds one row of three anchor buttons.</summary>
    private static Node AnchorRow(
        PaintSession editor,
        ResizeAnchor first,
        ResizeAnchor second,
        ResizeAnchor third) =>
        _.H("div", new() { ClassName = "resize-anchor-row" }, AnchorButton(editor, first), AnchorButton(editor, second), AnchorButton(editor, third));

    /// <summary>Builds one anchor choice.</summary>
    private static Node AnchorButton(PaintSession editor, ResizeAnchor anchor) =>
        _.H("button", new() { ClassName = editor.ResizeAnchor == anchor ? "resize-anchor is-on" : "resize-anchor", Title = "Anchor " + anchor, OnClick = () => editor.SetResizeAnchor(anchor) }, _.H("span", new() { ClassName = "resize-anchor-dot" }));
}