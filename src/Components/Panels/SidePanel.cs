using System.Collections.Generic;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>The right-hand inspector: one tab bar, one visible panel.</summary>
/// <remarks>
/// The active tab lives in the session rather than in local hook state so other
/// chrome (the colour chips, the Image menu) can jump to a tab.
/// </remarks>
public sealed class SidePanel : Component
{
    private static readonly string[] Tabs = ["Layers", "Colour", "Adjust", "History", "Effects"];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        int tab = editor.SideTab;
        List<Node> tabs = [];

        for (int index = 0; index < Tabs.Length; index++)
        {
            int target = index;
            tabs.Add(Button(new()
            {
                ClassName = target == tab ? "panel-tab is-active" : "panel-tab",
                OnClick = () => editor.OpenSideTab(target),
            }, Text(Tabs[target])));
        }

        return Div(new() { ClassName = "side" },
            Div(new() { ClassName = "panel-tabs" }, [.. tabs]),
            Div(new() { ClassName = tab == 0 ? "panel-body is-fixed" : "panel-body" },
                Show(tab == 0, C<LayersPanel>()),
                Show(tab == 1, C<ColorPanel>()),
                Show(tab == 2, C<AdjustPanel>()),
                Show(tab == 3, C<HistoryPanel>()),
                Show(tab == 4, C<EffectsPanel>())));
    }
}
