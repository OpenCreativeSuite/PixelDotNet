using System;
using System.Collections.Generic;
using System.Globalization;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Blend mode, opacity, the layer stack and the layer actions.</summary>
public sealed class LayersPanel : Component
{
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
        List<Node> rows = [];

        for (int index = document.Layers.Count - 1; index >= 0; index--)
        {
            rows.Add(Row(editor, document, index));
        }

        return Div(new() { ClassName = "layers-body" },
            Div(new() { ClassName = "layer-props" },
                Div(new() { ClassName = "prop-row" },
                    Span(new() { ClassName = "prop-label" }, Text("Blend")),
                    Ui.Stepper(LabelOf(active.Blend), "Blend mode of the active layer",
                        () => Cycle(editor, -1),
                        () => Cycle(editor, 1))),
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
                        Span(new() { ClassName = "prop-value" }, Text(Ui.Format(active.Opacity * 100f) + "%"))))),
            Div(new() { ClassName = "layer-list" }, [.. rows]),
            Div(new() { ClassName = "layer-actions" },
                Ui.Tip(editor, Ui.IconButton(Icons.Plus, "New layer", editor.AddLayer),
                    Icons.Plus, "New Layer", "layer.new", "Ctrl+Shift+N"),
                Ui.Tip(editor, Ui.IconButton(Icons.Duplicate, "Duplicate the active layer", editor.DuplicateLayer, regular: true),
                    Icons.Duplicate, "Duplicate Layer", "layer.duplicate", "Ctrl+J"),
                Ui.Tip(editor, Ui.IconButton(Icons.MergeDown, "Merge the active layer down", editor.MergeDown, document.ActiveIndex > 0),
                    Icons.MergeDown, "Merge Down", "layer.merge", "Ctrl+E"),
                Div(new() { ClassName = "actions-spacer" }),
                Ui.Tip(editor, Ui.IconButton(Icons.Delete, "Delete the active layer", editor.DeleteActiveLayer, document.Layers.Count > 1, regular: true),
                    Icons.Delete, "Delete Layer", "layer.delete")));
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

    /// <summary>Builds one clickable layer row.</summary>
    private static Node Row(PaintSession editor, PaintDocument document, int index)
    {
        PaintLayer layer = document.Layers[index];
        bool active = index == Math.Clamp(document.ActiveIndex, 0, document.Layers.Count - 1);

        return Div(new()
        {
            ClassName = active ? "layer is-active" : "layer",
            Key = index.ToString(CultureInfo.InvariantCulture),
            OnClick = () => editor.SelectLayer(index),
        },
        Canvas(new CanvasProps
        {
            ClassName = "layer-thumb",
            Attributes = Ui.Size(46, 34),
            OnDraw = context => DrawThumbnail(context, layer),
        }),
        Div(new() { ClassName = "layer-main" },
            Span(new() { ClassName = "layer-name" }, Text(layer.Name)),
            Span(new() { ClassName = "layer-meta" },
                Text($"{LabelOf(layer.Blend)} · {Ui.Format(layer.Opacity * 100f)}%"))),
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
        "layer.visible"));
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

        return alpha <= 0 ? checker : Rgba.Over(checker, source, alpha);
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
