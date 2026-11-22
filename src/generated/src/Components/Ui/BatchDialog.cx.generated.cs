using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Resize and format conversion for a whole folder of images.</summary>
public sealed class BatchDialog : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsBatchOpen)
        {
            return _.H("span", new() { });
        }

        return _.H("div", new() { ClassName = "resize-layer" }, _.H("div", new() { ClassName = "resize-panel batch-panel" }, _.H("div", new() { ClassName = "resize-head" }, _.H("span", new() { ClassName = "resize-head-icon" }, Ui.Icon(Icons.Flatten)), _.H("div", new() { ClassName = "resize-head-titles" }, _.H("span", new() { ClassName = "resize-title" }, Text("Batch Processing")), _.H("span", new() { ClassName = "resize-sub" }, Text("Resize and convert every image in a folder."))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close", () =>
                        {
                            editor.CloseBatchDialog();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Close", "")), _.H("div", new() { ClassName = "resize-body" }, FolderRow(editor, "Source", editor.BatchSource, editor.BatchSetSource, scan: true), _.H("div", new() { ClassName = "batch-places" }, Each(editor.BatchPlaces, place => (Node)Button(new()
                        {
                            ClassName = "file-place",
                            Title = "Use " + place.Path,
                            OnClick = () => editor.BatchUsePlace(place),
                        }, Text(place.Label)))), FolderRow(editor, "Output", editor.BatchOutput, editor.BatchSetOutput, scan: false), _.H("div", new() { ClassName = "resize-field" }, _.H("span", new() { ClassName = "resize-label" }, Text("Scale")), _.H("input", new() { ClassName = "text-field resize-input", Type = "text", Value = editor.BatchScaleText, Placeholder = "100", OnInput = editor.BatchSetScale }), _.H("span", new() { ClassName = "resize-unit" }, Text("%"))), _.H("div", new() { ClassName = "resize-field" }, _.H("span", new() { ClassName = "resize-label" }, Text("Suffix")), _.H("input", new() { ClassName = "text-field resize-input", Type = "text", Value = editor.BatchSuffix, Placeholder = "-edited", OnInput = editor.BatchSetSuffix }), _.H("span", new() { ClassName = "resize-unit" })), _.H("div", new() { ClassName = "batch-formats" }, _.H("span", new() { ClassName = "resize-label" }, Text("Format")), Each(new[]
                        {
                            ImageFormat.Png,
                            ImageFormat.Jpeg,
                            ImageFormat.Bmp,
                            ImageFormat.Tga,
                        }, format => (Node)Button(new()
                        {
                            ClassName = editor.BatchFormat == format ? "btn is-on" : "btn",
                            Title = "Write " + ImageExporter.NameOf(format),
                            OnClick = () => editor.BatchSetFormat(format),
                        }, Text(ImageExporter.NameOf(format)))), _.H("div", new() { ClassName = "actions-spacer" }), Ui.Toggle(
                            "Overwrite",
                            "Replace files that already exist",
                            editor.BatchOverwrite,
                            () => editor.ToggleBatchOverwrite())), _.H("div", new() { ClassName = "batch-status" }, Text(editor.BatchStatus))), _.H("div", new() { ClassName = "resize-foot" }, _.H("span", new() { ClassName = "resize-note" }, Text("Name = original + suffix. Format sets the extension.")), _.H("div", new() { ClassName = "resize-actions" }, Ui.Button("Close", "Close the batch dialog", () =>
                        {
                            editor.CloseBatchDialog();
                            editor.Refresh?.Invoke();
                        }), Ui.Button("Run", "Process the folder", () =>
                        {
                            editor.RunBatch();
                            editor.Refresh?.Invoke();
                        })))));
    }

    /// <summary>Builds one folder path row.</summary>
    private static Node FolderRow(
        PaintSession editor,
        string label,
        string value,
        Action<string> onCommit,
        bool scan) =>
        _.H("div", new() { ClassName = "resize-field" }, _.H("span", new() { ClassName = "resize-label" }, Text(label)), _.H("input", new() { ClassName = "text-field resize-input", Type = "text", Value = value, Placeholder = "Folder path; press Enter", OnChange = onCommit }), Show(scan, Ui.Button("Scan", "Recount the source folder", editor.BatchRefreshCount)));
}