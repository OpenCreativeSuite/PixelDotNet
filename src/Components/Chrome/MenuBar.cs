using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>One row in a drop-down menu.</summary>
/// <param name="Label">Text shown in the row.</param>
/// <param name="Shortcut">Keyboard hint shown on the right, or empty.</param>
/// <param name="Enabled">Optional predicate that greys the row out.</param>
/// <param name="Run">Action performed when the row is chosen.</param>
public sealed record MenuAction(string Label, string Shortcut, Func<PaintSession, bool>? Enabled, Action<PaintSession> Run);

/// <summary>A titled drop-down.</summary>
public sealed record MenuDefinition(string Title, MenuAction[] Items);

/// <summary>Top bar: the application menus and quick actions.</summary>
public sealed class MenuBar : Component
{
    private static readonly Func<PaintSession, bool>? NeverDim = null;

    private static readonly MenuDefinition[] Menus =
    [
        new("File",
        [
            new("New Document", "Ctrl+N", NeverDim, session => session.NewDocument(768, 576)),
            new("New Small Document", "", NeverDim, session => session.NewDocument(480, 360)),
            new("Close Document", "Ctrl+W", session => session.CanCloseDocument, session => session.CloseActiveDocument()),
            new("Save PNG", "Ctrl+S", NeverDim, session => session.SaveImage(ImageFormat.Png)),
            new("Save JPEG", "", NeverDim, session => session.SaveImage(ImageFormat.Jpeg)),
            new("Save BMP", "", NeverDim, session => session.SaveImage(ImageFormat.Bmp)),
            new("Save TGA", "", NeverDim, session => session.SaveImage(ImageFormat.Tga)),
            new("Open Sample", "", NeverDim, session => session.LoadDemo()),
        ]),
        new("Edit",
        [
            new("Undo", "Ctrl+Z", session => session.CanUndo, session => session.Undo()),
            new("Redo", "Ctrl+Y", session => session.CanRedo, session => session.Redo()),
            new("Cut", "Ctrl+X", session => !session.Document.Active.Locked, session => session.Cut()),
            new("Copy", "Ctrl+C", NeverDim, session => session.Copy()),
            new("Copy Merged", "", NeverDim, session => session.CopyMerged()),
            new("Paste", "Ctrl+V", session => session.HasClipboard, session => session.Paste()),
            new("Clear", "Del", session => !session.Document.Active.Locked, session => session.ClearSelectedPixels()),
        ]),
        new("Image",
        [
            new("Invert", "Ctrl+I", session => !session.Document.Active.Locked, session => session.Invert()),
            new("Desaturate", "Ctrl+Shift+U", session => !session.Document.Active.Locked, session => session.Desaturate()),
            new("Flip Horizontal", "", session => !session.Document.Active.Locked, session => session.FlipHorizontal()),
            new("Flip Vertical", "", session => !session.Document.Active.Locked, session => session.FlipVertical()),
            new("Brightness / Contrast", "", NeverDim, session => session.OpenSideTab(2)),
            new("Effects", "", NeverDim, session => session.OpenSideTab(4)),
            new("Flatten Image", "", NeverDim, session => session.FlattenImage()),
        ]),
        new("Layer",
        [
            new("New Layer", "Ctrl+Shift+N", NeverDim, session => session.AddLayer()),
            new("Duplicate Layer", "Ctrl+J", NeverDim, session => session.DuplicateLayer()),
            new("Merge Down", "Ctrl+E", session => session.Document.ActiveIndex > 0, session => session.MergeDown()),
            new("Delete Layer", "", session => session.Document.Layers.Count > 1, session => session.DeleteActiveLayer()),
        ]),
        new("Select",
        [
            new("Select All", "", NeverDim, session => session.SelectAll()),
            new("Deselect", "Esc", session => session.Selection is not null, session => session.Deselect()),
            new("Fill with Foreground", "", NeverDim, session => session.FillSelection()),
        ]),
        new("View",
        [
            new("Zoom In", "Ctrl++", session => session.Zoom < PaintSession.ZoomLevels[^1], session => session.ZoomIn()),
            new("Zoom Out", "Ctrl+-", session => session.Zoom > PaintSession.ZoomLevels[0], session => session.ZoomOut()),
            new("Actual Pixels", "Ctrl+0", NeverDim, session => session.ActualPixels()),
            new("Toggle Grid", "", NeverDim, session => session.ToggleGrid()),
            new("Next Document", "Ctrl+Tab", session => session.DocumentCount > 1, session => session.NextDocument()),
            new("Previous Document", "Ctrl+Shift+Tab", session => session.DocumentCount > 1, session => session.PreviousDocument()),
        ]),
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);

        return Div(new() { ClassName = "menubar" },
            Div(new() { ClassName = "brand" },
                Div(new() { ClassName = "brand-mark" },
                    Span(new() { ClassName = "brand-px brand-px-a" }),
                    Span(new() { ClassName = "brand-px brand-px-b" }),
                    Span(new() { ClassName = "brand-px brand-px-c" }),
                    Span(new() { ClassName = "brand-px brand-px-d" })),
                Span(new() { ClassName = "brand-name" }, Text("PixelDotNet"))),
            Each(Menus, menu => (Node)Menu(editor, menu)),
            Div(new() { ClassName = "menu-spacer" }),
            Div(new() { ClassName = "quick" },
                Ui.IconButton(Icons.Undo, "Undo the last step (Z)", editor.Undo, editor.CanUndo, className: "quick-btn"),
                Ui.IconButton(Icons.Redo, "Redo the last undone step (Y)", editor.Redo, editor.CanRedo, className: "quick-btn"),
                Div(new() { ClassName = "quick-sep" }),
                Ui.IconButton(Icons.Save, "Write a PNG to your Pictures folder", editor.SavePng, className: "quick-btn is-primary")));
    }

    /// <summary>Builds one menu title plus its drop-down.</summary>
    private static Node Menu(PaintSession editor, MenuDefinition definition)
    {
        bool open = editor.OpenMenu == definition.Title;

        return Div(new() { ClassName = "menu" },
            Button(new()
            {
                ClassName = open ? "menu-title is-open" : "menu-title",
                OnClick = () =>
                {
                    editor.CloseFontMenu();
                    editor.ToggleMenu(definition.Title);
                },
            }, Text(definition.Title)),
            Show(open, Div(new() { ClassName = "menu-list" },
                Each(definition.Items, item => (Node)Button(new()
                {
                    ClassName = "menu-item",
                    Disabled = item.Enabled is not null && !item.Enabled(editor),
                    OnClick = () =>
                    {
                        editor.CloseMenu();
                        item.Run(editor);
                    },
                },
                Span(new() { ClassName = "menu-item-label" }, Text(item.Label)),
                Span(new() { ClassName = "menu-item-key" }, Text(item.Shortcut)))))));
    }
}
