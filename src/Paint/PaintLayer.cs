using System;

namespace PixelDotNet.Paint;

/// <summary>
/// One raster layer: a straight-alpha <c>0xAARRGGBB</c> pixel buffer plus the
/// display properties the compositor reads.
/// </summary>
public sealed class PaintLayer
{
    /// <summary>Creates an empty layer of the given document size.</summary>
    /// <param name="name">Name shown in the layer panel.</param>
    /// <param name="width">Layer width in pixels.</param>
    /// <param name="height">Layer height in pixels.</param>
    /// <param name="isBackground">
    /// True for the bottom layer, whose eraser paints white instead of punching a
    /// hole through the document.
    /// </param>
    public PaintLayer(string name, int width, int height, bool isBackground = false)
    {
        Name = name;
        Width = width;
        Height = height;
        IsBackground = isBackground;
        Pixels = new uint[width * height];
    }

    /// <summary>Gets or sets the layer name.</summary>
    public string Name { get; set; }

    /// <summary>Gets the layer width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the layer height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets whether this is the opaque bottom layer.</summary>
    public bool IsBackground { get; }

    /// <summary>Gets or sets whether the layer contributes to the composite.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Gets or sets the layer opacity in the 0-1 range.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Gets or sets how the layer mixes into the stack below it.</summary>
    public BlendMode Blend { get; set; }

    /// <summary>Gets or sets whether editing this layer is blocked.</summary>
    public bool Locked { get; set; }

    /// <summary>Gets the pixel buffer, row major, top down.</summary>
    public uint[] Pixels { get; private set; }

    /// <summary>Returns whether a document-space coordinate is inside the layer.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Reads a pixel without bounds checking.</summary>
    public uint At(int x, int y) => Pixels[(y * Width) + x];

    /// <summary>Writes a pixel without bounds checking.</summary>
    public void Put(int x, int y, uint argb) => Pixels[(y * Width) + x] = argb;

    /// <summary>Floods the whole layer with one colour.</summary>
    public void Fill(uint argb) => Array.Fill(Pixels, argb);

    /// <summary>Returns a copy of the pixel buffer.</summary>
    public uint[] CapturePixels() => (uint[])Pixels.Clone();

    /// <summary>
    /// Takes ownership of a snapshot buffer, used by undo. The length must match
    /// the layer size; a mismatch is ignored so a stale snapshot cannot corrupt it.
    /// </summary>
    public void Adopt(uint[] pixels)
    {
        if (pixels.Length != Pixels.Length)
        {
            return;
        }

        Pixels = pixels;
    }
}
