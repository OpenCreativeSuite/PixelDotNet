using System;
using System.Collections.Generic;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>
/// The font family picker: a searchable, scrollable popup anchored under the
/// type tool's font button.
/// </summary>
/// <remarks>
/// It is ordinary DOM rather than a canvas because the search field and the
/// list both need native input and scrolling. The canvas display list honours
/// <c>z-index</c>, so this popup paints above the artwork even though the
/// document is itself a canvas.
/// </remarks>
public sealed class FontPicker : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        string sample = editor.FontPreviewText;
        int count = editor.FontMatchCount;
        string style =
            "position:absolute;z-index:60;"
            + "left:" + Ui.Number(TextFonts.MenuX) + "px;"
            + "top:" + Ui.Number(TextFonts.MenuY) + "px;"
            + "width:" + Ui.Number(TextFonts.MenuWidth) + "px;";

        List<Node> rows = [];
        for (int slot = 0; slot < count; slot++)
        {
            rows.Add(Row(editor, slot, sample));
        }

        return _.H("div", new() { className = "font-popup", Attributes = new() { ["style"] = style } }, _.H("div", new() { className = "font-popup-search" }, _.H("span", new() { className = "font-popup-search-icon" }, Ui.Icon(Icons.Zoom)), _.H("input", new() { className = "font-search", Type = "text", Value = editor.FontSearch, Placeholder = "Search fonts…", OnInput = editor.SetFontSearch, OnFocus = () => editor.SetFontSearchFocused(true), OnBlur = () => editor.SetFontSearchFocused(false) }), _.H("span", new() { className = count == 0 ? "font-popup-count is-empty" : "font-popup-count" }, Text(CountLabel(count)))), Show(count > 0, _.H("div", new() { className = "font-popup-list" }, rows.ToArray())), Show(count == 0, _.H("div", new() { className = "font-popup-empty" }, Text("No fonts match that search"))), _.H("div", new() { className = "font-popup-footer" }, _.H("button", new() { className = "font-popup-add", Title = "Download and cache a font from cdnfonts", OnClick = () =>
                    {
                        editor.OpenFontAdd();
                        editor.Refresh?.Invoke();
                    } }, Text("+  Add a font from cdnfonts…")), _.H("span", new() { className = "font-popup-hint" }, Text("Type to search - Enter"))));
    }

    /// <summary>Builds one family row with a preview set in that family.</summary>
    private static Node Row(PaintSession editor, int slot, string sample)
    {
        int index = editor.FontMatchAt(slot);
        TextFont font = TextFonts.All[index];
        bool selected = index == editor.TextFontIndex;

        Node preview = Preview(font, sample);

        return _.H("div", new() { className = selected ? "font-row is-selected" : "font-row", Title = font.Name, OnClick = () =>
            {
                editor.ApplyFont(index);
                editor.Refresh?.Invoke();
            } }, _.H("span", new() { className = "font-row-name" }, Text(font.Name)), preview);
    }

    /// <summary>
    /// Draws the sample in the family's own face. Downloaded families are not
    /// registered with the host's font manager, so their sample is a cached
    /// bitmap rendered by the paint engine.
    /// </summary>
    private static Node Preview(TextFont font, string sample)
    {
        if (font.IsDownloaded)
        {
            string? path = FontCatalog.SamplePreview(font, sample)
                ?? FontCatalog.PreviewPath(font);
            if (path is not null)
            {
                return _.H("div", new() { className = "font-row-preview-slot" }, _.H("img", new() { className = "font-row-preview-img", Src = path, Alt = sample }));
            }
        }

        string family = font.IsDownloaded ? "Segoe UI" : font.Name;
        return _.H("span", new() { className = "font-row-preview", Attributes = new() { ["style"] = "font-family:\"" + family.Replace("\"", "\\\"") + "\";" } }, Text(sample));
    }

    /// <summary>Labels the match count, using the singular for one hit.</summary>
    private static string CountLabel(int count) => count switch
    {
        0 => "no matches",
        1 => "1 match",
        _ => count + " matches",
    };
}