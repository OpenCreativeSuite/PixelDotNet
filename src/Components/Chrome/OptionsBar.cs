using System;
using System.Net.Http;
using System.Threading.Tasks;
using Donkeh.Hosting;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>
/// The contextual tool options row: what the active tool can do, plus the current
/// foreground and background colours.
/// </summary>
public sealed class OptionsBar : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);

        return Div(new() { ClassName = "optionsbar" },
            Div(new() { ClassName = "tool-badge" },
                Ui.Icon(Icons.ForTool(editor.Tool), "tool-badge-icon", Icons.IsRegular(editor.Tool)),
                Span(new() { ClassName = "tool-badge-name" }, Text(PaintSession.NameOf(editor.Tool))),
                Span(new() { ClassName = "tool-badge-key" }, Text(PaintSession.ShortcutOf(editor.Tool)))),
            Div(new() { ClassName = "opt-sep" }),
            Div(new() { ClassName = "options-group" }, Options(editor)),
            Div(new() { ClassName = "options-spacer" }),
            Div(new() { ClassName = "color-block" },
                Div(new() { ClassName = "color-pair" },
                    Ui.Swatch("color-chip-back", editor.Background, "Background colour — click to swap", editor.SwapColors, 22, 22),
                    Ui.Swatch("color-chip-front", editor.Foreground, "Foreground colour — click to open the colour panel", editor.OpenColourTab, 22, 22)),
                Div(new() { ClassName = "color-actions" },
                    Ui.IconButton(Icons.Swap, "Swap foreground and background (X)", editor.SwapColors, className: "icon-btn is-tiny"),
                    Button(new()
                    {
                        ClassName = "icon-btn is-tiny",
                        Title = "Default black and white (D)",
                        OnClick = editor.ResetColors,
                    }, Ui.DefaultsGlyph()))));
    }

    /// <summary>Explains what the active selection tool does.</summary>
    private static string SelectionNote(PaintTool tool) => tool switch
    {
        PaintTool.EllipseSelect => "Drag an ellipse; paint inside it, or drag it with the Move tool",
        PaintTool.Lasso => "Draw a freehand outline; it closes when you release",
        _ => "Drag a rectangle, then paint inside it or drag it with the Move tool",
    };

    /// <summary>Builds one text alignment toggle.</summary>
    private static Node AlignButton(PaintSession editor, TextAlign align, string icon, string title)
    {
        string helpId = align switch
        {
            TextAlign.Left => "options.alignLeft",
            TextAlign.Center => "options.alignCenter",
            _ => "options.alignRight",
        };

        return Ui.Tip(editor, Button(new()
        {
            ClassName = editor.TextAlignment == align ? "btn is-on" : "btn",
            Title = title,
            OnClick = () =>
            {
                editor.SetTextAlign(align);
                editor.Refresh?.Invoke();
            },
        }, Ui.Icon(icon, "align-glyph")), icon, title, helpId);
    }

    /// <summary>Downloads a cdnfonts family on a worker thread and adds it on the frame thread.</summary>
    private static void StartFontImport(PaintSession editor)
    {
        if (!editor.BeginFontImport())
        {
            editor.Refresh?.Invoke();
            return;
        }

        string input = editor.FontUrl;
        editor.Refresh?.Invoke();
        Task.Run(() =>
        {
            try
            {
                TextFont font = FontCatalog.Import(input);
                Host.Post(() =>
                {
                    editor.CompleteFontImport(font);
                    editor.Refresh?.Invoke();
                });
            }
            catch (Exception ex)
            {
                string message = ex is HttpRequestException { StatusCode: { } status }
                    ? $"cdnfonts returned {(int)status}. Check the font name or URL."
                    : ex.Message;
                Host.Post(() =>
                {
                    editor.FailFontImport(message);
                    editor.Refresh?.Invoke();
                });
            }
        });
    }

    /// <summary>Builds the normal text-tool controls.</summary>
    private static Node TextOptions(PaintSession editor) => F(
        Ui.TextField(
            editor.TextContent,
            "Type your text",
            editor.SetText,
            editor.BeginTyping,
            editor.EndTyping,
            null,
            "text-" + editor.TextGeneration),
        Div(new() { ClassName = "text-font" },
            Button(new()
            {
                ClassName = editor.IsFontMenuOpen ? "btn font-picker is-on" : "btn font-picker",
                Title = "Choose a font",
                OnClick = () =>
                {
                    editor.ToggleFontMenu();
                    editor.Refresh?.Invoke();
                },
            },
            Span(new() { ClassName = "font-picker-name" }, Text(editor.Font.Name)),
            Ui.Icon(Icons.ChevronDown, "font-picker-arrow"))),
        Ui.IconButton(Icons.Plus, "Add a font from cdnfonts", () =>
        {
            editor.ToggleFontAdd();
            editor.Refresh?.Invoke();
        }, className: "icon-btn is-tiny"),
        Ui.Field("Size", editor.TextSize, 8f, 200f, 1f, " px", value =>
        {
            editor.SetTextSize(Ui.Parse(value, 8f, 200f));
            editor.Refresh?.Invoke();
        }),
        Div(new() { ClassName = "segmented" },
            Ui.Tip(editor, Ui.Toggle("B", "Bold text", editor.TextBold, () =>
            {
                editor.ToggleTextBold();
                editor.Refresh?.Invoke();
            }), Icons.Text, "Bold", "options.bold"),
            Ui.Tip(editor, Ui.Toggle("I", "Italic text", editor.TextItalic, () =>
            {
                editor.ToggleTextItalic();
                editor.Refresh?.Invoke();
            }), Icons.Text, "Italic", "options.italic")),
        Div(new() { ClassName = "segmented" },
            AlignButton(editor, TextAlign.Left, Icons.AlignLeft, "Align left"),
            AlignButton(editor, TextAlign.Center, Icons.AlignCenter, "Centre"),
            AlignButton(editor, TextAlign.Right, Icons.AlignRight, "Align right")),
        Ui.Tip(editor, Button(new()
        {
            ClassName = editor.IsTextFxOpen ? "btn is-on" : "btn",
            Title = "Outline, shadow and glow effects",
            OnClick = () =>
            {
                editor.ToggleTextFx();
                editor.Refresh?.Invoke();
            },
        }, Ui.Icon(Icons.Sparkles, "btn-icon"), Text("FX")), Icons.Sparkles, "Text Effects", "options.fx"),
        Ui.Tip(editor, Ui.Swatch("text-color-chip", editor.Foreground, "Text colour — click to open the colour panel", editor.OpenColourTab, 22, 22),
            Icons.Palette, "Text Colour", "tab.colour"),
        Ui.Tip(editor, Ui.Button("Place", "Commit the text (Enter)", () =>
        {
            editor.ApplyText();
            editor.Refresh?.Invoke();
        }), Icons.Text, "Place Text", "options.place", "Enter"),
        Ui.Tip(editor, Ui.Button("Cancel", "Discard the text", () =>
        {
            editor.CancelText();
            editor.Refresh?.Invoke();
        }), Icons.Close, "Cancel Text", "options.cancel"));

    /// <summary>Builds the compact font-import row shown in place of the text options.</summary>
    private static Node ImportOptions(PaintSession editor) => F(
        Span(new() { ClassName = "options-note" }, Text("Import a font from cdnfonts")),
        Ui.TextField(
            editor.FontUrl,
            "cdnfonts URL or font name",
            editor.SetFontUrl,
            editor.BeginTyping,
            editor.EndTyping,
            _ =>
            {
                StartFontImport(editor);
            },
            "font-url-" + editor.FontAddGeneration,
            "font-url"),
        Ui.Button("Add", "Download and cache this font", () => StartFontImport(editor), !editor.IsFontDownloading),
        Ui.Button("Cancel", "Close the font import row", editor.CancelFontAdd, !editor.IsFontDownloading),
        Show(editor.FontImportMessage.Length > 0,
            Span(new()
            {
                ClassName = editor.FontImportFailed
                    ? "font-import-status is-error"
                    : editor.IsFontDownloading
                        ? "font-import-status is-busy"
                        : "font-import-status",
            }, Text(editor.FontImportMessage))));

    /// <summary>Builds the controls that belong to the active tool.</summary>
    private static Node Options(PaintSession editor) => editor.Tool switch
    {
        PaintTool.Brush or PaintTool.Eraser => F(
            Ui.Field("Size", editor.BrushSize, 1f, 120f, 1f, " px", value =>
            {
                editor.BrushSize = Ui.Parse(value, 1f, 120f);
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Hardness", editor.Hardness * 100f, 0f, 100f, 1f, " %", value =>
            {
                editor.Hardness = Ui.Parse(value, 0f, 100f) / 100f;
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Opacity", editor.Opacity * 100f, 5f, 100f, 1f, " %", value =>
            {
                editor.Opacity = Ui.Parse(value, 5f, 100f) / 100f;
                editor.Refresh?.Invoke();
            })),

        PaintTool.CloneStamp or PaintTool.HealingBrush => F(
            Ui.Field("Size", editor.BrushSize, 1f, 120f, 1f, " px", value =>
            {
                editor.BrushSize = Ui.Parse(value, 1f, 120f);
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Hardness", editor.Hardness * 100f, 0f, 100f, 1f, " %", value =>
            {
                editor.Hardness = Ui.Parse(value, 0f, 100f) / 100f;
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Opacity", editor.Opacity * 100f, 5f, 100f, 1f, " %", value =>
            {
                editor.Opacity = Ui.Parse(value, 5f, 100f) / 100f;
                editor.Refresh?.Invoke();
            }),
            Span(new() { ClassName = "options-note" },
                Text(editor.HasCloneSource
                    ? $"Source {editor.CloneSourceX}, {editor.CloneSourceY} — Ctrl-click to move it"
                    : "Ctrl-click to set the sample source"))),

        PaintTool.DodgeBurn => F(
            Div(new() { ClassName = "segmented" },
                Ui.Toggle("Dodge", "Lighten with brush strokes", editor.DodgeMode, () =>
                {
                    editor.DodgeMode = true;
                    editor.Refresh?.Invoke();
                }),
                Ui.Toggle("Burn", "Darken with brush strokes", !editor.DodgeMode, () =>
                {
                    editor.DodgeMode = false;
                    editor.Refresh?.Invoke();
                })),
            Ui.Field("Size", editor.BrushSize, 1f, 120f, 1f, " px", value =>
            {
                editor.BrushSize = Ui.Parse(value, 1f, 120f);
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Hardness", editor.Hardness * 100f, 0f, 100f, 1f, " %", value =>
            {
                editor.Hardness = Ui.Parse(value, 0f, 100f) / 100f;
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Strength", editor.Opacity * 100f, 5f, 100f, 1f, " %", value =>
            {
                editor.Opacity = Ui.Parse(value, 5f, 100f) / 100f;
                editor.Refresh?.Invoke();
            })),

        PaintTool.Line => F(
            Ui.Field("Weight", editor.BrushSize, 1f, 120f, 1f, " px", value =>
            {
                editor.BrushSize = Ui.Parse(value, 1f, 120f);
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Opacity", editor.Opacity * 100f, 5f, 100f, 1f, " %", value =>
            {
                editor.Opacity = Ui.Parse(value, 5f, 100f) / 100f;
                editor.Refresh?.Invoke();
            })),

        PaintTool.Rectangle or PaintTool.Ellipse => F(
            Div(new() { ClassName = "segmented" },
                Ui.Toggle("Fill", "Fill the shape", editor.FillShapes, () =>
                {
                    editor.FillShapes = true;
                    editor.Refresh?.Invoke();
                }),
                Ui.Toggle("Stroke", "Outline the shape", !editor.FillShapes, () =>
                {
                    editor.FillShapes = false;
                    editor.Refresh?.Invoke();
                })),
            Ui.Field("Width", editor.StrokeWidth, 1f, 40f, 1f, " px", value =>
            {
                editor.StrokeWidth = Ui.Parse(value, 1f, 40f);
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Opacity", editor.Opacity * 100f, 5f, 100f, 1f, " %", value =>
            {
                editor.Opacity = Ui.Parse(value, 5f, 100f) / 100f;
                editor.Refresh?.Invoke();
            })),

        PaintTool.Gradient => F(
            Ui.Field("Opacity", editor.Opacity * 100f, 5f, 100f, 1f, " %", value =>
            {
                editor.Opacity = Ui.Parse(value, 5f, 100f) / 100f;
                editor.Refresh?.Invoke();
            }),
            Span(new() { ClassName = "options-note" }, Text("Drag from the foreground to the background colour"))),

        PaintTool.Bucket => F(
            Ui.Field("Tolerance", editor.Tolerance, 0f, 128f, 1f, string.Empty, value =>
            {
                editor.Tolerance = (int)Ui.Parse(value, 0f, 128f);
                editor.Refresh?.Invoke();
            }),
            Ui.Field("Opacity", editor.Opacity * 100f, 5f, 100f, 1f, " %", value =>
            {
                editor.Opacity = Ui.Parse(value, 5f, 100f) / 100f;
                editor.Refresh?.Invoke();
            })),

        PaintTool.Marquee or PaintTool.EllipseSelect or PaintTool.Lasso => F(
            Span(new() { ClassName = "options-note" }, Text(SelectionNote(editor.Tool))),
            Ui.Tip(editor, Ui.Button("Select all", "Select the whole document", editor.SelectAll),
                Icons.Marquee, "Select All", "options.selectAll"),
            Ui.Tip(editor, Ui.Button("Deselect", "Clear the selection (Esc)", editor.Deselect, editor.Selection is not null),
                Icons.Close, "Deselect", "options.deselect", "Esc")),

        PaintTool.MagicWand => F(
            Ui.Field("Tolerance", editor.MagicTolerance, 0f, 128f, 1f, string.Empty, value =>
            {
                editor.MagicTolerance = (int)Ui.Parse(value, 0f, 128f);
                editor.Refresh?.Invoke();
            }),
            Ui.Toggle("Contiguous", "Only select the connected region", editor.MagicContiguous, () =>
            {
                editor.MagicContiguous = !editor.MagicContiguous;
                editor.Refresh?.Invoke();
            }),
            Span(new() { ClassName = "options-note" }, Text("Selects colours on the active layer")),
            Ui.Tip(editor, Ui.Button("Select all", "Select the whole document", editor.SelectAll),
                Icons.Marquee, "Select All", "options.selectAll"),
            Ui.Tip(editor, Ui.Button("Deselect", "Clear the selection (Esc)", editor.Deselect, editor.Selection is not null),
                Icons.Close, "Deselect", "options.deselect", "Esc")),

        PaintTool.Move => F(
            Span(new() { ClassName = "options-note" }, Text("Drag to move the selection, or a handle to resize it. With no selection, drag moves the layer.")),
            Ui.Button("Deselect", "Clear the selection (Esc)", editor.Deselect, editor.Selection is not null)),

        PaintTool.Text => editor.IsFontAddOpen ? ImportOptions(editor) : TextOptions(editor),

        PaintTool.Eyedropper => Span(new() { ClassName = "options-note" }, Text("Click anywhere to sample the composited colour")),

        _ => Span(new() { ClassName = "options-note" }, Text("Click to zoom in, right-click to zoom out")),
    };
}
