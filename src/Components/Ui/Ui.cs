using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Donkeh.Hosting;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>Small markup helpers shared by the chrome and the panels.</summary>
internal static class Ui
{
    /// <summary>Formats a value without trailing noise, for attribute values.</summary>
    public static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Formats a value as a whole number, for readouts.</summary>
    public static string Format(float value) => MathF.Round(value).ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses slider text, clamping into a range.</summary>
    public static float Parse(string text, float min, float max) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? Math.Clamp(value, min, max)
            : min;

    /// <summary>Builds the canvas width/height attributes.</summary>
    public static Dictionary<string, string> Size(int width, int height) => new()
    {
        ["width"] = width.ToString(CultureInfo.InvariantCulture),
        ["height"] = height.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Formats a colour as an uppercase hex string.</summary>
    public static string Hex(uint color) => $"#{Red(color):X2}{Green(color):X2}{Blue(color):X2}";

    /// <summary>Reads the red channel of a packed colour.</summary>
    public static int Red(uint color) => (int)((color >> 16) & 0xFF);

    /// <summary>Reads the green channel of a packed colour.</summary>
    public static int Green(uint color) => (int)((color >> 8) & 0xFF);

    /// <summary>Reads the blue channel of a packed colour.</summary>
    public static int Blue(uint color) => (int)(color & 0xFF);

    /// <summary>A Font Awesome glyph.</summary>
    /// <param name="name">Font Awesome class, for example <c>fa-paintbrush</c>.</param>
    /// <param name="extra">Additional classes, such as a size modifier.</param>
    /// <param name="regular">Draws the outline weight instead of the solid weight.</param>
    public static Node Icon(string name, string extra = "", bool regular = false) =>
        I(new() { ClassName = $"fa {(regular ? "fa-regular" : "fa-solid")} {name} {extra}".TrimEnd() });

    /// <summary>A square icon-only button.</summary>
    public static Node IconButton(
        string icon,
        string title,
        Action onClick,
        bool enabled = true,
        bool regular = false,
        string className = "icon-btn")
    {
        string baseClass = className == "icon-btn" ? "icon-btn" : $"icon-btn {className}";
        List<string> classes = [baseClass];
        if (!enabled)
        {
            classes.Add("is-disabled");
        }

        return HtmlHelper.Button(new()
        {
            ClassName = string.Join(' ', classes),
            Title = title,
            Disabled = !enabled,
            OnClick = onClick,
        },
        Icon(icon, regular: regular));
    }

    /// <summary>A single-line text field sized for the options bar.</summary>
    public static Node TextField(
        string value,
        string placeholder,
        Action<string> onInput,
        Action onFocus,
        Action onBlur,
        Action<string>? onCommit = null,
        string? key = null,
        string? className = null) =>
        Input(new()
        {
            ClassName = className is null ? "text-field" : "text-field " + className,
            Key = key,
            Type = "text",
            Value = value,
            Placeholder = placeholder,
            OnInput = onInput,
            OnFocus = onFocus,
            OnBlur = onBlur,
            OnChange = onCommit,
        });

    /// <summary>A labelled inline slider sized for the options bar.</summary>
    public static Node Field(string label, float value, float min, float max, float step, string suffix, Action<string> onInput) =>
        Div(new() { ClassName = "field" },
            Span(new() { ClassName = "field-label" }, Text(label)),
            Div(new() { ClassName = "field-track" },
                Input(new()
                {
                    ClassName = "field-input",
                    Type = "range",
                    Value = Number(value),
                    Attributes = new()
                    {
                        ["min"] = Number(min),
                        ["max"] = Number(max),
                        ["step"] = Number(step),
                    },
                    OnInput = onInput,
                })),
            Span(new() { ClassName = "field-value" }, Text(Format(value) + suffix)));

    /// <summary>A stacked slider for a panel.</summary>
    public static Node Slider(string label, float value, float min, float max, float step, string suffix, Action<string> onInput) =>
        Div(new() { ClassName = "slider" },
            Div(new() { ClassName = "slider-head" },
                Span(new() { ClassName = "slider-label" }, Text(label)),
                Span(new() { ClassName = "slider-value" }, Text(Format(value) + suffix))),
            Input(new()
            {
                ClassName = "slider-input",
                Type = "range",
                Value = Number(value),
                Attributes = new()
                {
                    ["min"] = Number(min),
                    ["max"] = Number(max),
                    ["step"] = Number(step),
                },
                OnInput = onInput,
            }));

    /// <summary>A titled block inside a panel.</summary>
    public static Node Section(string title, params Node[] children) =>
        Div(new() { ClassName = "section" },
            Div(new() { ClassName = "section-head" },
                Span(new() { ClassName = "section-title" }, Text(title)),
                Span(new() { ClassName = "section-rule" })),
            Div(new() { ClassName = "section-body" }, children));

    /// <summary>A labelled value that steps through a fixed list.</summary>
    public static Node Stepper(string value, string title, Action previous, Action next) =>
        Div(new() { ClassName = "stepper", Title = title },
            HtmlHelper.Button(new() { ClassName = "stepper-btn", Title = "Previous", OnClick = previous },
                Icon(Icons.Previous, "stepper-glyph")),
            Span(new() { ClassName = "stepper-value" }, Text(value)),
            HtmlHelper.Button(new() { ClassName = "stepper-btn", Title = "Next", OnClick = next },
                Icon(Icons.Next, "stepper-glyph")));

    /// <summary>An action button with a leading glyph.</summary>
    public static Node ButtonIcon(string icon, string label, string title, Action onClick, bool enabled = true, bool regular = false) =>
        HtmlHelper.Button(new()
        {
            ClassName = "btn",
            Title = title,
            Disabled = !enabled,
            OnClick = onClick,
        }, Icon(icon, "btn-icon", regular), Text(label));

    /// <summary>An action button.</summary>
    public static Node Button(string label, string title, Action onClick, bool enabled = true) =>
        HtmlHelper.Button(new()
        {
            ClassName = "btn",
            Title = title,
            Disabled = !enabled,
            OnClick = onClick,
        }, Text(label));

    /// <summary>A two-state toggle button.</summary>
    public static Node Toggle(string label, string title, bool active, Action onClick) =>
        HtmlHelper.Button(new()
        {
            ClassName = active ? "btn is-on" : "btn",
            Title = title,
            OnClick = onClick,
        }, Text(label));

    /// <summary>The black-over-white mark used by the default-colours button.</summary>
    public static Node DefaultsGlyph() =>
        Span(new() { ClassName = "defaults-glyph" },
            Span(new() { ClassName = "defaults-dot is-dark" }),
            Span(new() { ClassName = "defaults-dot is-light" }));

    /// <summary>A small square swatch painted by a canvas.</summary>
    public static Node Swatch(string className, uint color, string title, Action onClick, int width = 24, int height = 18) =>
        Canvas(new CanvasProps
        {
            ClassName = className,
            Title = title,
            Attributes = Size(width, height),
            OnClick = onClick,
            OnDraw = context => context.Clear(RgbaOpaque(color)),
        });

    /// <summary>Makes a colour opaque.</summary>
    public static uint RgbaOpaque(uint color) => 0xFF000000u | (color & 0x00FFFFFFu);

    /// <summary>
    /// Attaches a hover tooltip to a control. The control's own layout is left
    /// untouched; the floating bubble is rendered at the application root from the
    /// control's last bounding box.
    /// </summary>
    public static Node Tip(
        PaintSession editor,
        Node content,
        string icon,
        string title,
        string helpId,
        string hint = "",
        TipPlacement placement = TipPlacement.Auto,
        bool keepNameWhenDisabled = true)
    {
        if ((!editor.ShowTooltips && !keepNameWhenDisabled) || content is not ElementNode element)
        {
            return content;
        }

        // The event is copied from the freshly rendered element onto the live
        // one, so capture a ref instead of the render-time node: the layout
        // engine writes bounds to the live node only.
        Ref<ElementNode> reference = new();
        element.BindRef(reference);

        element.On("mouseover", (Action)(() =>
        {
            ElementNode target = reference.Value ?? element;
            editor.ShowTip(new TipState(
                icon,
                title,
                hint,
                helpId,
                placement,
                editor.ShowTooltips,
                target.PositionX,
                target.PositionY,
                target.Width,
                target.Height));
        }));

        element.On("mouseout", (Action)(() =>
        {
            int generation = editor.TipGeneration;
            Task.Delay(160).ContinueWith(_ => Host.Post(() =>
            {
                if (editor.TipGeneration == generation && !editor.TipHovered)
                {
                    editor.HideTip();
                }
            }));
        }));

        return content;
    }
}
