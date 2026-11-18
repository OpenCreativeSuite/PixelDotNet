using System;
using Donkeh.UILab;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>
/// The floating tooltip rendered at the application root. It is positioned from
/// the hovered node's bounding box, so it is never clipped by the tool rail or
/// options bar scroll containers.
/// </summary>
public sealed class TooltipOverlay : Component
{
    private const float Width = 200f;
    private const float Height = 32f;
    private const float Gap = 8f;
    private const float Margin = 8f;

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (editor.Tip is not { } tip)
        {
            return Span(new());
        }

        float viewportWidth = MathF.Max(320f, editor.ViewportWidth);
        float viewportHeight = MathF.Max(240f, editor.ViewportHeight);

        // Prefer the right of narrow controls near the left edge (tool rail,
        // menus, status bar) when there is room; otherwise sit below and flip
        // above when the control is near the bottom edge.
        bool placeRight = tip.X < viewportWidth * 0.35f
            && tip.X + tip.Width + Gap + Width <= viewportWidth - Margin;

        float left;
        float top;

        if (placeRight)
        {
            left = tip.X + tip.Width + Gap;
            top = tip.Y + ((tip.Height - Height) * 0.5f);
        }
        else
        {
            left = Math.Clamp(tip.X, Margin, MathF.Max(Margin, viewportWidth - Width - Margin));
            bool below = tip.Y + tip.Height + Gap + Height <= viewportHeight - Margin;
            top = below
                ? tip.Y + tip.Height + Gap
                : tip.Y - Height - Gap;
        }

        left = Math.Clamp(left, Margin, MathF.Max(Margin, viewportWidth - Width - Margin));
        top = Math.Clamp(top, Margin, MathF.Max(Margin, viewportHeight - Height - Margin));

        string style =
            "position:absolute;"
            + "left:" + Ui.Number(left) + "px;"
            + "top:" + Ui.Number(top) + "px;"
            + "width:" + Ui.Number(Width) + "px;"
            + "height:" + Ui.Number(Height) + "px;";

        return Div(new()
        {
            ClassName = "tip-float",
            Attributes = new() { ["style"] = style },
        },
        Span(new() { ClassName = "tip-icon" }, Ui.Icon(tip.Icon)),
        Span(new() { ClassName = "tip-title" }, Text(tip.Title)),
        Show(tip.Hint.Length > 0, Span(new() { ClassName = "tip-key" }, Text(tip.Hint))),
        Button(new()
        {
            ClassName = "tip-more",
            Title = "More about " + tip.Title,
            OnClick = () =>
            {
                editor.ShowHelp(HelpCatalog.Find(tip.HelpId));
                editor.Refresh?.Invoke();
            },
        }, Ui.Icon(Icons.Next, "tip-chevron")));
    }
}
