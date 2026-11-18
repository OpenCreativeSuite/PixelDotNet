using System;
using Donkeh.UILab;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Components;

/// <summary>
/// The document area: a tab strip naming the document, then the scrollable stage
/// that centres the canvas and receives keyboard shortcuts.
/// </summary>
public sealed class Viewport : Component
{
    /// <summary>
    /// Pasteboard kept around the artwork and rulers so overlay scrollbars never
    /// cover the document edges and border pixels stay reachable.
    /// </summary>
    internal const float CanvasPadding = 250f;

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        Ref<ElementNode> viewportRef = UseRef<ElementNode>();
        ViewportPan.Attach(viewportRef);

        float zoom = MathF.Max(editor.Zoom, 0.05f);
        float ruler = editor.ShowRulers ? CanvasSurface.RulerSize : 0f;
        int pixelWidth = Math.Max(16, (int)MathF.Round(editor.Document.Width * zoom));
        int pixelHeight = Math.Max(16, (int)MathF.Round(editor.Document.Height * zoom));
        float contentWidth = pixelWidth + ruler + (CanvasPadding * 2f);
        float contentHeight = pixelHeight + ruler + (CanvasPadding * 2f);

        ElementNode viewport = (ElementNode)Div(new()
        {
            ClassName = "viewport",
            OnPointerMove = (x, y) => ViewportPan.Handle(),
        },
            Div(new()
            {
                ClassName = "canvas-slot",
                Attributes = new()
                {
                    ["style"] = $"width:{Ui.Number(contentWidth)}px;height:{Ui.Number(contentHeight)}px;",
                },
            },
                C<CanvasSurface>()));
        viewport.BindRef(viewportRef);

