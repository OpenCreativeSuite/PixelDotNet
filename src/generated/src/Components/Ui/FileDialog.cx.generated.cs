using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>The in-app file browser used by File &gt; Open and File &gt; Save.</summary>
public sealed class FileDialog : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsFileDialogOpen)
        {
            return _.H("span", new() { });
        }

        bool saving = editor.FileDialogMode == FileDialogMode.Save;
        bool project = editor.FileDialogProject;
        bool svg = editor.FileDialogSvg;
        string title = svg
            ? saving ? "Export SVG" : "Import SVG"
            : project
                ? saving ? "Save Project" : "Open Project"
                : saving ? "Save Image" : "Open Image";
        string summary = svg
            ? saving
                ? "Shapes and text stay vector; other layers are embedded as a PNG."
                : "Choose an SVG to import as editable shape and text layers."
            : project
                ? saving
                    ? "Keep layers, masks, groups and guides in a .pxd file."
                    : "Open a .pxd project with all of its layers and masks."
                : saving
                    ? "Choose a folder, a file name and a format."
                    : "Choose an image to open in a new tab.";

        return _.H("div", new() { className = "file-layer" }, _.H("div", new() { className = "file-panel" }, Head(editor, saving, title, summary), Tools(editor), _.H("div", new() { className = "file-list", OnContextMenu = menu => editor.OpenFileContextMenu(
                        null,
                        menu.WindowX,
                        menu.WindowY) }, editor.FileDialogEntries.Count == 0
                        ? _.H("div", new() { className = "file-empty" }, Text("No folders or supported images here."))
                        : Each(editor.FileDialogEntries, entry => (Node)Row(editor, entry))), Foot(editor, saving, project, svg, title)), Show(editor.IsFileMenuOpen, _.H("div", new() { className = "file-menu-backdrop", OnClick = () => editor.CloseFileContextMenu() })), Show(editor.IsFileMenuOpen, ContextMenu(editor)), Show(editor.IsFilePromptOpen, Prompt(editor)));
    }

    /// <summary>Builds the dialog header.</summary>
    private static Node Head(PaintSession editor, bool saving, string title, string summary) =>
        _.H("div", new() { className = "file-head" }, _.H("span", new() { className = "file-head-icon" }, Ui.Icon(saving ? Icons.Save : Icons.FolderOpen)), _.H("div", new() { className = "file-head-titles" }, _.H("span", new() { className = "file-title" }, Text(title)), _.H("span", new() { className = "file-sub" }, Text(summary))), Ui.Tip(editor,
                Ui.IconButton(Icons.Close, "Close the file browser", () =>
                {
                    editor.CloseFileDialog();
                    editor.Refresh?.Invoke();
                }, className: "icon-btn file-close"),
                Icons.Close, "Close Browser", ""));

    /// <summary>Builds the folder path and shortcut toolbar.</summary>
    private static Node Tools(PaintSession editor) =>
        _.H("div", new() { className = "file-tools" }, _.H("div", new() { className = "file-path-row" }, Ui.ButtonIcon(Icons.Up, "Up", "Parent folder", editor.FileDialogGoUp), _.H("input", new() { className = "text-field file-path", Type = "text", Value = editor.FileDialogFolder, Placeholder = "Folder path; press Enter to go", OnChange = editor.FileDialogNavigate })), _.H("div", new() { className = "file-places" }, Each(editor.FileDialogPlaces, place => (Node)Button(new()
                {
                    className = "file-place",
                    Title = place.Path,
                    OnClick = () => editor.FileDialogUsePlace(place),
                }, Text(place.Label)))));

    /// <summary>Builds the name, format, status and action footer.</summary>
    private static Node Foot(
        PaintSession editor,
        bool saving,
        bool project,
        bool svg,
        string title) =>
        _.H("div", new() { className = "file-foot" }, _.H("div", new() { className = "file-name-row" }, _.H("span", new() { className = "file-label" }, Text("File name")), _.H("input", new() { className = "text-field file-name", Type = "text", Value = editor.FileDialogName, Placeholder = saving
                        ? svg
                            ? "drawing.svg"
                            : project ? "project-name.pxd" : "image-name.ext"
                        : "Choose a file above", OnInput = editor.FileDialogSetName })), Show(saving && !project && !svg, Formats(editor)), _.H("div", new() { className = "file-target" }, Text(editor.FileDialogTarget)), Show(editor.FileDialogError.Length > 0,
                _.H("div", new() { className = "file-error" }, Text(editor.FileDialogError))), _.H("div", new() { className = "file-actions" }, _.H("span", new() { className = "file-hint" }, Text("Right-click a row for file actions")), Ui.Button("Cancel", "Close the file browser", () =>
                {
                    editor.CloseFileDialog();
                    editor.Refresh?.Invoke();
                }), Ui.Button(saving ? "Save" : "Open", title, () =>
                {
                    editor.ConfirmFileDialog();
                    editor.Refresh?.Invoke();
                })));

    /// <summary>Builds one folder or file row.</summary>
    private static Node Row(PaintSession editor, FileEntry entry)
    {
        bool selected = !entry.IsDirectory
            && string.Equals(editor.FileDialogSelected, entry.FullPath, StringComparison.OrdinalIgnoreCase);

        string classes = "file-row";
        if (entry.IsDirectory)
        {
            classes += " is-dir";
        }

        if (selected)
        {
            classes += " is-selected";
        }

        return _.H("button", new() { className = classes, Title = entry.FullPath, OnClick = () => editor.FileDialogActivate(entry), OnContextMenu = menu => editor.OpenFileContextMenu(
                entry,
                menu.WindowX,
                menu.WindowY) }, Ui.Icon(entry.IsDirectory ? Icons.Folder : Icons.Document, "file-row-icon"), _.H("span", new() { className = "file-row-name" }, Text(entry.Name)));
    }

    /// <summary>Builds the save-format selector.</summary>
    private static Node Formats(PaintSession editor) =>
        _.H("div", new() { className = "file-formats" }, _.H("span", new() { className = "file-label" }, Text("Format")), Each(new[]
            {
                ImageFormat.Png,
                ImageFormat.Jpeg,
                ImageFormat.Bmp,
                ImageFormat.Tga,
            }, format => (Node)Button(new()
            {
                className = editor.FileDialogFormat == format ? "btn is-on" : "btn",
                Title = "Save as " + ImageExporter.NameOf(format),
                OnClick = () => editor.FileDialogSetFormat(format),
            }, Text(ImageExporter.NameOf(format)))));

    /// <summary>Builds the row context menu.</summary>
    private static Node ContextMenu(PaintSession editor)
    {
        bool target = editor.FileMenuHasTarget;
        bool clipboard = editor.HasFileClipboard;

        return _.H("div", new() { className = "file-menu", Attributes = new()
            {
                ["style"] =
                    $"left:{Ui.Number(editor.FileMenuX)}px;"
                    + $"top:{Ui.Number(editor.FileMenuY)}px;",
            } }, Show(target, MenuItem(
                editor.FileMenuIsDirectory ? Icons.FolderOpen : Icons.Document,
                editor.FileMenuIsDirectory ? "Open folder" : "Open",
                editor.FileMenuOpen)), Show(target, _.H("div", new() { className = "file-menu-sep" })), Show(target, MenuItem(Icons.Scissors, "Cut", editor.FileMenuCut)), Show(target, MenuItem(Icons.Duplicate, "Copy", editor.FileMenuCopy)), MenuItem(Icons.Paste, "Paste", editor.FileMenuPaste, clipboard), Show(target, _.H("div", new() { className = "file-menu-sep" })), Show(target, MenuItem(Icons.Pen, "Rename", editor.FileMenuRename)), Show(target, MenuItem(Icons.Delete, "Delete", editor.FileMenuDelete, className: "is-danger")), Show(target, _.H("div", new() { className = "file-menu-sep" })), MenuItem(Icons.FolderPlus, "New folder", editor.FileMenuNewFolder), MenuItem(Icons.Redo, "Refresh", editor.FileMenuRefresh));
    }

    /// <summary>Builds one context-menu action.</summary>
    private static Node MenuItem(
        string icon,
        string label,
        Action onClick,
        bool enabled = true,
        string className = "") =>
        _.H("button", new() { className = className.Length == 0 ? "file-menu-item" : "file-menu-item " + className, Disabled = !enabled, OnClick = onClick }, Ui.Icon(icon, "file-menu-icon"), _.H("span", new() { className = "file-menu-label" }, Text(label)));

    /// <summary>Builds the rename / new-folder prompt.</summary>
    private static Node Prompt(PaintSession editor) =>
        _.H("div", new() { className = "file-prompt-layer" }, _.H("div", new() { className = "file-prompt" }, _.H("span", new() { className = "file-prompt-title" }, Text(editor.FilePromptTitle)), _.H("input", new() { className = "text-field file-prompt-input", Type = "text", Value = editor.FilePromptValue, Placeholder = "Name", OnInput = editor.FilePromptSetValue }), _.H("div", new() { className = "file-prompt-actions" }, Ui.Button("Cancel", "Close without applying", () =>
                    {
                        editor.FilePromptCancel();
                        editor.Refresh?.Invoke();
                    }), Ui.Button("Apply", "Apply the name", () =>
                    {
                        editor.FilePromptConfirm();
                        editor.Refresh?.Invoke();
                    }))));
}