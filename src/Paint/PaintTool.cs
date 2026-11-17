namespace PixelDotNet.Paint;

/// <summary>The tools offered by the tool rail, in rail order.</summary>
public enum PaintTool
{
    /// <summary>Drags the active layer's pixels.</summary>
    Move,

    /// <summary>Drags a rectangular selection.</summary>
    Marquee,

    /// <summary>Paints with the current colour.</summary>
    Brush,

    /// <summary>Erases to white on the background layer, to transparency above it.</summary>
    Eraser,

    /// <summary>Drags a straight line with the current brush.</summary>
    Line,

    /// <summary>Drags a rectangle, filled or outlined.</summary>
    Rectangle,

    /// <summary>Drags an ellipse, filled or outlined.</summary>
    Ellipse,

    /// <summary>Drags a two-colour linear gradient.</summary>
    Gradient,

    /// <summary>Flood-fills the connected region under the pointer.</summary>
    Bucket,

    /// <summary>Samples a composited colour into the foreground colour.</summary>
    Eyedropper,

    /// <summary>Places styled text on the active layer.</summary>
    Text,

    /// <summary>Click to zoom in, right-click to zoom out.</summary>
    Zoom,
}
