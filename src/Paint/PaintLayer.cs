using System;
using System.Collections.Generic;

namespace PixelDotNet.Paint;

/// <summary>What a layer contributes to the composite.</summary>
public enum LayerKind
{
    /// <summary>Ordinary raster pixels.</summary>
    Raster,

    /// <summary>A non-destructive tone adjustment applied to the layers below.</summary>
    Adjustment,
}

/// <summary>The adjustment used by an adjustment layer.</summary>
public enum AdjustmentKind
{
    /// <summary>Not an adjustment layer.</summary>
    None,

    /// <summary>Black point, white point and midtone gamma.</summary>
    Levels,

    /// <summary>A tone curve through draggable points.</summary>
    Curves,
}

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
    /// <param name="kind">Whether this is a raster or adjustment layer.</param>
    /// <param name="adjustment">The adjustment used when <paramref name="kind"/> is Adjustment.</param>
    public PaintLayer(
        string name,
        int width,
        int height,
        bool isBackground = false,
        LayerKind kind = LayerKind.Raster,
        AdjustmentKind adjustment = AdjustmentKind.None)
    {
        Name = name;
        Width = width;
        Height = height;
        IsBackground = isBackground;
        Kind = kind;
        Adjustment = adjustment;
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

    /// <summary>Gets or sets whether this layer is clipped to the visible content below it.</summary>
    public bool ClipToBelow { get; set; }

    /// <summary>Gets whether this layer contributes pixels or an adjustment.</summary>
    public LayerKind Kind { get; }

    /// <summary>Gets or sets the adjustment kind for an adjustment layer.</summary>
    public AdjustmentKind Adjustment { get; set; }

    /// <summary>Gets whether this is an adjustment layer.</summary>
    public bool IsAdjustment => Kind == LayerKind.Adjustment;

    /// <summary>Gets or sets the levels black point for an adjustment layer.</summary>
    public float AdjustmentBlack { get; set; }

    /// <summary>Gets or sets the levels white point for an adjustment layer.</summary>
    public float AdjustmentWhite { get; set; } = 255f;

    /// <summary>Gets or sets the levels midtone gamma for an adjustment layer.</summary>
    public float AdjustmentGamma { get; set; } = 1f;

    /// <summary>Gets the tone curve of a Curves adjustment layer.</summary>
    public List<(float X, float Y)> AdjustmentCurve { get; } = [(0f, 0f), (1f, 1f)];

    /// <summary>Copies the adjustment settings from another layer.</summary>
    public void CopyAdjustmentFrom(PaintLayer source)
    {
        Adjustment = source.Adjustment;
        AdjustmentBlack = source.AdjustmentBlack;
        AdjustmentWhite = source.AdjustmentWhite;
        AdjustmentGamma = source.AdjustmentGamma;
        AdjustmentCurve.Clear();
        AdjustmentCurve.AddRange(source.AdjustmentCurve);
    }

    /// <summary>Gets the per-pixel layer mask, or null when the layer has none.</summary>
    public byte[]? Mask { get; private set; }

    /// <summary>Gets whether the layer has a mask.</summary>
    public bool HasMask => Mask is not null;

    /// <summary>Adds a fully revealing mask.</summary>
    public void AddMask()
    {
        if (Mask is not null)
        {
            return;
        }

        Mask = new byte[Width * Height];
        Array.Fill(Mask, (byte)255);
    }

    /// <summary>Removes the layer mask.</summary>
    public void RemoveMask() => Mask = null;

    /// <summary>Returns a copy of the mask buffer, or null when there is none.</summary>
    public byte[]? CaptureMask() => Mask is null ? null : (byte[])Mask.Clone();

    /// <summary>Takes ownership of a mask buffer, or clears the mask for null.</summary>
    public void AdoptMask(byte[]? mask)
    {
        if (mask is null)
        {
            Mask = null;
            return;
        }

        if (mask.Length == Width * Height)
        {
            Mask = mask;
        }
    }

    /// <summary>Reads a mask sample, treating absent masks as fully visible.</summary>
    public byte MaskAt(int index) => Mask is null ? (byte)255 : Mask[index];

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
