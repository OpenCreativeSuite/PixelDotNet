using System;
using System.Collections.Generic;
using System.IO;

namespace PixelDotNet.Paint;

/// <summary>
/// The whole editor state: document, tool settings, selection, zoom, adjustments
/// and undo history.
/// </summary>
/// <remarks>
/// <para>
/// The session is mutable on purpose. It lives in a ref, not in hook state, so a
/// pointer move can repaint the canvas without re-rendering the component tree.
/// UI that mirrors the model (tool highlight, colour readout, layer list, history)
/// calls <see cref="Refresh"/> when it needs to catch up.
/// </para>
/// <para>
/// Destructive drags (brush, eraser, shapes, gradient, move, adjustments) all work
/// from a pixel snapshot taken when the interaction starts and recompute the layer
/// from it. That is what keeps a semi-transparent brush from darkening where stamps
/// overlap, what lets the shape tools preview themselves, and what makes the live
/// adjustment sliders non-cumulative.
/// </para>
/// </remarks>
public sealed class PaintSession
{
    /// <summary>The discrete zoom stops the toolbar steps through.</summary>
    public static readonly float[] ZoomLevels = [0.25f, 0.5f, 0.6667f, 1f, 1.5f, 2f, 3f, 4f];

    private const int UndoDepth = 24;

    private List<DocumentSnapshot> _undo = [];
    private List<DocumentSnapshot> _redo = [];
    private readonly List<DocumentTab> _tabs = [];
    private readonly int[] _histogram = new int[64];
    private int _activeTab;
    private int _untitledCount;
    private ClipboardImage? _clipboard;
    private PaintLayer? _effectLayer;
    private uint[]? _effectBase;
    private PaintLayer? _textLayer;
    private uint[]? _textBase;
    private TextArtwork? _textArtwork;
    private int _textBitmapWidth;
    private int _textBitmapHeight;
    private int _textOriginX;
    private int _textOriginY;
    private string _textRenderKey = string.Empty;
    private bool _textFontAvailable;
    private int[] _fontMatches = [];
    private TextHandle _textHandle;
    private int _textDragX;
    private int _textDragY;
    private int _textDragAnchorX;
    private int _textDragAnchorY;
    private float _textDragSize;
    private float _textDragRotation;
    private float _textDragAngle;
    private float _textDragDistance;
    private float _textDragCenterX;
    private float _textDragCenterY;

    private PaintLayer? _strokeLayer;
    private uint[]? _strokeBase;
    private byte[]? _strokeMask;
    private int _maskX0;
    private int _maskY0;
    private int _maskX1 = -1;
    private int _maskY1 = -1;
    private float _startX;
    private float _startY;
    private float _lastX;
    private float _lastY;
    private bool _stroking;
    private bool _previewing;
    private bool _lassoing;
    private readonly List<(float X, float Y)> _lassoPoints = [];
    private bool _moving;
    private bool _movingSelection;
    private bool _resizingSelection;
    private ResizeEdges _resizeEdges;
    private bool _moveChanged;
    private Selection? _moveStart;
    private uint[]? _movePixels;
    private int _moveWidth;
    private int _moveHeight;

    private PaintLayer? _adjustLayer;
    private uint[]? _adjustBase;

    /// <summary>Wraps an existing document.</summary>
    public PaintSession(PaintDocument document, string documentName = "Untitled-1")
    {
        Document = document;
        DocumentName = documentName;
        _tabs.Add(new DocumentTab(document, documentName));
        _undo = _tabs[0].Undo;
        _redo = _tabs[0].Redo;

        if (documentName.StartsWith("Untitled-", StringComparison.Ordinal))
        {
            _untitledCount = 1;
        }

        PickerHue = Rgba.ToHsv(Foreground).Hue;
    }

    // ---- document ---------------------------------------------------------

    /// <summary>Gets the document being edited.</summary>
    public PaintDocument Document { get; private set; }

    /// <summary>Gets the document name shown in the tab and status bar.</summary>
    public string DocumentName { get; private set; }

    /// <summary>Gets the colour mode caption shown in the document tab.</summary>
    public string ColorMode => "RGB/8";

    /// <summary>One open document, as the tab strip needs to draw it.</summary>
    public readonly record struct DocumentInfo(int Index, string Name, int Width, int Height, bool IsActive);

    /// <summary>Gets every open document, oldest tab first.</summary>
    public IReadOnlyList<DocumentInfo> Documents
    {
        get
        {
            List<DocumentInfo> documents = new(_tabs.Count);
            for (int i = 0; i < _tabs.Count; i++)
            {
                DocumentTab tab = _tabs[i];
                documents.Add(new DocumentInfo(i, tab.Name, tab.Document.Width, tab.Document.Height, i == _activeTab));
            }

            return documents;
        }
    }

    /// <summary>Gets how many documents are open.</summary>
    public int DocumentCount => _tabs.Count;

    /// <summary>Gets whether there is more than one document, so one can be closed.</summary>
    public bool CanCloseDocument => _tabs.Count > 1;

    // ---- tool settings ----------------------------------------------------

    /// <summary>Gets or sets the selected tool.</summary>
    public PaintTool Tool { get; set; } = PaintTool.Brush;

    /// <summary>Gets or sets the foreground colour.</summary>
    public uint Foreground { get; set; } = Rgba.FromRgb(0x2F, 0x4A, 0x8B);    /// <summary>Gets or sets the background colour.</summary>
    public uint Background { get; set; } = Rgba.White;

    /// <summary>Gets or sets the brush diameter in document pixels.</summary>
    public float BrushSize { get; set; } = 18f;

    /// <summary>Gets or sets the brush edge hardness in the 0-1 range.</summary>
    public float Hardness { get; set; } = 0.8f;

    /// <summary>Gets or sets the tool opacity in the 0-1 range.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Gets or sets the flood-fill colour tolerance.</summary>
    public int Tolerance { get; set; } = 32;

    /// <summary>Gets or sets the magic wand's colour tolerance.</summary>
    public int MagicTolerance { get; set; } = 32;

    /// <summary>Gets or sets whether the magic wand only selects the connected region.</summary>
    public bool MagicContiguous { get; set; } = true;

    /// <summary>Gets or sets whether the shape tools fill instead of outline.</summary>
    public bool FillShapes { get; set; } = true;

    /// <summary>Gets or sets the outline width used when shapes are not filled.</summary>
    public float StrokeWidth { get; set; } = 4f;

    // ---- view -------------------------------------------------------------

    /// <summary>Gets or sets the canvas zoom factor.</summary>
    public float Zoom { get; set; } = 1f;

    /// <summary>Gets or sets whether the eight-pixel guide grid is drawn.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Gets the active selection, or null when nothing is selected.</summary>
    public Selection? Selection { get; private set; }

    /// <summary>Gets the freehand points of an in-progress lasso, for the canvas preview.</summary>
    public IReadOnlyList<(float X, float Y)> LassoPoints => _lassoPoints;

    /// <summary>Gets or sets the last pointer column in document pixels, or -1.</summary>
    public int CursorX { get; private set; } = -1;

    /// <summary>Gets or sets the last pointer row in document pixels, or -1.</summary>
    public int CursorY { get; private set; } = -1;

    /// <summary>Gets or sets the active inspector tab: 0 layers, 1 colour, 2 adjust, 3 history.</summary>
    public int SideTab { get; set; }

    /// <summary>Gets or sets the hue the colour picker remembers while saturation is zero.</summary>
    public float PickerHue { get; set; }

    /// <summary>Gets or sets the transient message shown in the status bar.</summary>
    public string Status { get; set; } = "Ready";

    /// <summary>Gets or sets the name of the open menu, or null when all are closed.</summary>
    public string? OpenMenu { get; set; }

    /// <summary>Gets or sets the callback that re-renders the components.</summary>
    public Action? Refresh { get; set; }

    // ---- adjustments ------------------------------------------------------

    /// <summary>Gets or sets the live brightness offset, -100 to 100.</summary>
    public float AdjustBrightness { get; set; }

    /// <summary>Gets or sets the live contrast offset, -100 to 100.</summary>
    public float AdjustContrast { get; set; }

    /// <summary>Gets or sets the live hue rotation in degrees, -180 to 180.</summary>
    public float AdjustHue { get; set; }

    /// <summary>Gets or sets the live saturation offset, -100 to 100.</summary>
    public float AdjustSaturation { get; set; }

    /// <summary>Gets or sets the live lightness offset, -100 to 100.</summary>
    public float AdjustLightness { get; set; }

    /// <summary>Gets whether any live adjustment is away from neutral.</summary>
    public bool HasAdjustments =>
        AdjustBrightness != 0f || AdjustContrast != 0f || AdjustHue != 0f || AdjustSaturation != 0f || AdjustLightness != 0f;

    // ---- history ----------------------------------------------------------

    /// <summary>Gets whether there is an undoable step.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets whether there is a redoable step.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Gets the history steps, oldest first, with the opening state at index zero.</summary>
    public IReadOnlyList<string> HistorySteps
    {
        get
        {
            List<string> steps = ["Open " + DocumentName];
            foreach (DocumentSnapshot snapshot in _undo)
            {
                steps.Add(snapshot.Name);
            }

            return steps;
        }
    }

    /// <summary>Gets the index of the current state in <see cref="HistorySteps"/>.</summary>
    public int HistoryIndex => _undo.Count;

    // ---- lifecycle --------------------------------------------------------

    /// <summary>Builds the session the app starts with: sample artwork and an empty ink layer.</summary>
    public static PaintSession CreateDemo() => new(DemoArt.BuildDocument(), "Moonlight");

    /// <summary>Gets the display name of a tool.</summary>
    public static string NameOf(PaintTool tool) => tool switch
    {
        PaintTool.Move => "Move",
        PaintTool.Marquee => "Marquee",
        PaintTool.EllipseSelect => "Elliptical Select",
        PaintTool.Lasso => "Lasso Select",
        PaintTool.MagicWand => "Magic Wand",
        PaintTool.Brush => "Brush",
        PaintTool.Eraser => "Eraser",
        PaintTool.Line => "Line",
        PaintTool.Rectangle => "Rectangle",
        PaintTool.Ellipse => "Ellipse",
        PaintTool.Gradient => "Gradient",
        PaintTool.Bucket => "Paint Bucket",
        PaintTool.Eyedropper => "Eyedropper",
        PaintTool.Text => "Text",
        _ => "Zoom",
    };

    /// <summary>Gets the single-key shortcut of a tool.</summary>
    public static string ShortcutOf(PaintTool tool) => tool switch
    {
        PaintTool.Move => "V",
        PaintTool.Marquee => "M",
        PaintTool.EllipseSelect => "Q",
        PaintTool.Lasso => "F",
        PaintTool.MagicWand => "W",
        PaintTool.Brush => "B",
        PaintTool.Eraser => "E",
        PaintTool.Line => "L",
        PaintTool.Rectangle => "R",
        PaintTool.Ellipse => "O",
        PaintTool.Gradient => "G",
        PaintTool.Bucket => "K",
        PaintTool.Eyedropper => "I",
        PaintTool.Text => "T",
        _ => "Z",
    };

    /// <summary>Selects a tool and re-renders.</summary>
    public void SelectTool(PaintTool tool)
    {
        if (Tool == tool)
        {
            return;
        }

        CommitPreviews();
        Tool = tool;
        IsFontMenuOpen = false;
        IsFontSearchFocused = false;
        IsTextFxOpen = false;
        ColorTarget = TextColorTarget.Fill;
        _lassoing = false;
        _lassoPoints.Clear();
        Status = NameOf(tool) + " tool";
        Changed();
    }

    /// <summary>Sets the foreground colour from three 0-255 channels.</summary>
    public void SetForeground(int r, int g, int b)
    {
        Foreground = Rgba.FromRgb(r, g, b);
        PickerHue = Rgba.ToHsv(Foreground).Hue;
        Changed();
    }

    /// <summary>Sets whichever text colour the colour panel is editing.</summary>
    public void SetActiveColor(int r, int g, int b)
    {
        uint color = Rgba.FromRgb(r, g, b);
        switch (ColorTarget)
        {
            case TextColorTarget.Outline:
                TextOutlineColor = color;
                TextPreset = TextPreset.Custom;
                break;

            case TextColorTarget.Shadow:
                TextShadowColor = color;
                TextPreset = TextPreset.Custom;
                break;

            default:
                Foreground = color;
                break;
        }

        PickerHue = Rgba.ToHsv(color).Hue;
        RefreshTextPreview();
        Changed();
    }

