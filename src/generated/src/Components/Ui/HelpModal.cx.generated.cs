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
            return _.H("span", new() { });
        }

        return _.H("div", new() { ClassName = "help-layer" }, _.H("div", new() { ClassName = "help-panel" }, _.H("div", new() { ClassName = "help-head" }, _.H("span", new() { ClassName = "help-icon-box" }, Ui.Icon(topic.Icon, "help-icon")), _.H("div", new() { ClassName = "help-titles" }, _.H("div", new() { ClassName = "help-title-row" }, _.H("span", new() { ClassName = "help-title" }, Text(topic.Title)), Show(topic.Shortcut.Length > 0, _.H("span", new() { ClassName = "help-key" }, Text(topic.Shortcut)))), _.H("span", new() { ClassName = "help-summary" }, Text(topic.Summary))), Ui.Tip(editor,
                        Ui.IconButton(Icons.Close, "Close help", () =>
                        {
                            editor.CloseHelp();
                            editor.Refresh?.Invoke();
                        }, className: "icon-btn help-close"),
                        Icons.Close, "Close Help", "")), _.H("div", new() { ClassName = "help-body" }, Ui.Section("How it works", [.. Lines(topic.HowItWorks)]), Ui.Section("Tips", [.. Tips(topic.Tips)])), _.H("div", new() { ClassName = "help-foot" }, _.H("span", new() { ClassName = "help-foot-note" }, Text("Hover any icon to see it again.")), Ui.Button("Got it", "Close this help article", () =>
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
            nodes[index] = _.H("div", new() { ClassName = "help-line" }, _.H("span", new() { ClassName = "help-bullet" }), _.H("span", new() { ClassName = "help-text" }, Text(lines[index])));
        }

        return nodes;
    }

    private static Node[] Tips(string[] tips)
    {
        Node[] nodes = new Node[tips.Length];
        for (int index = 0; index < tips.Length; index++)
        {
            nodes[index] = _.H("div", new() { ClassName = "help-tip" }, _.H("span", new() { ClassName = "help-tip-mark" }, Ui.Icon(Icons.Sparkles)), _.H("span", new() { ClassName = "help-text" }, Text(tips[index])));
        }

        return nodes;
    }
}