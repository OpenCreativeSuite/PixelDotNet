using System;
using System.Collections.Generic;
using System.Globalization;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Blend mode, opacity, the layer stack and the layer actions.</summary>
public sealed class LayersPanel : Component
{
    private const string LayerDragFormat = "application/x-pixeldotnet-layer";

    private static readonly (BlendMode Mode, string Label)[] BlendModes =
    [
        (BlendMode.Normal, "Normal"),
        (BlendMode.Multiply, "Multiply"),
        (BlendMode.Screen, "Screen"),
        (BlendMode.Overlay, "Overlay"),
        (BlendMode.Darken, "Darken"),
        (BlendMode.Lighten, "Lighten"),
        (BlendMode.LinearDodge, "Linear Dodge"),
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        PaintDocument document = editor.Document;
        PaintLayer active = document.Active;
        LayerGroup? activeGroup = active.Group;
        List<Node> rows = [];
        HashSet<LayerGroup> groups = [];

        for (int index = document.Layers.Count - 1; index >= 0; index--)
        {
            PaintLayer layer = document.Layers[index];
            LayerGroup? group = layer.Group;

            if (group is not null && groups.Add(group))
            {
                rows.Add(GroupRow(editor, document, group));
            }

            if (group is null || group.Expanded)
            {
                rows.Add(Row(editor, document, index, group is not null));
            }
        }

        return Div(new() { ClassName = "layers-body" },
            Div(new() { ClassName = "layer-props" },
                Div(new() { ClassName = "prop-row" },
                    Span(new() { ClassName = "prop-label" }, Text("Blend")),
                    Ui.Stepper(LabelOf(active.Blend), "Blend mode of the active layer",
                        () => Cycle(editor, -1),
                        () => Cycle(editor, 1))),
                Div(new() { ClassName = "prop-row" },
                    Span(new() { ClassName = "prop-label" }, Text("Clip")),
                    Ui.Toggle("Below", "Clip this layer to the visible content below it", active.ClipToBelow,
                        editor.ToggleClipToBelow)),
                Div(new() { ClassName = "prop-row" },
                    Span(new() { ClassName = "prop-label" }, Text("Opacity")),
                    Div(new() { ClassName = "prop-slider" },
                        Input(new()
                        {
                            ClassName = "prop-range",
                            Type = "range",
                            Value = Ui.Number(active.Opacity * 100f),
                            Attributes = new()
                            {
                                ["min"] = "0",
                                ["max"] = "100",
                                ["step"] = "1",
                            },
                            OnInput = value => editor.SetLayerOpacity(Ui.Parse(value, 0f, 100f) / 100f),
                        }),
                        Span(new() { ClassName = "prop-value" }, Text(Ui.Format(active.Opacity * 100f) + "%")))),
                Show(activeGroup is not null, Div(new() { ClassName = "prop-row" },
                    Span(new() { ClassName = "prop-label" }, Text("Group")),
                    Div(new() { ClassName = "prop-slider" },
                        Input(new()
                        {
                            ClassName = "prop-range",
                            Type = "range",
                            Value = Ui.Number((activeGroup?.Opacity ?? 1f) * 100f),
                            Attributes = new()
                            {
                                ["min"] = "0",
                                ["max"] = "100",
                                ["step"] = "1",
                            },
                            OnInput = value =>
                            {
                                if (activeGroup is not null)
                                {
                                    editor.SetGroupOpacity(
                                        activeGroup,
                                        Ui.Parse(value, 0f, 100f) / 100f);
                                }
                            },
                        }),
                        Span(new() { ClassName = "prop-value" },
                            Text(Ui.Format((activeGroup?.Opacity ?? 1f) * 100f) + "%")))))),
            Div(new()
            {
                ClassName = "layer-list",
                OnDragOver = drag =>
                {
                    if (drag.Data.HasData(LayerDragFormat))
                    {
                        drag.Data.DropEffect = "move";
                        drag.PreventDefault();
                    }
                },
                OnDrop = drag =>
                {
                    if (int.TryParse(drag.Data.GetData(LayerDragFormat), out int source))
                    {
                        editor.SetLayerDropTarget(string.Empty);
                        editor.MoveLayerToRoot(source);
                    }
                },
                OnContextMenu = menu => editor.OpenLayerMenu(-1, null, menu.WindowX, menu.WindowY),
            }, [.. rows]),
            Div(new() { ClassName = "layer-actions" },
                Ui.Tip(editor, Ui.IconButton(Icons.Plus, "New layer", editor.AddLayer),
                    Icons.Plus, "New Layer", "layer.new", "Ctrl+Shift+N", TipPlacement.Above),
                Ui.Tip(editor, Ui.IconButton(Icons.Duplicate, "Duplicate the active layer", editor.DuplicateLayer, regular: true),
                    Icons.Duplicate, "Duplicate Layer", "layer.duplicate", "Ctrl+J", TipPlacement.Above),
                Ui.Tip(editor, Ui.IconButton(Icons.MergeDown, "Merge the active layer down", editor.MergeDown, document.ActiveIndex > 0),
                    Icons.MergeDown, "Merge Down", "layer.merge", "Ctrl+E", TipPlacement.Above),
                Ui.Tip(editor, Ui.IconButton(Icons.FolderPlus, "Put the active layer in a new group", editor.GroupActiveLayer, activeGroup is null),
                    Icons.FolderPlus, "New Group", "layer.group", "", TipPlacement.Above),
                Ui.Tip(editor, Ui.IconButton(Icons.FolderMinus, "Take the active layer out of its group", editor.UngroupActiveLayer, activeGroup is not null),
                    Icons.FolderMinus, "Ungroup", "layer.ungroup", "", TipPlacement.Above),
                Ui.Tip(editor, Ui.IconButton(
                        Icons.Mask,
                        active.HasMask ? "Edit or remove the layer mask" : "Add a layer mask",
                        () =>
                        {
                            if (active.HasMask)
                            {
                                editor.ToggleMaskEditing(document.ActiveIndex);
                            }
                            else
                            {
                                editor.ToggleLayerMask();
                            }
                        }),
                    Icons.Mask,
                    active.HasMask ? "Layer Mask" : "Add Layer Mask",
                    "layer.mask",
                    "",
                    TipPlacement.Above),
                Div(new() { ClassName = "actions-spacer" }),
                Ui.Tip(editor, Ui.IconButton(Icons.Delete, "Delete the active layer", editor.DeleteActiveLayer, document.Layers.Count > 1, regular: true),
                    Icons.Delete, "Delete Layer", "layer.delete", "", TipPlacement.Above)));
    }

