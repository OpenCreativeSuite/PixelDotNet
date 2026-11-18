using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Right-click actions for a layer, a group, or empty layer-list space.</summary>
public sealed class LayerMenu : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsLayerMenuOpen)
        {
            return Span(new());
        }

        string style =
            "left:" + Ui.Number(editor.LayerMenuX) + "px;"
            + "top:" + Ui.Number(editor.LayerMenuY) + "px;";

        return Div(new()
        {
            ClassName = "file-menu",
            Attributes = new() { ["style"] = style },
        }, Items(editor));
    }

    /// <summary>Builds the menu entries for the current target.</summary>
    private static Node[] Items(PaintSession editor)
    {
        PaintDocument document = editor.Document;
        LayerGroup? group = editor.LayerMenuGroup;
        int index = editor.LayerMenuIndex;

        if (group is not null)
        {
            return
            [
                Item(editor, Icons.Plus, "New Layer in Group", "newLayer"),
                Item(
                    editor,
                    group.Expanded ? Icons.ChevronDown : Icons.Next,
                    group.Expanded ? "Collapse Group" : "Expand Group",
                    "expand"),
                Item(editor, Icons.Eye, group.Visible ? "Hide Group" : "Show Group", "visible"),
                Div(new() { ClassName = "file-menu-sep" }),
                Item(editor, Icons.FolderMinus, "Ungroup Group", "ungroup"),
            ];
        }

        if (index >= 0 && index < document.Layers.Count)
        {
            PaintLayer layer = document.Layers[index];
            return
            [
                Item(editor, Icons.Duplicate, "Duplicate Layer", "duplicate"),
                Item(editor, Icons.MergeDown, "Merge Down", "merge", index > 0),
                Div(new() { ClassName = "file-menu-sep" }),
                Item(
                    editor,
                    Icons.Mask,
                    layer.HasMask ? "Remove Mask" : "Add Layer Mask",
                    "mask"),
                Item(
                    editor,
                    Icons.Flatten,
                    layer.ClipToBelow ? "Release Clipping" : "Clip to Below",
                    "clip"),
                Item(
                    editor,
                    layer.Locked ? Icons.Unlock : Icons.Lock,
                    layer.Locked ? "Unlock Layer" : "Lock Layer",
                    "lock"),
                Item(
                    editor,
                    layer.Visible ? Icons.EyeOff : Icons.Eye,
                    layer.Visible ? "Hide Layer" : "Show Layer",
                    "visible"),
                Div(new() { ClassName = "file-menu-sep" }),
                Item(editor, Icons.Sparkles, "Layer Style...", "style"),
                Item(editor, Icons.Rectangle, "Edit Shape...", "shapeEdit", layer.IsShapeLayer),
                Item(editor, Icons.Flatten, "Rasterize Shape", "shapeRaster", layer.IsShapeLayer),
                Item(editor, Icons.PenNib, "Edit Path Nodes", "pathNodes", layer.IsShapeLayer && layer.Shape?.Kind == ShapeKind.Path),
                Item(editor, Icons.Text, "Text on Path...", "textOnPath", layer.IsShapeLayer && layer.Shape?.Kind == ShapeKind.Path),
                Item(editor, Icons.Text, "Edit Text...", "textEdit", layer.IsTextLayer),
                Item(editor, Icons.Flatten, "Rasterize Text", "textRaster", layer.IsTextLayer),
                Item(
                    editor,
                    Icons.SmartObject,
                    layer.IsSmartObject ? "Rasterize Smart Object" : "Convert to Smart Object",
                    layer.IsSmartObject ? "smartRaster" : "smartConvert",
                    !layer.IsAdjustment),
                Item(
                    editor,
                    Icons.Move,
                    "Smart Object Transform...",
                    "smartTransform",
                    layer.IsSmartObject),
                Item(editor, Icons.FolderPlus, "New Group", "group", layer.Group is null),
                Item(editor, Icons.FolderMinus, "Ungroup", "ungroup", layer.Group is not null),
                Div(new() { ClassName = "file-menu-sep" }),
                Item(
                    editor,
                    Icons.Delete,
                    "Delete Layer",
                    "delete",
                    document.Layers.Count > 1,
                    "is-danger"),
            ];
        }

        return
        [
            Item(editor, Icons.Plus, "New Layer", "newLayer"),
            Item(editor, Icons.FolderPlus, "New Group", "newGroup"),
        ];
    }

    /// <summary>Builds one action row.</summary>
    private static Node Item(
        PaintSession editor,
        string icon,
        string label,
        string action,
        bool enabled = true,
        string className = "") =>
        Button(new()
        {
            ClassName = className.Length == 0
                ? "file-menu-item"
                : "file-menu-item " + className,
            Disabled = !enabled,
            OnClick = () =>
            {
                editor.LayerMenuAction(action);
                editor.Refresh?.Invoke();
            },
        },
            Ui.Icon(icon, "file-menu-icon"),
            Span(new() { ClassName = "file-menu-label" }, Text(label)));
}
