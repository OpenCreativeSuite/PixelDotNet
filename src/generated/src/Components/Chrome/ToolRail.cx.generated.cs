using System.Collections.Generic;
using Donkeh.UILab;
using PixelDotNet.Help;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>The tool rail. A column on desktop, a scrolling strip on phones.</summary>
public sealed class ToolRail : Component
{
    private static readonly PaintTool[][] Groups =
    [
        [PaintTool.Move],
        [PaintTool.Marquee, PaintTool.EllipseSelect, PaintTool.Lasso, PaintTool.MagicWand, PaintTool.QuickSelect],
        [PaintTool.Brush, PaintTool.Eraser],
        [PaintTool.CloneStamp, PaintTool.HealingBrush, PaintTool.DodgeBurn],
        [PaintTool.Line, PaintTool.Rectangle, PaintTool.Ellipse, PaintTool.Pen],
        [PaintTool.Gradient, PaintTool.Bucket, PaintTool.Eyedropper],
        [PaintTool.Text],
        [PaintTool.Zoom],
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        List<Node> blocks = [];

        for (int index = 0; index < Groups.Length; index++)
        {
            PaintTool[] group = Groups[index];
            List<Node> tools = [];

            foreach (PaintTool tool in group)
            {
                bool active = editor.Tool == tool;
                tools.Add(Ui.Tip(editor, _.H("button", new() { ClassName = active ? "tool is-active" : "tool", Title = $"{PaintSession.NameOf(tool)} ({PaintSession.ShortcutOf(tool)})", OnClick = () => editor.SelectTool(tool) }, Ui.Icon(Icons.ForTool(tool), "tool-glyph", Icons.IsRegular(tool))),
                Icons.ForTool(tool),
                PaintSession.NameOf(tool),
                "tool." + tool,
                PaintSession.ShortcutOf(tool),
                TipPlacement.Right,
                keepNameWhenDisabled: true));
            }

            if (index > 0)
            {
                blocks.Add(_.H("div", new() { ClassName = "rail-sep" }));
            }

            blocks.Add(_.H("div", new() { ClassName = "rail-group" }, tools.ToArray()));
        }

        return _.H("div", new() { ClassName = "rail" }, blocks.ToArray());
    }
}