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
            return Span(new());
        }

        return Div(new() { ClassName = "resize-layer" },
            Div(new() { ClassName = "resize-panel batch-panel" },
                Div(new() { ClassName = "resize-head" },
                    Span(new() { ClassName = "resize-head-icon" },
                        Ui.Icon(Icons.Flatten)),
                    Div(new() { ClassName = "resize-head-titles" },
                        Span(new() { ClassName = "resize-title" }, Text("Batch Processing")),
                        Span(new() { ClassName = "resize-sub" },
                            Text("Resize and convert every image in a folder."))),
                    Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close", () =>
                        {
                            editor.CloseBatchDialog();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Close", "")),
                Div(new() { ClassName = "resize-body" },
                    FolderRow(editor, "Source", editor.BatchSource, editor.BatchSetSource, scan: true),
                    Div(new() { ClassName = "batch-places" },
                        Each(editor.BatchPlaces, place => (Node)Button(new()
                        {
                            ClassName = "file-place",
                            Title = "Use " + place.Path,
                            OnClick = () => editor.BatchUsePlace(place),
                        }, Text(place.Label)))),
                    FolderRow(editor, "Output", editor.BatchOutput, editor.BatchSetOutput, scan: false),
                    Div(new() { ClassName = "resize-field" },
                        Span(new() { ClassName = "resize-label" }, Text("Scale")),
                        Input(new()
                        {
                            ClassName = "text-field resize-input",
                            Type = "text",
                            Value = editor.BatchScaleText,
                            Placeholder = "100",
                            OnInput = editor.BatchSetScale,
                        }),
                        Span(new() { ClassName = "resize-unit" }, Text("%"))),
                    Div(new() { ClassName = "resize-field" },
                        Span(new() { ClassName = "resize-label" }, Text("Suffix")),
                        Input(new()
                        {
                            ClassName = "text-field resize-input",
                            Type = "text",
                            Value = editor.BatchSuffix,
                            Placeholder = "-edited",
                            OnInput = editor.BatchSetSuffix,
                        }),
                        Span(new() { ClassName = "resize-unit" })),
                    Div(new() { ClassName = "batch-formats" },
                        Span(new() { ClassName = "resize-label" }, Text("Format")),
                        Each(new[]
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
                        }, Text(ImageExporter.NameOf(format)))),
                        Div(new() { ClassName = "actions-spacer" }),
                        Ui.Toggle(
                            "Overwrite",
                            "Replace files that already exist",
                            editor.BatchOverwrite,
                            () => editor.ToggleBatchOverwrite())),
                    Div(new() { ClassName = "batch-status" }, Text(editor.BatchStatus))),
                Div(new() { ClassName = "resize-foot" },
                    Span(new() { ClassName = "resize-note" },
                        Text("Name = original + suffix. Format sets the extension.")),
                    Div(new() { ClassName = "resize-actions" },
                        Ui.Button("Close", "Close the batch dialog", () =>
                        {
                            editor.CloseBatchDialog();
                            editor.Refresh?.Invoke();
                        }),
                        Ui.Button("Run", "Process the folder", () =>
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
        Div(new() { ClassName = "resize-field" },
            Span(new() { ClassName = "resize-label" }, Text(label)),
            Input(new()
            {
                ClassName = "text-field resize-input",
                Type = "text",
                Value = value,
                Placeholder = "Folder path; press Enter",
                OnChange = onCommit,
            }),
            Show(scan, Ui.Button("Scan", "Recount the source folder", editor.BatchRefreshCount)));
}
