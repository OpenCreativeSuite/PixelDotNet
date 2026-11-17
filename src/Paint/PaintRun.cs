namespace PixelDotNet.Paint;

/// <summary>
/// A horizontal run of one colour in the composited document. Runs are what the
/// canvas draws, so they carry document-space coordinates.
/// </summary>
/// <param name="X">Left edge in document pixels.</param>
/// <param name="Y">Row in document pixels.</param>
/// <param name="Width">Length in document pixels.</param>
/// <param name="Color">Packed opaque <c>0xAARRGGBB</c> colour.</param>
public readonly record struct PaintRun(int X, int Y, int Width, uint Color);