        return Div(new()
        {
            ClassName = "stage",
            TabIndex = 0,
        },
        Div(new() { ClassName = "doctabs" },
            Each(editor.Documents, info => (Node)DocumentTab(editor, info))),
        viewport);
    }

    /// <summary>Builds one document tab: a pick target, the name and a close button.</summary>
    private static Node DocumentTab(PaintSession editor, PaintSession.DocumentInfo info) =>
        Div(new() { ClassName = info.IsActive ? "doctab is-active" : "doctab" },
            Button(new()
            {
                ClassName = "doctab-select",
                Title = info.Name,
                OnClick = () => editor.SwitchDocument(info.Index),
            },
            Ui.Icon(Icons.Document, "doctab-icon"),
            Span(new() { ClassName = "doctab-name" }, Text(info.Name))),
            Show(editor.DocumentCount > 1, Button(new()
            {
                ClassName = "doctab-close",
                Title = "Close " + info.Name,
                OnClick = () => editor.CloseDocument(info.Index),
            }, Ui.Icon(Icons.Close))));

    /// <summary>
    /// Single-key shortcuts. The host reports a key name only, so modifiers are not
    /// available; every binding here is a bare key.
    /// </summary>
    internal static void HandleShortcut(PaintSession editor, string key)
    {
        // The font picker owns the keyboard while open: letters build a live
        // search, arrows move the highlight, Enter applies and Escape closes.
        if (editor.IsFontMenuOpen)
        {
            editor.FontMenuKey(key);
            return;
        }

        // Text fields own the keyboard while they have focus, so letters type
        // instead of switching tools.
        if (editor.IsTyping)
        {
            return;
        }

        if (editor.IsColourToolsOpen)
        {
            if (key == "Escape")
            {
                editor.CloseColourToolsDialog();
            }

            return;
        }

        if (editor.IsLayerStyleOpen)
        {
            if (key == "Escape")
            {
                editor.CloseLayerStyleDialog();
            }

            return;
        }

        if (editor.IsLayerMenuOpen)
        {
            if (key == "Escape")
            {
                editor.CloseLayerMenu();
            }

            return;
        }

        if (editor.IsBatchOpen)
        {
            if (key == "Escape")
            {
                editor.CloseBatchDialog();
            }
            else if (key is "Return" or "Enter")
            {
                editor.RunBatch();
            }

            return;
        }

        if (editor.IsResizeOpen)
        {
            if (key == "Escape")
            {
                editor.CloseResizeDialog();
            }
            else if (key is "Return" or "Enter")
            {
                editor.ApplyResize();
            }

            return;
        }

        if (editor.IsFileDialogOpen)
        {
            if (editor.IsFilePromptOpen)
            {
                if (key == "Escape")
                {
                    editor.FilePromptCancel();
                }
                else if (key is "Return" or "Enter")
                {
                    editor.FilePromptConfirm();
                }

                return;
            }

            if (editor.IsFileMenuOpen && key == "Escape")
            {
                editor.CloseFileContextMenu();
                return;
            }

            if (key == "Escape")
            {
                editor.CloseFileDialog();
            }
            else if (key is "Return" or "Enter")
            {
                editor.ConfirmFileDialog();
            }

            return;
        }

        if (editor.OpenHelp is not null && key == "Escape")
        {
            editor.CloseHelp();
            return;
        }

        if (editor.Tip is not null && key == "Escape")
        {
            editor.HideTip();
            return;
        }

        if (editor.IsTextFxOpen && key == "Escape")
        {
            editor.CloseTextFx();
            return;
        }

        switch (key)
        {
            case "V":
                editor.SelectTool(PaintTool.Move);
                break;
            case "M":
                editor.SelectTool(PaintTool.Marquee);
                break;
            case "Q":
                editor.SelectTool(PaintTool.EllipseSelect);
                break;
            case "F":
                editor.SelectTool(PaintTool.Lasso);
                break;
            case "W":
                editor.SelectTool(PaintTool.MagicWand);
                break;
            case "A":
                editor.SelectTool(PaintTool.QuickSelect);
                break;
            case "B":
                editor.SelectTool(PaintTool.Brush);
                break;
            case "E":
                editor.SelectTool(PaintTool.Eraser);
                break;
            case "C":
                editor.SelectTool(PaintTool.CloneStamp);
                break;
            case "H":
                editor.SelectTool(PaintTool.HealingBrush);
                break;
            case "J":
                editor.SelectTool(PaintTool.DodgeBurn);
                break;
            case "L":
                editor.SelectTool(PaintTool.Line);
                break;
            case "R":
                editor.SelectTool(PaintTool.Rectangle);
                break;
            case "O":
                editor.SelectTool(PaintTool.Ellipse);
                break;
            case "G":
                editor.SelectTool(PaintTool.Gradient);
                break;
            case "K":
                editor.SelectTool(PaintTool.Bucket);
                break;
            case "I":
                editor.SelectTool(PaintTool.Eyedropper);
                break;
            case "T":
                editor.SelectTool(PaintTool.Text);
                break;
            case "Z":
                editor.SelectTool(PaintTool.Zoom);
                break;
            case "Ctrl+Z":
                editor.Undo();
                break;
            case "Ctrl+Y":
            case "Ctrl+Shift+Z":
                editor.Redo();
                break;
            case "Ctrl+X":
                editor.Cut();
                break;
            case "Ctrl+C":
                editor.Copy();
                break;
            case "Ctrl+V":
                editor.Paste();
                break;
            case "Ctrl+Tab":
                editor.NextDocument();
                break;
            case "Ctrl+Shift+Tab":
                editor.PreviousDocument();
                break;
            case "Ctrl+D":
                editor.Deselect();
                break;
            case "Ctrl+N":
                editor.NewDocument(768, 576);
                break;
            case "Ctrl+O":
                editor.ShowOpenDialog();
                break;
            case "Ctrl+S":
                editor.ShowSaveDialog();
                break;
            case "Ctrl+W":
                editor.CloseActiveDocument();
                break;
            case "Ctrl+I":
                editor.Invert();
                break;
            case "Ctrl+Shift+U":
                editor.Desaturate();
                break;
            case "Ctrl+J":
                editor.DuplicateLayer();
                break;
            case "Ctrl+Shift+N":
                editor.AddLayer();
                break;
            case "Ctrl+E":
                editor.MergeDown();
                break;
            case "Ctrl+0":
            case "Ctrl+Number0":
                editor.ActualPixels();
                break;
            case "Ctrl+Equal":
            case "Ctrl+Shift+Equal":
            case "Ctrl+Plus":
                editor.ZoomIn();
                break;
            case "Ctrl+Minus":
                editor.ZoomOut();
                break;
            case "Y":
                editor.Redo();
                break;
            case "Tab":
                editor.NextDocument();
                break;
            case "X":
                editor.SwapColors();
                break;
            case "D":
                editor.ResetColors();
                break;
            case "LeftBracket":
                editor.BrushSize = MathF.Max(1f, editor.BrushSize - 2f);
                editor.Refresh?.Invoke();
                break;
            case "RightBracket":
                editor.BrushSize = MathF.Min(120f, editor.BrushSize + 2f);
                editor.Refresh?.Invoke();
                break;
            case "Delete":
                editor.ClearSelectedPixels();
                break;
            case "Escape":
                if (editor.IsFontMenuOpen)
                {
                    editor.CloseFontMenu();
                }
                else if (editor.IsFontAddOpen)
                {
                    editor.CancelFontAdd();
                }
                else
                {
                    editor.Deselect();
                }

                break;
            default:
                break;
        }
    }
}
