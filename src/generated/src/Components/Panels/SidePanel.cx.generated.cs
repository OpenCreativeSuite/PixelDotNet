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

    private static readonly string[] TabIcons =
    [
        Icons.Flatten,
        Icons.Palette,
        Icons.Sliders,
        Icons.History,
        Icons.Sparkles,
    ];

    private static readonly string[] TabHelp =
    [
        "tab.layers",
        "tab.colour",
        "tab.adjust",
        "tab.history",
        "tab.effects",
    ];

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        int tab = editor.SideTab;
        List<Node> tabs = [];

        for (int index = 0; index < Tabs.Length; index++)
        {
            int target = index;
            tabs.Add(Ui.Tip(editor, _.H("button", new() { className = target == tab ? "panel-tab is-active" : "panel-tab", OnClick = () => editor.OpenSideTab(target) }, Text(Tabs[target])), TabIcons[index], Tabs[index], TabHelp[index]));
        }

        return _.H("div", new() { className = "side" }, _.H("div", new() { className = "panel-tabs" }, tabs.ToArray()), _.H("div", new() { className = tab == 0 ? "panel-body is-fixed" : "panel-body" }, Show(tab == 0, _.C<LayersPanel, LayersPanelProps>(new() { })), Show(tab == 1, _.C<ColorPanel, ColorPanelProps>(new() { })), Show(tab == 2, _.C<AdjustPanel, AdjustPanelProps>(new() { })), Show(tab == 3, _.C<HistoryPanel, HistoryPanelProps>(new() { })), Show(tab == 4, _.C<EffectsPanel, EffectsPanelProps>(new() { }))));
    }
}