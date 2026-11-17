using Donkeh.Hosting;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Components;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet;

/// <summary>
/// The application root: owns the paint session and publishes it to every panel.
/// </summary>
/// <remarks>
/// The session lives in a ref so strokes never re-render the tree; a counter in
/// hook state is bumped by <see cref="PaintSession.Refresh"/> when UI that mirrors
/// the model needs to catch up.
/// </remarks>
[AppRoot(
    Width = 1440,
    Height = 900,
    Title = "PixelDotNet",
    ClearColor = 0xFF17181B,
    MinWidth = 420,
    MinHeight = 560)]
public sealed class App : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        Ref<PaintSession> reference = UseRef<PaintSession>();
        PaintSession? session = reference.Value;
        if (session is null)
        {
            session = PaintSession.CreateDemo();
            reference.Value = session;
        }

        PaintSession editor = session;
        var tick = StateWithUpdate(0);
        editor.Refresh = () => tick.Update(value => value + 1);

        return Provider(EditorContext.Value, editor,
            Div(new()
            {
                ClassName = "app",
                // Shortcuts live on the root so any focused control (a tool, a
                // tab, a slider) forwards them; the canvas itself never takes
                // focus, so a stage-level handler would go quiet while painting.
                OnKeyDown = key => Viewport.HandleShortcut(editor, key),
            },
                C<OptionsBar>(),
                C<ToolRail>(),
                C<Viewport>(),
                C<SidePanel>(),
                C<StatusBar>(),
                // Late in the tree so its drop-downs are hit-tested before the
                // stage they overlap. Grid lines keep it visually on top.
                C<MenuBar>(),
                // Canvas-drawn popups live above the workspace, like the menus.
                // Starting below the options bar puts the font picker directly
                // under its button instead of at the artwork's top-left corner.
                Show(editor.IsFontMenuOpen, Canvas(new CanvasProps
                {
                    ClassName = "font-overlay",
                    OnDraw = context => CanvasSurface.DrawFontMenu(context, editor),
                    OnPointerDown = (x, y) => editor.FontMenuPointerDown(x, y),
                    OnPointerMove = (x, y) => editor.FontMenuPointerMove(x, y),
                    OnPointerUp = (x, y) => editor.FontMenuPointerUp(),
                }))));
    }
}
