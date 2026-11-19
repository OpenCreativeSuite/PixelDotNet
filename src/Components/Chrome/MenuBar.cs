using System;
using Donkeh.UILab;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>One row in a drop-down menu.</summary>
/// <param name="Label">Text shown in the row.</param>
/// <param name="Shortcut">Keyboard hint shown on the right, or empty.</param>
/// <param name="Enabled">Optional predicate that greys the row out.</param>
/// <param name="Run">Action performed when the row is chosen.</param>
/// <param name="StartsGroup">Draws a divider above the row to separate it from the previous group.</param>
/// <param name="Checked">Optional predicate that draws a check when the row's setting is on.</param>
public sealed record MenuAction(string Label, string Shortcut, Func<PaintSession, bool>? Enabled, Action<PaintSession> Run, bool StartsGroup = false, Func<PaintSession, bool>? Checked = null);

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
            new("Open...", "Ctrl+O", NeverDim, session => session.ShowOpenDialog(), StartsGroup: true),
            new("Open Project...", "", NeverDim, session => session.ShowOpenProjectDialog()),
            new("Import SVG...", "", NeverDim, session => session.ShowOpenSvgDialog()),
            new("Save...", "Ctrl+S", NeverDim, session => session.ShowSaveDialog(), StartsGroup: true),
            new("Save Project...", "", NeverDim, session => session.ShowSaveProjectDialog()),
            new("Batch Process...", "", NeverDim, session => session.ShowBatchDialog()),
            new("Export SVG...", "", NeverDim, session => session.ShowSaveSvgDialog()),
            new("Close Document", "Ctrl+W", session => session.CanCloseDocument, session => session.CloseActiveDocument(), StartsGroup: true),
            new("Open Sample", "", NeverDim, session => session.LoadDemo(), StartsGroup: true),
        ]),
        new("Edit",
        [
            new("Undo", "Ctrl+Z", session => session.CanUndo, session => session.Undo()),
            new("Redo", "Ctrl+Y", session => session.CanRedo, session => session.Redo()),
            new("Cut", "Ctrl+X", session => !session.Document.Active.Locked, session => session.Cut(), StartsGroup: true),
            new("Copy", "Ctrl+C", NeverDim, session => session.Copy()),
            new("Copy Merged", "", NeverDim, session => session.CopyMerged()),
            new("Paste", "Ctrl+V", session => session.HasClipboard, session => session.Paste()),
            new("Clear", "Del", session => !session.Document.Active.Locked, session => session.ClearSelectedPixels()),
            new("Content-Aware Fill", "", session => session.Selection is not null && !session.Document.Active.Locked, session => session.ContentAwareFill(), StartsGroup: true),
            new("Perspective Transform...", "", NeverDim, session => session.ShowPerspectiveDialog(), StartsGroup: true),
            new("Mesh Warp...", "", NeverDim, session => session.ShowMeshWarpDialog()),
        ]),
        new("Image",
        [
            new("Invert", "Ctrl+I", session => !session.Document.Active.Locked, session => session.Invert()),
            new("Desaturate", "Ctrl+Shift+U", session => !session.Document.Active.Locked, session => session.Desaturate()),
            new("Flip Horizontal", "", session => !session.Document.Active.Locked, session => session.FlipHorizontal(), StartsGroup: true),
            new("Flip Vertical", "", session => !session.Document.Active.Locked, session => session.FlipVertical()),
            new("Brightness / Contrast", "", NeverDim, session => session.OpenSideTab(2), StartsGroup: true),
            new("Effects", "", NeverDim, session => session.OpenSideTab(4)),
            new("Colour Tools...", "", NeverDim, session => session.ShowColourToolsDialog()),
            new("Image Size...", "", NeverDim, session => session.ShowImageResizeDialog(), StartsGroup: true),
            new("Canvas Size...", "", NeverDim, session => session.ShowCanvasResizeDialog()),
            new("Flatten Image", "", NeverDim, session => session.FlattenImage(), StartsGroup: true),
        ]),
        new("Layer",
        [
            new("New Layer", "Ctrl+Shift+N", NeverDim, session => session.AddLayer()),
            new("Duplicate Layer", "Ctrl+J", NeverDim, session => session.DuplicateLayer()),
            new("New Group", "", session => session.Document.Active.Group is null, session => session.GroupActiveLayer()),
            new("Ungroup", "", session => session.Document.Active.Group is not null, session => session.UngroupActiveLayer()),
            new("Layer Style...", "", NeverDim, session => session.ShowLayerStyleDialog(session.Document.ActiveIndex), StartsGroup: true),
            new("Edit Shape...", "", session => session.Document.Active.IsShapeLayer, session => session.ShowShapeEditDialog()),
            new("Rasterize Shape", "", session => session.Document.Active.IsShapeLayer, session => session.RasterizeShape()),
            new("Edit Path Nodes", "", session => session.CanEditPathNodes, session => session.TogglePathNodeEdit(), StartsGroup: true),
            new("Text on Path...", "", session => session.CanEditPathNodes, session => session.CreateTextOnPath()),
            new("Edit Text...", "", session => session.Document.Active.IsTextLayer, session => session.ShowTextEditDialog()),
            new("Rasterize Text", "", session => session.Document.Active.IsTextLayer, session => session.RasterizeText()),
            new("Convert to Smart Object", "", session => !session.Document.Active.IsSmartObject && !session.Document.Active.IsAdjustment, session => session.ConvertToSmartObject(), StartsGroup: true),
            new("Rasterize Smart Object", "", session => session.Document.Active.IsSmartObject, session => session.RasterizeSmartObject()),
            new("Smart Object Transform...", "", session => session.Document.Active.IsSmartObject, session => session.ShowSmartTransformDialog()),
            new("Merge Down", "Ctrl+E", session => session.Document.ActiveIndex > 0, session => session.MergeDown(), StartsGroup: true),
            new("Delete Layer", "", session => session.Document.Layers.Count > 1, session => session.DeleteActiveLayer()),
        ]),
        new("Select",
        [
            new("Select All", "", NeverDim, session => session.SelectAll()),
            new("Deselect", "Esc", session => session.Selection is not null, session => session.Deselect()),
            new("Fill with Foreground", "", NeverDim, session => session.FillSelection(), StartsGroup: true),
        ]),
        new("View",
        [
            new("Zoom In", "Ctrl++", session => session.Zoom < PaintSession.ZoomLevels[^1], session => session.ZoomIn()),
            new("Zoom Out", "Ctrl+-", session => session.Zoom > PaintSession.ZoomLevels[0], session => session.ZoomOut()),
            new("Actual Pixels", "Ctrl+0", NeverDim, session => session.ActualPixels()),
            new("Toggle Grid", "", NeverDim, session => session.ToggleGrid(), StartsGroup: true, Checked: session => session.ShowGrid),
            new("Toggle Rulers", "", NeverDim, session => session.ToggleRulers(), Checked: session => session.ShowRulers),
            new("Toggle Guides", "", NeverDim, session => session.ToggleGuides(), Checked: session => session.ShowGuides),
            new("Toggle Snapping", "", NeverDim, session => session.ToggleSnapping(), Checked: session => session.SnapToGuides),
            new("Add Horizontal Guide", "", NeverDim, session => session.PlaceGuide(true, session.Document.Height / 2f), StartsGroup: true),
            new("Add Vertical Guide", "", NeverDim, session => session.PlaceGuide(false, session.Document.Width / 2f)),
            new("Clear Guides", "", session => session.Document.Guides.Count > 0, session => session.ClearGuides()),
            new("Next Document", "Ctrl+Tab", session => session.DocumentCount > 1, session => session.NextDocument(), StartsGroup: true),
            new("Previous Document", "Ctrl+Shift+Tab", session => session.DocumentCount > 1, session => session.PreviousDocument()),
        ]),
        new("Help",
        [
            new("Enable Tooltips", "", session => !session.ShowTooltips, session => session.ToggleTooltips()),
            new("Disable Tooltips", "", session => session.ShowTooltips, session => session.ToggleTooltips()),
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
                Ui.Tip(editor,
                    Ui.IconButton(Icons.Undo, "Undo the last step (Z)", editor.Undo, editor.CanUndo, className: "quick-btn"),
                    Icons.Undo, "Undo", "quick.undo", "Z"),
                Ui.Tip(editor,
                    Ui.IconButton(Icons.Redo, "Redo the last undone step (Y)", editor.Redo, editor.CanRedo, className: "quick-btn"),
                    Icons.Redo, "Redo", "quick.redo", "Y"),
                Div(new() { ClassName = "quick-sep" }),
                Ui.Tip(editor,
                    Ui.IconButton(Icons.Save, "Choose where to save the image", editor.ShowSaveDialog, className: "quick-btn is-primary"),
                    Icons.Save, "Save Image", "quick.save", "Ctrl+S")));
    }

    /// <summary>Builds one menu title plus its drop-down.</summary>
    private static Node Menu(PaintSession editor, MenuDefinition definition)
    {
        bool open = editor.OpenMenu == definition.Title;
        Node title = Button(new()
        {
            ClassName = open ? "menu-title is-open" : "menu-title",
            OnClick = () =>
            {
                editor.CloseFontMenu();
                editor.ToggleMenu(definition.Title);
            },
            // Once a menu is open, sweeping across the titles swaps the open
            // drop-down instead of requiring another click.
            OnMouseOver = () => editor.HoverMenu(definition.Title),
        }, Text(definition.Title));

        return Div(new() { ClassName = "menu" },
            title,
            Show(open, Div(new() { ClassName = "menu-list" }, MenuItems(editor, definition))));
    }

    /// <summary>Builds the rows of a drop-down, inserting dividers between groups.</summary>
    private static Node[] MenuItems(PaintSession editor, MenuDefinition definition)
    {
        List<Node> rows = new(definition.Items.Length * 2);
        foreach (MenuAction item in definition.Items)
        {
            if (item.StartsGroup && rows.Count > 0)
            {
                rows.Add(Div(new() { ClassName = "menu-sep" }));
            }

            bool on = item.Checked is not null && item.Checked(editor);
            rows.Add(Button(new()
            {
                ClassName = "menu-item",
                Disabled = item.Enabled is not null && !item.Enabled(editor),
                OnClick = () =>
                {
                    editor.CloseMenu();
                    item.Run(editor);
                },
            },
            Span(new() { ClassName = "menu-item-check" }, Show(on, Ui.Icon(Icons.Check))),
            Span(new() { ClassName = "menu-item-label" }, Text(item.Label)),
            Span(new() { ClassName = "menu-item-key" }, Text(item.Shortcut))));
        }

        return rows.ToArray();
    }
}
