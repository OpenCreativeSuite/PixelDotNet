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
    ClearColor = 0xFF0B0C10,
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
            className = "app",
            // Shortcuts live on the root so any focused control (a tool, a
            // tab, a slider) forwards them; the canvas itself never takes
            // focus, so a stage-level handler would go quiet while painting.
            OnKeyDown = key => Viewport.HandleShortcut(editor, key),
        },
            _.C<OptionsBar, OptionsBarProps>(new() { }),
            _.C<ToolRail, ToolRailProps>(new() { }),
            _.C<Viewport, ViewportProps>(new() { }),
            _.C<SidePanel, SidePanelProps>(new() { }),
            _.C<StatusBar, StatusBarProps>(new() { }),
            // Late in the tree so its drop-downs are hit-tested before the
            // stage they overlap.
            C<MenuBar>(),
            // Floating UI paints above the canvas because the display list
            // honours z-index, so this is a real popup with its own input
            // and scrolling rather than a canvas-drawn approximation.
            Show(editor.IsFontMenuOpen, Div(new()
            {
                className = "font-backdrop",
                OnClick = () =>
                {
                    editor.CloseFontMenu();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsFontMenuOpen, _.C<FontPicker, FontPickerProps>(new() { })),
            Show(editor.IsTextFxOpen, Div(new()
            {
                className = "font-backdrop",
                OnClick = () =>
                {
                    editor.CloseTextFx();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsTextFxOpen, _.C<TextFxPanel, TextFxPanelProps>(new() { })),
            Show(editor.Tip is not null, _.C<TooltipOverlay, TooltipOverlayProps>(new() { })),
            Show(editor.OpenHelp is not null, Div(new()
            {
                className = "help-backdrop",
                OnClick = () =>
                {
                    editor.CloseHelp();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.OpenHelp is not null, _.C<HelpModal, HelpModalProps>(new() { })),
            Show(editor.IsFileDialogOpen, Div(new()
            {
                className = "file-backdrop",
                OnClick = () =>
                {
                    editor.CloseFileDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsFileDialogOpen, _.C<FileDialog, FileDialogProps>(new() { })),
            Show(editor.IsResizeOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseResizeDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsResizeOpen, _.C<ResizeDialog, ResizeDialogProps>(new() { })),
            Show(editor.IsBatchOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseBatchDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsBatchOpen, _.C<BatchDialog, BatchDialogProps>(new() { })),
            Show(editor.IsLayerMenuOpen, Div(new()
            {
                className = "file-menu-backdrop",
                OnClick = () => editor.CloseLayerMenu(),
            })),
            Show(editor.IsLayerMenuOpen, _.C<LayerMenu, LayerMenuProps>(new() { })),
            Show(editor.IsLayerStyleOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseLayerStyleDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsLayerStyleOpen, _.C<LayerStyleDialog, LayerStyleDialogProps>(new() { })),
            Show(editor.IsColourToolsOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CloseColourToolsDialog();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsColourToolsOpen, _.C<ColourToolsDialog, ColourToolsDialogProps>(new() { })),
            Show(editor.IsPerspectiveOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CancelPerspective();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsPerspectiveOpen, _.C<PerspectiveDialog, PerspectiveDialogProps>(new() { })),
            Show(editor.IsMeshWarpOpen, _.C<MeshWarpBar, MeshWarpBarProps>(new() { })),
            Show(editor.IsSmartTransformOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CancelSmartTransform();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsSmartTransformOpen, _.C<SmartObjectDialog, SmartObjectDialogProps>(new() { })),
            Show(editor.IsShapeEditOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CancelShapeEdit();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsShapeEditOpen, _.C<ShapeEditDialog, ShapeEditDialogProps>(new() { })),
            Show(editor.IsTextEditOpen, Div(new()
            {
                className = "resize-backdrop",
                OnClick = () =>
                {
                    editor.CancelTextEdit();
                    editor.Refresh?.Invoke();
                },
            })),
            Show(editor.IsTextEditOpen, _.C<TextEditDialog, TextEditDialogProps>(new() { })));

        root.BindRef(rootRef);
        if (rootRef.Value is { Width: > 0, Height: > 0 } bounds)
        {
            editor.ViewportWidth = bounds.Width;
            editor.ViewportHeight = bounds.Height;
        }

        return Provider(EditorContext.Value, editor, root);
    }
}
