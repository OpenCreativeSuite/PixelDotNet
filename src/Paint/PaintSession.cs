using System;
using System.Collections.Generic;
using System.IO;
using Donkeh.Windowing;
using PixelDotNet.Help;

namespace PixelDotNet.Paint;

/// <summary>Which action the file browser is collecting.</summary>
public enum FileDialogMode
{
    /// <summary>Choose an existing image to load.</summary>
    Open,

    /// <summary>Choose a folder and name to write the image to.</summary>
    Save,
}

/// <summary>One row in the file browser: a folder or a supported image file.</summary>
/// <param name="Name">Display name, without a path.</param>
/// <param name="FullPath">Absolute path.</param>
/// <param name="IsDirectory">Whether this row is a folder.</param>
public readonly record struct FileEntry(string Name, string FullPath, bool IsDirectory);

/// <summary>A shortcut to a well-known folder in the file browser.</summary>
/// <param name="Label">Short name shown on the button.</param>
/// <param name="Path">Absolute folder path.</param>
public readonly record struct FileLocation(string Label, string Path);

/// <summary>Where existing artwork sits when the canvas is resized.</summary>
public enum ResizeAnchor
{
    /// <summary>Top-left corner.</summary>
    TopLeft,

    /// <summary>Top edge, horizontally centred.</summary>
    Top,

    /// <summary>Top-right corner.</summary>
    TopRight,

    /// <summary>Left edge, vertically centred.</summary>
    Left,

    /// <summary>Centred in both axes.</summary>
    Center,

    /// <summary>Right edge, vertically centred.</summary>
    Right,

    /// <summary>Bottom-left corner.</summary>
    BottomLeft,

    /// <summary>Bottom edge, horizontally centred.</summary>
    Bottom,

