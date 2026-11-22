using System.Collections.Generic;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>The undo history, oldest step first, with click-to-travel.</summary>
public sealed class HistoryPanel : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        IReadOnlyList<string> steps = editor.HistorySteps;
        int current = editor.HistoryIndex;
        List<Node> rows = [];

        for (int i = 0; i < steps.Count; i++)
        {
            int index = i;
            bool future = index > current;
            rows.Add(_.H("button", new() { ClassName = index == current ? "history-item is-active" : future ? "history-item is-future" : "history-item", Key = index.ToString(), OnClick = () => editor.GoToHistory(index) }, _.H("span", new() { ClassName = "history-dot" }), _.H("span", new() { ClassName = "history-name" }, Text(steps[index])), _.H("span", new() { ClassName = "history-index" }, Text((index + 1).ToString()))));
        }

        return F(
            Ui.Section("History", _.H("div", new() { ClassName = "history-list" }, rows.ToArray())),
            _.H("span", new() { ClassName = "panel-note" }, Text("Click a step to travel back or forwards. New edits drop the redo steps below the current one.")));
    }
}