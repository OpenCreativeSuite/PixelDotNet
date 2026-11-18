using Donkeh.Hosting;
using Donkeh.UILab;
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

        Ref<ElementNode> rootRef = UseRef<ElementNode>();
        ElementNode root = (ElementNode)Div(new()
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
            // stage they overlap.
            C<MenuBar>(),
            // Floating UI paints above the canvas because the display list
            // honours z-index, so this is a real popup with its own input
            // and scrolling rather than a canvas-drawn approximation.
            Show(editor.IsFontMenuOpen, Div(new()
            {
                ClassName = "font-backdrop",
                OnClick = () =>
                {
                    editor.CloseFontMenu();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsFontMenuOpen, C<FontPicker>()),
            Show(editor.IsTextFxOpen, Div(new()
            {
                ClassName = "font-backdrop",
                OnClick = () =>
                {
                    editor.CloseTextFx();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsTextFxOpen, C<TextFxPanel>()),
            Show(editor.Tip is not null, C<TooltipOverlay>()),
            Show(editor.OpenHelp is not null, Div(new()
            {
                ClassName = "help-backdrop",
                OnClick = () =>
                {
                    editor.CloseHelp();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.OpenHelp is not null, C<HelpModal>()),
            Show(editor.IsFileDialogOpen, Div(new()
            {
                ClassName = "file-backdrop",
                OnClick = () =>
                {
                    editor.CloseFileDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsFileDialogOpen, C<FileDialog>()),
            Show(editor.IsResizeOpen, Div(new()
            {
                ClassName = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseResizeDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsResizeOpen, C<ResizeDialog>()),
            Show(editor.IsBatchOpen, Div(new()
            {
                ClassName = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseBatchDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsBatchOpen, C<BatchDialog>()),
            Show(editor.IsLayerMenuOpen, Div(new()
            {
                ClassName = "file-menu-backdrop",
                OnClick = () => editor.CloseLayerMenu(),
            })),
            Show(editor.IsLayerMenuOpen, C<LayerMenu>()),
            Show(editor.IsLayerStyleOpen, Div(new()
            {
                ClassName = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseLayerStyleDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsLayerStyleOpen, C<LayerStyleDialog>()),
            Show(editor.IsColourToolsOpen, Div(new()
            {
                ClassName = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseColourToolsDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsColourToolsOpen, C<ColourToolsDialog>()),
            Show(editor.IsPerspectiveOpen, Div(new()
            {
                ClassName = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CancelPerspective();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsPerspectiveOpen, C<PerspectiveDialog>()),
            Show(editor.IsMeshWarpOpen, C<MeshWarpBar>()),
            Show(editor.IsSmartTransformOpen, Div(new()
            {
                ClassName = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CancelSmartTransform();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsSmartTransformOpen, C<SmartObjectDialog>()));

        root.BindRef(rootRef);
        if (rootRef.Value is { Width: > 0, Height: > 0 } bounds)
        {
            editor.ViewportWidth = bounds.Width;
            editor.ViewportHeight = bounds.Height;
        }

        return Provider(EditorContext.Value, editor, root);
    }
}