    /// <summary>Steps the active layer's blend mode through the list.</summary>
    private static void Cycle(PaintSession editor, int direction)
    {
        BlendMode current = editor.Document.Active.Blend;
        int index = 0;

        for (int i = 0; i < BlendModes.Length; i++)
        {
            if (BlendModes[i].Mode == current)
            {
                index = i;
                break;
            }
        }

        index = ((index + direction) % BlendModes.Length + BlendModes.Length) % BlendModes.Length;
        editor.SetLayerBlend(BlendModes[index].Mode);
    }

    /// <summary>Builds the header row for a layer group.</summary>
    private static Node GroupRow(PaintSession editor, PaintDocument document, LayerGroup group)
    {
        bool active = ReferenceEquals(document.Active.Group, group);
        int count = CountGroup(document, group);
        string dropKey = "group:" + group.Name;
        string className = active ? "layer-group is-active" : "layer-group";
        if (editor.LayerDropTarget == dropKey)
        {
            className += " is-drop-target";
        }

        return Div(new()
        {
            ClassName = className,
            Title = group.Name,
            OnClick = () => editor.SelectGroup(group),
            OnDragEnter = drag =>
            {
                if (drag.Data.HasData(LayerDragFormat))
                {
                    editor.SetLayerDropTarget(dropKey);
                }
            },
            OnDragLeave = _ => editor.SetLayerDropTarget(string.Empty),
            OnDragOver = drag =>
            {
                if (drag.Data.HasData(LayerDragFormat))
                {
                    drag.Data.DropEffect = "move";
                    drag.PreventDefault();
                }
            },
            OnDrop = drag =>
            {
                editor.SetLayerDropTarget(string.Empty);
                if (int.TryParse(drag.Data.GetData(LayerDragFormat), out int source))
                {
                    editor.MoveLayerIntoGroup(source, group);
                }
            },
            OnContextMenu = menu => editor.OpenLayerMenu(-1, group, menu.WindowX, menu.WindowY),
        },
            Ui.Tip(editor, Button(new()
            {
                ClassName = "layer-group-chevron",
                Title = group.Expanded ? "Collapse the group" : "Expand the group",
                OnClick = () => editor.ToggleGroupExpanded(group),
            }, Ui.Icon(group.Expanded ? Icons.ChevronDown : Icons.Next)),
                Icons.ChevronDown, "Expand Group", "layer.expand"),
            Span(new() { ClassName = "layer-group-icon" }, Ui.Icon(Icons.Folder)),
            Div(new() { ClassName = "layer-main" },
                Span(new() { ClassName = "layer-name" }, Text(group.Name)),
                Span(new() { ClassName = "layer-meta" },
                    Text($"{count} layer{(count == 1 ? string.Empty : "s")} | "
                        + Ui.Format(group.Opacity * 100f) + "%"))),
            Ui.Tip(editor, Button(new()
            {
                ClassName = group.Visible ? "layer-btn" : "layer-btn is-dim",
                Title = group.Visible ? "Hide the group" : "Show the group",
                OnClick = () => editor.ToggleGroupVisibility(group),
            }, Ui.Icon(group.Visible ? Icons.Eye : Icons.EyeOff, regular: true)),
                group.Visible ? Icons.Eye : Icons.EyeOff,
                group.Visible ? "Hide Group" : "Show Group",
                "layer.groupVisible"));
    }