    /// <summary>Sets the active colour from the palette and remembers the picker hue.</summary>
    public void SetActiveColor(uint color) => SetActiveColor(Rgba.R(color), Rgba.G(color), Rgba.B(color));

    /// <summary>Switches the colour panel to one of the text effect colours and opens it.</summary>
    public void EditTextColour(TextColorTarget target)
    {
        ColorTarget = target;
        PickerHue = Rgba.ToHsv(ActiveColor).Hue;
        OpenSideTab(1);
    }

    /// <summary>Sets the foreground colour and remembers the picker hue.</summary>
    public void SetHsv(float hue, float saturation, float value)
    {
        PickerHue = ((hue % 1f) + 1f) % 1f;
        uint color = Rgba.FromHsv(PickerHue, saturation, value);
        switch (ColorTarget)
        {
            case TextColorTarget.Outline:
                TextOutlineColor = color;
                TextPreset = TextPreset.Custom;
                break;

            case TextColorTarget.Shadow:
                TextShadowColor = color;
                TextPreset = TextPreset.Custom;
                break;

            default:
                Foreground = color;
                break;
        }

        RefreshTextPreview();
        Changed();
    }

    /// <summary>Opens an inspector tab: 0 layers, 1 colour, 2 adjust, 3 history, 4 effects.</summary>
    public void OpenSideTab(int tab)
    {
        SideTab = Math.Clamp(tab, 0, 4);
        Changed();
    }

    /// <summary>Opens the colour tab and edits the text fill colour.</summary>
    public void OpenColourTab()
    {
        ColorTarget = TextColorTarget.Fill;
        OpenSideTab(1);
    }

    /// <summary>Sets the foreground colour.</summary>
    public void SetForeground(uint color)
    {
        Foreground = Rgba.Opaque(color);
        PickerHue = Rgba.ToHsv(Foreground).Hue;
        RefreshTextPreview();
        Changed();
    }

    /// <summary>Swaps the foreground and background colours.</summary>
    public void SwapColors()
    {
        (Foreground, Background) = (Background, Foreground);
        PickerHue = Rgba.ToHsv(Foreground).Hue;
        ColorTarget = TextColorTarget.Fill;
        RefreshTextPreview();
        Changed();
    }

    /// <summary>Restores the default black-on-white colour pair.</summary>
    public void ResetColors()
    {
        Foreground = Rgba.FromRgb(0x1A, 0x1A, 0x1A);
        Background = Rgba.White;
        PickerHue = 0f;
        ColorTarget = TextColorTarget.Fill;
        RefreshTextPreview();
        Changed();
    }

    // ---- view -------------------------------------------------------------

    /// <summary>Steps the zoom up one stop.</summary>
    public void ZoomIn()
    {
        Zoom = StepZoom(Zoom, 1);
        Status = $"{MathF.Round(Zoom * 100f)}%";
        Changed();
    }

    /// <summary>Steps the zoom down one stop.</summary>
    public void ZoomOut()
    {
        Zoom = StepZoom(Zoom, -1);
        Status = $"{MathF.Round(Zoom * 100f)}%";
        Changed();
    }

    /// <summary>Returns to one document pixel per CSS pixel.</summary>
    public void ActualPixels()
    {
        Zoom = 1f;
        Status = "100%";
        Changed();
    }

