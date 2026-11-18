using Donkeh.UILab;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>The centred help modal opened by a tooltip's chevron.</summary>
public sealed class HelpModal : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        if (editor.OpenHelp is not { } topic)
        {
            return Span(new());
        }

        return Div(new() { ClassName = "help-layer" },
            Div(new() { ClassName = "help-panel" },
                Div(new() { ClassName = "help-head" },
                    Span(new() { ClassName = "help-icon-box" }, Ui.Icon(topic.Icon, "help-icon")),
                    Div(new() { ClassName = "help-titles" },
                        Div(new() { ClassName = "help-title-row" },
                            Span(new() { ClassName = "help-title" }, Text(topic.Title)),
                            Show(topic.Shortcut.Length > 0,
                                Span(new() { ClassName = "help-key" }, Text(topic.Shortcut)))),
                        Span(new() { ClassName = "help-summary" }, Text(topic.Summary))),
                    Ui.IconButton(Icons.Close, "Close help", () =>
                    {
                        editor.CloseHelp();
                        editor.Refresh?.Invoke();
                    }, className: "icon-btn help-close")),
                Div(new() { ClassName = "help-body" },
                    Ui.Section("How it works", [.. Lines(topic.HowItWorks)]),
                    Ui.Section("Tips", [.. Tips(topic.Tips)])),
                Div(new() { ClassName = "help-foot" },
                    Span(new() { ClassName = "help-foot-note" }, Text("Hover any icon to see it again.")),
                    Ui.Button("Got it", "Close this help article", () =>
                    {
                        editor.CloseHelp();
                        editor.Refresh?.Invoke();
                    }))));
    }

    private static Node[] Lines(string[] lines)
    {
        Node[] nodes = new Node[lines.Length];
        for (int index = 0; index < lines.Length; index++)
        {
            nodes[index] = Div(new() { ClassName = "help-line" },
                Span(new() { ClassName = "help-bullet" }),
                Span(new() { ClassName = "help-text" }, Text(lines[index])));
        }

        return nodes;
    }

    private static Node[] Tips(string[] tips)
    {
        Node[] nodes = new Node[tips.Length];
        for (int index = 0; index < tips.Length; index++)
        {
            nodes[index] = Div(new() { ClassName = "help-tip" },
                Span(new() { ClassName = "help-tip-mark" }, Ui.Icon(Icons.Sparkles)),
                Span(new() { ClassName = "help-text" }, Text(tips[index])));
        }

        return nodes;
    }
}
