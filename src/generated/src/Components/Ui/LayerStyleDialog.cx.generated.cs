using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Non-destructive shadow, stroke and glow settings for one layer.</summary>
public sealed class LayerStyleDialog : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (!editor.IsLayerStyleOpen || editor.LayerStyleLayer is not { } layer)
        {
            return _.H("span", new() { });
        }

        LayerStyle style = layer.Style;

        return _.H("div", new() { ClassName = "resize-layer" }, _.H("div", new() { ClassName = "resize-panel style-panel" }, _.H("div", new() { ClassName = "resize-head" }, _.H("span", new() { ClassName = "resize-head-icon" }, Ui.Icon(Icons.Sparkles)), _.H("div", new() { ClassName = "resize-head-titles" }, _.H("span", new() { ClassName = "resize-title" }, Text("Layer Style")), _.H("span", new() { ClassName = "resize-sub" }, Text(layer.Name + " - non-destructive effects"))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close", () =>
                        {
                            editor.CloseLayerStyleDialog();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn"),
                        Icons.Close, "Close", "")), _.H("div", new() { ClassName = "style-body" }, Section(
                        editor,
                        "Drop Shadow",
                        style.DropShadow,
                        () =>
                        {
                            style.DropShadow = !style.DropShadow;
                            editor.TouchLayerStyle();
                        },
                        SectionRows(
                            editor,
                            Ui.Slider("Opacity", style.DropShadowOpacity * 100f, 0f, 100f, 1f, " %", value =>
                            {
                                style.DropShadowOpacity = Ui.Parse(value, 0f, 100f) / 100f;
                                editor.TouchLayerStyle();
                            }),
                            Ui.Slider("Distance", style.DropShadowDistance, 0f, 80f, 1f, " px", value =>
                            {
                                style.DropShadowDistance = Ui.Parse(value, 0f, 80f);
                                editor.TouchLayerStyle();
                            }),
                            Ui.Slider("Angle", style.DropShadowAngle, 0f, 360f, 1f, " deg", value =>
                            {
                                style.DropShadowAngle = Ui.Parse(value, 0f, 360f);
                                editor.TouchLayerStyle();
                            }),
                            Ui.Slider("Blur", style.DropShadowBlur, 0f, 40f, 1f, " px", value =>
                            {
                                style.DropShadowBlur = Ui.Parse(value, 0f, 40f);
                                editor.TouchLayerStyle();
                            }),
                            Colors(editor, style.DropShadowColor, color =>
                            {
                                style.DropShadowColor = color;
                                editor.TouchLayerStyle();
                            }))), Section(
                        editor,
                        "Stroke",
                        style.Stroke,
                        () =>
                        {
                            style.Stroke = !style.Stroke;
                            editor.TouchLayerStyle();
                        },
                        SectionRows(
                            editor,
                            Ui.Slider("Width", style.StrokeWidth, 1f, 40f, 1f, " px", value =>
                            {
                                style.StrokeWidth = Ui.Parse(value, 1f, 40f);
                                editor.TouchLayerStyle();
                            }),
                            Ui.Slider("Opacity", style.StrokeOpacity * 100f, 0f, 100f, 1f, " %", value =>
                            {
                                style.StrokeOpacity = Ui.Parse(value, 0f, 100f) / 100f;
                                editor.TouchLayerStyle();
                            }),
                            _.H("div", new() { ClassName = "style-option" }, Ui.Toggle(
                                    "Inside",
                                    "Draw the stroke inside the layer edge",
                                    style.StrokeInside,
                                    () =>
                                    {
                                        style.StrokeInside = !style.StrokeInside;
                                        editor.TouchLayerStyle();
                                    })),
                            Colors(editor, style.StrokeColor, color =>
                            {
                                style.StrokeColor = color;
                                editor.TouchLayerStyle();
                            }))), Section(
                        editor,
                        "Outer Glow",
                        style.OuterGlow,
                        () =>
                        {
                            style.OuterGlow = !style.OuterGlow;
                            editor.TouchLayerStyle();
                        },
                        SectionRows(
                            editor,
                            Ui.Slider("Size", style.OuterGlowSize, 0f, 40f, 1f, " px", value =>
                            {
                                style.OuterGlowSize = Ui.Parse(value, 0f, 40f);
                                editor.TouchLayerStyle();
                            }),
                            Ui.Slider("Opacity", style.OuterGlowOpacity * 100f, 0f, 100f, 1f, " %", value =>
                            {
                                style.OuterGlowOpacity = Ui.Parse(value, 0f, 100f) / 100f;
                                editor.TouchLayerStyle();
                            }),
                            Colors(editor, style.OuterGlowColor, color =>
                            {
                                style.OuterGlowColor = color;
                                editor.TouchLayerStyle();
                            })))), _.H("div", new() { ClassName = "resize-foot" }, _.H("span", new() { ClassName = "resize-note" }, Text("Styles redraw from the layer's pixels.")), _.H("div", new() { ClassName = "resize-actions" }, Ui.Button("Done", "Close the style editor", () =>
                        {
                            editor.CloseLayerStyleDialog();
                            editor.Refresh?.Invoke();
                        })))));
    }

    /// <summary>Builds one titled style section with a toggle.</summary>
    private static Node Section(
        PaintSession editor,
        string title,
        bool enabled,
        Action toggle,
        Node content) =>
        _.H("div", new() { ClassName = "style-section" }, _.H("div", new() { ClassName = "style-section-head" }, _.H("span", new() { ClassName = "style-section-title" }, Text(title)), Ui.Toggle(
                    enabled ? "On" : "Off",
                    "Enable " + title,
                    enabled,
                    toggle)), Show(enabled, content));

    /// <summary>Groups the rows of a style section.</summary>
    private static Node SectionRows(PaintSession editor, params Node[] rows) =>
        _.H("div", new() { ClassName = "style-rows" }, rows);

    /// <summary>Builds a row of preset colour swatches.</summary>
    private static Node Colors(PaintSession editor, uint current, Action<uint> pick) =>
        _.H("div", new() { ClassName = "style-swatches" }, _.H("span", new() { ClassName = "resize-label" }, Text("Colour")), Each(new uint[]
            {
                0xFF000000,
                0xFFFFFFFF,
                0xFFE05454,
                0xFF3D7BE0,
                0xFFFFD84D,
            }, color => (Node)Ui.Swatch(
                current == color ? "style-swatch is-on" : "style-swatch",
                color,
                "Use this colour",
                () => pick(color),
                18,
                18)), Ui.Swatch(
                "style-swatch is-foreground",
                editor.Foreground,
                "Use the foreground colour",
                () => pick(editor.Foreground),
                18,
                18));
}