    /// <summary>Bottom-right corner.</summary>
    BottomRight,
}

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
    private int _guideDragIndex = -1;
    private bool _guideDragHorizontal;
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

    private readonly List<FileEntry> _fileEntries = [];
    private readonly List<FileLocation> _filePlaces = [];
    private string _lastFolder = string.Empty;
    private ImageFormat _saveFormat = ImageFormat.Png;
    private FileEntry? _fileMenuEntry;
    private string _fileClipboard = string.Empty;
    private bool _fileClipboardCut;
    private bool _filePromptIsRename;
    private string _filePromptTarget = string.Empty;

    private PaintLayer? _strokeLayer;
    private uint[]? _strokeBase;
    private byte[]? _maskBase;
    private byte[]? _strokeMask;
    private int _maskX0;
    private int _maskY0;
    private int _maskX1 = -1;
    private int _maskY1 = -1;
    private float _startX;
    private float _startY;
    private float _lastX;
    private float _lastY;
    private int _cloneOffsetX;
    private int _cloneOffsetY;
    private bool _stroking;
    private bool _previewing;
    private bool _lassoing;
    private bool _shapeAsLayer;
    private PaintLayer? _shapeLayer;
    private float _shapeStartX;
    private float _shapeStartY;
    private int _shapePreviousIndex;
    private ShapeData? _shapeEditBackup;
    private readonly List<PathNode> _penPoints = [];
    private float _penPreviewX;
    private float _penPreviewY;
    private int _penDragNode = -1;
    private bool _textAsLayer;
    private bool _isPathNodeEditing;
    private int _pathNodeEditIndex = -1;
    private int _selectedPathNode = -1;
    private int _pathNodeDrag = -1;
    private int _pathHandleDrag;
    private bool _pathUndoPushed;
    private float _pathDragX;
    private float _pathDragY;
    private TextData? _textEditBackup;
    private bool _quickSelecting;
    private bool _quickSubtractGesture;
    private byte[]? _quickMask;
    private Selection? _selectionBase;
    private int _selectionCombine;
    private int _quickLastX = int.MinValue / 2;
    private int _quickLastY = int.MinValue / 2;
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
    private PaintLayer? _adjustEditLayer;

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

    /// <summary>Gets the clone/healing sample X, or -1 when no source is set.</summary>
    public int CloneSourceX { get; private set; } = -1;

    /// <summary>Gets the clone/healing sample Y, or -1 when no source is set.</summary>
    public int CloneSourceY { get; private set; } = -1;

    /// <summary>Gets whether a clone/healing source has been sampled.</summary>
    public bool HasCloneSource => CloneSourceX >= 0 && CloneSourceY >= 0;

    /// <summary>Gets the source point currently drawn while painting.</summary>
    public int CloneMarkerX { get; private set; } = -1;

    /// <summary>Gets the source point currently drawn while painting.</summary>
    public int CloneMarkerY { get; private set; } = -1;

    /// <summary>Gets or sets whether the Dodge/Burn tool lightens (true) or darkens.</summary>
    public bool DodgeMode { get; set; } = true;

    /// <summary>Gets or sets the brush edge hardness in the 0-1 range.</summary>
    public float Hardness { get; set; } = 0.8f;

    /// <summary>Gets or sets the tool opacity in the 0-1 range.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Gets or sets the flood-fill colour tolerance.</summary>
    public int Tolerance { get; set; } = 32;

    /// <summary>Gets or sets the magic wand's colour tolerance.</summary>
    public int MagicTolerance { get; set; } = 32;

    /// <summary>Gets or sets the colour tolerance of the quick selection brush.</summary>
    public int QuickTolerance { get; set; } = 32;

    /// <summary>Gets or sets whether quick selection subtracts instead of adds.</summary>
    public bool QuickSubtract { get; set; }

    /// <summary>Gets or sets whether the magic wand only selects the connected region.</summary>
    public bool MagicContiguous { get; set; } = true;

    /// <summary>Gets or sets whether the shape tools fill instead of outline.</summary>
    public bool FillShapes { get; set; } = true;

    /// <summary>Gets or sets whether shape tools create editable shape layers.</summary>
    public bool ShapeAsLayer
    {
        get => _shapeAsLayer;
        set => _shapeAsLayer = value;
    }

    /// <summary>Gets whether the shape editor is open.</summary>
    public bool IsShapeEditOpen { get; private set; }

    /// <summary>Gets the shape layer being edited.</summary>
    public int ShapeEditIndex { get; private set; } = -1;

    /// <summary>Gets the shape layer the editor targets.</summary>
    public PaintLayer? ShapeEditLayer =>
        ShapeEditIndex >= 0 && ShapeEditIndex < Document.Layers.Count
            ? Document.Layers[ShapeEditIndex]
            : null;

    /// <summary>Gets the editable shape data for the dialog.</summary>
    public ShapeData? ShapeEditTarget => ShapeEditLayer?.Shape;

    /// <summary>Gets or sets whether the text tool creates editable text layers.</summary>
    public bool TextAsLayer
    {
        get => _textAsLayer;
        set => _textAsLayer = value;
    }

    /// <summary>Gets whether the text-layer editor is open.</summary>
    public bool IsTextEditOpen { get; private set; }

    /// <summary>Gets the text layer being edited.</summary>
    public int TextEditIndex { get; private set; } = -1;

    /// <summary>Gets the text layer the editor targets.</summary>
    public PaintLayer? TextEditLayer =>
        TextEditIndex >= 0 && TextEditIndex < Document.Layers.Count
            ? Document.Layers[TextEditIndex]
            : null;

    /// <summary>Gets the editable text data for the dialog.</summary>
    public TextData? TextEditTarget => TextEditLayer?.Text;

    /// <summary>Gets whether path nodes are being edited on the canvas.</summary>
    public bool IsPathNodeEditing => _isPathNodeEditing;

    /// <summary>Gets the index of the path layer being node-edited.</summary>
    public int PathNodeEditIndex => _pathNodeEditIndex;

    /// <summary>Gets the path layer being node-edited.</summary>
    public PaintLayer? PathNodeLayer =>
        _pathNodeEditIndex >= 0 && _pathNodeEditIndex < Document.Layers.Count
            ? Document.Layers[_pathNodeEditIndex]
            : null;

    /// <summary>Gets the path data being node-edited.</summary>
    public ShapeData? PathNodeShape => PathNodeLayer?.Shape;

    /// <summary>Gets the index of the selected path node, or -1.</summary>
    public int SelectedPathNode => _selectedPathNode;

    /// <summary>Gets whether the active layer is a path that can be node-edited.</summary>
    public bool CanEditPathNodes =>
        Document.Active.IsShapeLayer
        && Document.Active.Shape is { Kind: ShapeKind.Path, Points: { Count: >= 2 } };

    /// <summary>Gets whether a pen path is in progress.</summary>
    public bool IsPenActive => _penPoints.Count > 0;

    /// <summary>Gets the path nodes placed so far by the pen tool.</summary>
    public IReadOnlyList<PathNode> PenPoints => _penPoints;

    /// <summary>Gets the pen preview X.</summary>
    public float PenPreviewX => _penPreviewX;

    /// <summary>Gets the pen preview Y.</summary>
    public float PenPreviewY => _penPreviewY;

    /// <summary>Gets whether brush and eraser strokes currently edit the active layer's mask.</summary>
    public bool MaskEditing { get; private set; }

    /// <summary>Gets or sets the outline width used when shapes are not filled.</summary>
    public float StrokeWidth { get; set; } = 4f;

    // ---- view -------------------------------------------------------------

    /// <summary>Gets or sets the canvas zoom factor.</summary>
    public float Zoom { get; set; } = 1f;

    /// <summary>Gets or sets whether the eight-pixel guide grid is drawn.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Gets or sets whether rulers are shown around the canvas.</summary>
    public bool ShowRulers { get; set; } = true;

    /// <summary>Gets or sets whether guides are drawn.</summary>
    public bool ShowGuides { get; set; } = true;

    /// <summary>Gets or sets whether guides and canvas edges attract drags.</summary>
    public bool SnapToGuides { get; set; } = true;

    /// <summary>Gets or sets whether hovering controls shows help tooltips.</summary>
    public bool ShowTooltips { get; set; }

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

    /// <summary>Gets the tooltip currently shown, or null when the pointer is not over a control.</summary>
    public TipState? Tip { get; private set; }

    /// <summary>Gets or sets whether the pointer is over the floating tooltip itself.</summary>
    public bool TipHovered { get; set; }

    /// <summary>Gets a counter bumped whenever a new tooltip is shown.</summary>
    public int TipGeneration { get; private set; }

    /// <summary>Gets the help article shown in the modal, or null when it is closed.</summary>
    public HelpTopic? OpenHelp { get; private set; }

    /// <summary>Gets or sets the viewport width used to place the tooltip.</summary>
    public float ViewportWidth { get; set; } = 1440f;

    /// <summary>Gets or sets the viewport height used to place the tooltip.</summary>
    public float ViewportHeight { get; set; } = 900f;

    /// <summary>Shows a tooltip anchored to a control's last bounding box.</summary>
    public void ShowTip(TipState tip)
    {
        if (!ShowTooltips && tip.ShowChevron)
        {
            return;
        }

        Tip = tip;
        TipGeneration++;
        TipHovered = false;
        Changed();
    }

    /// <summary>Hides the current tooltip.</summary>
    public void HideTip()
    {
        if (Tip is null)
        {
            return;
        }

        Tip = null;
        TipHovered = false;
        Changed();
    }

    /// <summary>Opens a help article in the modal.</summary>
    public void ShowHelp(HelpTopic topic)
    {
        OpenHelp = topic;
        Tip = null;
        Status = "Help: " + topic.Title;
        Changed();
    }

    /// <summary>Closes the help modal.</summary>
    public void CloseHelp()
    {
        if (OpenHelp is null)
        {
            return;
        }

        OpenHelp = null;
        Changed();
    }

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

    /// <summary>Gets or sets the levels black point, 0-254.</summary>
    public float LevelsBlack { get; set; }

    /// <summary>Gets or sets the levels white point, 1-255.</summary>
    public float LevelsWhite { get; set; } = 255f;

    /// <summary>Gets or sets the levels midtone gamma, 0.1-5.</summary>
    public float LevelsGamma { get; set; } = 1f;

    /// <summary>Gets the control points of the tone curve, normalised 0-1.</summary>
    public List<(float X, float Y)> CurvePoints { get; } = [(0f, 0f), (1f, 1f)];

    /// <summary>Gets whether the levels controls are away from neutral.</summary>
    public bool HasLevels => LevelsBlack > 0.01f || LevelsWhite < 254.99f || MathF.Abs(LevelsGamma - 1f) > 0.001f;

    /// <summary>Gets whether the curve is away from the identity line.</summary>
    public bool HasCurve
    {
        get
        {
            foreach ((float x, float y) in CurvePoints)
            {
                if (MathF.Abs(x - y) > 0.002f)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets whether any live adjustment is away from neutral.</summary>
    public bool HasAdjustments =>
        AdjustBrightness != 0f || AdjustContrast != 0f || AdjustHue != 0f || AdjustSaturation != 0f || AdjustLightness != 0f
        || HasLevels || HasCurve;

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
        PaintTool.QuickSelect => "Quick Selection",
        PaintTool.Brush => "Brush",
        PaintTool.Eraser => "Eraser",
        PaintTool.CloneStamp => "Clone Stamp",
        PaintTool.HealingBrush => "Healing Brush",
        PaintTool.DodgeBurn => "Dodge / Burn",
        PaintTool.Line => "Line",
        PaintTool.Rectangle => "Rectangle",
        PaintTool.Ellipse => "Ellipse",
        PaintTool.Pen => "Pen",
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
        PaintTool.QuickSelect => "A",
        PaintTool.Brush => "B",
        PaintTool.Eraser => "E",
        PaintTool.CloneStamp => "C",
        PaintTool.HealingBrush => "H",
        PaintTool.DodgeBurn => "J",
        PaintTool.Line => "L",
        PaintTool.Rectangle => "R",
        PaintTool.Ellipse => "O",
        PaintTool.Pen => "P",
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
        if (_shapeLayer is not null)
        {
            FinishShapeLayer();
        }

        if (tool != PaintTool.Pen && _penPoints.Count > 0)
        {
            CancelPenPath();
        }

        if (_isPathNodeEditing)
        {
            EndPathNodeEdit();
        }

        Tool = tool;
        _quickSelecting = false;
        _quickMask = null;
        IsFontMenuOpen = false;
        IsFontSearchFocused = false;
        IsTextFxOpen = false;
        ColorTarget = TextColorTarget.Fill;
        _lassoing = false;
        _lassoPoints.Clear();
        Tip = null;
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
        Tip = null;
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

    /// <summary>Shows or hides the rulers.</summary>
    public void ToggleRulers()
    {
        ShowRulers = !ShowRulers;
        Status = ShowRulers ? "Rulers on" : "Rulers off";
        Changed();
    }

    /// <summary>Shows or hides document guides.</summary>
    public void ToggleGuides()
    {
        ShowGuides = !ShowGuides;
        Status = ShowGuides ? "Guides on" : "Guides off";
        Changed();
    }

    /// <summary>Enables or disables guide and edge snapping.</summary>
    public void ToggleSnapping()
    {
        SnapToGuides = !SnapToGuides;
        Status = SnapToGuides ? "Snapping on" : "Snapping off";
        Changed();
    }

    /// <summary>Removes every guide from the current document.</summary>
    public void ClearGuides()
    {
        if (Document.Guides.Count == 0)
        {
            return;
        }

        Document.Guides.Clear();
        _guideDragIndex = -1;
        Status = "Guides cleared";
        Changed();
    }

    /// <summary>Places a guide at a document position from a ruler click.</summary>
    public void PlaceGuide(bool horizontal, float position)
    {
        int limit = horizontal ? Document.Height : Document.Width;
        int value = Math.Clamp((int)MathF.Round(position), 0, limit);
        value = SnapGuidePosition(value, horizontal);
        Document.Guides.Add(new Guide(horizontal, value));
        ShowGuides = true;
        Status = $"Guide {value}";
        Changed();
    }

    /// <summary>Starts dragging a new guide from a ruler.</summary>
    public void BeginGuideDrag(bool horizontal)
    {
        _guideDragHorizontal = horizontal;
        Document.Guides.Add(new Guide(horizontal, 0));
        _guideDragIndex = Document.Guides.Count - 1;
        ShowGuides = true;
        Status = "Drag to place the guide";
        Changed();
    }

    /// <summary>Moves the guide being dragged to a document position.</summary>
    public void UpdateGuideDrag(float position)
    {
        if (_guideDragIndex < 0 || _guideDragIndex >= Document.Guides.Count)
        {
            return;
        }

        int limit = _guideDragHorizontal ? Document.Height : Document.Width;
        int value = Math.Clamp((int)MathF.Round(position), 0, limit);
        value = SnapGuidePosition(value, _guideDragHorizontal);
        Document.Guides[_guideDragIndex] = new Guide(_guideDragHorizontal, value);
        Status = $"Guide {value}";
        Changed();
    }

    /// <summary>Finishes a guide drag.</summary>
    public void EndGuideDrag()
    {
        if (_guideDragIndex < 0)
        {
            return;
        }

        _guideDragIndex = -1;
        Status = "Guide placed";
        Changed();
    }

    /// <summary>Snaps a guide position to the canvas edges, centre or another guide.</summary>
    private int SnapGuidePosition(int value, bool horizontal)
    {
        if (!SnapToGuides)
        {
            return value;
        }

        float threshold = 6f / MathF.Max(Zoom, 0.05f);
        int size = horizontal ? Document.Height : Document.Width;
        int best = value;
        float bestDistance = threshold + 0.01f;

        Check(0);
        Check(size / 2);
        Check(size);

        foreach (Guide guide in Document.Guides)
        {
            if (guide.Horizontal == horizontal)
            {
                Check(guide.Position);
            }
        }

        return best;

        void Check(int candidate)
        {
            float distance = MathF.Abs(candidate - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
    }

    /// <summary>Snaps a moving rectangle's offset to guides and canvas edges.</summary>
    public (int X, int Y) SnapMove(int startX, int startY, int width, int height, int dx, int dy)
    {
        if (!SnapToGuides)
        {
            return (dx, dy);
        }

        float threshold = 6f / MathF.Max(Zoom, 0.05f);
        dx = SnapAxis(startX, width, dx, vertical: true, threshold);
        dy = SnapAxis(startY, height, dy, vertical: false, threshold);
        return (dx, dy);
    }

    /// <summary>Snaps one axis of a moving rectangle.</summary>
    private int SnapAxis(int start, int size, int delta, bool vertical, float threshold)
    {
        int extent = vertical ? Document.Width : Document.Height;
        int best = delta;
        float bestDistance = threshold + 0.01f;

        Check(0);
        Check(extent / 2);
        Check(extent);

        foreach (Guide guide in Document.Guides)
        {
            if (guide.Horizontal == !vertical)
            {
                Check(guide.Position);
            }
        }

        return best;

        void Check(int candidate)
        {
            CheckEdge(start + delta, candidate);
            CheckEdge(start + delta + (size / 2), candidate);
            CheckEdge(start + delta + size, candidate);
        }

        void CheckEdge(int edge, int candidate)
        {
            float distance = MathF.Abs(candidate - edge);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = delta + (candidate - edge);
            }
        }
    }

    /// <summary>Turns hover tooltips on or off.</summary>
    public void ToggleTooltips()
    {
        ShowTooltips = !ShowTooltips;
        Tip = null;
        Status = ShowTooltips ? "Tooltips on" : "Tooltips off";
        Changed();
    }

    /// <summary>Toggles a top-level menu open or closed.</summary>
    public void ToggleMenu(string name)
    {
        Tip = null;
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
        Tip = null;
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

        if (IsPathNodeEditing)
        {
            PathNodePointerDown(documentX, documentY);
            return;
        }

        if (MaskEditing && Tool is not (
            PaintTool.Brush
            or PaintTool.Eraser
            or PaintTool.Marquee
            or PaintTool.EllipseSelect
            or PaintTool.Lasso
            or PaintTool.MagicWand
            or PaintTool.Zoom
            or PaintTool.Eyedropper))
        {
            Status = "Editing layer mask - use the brush or eraser";
            Changed();
            return;
        }

        if (Document.Active.IsAdjustment && Tool is not (
            PaintTool.Marquee
            or PaintTool.EllipseSelect
            or PaintTool.Lasso
            or PaintTool.MagicWand
            or PaintTool.Zoom
            or PaintTool.Eyedropper))
        {
            Status = "Select a raster layer to paint";
            Changed();
            return;
        }

        if (Tool is PaintTool.CloneStamp or PaintTool.HealingBrush && !HasCloneSource)
        {
            Status = "Ctrl-click to set the clone source";
            Changed();
            return;
        }

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
            _selectionCombine = SelectionCombineMode();
            _selectionBase = _selectionCombine == 0 ? null : Selection;
            Selection? incoming = Selection.FromPoints(
                documentX,
                documentY,
                documentX,
                documentY,
                Document.Width,
                Document.Height);
            Selection = CombineSelection(incoming, _selectionCombine);
            Changed();
            return;
        }

        if (Tool == PaintTool.EllipseSelect)
        {
            _previewing = true;
            _selectionCombine = SelectionCombineMode();
            _selectionBase = _selectionCombine == 0 ? null : Selection;
            Selection? incoming = Selection.EllipseFromPoints(
                documentX,
                documentY,
                documentX,
                documentY,
                Document.Width,
                Document.Height);
            Selection = CombineSelection(incoming, _selectionCombine);
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

        if (Tool == PaintTool.QuickSelect)
        {
            BeginQuickSelection(CursorX, CursorY);
            return;
        }

        if (Document.Active.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        if (Tool == PaintTool.Pen)
        {
            PenPointerDown(documentX, documentY);
            return;
        }

        if (Document.Active.IsSmartObject
            || Document.Active.IsShapeLayer
            || Document.Active.IsTextLayer)
        {
            Status = "Rasterize the layer to paint";
            Changed();
            return;
        }

        if (_shapeAsLayer && Tool is PaintTool.Rectangle or PaintTool.Ellipse or PaintTool.Line)
        {
            BeginShapeLayer(documentX, documentY);
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

        if (IsPathNodeEditing)
        {
            PathNodePointerMove(documentX, documentY);
            return;
        }

        if (Tool == PaintTool.QuickSelect)
        {
            if (_quickSelecting)
            {
                QuickSelectAt(CursorX, CursorY);
            }

            return;
        }

        if (_shapeLayer is not null)
        {
            UpdateShapeLayer(documentX, documentY);
            return;
        }

        if (Tool == PaintTool.Pen)
        {
            if (_penDragNode >= 0 && _penDragNode < _penPoints.Count)
            {
                PathNode node = _penPoints[_penDragNode];
                node.OutX = documentX;
                node.OutY = documentY;
                node.InX = (node.X * 2f) - documentX;
                node.InY = (node.Y * 2f) - documentY;
                _penPoints[_penDragNode] = node;
            }
            else
            {
                _penPreviewX = documentX;
                _penPreviewY = documentY;
            }

            if (_penPoints.Count > 0)
            {
                Changed();
            }

            return;
        }

        if (_stroking && Tool is PaintTool.CloneStamp or PaintTool.HealingBrush && HasCloneSource)
        {
            CloneMarkerX = CursorX + _cloneOffsetX;
            CloneMarkerY = CursorY + _cloneOffsetY;
        }

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
                Selection = CombineSelection(
                    Selection.FromPoints(_startX, _startY, documentX, documentY, Document.Width, Document.Height),
                    _selectionCombine);
            }
            else if (Tool == PaintTool.EllipseSelect)
            {
                Selection = CombineSelection(
                    Selection.EllipseFromPoints(_startX, _startY, documentX, documentY, Document.Width, Document.Height),
                    _selectionCombine);
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
            if (_moveStart is { } start)
            {
                (dx, dy) = SnapMove(start.X, start.Y, start.Width, start.Height, dx, dy);
            }

            MoveSelectionBy(dx, dy);
            return;
        }

        if (_moving && _strokeLayer is not null && _strokeBase is not null)
        {
            int dx = (int)MathF.Round(documentX - _startX);
            int dy = (int)MathF.Round(documentY - _startY);
            (dx, dy) = SnapMove(0, 0, _strokeLayer.Width, _strokeLayer.Height, dx, dy);
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

        if (IsPathNodeEditing)
        {
            PathNodePointerUp();
            return;
        }

        if (Tool == PaintTool.QuickSelect)
        {
            if (_quickSelecting)
            {
                _quickSelecting = false;
                _quickMask = null;
                Status = Selection is null
                    ? "Nothing selected"
                    : $"Selection {Selection.Width} x {Selection.Height}";
                Changed();
            }

            return;
        }

        if (_shapeLayer is not null)
        {
            FinishShapeLayer();
            return;
        }

        if (Tool == PaintTool.Pen)
        {
            _penDragNode = -1;
            return;
        }

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
        _maskBase = null;
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
        Status = $"Resize {newWidth} x {newHeight}";
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

        if (Tool is PaintTool.CloneStamp or PaintTool.HealingBrush)
        {
            if (Inside(x, y))
            {
                CloneSourceX = x;
                CloneSourceY = y;
                CloneMarkerX = x;
                CloneMarkerY = y;
                Status = $"{NameOf(Tool)} source {x}, {y}";
                Changed();
            }

            return;
        }

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

    /// <summary>Sets the clone/healing source from a canvas-local position.</summary>
    public void SetCloneSource(float localX, float localY)
    {
        (float documentX, float documentY) = ToDocument(localX, localY);
        int x = (int)MathF.Floor(documentX);
        int y = (int)MathF.Floor(documentY);
        if (!Inside(x, y))
        {
            return;
        }

        CloneSourceX = x;
        CloneSourceY = y;
        CloneMarkerX = x;
        CloneMarkerY = y;
        Status = $"{NameOf(Tool)} source {x}, {y}";
        Changed();
    }

    /// <summary>Changes the brush diameter from a wheel delta.</summary>
    public void NudgeBrushSize(float wheel)
    {
        BrushSize = Math.Clamp(BrushSize + (wheel * 3f), 1f, 120f);
        Status = $"Brush {MathF.Round(BrushSize)} px";
        Changed();
    }

    /// <summary>Steps the canvas zoom from a wheel delta.</summary>
    public void ZoomByWheel(float wheel)
    {
        if (wheel > 0f)
        {
            ZoomIn();
        }
        else if (wheel < 0f)
        {
            ZoomOut();
        }
    }

    /// <summary>Starts a freehand selection and records its first point.</summary>
    private void BeginLasso(float x, float y)
    {
        _lassoing = true;
        _selectionCombine = SelectionCombineMode();
        _selectionBase = _selectionCombine == 0 ? null : Selection;
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
        Selection? polygon = SelectionOps.FromPolygon(
            _lassoPoints,
            Document.Width,
            Document.Height);
        Selection = CombineSelection(polygon, _selectionCombine);
        _lassoPoints.Clear();
        Status = Selection is null
            ? "Deselected"
            : $"Selection {Selection.Width} x {Selection.Height}";
        Changed();
    }

    /// <summary>Selects the colour region under a pointer position with the wand.</summary>
    private void SelectMagicRegion(int x, int y)
    {
        PushUndo("Magic Wand");
        Selection? existing = Selection;
        Selection? region = SelectionOps.MagicWand(
            Document,
            x,
            y,
            MagicTolerance,
            MagicContiguous);

        int mode = SelectionCombineMode();
        Selection = mode switch
        {
            1 => SelectionOps.Combine(existing, region, false, Document.Width, Document.Height),
            2 => SelectionOps.Combine(existing, region, true, Document.Width, Document.Height),
            _ => region,
        };

        Status = Selection is null
            ? "Nothing selected"
            : $"Selection {Selection.Width} x {Selection.Height}";
        Changed();
    }

    /// <summary>Reads the selection combine mode from the live modifiers.</summary>
    private static int SelectionCombineMode()
    {
        if (InputState.Alt)
        {
            return 2;
        }

        return InputState.Shift || InputState.Control ? 1 : 0;
    }

    /// <summary>Combines a new shape with the selection captured when a drag began.</summary>
    private Selection? CombineSelection(Selection? incoming, int mode)
    {
        return mode switch
        {
            1 => SelectionOps.Combine(
                _selectionBase,
                incoming,
                false,
                Document.Width,
                Document.Height),
            2 => SelectionOps.Combine(
                _selectionBase,
                incoming,
                true,
                Document.Width,
                Document.Height),
            _ => incoming,
        };
    }

    /// <summary>Starts a brush-driven quick selection stroke.</summary>
    private void BeginQuickSelection(int x, int y)
    {
        int width = Document.Width;
        int height = Document.Height;
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        CommitPreviews();
        PushUndo("Quick Selection");
        _quickSelecting = true;
        _quickSubtractGesture = QuickSubtract || InputState.Alt;
        _quickMask = new byte[width * height];

        if (Selection is { } selection)
        {
            int left = Math.Max(0, selection.X);
            int top = Math.Max(0, selection.Y);
            int right = Math.Min(width, selection.Right);
            int bottom = Math.Min(height, selection.Bottom);

            for (int sy = top; sy < bottom; sy++)
            {
                for (int sx = left; sx < right; sx++)
                {
                    if (selection.Contains(sx, sy))
                    {
                        _quickMask[(sy * width) + sx] = 255;
                    }
                }
            }
        }

        _quickLastX = int.MinValue / 2;
        _quickLastY = int.MinValue / 2;
        QuickSelectAt(x, y);
    }

    /// <summary>Adds or removes one quick-selection seed region.</summary>
    private void QuickSelectAt(int x, int y)
    {
        if (_quickMask is null)
        {
            return;
        }

        int spacing = Math.Max(3, (int)(BrushSize * 0.5f));
        int dx = x - _quickLastX;
        int dy = y - _quickLastY;
        if ((dx * dx) + (dy * dy) < spacing * spacing)
        {
            return;
        }

        _quickLastX = x;
        _quickLastY = y;

        int width = Document.Width;
        int height = Document.Height;
        Selection? region = SelectionOps.FromPixels(
            Document.Composite,
            width,
            height,
            x,
            y,
            QuickTolerance,
            contiguous: true);

        if (region?.Mask is not { } regionMask)
        {
            return;
        }

        byte value = _quickSubtractGesture ? (byte)0 : (byte)255;
        int left = Math.Max(0, region.X);
        int top = Math.Max(0, region.Y);
        int right = Math.Min(width, region.Right);
        int bottom = Math.Min(height, region.Bottom);

        for (int row = top; row < bottom; row++)
        {
            int sourceRow = (row - region.Y) * region.Width;
            int targetRow = row * width;
            for (int column = left; column < right; column++)
            {
                if (regionMask[sourceRow + (column - region.X)] != 0)
                {
                    _quickMask[targetRow + column] = value;
                }
            }
        }

        RebuildQuickSelection();
        Status = Selection is null
            ? "Nothing selected"
            : $"Selection {Selection.Width} x {Selection.Height}";
        Changed();
    }

    /// <summary>Publishes the accumulated quick mask as a trimmed selection.</summary>
    private void RebuildQuickSelection()
    {
        if (_quickMask is null)
        {
            return;
        }

        int width = Document.Width;
        int height = Document.Height;
        int left = width;
        int top = height;
        int right = -1;
        int bottom = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (_quickMask[row + x] == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            Selection = null;
            return;
        }

        int trimmedWidth = right - left + 1;
        int trimmedHeight = bottom - top + 1;
        byte[] trimmed = new byte[trimmedWidth * trimmedHeight];

        for (int row = 0; row < trimmedHeight; row++)
        {
            Array.Copy(
                _quickMask,
                ((top + row) * width) + left,
                trimmed,
                row * trimmedWidth,
                trimmedWidth);
        }

        Selection = Selection.FromMask(left, top, trimmedWidth, trimmedHeight, trimmed);
    }

    /// <summary>Switches quick selection between adding and subtracting.</summary>
    public void ToggleQuickSubtract()
    {
        QuickSubtract = !QuickSubtract;
        Status = QuickSubtract
            ? "Quick Selection: subtract"
            : "Quick Selection: add";
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

        if (layer.IsAdjustment)
        {
            Status = "Adjustment layers cannot be filled";
            Changed();
            return;
        }

        PushUndo("Fill Selection");

        if (MaskEditing && layer.Mask is { } mask)
        {
            int value = ((Rgba.R(Foreground) * 77) + (Rgba.G(Foreground) * 150) + (Rgba.B(Foreground) * 29)) >> 8;
            for (int y = 0; y < layer.Height; y++)
            {
                for (int x = 0; x < layer.Width; x++)
                {
                    if (Selection is null || Selection.Contains(x, y))
                    {
                        mask[(y * layer.Width) + x] = (byte)value;
                    }
                }
            }

            Changed();
            return;
        }

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

        if (layer.IsAdjustment)
        {
            Status = "Adjustment layers cannot be cleared";
            Changed();
            return;
        }

        PushUndo("Clear");

        if (MaskEditing && layer.Mask is { } mask)
        {
            for (int y = 0; y < layer.Height; y++)
            {
                for (int x = 0; x < layer.Width; x++)
                {
                    if (Selection is null || Selection.Contains(x, y))
                    {
                        mask[(y * layer.Width) + x] = 0;
                    }
                }
            }

            Changed();
            return;
        }

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
            ? $"Copied {image.Width} x {image.Height} (empty area)"
            : $"Copied {image.Width} x {image.Height}{note}";
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
        Status = $"Copied merged {image.Width} x {image.Height}";
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
        Status = $"Cut {image.Width} x {image.Height}";
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
        // canvas ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â then centre it, which is what matters across documents.
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

        layer.Group = Document.Active.Group;
        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        Status = $"Pasted {clip.Width} x {clip.Height}";
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
        byte[]? layerMask = merged ? null : Document.Active.Mask;
        uint[] pixels = new uint[width * height];
        bool empty = true;
        Selection? selection = Selection;
        bool masked = selection?.Mask is not null;

        for (int row = 0; row < height; row++)
        {
            Array.Copy(source, ((originY + row) * Document.Width) + originX, pixels, row * width, width);

            if (layerMask is not null)
            {
                int maskRow = (originY + row) * Document.Width;
                for (int column = 0; column < width; column++)
                {
                    int index = (row * width) + column;
                    uint pixel = pixels[index];
                    int alpha = Rgba.A(pixel) * layerMask[maskRow + originX + column] / 255;
                    pixels[index] = Rgba.WithAlpha(alpha, pixel);
                }
            }

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
        PaintLayer layer = new($"Layer {Document.Layers.Count + 1}", Document.Width, Document.Height)
        {
            Group = Document.Active.Group,
        };
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
        PaintLayer copy = new(
            source.Name + " copy",
            Document.Width,
            Document.Height,
            kind: source.Kind,
            adjustment: source.Adjustment)
        {
            Opacity = source.Opacity,
            Blend = source.Blend,
            ClipToBelow = source.ClipToBelow,
            Group = source.Group,
        };
        copy.Adopt(source.CapturePixels());
        copy.AdoptMask(source.CaptureMask());
        copy.CopyAdjustmentFrom(source);
        copy.Style.CopyFrom(source.Style);

        if (source.IsSmartObject && source.SmartSource is { } smart)
        {
            copy.AdoptSmart(
                (uint[])smart.Clone(),
                source.SmartSourceWidth,
                source.SmartSourceHeight,
                source.SmartX,
                source.SmartY,
                source.SmartScaleX,
                source.SmartScaleY,
                source.SmartRotation,
                source.SmartFlipX,
                source.SmartFlipY);
        }

        if (source.IsShapeLayer && source.Shape is { } shape)
        {
            copy.AdoptShape(shape.Clone());
        }

        if (source.IsTextLayer && source.Text is { } text)
        {
            copy.AdoptText(text.Clone());
        }

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
        LayerGroup? topGroup = top.Group;

        if (top.IsAdjustment)
        {
            AdjustmentOps.ApplyToPixels(bottom.Pixels, top);
            bottom.Opacity = 1f;
            Document.Layers.RemoveAt(index);
            RemoveGroupIfEmpty(topGroup);
            Document.ActiveIndex = index - 1;
            Status = "Merged adjustment down";
            Changed();
            return;
        }

        int alpha = (int)MathF.Round(Math.Clamp(top.Opacity, 0f, 1f) * 255f);

        for (int i = 0; i < bottom.Pixels.Length; i++)
        {
            uint source = top.Pixels[i];
            int sourceAlpha = (int)(source >> 24);
            if (sourceAlpha == 0)
            {
                continue;
            }

            if (top.Mask is { } mask)
            {
                sourceAlpha = sourceAlpha * mask[i] / 255;
            }

            if (top.ClipToBelow)
            {
                sourceAlpha = sourceAlpha * Rgba.A(bottom.Pixels[i]) / 255;
            }

            if (sourceAlpha == 0)
            {
                continue;
            }

            bottom.Pixels[i] = Rgba.Blend(bottom.Pixels[i], source, sourceAlpha * alpha / 255, top.Blend);
        }

        bottom.Opacity = 1f;
        Document.Layers.RemoveAt(index);
        RemoveGroupIfEmpty(topGroup);
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
        Document.Groups.Clear();
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
        LayerGroup? group = Document.Layers[index].Group;
        Document.Layers.RemoveAt(index);
        RemoveGroupIfEmpty(group);
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
        MaskEditing = false;
        Status = Document.Active.Name;
        Changed();
    }

    /// <summary>Wraps the active layer in a new group.</summary>
    public void GroupActiveLayer()
    {
        PaintLayer layer = Document.Active;
        if (layer.Group is not null)
        {
            Status = "Layer is already in a group";
            Changed();
            return;
        }

        CommitPreviews();
        PushUndo("New Group");
        LayerGroup group = new(FreeGroupName());
        Document.Groups.Add(group);
        layer.Group = group;
        Status = "Grouped " + layer.Name;
        Changed();
    }

    /// <summary>Removes the active layer from its group.</summary>
    public void UngroupActiveLayer()
    {
        LayerGroup? group = Document.Active.Group;
        if (group is null)
        {
            Status = "Layer is not in a group";
            Changed();
            return;
        }

        CommitPreviews();
        PushUndo("Ungroup");
        for (int i = 0; i < Document.Layers.Count; i++)
        {
            if (ReferenceEquals(Document.Layers[i].Group, group))
            {
                Document.Layers[i].Group = null;
            }
        }

        Document.Groups.Remove(group);
        Status = "Ungrouped " + group.Name;
        Changed();
    }

    /// <summary>Selects the topmost layer inside a group.</summary>
    public void SelectGroup(LayerGroup group)
    {
        for (int i = Document.Layers.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Document.Layers[i].Group, group))
            {
                SelectLayer(i);
                return;
            }
        }

        Status = group.Name + " is empty";
        Changed();
    }

    /// <summary>Shows or hides a group in the layer panel.</summary>
    public void ToggleGroupExpanded(LayerGroup group)
    {
        group.Expanded = !group.Expanded;
        Changed();
    }

    /// <summary>Shows or hides a group's contribution to the image.</summary>
    public void ToggleGroupVisibility(LayerGroup group)
    {
        group.Visible = !group.Visible;
        Document.MarkDirty();
        Status = group.Visible ? "Group visible" : "Group hidden";
        Changed();
    }

    /// <summary>Sets the opacity of a group.</summary>
    public void SetGroupOpacity(LayerGroup group, float opacity)
    {
        group.Opacity = Math.Clamp(opacity, 0f, 1f);
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Reorders a layer around another layer.</summary>
    /// <param name="sourceIndex">Layer being dragged.</param>
    /// <param name="targetIndex">Layer the pointer is over.</param>
    /// <param name="above">True to place the dragged layer above the target.</param>
    public void MoveLayerTo(int sourceIndex, int targetIndex, bool above)
    {
        if (sourceIndex < 0 || sourceIndex >= Document.Layers.Count
            || targetIndex < 0 || targetIndex >= Document.Layers.Count
            || sourceIndex == targetIndex)
        {
            return;
        }

        CommitPreviews();
        PushUndo("Move Layer");

        PaintLayer layer = Document.Layers[sourceIndex];
        LayerGroup? oldGroup = layer.Group;
        LayerGroup? newGroup = Document.Layers[targetIndex].Group;
        Document.Layers.RemoveAt(sourceIndex);

        int target = targetIndex > sourceIndex ? targetIndex - 1 : targetIndex;
        int insertAt = Math.Clamp(above ? target + 1 : target, 0, Document.Layers.Count);

        layer.Group = newGroup;
        if (!ReferenceEquals(oldGroup, newGroup))
        {
            RemoveGroupIfEmpty(oldGroup);
        }

        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        Status = "Moved " + layer.Name;
        Changed();
    }

    /// <summary>Moves a layer inside a group, leaving it at the top of the group.</summary>
    public void MoveLayerIntoGroup(int sourceIndex, LayerGroup group)
    {
        if (sourceIndex < 0 || sourceIndex >= Document.Layers.Count)
        {
            return;
        }

        PaintLayer layer = Document.Layers[sourceIndex];
        LayerGroup? oldGroup = layer.Group;
        if (ReferenceEquals(oldGroup, group))
        {
            return;
        }

        CommitPreviews();
        PushUndo("Move Layer");
        Document.Layers.RemoveAt(sourceIndex);
        RemoveGroupIfEmpty(oldGroup);
        layer.Group = group;
        group.Expanded = true;

        int insertAt = -1;
        for (int i = Document.Layers.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Document.Layers[i].Group, group))
            {
                insertAt = i + 1;
                break;
            }
        }

        if (insertAt < 0)
        {
            insertAt = Document.Layers.Count;
        }

        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        Status = "Moved " + layer.Name + " into " + group.Name;
        Changed();
    }

    /// <summary>Takes a layer out of its group and puts it at the top of the stack.</summary>
    public void MoveLayerToRoot(int sourceIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= Document.Layers.Count)
        {
            return;
        }

        PaintLayer layer = Document.Layers[sourceIndex];
        if (layer.Group is null)
        {
            return;
        }

        CommitPreviews();
        PushUndo("Move Layer");
        LayerGroup? oldGroup = layer.Group;
        Document.Layers.RemoveAt(sourceIndex);
        RemoveGroupIfEmpty(oldGroup);
        layer.Group = null;
        Document.Layers.Add(layer);
        Document.ActiveIndex = Document.Layers.Count - 1;
        Status = "Moved " + layer.Name + " out of the group";
        Changed();
    }

    /// <summary>Dissolves a group, leaving its layers in place.</summary>
    public void UngroupGroup(LayerGroup group)
    {
        if (!Document.Groups.Contains(group))
        {
            return;
        }

        CommitPreviews();
        PushUndo("Ungroup");
        for (int i = 0; i < Document.Layers.Count; i++)
        {
            if (ReferenceEquals(Document.Layers[i].Group, group))
            {
                Document.Layers[i].Group = null;
            }
        }

        Document.Groups.Remove(group);
        Status = "Ungrouped " + group.Name;
        Changed();
    }

    // ---- layer drag state and context menu ---------------------------------

    /// <summary>Gets the row currently highlighted as a drag target.</summary>
    public string LayerDropTarget { get; private set; } = string.Empty;

    /// <summary>Highlights a layer or group row during a drag.</summary>
    public void SetLayerDropTarget(string target)
    {
        if (LayerDropTarget == target)
        {
            return;
        }

        LayerDropTarget = target;
        Changed();
    }

    /// <summary>Gets whether the layer context menu is open.</summary>
    public bool IsLayerMenuOpen { get; private set; }

    /// <summary>Gets the context menu X in window CSS pixels.</summary>
    public float LayerMenuX { get; private set; }

    /// <summary>Gets the context menu Y in window CSS pixels.</summary>
    public float LayerMenuY { get; private set; }

    /// <summary>Gets the layer the context menu targets, or -1.</summary>
    public int LayerMenuIndex { get; private set; } = -1;

    /// <summary>Gets the group the context menu targets, or null.</summary>
    public LayerGroup? LayerMenuGroup { get; private set; }

    /// <summary>Opens the layer or group context menu.</summary>
    public void OpenLayerMenu(int index, LayerGroup? group, float x, float y)
    {
        Tip = null;
        LayerMenuIndex = index;
        LayerMenuGroup = group;
        LayerMenuX = MathF.Min(x, MathF.Max(0f, ViewportWidth - 210f));
        LayerMenuY = MathF.Min(y, MathF.Max(0f, ViewportHeight - 320f));
        IsLayerMenuOpen = true;
        Changed();
    }

    /// <summary>Closes the layer context menu.</summary>
    public void CloseLayerMenu()
    {
        if (!IsLayerMenuOpen)
        {
            return;
        }

        IsLayerMenuOpen = false;
        LayerMenuGroup = null;
        LayerMenuIndex = -1;
        Changed();
    }

    /// <summary>Runs one action from the layer context menu.</summary>
    public void LayerMenuAction(string action)
    {
        LayerGroup? group = LayerMenuGroup;
        int index = LayerMenuIndex;
        IsLayerMenuOpen = false;

        if (group is not null)
        {
            switch (action)
            {
                case "newLayer":
                    SelectGroup(group);
                    AddLayer();
                    break;
                case "expand":
                    ToggleGroupExpanded(group);
                    break;
                case "visible":
                    ToggleGroupVisibility(group);
                    break;
                case "ungroup":
                    UngroupGroup(group);
                    break;
                default:
                    Changed();
                    break;
            }

            LayerMenuGroup = null;
            return;
        }

        if (index < 0)
        {
            switch (action)
            {
                case "newLayer":
                    AddLayer();
                    break;
                case "newGroup":
                    GroupActiveLayer();
                    break;
                default:
                    Changed();
                    break;
            }

            return;
        }

        SelectLayer(index);
        switch (action)
        {
            case "duplicate":
                DuplicateLayer();
                break;
            case "merge":
                MergeDown();
                break;
            case "mask":
                ToggleLayerMask();
                break;
            case "clip":
                ToggleClipToBelow();
                break;
            case "style":
                ShowLayerStyleDialog(index);
                break;
            case "smartConvert":
                ConvertToSmartObject();
                break;
            case "smartRaster":
                RasterizeSmartObject();
                break;
            case "smartTransform":
                ShowSmartTransformDialog();
                break;
            case "shapeEdit":
                ShowShapeEditDialog();
                break;
            case "shapeRaster":
                RasterizeShape();
                break;
            case "pathNodes":
                TogglePathNodeEdit();
                break;
            case "textOnPath":
                CreateTextOnPath();
                break;
            case "textEdit":
                ShowTextEditDialog();
                break;
            case "textRaster":
                RasterizeText();
                break;
            case "lock":
                ToggleLock(index);
                break;
            case "visible":
                ToggleLayer(index);
                break;
            case "group":
                GroupActiveLayer();
                break;
            case "ungroup":
                UngroupActiveLayer();
                break;
            case "delete":
                DeleteActiveLayer();
                break;
            default:
                Changed();
                break;
        }
    }

    /// <summary>Removes a group from the document when its last layer is gone.</summary>
    private void RemoveGroupIfEmpty(LayerGroup? group)
    {
        if (group is null)
        {
            return;
        }

        for (int i = 0; i < Document.Layers.Count; i++)
        {
            if (ReferenceEquals(Document.Layers[i].Group, group))
            {
                return;
            }
        }

        Document.Groups.Remove(group);
    }

    /// <summary>Returns a group name not already used in this document.</summary>
    private string FreeGroupName()
    {
        string name = "Group";
        int suffix = 1;
        while (GroupNamed(name + " " + suffix))
        {
            suffix++;
        }

        return name + " " + suffix;
    }

    /// <summary>Returns whether any group already has the given name.</summary>
    private bool GroupNamed(string name)
    {
        for (int i = 0; i < Document.Groups.Count; i++)
        {
            if (Document.Groups[i].Name == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds or removes the active layer's mask.</summary>
    public void ToggleLayerMask()
    {
        PaintLayer layer = Document.Active;
        if (layer.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        CommitPreviews();
        if (layer.HasMask)
        {
            PushUndo("Remove Layer Mask");
            layer.RemoveMask();
            MaskEditing = false;
            Status = "Removed layer mask";
        }
        else
        {
            PushUndo("Add Layer Mask");
            layer.AddMask();
            MaskEditing = true;
            Status = "Added layer mask";
        }

        Changed();
    }

    /// <summary>Switches painting between the layer pixels and its mask.</summary>
    public void ToggleMaskEditing(int index)
    {
        if (index != Document.ActiveIndex)
        {
            Document.ActiveIndex = Math.Clamp(index, 0, Document.Layers.Count - 1);
        }

        PaintLayer layer = Document.Active;
        if (!layer.HasMask)
        {
            return;
        }

        MaskEditing = !MaskEditing;
        Status = MaskEditing ? "Editing layer mask" : "Editing layer pixels";
        Changed();
    }

    /// <summary>Clips or unclips the active layer to the content below it.</summary>
    public void ToggleClipToBelow()
    {
        CommitPreviews();
        PushUndo("Clip Layer");
        Document.Active.ClipToBelow = !Document.Active.ClipToBelow;
        Status = Document.Active.ClipToBelow ? "Clipped to layer below" : "Clipping off";
        Changed();
    }

    /// <summary>Gets the active layer when it is an adjustment layer.</summary>
    public PaintLayer? ActiveAdjustmentLayer => Document.Active.IsAdjustment ? Document.Active : null;

    /// <summary>Adds a non-destructive adjustment layer above the active layer.</summary>
    public void AddAdjustmentLayer(AdjustmentKind kind)
    {
        if (kind == AdjustmentKind.None)
        {
            return;
        }

        CommitPreviews();
        PushUndo(kind + " Adjustment Layer");
        PaintLayer layer = new(
            kind + " Adjustment",
            Document.Width,
            Document.Height,
            kind: LayerKind.Adjustment,
            adjustment: kind);

        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        MaskEditing = false;
        Status = "Added " + kind + " adjustment layer";
        Changed();
    }

    /// <summary>Sets the levels values of the active Levels adjustment layer.</summary>
    public void SetAdjustmentLevels(float black, float white, float gamma)
    {
        if (ActiveAdjustmentLayer is not { Adjustment: AdjustmentKind.Levels } layer)
        {
            return;
        }

        layer.AdjustmentBlack = Math.Clamp(black, 0f, 254f);
        layer.AdjustmentWhite = Math.Clamp(white, 1f, 255f);
        layer.AdjustmentGamma = Math.Clamp(gamma, 0.1f, 5f);
        TouchAdjustmentLayer();
    }

    /// <summary>Resets the active Levels adjustment layer to neutral.</summary>
    public void ResetAdjustmentLevels()
    {
        if (ActiveAdjustmentLayer is not { Adjustment: AdjustmentKind.Levels } layer)
        {
            return;
        }

        layer.AdjustmentBlack = 0f;
        layer.AdjustmentWhite = 255f;
        layer.AdjustmentGamma = 1f;
        TouchAdjustmentLayer();
    }

    /// <summary>Gets the curve points edited by the Adjust panel.</summary>
    public IReadOnlyList<(float X, float Y)> ActiveCurvePoints =>
        ActiveAdjustmentLayer is { Adjustment: AdjustmentKind.Curves } layer
            ? layer.AdjustmentCurve
            : CurvePoints;

    /// <summary>Adds a point to the active curve target.</summary>
    public int AddActiveCurvePoint(float x, float y)
    {
        if (ActiveAdjustmentLayer is { Adjustment: AdjustmentKind.Curves } layer)
        {
            return InsertCurvePoint(layer.AdjustmentCurve, x, y);
        }

        return AddCurvePoint(x, y);
    }

    /// <summary>Moves a point on the active curve target.</summary>
    public void MoveActiveCurvePoint(int index, float x, float y)
    {
        if (ActiveAdjustmentLayer is { Adjustment: AdjustmentKind.Curves } layer)
        {
            MoveCurvePointIn(layer.AdjustmentCurve, index, x, y);
            return;
        }

        MoveCurvePoint(index, x, y);
    }

    /// <summary>Removes a point from the active curve target.</summary>
    public void RemoveActiveCurvePoint(int index)
    {
        if (ActiveAdjustmentLayer is { Adjustment: AdjustmentKind.Curves } layer)
        {
            if (index > 0 && index < layer.AdjustmentCurve.Count - 1 && layer.AdjustmentCurve.Count > 2)
            {
                layer.AdjustmentCurve.RemoveAt(index);
            }

            return;
        }

        RemoveCurvePoint(index);
    }

    /// <summary>Finds the nearest point on the active curve target.</summary>
    public int FindActiveCurvePoint(float x, float y, float radius)
    {
        IReadOnlyList<(float X, float Y)> points = ActiveCurvePoints;
        int best = -1;
        float bestDistance = radius * radius;

        for (int index = 0; index < points.Count; index++)
        {
            float dx = points[index].X - x;
            float dy = points[index].Y - y;
            float distance = (dx * dx) + (dy * dy);
            if (distance <= bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Resets the active curve target to the identity line.</summary>
    public void ResetActiveCurve()
    {
        if (ActiveAdjustmentLayer is { Adjustment: AdjustmentKind.Curves } layer)
        {
            layer.AdjustmentCurve.Clear();
            layer.AdjustmentCurve.Add((0f, 0f));
            layer.AdjustmentCurve.Add((1f, 1f));
            return;
        }

        ResetCurve();
    }

    /// <summary>Inserts a point into a curve list and returns its index.</summary>
    private static int InsertCurvePoint(List<(float X, float Y)> points, float x, float y)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);
        int index = 1;

        while (index < points.Count && points[index].X < x)
        {
            index++;
        }

        points.Insert(index, (x, y));
        return index;
    }

    /// <summary>Moves a point inside a curve list, keeping x ordered.</summary>
    private static void MoveCurvePointIn(List<(float X, float Y)> points, int index, float x, float y)
    {
        if (index < 0 || index >= points.Count)
        {
            return;
        }

        float left = index == 0 ? 0f : points[index - 1].X + 0.002f;
        float right = index == points.Count - 1 ? 1f : points[index + 1].X - 0.002f;
        points[index] = (
            Math.Clamp(x, Math.Min(left, right), Math.Max(left, right)),
            Math.Clamp(y, 0f, 1f));
    }

    /// <summary>Marks the active adjustment layer dirty and records one undo step per edit run.</summary>
    public void TouchAdjustmentLayer()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsAdjustment)
        {
            return;
        }

        if (!ReferenceEquals(_adjustEditLayer, layer))
        {
            PushUndo("Adjust Layer");
            _adjustEditLayer = layer;
        }

        Document.MarkDirty();
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

        if (layer.IsAdjustment)
        {
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

        if (HasLevels)
        {
            PaintOps.Levels(layer, Selection, LevelsBlack, LevelsWhite, LevelsGamma);
        }

        if (HasCurve)
        {
            PaintOps.Curves(layer, Selection, CurvePoints);
        }

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
        LevelsBlack = 0f;
        LevelsWhite = 255f;
        LevelsGamma = 1f;
        ResetCurve();
        Changed();
    }

    /// <summary>Ends the current adjustment session, leaving the pixels as they are.</summary>
    public void CommitAdjustments()
    {
        if (_adjustBase is null && _adjustEditLayer is null)
        {
            return;
        }

        _adjustBase = null;
        _adjustLayer = null;
        _adjustEditLayer = null;
        AdjustBrightness = 0f;
        AdjustContrast = 0f;
        AdjustHue = 0f;
        AdjustSaturation = 0f;
        AdjustLightness = 0f;
        LevelsBlack = 0f;
        LevelsWhite = 255f;
        LevelsGamma = 1f;
        ResetCurve();
        Document.MarkDirty();
    }

    /// <summary>Restores the Levels controls to neutral.</summary>
    public void ResetLevels()
    {
        LevelsBlack = 0f;
        LevelsWhite = 255f;
        LevelsGamma = 1f;
        TouchAdjustments();
        Changed();
    }

    /// <summary>Restores the tone curve to the identity line.</summary>
    public void ResetCurve()
    {
        CurvePoints.Clear();
        CurvePoints.Add((0f, 0f));
        CurvePoints.Add((1f, 1f));
    }

    /// <summary>
    /// Adds a curve control point after the specified x position and returns its
    /// index. Points stay ordered by x.
    /// </summary>
    public int AddCurvePoint(float x, float y)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);
        int index = 1;

        while (index < CurvePoints.Count && CurvePoints[index].X < x)
        {
            index++;
        }

        CurvePoints.Insert(index, (x, y));
        return index;
    }

    /// <summary>
    /// Moves one curve point. Its x position is constrained between the
    /// neighbouring points so the curve remains a function.
    /// </summary>
    public void MoveCurvePoint(int index, float x, float y)
    {
        if (index < 0 || index >= CurvePoints.Count)
        {
            return;
        }

        float left = index == 0 ? 0f : CurvePoints[index - 1].X + 0.002f;
        float right = index == CurvePoints.Count - 1 ? 1f : CurvePoints[index + 1].X - 0.002f;
        CurvePoints[index] = (
            Math.Clamp(x, Math.Min(left, right), Math.Max(left, right)),
            Math.Clamp(y, 0f, 1f));
    }

    /// <summary>Removes a curve point while keeping the two endpoints.</summary>
    public void RemoveCurvePoint(int index)
    {
        if (CurvePoints.Count <= 2 || index <= 0 || index >= CurvePoints.Count - 1)
        {
            return;
        }

        CurvePoints.RemoveAt(index);
    }

    /// <summary>Finds the curve point nearest to a normalised position.</summary>
    public int FindCurvePoint(float x, float y, float radius)
    {
        int best = -1;
        float bestDistance = radius * radius;

        for (int index = 0; index < CurvePoints.Count; index++)
        {
            float dx = CurvePoints[index].X - x;
            float dy = CurvePoints[index].Y - y;
            float distance = (dx * dx) + (dy * dy);
            if (distance <= bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return best;
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

        if (Document.Active.IsAdjustment)
        {
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

        if (layer.IsAdjustment)
        {
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
            return text.Length <= Limit ? text : text[..Limit].TrimEnd() + "...";
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
                Status = $"Rotate {TextRotation:0} deg";
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
        Tip = null;
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
        FontImportMessage = "Downloading...";
        Status = "Downloading font...";
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
        Tip = null;
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

        bool madeLayer = false;
        if (TextAsLayer && TextContent.Length > 0)
        {
            _textLayer?.Adopt(_textBase);
            TextData created = TextDataFromSession();
            CreateTextLayer(created, TextLayerName(created));
            madeLayer = true;
        }

        _textBase = null;
        _textLayer = null;
        _textHandle = TextHandle.None;
        TextContent = string.Empty;
        TextRotation = 0f;
        _textArtwork = null;
        _textRenderKey = string.Empty;
        TextGeneration++;
        Status = madeLayer ? "Text layer created" : "Text placed";
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
        if (Document.Active.Locked || Document.Active.IsAdjustment)
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
        if (Document.Active.Locked || Document.Active.IsAdjustment)
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
        Selection = RestoreSnapshot(snapshot);
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
        Selection = RestoreSnapshot(snapshot);
        Status = "Redo " + snapshot.Name;
        Changed();
    }

    /// <summary>Restores a snapshot, rebuilding the document when its size changed.</summary>
    private Selection? RestoreSnapshot(DocumentSnapshot snapshot)
    {
        if (snapshot.Width == Document.Width && snapshot.Height == Document.Height)
        {
            return snapshot.Restore(Document);
        }

        PaintDocument resized = new(snapshot.Width, snapshot.Height);
        Selection? selection = snapshot.Restore(resized);
        AdoptDocument(resized);
        return selection;
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
        MaskEditing = false;
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

    /// <summary>Renames the active tab after a save.</summary>
    private void NameActiveDocument(string name)
    {
        DocumentName = name;
        _tabs[_activeTab].Name = name;
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

    // ---- colour tools ------------------------------------------------------

    private readonly float[] _colourBalance = new float[9];
    private readonly float[] _channelMix = new float[9];
    private readonly float[] _selectiveColour = new float[36];
    private readonly float[] _perspective = new float[8];
    private uint[]? _perspectiveBase;
    private Selection? _perspectiveSelection;
    private readonly float[] _mesh = new float[MeshWarpOps.Points * MeshWarpOps.Points * 2];
    private uint[]? _meshBase;
    private Selection? _meshSelection;

    /// <summary>Gets whether the colour tools dialog is open.</summary>
    public bool IsColourToolsOpen { get; private set; }

    /// <summary>Gets the threshold level, 0-255.</summary>
    public int ThresholdLevel { get; private set; } = 128;

    /// <summary>Gets the dark end of the gradient map.</summary>
    public uint GradientLow { get; private set; } = 0xFF000000;

    /// <summary>Gets the light end of the gradient map.</summary>
    public uint GradientHigh { get; private set; } = 0xFFFFFFFF;

    /// <summary>Gets the colour replaced by the replace tool.</summary>
    public uint ReplaceFrom { get; private set; } = 0xFF000000;

    /// <summary>Gets the replacement colour.</summary>
    public uint ReplaceTo { get; private set; } = 0xFFFFFFFF;

    /// <summary>Gets the replace-colour tolerance.</summary>
    public int ReplaceTolerance { get; private set; } = 32;

    /// <summary>Gets one colour-balance slider value.</summary>
    public float BalanceValue(int index) =>
        index >= 0 && index < _colourBalance.Length ? _colourBalance[index] : 0f;

    /// <summary>Gets one channel-mixer percentage.</summary>
    public float ChannelMixValue(int index) =>
        index >= 0 && index < _channelMix.Length ? _channelMix[index] : 0f;

    /// <summary>Gets the selective-colour range being edited, 0-8.</summary>
    public int SelectiveRange { get; private set; }

    /// <summary>Gets or sets the red-eye removal strength, 0-100.</summary>
    public int RedEyeStrength { get; private set; } = 80;

    /// <summary>Gets whether the perspective transform dialog is open.</summary>
    public bool IsPerspectiveOpen { get; private set; }

    /// <summary>Gets the layer being transformed.</summary>
    public int PerspectiveIndex { get; private set; } = -1;

    /// <summary>Gets the layer currently being transformed.</summary>
    public PaintLayer? PerspectiveLayer =>
        PerspectiveIndex >= 0 && PerspectiveIndex < Document.Layers.Count
            ? Document.Layers[PerspectiveIndex]
            : null;

    /// <summary>Gets one of the eight perspective corner offsets.</summary>
    public float PerspectiveValue(int index) =>
        index >= 0 && index < _perspective.Length ? _perspective[index] : 0f;

    /// <summary>Gets whether the mesh warp is active.</summary>
    public bool IsMeshWarpOpen { get; private set; }

    /// <summary>Gets the layer being mesh-warped.</summary>
    public int MeshWarpIndex { get; private set; } = -1;

    /// <summary>Gets the control point currently being dragged, or -1.</summary>
    public int MeshWarpActiveNode { get; private set; } = -1;

    /// <summary>Gets the layer being mesh-warped.</summary>
    public PaintLayer? MeshWarpLayer =>
        MeshWarpIndex >= 0 && MeshWarpIndex < Document.Layers.Count
            ? Document.Layers[MeshWarpIndex]
            : null;

    /// <summary>Gets the document-space position of a mesh control point.</summary>
    public (float X, float Y) MeshNodePosition(int node)
    {
        MeshRect(out float x, out float y, out float width, out float height);
        int column = node % MeshWarpOps.Points;
        int row = node / MeshWarpOps.Points;
        int index = node * 2;

        return (
            x + (column * width / (MeshWarpOps.Points - 1f)) + _mesh[index],
            y + (row * height / (MeshWarpOps.Points - 1f)) + _mesh[index + 1]);
    }

    /// <summary>Gets one of the four offsets for the selected colour range.</summary>
    public float SelectiveValue(int channel)
    {
        int index = (SelectiveRange * 4) + channel;
        return index >= 0 && index < _selectiveColour.Length ? _selectiveColour[index] : 0f;
    }

    /// <summary>Opens the colour tools dialog.</summary>
    public void ShowColourToolsDialog()
    {
        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        IsColourToolsOpen = true;
        ThresholdLevel = 128;
        GradientLow = 0xFF000000;
        GradientHigh = 0xFFFFFFFF;
        ReplaceFrom = Foreground;
        ReplaceTo = Background;
        ReplaceTolerance = 32;
        Array.Clear(_colourBalance);
        Array.Clear(_channelMix);
        _channelMix[0] = 100f;
        _channelMix[4] = 100f;
        _channelMix[8] = 100f;
        Array.Clear(_selectiveColour);
        SelectiveRange = 0;
        Status = "Colour tools";
        Changed();
    }

    /// <summary>Closes the colour tools dialog.</summary>
    public void CloseColourToolsDialog()
    {
        if (!IsColourToolsOpen)
        {
            return;
        }

        IsColourToolsOpen = false;
        Status = "Ready";
        Changed();
    }

    /// <summary>Updates the threshold level.</summary>
    public void SetThresholdLevel(string text)
    {
        if (int.TryParse(text, out int value))
        {
            ThresholdLevel = Math.Clamp(value, 0, 255);
            Changed();
        }
    }

    /// <summary>Updates one colour-balance slider.</summary>
    public void SetBalanceValue(int index, float value)
    {
        if (index < 0 || index >= _colourBalance.Length)
        {
            return;
        }

        _colourBalance[index] = Math.Clamp(value, -100f, 100f);
        Changed();
    }

    /// <summary>Sets the dark gradient-map colour.</summary>
    public void SetGradientLow(uint color)
    {
        GradientLow = color;
        Changed();
    }

    /// <summary>Sets the light gradient-map colour.</summary>
    public void SetGradientHigh(uint color)
    {
        GradientHigh = color;
        Changed();
    }

    /// <summary>Sets the colour being replaced.</summary>
    public void SetReplaceFrom(uint color)
    {
        ReplaceFrom = color;
        Changed();
    }

    /// <summary>Sets the replacement colour.</summary>
    public void SetReplaceTo(uint color)
    {
        ReplaceTo = color;
        Changed();
    }

    /// <summary>Sets the replace-colour tolerance.</summary>
    public void SetReplaceTolerance(string text)
    {
        if (int.TryParse(text, out int value))
        {
            ReplaceTolerance = Math.Clamp(value, 0, 255);
            Changed();
        }
    }

    /// <summary>Updates one channel-mixer percentage.</summary>
    public void SetChannelMixValue(int index, float value)
    {
        if (index < 0 || index >= _channelMix.Length)
        {
            return;
        }

        _channelMix[index] = Math.Clamp(value, -200f, 200f);
        Changed();
    }

    /// <summary>Restores the identity channel mix.</summary>
    public void ResetChannelMix()
    {
        Array.Clear(_channelMix);
        _channelMix[0] = 100f;
        _channelMix[4] = 100f;
        _channelMix[8] = 100f;
        Changed();
    }

    /// <summary>Selects which selective-colour range the sliders edit.</summary>
    public void SelectSelectiveRange(int range)
    {
        SelectiveRange = Math.Clamp(range, 0, 8);
        Changed();
    }

    /// <summary>Updates one offset for the selected colour range.</summary>
    public void SetSelectiveValue(int channel, float value)
    {
        int index = (SelectiveRange * 4) + channel;
        if (index < 0 || index >= _selectiveColour.Length)
        {
            return;
        }

        _selectiveColour[index] = Math.Clamp(value, -100f, 100f);
        Changed();
    }

    /// <summary>Clears every selective-colour offset.</summary>
    public void ResetSelectiveColour()
    {
        Array.Clear(_selectiveColour);
        Changed();
    }

    /// <summary>Applies the channel mixer to the active layer.</summary>
    public void ApplyChannelMix()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Channel Mixer");
        ColourTools.ChannelMix(layer, Selection, _channelMix);
        Status = "Channel mixer";
        Changed();
    }

    /// <summary>Applies the selective-colour offsets to the active layer.</summary>
    public void ApplySelectiveColour()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Selective Colour");
        ColourTools.SelectiveColour(layer, Selection, _selectiveColour);
        Status = "Selective colour";
        Changed();
    }

    /// <summary>Updates the red-eye removal strength.</summary>
    public void SetRedEyeStrength(string text)
    {
        if (int.TryParse(text, out int value))
        {
            RedEyeStrength = Math.Clamp(value, 0, 100);
            Changed();
        }
    }

    /// <summary>Neutralises red pupils inside the selection.</summary>
    public void ApplyRedEye()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Red-Eye Removal");
        ColourTools.RemoveRedEye(layer, Selection, RedEyeStrength / 100f);
        Status = "Red-eye removal";
        Changed();
    }

    /// <summary>Fills the selection from its surrounding pixels.</summary>
    public void ContentAwareFill()
    {
        if (Selection is not { IsEmpty: false })
        {
            Status = "Make a selection first";
            Changed();
            return;
        }

        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Content-Aware Fill");
        FillOps.ContentAware(layer, Selection);
        Status = "Content-aware fill";
        Changed();
    }

    // ---- mesh warp --------------------------------------------------------

    /// <summary>Opens the interactive mesh warp.</summary>
    public void ShowMeshWarpDialog()
    {
        PaintLayer layer = Document.Active;
        if (layer.IsAdjustment)
        {
            Status = "Adjustment layers cannot be warped";
            Changed();
            return;
        }

        if (layer.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        MeshWarpIndex = Document.ActiveIndex;
        _meshBase = layer.CapturePixels();
        _meshSelection = Selection;
        Array.Clear(_mesh);
        MeshWarpActiveNode = -1;
        IsMeshWarpOpen = true;
        PushUndo("Mesh Warp");
        Status = "Drag the mesh points";
        Changed();
    }

    /// <summary>Starts dragging a control point near a document position.</summary>
    public bool BeginMeshWarpDrag(float x, float y, float zoom)
    {
        if (!IsMeshWarpOpen)
        {
            return false;
        }

        float threshold = 12f / MathF.Max(zoom, 0.05f);
        float bestDistance = threshold * threshold;
        int best = -1;

        for (int node = 0; node < MeshWarpOps.Points * MeshWarpOps.Points; node++)
        {
            (float nodeX, float nodeY) = MeshNodePosition(node);
            float dx = nodeX - x;
            float dy = nodeY - y;
            float distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = node;
            }
        }

        MeshWarpActiveNode = best;
        return best >= 0;
    }

    /// <summary>Moves the active control point and refreshes the preview.</summary>
    public void UpdateMeshWarpDrag(float x, float y)
    {
        if (!IsMeshWarpOpen || MeshWarpActiveNode < 0)
        {
            return;
        }

        MeshRect(out float left, out float top, out float width, out float height);
        int column = MeshWarpActiveNode % MeshWarpOps.Points;
        int row = MeshWarpActiveNode / MeshWarpOps.Points;
        float baseX = left + (column * width / (MeshWarpOps.Points - 1f));
        float baseY = top + (row * height / (MeshWarpOps.Points - 1f));
        int index = MeshWarpActiveNode * 2;

        _mesh[index] = Math.Clamp(x - baseX, -500f, 500f);
        _mesh[index + 1] = Math.Clamp(y - baseY, -500f, 500f);
        ApplyMeshWarpPreview();
    }

    /// <summary>Ends the current control-point drag.</summary>
    public void EndMeshWarpDrag() => MeshWarpActiveNode = -1;

    /// <summary>Clears every control-point offset.</summary>
    public void ResetMeshWarp()
    {
        Array.Clear(_mesh);
        MeshWarpActiveNode = -1;
        ApplyMeshWarpPreview();
    }

    /// <summary>Keeps the warp and closes the mesh editor.</summary>
    public void CommitMeshWarp()
    {
        if (!IsMeshWarpOpen)
        {
            return;
        }

        IsMeshWarpOpen = false;
        MeshWarpIndex = -1;
        MeshWarpActiveNode = -1;
        _meshBase = null;
        _meshSelection = null;
        Status = "Mesh warp applied";
        Changed();
    }

    /// <summary>Restores the captured pixels and closes the mesh editor.</summary>
    public void CancelMeshWarp()
    {
        if (!IsMeshWarpOpen)
        {
            return;
        }

        if (_meshBase is not null && MeshWarpLayer is { } layer)
        {
            layer.Adopt((uint[])_meshBase.Clone());
        }

        IsMeshWarpOpen = false;
        MeshWarpIndex = -1;
        MeshWarpActiveNode = -1;
        _meshBase = null;
        _meshSelection = null;
        Document.MarkDirty();
        Status = "Mesh warp cancelled";
        Changed();
    }

    /// <summary>Rebuilds the mesh-warp preview from the captured pixels.</summary>
    private void ApplyMeshWarpPreview()
    {
        if (MeshWarpLayer is not { } layer || _meshBase is null)
        {
            return;
        }

        layer.Adopt((uint[])_meshBase.Clone());

        bool identity = true;
        for (int i = 0; i < _mesh.Length; i++)
        {
            if (MathF.Abs(_mesh[i]) > 0.01f)
            {
                identity = false;
                break;
            }
        }

        if (!identity && !MeshWarpOps.Transform(layer, _meshBase, _meshSelection, _mesh))
        {
            layer.Adopt((uint[])_meshBase.Clone());
        }

        Document.MarkDirty();
        Changed();
    }

    /// <summary>Gets the mesh rectangle in document pixels.</summary>
    private void MeshRect(out float x, out float y, out float width, out float height)
    {
        int documentWidth = Document.Width;
        int documentHeight = Document.Height;
        x = Math.Clamp(_meshSelection?.X ?? 0, 0, documentWidth);
        y = Math.Clamp(_meshSelection?.Y ?? 0, 0, documentHeight);
        width = Math.Clamp(
            _meshSelection?.Width ?? documentWidth,
            0,
            documentWidth - x);
        height = Math.Clamp(
            _meshSelection?.Height ?? documentHeight,
            0,
            documentHeight - y);
    }

    // ---- perspective transform --------------------------------------------

    /// <summary>Opens the four-corner perspective transform.</summary>
    public void ShowPerspectiveDialog()
    {
        PaintLayer layer = Document.Active;
        if (layer.IsAdjustment)
        {
            Status = "Adjustment layers cannot be transformed";
            Changed();
            return;
        }

        if (layer.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return;
        }

        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        PerspectiveIndex = Document.ActiveIndex;
        _perspectiveBase = layer.CapturePixels();
        _perspectiveSelection = Selection;
        Array.Clear(_perspective);
        IsPerspectiveOpen = true;
        PushUndo("Perspective Transform");
        Status = "Perspective transform";
        Changed();
    }

    /// <summary>Updates one corner offset and refreshes the preview.</summary>
    public void SetPerspectiveValue(int index, float value)
    {
        if (index < 0 || index >= _perspective.Length)
        {
            return;
        }

        _perspective[index] = Math.Clamp(value, -500f, 500f);
        ApplyPerspectivePreview();
    }

    /// <summary>Returns every corner offset to zero.</summary>
    public void ResetPerspective()
    {
        Array.Clear(_perspective);
        ApplyPerspectivePreview();
    }

    /// <summary>Keeps the transformed layer and closes the dialog.</summary>
    public void CommitPerspective()
    {
        if (!IsPerspectiveOpen)
        {
            return;
        }

        IsPerspectiveOpen = false;
        PerspectiveIndex = -1;
        _perspectiveBase = null;
        _perspectiveSelection = null;
        Status = "Perspective applied";
        Changed();
    }

    /// <summary>Restores the layer pixels and closes the dialog.</summary>
    public void CancelPerspective()
    {
        if (!IsPerspectiveOpen)
        {
            return;
        }

        if (_perspectiveBase is not null && PerspectiveLayer is { } layer)
        {
            layer.Adopt((uint[])_perspectiveBase.Clone());
        }

        IsPerspectiveOpen = false;
        PerspectiveIndex = -1;
        _perspectiveBase = null;
        _perspectiveSelection = null;
        Document.MarkDirty();
        Status = "Perspective cancelled";
        Changed();
    }

    /// <summary>Rebuilds the preview from the captured source pixels.</summary>
    private void ApplyPerspectivePreview()
    {
        if (PerspectiveLayer is not { } layer || _perspectiveBase is null)
        {
            return;
        }

        layer.Adopt((uint[])_perspectiveBase.Clone());

        bool identity = true;
        for (int i = 0; i < _perspective.Length; i++)
        {
            if (MathF.Abs(_perspective[i]) > 0.01f)
            {
                identity = false;
                break;
            }
        }

        if (!identity
            && !PerspectiveOps.Transform(layer, _perspectiveBase, _perspectiveSelection, _perspective))
        {
            layer.Adopt((uint[])_perspectiveBase.Clone());
        }

        Document.MarkDirty();
        Changed();
    }

    /// <summary>Applies a threshold to the active layer.</summary>
    public void ApplyThreshold()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Threshold");
        ColourTools.Threshold(layer, Selection, ThresholdLevel);
        Status = "Threshold " + ThresholdLevel;
        Changed();
    }

    /// <summary>Applies a two-colour gradient map to the active layer.</summary>
    public void ApplyGradientMap()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Gradient Map");
        ColourTools.GradientMap(layer, Selection, GradientLow, GradientHigh);
        Status = "Gradient map";
        Changed();
    }

    /// <summary>Replaces the selected colour across the active layer.</summary>
    public void ApplyColourReplace()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Replace Colour");
        ColourTools.ReplaceColour(
            layer,
            Selection,
            ReplaceFrom,
            ReplaceTo,
            ReplaceTolerance);
        Status = "Replaced colour";
        Changed();
    }

    /// <summary>Applies the colour-balance offsets to the active layer.</summary>
    public void ApplyColourBalance()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Colour Balance");
        ColourTools.ColourBalance(layer, Selection, _colourBalance);
        Status = "Colour balance";
        Changed();
    }

    /// <summary>Splits the active layer into red, green and blue layers.</summary>
    public void ApplySplitChannels()
    {
        if (!CanEditActiveLayer(out PaintLayer layer))
        {
            return;
        }

        PushUndo("Split Channels");
        ColourTools.SplitChannels(Document, layer, Selection);
        Status = "Split channels";
        Changed();
    }

    /// <summary>Returns the active layer when it accepts destructive edits.</summary>
    private bool CanEditActiveLayer(out PaintLayer layer)
    {
        layer = Document.Active;
        if (layer.IsAdjustment)
        {
            Status = "Adjustment layers cannot be edited this way";
            Changed();
            return false;
        }

        if (layer.Locked)
        {
            Status = "Layer is locked";
            Changed();
            return false;
        }

        if (layer.IsSmartObject)
        {
            Status = "Rasterize the smart object first";
            Changed();
            return false;
        }

        if (layer.IsShapeLayer)
        {
            Status = "Rasterize the shape first";
            Changed();
            return false;
        }

        if (layer.IsTextLayer)
        {
            Status = "Rasterize the text first";
            Changed();
            return false;
        }

        return true;
    }

    // ---- shape layers ------------------------------------------------------

    /// <summary>Adds a pen point, closing the path when the first point is hit.</summary>
    private void PenPointerDown(float x, float y)
    {
        if (_penPoints.Count >= 3)
        {
            PathNode first = _penPoints[0];
            float threshold = 8f / MathF.Max(Zoom, 0.05f);
            float dx = x - first.X;
            float dy = y - first.Y;
            if ((dx * dx) + (dy * dy) <= threshold * threshold)
            {
                FinishPenPath();
                return;
            }
        }

        if (_penPoints.Count > 0)
        {
            PathNode last = _penPoints[^1];
            float dx = x - last.X;
            float dy = y - last.Y;
            if ((dx * dx) + (dy * dy) < 1f)
            {
                return;
            }
        }

        _penPoints.Add(new PathNode(x, y));
        _penDragNode = _penPoints.Count - 1;
        _penPreviewX = x;
        _penPreviewY = y;
        Status = $"Pen: {_penPoints.Count} point{(_penPoints.Count == 1 ? string.Empty : "s")}; drag for a curve, click the first point to close";
        Changed();
    }

    /// <summary>Closes the pen path and creates a path shape layer.</summary>
    public void FinishPenPath()
    {
        if (_penPoints.Count < 3)
        {
            CancelPenPath();
            return;
        }

        PushUndo("Pen Path");

        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;
        foreach (PathNode node in _penPoints)
        {
            left = MathF.Min(left, node.X);
            top = MathF.Min(top, node.Y);
            right = MathF.Max(right, node.X);
            bottom = MathF.Max(bottom, node.Y);
        }

        List<PathNode> points = [.. _penPoints];
        LayerGroup? group = Document.Active.Group;
        PaintLayer layer = new(FreeLayerName("Path"), Document.Width, Document.Height)
        {
            Group = group,
        };

        layer.AdoptShape(new ShapeData
        {
            Kind = ShapeKind.Path,
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top,
            Fill = FillShapes,
            StrokeWidth = MathF.Max(1f, StrokeWidth),
            FillColor = Foreground,
            StrokeColor = Foreground,
            Points = points,
        });

        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        _penPoints.Clear();
        _penDragNode = -1;
        Document.MarkDirty();
        Status = "Path created";
        Changed();
    }

    /// <summary>Drops an in-progress pen path.</summary>
    public void CancelPenPath()
    {
        if (_penPoints.Count == 0)
        {
            return;
        }

        _penPoints.Clear();
        _penDragNode = -1;
        Status = "Pen cancelled";
        Changed();
    }

    /// <summary>Starts a shape layer from a drag on a shape tool.</summary>
    private void BeginShapeLayer(float x, float y)
    {
        CommitPreviews();
        PushUndo("Shape Layer");

        ShapeKind kind = Tool switch
        {
            PaintTool.Ellipse => ShapeKind.Ellipse,
            PaintTool.Line => ShapeKind.Line,
            _ => ShapeKind.Rectangle,
        };

        _shapePreviousIndex = Document.ActiveIndex;
        LayerGroup? group = Document.Active.Group;
        PaintLayer layer = new(
            FreeLayerName(kind + " Shape"),
            Document.Width,
            Document.Height)
        {
            Group = group,
        };

        layer.AdoptShape(new ShapeData
        {
            Kind = kind,
            X = x,
            Y = y,
            Fill = kind != ShapeKind.Line && FillShapes,
            StrokeWidth = MathF.Max(1f, kind == ShapeKind.Line ? BrushSize : StrokeWidth),
            FillColor = Foreground,
            StrokeColor = Foreground,
        });

        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        _shapeLayer = layer;
        _shapeStartX = x;
        _shapeStartY = y;
        Document.MarkDirty();
        Status = "Drag to size the shape";
        Changed();
    }

    /// <summary>Resizes the shape layer being dragged.</summary>
    private void UpdateShapeLayer(float x, float y)
    {
        if (_shapeLayer?.Shape is not { } shape)
        {
            return;
        }

        shape.X = MathF.Min(_shapeStartX, x);
        shape.Y = MathF.Min(_shapeStartY, y);
        shape.Width = MathF.Abs(x - _shapeStartX);
        shape.Height = MathF.Abs(y - _shapeStartY);
        _shapeLayer.RefreshShape();
        Document.MarkDirty();
        Status = $"{shape.Kind} {MathF.Round(shape.Width)} x {MathF.Round(shape.Height)}";
        Changed();
    }

    /// <summary>Finishes the shape drag, discarding degenerate shapes.</summary>
    private void FinishShapeLayer()
    {
        if (_shapeLayer is null)
        {
            return;
        }

        ShapeData? shape = _shapeLayer.Shape;
        if (shape is null || (shape.Width < 1f && shape.Height < 1f))
        {
            Document.Layers.Remove(_shapeLayer);
            Document.ActiveIndex = Math.Clamp(
                _shapePreviousIndex,
                0,
                Document.Layers.Count - 1);
            Status = "Shape cancelled";
        }
        else
        {
            Status = $"{shape.Kind} shape";
        }

        _shapeLayer = null;
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Opens the shape editor for the active shape layer.</summary>
    public void ShowShapeEditDialog()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsShapeLayer || layer.Shape is null)
        {
            Status = "Select a shape layer first";
            Changed();
            return;
        }

        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        ShapeEditIndex = Document.ActiveIndex;
        _shapeEditBackup = layer.Shape.Clone();
        IsShapeEditOpen = true;
        PushUndo("Edit Shape");
        Status = "Edit shape";
        Changed();
    }

    /// <summary>Rerenders the edited shape after a setting changes.</summary>
    public void TouchShape()
    {
        if (ShapeEditLayer is not { } layer || layer.Shape is null)
        {
            return;
        }

        layer.RefreshShape();
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Keeps the shape edits and closes the dialog.</summary>
    public void CommitShapeEdit()
    {
        if (!IsShapeEditOpen)
        {
            return;
        }

        IsShapeEditOpen = false;
        ShapeEditIndex = -1;
        _shapeEditBackup = null;
        Status = "Shape updated";
        Changed();
    }

    /// <summary>Restores the shape captured when the editor opened.</summary>
    public void CancelShapeEdit()
    {
        if (!IsShapeEditOpen)
        {
            return;
        }

        if (ShapeEditLayer is { } layer
            && layer.Shape is { } shape
            && _shapeEditBackup is { } backup)
        {
            shape.CopyFrom(backup);
            layer.RefreshShape();
            Document.MarkDirty();
        }

        IsShapeEditOpen = false;
        ShapeEditIndex = -1;
        _shapeEditBackup = null;
        Status = "Shape edit cancelled";
        Changed();
    }

    /// <summary>Bakes the active shape layer into raster pixels.</summary>
    public void RasterizeShape()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsShapeLayer)
        {
            Status = "Select a shape layer first";
            Changed();
            return;
        }

        PushUndo("Rasterize Shape");
        layer.RasterizeShape();
        Document.MarkDirty();
        Status = "Rasterized shape";
        Changed();
    }

    // ---- path node editing -------------------------------------------------

    /// <summary>Enters or leaves on-canvas path-node editing.</summary>
    public void TogglePathNodeEdit()
    {
        if (IsPathNodeEditing)
        {
            EndPathNodeEdit();
        }
        else
        {
            StartPathNodeEdit();
        }
    }

    /// <summary>Starts editing the active path layer's anchors and handles.</summary>
    public void StartPathNodeEdit()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsShapeLayer
            || layer.Shape is not { Kind: ShapeKind.Path, Points: { Count: >= 2 } })
        {
            Status = "Select a path layer first";
            Changed();
            return;
        }

        if (IsShapeEditOpen)
        {
            CommitShapeEdit();
        }

        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        _pathNodeEditIndex = Document.ActiveIndex;
        _selectedPathNode = -1;
        _pathNodeDrag = -1;
        _isPathNodeEditing = true;
        Status = "Drag an anchor or handle; click a segment to add a node";
        Changed();
    }

    /// <summary>Stops on-canvas path-node editing.</summary>
    public void EndPathNodeEdit()
    {
        if (!_isPathNodeEditing)
        {
            return;
        }

        _isPathNodeEditing = false;
        _pathNodeEditIndex = -1;
        _selectedPathNode = -1;
        _pathNodeDrag = -1;
        _pathUndoPushed = false;
        Status = "Path editing finished";
        Changed();
    }

    /// <summary>Removes the selected path anchor, if the path keeps three points.</summary>
    public void DeleteSelectedPathNode()
    {
        if (!_isPathNodeEditing
            || PathNodeShape is not { } shape
            || shape.Points is not { } points
            || _selectedPathNode < 0
            || _selectedPathNode >= points.Count)
        {
            Status = "Select a path node first";
            Changed();
            return;
        }

        if (points.Count <= 3)
        {
            Status = "A path needs at least three nodes";
            Changed();
            return;
        }

        PushUndo("Delete Path Node");
        points.RemoveAt(_selectedPathNode);
        _selectedPathNode = Math.Min(_selectedPathNode, points.Count - 1);
        _pathNodeDrag = -1;
        UpdatePathBounds(shape);
        RefreshPathLayer(PathNodeLayer!);
        Status = "Path node deleted";
        Changed();
    }

    /// <summary>Inserts a new anchor halfway along the selected node's outgoing segment.</summary>
    public void AddPathNode()
    {
        if (!_isPathNodeEditing
            || PathNodeShape is not { } shape
            || shape.Points is not { } points
            || points.Count < 2)
        {
            Changed();
            return;
        }

        if (_selectedPathNode < 0 || _selectedPathNode >= points.Count)
        {
            Status = "Select a path node first";
            Changed();
            return;
        }

        PushUndo("Add Path Node");
        PathNode a = points[_selectedPathNode];
        PathNode b = points[(_selectedPathNode + 1) % points.Count];
        (float x, float y) = CubicPoint(a, b, 0.5f);
        int insertAt = _selectedPathNode + 1;
        points.Insert(insertAt, new PathNode(x, y));
        _selectedPathNode = insertAt;
        UpdatePathBounds(shape);
        RefreshPathLayer(PathNodeLayer!);
        Status = "Path node added";
        Changed();
    }

    /// <summary>Handles a canvas press while path nodes are visible.</summary>
    private void PathNodePointerDown(float x, float y)
    {
        if (PathNodeShape is not { } shape || shape.Points is not { } points)
        {
            EndPathNodeEdit();
            return;
        }

        float threshold = 8f / MathF.Max(Zoom, 0.05f);
        float limit = threshold * threshold;

        int node = -1;
        int handle = 0;
        float best = limit;

        for (int i = 0; i < points.Count; i++)
        {
            PathNode candidate = points[i];
            if (!candidate.HasHandles)
            {
                continue;
            }

            float inDistance = DistanceSquared(candidate.InX, candidate.InY, x, y);
            if (inDistance < best)
            {
                best = inDistance;
                node = i;
                handle = -1;
            }

            float outDistance = DistanceSquared(candidate.OutX, candidate.OutY, x, y);
            if (outDistance < best)
            {
                best = outDistance;
                node = i;
                handle = 1;
            }
        }

        if (node < 0)
        {
            for (int i = 0; i < points.Count; i++)
            {
                float distance = DistanceSquared(points[i].X, points[i].Y, x, y);
                if (distance < best)
                {
                    best = distance;
                    node = i;
                    handle = 0;
                }
            }
        }

        if (node >= 0)
        {
            _selectedPathNode = node;
            _pathNodeDrag = node;
            _pathHandleDrag = handle;
            _pathUndoPushed = false;
            _pathDragX = x;
            _pathDragY = y;
            Status = handle == 0
                ? "Drag the anchor"
                : "Drag the Bezier handle";
            Changed();
            return;
        }

        if (TryInsertPathNode(points, x, y, threshold, out int inserted))
        {
            PushUndo("Add Path Node");
            _selectedPathNode = inserted;
            _pathNodeDrag = inserted;
            _pathHandleDrag = 0;
            _pathUndoPushed = true;
            UpdatePathBounds(shape);
            RefreshPathLayer(PathNodeLayer!);
            Status = "Path node added";
            Changed();
            return;
        }

        _selectedPathNode = -1;
        _pathNodeDrag = -1;
        Status = "Path editing: drag a node, or click a segment to add one";
        Changed();
    }

    /// <summary>Drags the selected anchor or one of its handles.</summary>
    private void PathNodePointerMove(float x, float y)
    {
        if (PathNodeShape is not { } shape
            || shape.Points is not { } points
            || _pathNodeDrag < 0
            || _pathNodeDrag >= points.Count)
        {
            return;
        }

        PathNode node = points[_pathNodeDrag];
        if (!_pathUndoPushed)
        {
            PushUndo("Edit Path");
            _pathUndoPushed = true;
        }

        float dx = x - _pathDragX;
        float dy = y - _pathDragY;

        if (_pathHandleDrag == 0)
        {
            node.X += dx;
            node.Y += dy;
            node.InX += dx;
            node.InY += dy;
            node.OutX += dx;
            node.OutY += dy;
        }
        else if (_pathHandleDrag < 0)
        {
            node.InX = x;
            node.InY = y;
            if (!InputState.Alt)
            {
                node.OutX = (node.X * 2f) - x;
                node.OutY = (node.Y * 2f) - y;
            }
        }
        else
        {
            node.OutX = x;
            node.OutY = y;
            if (!InputState.Alt)
            {
                node.InX = (node.X * 2f) - x;
                node.InY = (node.Y * 2f) - y;
            }
        }

        points[_pathNodeDrag] = node;
        _pathDragX = x;
        _pathDragY = y;
        UpdatePathBounds(shape);
        RefreshPathLayer(PathNodeLayer!);
        Status = _pathHandleDrag == 0
            ? $"Node {_pathNodeDrag + 1} at {MathF.Round(node.X)}, {MathF.Round(node.Y)}"
            : "Curve handle";
        Changed();
    }

    /// <summary>Ends an anchor or handle drag.</summary>
    private void PathNodePointerUp()
    {
        if (_pathNodeDrag < 0)
        {
            return;
        }

        _pathNodeDrag = -1;
        _pathHandleDrag = 0;
        _pathUndoPushed = false;
        Status = "Path updated";
        Changed();
    }

    /// <summary>Inserts a corner node on the nearest path segment.</summary>
    private static bool TryInsertPathNode(
        List<PathNode> points,
        float x,
        float y,
        float threshold,
        out int inserted)
    {
        inserted = -1;
        int segments = points.Count;
        float best = threshold * threshold;
        int bestSegment = -1;
        float bestX = 0f;
        float bestY = 0f;

        for (int i = 0; i < segments; i++)
        {
            PathNode a = points[i];
            PathNode b = points[(i + 1) % points.Count];

            for (int step = 1; step < 20; step++)
            {
                float t = step / 20f;
                (float px, float py) = CubicPoint(a, b, t);
                float distance = DistanceSquared(px, py, x, y);
                if (distance < best)
                {
                    best = distance;
                    bestSegment = i;
                    bestX = px;
                    bestY = py;
                }
            }
        }

        if (bestSegment < 0)
        {
            return false;
        }

        inserted = bestSegment + 1;
        points.Insert(inserted, new PathNode(bestX, bestY));
        return true;
    }

    /// <summary>Recomputes a path shape's bounding box from its anchors.</summary>
    private static void UpdatePathBounds(ShapeData shape)
    {
        if (shape.Points is not { Count: > 0 } points)
        {
            return;
        }

        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;

        foreach (PathNode node in points)
        {
            left = MathF.Min(left, node.X);
            top = MathF.Min(top, node.Y);
            right = MathF.Max(right, node.X);
            bottom = MathF.Max(bottom, node.Y);
        }

        shape.X = left;
        shape.Y = top;
        shape.Width = right - left;
        shape.Height = bottom - top;
    }

    /// <summary>Rerenders a path layer and any text attached to it.</summary>
    private void RefreshPathLayer(PaintLayer layer)
    {
        layer.RefreshShape();
        foreach (PaintLayer other in Document.Layers)
        {
            if (other.IsTextLayer
                && !string.IsNullOrEmpty(other.Text?.PathLayer)
                && string.Equals(
                    other.Text.PathLayer,
                    layer.Name,
                    StringComparison.Ordinal))
            {
                other.RefreshText(Document);
            }
        }

        Document.MarkDirty();
    }

    /// <summary>Evaluates a cubic segment at a parameter value.</summary>
    private static (float X, float Y) CubicPoint(PathNode a, PathNode b, float t)
    {
        float inv = 1f - t;
        float x = (inv * inv * inv * a.X)
            + (3f * inv * inv * t * a.OutX)
            + (3f * inv * t * t * b.InX)
            + (t * t * t * b.X);
        float y = (inv * inv * inv * a.Y)
            + (3f * inv * inv * t * a.OutY)
            + (3f * inv * t * t * b.InY)
            + (t * t * t * b.Y);
        return (x, y);
    }

    /// <summary>Squared distance between a point and a coordinate.</summary>
    private static float DistanceSquared(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx;
        float dy = ay - by;
        return (dx * dx) + (dy * dy);
    }

    // ---- text layers -------------------------------------------------------

    /// <summary>Opens the editor for the active text layer.</summary>
    public void ShowTextEditDialog()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsTextLayer || layer.Text is null)
        {
            Status = "Select a text layer first";
            Changed();
            return;
        }

        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        TextEditIndex = Document.ActiveIndex;
        _textEditBackup = layer.Text.Clone();
        IsTextEditOpen = true;
        PushUndo("Edit Text");
        Status = "Edit text";
        Changed();
    }

    /// <summary>Rerenders the edited text after a setting changes.</summary>
    public void TouchTextLayer()
    {
        if (TextEditLayer is not { } layer || layer.Text is null)
        {
            return;
        }

        layer.RefreshText(Document);
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Keeps the text edits and closes the dialog.</summary>
    public void CommitTextEdit()
    {
        if (!IsTextEditOpen)
        {
            return;
        }

        IsTextEditOpen = false;
        TextEditIndex = -1;
        _textEditBackup = null;
        Status = "Text updated";
        Changed();
    }

    /// <summary>Restores the text captured when the editor opened.</summary>
    public void CancelTextEdit()
    {
        if (!IsTextEditOpen)
        {
            return;
        }

        if (TextEditLayer is { } layer
            && layer.Text is { } text
            && _textEditBackup is { } backup)
        {
            text.CopyFrom(backup);
            layer.RefreshText(Document);
            Document.MarkDirty();
        }

        IsTextEditOpen = false;
        TextEditIndex = -1;
        _textEditBackup = null;
        Status = "Text edit cancelled";
        Changed();
    }

    /// <summary>Creates a text layer that follows the selected path layer.</summary>
    public void CreateTextOnPath()
    {
        PaintLayer pathLayer = Document.Active;
        if (!pathLayer.IsShapeLayer
            || pathLayer.Shape is not { Kind: ShapeKind.Path, Points: { Count: >= 2 } } path)
        {
            Status = "Select a path layer first";
            Changed();
            return;
        }

        PushUndo("Text on Path");
        TextData data = TextDataFromSession();
        data.Content = data.Content.Length > 0 ? data.Content : "Text on Path";
        data.Path = [.. path.Points];
        data.PathLayer = pathLayer.Name;
        data.PathClosed = path.Closed;
        data.PathOffset = 0f;
        data.Rotation = 0f;

        int insertAt = CreateTextLayer(data, "Path Text");
        TextEditIndex = insertAt;
        _textEditBackup = data.Clone();
        IsTextEditOpen = true;
        Status = "Text on path";
        Changed();
    }

    /// <summary>Bakes the active text layer into raster pixels.</summary>
    public void RasterizeText()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsTextLayer)
        {
            Status = "Select a text layer first";
            Changed();
            return;
        }

        PushUndo("Rasterize Text");
        layer.RasterizeText();
        Document.MarkDirty();
        Status = "Rasterized text";
        Changed();
    }

    /// <summary>Snapshots the current text-tool settings into editable text data.</summary>
    private TextData TextDataFromSession() => new()
    {
        Content = TextContent,
        FontIndex = TextFontIndex,
        Size = TextSize,
        Bold = TextBold,
        Italic = TextItalic,
        Align = TextAlignment,
        Color = Foreground,
        X = TextX,
        Y = TextY,
        Rotation = TextRotation,
        Outline = TextOutline,
        OutlineWidth = TextOutlineWidth,
        OutlineColor = TextOutlineColor,
        Shadow = TextShadow,
        ShadowX = TextShadowX,
        ShadowY = TextShadowY,
        ShadowBlur = TextShadowBlur,
        ShadowOpacity = TextShadowOpacity,
        ShadowColor = TextShadowColor,
    };

    /// <summary>Inserts a rendered text layer above the active layer.</summary>
    private int CreateTextLayer(TextData data, string name)
    {
        LayerGroup? group = Document.Active.Group;
        PaintLayer layer = new(FreeLayerName(name), Document.Width, Document.Height)
        {
            Group = group,
        };

        layer.AdoptText(data);
        int insertAt = Math.Clamp(Document.ActiveIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(insertAt, layer);
        Document.ActiveIndex = insertAt;
        Document.MarkDirty();
        return insertAt;
    }

    /// <summary>Uses the first line of new text as its layer name.</summary>
    private static string TextLayerName(TextData data)
    {
        string name = data.Content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (name.Length == 0)
        {
            return "Text";
        }

        return name.Length <= 24 ? name : name[..24].TrimEnd();
    }

    // ---- smart objects -----------------------------------------------------

    private float _smartBackupX;
    private float _smartBackupY;
    private float _smartBackupScaleX;
    private float _smartBackupScaleY;
    private float _smartBackupRotation;
    private bool _smartBackupFlipX;
    private bool _smartBackupFlipY;

    /// <summary>Gets whether the smart-object transform dialog is open.</summary>
    public bool IsSmartTransformOpen { get; private set; }

    /// <summary>Gets the smart-object layer being transformed.</summary>
    public int SmartTransformIndex { get; private set; } = -1;

    /// <summary>Gets the layer the smart transform dialog edits.</summary>
    public PaintLayer? SmartTransformLayer =>
        SmartTransformIndex >= 0 && SmartTransformIndex < Document.Layers.Count
            ? Document.Layers[SmartTransformIndex]
            : null;

    /// <summary>Converts the active raster layer into a smart object.</summary>
    public void ConvertToSmartObject()
    {
        PaintLayer layer = Document.Active;
        if (layer.IsAdjustment)
        {
            Status = "Adjustment layers cannot become smart objects";
            Changed();
            return;
        }

        if (layer.IsSmartObject)
        {
            Status = "Layer is already a smart object";
            Changed();
            return;
        }

        if (layer.IsTextLayer || layer.IsShapeLayer)
        {
            Status = "Rasterize the vector layer first";
            Changed();
            return;
        }

        PushUndo("Convert to Smart Object");
        layer.MakeSmart();
        Document.MarkDirty();
        Status = "Smart object";
        Changed();
    }

    /// <summary>Bakes the smart object's current pixels into a raster layer.</summary>
    public void RasterizeSmartObject()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsSmartObject)
        {
            Status = "Layer is not a smart object";
            Changed();
            return;
        }

        PushUndo("Rasterize Smart Object");
        layer.RasterizeSmart();
        Document.MarkDirty();
        Status = "Rasterized smart object";
        Changed();
    }

    /// <summary>Opens the smart-object transform controls.</summary>
    public void ShowSmartTransformDialog()
    {
        PaintLayer layer = Document.Active;
        if (!layer.IsSmartObject)
        {
            Status = "Convert the layer to a smart object first";
            Changed();
            return;
        }

        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        SmartTransformIndex = Document.ActiveIndex;
        _smartBackupX = layer.SmartX;
        _smartBackupY = layer.SmartY;
        _smartBackupScaleX = layer.SmartScaleX;
        _smartBackupScaleY = layer.SmartScaleY;
        _smartBackupRotation = layer.SmartRotation;
        _smartBackupFlipX = layer.SmartFlipX;
        _smartBackupFlipY = layer.SmartFlipY;
        IsSmartTransformOpen = true;
        PushUndo("Smart Object Transform");
        Status = "Smart object transform";
        Changed();
    }

    /// <summary>Gets one of the smart transform values for the dialog.</summary>
    public float SmartValue(int index) => SmartTransformLayer switch
    {
        null => 0f,
        { } layer => index switch
        {
            0 => layer.SmartX,
            1 => layer.SmartY,
            2 => layer.SmartScaleX * 100f,
            3 => layer.SmartScaleY * 100f,
            _ => layer.SmartRotation,
        },
    };

    /// <summary>Gets whether the smart object is mirrored horizontally.</summary>
    public bool SmartFlipX => SmartTransformLayer?.SmartFlipX == true;

    /// <summary>Gets whether the smart object is mirrored vertically.</summary>
    public bool SmartFlipY => SmartTransformLayer?.SmartFlipY == true;

    /// <summary>Updates one smart transform value and rerenders the preview.</summary>
    public void SetSmartValue(int index, float value)
    {
        if (SmartTransformLayer is not { } layer)
        {
            return;
        }

        switch (index)
        {
            case 0:
                layer.SmartX = Math.Clamp(value, -4000f, 4000f);
                break;
            case 1:
                layer.SmartY = Math.Clamp(value, -4000f, 4000f);
                break;
            case 2:
                layer.SmartScaleX = Math.Clamp(value / 100f, 0.01f, 8f);
                break;
            case 3:
                layer.SmartScaleY = Math.Clamp(value / 100f, 0.01f, 8f);
                break;
            default:
                layer.SmartRotation = Math.Clamp(value, -180f, 180f);
                break;
        }

        layer.RefreshSmart();
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Mirrors the smart object horizontally.</summary>
    public void ToggleSmartFlipX()
    {
        if (SmartTransformLayer is not { } layer)
        {
            return;
        }

        layer.SmartFlipX = !layer.SmartFlipX;
        layer.RefreshSmart();
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Mirrors the smart object vertically.</summary>
    public void ToggleSmartFlipY()
    {
        if (SmartTransformLayer is not { } layer)
        {
            return;
        }

        layer.SmartFlipY = !layer.SmartFlipY;
        layer.RefreshSmart();
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Restores the identity smart transform.</summary>
    public void ResetSmartTransform()
    {
        if (SmartTransformLayer is not { } layer)
        {
            return;
        }

        layer.SmartX = 0f;
        layer.SmartY = 0f;
        layer.SmartScaleX = 1f;
        layer.SmartScaleY = 1f;
        layer.SmartRotation = 0f;
        layer.SmartFlipX = false;
        layer.SmartFlipY = false;
        layer.RefreshSmart();
        Document.MarkDirty();
        Changed();
    }

    /// <summary>Keeps the transform and closes the dialog.</summary>
    public void CommitSmartTransform()
    {
        if (!IsSmartTransformOpen)
        {
            return;
        }

        IsSmartTransformOpen = false;
        SmartTransformIndex = -1;
        Status = "Smart object transformed";
        Changed();
    }

    /// <summary>Restores the transform captured when the dialog opened.</summary>
    public void CancelSmartTransform()
    {
        if (!IsSmartTransformOpen)
        {
            return;
        }

        if (SmartTransformLayer is { } layer)
        {
            layer.SmartX = _smartBackupX;
            layer.SmartY = _smartBackupY;
            layer.SmartScaleX = _smartBackupScaleX;
            layer.SmartScaleY = _smartBackupScaleY;
            layer.SmartRotation = _smartBackupRotation;
            layer.SmartFlipX = _smartBackupFlipX;
            layer.SmartFlipY = _smartBackupFlipY;
            layer.RefreshSmart();
            Document.MarkDirty();
        }

        IsSmartTransformOpen = false;
        SmartTransformIndex = -1;
        Status = "Smart transform cancelled";
        Changed();
    }

    // ---- layer styles ------------------------------------------------------

    /// <summary>Gets whether the layer style dialog is open.</summary>
    public bool IsLayerStyleOpen { get; private set; }

    /// <summary>Gets the layer index the style dialog edits.</summary>
    public int LayerStyleIndex { get; private set; } = -1;

    /// <summary>Gets the layer the style dialog edits.</summary>
    public PaintLayer? LayerStyleLayer =>
        LayerStyleIndex >= 0 && LayerStyleIndex < Document.Layers.Count
            ? Document.Layers[LayerStyleIndex]
            : null;

    /// <summary>Gets the style settings the dialog edits.</summary>
    public LayerStyle? LayerStyleTarget => LayerStyleLayer?.Style;

    /// <summary>Opens the style editor for a layer.</summary>
    public void ShowLayerStyleDialog(int index)
    {
        if (index < 0 || index >= Document.Layers.Count)
        {
            return;
        }

        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        LayerStyleIndex = index;
        IsLayerStyleOpen = true;
        PushUndo("Layer Style");
        Status = "Layer style: " + Document.Layers[index].Name;
        Changed();
    }

    /// <summary>Closes the style editor.</summary>
    public void CloseLayerStyleDialog()
    {
        if (!IsLayerStyleOpen)
        {
            return;
        }

        IsLayerStyleOpen = false;
        LayerStyleIndex = -1;
        Status = "Ready";
        Changed();
    }

    /// <summary>Marks the composite dirty after a style setting changed.</summary>
    public void TouchLayerStyle()
    {
        Document.MarkDirty();
        Changed();
    }

    // ---- batch processing --------------------------------------------------

    /// <summary>Gets whether the batch dialog is open.</summary>
    public bool IsBatchOpen { get; private set; }

    /// <summary>Gets the folder batch reads from.</summary>
    public string BatchSource { get; private set; } = string.Empty;

    /// <summary>Gets the folder batch writes to.</summary>
    public string BatchOutput { get; private set; } = string.Empty;

    /// <summary>Gets the resize percentage as typed.</summary>
    public string BatchScaleText { get; private set; } = "100";

    /// <summary>Gets the output format.</summary>
    public ImageFormat BatchFormat { get; private set; } = ImageFormat.Png;

    /// <summary>Gets the suffix appended to each output name.</summary>
    public string BatchSuffix { get; private set; } = "-edited";

    /// <summary>Gets whether existing files are replaced.</summary>
    public bool BatchOverwrite { get; private set; }

    /// <summary>Gets the last scan or run message.</summary>
    public string BatchStatus { get; private set; } = string.Empty;

    /// <summary>Gets how many images the last scan found.</summary>
    public int BatchFound { get; private set; }

    /// <summary>Gets the folder shortcuts offered by the batch dialog.</summary>
    public IReadOnlyList<FileLocation> BatchPlaces => _filePlaces;

    /// <summary>Opens the batch processing dialog.</summary>
    public void ShowBatchDialog()
    {
        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        IsBatchOpen = true;
        BatchSource = StartFolder();
        BatchOutput = BatchSource;
        BatchScaleText = "100";
        BatchFormat = ImageFormat.Png;
        BatchSuffix = "-edited";
        BatchOverwrite = false;
        RefreshFilePlaces();
        BatchRefreshCount();
        Status = "Batch processing";
        Changed();
    }

    /// <summary>Closes the batch dialog.</summary>
    public void CloseBatchDialog()
    {
        if (!IsBatchOpen)
        {
            return;
        }

        IsBatchOpen = false;
        BatchStatus = string.Empty;
        Status = "Ready";
        Changed();
    }

    /// <summary>Updates the source folder.</summary>
    public void BatchSetSource(string folder)
    {
        string previous = BatchSource;
        BatchSource = folder;
        if (BatchOutput.Trim().Length == 0
            || string.Equals(BatchOutput, previous, StringComparison.OrdinalIgnoreCase))
        {
            BatchOutput = folder;
        }

        BatchRefreshCount();
        Changed();
    }

    /// <summary>Updates the output folder.</summary>
    public void BatchSetOutput(string folder)
    {
        BatchOutput = folder;
        Changed();
    }

    /// <summary>Updates the resize percentage.</summary>
    public void BatchSetScale(string text)
    {
        BatchScaleText = text;
        Changed();
    }

    /// <summary>Chooses the output format.</summary>
    public void BatchSetFormat(ImageFormat format)
    {
        BatchFormat = format;
        Changed();
    }

    /// <summary>Updates the output name suffix.</summary>
    public void BatchSetSuffix(string suffix)
    {
        BatchSuffix = suffix;
        Changed();
    }

    /// <summary>Links or unlinks the source and output folders.</summary>
    public void ToggleBatchOverwrite()
    {
        BatchOverwrite = !BatchOverwrite;
        Changed();
    }

    /// <summary>Uses a well-known folder as the batch source.</summary>
    public void BatchUsePlace(FileLocation place)
    {
        BatchSetSource(place.Path);
    }

    /// <summary>Recounts the images in the source folder.</summary>
    public void BatchRefreshCount()
    {
        BatchFound = BatchProcessor.FindImages(BatchSource).Length;
        BatchStatus = BatchFound == 0
            ? "No supported images in that folder."
            : $"{BatchFound} image{(BatchFound == 1 ? string.Empty : "s")} ready.";
        Changed();
    }

    /// <summary>Runs the batch with the dialog's settings.</summary>
    public void RunBatch()
    {
        if (!IsBatchOpen)
        {
            return;
        }

        if (!int.TryParse(BatchScaleText.Trim(), out int scale) || scale < 1 || scale > 400)
        {
            BatchStatus = "Enter a scale between 1% and 400%.";
            Changed();
            return;
        }

        BatchOptions options = new()
        {
            SourceFolder = BatchSource,
            OutputFolder = BatchOutput,
            ScalePercent = scale,
            Format = BatchFormat,
            Suffix = BatchSuffix,
            Overwrite = BatchOverwrite,
        };

        BatchResult result = BatchProcessor.Run(options);
        BatchStatus = result.Message;
        Status = result.Message;
        Changed();
    }

    // ---- resize -----------------------------------------------------------

    /// <summary>Gets whether the resize dialog is open.</summary>
    public bool IsResizeOpen { get; private set; }

    /// <summary>Gets whether the dialog changes canvas size instead of scaling pixels.</summary>
    public bool ResizeIsCanvas { get; private set; }

    /// <summary>Gets the width typed into the resize dialog.</summary>
    public string ResizeWidthText { get; private set; } = string.Empty;

    /// <summary>Gets the height typed into the resize dialog.</summary>
    public string ResizeHeightText { get; private set; } = string.Empty;

    /// <summary>Gets whether the dialog keeps the document aspect ratio.</summary>
    public bool ResizeKeepAspect { get; private set; } = true;

    /// <summary>Gets the anchor used when the canvas grows or shrinks.</summary>
    public ResizeAnchor ResizeAnchor { get; private set; } = ResizeAnchor.Center;

    /// <summary>Gets the last problem reported by the resize dialog.</summary>
    public string ResizeError { get; private set; } = string.Empty;

    /// <summary>Gets the width of the document when the dialog opened.</summary>
    public int ResizeOriginalWidth { get; private set; }

    /// <summary>Gets the height of the document when the dialog opened.</summary>
    public int ResizeOriginalHeight { get; private set; }

    /// <summary>Opens the dialog that scales the artwork.</summary>
    public void ShowImageResizeDialog() => ShowResizeDialog(canvas: false);

    /// <summary>Opens the dialog that changes the canvas bounds.</summary>
    public void ShowCanvasResizeDialog() => ShowResizeDialog(canvas: true);

    /// <summary>Closes the resize dialog.</summary>
    public void CloseResizeDialog()
    {
        if (!IsResizeOpen)
        {
            return;
        }

        IsResizeOpen = false;
        ResizeError = string.Empty;
        Changed();
    }

    /// <summary>Updates the width field and, when linked, the height.</summary>
    public void ResizeSetWidth(string text)
    {
        ResizeWidthText = text;
        ResizeError = string.Empty;
        SyncAspect(fromWidth: true);
        Changed();
    }

    /// <summary>Updates the height field and, when linked, the width.</summary>
    public void ResizeSetHeight(string text)
    {
        ResizeHeightText = text;
        ResizeError = string.Empty;
        SyncAspect(fromWidth: false);
        Changed();
    }

    /// <summary>Links or unlinks the width and height fields.</summary>
    public void ToggleResizeAspect()
    {
        ResizeKeepAspect = !ResizeKeepAspect;
        if (ResizeKeepAspect)
        {
            SyncAspect(fromWidth: true);
        }

        Changed();
    }

    /// <summary>Chooses the anchor used by a canvas resize.</summary>
    public void SetResizeAnchor(ResizeAnchor anchor)
    {
        ResizeAnchor = anchor;
        Changed();
    }

    /// <summary>Applies the dialog's size to the document.</summary>
    public void ApplyResize()
    {
        if (!IsResizeOpen)
        {
            return;
        }

        if (!int.TryParse(ResizeWidthText.Trim(), out int width)
            || !int.TryParse(ResizeHeightText.Trim(), out int height)
            || width < 1
            || height < 1)
        {
            ResizeError = "Enter a whole number of pixels for both sides.";
            Changed();
            return;
        }

        width = Math.Clamp(width, 1, 8192);
        height = Math.Clamp(height, 1, 8192);
        if (width == Document.Width && height == Document.Height)
        {
            ResizeError = "The document is already that size.";
            Changed();
            return;
        }

        if (ResizeIsCanvas)
        {
            ResizeCanvas(width, height, ResizeAnchor);
        }
        else
        {
            ResizeImage(width, height);
        }

        IsResizeOpen = false;
        ResizeError = string.Empty;
        Changed();
    }

    /// <summary>Opens the resize dialog seeded from the current document size.</summary>
    private void ShowResizeDialog(bool canvas)
    {
        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        ResizeIsCanvas = canvas;
        ResizeOriginalWidth = Document.Width;
        ResizeOriginalHeight = Document.Height;
        ResizeWidthText = Document.Width.ToString();
        ResizeHeightText = Document.Height.ToString();
        ResizeKeepAspect = true;
        ResizeAnchor = ResizeAnchor.Center;
        ResizeError = string.Empty;
        IsResizeOpen = true;
        Status = canvas ? "Canvas size" : "Image size";
        Changed();
    }

    /// <summary>Derives the linked side from the edited one.</summary>
    private void SyncAspect(bool fromWidth)
    {
        if (!ResizeKeepAspect || ResizeOriginalWidth <= 0 || ResizeOriginalHeight <= 0)
        {
            return;
        }

        float aspect = ResizeOriginalWidth / (float)ResizeOriginalHeight;
        if (fromWidth && int.TryParse(ResizeWidthText.Trim(), out int widthValue) && widthValue > 0)
        {
            int derivedHeight = Math.Max(1, (int)MathF.Round(widthValue / aspect));
            if (derivedHeight <= 8192)
            {
                ResizeHeightText = derivedHeight.ToString();
            }
        }
        else if (!fromWidth
            && int.TryParse(ResizeHeightText.Trim(), out int heightValue)
            && heightValue > 0)
        {
            int derivedWidth = Math.Max(1, (int)MathF.Round(heightValue * aspect));
            if (derivedWidth <= 8192)
            {
                ResizeWidthText = derivedWidth.ToString();
            }
        }
    }

    /// <summary>Replaces the document and drops state tied to the old pixel grid.</summary>
    private void AdoptDocument(PaintDocument document)
    {
        FinishInteraction();
        Document = document;
        _tabs[_activeTab].Document = document;
        Selection = null;
        MaskEditing = false;
        CursorX = -1;
        CursorY = -1;
    }

    /// <summary>Scales every layer, mask and guide to a new size.</summary>
    private void ResizeImage(int width, int height)
    {
        CommitPreviews();
        FinishInteraction();
        PushUndo("Image Size");

        int oldWidth = Document.Width;
        int oldHeight = Document.Height;
        PaintDocument resized = new(width, height);
        Dictionary<LayerGroup, LayerGroup> groups = CopyGroups(Document, resized);

        foreach (PaintLayer source in Document.Layers)
        {
            PaintLayer layer = CreateResizedLayer(source, width, height);
            layer.Group = MapGroup(source.Group, groups);
            layer.Adopt(ImageOps.Scale(source.Pixels, source.Width, source.Height, width, height));
            if (source.Mask is { } mask)
            {
                layer.AdoptMask(ScaleMask(mask, source.Width, source.Height, width, height));
            }

            resized.Layers.Add(layer);
        }

        resized.ActiveIndex = Math.Clamp(Document.ActiveIndex, 0, resized.Layers.Count - 1);

        foreach (Guide guide in Document.Guides)
        {
            int limit = guide.Horizontal ? height : width;
            int basis = guide.Horizontal ? oldHeight : oldWidth;
            int position = basis <= 0 ? 0 : (guide.Position * limit) / basis;
            resized.Guides.Add(new Guide(guide.Horizontal, Math.Clamp(position, 0, limit)));
        }

        AdoptDocument(resized);
        Status = $"Image resized to {width} Ã— {height}";
    }

    /// <summary>Moves the existing pixels inside a new canvas size.</summary>
    private void ResizeCanvas(int width, int height, ResizeAnchor anchor)
    {
        CommitPreviews();
        FinishInteraction();
        PushUndo("Canvas Size");

        int oldWidth = Document.Width;
        int oldHeight = Document.Height;
        int offsetX = anchor switch
        {
            ResizeAnchor.TopLeft or ResizeAnchor.Left or ResizeAnchor.BottomLeft => 0,
            ResizeAnchor.TopRight or ResizeAnchor.Right or ResizeAnchor.BottomRight => width - oldWidth,
            _ => (width - oldWidth) / 2,
        };
        int offsetY = anchor switch
        {
            ResizeAnchor.TopLeft or ResizeAnchor.Top or ResizeAnchor.TopRight => 0,
            ResizeAnchor.BottomLeft or ResizeAnchor.Bottom or ResizeAnchor.BottomRight => height - oldHeight,
            _ => (height - oldHeight) / 2,
        };

        PaintDocument resized = new(width, height);
        Dictionary<LayerGroup, LayerGroup> groups = CopyGroups(Document, resized);

        foreach (PaintLayer source in Document.Layers)
        {
            PaintLayer layer = CreateResizedLayer(source, width, height);
            layer.Group = MapGroup(source.Group, groups);
            uint[] pixels = layer.Pixels;

            for (int y = 0; y < oldHeight; y++)
            {
                int targetY = y + offsetY;
                if (targetY < 0 || targetY >= height)
                {
                    continue;
                }

                for (int x = 0; x < oldWidth; x++)
                {
                    int targetX = x + offsetX;
                    if (targetX < 0 || targetX >= width)
                    {
                        continue;
                    }

                    pixels[(targetY * width) + targetX] = source.Pixels[(y * oldWidth) + x];
                }
            }

            if (source.Mask is { } mask)
            {
                byte[] targetMask = new byte[width * height];

                for (int y = 0; y < oldHeight; y++)
                {
                    int targetY = y + offsetY;
                    if (targetY < 0 || targetY >= height)
                    {
                        continue;
                    }

                    for (int x = 0; x < oldWidth; x++)
                    {
                        int targetX = x + offsetX;
                        if (targetX < 0 || targetX >= width)
                        {
                            continue;
                        }

                        targetMask[(targetY * width) + targetX] = mask[(y * oldWidth) + x];
                    }
                }

                layer.AdoptMask(targetMask);
            }

            resized.Layers.Add(layer);
        }

        resized.ActiveIndex = Math.Clamp(Document.ActiveIndex, 0, resized.Layers.Count - 1);

        foreach (Guide guide in Document.Guides)
        {
            int limit = guide.Horizontal ? height : width;
            int shift = guide.Horizontal ? offsetY : offsetX;
            resized.Guides.Add(new Guide(guide.Horizontal, Math.Clamp(guide.Position + shift, 0, limit)));
        }

        AdoptDocument(resized);
        Status = $"Canvas resized to {width} Ã— {height}";
    }

    /// <summary>Copies group metadata into a resized document.</summary>
    private static Dictionary<LayerGroup, LayerGroup> CopyGroups(
        PaintDocument source,
        PaintDocument target)
    {
        Dictionary<LayerGroup, LayerGroup> map = [];

        foreach (LayerGroup group in source.Groups)
        {
            LayerGroup copy = new(group.Name)
            {
                Visible = group.Visible,
                Opacity = group.Opacity,
                Expanded = group.Expanded,
            };

            map[group] = copy;
            target.Groups.Add(copy);
        }

        return map;
    }

    /// <summary>Maps a source group to its copy in the resized document.</summary>
    private static LayerGroup? MapGroup(
        LayerGroup? group,
        Dictionary<LayerGroup, LayerGroup> map) =>
        group is not null && map.TryGetValue(group, out LayerGroup? copy) ? copy : null;

    /// <summary>Creates an empty layer with the source layer's display settings.</summary>
    private static PaintLayer CreateResizedLayer(PaintLayer source, int width, int height)
    {
        PaintLayer layer = new(
            source.Name,
            width,
            height,
            source.IsBackground,
            source.IsSmartObject || source.IsShapeLayer || source.IsTextLayer
                ? LayerKind.Raster
                : source.Kind,
            source.Adjustment)
        {
            Visible = source.Visible,
            Opacity = source.Opacity,
            Blend = source.Blend,
            Locked = source.Locked,
            ClipToBelow = source.ClipToBelow,
        };

        if (source.IsAdjustment)
        {
            layer.CopyAdjustmentFrom(source);
        }

        layer.Style.CopyFrom(source.Style);
        return layer;
    }

    /// <summary>Scales a single-channel mask with bilinear sampling.</summary>
    private static byte[] ScaleMask(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        byte[] destination = new byte[width * height];
        float scaleX = sourceWidth / (float)width;
        float scaleY = sourceHeight / (float)height;

        for (int y = 0; y < height; y++)
        {
            float sampleY = ((y + 0.5f) * scaleY) - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(sampleY), 0, sourceHeight - 1);
            int y1 = Math.Clamp(y0 + 1, 0, sourceHeight - 1);
            float fy = sampleY - MathF.Floor(sampleY);

            for (int x = 0; x < width; x++)
            {
                float sampleX = ((x + 0.5f) * scaleX) - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(sampleX), 0, sourceWidth - 1);
                int x1 = Math.Clamp(x0 + 1, 0, sourceWidth - 1);
                float fx = sampleX - MathF.Floor(sampleX);

                float top = (source[(y0 * sourceWidth) + x0] * (1f - fx))
                    + (source[(y0 * sourceWidth) + x1] * fx);
                float bottom = (source[(y1 * sourceWidth) + x0] * (1f - fx))
                    + (source[(y1 * sourceWidth) + x1] * fx);
                destination[(y * width) + x] = (byte)Math.Clamp(
                    (int)MathF.Round((top * (1f - fy)) + (bottom * fy)),
                    0,
                    255);
            }
        }

        return destination;
    }

    // ---- file browser -----------------------------------------------------

    /// <summary>Gets whether the file browser modal is open.</summary>
    public bool IsFileDialogOpen { get; private set; }

    /// <summary>Gets whether the browser is opening or saving.</summary>
    public FileDialogMode FileDialogMode { get; private set; }

    /// <summary>Gets the folder currently listed.</summary>
    public string FileDialogFolder { get; private set; } = string.Empty;

    /// <summary>Gets the file name typed or selected.</summary>
    public string FileDialogName { get; private set; } = string.Empty;

    /// <summary>Gets the absolute path of the selected file, or an empty string.</summary>
    public string FileDialogSelected { get; private set; } = string.Empty;

    /// <summary>Gets the last problem reported by the browser, if any.</summary>
    public string FileDialogError { get; private set; } = string.Empty;

    /// <summary>Gets the format used when saving.</summary>
    public ImageFormat FileDialogFormat => _saveFormat;

    /// <summary>Gets whether the browser is handling a layered project file.</summary>
    public bool FileDialogProject { get; private set; }

    /// <summary>Gets whether the browser is exporting SVG.</summary>
    public bool FileDialogSvg { get; private set; }

    /// <summary>Gets whether the row context menu is open.</summary>
    public bool IsFileMenuOpen { get; private set; }

    /// <summary>Gets the context menu X in window CSS pixels.</summary>
    public float FileMenuX { get; private set; }

    /// <summary>Gets the context menu Y in window CSS pixels.</summary>
    public float FileMenuY { get; private set; }

    /// <summary>Gets whether the context menu is acting on a row.</summary>
    public bool FileMenuHasTarget => _fileMenuEntry is not null;

    /// <summary>Gets whether the context menu target is a folder.</summary>
    public bool FileMenuIsDirectory => _fileMenuEntry?.IsDirectory == true;

    /// <summary>Gets whether the rename/new-folder prompt is open.</summary>
    public bool IsFilePromptOpen { get; private set; }

    /// <summary>Gets the prompt title.</summary>
    public string FilePromptTitle { get; private set; } = string.Empty;

    /// <summary>Gets the name being edited in the prompt.</summary>
    public string FilePromptValue { get; private set; } = string.Empty;

    /// <summary>Gets whether the file clipboard holds an entry.</summary>
    public bool HasFileClipboard => _fileClipboard.Length > 0;

    /// <summary>Gets whether the clipboard entry was cut rather than copied.</summary>
    public bool FileClipboardIsCut => _fileClipboardCut;

    /// <summary>Gets the folder and file rows currently listed.</summary>
    public IReadOnlyList<FileEntry> FileDialogEntries => _fileEntries;

    /// <summary>Gets shortcuts to well-known folders.</summary>
    public IReadOnlyList<FileLocation> FileDialogPlaces => _filePlaces;

    /// <summary>Gets the absolute path the dialog would open or write.</summary>
    public string FileDialogTarget
    {
        get
        {
            if (FileDialogMode == FileDialogMode.Save)
            {
                string stem = FileNameStem(FileDialogName);
                if (stem.Length == 0)
                {
                    stem = FileNameStem(DocumentName);
                }

                string extension = FileDialogSvg
                    ? SvgExporter.Extension
                    : FileDialogProject
                        ? ProjectFile.Extension
                        : ImageExporter.Extension(_saveFormat);
                return Combine(FileDialogFolder, stem + extension);
            }

            string name = FileDialogName.Trim();
            return name.Length > 0 ? Combine(FileDialogFolder, name) : FileDialogFolder;
        }
    }

    /// <summary>Combines a folder and name, returning an empty string when invalid.</summary>
    private static string Combine(string folder, string name)
    {
        try
        {
            return Path.Combine(folder, name);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Opens the row context menu at a window position.</summary>
    /// <param name="entry">The row, or null for the empty list background.</param>
    /// <param name="x">Pointer X in window CSS pixels.</param>
    /// <param name="y">Pointer Y in window CSS pixels.</param>
    public void OpenFileContextMenu(FileEntry? entry, float x, float y)
    {
        _fileMenuEntry = entry;
        FileMenuX = MathF.Min(x, MathF.Max(0f, ViewportWidth - 200f));
        FileMenuY = MathF.Min(y, MathF.Max(0f, ViewportHeight - 220f));
        IsFileMenuOpen = true;
        Changed();
    }

    /// <summary>Closes the row context menu.</summary>
    public void CloseFileContextMenu()
    {
        if (!IsFileMenuOpen)
        {
            return;
        }

        IsFileMenuOpen = false;
        Changed();
    }

    /// <summary>Opens the row under the context menu, or navigates into a folder.</summary>
    public void FileMenuOpen()
    {
        if (_fileMenuEntry is not { } entry)
        {
            return;
        }

        IsFileMenuOpen = false;
        FileDialogActivate(entry);
    }

    /// <summary>Remembers the row as cut.</summary>
    public void FileMenuCut()
    {
        if (_fileMenuEntry is not { } entry)
        {
            return;
        }

        _fileClipboard = entry.FullPath;
        _fileClipboardCut = true;
        IsFileMenuOpen = false;
        Status = "Cut " + entry.Name;
        Changed();
    }

    /// <summary>Remembers the row as copied.</summary>
    public void FileMenuCopy()
    {
        if (_fileMenuEntry is not { } entry)
        {
            return;
        }

        _fileClipboard = entry.FullPath;
        _fileClipboardCut = false;
        IsFileMenuOpen = false;
        Status = "Copied " + entry.Name;
        Changed();
    }

    /// <summary>Pastes the clipboard entry into the current or target folder.</summary>
    public void FileMenuPaste()
    {
        string source = _fileClipboard;
        if (source.Length == 0)
        {
            return;
        }

        string folder = _fileMenuEntry is { IsDirectory: true } target
            ? target.FullPath
            : FileDialogFolder;

        try
        {
            bool directory = Directory.Exists(source);
            string name = Path.GetFileName(source);
            string destination = UniquePath(folder, name, directory);

            if (directory)
            {
                if (_fileClipboardCut)
                {
                    Directory.Move(source, destination);
                }
                else
                {
                    CopyDirectory(source, destination);
                }
            }
            else if (_fileClipboardCut)
            {
                File.Move(source, destination);
            }
            else
            {
                File.Copy(source, destination);
            }

            if (_fileClipboardCut)
            {
                _fileClipboard = string.Empty;
                _fileClipboardCut = false;
            }

            IsFileMenuOpen = false;
            FileDialogError = string.Empty;
            Status = "Pasted " + name;
            RefreshFileEntries();
        }
        catch (Exception error)
        {
            IsFileMenuOpen = false;
            FileDialogError = error.Message;
            Status = "Paste failed: " + error.Message;
        }

        Changed();
    }

    /// <summary>Starts renaming the row under the context menu.</summary>
    public void FileMenuRename()
    {
        if (_fileMenuEntry is not { } entry)
        {
            return;
        }

        _filePromptIsRename = true;
        _filePromptTarget = entry.FullPath;
        FilePromptTitle = "Rename " + (entry.IsDirectory ? "folder" : "file");
        FilePromptValue = entry.Name;
        IsFileMenuOpen = false;
        IsFilePromptOpen = true;
        FileDialogError = string.Empty;
        Changed();
    }

    /// <summary>Deletes the row under the context menu.</summary>
    public void FileMenuDelete()
    {
        if (_fileMenuEntry is not { } entry)
        {
            return;
        }

        try
        {
            if (entry.IsDirectory)
            {
                // Only empty folders are removed; this is a browser, not a
                // recursive delete tool.
                Directory.Delete(entry.FullPath);
            }
            else
            {
                File.Delete(entry.FullPath);
            }

            IsFileMenuOpen = false;
            FileDialogError = string.Empty;
            Status = "Deleted " + entry.Name;
            RefreshFileEntries();
        }
        catch (Exception error)
        {
            IsFileMenuOpen = false;
            FileDialogError = error.Message;
            Status = "Delete failed: " + error.Message;
        }

        Changed();
    }

    /// <summary>Opens the new-folder prompt.</summary>
    public void FileMenuNewFolder()
    {
        _filePromptIsRename = false;
        _filePromptTarget = string.Empty;
        FilePromptTitle = "New folder";
        FilePromptValue = "New Folder";
        IsFileMenuOpen = false;
        IsFilePromptOpen = true;
        FileDialogError = string.Empty;
        Changed();
    }

    /// <summary>Relists the current folder.</summary>
    public void FileMenuRefresh()
    {
        IsFileMenuOpen = false;
        RefreshFileEntries();
        Changed();
    }

    /// <summary>Updates the rename/new-folder prompt text.</summary>
    public void FilePromptSetValue(string value)
    {
        FilePromptValue = value;
        Changed();
    }

    /// <summary>Closes the prompt without applying it.</summary>
    public void FilePromptCancel()
    {
        if (!IsFilePromptOpen)
        {
            return;
        }

        IsFilePromptOpen = false;
        FilePromptValue = string.Empty;
        FileDialogError = string.Empty;
        Changed();
    }

    /// <summary>Applies the rename or new-folder prompt.</summary>
    public void FilePromptConfirm()
    {
        if (!IsFilePromptOpen)
        {
            return;
        }

        string name = FilePromptValue.Trim();
        if (name.Length == 0)
        {
            FileDialogError = "Enter a name.";
            Changed();
            return;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.IndexOf(Path.DirectorySeparatorChar) >= 0
            || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
        {
            FileDialogError = "The name contains characters that are not allowed.";
            Changed();
            return;
        }

        try
        {
            if (_filePromptIsRename)
            {
                string parent = Path.GetDirectoryName(_filePromptTarget) ?? FileDialogFolder;
                string destination = Path.Combine(parent, name);

                if (!string.Equals(_filePromptTarget, destination, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(destination) || Directory.Exists(destination))
                    {
                        FileDialogError = "A file or folder with that name already exists.";
                        Changed();
                        return;
                    }

                    if (Directory.Exists(_filePromptTarget))
                    {
                        Directory.Move(_filePromptTarget, destination);
                    }
                    else
                    {
                        File.Move(_filePromptTarget, destination);
                    }
                }

                Status = "Renamed to " + name;
            }
            else
            {
                string destination = UniquePath(FileDialogFolder, name, isDirectory: true);
                Directory.CreateDirectory(destination);
                Status = "Created " + Path.GetFileName(destination);
            }

            IsFilePromptOpen = false;
            FilePromptValue = string.Empty;
            FileDialogError = string.Empty;
            RefreshFileEntries();
        }
        catch (Exception error)
        {
            FileDialogError = error.Message;
            Status = "File action failed: " + error.Message;
        }

        Changed();
    }

    /// <summary>Returns a destination path that does not collide with an existing entry.</summary>
    private static string UniquePath(string folder, string name, bool isDirectory)
    {
        string destination = Path.Combine(folder, name);
        if (!File.Exists(destination) && !Directory.Exists(destination))
        {
            return destination;
        }

        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (int index = 2; index < 1000; index++)
        {
            destination = Path.Combine(folder, stem + " (" + index + ")" + extension);
            if (!File.Exists(destination) && !Directory.Exists(destination))
            {
                return destination;
            }
        }

        return Path.Combine(folder, stem + "-" + Guid.NewGuid().ToString("N")[..6] + extension);
    }

    /// <summary>Copies a folder tree without following reparse points.</summary>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }
    }

    /// <summary>Opens the browser to choose an image file.</summary>
    public void ShowOpenDialog() => ShowFileDialog(FileDialogMode.Open, project: false);

    /// <summary>Opens the browser to choose a layered project file.</summary>
    public void ShowOpenProjectDialog() => ShowFileDialog(FileDialogMode.Open, project: true);

    /// <summary>Opens the browser to choose where the current image is written.</summary>
    public void ShowSaveDialog() => ShowFileDialog(FileDialogMode.Save, project: false);

    /// <summary>Opens the browser to choose where the layered project is written.</summary>
    public void ShowSaveProjectDialog() => ShowFileDialog(FileDialogMode.Save, project: true);

    /// <summary>Opens the browser to export the document as SVG.</summary>
    public void ShowSaveSvgDialog() =>
        ShowFileDialog(FileDialogMode.Save, project: false, svg: true);

    /// <summary>Opens the browser filtered to SVG files.</summary>
    public void ShowOpenSvgDialog() =>
        ShowFileDialog(FileDialogMode.Open, project: false, svg: true);

    /// <summary>Opens the file browser in one of its modes.</summary>
    private void ShowFileDialog(FileDialogMode mode, bool project, bool svg = false)
    {
        Tip = null;
        OpenMenu = null;
        FinishInteraction();
        FileDialogMode = mode;
        FileDialogProject = project;
        FileDialogSvg = svg;
        IsFileDialogOpen = true;
        FileDialogSelected = string.Empty;
        FileDialogError = string.Empty;
        FileDialogFolder = StartFolder();
        FileDialogName = mode == FileDialogMode.Save
            ? FileNameStem(DocumentName)
                + (svg
                    ? SvgExporter.Extension
                    : project
                        ? ProjectFile.Extension
                        : ImageExporter.Extension(_saveFormat))
            : string.Empty;
        RefreshFilePlaces();
        RefreshFileEntries();
        Status = mode == FileDialogMode.Open
            ? project ? "Choose a project to open" : "Choose an image to open"
            : svg ? "Choose where to export the SVG"
            : project ? "Choose where to save the project" : "Choose where to save";
        Changed();
    }

    /// <summary>Closes the browser without touching the document.</summary>
    public void CloseFileDialog()
    {
        if (!IsFileDialogOpen)
        {
            return;
        }

        IsFileDialogOpen = false;
        FileDialogError = string.Empty;
        Status = "Ready";
        Changed();
    }

    /// <summary>Lists another folder, or reports why it cannot be listed.</summary>
    public void FileDialogNavigate(string path)
    {
        try
        {
            string full = Path.GetFullPath(path.Trim().Trim('"'));
            if (!Directory.Exists(full))
            {
                FileDialogError = "Folder not found: " + full;
                Changed();
                return;
            }

            FileDialogFolder = full;
            FileDialogSelected = string.Empty;
            FileDialogError = string.Empty;
            RefreshFileEntries();
            Changed();
        }
        catch (Exception error)
        {
            FileDialogError = error.Message;
            Changed();
        }
    }

    /// <summary>Lists the parent of the current folder.</summary>
    public void FileDialogGoUp()
    {
        DirectoryInfo? parent = Directory.GetParent(FileDialogFolder);
        if (parent is not null)
        {
            FileDialogNavigate(parent.FullName);
        }
    }

    /// <summary>Lists one of the well-known folders.</summary>
    public void FileDialogUsePlace(FileLocation place) => FileDialogNavigate(place.Path);

    /// <summary>Handles a click on a row: folders navigate, files are selected.</summary>
    public void FileDialogActivate(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            FileDialogNavigate(entry.FullPath);
            return;
        }

        FileDialogName = entry.Name;
        FileDialogSelected = entry.FullPath;
        FileDialogError = string.Empty;
        Changed();
    }

    /// <summary>Updates the file name field.</summary>
    public void FileDialogSetName(string name)
    {
        FileDialogName = name;
        FileDialogSelected = string.Empty;
        FileDialogError = string.Empty;
        Changed();
    }

    /// <summary>Chooses the output format and fixes the name's extension.</summary>
    public void FileDialogSetFormat(ImageFormat format)
    {
        if (FileDialogProject)
        {
            return;
        }

        _saveFormat = format;
        string stem = FileNameStem(FileDialogName);
        if (stem.Length == 0)
        {
            stem = FileNameStem(DocumentName);
        }

        FileDialogName = stem + ImageExporter.Extension(format);
        FileDialogError = string.Empty;
        Changed();
    }

    /// <summary>Opens or saves according to the browser mode.</summary>
    public void ConfirmFileDialog()
    {
        if (!IsFileDialogOpen)
        {
            return;
        }

        if (FileDialogSvg && FileDialogMode == FileDialogMode.Save)
        {
            ConfirmSvgExport();
            return;
        }

        if (FileDialogProject)
        {
            ConfirmProjectFile();
            return;
        }

        if (FileDialogMode == FileDialogMode.Open)
        {
            string name = FileDialogName.Trim();
            if (name.Length == 0)
            {
                FileDialogError = "Choose an image to open.";
                Changed();
                return;
            }

            string path;
            try
            {
                path = Path.Combine(FileDialogFolder, name);
            }
            catch (Exception error)
            {
                FileDialogError = error.Message;
                Changed();
                return;
            }

            if (!File.Exists(path))
            {
                FileDialogError = "File not found: " + name;
                Changed();
                return;
            }

            OpenImageFile(path);
            return;
        }

        string stem = FileNameStem(FileDialogName);
        if (stem.Length == 0)
        {
            FileDialogError = "Enter a file name.";
            Changed();
            return;
        }

        string target = Path.Combine(FileDialogFolder, stem + ImageExporter.Extension(_saveFormat));
        SaveImageFile(target);
    }

    /// <summary>Writes the document as an SVG export.</summary>
    private void ConfirmSvgExport()
    {
        string stem = FileNameStem(FileDialogName);
        if (stem.Length == 0)
        {
            FileDialogError = "Enter a file name.";
            Changed();
            return;
        }

        string target = Path.Combine(FileDialogFolder, stem + SvgExporter.Extension);

        try
        {
            SvgExporter.Save(target, Document);
            _lastFolder = Path.GetDirectoryName(target) ?? _lastFolder;
            IsFileDialogOpen = false;
            FileDialogError = string.Empty;
            Status = "Exported " + target;
        }
        catch (Exception error)
        {
            FileDialogError = error.Message;
            Status = "SVG export failed: " + error.Message;
        }

        Changed();
    }

    /// <summary>Opens or writes a layered project file.</summary>
    private void ConfirmProjectFile()
    {
        if (FileDialogMode == FileDialogMode.Open)
        {
            string name = FileDialogName.Trim();
            if (name.Length == 0)
            {
                FileDialogError = "Choose a project to open.";
                Changed();
                return;
            }

            string path = Combine(FileDialogFolder, name);
            if (path.Length == 0 || !File.Exists(path))
            {
                FileDialogError = "File not found: " + name;
                Changed();
                return;
            }

            try
            {
                PaintDocument document = ProjectFile.Load(path);
                _lastFolder = Path.GetDirectoryName(path) ?? _lastFolder;
                IsFileDialogOpen = false;
                FileDialogError = string.Empty;
                AddDocument(
                    document,
                    FreeName(Path.GetFileNameWithoutExtension(path)),
                    "Opened project " + path);
            }
            catch (Exception error)
            {
                FileDialogError = error.Message;
                Status = "Open project failed: " + error.Message;
                Changed();
            }

            return;
        }

        string stem = FileNameStem(FileDialogName);
        if (stem.Length == 0)
        {
            FileDialogError = "Enter a file name.";
            Changed();
            return;
        }

        string target = Path.Combine(FileDialogFolder, stem + ProjectFile.Extension);

        try
        {
            ProjectFile.Save(target, Document);
            _lastFolder = Path.GetDirectoryName(target) ?? _lastFolder;
            NameActiveDocument(Path.GetFileName(target));
            IsFileDialogOpen = false;
            FileDialogError = string.Empty;
            Status = "Saved project " + target;
        }
        catch (Exception error)
        {
            FileDialogError = error.Message;
            Status = "Save project failed: " + error.Message;
        }

        Changed();
    }

    /// <summary>Resolves the file name without its extension.</summary>
    private static string FileNameStem(string name)
    {
        string filename = Path.GetFileName(name.Trim());
        string stem = Path.GetFileNameWithoutExtension(filename);
        return stem.Length > 0 ? stem : filename;
    }

    /// <summary>Picks the folder the browser starts in.</summary>
    private string StartFolder()
    {
        if (_lastFolder.Length > 0 && Directory.Exists(_lastFolder))
        {
            return _lastFolder;
        }

        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (pictures.Length > 0 && Directory.Exists(pictures))
        {
            return pictures;
        }

        return Environment.CurrentDirectory;
    }

    /// <summary>Rebuilds the quick-folder shortcuts.</summary>
    private void RefreshFilePlaces()
    {
        _filePlaces.Clear();
        AddPlace("Pictures", Environment.SpecialFolder.MyPictures);
        AddPlace("Desktop", Environment.SpecialFolder.Desktop);
        AddPlace("Documents", Environment.SpecialFolder.MyDocuments);
        AddPlace("Home", Environment.SpecialFolder.UserProfile);

        string downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        if (Directory.Exists(downloads))
        {
            _filePlaces.Add(new FileLocation("Downloads", downloads));
        }
    }

    /// <summary>Adds a shortcut when the folder exists.</summary>
    private void AddPlace(string label, Environment.SpecialFolder special)
    {
        string path = Environment.GetFolderPath(special);
        if (path.Length > 0 && Directory.Exists(path))
        {
            _filePlaces.Add(new FileLocation(label, path));
        }
    }

    /// <summary>Lists the folders and supported images in the current folder.</summary>
    private void RefreshFileEntries()
    {
        _fileEntries.Clear();
        FileDialogError = string.Empty;
        List<FileEntry> folders = [];
        List<FileEntry> files = [];

        try
        {
            DirectoryInfo folder = new(FileDialogFolder);
            foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo directory)
                {
                    folders.Add(new FileEntry(directory.Name, directory.FullName, true));
                }
                else if (entry is FileInfo file
                    && (FileDialogSvg
                        ? file.Name.EndsWith(
                            SvgExporter.Extension,
                            StringComparison.OrdinalIgnoreCase)
                        : FileDialogProject
                            ? ProjectFile.IsProject(file.Name)
                            : ImageLoader.IsImage(file.Name)
                                || SvgImporter.IsSvg(file.Name)))
                {
                    files.Add(new FileEntry(file.Name, file.FullName, false));
                }
            }
        }
        catch (Exception error)
        {
            FileDialogError = error.Message;
        }

        folders.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        files.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

        const int MaxEntries = 500;
        _fileEntries.AddRange(folders);
        _fileEntries.AddRange(files);

        if (_fileEntries.Count > MaxEntries)
        {
            _fileEntries.RemoveRange(MaxEntries, _fileEntries.Count - MaxEntries);
            FileDialogError = "Showing the first " + MaxEntries + " entries.";
        }
    }

    /// <summary>Loads an image file into a new tab.</summary>
    private void OpenImageFile(string path)
    {
        try
        {
            if (SvgImporter.IsSvg(path))
            {
                PaintDocument imported = SvgImporter.Load(path);
                _lastFolder = Path.GetDirectoryName(path) ?? _lastFolder;
                IsFileDialogOpen = false;
                FileDialogError = string.Empty;
                AddDocument(
                    imported,
                    FreeName(Path.GetFileNameWithoutExtension(path)),
                    "Imported " + path);
                return;
            }

            (int width, int height, uint[] pixels) = ImageLoader.Load(path);

            if (width > 8192 || height > 8192)
            {
                FileDialogError = "The image is larger than 8192 pixels on a side.";
                Changed();
                return;
            }

            PaintDocument document = new(width, height);
            PaintLayer layer = new(Path.GetFileNameWithoutExtension(path), width, height);
            layer.Adopt(pixels);
            document.Layers.Add(layer);
            document.ActiveIndex = 0;
            document.MarkDirty();

            _lastFolder = Path.GetDirectoryName(path) ?? _lastFolder;
            IsFileDialogOpen = false;
            FileDialogError = string.Empty;
            AddDocument(document, FreeName(Path.GetFileNameWithoutExtension(path)), "Opened " + path);
        }
        catch (Exception error)
        {
            FileDialogError = error.Message;
            Status = "Open failed: " + error.Message;
            Changed();
        }
    }

    /// <summary>Writes the composite to an explicit path.</summary>
    private void SaveImageFile(string path)
    {
        try
        {
            CommitPreviews();
            Document.MarkDirty();
            File.WriteAllBytes(
                path,
                ImageExporter.Encode(
                    Document.Composite,
                    Document.Width,
                    Document.Height,
                    _saveFormat));

            _lastFolder = Path.GetDirectoryName(path) ?? _lastFolder;
            NameActiveDocument(Path.GetFileName(path));
            IsFileDialogOpen = false;
            FileDialogError = string.Empty;
            Status = "Saved " + path;
        }
        catch (Exception error)
        {
            FileDialogError = error.Message;
            Status = "Save failed: " + error.Message;
        }

        Changed();
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
        _quickSelecting = false;
        _quickMask = null;
        _selectionBase = null;
        _selectionCombine = 0;
        _moving = false;
        _movingSelection = false;
        _resizingSelection = false;
        _moveChanged = false;
        _moveStart = null;
        _movePixels = null;
        _strokeLayer = null;
        _strokeBase = null;
        _maskBase = null;
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

    /// <summary>Perceptual luminance used by the healing brush.</summary>
    private static int Luminance(uint color) =>
        ((Rgba.R(color) * 77) + (Rgba.G(color) * 150) + (Rgba.B(color) * 29)) >> 8;

    /// <summary>Trims a message to something the options bar can show inline.</summary>
    private static string Shorten(string message)
    {
        string single = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return single.Length <= 72 ? single : single[..69] + "...";
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
        PushUndo(Tool switch
        {
            PaintTool.Eraser => "Erase",
            PaintTool.CloneStamp => "Clone Stamp",
            PaintTool.HealingBrush => "Heal",
            PaintTool.DodgeBurn => DodgeMode ? "Dodge" : "Burn",
            _ => "Brush",
        });
        _strokeLayer = layer;
        _strokeBase = layer.CapturePixels();
        _maskBase = MaskEditing && layer.HasMask ? layer.CaptureMask() : null;
        _cloneOffsetX = CloneSourceX >= 0 ? CloneSourceX - x : 0;
        _cloneOffsetY = CloneSourceY >= 0 ? CloneSourceY - y : 0;
        if (HasCloneSource && Tool is PaintTool.CloneStamp or PaintTool.HealingBrush)
        {
            CloneMarkerX = x + _cloneOffsetX;
            CloneMarkerY = y + _cloneOffsetY;
        }
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
        bool cloning = Tool is PaintTool.CloneStamp or PaintTool.HealingBrush;
        bool dodging = Tool == PaintTool.DodgeBurn;
        int brushAlpha = Alpha;
        if (brushAlpha <= 0)
        {
            return;
        }

        if (MaskEditing && _maskBase is not null && _strokeLayer.Mask is { } maskTarget)
        {
            Array.Copy(_maskBase, maskTarget, _maskBase.Length);

            for (int y = _maskY0; y <= _maskY1; y++)
            {
                int row = y * _strokeLayer.Width;
                for (int x = _maskX0; x <= _maskX1; x++)
                {
                    int index = row + x;
                    int coverage = _strokeMask[index];
                    if (coverage == 0)
                    {
                        continue;
                    }

                    int amount = coverage * brushAlpha / 255;
                    int value = _maskBase[index];
                    maskTarget[index] = erasing
                        ? (byte)(value * (255 - amount) / 255)
                        : (byte)(value + ((255 - value) * amount / 255));
                }
            }

            Document.MarkDirty();
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

                if (cloning)
                {
                    int sourceX = x + _cloneOffsetX;
                    int sourceY = y + _cloneOffsetY;
                    if (sourceX < 0 || sourceY < 0 || sourceX >= width || sourceY >= _strokeLayer.Height)
                    {
                        pixels[index] = basePixel;
                        continue;
                    }

                    uint sampled = _strokeBase[(sourceY * width) + sourceX];
                    if (Rgba.A(sampled) == 0)
                    {
                        pixels[index] = basePixel;
                        continue;
                    }

                    if (Tool == PaintTool.HealingBrush)
                    {
                        int destination = Luminance(basePixel);
                        int source = Luminance(sampled);
                        int delta = (destination - source) / 2;
                        sampled = Rgba.Pack(
                            Rgba.A(sampled),
                            Math.Clamp(Rgba.R(sampled) + delta, 0, 255),
                            Math.Clamp(Rgba.G(sampled) + delta, 0, 255),
                            Math.Clamp(Rgba.B(sampled) + delta, 0, 255));
                    }

                    int sampledAlpha = Rgba.A(sampled) * amount / 255;
                    pixels[index] = Rgba.Over(basePixel, sampled, sampledAlpha);
                    continue;
                }

                if (dodging)
                {
                    int red = Rgba.R(basePixel);
                    int green = Rgba.G(basePixel);
                    int blue = Rgba.B(basePixel);

                    if (DodgeMode)
                    {
                        red += (255 - red) * amount / 255;
                        green += (255 - green) * amount / 255;
                        blue += (255 - blue) * amount / 255;
                    }
                    else
                    {
                        red = red * (255 - amount) / 255;
                        green = green * (255 - amount) / 255;
                        blue = blue * (255 - amount) / 255;
                    }

                    pixels[index] = Rgba.Pack(Rgba.A(basePixel), red, green, blue);
                    continue;
                }

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
        private readonly GroupState[] _groups;
        private readonly int _active;
        private readonly Selection? _selection;
        private readonly int _width;
        private readonly int _height;

        private DocumentSnapshot(
            Frame[] frames,
            GroupState[] groups,
            int active,
            Selection? selection,
            string name,
            int width,
            int height)
        {
            _frames = frames;
            _groups = groups;
            _active = active;
            _selection = selection;
            _width = width;
            _height = height;
            Name = name;
        }

        /// <summary>Gets the name of the step this snapshot represents.</summary>
        public string Name { get; }

        /// <summary>Gets the document width this snapshot was taken at.</summary>
        public int Width => _width;

        /// <summary>Gets the document height this snapshot was taken at.</summary>
        public int Height => _height;

        /// <summary>Copies every layer's pixels and the selection around them.</summary>
        public static DocumentSnapshot Capture(PaintDocument document, Selection? selection, string name)
        {
            Frame[] frames = new Frame[document.Layers.Count];
            for (int i = 0; i < frames.Length; i++)
            {
                PaintLayer layer = document.Layers[i];
                frames[i] = new Frame(
                    layer,
                    layer.Group,
                    layer.CapturePixels(),
                    layer.CaptureMask(),
                    layer.Visible,
                    layer.Opacity,
                    layer.Blend,
                    layer.Locked,
                    layer.ClipToBelow,
                    layer.Adjustment,
                    layer.AdjustmentBlack,
                    layer.AdjustmentWhite,
                    layer.AdjustmentGamma,
                    layer.IsAdjustment ? [.. layer.AdjustmentCurve] : null,
                    layer.Style.Clone(),
                    layer.Kind,
                    layer.SmartSource is { } smart ? (uint[])smart.Clone() : null,
                    layer.SmartSourceWidth,
                    layer.SmartSourceHeight,
                    layer.SmartX,
                    layer.SmartY,
                    layer.SmartScaleX,
                    layer.SmartScaleY,
                    layer.SmartRotation,
                    layer.SmartFlipX,
                    layer.SmartFlipY,
                    layer.Shape?.Clone(),
                    layer.Text?.Clone());
            }

            GroupState[] groups = new GroupState[document.Groups.Count];
            for (int i = 0; i < groups.Length; i++)
            {
                LayerGroup group = document.Groups[i];
                groups[i] = new GroupState(
                    group,
                    group.Name,
                    group.Visible,
                    group.Opacity,
                    group.Expanded);
            }

            return new DocumentSnapshot(
                frames,
                groups,
                document.ActiveIndex,
                selection,
                name,
                document.Width,
                document.Height);
        }

        /// <summary>Restores the copied layers and their order, returning the selection.</summary>
        public Selection? Restore(PaintDocument document)
        {
            document.Layers.Clear();
            document.Groups.Clear();

            foreach (GroupState state in _groups)
            {
                state.Group.Name = state.Name;
                state.Group.Visible = state.Visible;
                state.Group.Opacity = state.Opacity;
                state.Group.Expanded = state.Expanded;
                document.Groups.Add(state.Group);
            }

            foreach (Frame frame in _frames)
            {
                frame.Layer.Group = frame.Group;
                frame.Layer.Adopt(frame.Pixels);
                frame.Layer.AdoptMask(frame.Mask);
                frame.Layer.Visible = frame.Visible;
                frame.Layer.Opacity = frame.Opacity;
                frame.Layer.Blend = frame.Blend;
                frame.Layer.Locked = frame.Locked;
                frame.Layer.ClipToBelow = frame.ClipToBelow;
                frame.Layer.Style.CopyFrom(frame.Style);
                frame.Layer.RestoreSmart(
                    frame.SmartSource,
                    frame.SmartSourceWidth,
                    frame.SmartSourceHeight,
                    frame.SmartX,
                    frame.SmartY,
                    frame.SmartScaleX,
                    frame.SmartScaleY,
                    frame.SmartRotation,
                    frame.SmartFlipX,
                    frame.SmartFlipY,
                    frame.Kind);
                frame.Layer.RestoreShape(frame.Shape, frame.Kind);
                frame.Layer.RestoreText(frame.Text, frame.Kind);
                frame.Layer.Adjustment = frame.Adjustment;
                frame.Layer.AdjustmentBlack = frame.AdjustmentBlack;
                frame.Layer.AdjustmentWhite = frame.AdjustmentWhite;
                frame.Layer.AdjustmentGamma = frame.AdjustmentGamma;
                if (frame.Curve is not null)
                {
                    frame.Layer.AdjustmentCurve.Clear();
                    frame.Layer.AdjustmentCurve.AddRange(frame.Curve);
                }

                document.Layers.Add(frame.Layer);
            }

            document.ActiveIndex = Math.Clamp(_active, 0, document.Layers.Count - 1);
            document.MarkDirty();
            return _selection;
        }

        private readonly record struct GroupState(
            LayerGroup Group,
            string Name,
            bool Visible,
            float Opacity,
            bool Expanded);

        private readonly record struct Frame(
            PaintLayer Layer,
            LayerGroup? Group,
            uint[] Pixels,
            byte[]? Mask,
            bool Visible,
            float Opacity,
            BlendMode Blend,
            bool Locked,
            bool ClipToBelow,
            AdjustmentKind Adjustment,
            float AdjustmentBlack,
            float AdjustmentWhite,
            float AdjustmentGamma,
            List<(float X, float Y)>? Curve,
            LayerStyle Style,
            LayerKind Kind,
            uint[]? SmartSource,
            int SmartSourceWidth,
            int SmartSourceHeight,
            float SmartX,
            float SmartY,
            float SmartScaleX,
            float SmartScaleY,
            float SmartRotation,
            bool SmartFlipX,
            bool SmartFlipY,
            ShapeData? Shape,
            TextData? Text);
    }
}