    /// <summary>Toggles the guide grid.</summary>
    public void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
        Changed();
    }

    /// <summary>Toggles a top-level menu open or closed.</summary>
    public void ToggleMenu(string name)
    {
        OpenMenu = OpenMenu == name ? null : name;
        Changed();
    }

    /// <summary>Closes any open menu.</summary>
    public void CloseMenu()
    {
        if (OpenMenu is null)
        {
            return;
        }

        OpenMenu = null;
        Changed();
    }

    // ---- pointer input ----------------------------------------------------

    /// <summary>Starts a pointer interaction at a canvas-local position.</summary>
    public void PointerDown(float localX, float localY)
    {
        // Painting on top of a live effect preview must lock in the preview first,
        // otherwise the next slider move would erase the stroke.
        (float documentX, float documentY) = ToDocument(localX, localY);
        int x = (int)MathF.Floor(documentX);
        int y = (int)MathF.Floor(documentY);
        CursorX = x;
        CursorY = y;
        _startX = documentX;
        _startY = documentY;
        _lastX = documentX;
        _lastY = documentY;

        if (Tool == PaintTool.Text)
        {
            if (Inside(x, y))
            {
                TextPointerDown(x, y);
            }

            return;
        }

        // Painting with a live preview would bake the stroke into the snapshot;
        // commit the previews first so the next slider move cannot undo the paint.
        CommitEffect();
        CommitText();

        if (Tool == PaintTool.Zoom)
        {
            ZoomIn();
            return;
        }

        if (!Inside(x, y))
        {
            return;
        }

        if (Tool == PaintTool.Eyedropper)
        {
            Foreground = Rgba.Opaque(Document.CompositeAt(x, y));
            PickerHue = Rgba.ToHsv(Foreground).Hue;
            Status = "Picked " + Hex(Foreground);
            Changed();
            return;
        }

        if (Tool == PaintTool.Marquee)
        {
            _previewing = true;
            Selection = Selection.FromPoints(documentX, documentY, documentX, documentY, Document.Width, Document.Height);
            Changed();
            return;
        }

        if (Tool == PaintTool.EllipseSelect)
        {
            _previewing = true;
            Selection = Selection.EllipseFromPoints(documentX, documentY, documentX, documentY, Document.Width, Document.Height);
            Changed();
            return;
        }

        if (Tool == PaintTool.Lasso)
        {
            BeginLasso(documentX, documentY);
            return;
        }

        if (Tool == PaintTool.MagicWand)
        {
            SelectMagicRegion(x, y);
            return;
        }

        if (Document.Active.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        switch (Tool)
        {
            case PaintTool.Bucket:
                PushUndo("Fill");
                PaintOps.FloodFill(Document.Active, x, y, Foreground, Tolerance, Alpha, Selection);
                Changed();
                return;

            case PaintTool.Move:
                if (Selection is { IsEmpty: false } selection)
                {
                    ResizeEdges edges = HitSelectionHandle(selection, documentX, documentY);
                    if (edges != ResizeEdges.None)
                    {
                        BeginSelectionResize(selection, edges);
                    }
                    else
                    {
                        BeginSelectionMove(selection);
                    }
                }
                else
                {
                    PushUndo("Move");
                    _strokeLayer = Document.Active;
                    _strokeBase = Document.Active.CapturePixels();
                    _moveChanged = false;
                    _moving = true;
                }

                return;

            case PaintTool.Line:
            case PaintTool.Rectangle:
            case PaintTool.Ellipse:
            case PaintTool.Gradient:
                PushUndo(NameOf(Tool));
                _strokeLayer = Document.Active;
                _strokeBase = Document.Active.CapturePixels();
                _previewing = true;
                PreviewShape(documentX, documentY);
                return;

            default:
                BeginStroke(x, y);
                return;
        }
    }

    /// <summary>Extends or previews the active interaction.</summary>
    public void PointerMove(float localX, float localY)
    {
        (float documentX, float documentY) = ToDocument(localX, localY);
        CursorX = (int)MathF.Floor(documentX);
        CursorY = (int)MathF.Floor(documentY);

        if (Tool == PaintTool.Text)
        {
            TextPointerMove(CursorX, CursorY);
            return;
        }

        if (_lassoing)
        {
            ExtendLasso(documentX, documentY);
            return;
        }

        if (_previewing)
        {
            if (Tool == PaintTool.Marquee)
            {
                Selection = Selection.FromPoints(_startX, _startY, documentX, documentY, Document.Width, Document.Height);
            }
            else if (Tool == PaintTool.EllipseSelect)
            {
                Selection = Selection.EllipseFromPoints(_startX, _startY, documentX, documentY, Document.Width, Document.Height);
            }
            else
            {
                PreviewShape(documentX, documentY);
            }

            return;
        }

        if (_resizingSelection)
        {
            ResizeSelectionTo(documentX, documentY);
            return;
        }

        if (_movingSelection)
        {
            int dx = (int)MathF.Round(documentX - _startX);
            int dy = (int)MathF.Round(documentY - _startY);
            MoveSelectionBy(dx, dy);
            return;
        }

        if (_moving && _strokeLayer is not null && _strokeBase is not null)
        {
            int dx = (int)MathF.Round(documentX - _startX);
            int dy = (int)MathF.Round(documentY - _startY);
            _moveChanged |= dx != 0 || dy != 0;
            PaintOps.Translate(_strokeLayer, _strokeBase, dx, dy);
            Document.MarkDirty();
            Status = $"Move {dx}, {dy}";
            return;
        }

        if (!_stroking)
        {
            return;
        }

        float targetX = documentX;
        float targetY = documentY;
        StampLine(_lastX, _lastY, targetX, targetY);
        _lastX = targetX;
        _lastY = targetY;
        ApplyMask();
    }

    /// <summary>Finishes the active interaction.</summary>
    public void PointerUp(float localX, float localY)
    {
        (float documentX, float documentY) = ToDocument(localX, localY);
        CursorX = (int)MathF.Floor(documentX);
        CursorY = (int)MathF.Floor(documentY);

        if (Tool == PaintTool.Text)
        {
            TextPointerUp();
            return;
        }

        if (Tool == PaintTool.Lasso && _lassoing)
        {
            FinishLasso();
            return;
        }

        if ((Tool is PaintTool.Marquee or PaintTool.EllipseSelect) && _previewing)
        {
            _previewing = false;
            if (Selection is null || Selection.IsEmpty)
            {
                Selection = null;
                Status = "Deselected";
            }
            else
            {
                Status = $"Selection {Selection.Width} x {Selection.Height}";
            }

            Changed();
            return;
        }

        if (_resizingSelection)
        {
            _resizingSelection = false;
            _moveStart = null;
            _movePixels = null;
            _strokeLayer = null;
            _strokeBase = null;
            DropUnchangedMove();
            Changed();
            return;
        }

        if (_movingSelection)
        {
            _movingSelection = false;
            _moveStart = null;
            _movePixels = null;
            _strokeLayer = null;
            _strokeBase = null;
            DropUnchangedMove();
            Changed();
            return;
        }

        if (_moving)
        {
            _moving = false;
            _strokeLayer = null;
            _strokeBase = null;
            DropUnchangedMove();
            Changed();
            return;
        }

        if (_previewing)
        {
            _previewing = false;
            _strokeLayer = null;
            _strokeBase = null;
            Changed();
            return;
        }

        if (!_stroking)
        {
            return;
        }

        _stroking = false;
        _strokeLayer = null;
        _strokeBase = null;
        _strokeMask = null;
        Changed();
    }

    /// <summary>
    /// Lifts the selected pixels off the active layer so a drag can move them.
    /// </summary>
    /// <remarks>
    /// The layer is snapshotted first, then every move rebuilds the layer from
    /// that snapshot: the original rectangle is cleared and the lifted pixels are
    /// put down at the new offset. That keeps the hole consistent no matter how
    /// far the pointer moves, and the marquee travels with the pixels.
    /// </remarks>
    private void BeginSelectionMove(Selection selection)
    {
        PushUndo("Move Selection");
        StartSelectionLift(selection);
        _movingSelection = true;
    }

    /// <summary>Starts a resize drag from one of the selection's handles.</summary>
    private void BeginSelectionResize(Selection selection, ResizeEdges edges)
    {
        PushUndo("Resize Selection");
        StartSelectionLift(selection);
        _resizingSelection = true;
        _resizeEdges = edges;
    }

    /// <summary>Snapshots the layer and copies the selection into a buffer.</summary>
    private void StartSelectionLift(Selection selection)
    {
        _strokeLayer = Document.Active;
        _strokeBase = Document.Active.CapturePixels();
        _moveChanged = false;
        _moveStart = selection;
        _moveWidth = selection.Width;
        _moveHeight = selection.Height;
        _movePixels = new uint[_moveWidth * _moveHeight];

        for (int row = 0; row < _moveHeight; row++)
        {
            Array.Copy(
                _strokeBase,
                ((selection.Y + row) * Document.Width) + selection.X,
                _movePixels,
                row * _moveWidth,
                _moveWidth);
        }
    }

    /// <summary>Returns which edge handles a pointer position is grabbing.</summary>
    private ResizeEdges HitSelectionHandle(Selection selection, float x, float y)
    {
        if (!selection.CanResize)
        {
            return ResizeEdges.None;
        }

        float radius = 6f / MathF.Max(Zoom, 0.05f);
        float left = selection.X;
        float right = selection.Right;
        float top = selection.Y;
        float bottom = selection.Bottom;
        float midX = (left + right) * 0.5f;
        float midY = (top + bottom) * 0.5f;

        // Corners first: on a tiny selection their hit boxes overlap the edges.
        if (Near(x, y, left, top, radius))
        {
            return ResizeEdges.Left | ResizeEdges.Top;
        }

        if (Near(x, y, right, top, radius))
        {
            return ResizeEdges.Right | ResizeEdges.Top;
        }

        if (Near(x, y, right, bottom, radius))
        {
            return ResizeEdges.Right | ResizeEdges.Bottom;
        }

        if (Near(x, y, left, bottom, radius))
        {
            return ResizeEdges.Left | ResizeEdges.Bottom;
        }

        if (Near(x, y, left, midY, radius))
        {
            return ResizeEdges.Left;
        }

        if (Near(x, y, right, midY, radius))
        {
            return ResizeEdges.Right;
        }

        if (Near(x, y, midX, top, radius))
        {
            return ResizeEdges.Top;
        }

        if (Near(x, y, midX, bottom, radius))
        {
            return ResizeEdges.Bottom;
        }

        return ResizeEdges.None;
    }

    /// <summary>Returns whether two points are within one square hit box.</summary>
    private static bool Near(float x, float y, float targetX, float targetY, float radius) =>
        MathF.Abs(x - targetX) <= radius && MathF.Abs(y - targetY) <= radius;

    /// <summary>Rebuilds the layer with the lifted pixels scaled into a new rectangle.</summary>
    private void ResizeSelectionTo(float documentX, float documentY)
    {
        if (_strokeLayer is not { } layer || _strokeBase is null || _movePixels is null || _moveStart is not { } start)
        {
            return;
        }

        if (!start.CanResize)
        {
            return;
        }

        int left = start.X;
        int top = start.Y;
        int right = start.Right;
        int bottom = start.Bottom;

        if ((_resizeEdges & ResizeEdges.Left) != 0)
        {
            left = Math.Clamp((int)MathF.Round(documentX), 0, right - 1);
        }

        if ((_resizeEdges & ResizeEdges.Right) != 0)
        {
            right = Math.Clamp((int)MathF.Round(documentX), left + 1, Document.Width);
        }

        if ((_resizeEdges & ResizeEdges.Top) != 0)
        {
            top = Math.Clamp((int)MathF.Round(documentY), 0, bottom - 1);
        }

        if ((_resizeEdges & ResizeEdges.Bottom) != 0)
        {
            bottom = Math.Clamp((int)MathF.Round(documentY), top + 1, Document.Height);
        }

        int newWidth = right - left;
        int newHeight = bottom - top;
        if (newWidth == start.Width && newHeight == start.Height && left == start.X && top == start.Y)
        {
            return;
        }

        _moveChanged = true;
        Array.Copy(_strokeBase, layer.Pixels, _strokeBase.Length);

        for (int y = start.Y; y < start.Bottom; y++)
        {
            for (int x = start.X; x < start.Right; x++)
            {
                if (start.Contains(x, y))
                {
                    layer.Put(x, y, Rgba.Transparent);
                }
            }
        }

        // Nearest-neighbour scale: destination coordinates map back into the lifted
        // buffer, which keeps hard pixel edges instead of inventing smooth ones.
        // An elliptical selection gets a fresh inscribed mask at its new size.
        byte[]? destinationMask = start.Shape == SelectionShape.Ellipse
            ? Selection.CreateEllipseMask(newWidth, newHeight)
            : null;

        for (int row = 0; row < newHeight; row++)
        {
            int sourceRow = (row * _moveHeight / newHeight) * _moveWidth;
            int targetY = top + row;

            for (int column = 0; column < newWidth; column++)
            {
                if (destinationMask is not null && destinationMask[(row * newWidth) + column] == 0)
                {
                    continue;
                }

                int sourceX = column * _moveWidth / newWidth;
                int sourceY = row * _moveHeight / newHeight;
                if (!start.Contains(start.X + sourceX, start.Y + sourceY))
                {
                    layer.Put(left + column, targetY, Rgba.Transparent);
                    continue;
                }

                layer.Put(left + column, targetY, _movePixels[sourceRow + sourceX]);
            }
        }

        Selection = start.Shape == SelectionShape.Ellipse
            ? Selection.Ellipse(left, top, newWidth, newHeight)
            : new Selection(left, top, newWidth, newHeight);
        Status = $"Resize {newWidth} ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â {newHeight}";
        Document.MarkDirty();
    }

    /// <summary>Rebuilds the active layer with the lifted selection at an offset.</summary>
    private void MoveSelectionBy(int dx, int dy)
    {
        if (_strokeLayer is not { } layer || _strokeBase is null || _movePixels is null || _moveStart is not { } start)
        {
            return;
        }

        // Keep the marquee inside the document so every other selection operation
        // can assume its rectangle is addressable.
        dx = Math.Clamp(dx, -start.X, Document.Width - start.Right);
        dy = Math.Clamp(dy, -start.Y, Document.Height - start.Bottom);
        _moveChanged |= dx != 0 || dy != 0;

        Array.Copy(_strokeBase, layer.Pixels, _strokeBase.Length);

        for (int y = start.Y; y < start.Bottom; y++)
        {
            for (int x = start.X; x < start.Right; x++)
            {
                if (start.Contains(x, y))
                {
                    layer.Put(x, y, Rgba.Transparent);
                }
            }
        }

        for (int row = 0; row < _moveHeight; row++)
        {
            int targetY = start.Y + dy + row;
            int sourceRow = row * _moveWidth;

            for (int column = 0; column < _moveWidth; column++)
            {
                if (!start.Contains(start.X + column, start.Y + row))
                {
                    continue;
                }

                layer.Put(start.X + dx + column, targetY, _movePixels[sourceRow + column]);
            }
        }

        Selection = start.Shifted(dx, dy);
        Status = dx == 0 && dy == 0 ? "Move selection" : $"Move {dx}, {dy}";
        Document.MarkDirty();
    }

    /// <summary>Drops the undo step a move pushed when the drag never actually moved.</summary>
    private void DropUnchangedMove()
    {
        if (_moveChanged || _undo.Count == 0)
        {
            return;
        }

        _undo.RemoveAt(_undo.Count - 1);
        _moveChanged = false;
    }

    /// <summary>Right-click / secondary action: picks a colour, or zooms out with the zoom tool.</summary>
    public void SecondaryAction(float localX, float localY)
    {
        (float documentX, float documentY) = ToDocument(localX, localY);
        int x = (int)MathF.Floor(documentX);
        int y = (int)MathF.Floor(documentY);

        if (Tool == PaintTool.Zoom)
        {
            ZoomOut();
            return;
        }

        if (Inside(x, y))
        {
            Foreground = Rgba.Opaque(Document.CompositeAt(x, y));
            PickerHue = Rgba.ToHsv(Foreground).Hue;
            Status = "Picked " + Hex(Foreground);
            Changed();
        }
    }

    // ---- selection --------------------------------------------------------

    /// <summary>Starts a freehand selection and records its first point.</summary>
    private void BeginLasso(float x, float y)
    {
        _lassoing = true;
        _lassoPoints.Clear();
        _lassoPoints.Add((x, y));
        Status = "Draw a selection";
        Changed();
    }

    /// <summary>Adds a point once the pointer has moved at least a pixel.</summary>
    private void ExtendLasso(float x, float y)
    {
        (float lastX, float lastY) = _lassoPoints[^1];
        float dx = x - lastX;
        float dy = y - lastY;
        if ((dx * dx) + (dy * dy) < 1f)
        {
            return;
        }

        _lassoPoints.Add((x, y));
    }

    /// <summary>Closes the lasso outline and turns it into a selection.</summary>
    private void FinishLasso()
    {
        _lassoing = false;
        Selection = SelectionOps.FromPolygon(_lassoPoints, Document.Width, Document.Height);
        _lassoPoints.Clear();
        Status = Selection is null
            ? "Deselected"
            : $"Selection {Selection.Width} x {Selection.Height}";
        Changed();
    }

    /// <summary>Selects the colour region under a pointer position with the wand.</summary>
    private void SelectMagicRegion(int x, int y)
    {
        Selection = SelectionOps.MagicWand(
            Document,
            x,
            y,
            MagicTolerance,
            MagicContiguous);

        Status = Selection is null
            ? "Nothing selected"
            : $"Selection {Selection.Width} x {Selection.Height}";
        Changed();
    }

    /// <summary>Selects the whole document.</summary>
    public void SelectAll()
    {
        Selection = new Selection(0, 0, Document.Width, Document.Height);
        Status = "Selected all";
        Changed();
    }

    /// <summary>Clears the selection.</summary>
    public void Deselect()
    {
        Selection = null;
        _lassoing = false;
        _lassoPoints.Clear();
        Status = "Deselected";
        Changed();
    }

    /// <summary>Fills the selection, or the whole layer, with the foreground colour.</summary>
    public void FillSelection()
    {
        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        PushUndo("Fill Selection");
        int x0 = Selection?.X ?? 0;
        int y0 = Selection?.Y ?? 0;
        int x1 = Selection?.Right ?? Document.Width;
        int y1 = Selection?.Bottom ?? Document.Height;
        int alpha = Alpha;

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                PaintOps.Blend(layer, x, y, Foreground, alpha, Selection);
            }
        }

        Changed();
    }

    /// <summary>Clears the selected pixels, or the whole layer when nothing is selected.</summary>
    public void ClearSelectedPixels()
    {
        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            return;
        }

        PushUndo("Clear");
        ClearRegion(layer);
        Changed();
    }

    // ---- clipboard --------------------------------------------------------

    /// <summary>Gets whether there is copied pixels ready to paste.</summary>
    public bool HasClipboard => _clipboard is not null;

    /// <summary>Copies the selected area of the active layer.</summary>
    public void Copy()
    {
        ClipboardImage? image = Capture(merged: false);
        string note = string.Empty;

        if (image is null)
        {
            return;
        }

        _clipboard = image;
        Status = image.Empty
            ? $"Copied {image.Width} ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â {image.Height} (empty area)"
            : $"Copied {image.Width} ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â {image.Height}{note}";
        Changed();
    }

    /// <summary>Copies what is visible, as if every layer were flattened.</summary>
    public void CopyMerged()
    {
        if (Capture(merged: true) is not { } image)
        {
            return;
        }

        _clipboard = image;
        Status = $"Copied merged {image.Width} ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â {image.Height}";
        Changed();
    }

    /// <summary>Cuts the selected area of the active layer onto the clipboard.</summary>
    public void Cut()
    {
        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        if (Capture(merged: false) is not { } image || image.Empty)
        {
            Status = "Nothing to cut on this layer";
            Changed();
            return;
        }

        _clipboard = image;
        PushUndo("Cut");
        ClearRegion(layer);
        Status = $"Cut {image.Width} ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â {image.Height}";
        Changed();
    }

    /// <summary>Pastes the clipboard as a new layer above the active one.</summary>
    public void Paste()
    {
        if (_clipboard is not { } clip)
        {
            return;
        }

        CommitPreviews();
        PushUndo("Paste");

        PaintLayer layer = new(FreeLayerName("Pasted Layer"), Document.Width, Document.Height);

        // Paste back where it was cut from, unless that would hang off the
        // canvas ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â then centre it, which is what matters across documents.
        int originX = clip.OriginX;
        int originY = clip.OriginY;
        if (originX + clip.Width > Document.Width || originY + clip.Height > Document.Height)
        {
            originX = Math.Max(0, (Document.Width - clip.Width) / 2);
            originY = Math.Max(0, (Document.Height - clip.Height) / 2);
        }

        for (int row = 0; row < clip.Height; row++)
        {
            int y = originY + row;
            if (y < 0 || y >= Document.Height)
            {
                continue;
            }

            for (int column = 0; column < clip.Width; column++)
            {
                int x = originX + column;
                if (x >= 0 && x < Document.Width)
                {
                    layer.Put(x, y, clip.Pixels[(row * clip.Width) + column]);
                }
            }
        }

        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        Status = $"Pasted {clip.Width} ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â {clip.Height}";
        Changed();
    }

    /// <summary>Copies the selection, or the whole document, without touching the clipboard.</summary>
    private ClipboardImage? Capture(bool merged)
    {
        int originX = Selection?.X ?? 0;
        int originY = Selection?.Y ?? 0;
        int width = Selection?.Width ?? Document.Width;
        int height = Selection?.Height ?? Document.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        uint[] source = merged ? Document.Composite : Document.Active.Pixels;
        uint[] pixels = new uint[width * height];
        bool empty = true;
        Selection? selection = Selection;
        bool masked = selection?.Mask is not null;

        for (int row = 0; row < height; row++)
        {
            Array.Copy(source, ((originY + row) * Document.Width) + originX, pixels, row * width, width);

            if (masked)
            {
                for (int column = 0; column < width; column++)
                {
                    if (!selection!.Contains(originX + column, originY + row))
                    {
                        pixels[(row * width) + column] = Rgba.Transparent;
                    }
                }
            }

            if (empty)
            {
                for (int column = 0; column < width; column++)
                {
                    if (Rgba.A(pixels[(row * width) + column]) != 0)
                    {
                        empty = false;
                        break;
                    }
                }
            }
        }

        return new ClipboardImage(width, height, originX, originY, pixels, empty);
    }

    /// <summary>Clears the selection, or the whole layer, to transparency.</summary>
    private void ClearRegion(PaintLayer layer)
    {
        if (Selection is null)
        {
            layer.Fill(Rgba.Transparent);
            return;
        }

        for (int y = Selection.Y; y < Selection.Bottom; y++)
        {
            for (int x = Selection.X; x < Selection.Right; x++)
            {
                if (Selection.Contains(x, y))
                {
                    layer.Put(x, y, Rgba.Transparent);
                }
            }
        }
    }

    /// <summary>Returns a layer name that is not already used in this document.</summary>
    private string FreeLayerName(string preferred)
    {
        if (!HasLayerNamed(preferred))
        {
            return preferred;
        }

        int suffix = 2;
        while (HasLayerNamed(preferred + " " + suffix))
        {
            suffix++;
        }

        return preferred + " " + suffix;
    }

    /// <summary>Returns whether any layer in the document has the given name.</summary>
    private bool HasLayerNamed(string name)
    {
        for (int i = 0; i < Document.Layers.Count; i++)
        {
            if (Document.Layers[i].Name == name)
            {
                return true;
            }
        }

        return false;
    }

    // ---- layers -----------------------------------------------------------

    /// <summary>Inserts an empty layer above the active one and selects it.</summary>
    public void AddLayer()
    {
        CommitPreviews();
        PushUndo("New Layer");
        PaintLayer layer = new($"Layer {Document.Layers.Count + 1}", Document.Width, Document.Height);
        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        Status = "New layer";
        Changed();
    }

    /// <summary>Copies the active layer above itself.</summary>
    public void DuplicateLayer()
    {
        CommitPreviews();
        PushUndo("Duplicate Layer");
        PaintLayer source = Document.Active;
        PaintLayer copy = new(source.Name + " copy", Document.Width, Document.Height)
        {
            Opacity = source.Opacity,
            Blend = source.Blend,
        };
        copy.Adopt(source.CapturePixels());

        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, copy);
        Document.ActiveIndex = insertAt;
        Status = "Duplicated layer";
        Changed();
    }

    /// <summary>Merges the active layer down into the one below it.</summary>
    public void MergeDown()
    {
        int index = Math.Clamp(Document.ActiveIndex, 0, Document.Layers.Count - 1);
        if (index <= 0)
        {
            Status = "No layer below";
            Changed();
            return;
        }

        CommitPreviews();
        PushUndo("Merge Down");
        PaintLayer top = Document.Layers[index];
        PaintLayer bottom = Document.Layers[index - 1];
        int alpha = (int)MathF.Round(Math.Clamp(top.Opacity, 0f, 1f) * 255f);

        for (int i = 0; i < bottom.Pixels.Length; i++)
        {
            uint source = top.Pixels[i];
            int sourceAlpha = (int)(source >> 24);
            if (sourceAlpha == 0)
            {
                continue;
            }

            bottom.Pixels[i] = Rgba.Blend(bottom.Pixels[i], source, sourceAlpha * alpha / 255, top.Blend);
        }

        bottom.Opacity = 1f;
        Document.Layers.RemoveAt(index);
        Document.ActiveIndex = index - 1;
        Status = "Merged down";
        Changed();
    }

    /// <summary>Flattens every visible layer into one.</summary>
    public void FlattenImage()
    {
        CommitPreviews();
        PushUndo("Flatten Image");

        uint[] composite = Document.Composite;
        PaintLayer flattened = new("Background", Document.Width, Document.Height, isBackground: true);
        Array.Copy(composite, flattened.Pixels, composite.Length);

        Document.Layers.Clear();
        Document.Layers.Add(flattened);
        Document.ActiveIndex = 0;
        Selection = null;
        Status = "Flattened";
        Changed();
    }

    /// <summary>Removes the active layer, keeping at least one.</summary>
    public void DeleteActiveLayer()
    {
        if (Document.Layers.Count <= 1)
        {
            Status = "Cannot delete the only layer";
            Changed();
            return;
        }

        CommitPreviews();
        PushUndo("Delete Layer");
        int index = Math.Clamp(Document.ActiveIndex, 0, Document.Layers.Count - 1);
        Document.Layers.RemoveAt(index);
        Document.ActiveIndex = Math.Clamp(index - 1, 0, Document.Layers.Count - 1);
        Status = "Deleted layer";
        Changed();
    }

    /// <summary>Selects a layer by index.</summary>
    public void SelectLayer(int index)
    {
        if (index == Document.ActiveIndex)
        {
            return;
        }

        CommitPreviews();
        Document.ActiveIndex = Math.Clamp(index, 0, Document.Layers.Count - 1);
        Status = Document.Active.Name;
        Changed();
    }

    /// <summary>Toggles a layer's visibility.</summary>
    public void ToggleLayer(int index)
    {
        if (index < 0 || index >= Document.Layers.Count)
        {
            return;
        }

        PaintLayer layer = Document.Layers[index];
        layer.Visible = !layer.Visible;
        Changed();
    }

    /// <summary>Toggles a layer's lock.</summary>
    public void ToggleLock(int index)
    {
        if (index < 0 || index >= Document.Layers.Count)
        {
            return;
        }

        Document.Layers[index].Locked = !Document.Layers[index].Locked;
        Changed();
    }

    /// <summary>Sets the active layer's opacity from a 0-1 value.</summary>
    public void SetLayerOpacity(float opacity)
    {
        Document.Active.Opacity = Math.Clamp(opacity, 0f, 1f);
        Changed();
    }

    /// <summary>Sets the active layer's blend mode.</summary>
    public void SetLayerBlend(BlendMode blend)
    {
        Document.Active.Blend = blend;
        Status = blend.ToString();
        Changed();
    }

    // ---- adjustments ------------------------------------------------------

    /// <summary>
    /// Rebuilds the active layer from its snapshot with the current adjustment
    /// values applied. The first call of a session records an undo step.
    /// </summary>
    public void TouchAdjustments()
    {
        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            Status = "Layer is locked";
            return;
        }

        CommitEffect();
        CommitText();

        if (_adjustLayer != layer || _adjustBase is null)
        {
            if (_adjustBase is null)
            {
                PushUndo("Adjustments");
            }

            _adjustLayer = layer;
            _adjustBase = layer.CapturePixels();
        }

        PaintOps.Retone(
            layer,
            _adjustBase,
            Selection,
            AdjustBrightness / 100f,
            AdjustContrast / 100f,
            AdjustHue / 360f,
            1f + (AdjustSaturation / 100f),
            AdjustLightness / 100f);

        Document.MarkDirty();
    }

    /// <summary>Restores the adjustment sliders to neutral and reverts the preview.</summary>
    public void ResetAdjustments()
    {
        if (_adjustLayer is not null && _adjustBase is not null)
        {
            _adjustLayer.Adopt(_adjustBase);
        }

        _adjustBase = null;
        _adjustLayer = null;
        AdjustBrightness = 0f;
        AdjustContrast = 0f;
        AdjustHue = 0f;
        AdjustSaturation = 0f;
        AdjustLightness = 0f;
        Changed();
    }

    /// <summary>Ends the current adjustment session, leaving the pixels as they are.</summary>
    public void CommitAdjustments()
    {
        if (_adjustBase is null)
        {
            return;
        }

        _adjustBase = null;
        _adjustLayer = null;
        AdjustBrightness = 0f;
        AdjustContrast = 0f;
        AdjustHue = 0f;
        AdjustSaturation = 0f;
        AdjustLightness = 0f;
        Document.MarkDirty();
    }

    /// <summary>Flips the active layer, or just the selection, left to right.</summary>
    public void FlipHorizontal() => Flip(horizontal: true);

    /// <summary>Flips the active layer, or just the selection, top to bottom.</summary>
    public void FlipVertical() => Flip(horizontal: false);

    /// <summary>Mirrors pixels inside the selection, or the whole layer.</summary>
    private void Flip(bool horizontal)
    {
        if (Document.Active.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        CommitPreviews();
        PushUndo(horizontal ? "Flip Horizontal" : "Flip Vertical");

        PaintLayer layer = Document.Active;
        uint[] source = layer.CapturePixels();
        int width = layer.Width;
        int left = Selection?.X ?? 0;
        int top = Selection?.Y ?? 0;
        int right = Selection?.Right ?? width;
        int bottom = Selection?.Bottom ?? layer.Height;

        for (int y = top; y < bottom; y++)
        {
            int sourceY = horizontal ? y : top + (bottom - 1) - (y - top);
            for (int x = left; x < right; x++)
            {
                if (Selection is not null && !Selection.Contains(x, y))
                {
                    continue;
                }

                int sourceX = horizontal ? left + (right - 1) - (x - left) : x;
                layer.Put(x, y, source[(sourceY * width) + sourceX]);
            }
        }

        Status = horizontal ? "Flipped horizontally" : "Flipped vertically";
        Changed();
    }

    // ---- effects ----------------------------------------------------------

    /// <summary>Gets the effect currently picked in the Effects panel.</summary>
    public EffectKind Effect { get; private set; }

    /// <summary>Gets the amount slider value for <see cref="Effect"/>.</summary>
    public float EffectAmount { get; private set; }

    /// <summary>Gets whether an effect preview is live on the active layer.</summary>
    public bool HasEffectPreview => _effectBase is not null;

    /// <summary>Gets the last effect that was applied, for a repeat command.</summary>
    public EffectKind LastEffect { get; private set; }

    /// <summary>Gets the amount the last effect was applied with.</summary>
    public float LastEffectAmount { get; private set; }

    /// <summary>Picks an effect and previews it at its default amount.</summary>
    public void SelectEffect(EffectKind effect)
    {
        if (Document.Active.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        // Fold live tone changes into the pixels first, then reuse the existing
        // effect snapshot so switching effects replaces the preview instead of
        // stacking one filter on top of another.
        CommitAdjustments();
        CommitText();
        Effect = effect;
        EffectAmount = Effects.RangeOf(effect).Default;
        TouchEffect();
        Status = "Previewing " + Effects.NameOf(effect);
        Changed();
    }

    /// <summary>Updates the amount of the selected effect and re-previews it.</summary>
    public void SetEffectAmount(float amount)
    {
        if (Effect == EffectKind.None)
        {
            return;
        }

        (float min, float max, _, _) = Effects.RangeOf(Effect);
        EffectAmount = Math.Clamp(amount, min, max);
        TouchEffect();
    }

    /// <summary>Rebuilds the active layer from the effect snapshot at the current amount.</summary>
    public void TouchEffect()
    {
        if (Effect == EffectKind.None)
        {
            return;
        }

        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            Status = "Layer is locked";
            return;
        }

        if (_effectLayer != layer || _effectBase is null)
        {
            if (_effectBase is null)
            {
                PushUndo(Effects.NameOf(Effect));
            }

            _effectLayer = layer;
            _effectBase = layer.CapturePixels();
        }

        EffectOps.Apply(layer, _effectBase, Selection, Effect, EffectAmount);
        Document.MarkDirty();
    }

    /// <summary>Keeps the previewed pixels and ends the preview session.</summary>
    public void ApplyEffect()
    {
        if (_effectBase is null)
        {
            return;
        }

        RememberEffect();
        Status = "Applied " + Effects.NameOf(Effect);
        Changed();
    }

    /// <summary>Restores the snapshot and clears the effect selection.</summary>
    public void ResetEffect()
    {
        if (_effectLayer is not null && _effectBase is not null)
        {
            _effectLayer.Adopt(_effectBase);
        }

        _effectBase = null;
        _effectLayer = null;
        Effect = EffectKind.None;
        Status = "Effect cancelled";
        Changed();
    }

    /// <summary>Ends an effect preview, leaving the pixels and remembering the effect.</summary>
    public void CommitEffect()
    {
        if (_effectBase is null)
        {
            return;
        }

        RememberEffect();
        Document.MarkDirty();
    }

    /// <summary>Applies the last committed effect again in one step.</summary>
    public void RepeatLastEffect()
    {
        if (LastEffect == EffectKind.None)
        {
            return;
        }

        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        CommitPreviews();
        PushUndo(Effects.NameOf(LastEffect));
        EffectOps.Apply(layer, layer.CapturePixels(), Selection, LastEffect, LastEffectAmount);
        Status = "Applied " + Effects.NameOf(LastEffect);
        Changed();
    }

    /// <summary>Records the current effect and clears the preview snapshot.</summary>
    private void RememberEffect()
    {
        LastEffect = Effect;
        LastEffectAmount = EffectAmount;
        _effectBase = null;
        _effectLayer = null;
    }

    /// <summary>Commits any live adjustment, effect or text preview.</summary>
    private void CommitPreviews()
    {
        CommitAdjustments();
        CommitEffect();
        CommitText();
    }

    // ---- text -------------------------------------------------------------

    /// <summary>Gets the text currently being composed by the text tool.</summary>
    public string TextContent { get; private set; } = string.Empty;

    /// <summary>Gets the text size in pixels.</summary>
    public float TextSize { get; private set; } = 64f;

    /// <summary>Gets the index of the selected font family.</summary>
    public int TextFontIndex { get; private set; }

    /// <summary>Gets the selected font family.</summary>
    public TextFont Font => TextFonts.At(TextFontIndex);

    /// <summary>Gets how the line is aligned against its anchor.</summary>
    public TextAlign TextAlignment { get; private set; }

    /// <summary>Gets whether the font picker menu is open.</summary>
    public bool IsFontMenuOpen { get; private set; }

    /// <summary>Gets whether the picker's search field owns the keyboard.</summary>
    public bool IsFontSearchFocused { get; private set; }

    /// <summary>Gets the live search query typed into the font picker.</summary>
    public string FontSearch { get; private set; } = string.Empty;

    /// <summary>Gets how many families match <see cref="FontSearch"/>.</summary>
    public int FontMatchCount => _fontMatches.Length;

    /// <summary>Gets the global font index for one match slot.</summary>
    public int FontMatchAt(int slot) => _fontMatches[Math.Clamp(slot, 0, _fontMatches.Length - 1)];

    /// <summary>
    /// Gets the line the picker previews in each family: the text being composed
    /// when there is any, otherwise the classic sample.
    /// </summary>
    public string FontPreviewText
    {
        get
        {
            string text = TextContent.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (text.Length == 0)
            {
                return "The quick brown fox";
            }

            const int Limit = 26;
            return text.Length <= Limit ? text : text[..Limit].TrimEnd() + "â€¦";
        }
    }

    /// <summary>Gets whether the cdnfonts URL row is open in the options bar.</summary>
    public bool IsFontAddOpen { get; private set; }

    /// <summary>Gets the URL or family name typed into the cdnfonts row.</summary>
    public string FontUrl { get; private set; } = string.Empty;

    /// <summary>Gets whether a remote font is currently being downloaded.</summary>
    public bool IsFontDownloading { get; private set; }

    /// <summary>Gets the status shown next to the add-font controls.</summary>
    public string FontImportMessage { get; private set; } = string.Empty;

    /// <summary>Gets whether <see cref="FontImportMessage"/> reports a failure.</summary>
    public bool FontImportFailed { get; private set; }

    /// <summary>Gets a counter bumped when the URL field should be cleared.</summary>
    public int FontAddGeneration { get; private set; }

    /// <summary>Gets whether the text is bold.</summary>
    public bool TextBold { get; private set; }

    /// <summary>Gets whether the text is italic.</summary>
    public bool TextItalic { get; private set; }

    /// <summary>Gets the clockwise rotation of the text in degrees.</summary>
    public float TextRotation { get; private set; }

    /// <summary>Gets whether the text is drawn with an outline.</summary>
    public bool TextOutline { get; private set; }

    /// <summary>Gets the outline thickness in document pixels.</summary>
    public float TextOutlineWidth { get; private set; } = 3f;

    /// <summary>Gets the outline colour.</summary>
    public uint TextOutlineColor { get; private set; } = Rgba.FromRgb(0x10, 0x10, 0x14);

    /// <summary>Gets whether the text is drawn with a shadow or glow.</summary>
    public bool TextShadow { get; private set; }

    /// <summary>Gets the horizontal shadow offset in pixels.</summary>
    public int TextShadowX { get; private set; } = 4;

    /// <summary>Gets the vertical shadow offset in pixels.</summary>
    public int TextShadowY { get; private set; } = 4;

    /// <summary>Gets the shadow softness in pixels.</summary>
    public float TextShadowBlur { get; private set; } = 6f;

    /// <summary>Gets the shadow opacity in the 0-100 range.</summary>
    public int TextShadowOpacity { get; private set; } = 65;

    /// <summary>Gets the shadow colour.</summary>
    public uint TextShadowColor { get; private set; } = Rgba.FromRgb(0, 0, 0);

    /// <summary>Gets the last preset applied, or <see cref="TextPreset.Custom"/>.</summary>
    public TextPreset TextPreset { get; private set; } = TextPreset.None;

    /// <summary>Gets whether the text effects popup is open.</summary>
    public bool IsTextFxOpen { get; private set; }

    /// <summary>Gets which text colour the colour panel edits.</summary>
    public TextColorTarget ColorTarget { get; private set; }

    /// <summary>Gets the colour the colour panel is currently editing.</summary>
    public uint ActiveColor => ColorTarget switch
    {
        TextColorTarget.Outline => TextOutlineColor,
        TextColorTarget.Shadow => TextShadowColor,
        _ => Foreground,
    };

    /// <summary>Gets a caption for <see cref="ColorTarget"/>, or an empty string for the fill.</summary>
    public string ColorTargetLabel => ColorTarget switch
    {
        TextColorTarget.Outline => "Outline colour",
        TextColorTarget.Shadow => "Shadow colour",
        _ => string.Empty,
    };

    /// <summary>Gets the outline and shadow settings passed to the rasteriser.</summary>
    public TextEffectStyle TextEffectStyle => new(
        TextOutline,
        TextOutlineWidth,
        TextOutlineColor,
        TextShadow,
        TextShadowX,
        TextShadowY,
        TextShadowBlur,
        TextShadowOpacity,
        TextShadowColor);

    /// <summary>Gets whether a text preview is live on the active layer.</summary>
    public bool HasTextPreview => _textBase is not null;

    /// <summary>Gets whether a text field has focus, so shortcuts stand down.</summary>
    public bool IsTyping { get; private set; }

    /// <summary>Gets the baseline X of the text being composed.</summary>
    public int TextX { get; private set; } = 24;

    /// <summary>Gets the baseline Y of the text being composed.</summary>
    public int TextY { get; private set; }

    /// <summary>Gets a counter bumped when the text field should start empty.</summary>
    public int TextGeneration { get; private set; }

    /// <summary>Gets the centre of the text bitmap in document pixels.</summary>
    public (float X, float Y) TextCenter => (
        TextX + _textOriginX + (_textBitmapWidth * 0.5f),
        TextY + _textOriginY + (_textBitmapHeight * 0.5f));

    /// <summary>Gets the half extents of the text bitmap before rotation.</summary>
    public (float X, float Y) TextExtent => (_textBitmapWidth * 0.5f, _textBitmapHeight * 0.5f);

    /// <summary>Marks the start of keyboard text entry.</summary>
    public void BeginTyping()
    {
        IsTyping = true;
        if (IsFontMenuOpen)
        {
            CloseFontMenu();
        }
    }

    /// <summary>Marks the end of keyboard text entry.</summary>
    public void EndTyping() => IsTyping = false;

    /// <summary>Handles a press with the text tool: transform box, or a new anchor.</summary>
    public void TextPointerDown(int x, int y)
    {
        if (HasTextPreview && _textArtwork is not null)
        {
            TextHandle handle = HitTextHandle(x, y);
            if (handle != TextHandle.None)
            {
                BeginTextDrag(handle, x, y);
                return;
            }
        }

        BeginText(x, y);
    }

    /// <summary>Extends a text move, scale or rotate drag.</summary>
    public void TextPointerMove(int x, int y)
    {
        switch (_textHandle)
        {
            case TextHandle.Move:
                TextX = _textDragAnchorX + (x - _textDragX);
                TextY = _textDragAnchorY + (y - _textDragY);
                Status = "Move text";
                TouchText();
                break;

            case TextHandle.Rotate:
                float angle = MathF.Atan2(y - _textDragCenterY, x - _textDragCenterX) * 180f / MathF.PI;
                TextRotation = _textDragRotation + (angle - _textDragAngle);
                Status = $"Rotate {TextRotation:0}Ã‚Â°";
                TouchText();
                break;

            case TextHandle.None:
                break;

            default:
                float dx = x - _textDragCenterX;
                float dy = y - _textDragCenterY;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                float scale = distance / MathF.Max(1f, _textDragDistance);
                ScaleTextAbout(_textDragSize * scale, _textDragCenterX, _textDragCenterY);
                break;
        }
    }

    /// <summary>Finishes a text transform drag.</summary>
    public void TextPointerUp()
    {
        if (_textHandle == TextHandle.None)
        {
            return;
        }

        _textHandle = TextHandle.None;
        Changed();
    }

    /// <summary>Places the text baseline at a document position and previews it.</summary>
    public void BeginText(int x, int y)
    {
        TextX = x;
        TextY = y;
        TextRotation = 0f;
        TouchText();
        Status = $"Text at {x}, {y}";
        Changed();
    }

    /// <summary>Replaces the text being composed and previews it.</summary>
    public void SetText(string text)
    {
        TextContent = text;
        InvalidateText();
        TouchText();
    }

    /// <summary>Sets the text size and previews it.</summary>
    public void SetTextSize(float size)
    {
        TextSize = Math.Clamp(size, 8f, 400f);
        InvalidateText();
        TouchText();
    }

    /// <summary>Steps the font family and previews it.</summary>
    public void CycleTextFont(int direction)
    {
        int count = TextFonts.All.Length;
        TextFontIndex = (((TextFontIndex + direction) % count) + count) % count;
        InvalidateText();
        TouchText();
        Changed();
    }

    /// <summary>Selects a font family by index and previews it.</summary>
    public void SelectTextFont(int index)
    {
        TextFontIndex = Math.Clamp(index, 0, TextFonts.All.Length - 1);
        InvalidateText();
        TouchText();
        Changed();
    }

    /// <summary>Applies a family chosen from the picker, closes it and reports the choice.</summary>
    public void ApplyFont(int index)
    {
        SelectTextFont(index);
        IsFontMenuOpen = false;
        IsFontSearchFocused = false;
        Status = Font.Name;
        Changed();
    }

    /// <summary>Opens or closes the font picker menu.</summary>
    public void ToggleFontMenu()
    {
        IsFontMenuOpen = !IsFontMenuOpen;
        IsFontSearchFocused = false;
        if (IsFontMenuOpen)
        {
            IsFontAddOpen = false;
            IsTextFxOpen = false;
            FontSearch = string.Empty;
            RebuildFontMatches();
        }

        Status = IsFontMenuOpen ? "Choose a font" : "Ready";
        Changed();
    }

    /// <summary>Replaces the search text from the picker's input field.</summary>
    public void SetFontSearch(string text)
    {
        string value = text ?? string.Empty;
        if (FontSearch == value)
        {
            return;
        }

        FontSearch = value;
        RebuildFontMatches();
        Status = value.Length == 0 ? "Choose a font" : "Search: " + value;
        Changed();
    }

    /// <summary>Records whether the picker's search field owns the keyboard.</summary>
    public void SetFontSearchFocused(bool focused) => IsFontSearchFocused = focused;

    /// <summary>Adds typed text to the font search and refreshes the matches.</summary>
    public void AppendFontSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        FontSearch += text.ToLowerInvariant();
        RebuildFontMatches();
        Status = "Search: " + FontSearch;
        Changed();
    }

    /// <summary>Removes the last character from the font search.</summary>
    public void BackspaceFontSearch()
    {
        if (FontSearch.Length == 0)
        {
            return;
        }

        FontSearch = FontSearch[..^1];
        RebuildFontMatches();
        Status = FontSearch.Length == 0 ? "Choose a font" : "Search: " + FontSearch;
        Changed();
    }

    /// <summary>Handles a key press while the font picker is open.</summary>
    /// <remarks>
    /// When the search field has focus it performs its own text editing, so only
    /// navigation and commit keys are handled here. Without focus, printable
    /// keys still drive type-to-search.
    /// </remarks>
    public void FontMenuKey(string key)
    {
        if (key is "Escape")
        {
            CloseFontMenu();
            return;
        }

        if (!IsFontSearchFocused && key is "Backspace" or "Back")
        {
            BackspaceFontSearch();
            return;
        }

        if (!IsFontSearchFocused && key == "Space")
        {
            AppendFontSearch(" ");
            return;
        }

        if (!IsFontSearchFocused && key.Length == 1 && !char.IsControl(key[0]))
        {
            AppendFontSearch(key);
            return;
        }

        if (key is "Enter" or "Return" or "NumpadEnter")
        {
            if (FontMatchCount > 0)
            {
                int highlighted = Array.IndexOf(_fontMatches, TextFontIndex);
                ApplyFont(highlighted >= 0 ? TextFontIndex : FontMatchAt(0));
            }

            return;
        }

        if (key is "Up" or "ArrowUp" or "Down" or "ArrowDown")
        {
            int direction = key is "Up" or "ArrowUp" ? -1 : 1;
            if (FontMatchCount == 0)
            {
                return;
            }

            int current = Array.IndexOf(_fontMatches, TextFontIndex);
            int next = current < 0
                ? (direction > 0 ? 0 : FontMatchCount - 1)
                : Math.Clamp(current + direction, 0, FontMatchCount - 1);
            SelectTextFont(FontMatchAt(next));
            Status = Font.Name;
        }
    }

    /// <summary>Rebuilds the match list for the current search text.</summary>
    private void RebuildFontMatches()
    {
        string query = FontSearch.Trim();
        if (query.Length == 0)
        {
            _fontMatches = new int[TextFonts.All.Length];
            for (int index = 0; index < _fontMatches.Length; index++)
            {
                _fontMatches[index] = index;
            }
        }
        else
        {
            List<int> matches = [];
            for (int index = 0; index < TextFonts.All.Length; index++)
            {
                if (TextFonts.All[index].Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(index);
                }
            }

            _fontMatches = [.. matches];
        }
    }

    /// <summary>Closes the font picker menu.</summary>
    public void CloseFontMenu()
    {
        if (!IsFontMenuOpen)
        {
            return;
        }

        IsFontMenuOpen = false;
        IsFontSearchFocused = false;
        Status = NameOf(Tool) + " tool";
        Changed();
    }

    /// <summary>Opens the cdnfonts URL row beside the font picker.</summary>
    public void OpenFontAdd()
    {
        if (IsFontDownloading)
        {
            return;
        }

        IsFontMenuOpen = false;
        IsFontSearchFocused = false;
        IsFontAddOpen = true;
        IsTextFxOpen = false;
        FontUrl = string.Empty;
        FontImportMessage = string.Empty;
        FontImportFailed = false;
        FontAddGeneration++;
        Status = "Add a font from cdnfonts";
        Changed();
    }

    /// <summary>Opens or closes the cdnfonts URL row.</summary>
    public void ToggleFontAdd()
    {
        if (IsFontAddOpen)
        {
            CancelFontAdd();
            return;
        }

        OpenFontAdd();
    }

    /// <summary>Closes the cdnfonts URL row without downloading anything.</summary>
    public void CancelFontAdd()
    {
        if (IsFontDownloading)
        {
            return;
        }

        IsFontAddOpen = false;
        FontUrl = string.Empty;
        FontImportMessage = string.Empty;
        FontImportFailed = false;
        FontAddGeneration++;
        Status = "Ready";
        Changed();
    }

    /// <summary>Records the URL or family name typed into the add-font row.</summary>
    public void SetFontUrl(string value)
    {
        FontUrl = value;
        if (FontImportFailed)
        {
            FontImportFailed = false;
            FontImportMessage = string.Empty;
        }
    }

    /// <summary>Marks the start of a download and returns false when it cannot start.</summary>
    public bool BeginFontImport()
    {
        if (IsFontDownloading)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(FontUrl))
        {
            FailFontImport("Enter a cdnfonts URL or font name.");
            return false;
        }

        IsFontDownloading = true;
        FontImportFailed = false;
        FontImportMessage = "Downloadingâ€¦";
        Status = "Downloading fontâ€¦";
        Changed();
        return true;
    }

    /// <summary>Adds a downloaded family, selects it, and closes the add-font row.</summary>
    public void CompleteFontImport(TextFont font)
    {
        int index = TextFonts.Add(font);
        IsFontDownloading = false;
        IsFontAddOpen = false;
        FontUrl = string.Empty;
        FontImportFailed = false;
        FontImportMessage = "Added " + font.Name;
        FontAddGeneration++;
        SelectTextFont(index);
        Status = "Added font " + font.Name;
        Changed();
    }

    /// <summary>Reports a failed download and leaves the row open for a retry.</summary>
    public void FailFontImport(string message)
    {
        IsFontDownloading = false;
        FontImportFailed = true;
        FontImportMessage = Shorten(message);
        Status = "Font import failed";
        Changed();
    }

    /// <summary>Sets the line alignment and previews it.</summary>
    public void SetTextAlign(TextAlign align)
    {
        if (TextAlignment == align)
        {
            return;
        }

        TextAlignment = align;
        InvalidateText();
        TouchText();
        Changed();
    }

    /// <summary>Toggles bold text and previews it.</summary>
    public void ToggleTextBold()
    {
        TextBold = !TextBold;
        InvalidateText();
        TouchText();
        Changed();
    }

    /// <summary>Toggles italic text and previews it.</summary>
    public void ToggleTextItalic()
    {
        TextItalic = !TextItalic;
        InvalidateText();
        TouchText();
        Changed();
    }

    /// <summary>Opens or closes the text effects popup.</summary>
    public void ToggleTextFx()
    {
        IsTextFxOpen = !IsTextFxOpen;
        if (IsTextFxOpen)
        {
            IsFontMenuOpen = false;
            IsFontSearchFocused = false;
            IsFontAddOpen = false;
        }

        Status = IsTextFxOpen ? "Text effects" : "Ready";
        Changed();
    }

    /// <summary>Closes the text effects popup.</summary>
    public void CloseTextFx()
    {
        if (!IsTextFxOpen)
        {
            return;
        }

        IsTextFxOpen = false;
        Changed();
    }

    /// <summary>Turns the outline on or off and previews it.</summary>
    public void SetTextOutline(bool on)
    {
        TextOutline = on;
        TextPreset = TextPreset.Custom;
        TextEffectsChanged();
    }

    /// <summary>Sets the outline thickness and previews it.</summary>
    public void SetTextOutlineWidth(float width)
    {
        TextOutlineWidth = Math.Clamp(width, 0.5f, 24f);
        TextPreset = TextPreset.Custom;
        TextEffectsChanged();
    }

    /// <summary>Turns the shadow on or off and previews it.</summary>
    public void SetTextShadow(bool on)
    {
        TextShadow = on;
        TextPreset = TextPreset.Custom;
        TextEffectsChanged();
    }

    /// <summary>Sets the shadow offset and previews it.</summary>
    public void SetTextShadowOffset(int x, int y)
    {
        TextShadowX = Math.Clamp(x, -48, 48);
        TextShadowY = Math.Clamp(y, -48, 48);
        TextPreset = TextPreset.Custom;
        TextEffectsChanged();
    }

    /// <summary>Sets the shadow softness and previews it.</summary>
    public void SetTextShadowBlur(float blur)
    {
        TextShadowBlur = Math.Clamp(blur, 0f, 32f);
        TextPreset = TextPreset.Custom;
        TextEffectsChanged();
    }

    /// <summary>Sets the shadow opacity and previews it.</summary>
    public void SetTextShadowOpacity(int opacity)
    {
        TextShadowOpacity = Math.Clamp(opacity, 5, 100);
        TextPreset = TextPreset.Custom;
        TextEffectsChanged();
    }

    /// <summary>Applies one of the ready-made effect combinations.</summary>
    public void ApplyTextPreset(TextPreset preset)
    {
        TextPreset = preset;
        switch (preset)
        {
            case TextPreset.None:
                TextOutline = false;
                TextShadow = false;
                break;

            case TextPreset.Outline:
                TextOutline = true;
                TextOutlineWidth = 3f;
                TextShadow = false;
                break;

            case TextPreset.Shadow:
                TextOutline = false;
                TextShadow = true;
                TextShadowX = 4;
                TextShadowY = 5;
                TextShadowBlur = 6f;
                TextShadowOpacity = 65;
                break;

            case TextPreset.Glow:
                TextOutline = false;
                TextShadow = true;
                TextShadowX = 0;
                TextShadowY = 0;
                TextShadowBlur = 16f;
                TextShadowOpacity = 90;
                TextShadowColor = Rgba.FromRgb(0x7F, 0xE7, 0xFF);
                break;

            case TextPreset.Neon:
                TextOutline = true;
                TextOutlineWidth = 1f;
                TextOutlineColor = Rgba.FromRgb(0x0B, 0x0B, 0x0F);
                TextShadow = true;
                TextShadowX = 0;
                TextShadowY = 0;
                TextShadowBlur = 18f;
                TextShadowOpacity = 100;
                TextShadowColor = Rgba.FromRgb(0x22, 0xE6, 0xFF);
                break;

            case TextPreset.Retro:
                TextOutline = true;
                TextOutlineWidth = 3f;
                TextOutlineColor = Rgba.FromRgb(0x12, 0x12, 0x1A);
                TextShadow = true;
                TextShadowX = 7;
                TextShadowY = 7;
                TextShadowBlur = 0f;
                TextShadowOpacity = 100;
                TextShadowColor = Rgba.FromRgb(0xFF, 0x4F, 0xA3);
                break;

            default:
                break;
        }

        Status = preset == TextPreset.Custom ? "Text effects" : preset.ToString() + " text";
        TextEffectsChanged();
    }

    /// <summary>Re-rasterises and redraws the text after an effect setting changed.</summary>
    private void TextEffectsChanged()
    {
        InvalidateText();
        TouchText();
        Changed();
    }

    /// <summary>Redraws the text from the snapshot with its current transform.</summary>
    public void TouchText()
    {
        if (TextContent.Length == 0 && _textBase is null)
        {
            return;
        }

        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            Status = "Layer is locked";
            return;
        }

        CommitAdjustments();
        CommitEffect();

        if (_textLayer != layer || _textBase is null)
        {
            if (_textBase is null)
            {
                PushUndo("Text");
            }

            _textLayer = layer;
            _textBase = layer.CapturePixels();
        }

        if (TextY <= 0)
        {
            TextX = Math.Max(8, TextX);
            TextY = Math.Max(8, Document.Height / 2);
        }

        EnsureTextBitmap();

        if (TextContent.Length == 0)
        {
            Array.Copy(_textBase, layer.Pixels, _textBase.Length);
        }
        else if (_textArtwork is null || _textArtwork.Fill.Length == 0 || !_textFontAvailable)
        {
            Array.Copy(_textBase, layer.Pixels, _textBase.Length);
            Status = "No text font found";
        }
        else
        {
            TextArtwork art = _textArtwork;
            (float centerX, float centerY) = TextCenter;
            Array.Copy(_textBase, layer.Pixels, _textBase.Length);

            if (art.Shadow is not null)
            {
                TextRasterizer.Overlay(
                    layer,
                    Selection,
                    art.Shadow,
                    art.Width,
                    art.Height,
                    centerX,
                    centerY,
                    TextRotation,
                    TextEffectStyle.ShadowTint);
            }

            if (art.Outline is not null)
            {
                TextRasterizer.Overlay(
                    layer,
                    Selection,
                    art.Outline,
                    art.Width,
                    art.Height,
                    centerX,
                    centerY,
                    TextRotation,
                    TextOutlineColor);
            }

            TextRasterizer.Overlay(
                layer,
                Selection,
                art.Fill,
                art.Width,
                art.Height,
                centerX,
                centerY,
                TextRotation,
                Foreground);
        }

        Document.MarkDirty();
    }

    /// <summary>Bakes the previewed text into the layer.</summary>
    public void ApplyText()
    {
        if (_textBase is null)
        {
            return;
        }

        _textBase = null;
        _textLayer = null;
        _textHandle = TextHandle.None;
        TextContent = string.Empty;
        TextRotation = 0f;
        _textArtwork = null;
        _textRenderKey = string.Empty;
        TextGeneration++;
        Status = "Text placed";
        Changed();
    }

    /// <summary>Discards the previewed text.</summary>
    public void CancelText()
    {
        if (_textLayer is not null && _textBase is not null)
        {
            _textLayer.Adopt(_textBase);
        }

        _textBase = null;
        _textLayer = null;
        _textHandle = TextHandle.None;
        TextContent = string.Empty;
        TextRotation = 0f;
        _textArtwork = null;
        _textRenderKey = string.Empty;
        TextGeneration++;
        Status = "Text cancelled";
        Changed();
    }

    /// <summary>Ends a text preview, leaving the pixels in place.</summary>
    public void CommitText()
    {
        if (_textBase is null)
        {
            return;
        }

        _textBase = null;
        _textLayer = null;
        _textHandle = TextHandle.None;
        TextContent = string.Empty;
        TextRotation = 0f;
        _textArtwork = null;
        _textRenderKey = string.Empty;
        TextGeneration++;
        Document.MarkDirty();
    }

    /// <summary>Forces the next text redraw to re-rasterise the glyphs.</summary>
    private void InvalidateText() => _textRenderKey = string.Empty;

    /// <summary>Rasterises the current text when its style, content or effects changed.</summary>
    private void EnsureTextBitmap()
    {
        TextEffectStyle style = TextEffectStyle;
        string key =
            $"{TextFontIndex}|{TextContent}|{TextSize:0.###}|{TextBold}|{TextItalic}|{TextAlignment}"
            + $"|{style.Outline}|{style.OutlineWidth:0.###}|{style.OutlineColor}"
            + $"|{style.Shadow}|{style.ShadowOffsetX}|{style.ShadowOffsetY}"
            + $"|{style.ShadowBlur:0.###}|{style.ShadowOpacity}|{style.ShadowColor}";

        if (key == _textRenderKey && _textArtwork is not null)
        {
            return;
        }

        bool ok = TextRasterizer.RenderStyled(
            Font,
            TextContent,
            TextSize,
            TextBold,
            TextItalic,
            TextAlignment,
            style,
            out TextArtwork? artwork);

        _textFontAvailable = ok;
        _textArtwork = artwork is not null && artwork.Fill.Length > 0 ? artwork : null;
        _textBitmapWidth = artwork?.Width ?? 0;
        _textBitmapHeight = artwork?.Height ?? 0;
        _textOriginX = artwork?.OriginX ?? 0;
        _textOriginY = artwork?.OriginY ?? 0;
        _textRenderKey = key;
    }

    /// <summary>Scales the text while keeping a document point at the bitmap centre.</summary>
    private void ScaleTextAbout(float size, float centerX, float centerY)
    {
        TextSize = Math.Clamp(size, 8f, 400f);
        InvalidateText();
        EnsureTextBitmap();

        (float currentX, float currentY) = TextCenter;
        TextX += (int)MathF.Round(centerX - currentX);
        TextY += (int)MathF.Round(centerY - currentY);
        Status = $"Text {MathF.Round(TextSize)} px";
        TouchText();
    }

    /// <summary>Returns the handle under a document position on the text box.</summary>
    private TextHandle HitTextHandle(int x, int y)
    {
        (float centerX, float centerY) = TextCenter;
        (float halfWidth, float halfHeight) = TextExtent;

        float radians = -TextRotation * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float dx = x - centerX;
        float dy = y - centerY;
        float localX = (dx * cos) - (dy * sin);
        float localY = (dx * sin) + (dy * cos);

        float zoom = MathF.Max(Zoom, 0.05f);
        float radius = 7f / zoom;
        float gap = 18f / zoom;

        if (MathF.Abs(localX) <= radius && MathF.Abs(localY + halfHeight + gap) <= radius)
        {
            return TextHandle.Rotate;
        }

        if (NearLocal(localX, localY, -halfWidth, -halfHeight, radius))
        {
            return TextHandle.TopLeft;
        }

        if (NearLocal(localX, localY, halfWidth, -halfHeight, radius))
        {
            return TextHandle.TopRight;
        }

        if (NearLocal(localX, localY, halfWidth, halfHeight, radius))
        {
            return TextHandle.BottomRight;
        }

        if (NearLocal(localX, localY, -halfWidth, halfHeight, radius))
        {
            return TextHandle.BottomLeft;
        }

        if (MathF.Abs(localX) <= halfWidth && MathF.Abs(localY) <= halfHeight)
        {
            return TextHandle.Move;
        }

        return TextHandle.None;
    }

    /// <summary>Tests a local point against a rotated handle position.</summary>
    private static bool NearLocal(float x, float y, float targetX, float targetY, float radius) =>
        MathF.Abs(x - targetX) <= radius && MathF.Abs(y - targetY) <= radius;

    /// <summary>Records the state a text transform drag started from.</summary>
    private void BeginTextDrag(TextHandle handle, int x, int y)
    {
        _textHandle = handle;
        _textDragX = x;
        _textDragY = y;
        _textDragAnchorX = TextX;
        _textDragAnchorY = TextY;
        _textDragSize = TextSize;
        _textDragRotation = TextRotation;
        _textDragCenterX = TextCenter.X;
        _textDragCenterY = TextCenter.Y;

        float dx = x - _textDragCenterX;
        float dy = y - _textDragCenterY;
        _textDragAngle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        _textDragDistance = MathF.Max(1f, MathF.Sqrt((dx * dx) + (dy * dy)));
    }

    /// <summary>Redraws a live text preview when the paint colour changes.</summary>
    private void RefreshTextPreview()
    {
        if (_textBase is not null)
        {
            TouchText();
        }
    }

    /// <summary>Inverts the active layer inside the selection.</summary>
    public void Invert()
    {
        if (Document.Active.Locked)
        {
            return;
        }

        CommitPreviews();
        PushUndo("Invert");
        PaintOps.Invert(Document.Active, Selection);
        Status = "Inverted";
        Changed();
    }

    /// <summary>Removes colour from the active layer inside the selection.</summary>
    public void Desaturate()
    {
        if (Document.Active.Locked)
        {
            return;
        }

        CommitPreviews();
        PushUndo("Desaturate");
        PaintOps.Desaturate(Document.Active, Selection);
        Status = "Desaturated";
        Changed();
    }

    // ---- histogram --------------------------------------------------------

    /// <summary>Gets 64 luminance buckets of the composited document.</summary>
    public int[] Histogram
    {
        get
        {
            Document.BuildHistogram(_histogram);
            return _histogram;
        }
    }

    // ---- history ----------------------------------------------------------

    /// <summary>Undoes the newest step.</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        CommitPreviews();
        DocumentSnapshot snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(DocumentSnapshot.Capture(Document, Selection, snapshot.Name));
        Selection = snapshot.Restore(Document);
        Status = "Undo " + snapshot.Name;
        Changed();
    }

    /// <summary>Redoes the newest undone step.</summary>
    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        CommitPreviews();
        DocumentSnapshot snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(DocumentSnapshot.Capture(Document, Selection, snapshot.Name));
        Selection = snapshot.Restore(Document);
        Status = "Redo " + snapshot.Name;
        Changed();
    }

    /// <summary>Travels to a history step by undoing or redoing as needed.</summary>
    public void GoToHistory(int index)
    {
        int target = Math.Clamp(index, 0, _undo.Count + _redo.Count);
        while (_undo.Count > target)
        {
            Undo();
        }

        while (_undo.Count < target)
        {
            Redo();
        }
    }

    // ---- documents --------------------------------------------------------

    /// <summary>Opens a blank document in a new tab and switches to it.</summary>
    public void NewDocument(int width, int height)
    {
        _untitledCount++;
        string name = "Untitled-" + _untitledCount;
        AddDocument(DemoArt.BuildBlank(width, height), name, "New document");
    }

    /// <summary>Opens the sample artwork in a new tab and switches to it.</summary>
    public void LoadDemo()
    {
        AddDocument(DemoArt.BuildDocument(), FreeName("Moonlight"), "Sample artwork");
    }

    /// <summary>Switches the tab strip to another document.</summary>
    public void SwitchDocument(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _activeTab)
        {
            return;
        }

        CommitPreviews();
        FinishInteraction();
        SaveActiveTab();
        _activeTab = index;
        LoadActiveTab();
        Status = DocumentName;
        Changed();
    }

    /// <summary>Closes a document tab. The last document cannot be closed.</summary>
    public void CloseDocument(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count)
        {
            return;
        }

        CommitPreviews();
        FinishInteraction();

        if (index == _activeTab)
        {
            _tabs.RemoveAt(index);
            _activeTab = Math.Min(index, _tabs.Count - 1);
            LoadActiveTab();
        }
        else
        {
            SaveActiveTab();
            _tabs.RemoveAt(index);
            if (index < _activeTab)
            {
                _activeTab--;
            }
        }

        Status = "Closed document";
        Changed();
    }

    /// <summary>Closes the document shown in the active tab.</summary>
    public void CloseActiveDocument() => CloseDocument(_activeTab);

    /// <summary>Activates the next tab, wrapping around at the end.</summary>
    public void NextDocument()
    {
        if (_tabs.Count > 1)
        {
            SwitchDocument((_activeTab + 1) % _tabs.Count);
        }
    }

    /// <summary>Activates the previous tab, wrapping around at the start.</summary>
    public void PreviousDocument()
    {
        if (_tabs.Count > 1)
        {
            SwitchDocument((_activeTab - 1 + _tabs.Count) % _tabs.Count);
        }
    }

    /// <summary>Opens a prepared document in a new tab and makes it active.</summary>
    private void AddDocument(PaintDocument document, string name, string status)
    {
        CommitPreviews();
        FinishInteraction();
        SaveActiveTab();

        _tabs.Add(new DocumentTab(document, name));
        _activeTab = _tabs.Count - 1;
        LoadActiveTab();
        Status = status;
        Changed();
    }

    /// <summary>Copies the active document's per-tab state back onto its tab.</summary>
    private void SaveActiveTab()
    {
        DocumentTab tab = _tabs[_activeTab];
        tab.Document = Document;
        tab.Name = DocumentName;
        tab.Undo = _undo;
        tab.Redo = _redo;
        tab.Selection = Selection;
        tab.Zoom = Zoom;
        tab.CursorX = CursorX;
        tab.CursorY = CursorY;
        tab.Brightness = AdjustBrightness;
        tab.Contrast = AdjustContrast;
        tab.Hue = AdjustHue;
        tab.Saturation = AdjustSaturation;
        tab.Lightness = AdjustLightness;
    }

    /// <summary>Loads the active tab's state into the session.</summary>
    private void LoadActiveTab()
    {
        DocumentTab tab = _tabs[_activeTab];
        Document = tab.Document;
        DocumentName = tab.Name;
        _undo = tab.Undo;
        _redo = tab.Redo;
        Selection = tab.Selection;
        Zoom = tab.Zoom;
        CursorX = tab.CursorX;
        CursorY = tab.CursorY;
        AdjustBrightness = tab.Brightness;
        AdjustContrast = tab.Contrast;
        AdjustHue = tab.Hue;
        AdjustSaturation = tab.Saturation;
        AdjustLightness = tab.Lightness;
    }

    /// <summary>Returns a name that no open tab is already using.</summary>
    private string FreeName(string name)
    {
        if (_tabs.TrueForAll(tab => tab.Name != name))
        {
            return name;
        }

        int suffix = 2;
        while (_tabs.Exists(tab => tab.Name == name + " " + suffix))
        {
            suffix++;
        }

        return name + " " + suffix;
    }

    /// <summary>Writes the flattened document as a PNG next to the user's pictures.</summary>
    public void SavePng() => SaveImage(ImageFormat.Png);

    /// <summary>Writes the flattened document in the requested format.</summary>
    public void SaveImage(ImageFormat format)
    {
        CommitPreviews();

        try
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(folder))
            {
                folder = Environment.CurrentDirectory;
            }

            string name = DocumentName.Replace(' ', '-') + "-" + DateTime.Now.ToString("HHmmss") + ImageExporter.Extension(format);
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, ImageExporter.Encode(Document.Composite, Document.Width, Document.Height, format));
            Status = "Saved " + path;
        }
        catch (Exception error)
        {
            Status = "Save failed: " + error.Message;
        }

        Changed();
    }

    // ---- internals --------------------------------------------------------

    /// <summary>Opacity as 0-255.</summary>
    private int Alpha => (int)MathF.Round(Math.Clamp(Opacity, 0f, 1f) * 255f);

    /// <summary>Marks the document dirty and re-renders the components.</summary>
    private void Changed()
    {
        Document.MarkDirty();
        Refresh?.Invoke();
    }

    /// <summary>Clears any half-finished stroke, preview, move or adjustment.</summary>
    private void FinishInteraction()
    {
        _stroking = false;
        _previewing = false;
        _moving = false;
        _movingSelection = false;
        _resizingSelection = false;
        _moveChanged = false;
        _moveStart = null;
        _movePixels = null;
        _strokeLayer = null;
        _strokeBase = null;
        _strokeMask = null;
        _adjustBase = null;
        _adjustLayer = null;
    }

    /// <summary>Converts canvas-local CSS pixels to document pixels.</summary>
    private (float X, float Y) ToDocument(float localX, float localY)
    {
        float zoom = MathF.Max(Zoom, 0.01f);
        return (localX / zoom, localY / zoom);
    }

    /// <summary>Returns whether a document pixel is addressable.</summary>
    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Document.Width && y < Document.Height;

    /// <summary>Formats a colour as an uppercase hex string.</summary>
    private static string Hex(uint color) => $"#{Rgba.R(color):X2}{Rgba.G(color):X2}{Rgba.B(color):X2}";

    /// <summary>Trims a message to something the options bar can show inline.</summary>
    private static string Shorten(string message)
    {
        string single = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 72 ? single : single[..69] + "â€¦";
    }

    /// <summary>Snaps a zoom factor onto the next stop in a direction.</summary>
    private static float StepZoom(float zoom, int direction)
    {
        int index = 0;
        float best = MathF.Abs(ZoomLevels[0] - zoom);
        for (int i = 1; i < ZoomLevels.Length; i++)
        {
            float distance = MathF.Abs(ZoomLevels[i] - zoom);
            if (distance < best)
            {
                best = distance;
                index = i;
            }
        }

        return ZoomLevels[Math.Clamp(index + direction, 0, ZoomLevels.Length - 1)];
    }

    /// <summary>Captures the current document onto the undo stack.</summary>
    private void PushUndo(string name)
    {
        _undo.Add(DocumentSnapshot.Capture(Document, Selection, name));
        if (_undo.Count > UndoDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    /// <summary>Redraws the pending shape or gradient from the snapshot.</summary>
    private void PreviewShape(float currentX, float currentY)
    {
        if (_strokeLayer is null || _strokeBase is null)
        {
            return;
        }

        // Reset the whole layer first: the previous preview may cover pixels the new
        // one no longer touches, and those have to go back to their snapshot values.
        Array.Copy(_strokeBase, _strokeLayer.Pixels, _strokeBase.Length);

        float targetX = Math.Clamp(currentX, 0f, Document.Width);
        float targetY = Math.Clamp(currentY, 0f, Document.Height);
        int alpha = Alpha;

        switch (Tool)
        {
            case PaintTool.Gradient:
                PaintOps.LinearGradient(_strokeLayer, _strokeBase, _startX, _startY, targetX, targetY, Foreground, Background, alpha, Selection);
                break;

            case PaintTool.Ellipse:
                PaintOps.Shape(_strokeLayer, _strokeBase, _startX, _startY, targetX, targetY, true, FillShapes, StrokeWidth, Foreground, alpha, Selection);
                break;

            case PaintTool.Rectangle:
                PaintOps.Shape(_strokeLayer, _strokeBase, _startX, _startY, targetX, targetY, false, FillShapes, StrokeWidth, Foreground, alpha, Selection);
                break;

            case PaintTool.Line:
                if (_strokeMask is null || _strokeMask.Length != _strokeLayer.Pixels.Length)
                {
                    _strokeMask = new byte[_strokeLayer.Width * _strokeLayer.Height];
                }
                else
                {
                    Array.Clear(_strokeMask);
                }

                _maskX0 = int.MaxValue;
                _maskY0 = int.MaxValue;
                _maskX1 = -1;
                _maskY1 = -1;
                StampLine(_startX, _startY, targetX, targetY);
                ApplyMask();
                break;

            default:
                return;
        }

        Document.MarkDirty();
    }

    /// <summary>Starts a brush or eraser stroke, snapshotting the target layer.</summary>
    private void BeginStroke(int x, int y)
    {
        PaintLayer layer = Document.Active;
        PushUndo(Tool == PaintTool.Eraser ? "Erase" : "Brush");
        _strokeLayer = layer;
        _strokeBase = layer.CapturePixels();
        _strokeMask = new byte[layer.Width * layer.Height];
        _maskX0 = int.MaxValue;
        _maskY0 = int.MaxValue;
        _maskX1 = -1;
        _maskY1 = -1;
        _lastX = x + 0.5f;
        _lastY = y + 0.5f;
        _stroking = true;

        StampDisc(_lastX, _lastY);
        ApplyMask();
    }

    /// <summary>Stamps a straight line of dabs between two points.</summary>
    private void StampLine(float fromX, float fromY, float toX, float toY)
    {
        float dx = toX - fromX;
        float dy = toY - fromY;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));
        float spacing = MathF.Max(0.5f, BrushSize * 0.2f);
        int steps = (int)(distance / spacing);

        if (steps <= 0)
        {
            StampDisc(toX, toY);
            return;
        }

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            StampDisc(fromX + (dx * t), fromY + (dy * t));
        }
    }

    /// <summary>Adds one brush dab to the coverage mask.</summary>
    private void StampDisc(float centerX, float centerY)
    {
        if (_strokeLayer is null || _strokeMask is null)
        {
            return;
        }

        float radius = MathF.Max(0.5f, BrushSize * 0.5f);
        float inner = radius * Math.Clamp(Hardness, 0f, 1f);
        float falloff = MathF.Max(0.5f, radius - inner + 0.5f);
        int minX = Math.Max(0, (int)MathF.Floor(centerX - radius - 1f));
        int maxX = Math.Min(_strokeLayer.Width - 1, (int)MathF.Ceiling(centerX + radius + 1f));
        int minY = Math.Max(0, (int)MathF.Floor(centerY - radius - 1f));
        int maxY = Math.Min(_strokeLayer.Height - 1, (int)MathF.Ceiling(centerY + radius + 1f));

        if (minX > maxX || minY > maxY)
        {
            return;
        }

        int width = _strokeLayer.Width;
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (Selection is not null && !Selection.Contains(x, y))
                {
                    continue;
                }

                float dx = x + 0.5f - centerX;
                float dy = y + 0.5f - centerY;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                if (distance > radius + 1f)
                {
                    continue;
                }

                float coverage = distance <= inner ? 1f : (radius + 0.5f - distance) / falloff;
                if (coverage <= 0f)
                {
                    continue;
                }

                int value = (int)(Math.Clamp(coverage, 0f, 1f) * 255f);
                int index = (y * width) + x;
                if (value > _strokeMask[index])
                {
                    _strokeMask[index] = (byte)value;
                }

                if (x < _maskX0)
                {
                    _maskX0 = x;
                }

                if (y < _maskY0)
                {
                    _maskY0 = y;
                }

                if (x > _maskX1)
                {
                    _maskX1 = x;
                }

                if (y > _maskY1)
                {
                    _maskY1 = y;
                }
            }
        }
    }

    /// <summary>Blends the coverage mask over the stroke snapshot.</summary>
    private void ApplyMask()
    {
        if (_strokeLayer is null || _strokeBase is null || _strokeMask is null || _maskX1 < 0)
        {
            return;
        }

        bool erasing = Tool == PaintTool.Eraser;
        int brushAlpha = Alpha;
        if (brushAlpha <= 0)
        {
            return;
        }

        uint paint = Rgba.Opaque(Foreground);
        int width = _strokeLayer.Width;
        uint[] pixels = _strokeLayer.Pixels;

        for (int y = _maskY0; y <= _maskY1; y++)
        {
            int row = y * width;
            for (int x = _maskX0; x <= _maskX1; x++)
            {
                int index = row + x;
                int coverage = _strokeMask[index];
                uint basePixel = _strokeBase[index];

                if (coverage == 0)
                {
                    pixels[index] = basePixel;
                    continue;
                }

                int amount = coverage * brushAlpha / 255;

                if (erasing)
                {
                    // Erasing punches transparency through any layer, including the
                    // bottom one, so the checker grid shows where paint was removed.
                    int alpha = Rgba.A(basePixel) * (255 - amount) / 255;
                    pixels[index] = Rgba.WithAlpha(alpha, basePixel);
                    continue;
                }

                pixels[index] = Rgba.Over(basePixel, paint, amount);
            }
        }

        Document.MarkDirty();
    }

    /// <summary>Which edges of the selection a resize drag is moving.</summary>
    [Flags]
    private enum ResizeEdges
    {
        /// <summary>No edge.</summary>
        None = 0,

        /// <summary>The left edge follows the pointer.</summary>
        Left = 1,

        /// <summary>The right edge follows the pointer.</summary>
        Right = 2,

        /// <summary>The top edge follows the pointer.</summary>
        Top = 4,

        /// <summary>The bottom edge follows the pointer.</summary>
        Bottom = 8,
    }

    /// <summary>What a drag on the text transform box is doing.</summary>
    private enum TextHandle
    {
        /// <summary>No drag in progress.</summary>
        None,

        /// <summary>Moving the text.</summary>
        Move,

        /// <summary>Scaling from the top-left corner.</summary>
        TopLeft,

        /// <summary>Scaling from the top-right corner.</summary>
        TopRight,

        /// <summary>Scaling from the bottom-right corner.</summary>
        BottomRight,

        /// <summary>Scaling from the bottom-left corner.</summary>
        BottomLeft,

        /// <summary>Spinning the text.</summary>
        Rotate,
    }

    /// <summary>Pixels lifted from a document, with the place they came from.</summary>
    private sealed class ClipboardImage
    {
        public ClipboardImage(int width, int height, int originX, int originY, uint[] pixels, bool empty)
        {
            Width = width;
            Height = height;
            OriginX = originX;
            OriginY = originY;
            Pixels = pixels;
            Empty = empty;
        }

        public int Width { get; }

        public int Height { get; }

        public int OriginX { get; }

        public int OriginY { get; }

        public uint[] Pixels { get; }

        /// <summary>Gets whether every copied pixel is fully transparent.</summary>
        public bool Empty { get; }
    }

    /// <summary>Everything that belongs to one open document rather than the editor.</summary>
    private sealed class DocumentTab
    {
        public DocumentTab(PaintDocument document, string name)
        {
            Document = document;
            Name = name;
        }

        public PaintDocument Document { get; set; }

        public string Name { get; set; }

        public List<DocumentSnapshot> Undo { get; set; } = [];

        public List<DocumentSnapshot> Redo { get; set; } = [];

        public Selection? Selection { get; set; }

        public float Zoom { get; set; } = 1f;

        public int CursorX { get; set; } = -1;

        public int CursorY { get; set; } = -1;

        public float Brightness { get; set; }

        public float Contrast { get; set; }

        public float Hue { get; set; }

        public float Saturation { get; set; }

        public float Lightness { get; set; }
    }

    /// <summary>An immutable copy of the whole layer stack, used for undo and redo.</summary>
    private sealed class DocumentSnapshot
    {
        private readonly Frame[] _frames;
        private readonly int _active;
        private readonly Selection? _selection;

        private DocumentSnapshot(Frame[] frames, int active, Selection? selection, string name)
        {
            _frames = frames;
            _active = active;
            _selection = selection;
            Name = name;
        }

        /// <summary>Gets the name of the step this snapshot represents.</summary>
        public string Name { get; }

        /// <summary>Copies every layer's pixels and the selection around them.</summary>
        public static DocumentSnapshot Capture(PaintDocument document, Selection? selection, string name)
        {
            Frame[] frames = new Frame[document.Layers.Count];
            for (int i = 0; i < frames.Length; i++)
            {
                PaintLayer layer = document.Layers[i];
                frames[i] = new Frame(layer, layer.CapturePixels(), layer.Visible, layer.Opacity, layer.Blend, layer.Locked);
            }

            return new DocumentSnapshot(frames, document.ActiveIndex, selection, name);
        }

        /// <summary>Restores the copied layers and their order, returning the selection.</summary>
        public Selection? Restore(PaintDocument document)
        {
            document.Layers.Clear();
            foreach (Frame frame in _frames)
            {
                frame.Layer.Adopt(frame.Pixels);
                frame.Layer.Visible = frame.Visible;
                frame.Layer.Opacity = frame.Opacity;
                frame.Layer.Blend = frame.Blend;
                frame.Layer.Locked = frame.Locked;
                document.Layers.Add(frame.Layer);
            }

            document.ActiveIndex = Math.Clamp(_active, 0, document.Layers.Count - 1);
            document.MarkDirty();
            return _selection;
        }

        private readonly record struct Frame(
            PaintLayer Layer,
            uint[] Pixels,
            bool Visible,
            float Opacity,
            BlendMode Blend,
            bool Locked);
    }
}
