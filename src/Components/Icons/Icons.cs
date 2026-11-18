using PixelDotNet.Paint;

namespace PixelDotNet.Components;

/// <summary>
/// Font Awesome class names used by the interface, plus the tool mapping.
/// </summary>
/// <remarks>
/// Icons are font glyphs, so they inherit colour, size and hover states from the
/// containing control instead of being baked into a canvas.
/// </remarks>
public static class Icons
{
    /// <summary>Four-way move arrow.</summary>
    public const string Move = "fa-up-down-left-right";

    /// <summary>Marquee selection.</summary>
    public const string Marquee = "fa-vector-square";

    /// <summary>Paintbrush.</summary>
    public const string Brush = "fa-paintbrush";

    /// <summary>Eraser.</summary>
    public const string Eraser = "fa-eraser";

    /// <summary>Clone stamp.</summary>
    public const string CloneStamp = "fa-stamp";

    /// <summary>Healing brush.</summary>
    public const string HealingBrush = "fa-bandage";

    /// <summary>Dodge and burn.</summary>
    public const string DodgeBurn = "fa-sun";

    /// <summary>Line tool.</summary>
    public const string Line = "fa-slash";

    /// <summary>Rectangle tool.</summary>
    public const string Rectangle = "fa-square";

    /// <summary>Ellipse tool.</summary>
    public const string Ellipse = "fa-circle";

    /// <summary>Gradient tool.</summary>
    public const string Gradient = "fa-circle-half-stroke";

    /// <summary>Paint bucket.</summary>
    public const string Bucket = "fa-fill-drip";

    /// <summary>Eyedropper.</summary>
    public const string Eyedropper = "fa-eye-dropper";

    /// <summary>Zoom.</summary>
    public const string Zoom = "fa-magnifying-glass";

    /// <summary>Undo.</summary>
    public const string Undo = "fa-rotate-left";

    /// <summary>Redo.</summary>
    public const string Redo = "fa-rotate-right";

    /// <summary>Save.</summary>
    public const string Save = "fa-floppy-disk";

    /// <summary>Visible layer.</summary>
    public const string Eye = "fa-eye";

    /// <summary>Hidden layer.</summary>
    public const string EyeOff = "fa-eye-slash";

    /// <summary>Locked layer.</summary>
    public const string Lock = "fa-lock";

    /// <summary>Unlocked layer.</summary>
    public const string Unlock = "fa-lock-open";

    /// <summary>Add.</summary>
    public const string Plus = "fa-plus";

    /// <summary>Duplicate.</summary>
    public const string Duplicate = "fa-copy";

    /// <summary>Merge down.</summary>
    public const string MergeDown = "fa-arrow-down-long";

    /// <summary>Delete.</summary>
    public const string Delete = "fa-trash-can";

    /// <summary>Swap.</summary>
    public const string Swap = "fa-arrow-right-arrow-left";

    /// <summary>History.</summary>
    public const string History = "fa-clock-rotate-left";

    /// <summary>Previous.</summary>
    public const string Previous = "fa-chevron-left";

    /// <summary>Next.</summary>
    public const string Next = "fa-chevron-right";

    /// <summary>Close or clear.</summary>
    public const string Close = "fa-xmark";

    /// <summary>Zoom out.</summary>
    public const string Minus = "fa-minus";

    /// <summary>Invert colours.</summary>
    public const string Invert = "fa-circle-half-stroke";

    /// <summary>Remove colour.</summary>
    public const string Desaturate = "fa-droplet";

    /// <summary>Merge the layer stack.</summary>
    public const string Flatten = "fa-layer-group";

    /// <summary>A document tab.</summary>
    public const string Document = "fa-image";

    /// <summary>A folder in the file browser.</summary>
    public const string Folder = "fa-folder";

    /// <summary>A folder being opened.</summary>
    public const string FolderOpen = "fa-folder-open";

    /// <summary>Go to the parent folder.</summary>
    public const string Up = "fa-arrow-up";

    /// <summary>Cut a file.</summary>
    public const string Scissors = "fa-scissors";

    /// <summary>Paste a file.</summary>
    public const string Paste = "fa-paste";

    /// <summary>Rename a file.</summary>
    public const string Pen = "fa-pen";

    /// <summary>Create a folder.</summary>
    public const string FolderPlus = "fa-folder-plus";

    /// <summary>Remove a folder.</summary>
    public const string FolderMinus = "fa-folder-minus";

    /// <summary>A smart object.</summary>
    public const string SmartObject = "fa-cube";

    /// <summary>The pen tool.</summary>
    public const string PenNib = "fa-pen-nib";

    /// <summary>The text tool.</summary>
    public const string Text = "fa-font";

    /// <summary>Elliptical selection.</summary>
    public const string EllipseSelect = "fa-circle";

    /// <summary>Freehand lasso selection.</summary>
    public const string Lasso = "fa-draw-polygon";

    /// <summary>Magic wand colour selection.</summary>
    public const string MagicWand = "fa-wand-magic";

    /// <summary>Brush-driven quick selection.</summary>
    public const string QuickSelect = "fa-wand-magic-sparkles";

    /// <summary>Align text left.</summary>
    public const string AlignLeft = "fa-align-left";

    /// <summary>Centre text.</summary>
    public const string AlignCenter = "fa-align-center";

    /// <summary>Align text right.</summary>
    public const string AlignRight = "fa-align-right";

    /// <summary>A downward chevron, used by dropdowns.</summary>
    public const string ChevronDown = "fa-chevron-down";

    /// <summary>Sparkles, used by the text effects button.</summary>
    public const string Sparkles = "fa-wand-magic-sparkles";

    /// <summary>Colour palette, used by the Colour tab.</summary>
    public const string Palette = "fa-palette";

    /// <summary>Adjustment sliders, used by the Adjust tab.</summary>
    public const string Sliders = "fa-sliders";

    /// <summary>Information circle, used by generic help topics.</summary>
    public const string Info = "fa-circle-info";

    /// <summary>Layer mask.</summary>
    public const string Mask = "fa-mask";

    /// <summary>Gets the glyph for a tool.</summary>
    public static string ForTool(PaintTool tool) => tool switch
    {
        PaintTool.Move => Move,
        PaintTool.Marquee => Marquee,
        PaintTool.EllipseSelect => EllipseSelect,
        PaintTool.Lasso => Lasso,
        PaintTool.MagicWand => MagicWand,
        PaintTool.QuickSelect => QuickSelect,
        PaintTool.Brush => Brush,
        PaintTool.Eraser => Eraser,
        PaintTool.CloneStamp => CloneStamp,
        PaintTool.HealingBrush => HealingBrush,
        PaintTool.DodgeBurn => DodgeBurn,
        PaintTool.Line => Line,
        PaintTool.Rectangle => Rectangle,
        PaintTool.Ellipse => Ellipse,
        PaintTool.Pen => PenNib,
        PaintTool.Gradient => Gradient,
        PaintTool.Bucket => Bucket,
        PaintTool.Eyedropper => Eyedropper,
        PaintTool.Text => Text,
        _ => Zoom,
    };

    /// <summary>Gets whether a tool reads best in the outline (regular) weight.</summary>
    public static bool IsRegular(PaintTool tool) =>
        tool is PaintTool.Rectangle or PaintTool.Ellipse or PaintTool.EllipseSelect;
}