    /// <summary>Counts the layers inside a group.</summary>
    private static int CountGroup(PaintDocument document, LayerGroup group)
    {
        int count = 0;
        for (int i = 0; i < document.Layers.Count; i++)
        {
            if (ReferenceEquals(document.Layers[i].Group, group))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Builds one clickable layer row.</summary>
    private static Node Row(PaintSession editor, PaintDocument document, int index, bool child)
    {
        PaintLayer layer = document.Layers[index];
        bool active = index == Math.Clamp(document.ActiveIndex, 0, document.Layers.Count - 1);
        string className = active ? "layer is-active" : "layer";
        if (child)
        {
            className += " is-child";
        }

        string aboveKey = "layer:" + index + ":above";
        string belowKey = "layer:" + index + ":below";
        bool showAbove = editor.LayerDropTarget == aboveKey;
        bool showBelow = editor.LayerDropTarget == belowKey;

        return Div(new()
        {
            ClassName = className,
            Key = index.ToString(CultureInfo.InvariantCulture),
            Draggable = true,
            OnClick = () => editor.SelectLayer(index),
            OnContextMenu = menu => editor.OpenLayerMenu(index, null, menu.WindowX, menu.WindowY),
            OnDragStart = drag =>
            {
                drag.Data.SetData(LayerDragFormat, index.ToString(CultureInfo.InvariantCulture));
                drag.Data.EffectAllowed = "move";
            },
            OnDragEnd = _ => editor.SetLayerDropTarget(string.Empty),
            OnDragEnter = drag =>
            {
                if (drag.Data.HasData(LayerDragFormat))
                {
                    editor.SetLayerDropTarget(aboveKey);
                }
            },
            OnDragLeave = _ => editor.SetLayerDropTarget(string.Empty),
            OnDragOver = drag =>
            {
                if (drag.Data.HasData(LayerDragFormat))
                {
                    editor.SetLayerDropTarget(
                        drag.LocalY < 18f ? aboveKey : belowKey);
                    drag.Data.DropEffect = "move";
                    drag.PreventDefault();
                }
            },
            OnDrop = drag =>
            {
                editor.SetLayerDropTarget(string.Empty);
                if (int.TryParse(drag.Data.GetData(LayerDragFormat), out int source))
                {
                    editor.MoveLayerTo(source, index, drag.LocalY < 18f);
                }
            },
        },
        layer.IsAdjustment
            ? Div(new() { ClassName = "layer-adjust-thumb" }, Ui.Icon(Icons.Sliders))
            : Canvas(new CanvasProps
            {
                ClassName = "layer-thumb",
                Attributes = Ui.Size(46, 34),
                OnDraw = context => DrawThumbnail(context, layer),
            }),
        Show(layer.HasMask, Canvas(new CanvasProps
        {
            ClassName = active && editor.MaskEditing ? "layer-mask-thumb is-active" : "layer-mask-thumb",
            Title = "Click to edit the mask, right-click to remove it",
            Attributes = Ui.Size(30, 34),
            OnClick = () => editor.ToggleMaskEditing(index),
            OnContextMenu = _ => editor.ToggleLayerMask(),
            OnDraw = context => DrawMaskThumbnail(context, layer),
        })),
        Div(new() { ClassName = "layer-main" },
            Span(new() { ClassName = "layer-name" }, Text(layer.Name)),
            Span(new() { ClassName = "layer-meta" },
                Text(layer.IsAdjustment
                    ? $"{layer.Adjustment} adjustment | {Ui.Format(layer.Opacity * 100f)}%"
                    : $"{LabelOf(layer.Blend)} | {Ui.Format(layer.Opacity * 100f)}%" + (layer.ClipToBelow ? " | clipped" : string.Empty)))),
        Ui.Tip(editor, Button(new()
        {
            ClassName = layer.Locked ? "layer-btn is-on" : "layer-btn is-dim",
            Title = layer.Locked ? "Unlock the layer" : "Lock the layer",
            OnClick = () => editor.ToggleLock(index),
        }, Ui.Icon(layer.Locked ? Icons.Lock : Icons.Unlock)),
        layer.Locked ? Icons.Lock : Icons.Unlock,
        layer.Locked ? "Unlock Layer" : "Lock Layer",
        "layer.lock"),
        Ui.Tip(editor, Button(new()
        {
            ClassName = layer.Visible ? "layer-btn" : "layer-btn is-dim",
            Title = layer.Visible ? "Hide the layer" : "Show the layer",
            OnClick = () => editor.ToggleLayer(index),
        }, Ui.Icon(layer.Visible ? Icons.Eye : Icons.EyeOff, regular: true)),
        layer.Visible ? Icons.Eye : Icons.EyeOff,
        layer.Visible ? "Hide Layer" : "Show Layer",
        "layer.visible"),
        Show(showAbove, Div(new() { ClassName = "drop-line is-above" })),
        Show(showBelow, Div(new() { ClassName = "drop-line is-below" })));
    }

    /// <summary>
    /// Downsamples one layer into the thumbnail canvas over a checkerboard, merging
    /// equal colours into horizontal spans so a flat layer costs a few rectangles.
    /// </summary>
    private static void DrawThumbnail(CanvasContext context, PaintLayer layer)
    {
        const int ThumbWidth = 46;
        const int ThumbHeight = 34;
        const int Cell = 4;

        float stepX = (float)layer.Width / ThumbWidth;
        float stepY = (float)layer.Height / ThumbHeight;
        int opacity = (int)MathF.Round(Math.Clamp(layer.Opacity, 0f, 1f) * 255f);

        if (!layer.Visible)
        {
            opacity /= 3;
        }

        for (int row = 0; row < ThumbHeight; row++)
        {
            int sourceY = Math.Min(layer.Height - 1, (int)(row * stepY));
            int sourceRow = sourceY * layer.Width;
            int runStart = 0;
            uint runColor = Sample(layer, sourceRow, 0, row, stepX, opacity, Cell);

            for (int column = 1; column < ThumbWidth; column++)
            {
                uint next = Sample(layer, sourceRow, column, row, stepX, opacity, Cell);
                if (next == runColor)
                {
                    continue;
                }

                context.FillRect(runStart, row, column - runStart, 1f, runColor);
                runStart = column;
                runColor = next;
            }

            context.FillRect(runStart, row, ThumbWidth - runStart, 1f, runColor);
        }
    }

    /// <summary>Samples one thumbnail cell over the checkerboard.</summary>
    private static uint Sample(PaintLayer layer, int sourceRow, int column, int row, float stepX, int opacity, int cell)
    {
        uint checker = ((row / cell) + (column / cell)) % 2 == 0 ? 0xFF2A2A2A : 0xFF323232;
        int sourceX = Math.Min(layer.Width - 1, (int)(column * stepX));
        uint source = layer.Pixels[sourceRow + sourceX];
        int alpha = Rgba.A(source) * opacity / 255;

        if (layer.Mask is { } mask)
        {
            alpha = alpha * mask[sourceRow + sourceX] / 255;
        }

        return alpha <= 0 ? checker : Rgba.Over(checker, source, alpha);
    }

    /// <summary>Draws a small greyscale preview of a layer mask.</summary>
    private static void DrawMaskThumbnail(CanvasContext context, PaintLayer layer)
    {
        const int ThumbWidth = 30;
        const int ThumbHeight = 34;
        if (layer.Mask is not { } mask)
        {
            return;
        }

        float stepX = (float)layer.Width / ThumbWidth;
        for (int row = 0; row < ThumbHeight; row++)
        {
            int sourceY = Math.Min(layer.Height - 1, (int)(row * layer.Height / (float)ThumbHeight));
            int sourceRow = sourceY * layer.Width;
            int runStart = 0;
            uint runColor = MaskSample(mask, layer.Width, sourceRow, 0, stepX);

            for (int column = 1; column < ThumbWidth; column++)
            {
                uint next = MaskSample(mask, layer.Width, sourceRow, column, stepX);
                if (next == runColor)
                {
                    continue;
                }

                context.FillRect(runStart, row, column - runStart, 1f, runColor);
                runStart = column;
                runColor = next;
            }

            context.FillRect(runStart, row, ThumbWidth - runStart, 1f, runColor);
        }
    }

    /// <summary>Reads one greyscale sample for the mask thumbnail.</summary>
    private static uint MaskSample(byte[] mask, int layerWidth, int sourceRow, int column, float stepX)
    {
        int sourceX = Math.Min(layerWidth - 1, (int)(column * stepX));
        int value = mask[sourceRow + sourceX];
        return Rgba.FromRgb(value, value, value);
    }

    /// <summary>Maps a blend mode onto its display name.</summary>
    private static string LabelOf(BlendMode mode)
    {
        foreach ((BlendMode candidate, string label) in BlendModes)
        {
            if (candidate == mode)
            {
                return label;
            }
        }

        return "Normal";
    }
}
