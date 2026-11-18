using System.Collections.Generic;
using Donkeh.UILab;
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
        [PaintTool.Marquee, PaintTool.EllipseSelect, PaintTool.Lasso, PaintTool.MagicWand],
        [PaintTool.Brush, PaintTool.Eraser],
        [PaintTool.Line, PaintTool.Rectangle, PaintTool.Ellipse],
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
                tools.Add(Ui.Tip(editor, Button(new()
                {
                    ClassName = active ? "tool is-active" : "tool",
                    Title = $"{PaintSession.NameOf(tool)} ({PaintSession.ShortcutOf(tool)})",
                    OnClick = () => editor.SelectTool(tool),
                },
                Ui.Icon(Icons.ForTool(tool), "tool-glyph", Icons.IsRegular(tool))),
                Icons.ForTool(tool),
                PaintSession.NameOf(tool),
                "tool." + tool,
                PaintSession.ShortcutOf(tool)));
            }

            if (index > 0)
            {
                blocks.Add(Div(new() { ClassName = "rail-sep" }));
            }

            blocks.Add(Div(new() { ClassName = "rail-group" }, [.. tools]));
        }

        return Div(new() { ClassName = "rail" }, [.. blocks]);
    }
}
