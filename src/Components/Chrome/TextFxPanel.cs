using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>
/// The text effects popup: outline, shadow/glow and a few ready-made presets.
/// </summary>
/// <remarks>
/// Like the font picker this is ordinary DOM rather than a canvas, so the
/// sliders and colour swatches get native input. It opens beside the inspector
/// so the colour panel and the effect controls can be used together.
/// </remarks>
public sealed class TextFxPanel : Component
{
    private static readonly (TextPreset Preset, string Label, string Title)[] Presets =
    [
        (TextPreset.None, "Plain", "Text with no outline or shadow"),
        (TextPreset.Outline, "Outline", "A solid keyline around the glyphs"),
        (TextPreset.Shadow, "Shadow", "A soft drop shadow"),
        (TextPreset.Glow, "Glow", "A centred coloured halo"),
        (TextPreset.Neon, "Neon", "A bright cyan halo with a dark keyline"),
        (TextPreset.Retro, "Retro", "A hard offset shadow with an outline"),
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);

        return Div(new() { ClassName = "text-fx-popup" },
            Div(new() { ClassName = "text-fx-head" },
                Span(new() { ClassName = "text-fx-title" },
                    Ui.Icon(Icons.Sparkles, "text-fx-title-icon"),
                    Text("Text effects")),
                Ui.IconButton(Icons.Close, "Close text effects", () =>
                {
                    editor.CloseTextFx();
                    editor.Refresh?.Invoke();
                }, className: "icon-btn is-tiny")),
            Div(new() { ClassName = "text-fx-body" },
                Ui.Section("Presets",
                    Div(new() { ClassName = "text-fx-presets" }, [.. PresetButtons(editor)]),
                    Span(new() { ClassName = "text-fx-note" },
                        Text("Presets set the effects below; tweak any slider afterwards."))),
                Ui.Section("Outline",
                    Div(new() { ClassName = "text-fx-switch" },
                        Ui.Toggle(
                            editor.TextOutline ? "On" : "Off",
                            "Draw a keyline around the text",
                            editor.TextOutline,
                            () =>
                            {
                                editor.SetTextOutline(!editor.TextOutline);
                                editor.Refresh?.Invoke();
                            }),
                        Ui.Swatch(
                            "fx-swatch",
                            editor.TextOutlineColor,
                            "Outline colour — opens the colour panel",
                            () => EditColour(editor, TextColorTarget.Outline),
                            28,
                            20)),
                    Div(new() { ClassName = editor.TextOutline ? "text-fx-controls" : "text-fx-controls is-off" },
                        Ui.Slider("Width", editor.TextOutlineWidth, 1f, 24f, 1f, " px", value =>
                        {
                            editor.SetTextOutlineWidth(Ui.Parse(value, 1f, 24f));
                            editor.Refresh?.Invoke();
                        }))),
                Ui.Section("Shadow & glow",
                    Div(new() { ClassName = "text-fx-switch" },
                        Ui.Toggle(
                            editor.TextShadow ? "On" : "Off",
                            "Draw a shadow or glow behind the text",
                            editor.TextShadow,
                            () =>
                            {
                                editor.SetTextShadow(!editor.TextShadow);
                                editor.Refresh?.Invoke();
                            }),
                        Ui.Swatch(
                            "fx-swatch",
                            editor.TextShadowColor,
                            "Shadow colour — opens the colour panel",
                            () => EditColour(editor, TextColorTarget.Shadow),
                            28,
                            20)),
                    Div(new() { ClassName = editor.TextShadow ? "text-fx-controls" : "text-fx-controls is-off" },
                        Div(new() { ClassName = "text-fx-pair" },
                            Ui.Slider("X", editor.TextShadowX, -48f, 48f, 1f, " px", value =>
                            {
                                editor.SetTextShadowOffset((int)Ui.Parse(value, -48f, 48f), editor.TextShadowY);
                                editor.Refresh?.Invoke();
                            }),
                            Ui.Slider("Y", editor.TextShadowY, -48f, 48f, 1f, " px", value =>
                            {
                                editor.SetTextShadowOffset(editor.TextShadowX, (int)Ui.Parse(value, -48f, 48f));
                                editor.Refresh?.Invoke();
                            })),
                        Div(new() { ClassName = "text-fx-pair" },
                            Ui.Slider("Blur", editor.TextShadowBlur, 0f, 32f, 1f, " px", value =>
                            {
                                editor.SetTextShadowBlur(Ui.Parse(value, 0f, 32f));
                                editor.Refresh?.Invoke();
                            }),
                            Ui.Slider("Opacity", editor.TextShadowOpacity, 5f, 100f, 1f, " %", value =>
                            {
                                editor.SetTextShadowOpacity((int)Ui.Parse(value, 5f, 100f));
                                editor.Refresh?.Invoke();
                            })))),
                Span(new() { ClassName = "text-fx-tip" },
                    Text("Tip: X and Y at 0 with a high Blur makes a glow."))));
    }

    /// <summary>Switches the colour panel to an effect colour without closing this popup.</summary>
    private static void EditColour(PaintSession editor, TextColorTarget target)
    {
        editor.EditTextColour(target);
        editor.Refresh?.Invoke();
    }

    /// <summary>Builds the preset chip row.</summary>
    private static Node[] PresetButtons(PaintSession editor)
    {
        Node[] buttons = new Node[Presets.Length];
        for (int index = 0; index < Presets.Length; index++)
        {
            (TextPreset preset, string label, string title) = Presets[index];
            buttons[index] = Button(new()
            {
                ClassName = editor.TextPreset == preset ? "btn is-on" : "btn",
                Title = title,
                OnClick = () =>
                {
                    editor.ApplyTextPreset(preset);
                    editor.Refresh?.Invoke();
                },
            }, Text(label));
        }

        return buttons;
    }
}
