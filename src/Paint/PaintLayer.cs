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

    /// <summary>Pixels retained from a source image and placed with a transform.</summary>
    SmartObject,

    /// <summary>A non-destructive rectangle, ellipse or line.</summary>
    Shape,
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

    /// <summary>Gets or sets the group this layer belongs to, if any.</summary>
    public LayerGroup? Group { get; set; }

    /// <summary>Gets the non-destructive shadow, stroke and glow settings.</summary>
    public LayerStyle Style { get; } = new();

    /// <summary>Gets whether this layer contributes pixels or an adjustment.</summary>
    public LayerKind Kind { get; set; }

    /// <summary>Gets or sets the adjustment kind for an adjustment layer.</summary>
    public AdjustmentKind Adjustment { get; set; }

    /// <summary>Gets whether this is an adjustment layer.</summary>
    public bool IsAdjustment => Kind == LayerKind.Adjustment;

    /// <summary>Gets whether this layer keeps an original source image.</summary>
    public bool IsSmartObject => Kind == LayerKind.SmartObject && SmartSource is not null;

    /// <summary>Gets whether this layer draws editable shape geometry.</summary>
    public bool IsShapeLayer => Kind == LayerKind.Shape && Shape is not null;

    /// <summary>Gets the editable shape geometry, if this is a shape layer.</summary>
    public ShapeData? Shape { get; private set; }

    /// <summary>Installs shape geometry and rebuilds the pixel cache.</summary>
    public void AdoptShape(ShapeData shape)
    {
        Shape = shape;
        Kind = LayerKind.Shape;
        ShapeOps.Render(this);
    }

    /// <summary>Bakes the shape into its pixel cache and becomes a raster layer.</summary>
    public void RasterizeShape()
    {
        Shape = null;
        Kind = LayerKind.Raster;
    }

    /// <summary>Rebuilds the pixel cache from the shape geometry.</summary>
    public void RefreshShape() => ShapeOps.Render(this);

    /// <summary>Restores shape geometry from a snapshot without rerendering.</summary>
    public void RestoreShape(ShapeData? shape, LayerKind kind)
    {
        Shape = shape;
        Kind = kind;
    }

    /// <summary>Gets the retained source pixels of a smart object.</summary>
    public uint[]? SmartSource { get; private set; }

    /// <summary>Gets the source width in pixels.</summary>
    public int SmartSourceWidth { get; private set; }

    /// <summary>Gets the source height in pixels.</summary>
    public int SmartSourceHeight { get; private set; }

    /// <summary>Gets or sets the smart object's X placement in document pixels.</summary>
    public float SmartX { get; set; }

    /// <summary>Gets or sets the smart object's Y placement in document pixels.</summary>
    public float SmartY { get; set; }

    /// <summary>Gets or sets the horizontal scale of the smart object.</summary>
    public float SmartScaleX { get; set; } = 1f;

    /// <summary>Gets or sets the vertical scale of the smart object.</summary>
    public float SmartScaleY { get; set; } = 1f;

    /// <summary>Gets or sets the rotation of the smart object in degrees.</summary>
    public float SmartRotation { get; set; }

    /// <summary>Gets or sets whether the smart object is mirrored horizontally.</summary>
    public bool SmartFlipX { get; set; }

    /// <summary>Gets or sets whether the smart object is mirrored vertically.</summary>
    public bool SmartFlipY { get; set; }

    /// <summary>Captures the current pixels as a smart-object source.</summary>
    public void MakeSmart()
    {
        if (SmartSource is not null)
        {
            return;
        }

        SmartSource = (uint[])Pixels.Clone();
        SmartSourceWidth = Width;
        SmartSourceHeight = Height;
        SmartX = 0f;
        SmartY = 0f;
        SmartScaleX = 1f;
        SmartScaleY = 1f;
        SmartRotation = 0f;
        SmartFlipX = false;
        SmartFlipY = false;
        Kind = LayerKind.SmartObject;
    }

    /// <summary>Installs a smart-object source and transform, then renders the cache.</summary>
    public void AdoptSmart(
        uint[] source,
        int sourceWidth,
        int sourceHeight,
        float x,
        float y,
        float scaleX,
        float scaleY,
        float rotation,
        bool flipX,
        bool flipY)
    {
        SmartSource = source;
        SmartSourceWidth = sourceWidth;
        SmartSourceHeight = sourceHeight;
        SmartX = x;
        SmartY = y;
        SmartScaleX = scaleX <= 0f ? 1f : scaleX;
        SmartScaleY = scaleY <= 0f ? 1f : scaleY;
        SmartRotation = rotation;
        SmartFlipX = flipX;
        SmartFlipY = flipY;
        Kind = LayerKind.SmartObject;
        RefreshSmart();
    }

    /// <summary>Restores smart-object state from a snapshot without rerendering.</summary>
    public void RestoreSmart(
        uint[]? source,
        int sourceWidth,
        int sourceHeight,
        float x,
        float y,
        float scaleX,
        float scaleY,
        float rotation,
        bool flipX,
        bool flipY,
        LayerKind kind)
    {
        SmartSource = source;
        SmartSourceWidth = sourceWidth;
        SmartSourceHeight = sourceHeight;
        SmartX = x;
        SmartY = y;
        SmartScaleX = scaleX <= 0f ? 1f : scaleX;
        SmartScaleY = scaleY <= 0f ? 1f : scaleY;
        SmartRotation = rotation;
        SmartFlipX = flipX;
        SmartFlipY = flipY;
        Kind = kind;
    }

    /// <summary>Drops the source and keeps the current rendered pixels.</summary>
    public void RasterizeSmart()
    {
        SmartSource = null;
        Kind = LayerKind.Raster;
    }

    /// <summary>Rerenders the layer cache from the smart-object source.</summary>
    public void RefreshSmart()
    {
        if (SmartSource is null
            || SmartSourceWidth <= 0
            || SmartSourceHeight <= 0
            || Pixels.Length != Width * Height)
        {
            return;
        }

        Array.Clear(Pixels);
        float centerX = SmartX + (SmartSourceWidth * SmartScaleX * 0.5f);
        float centerY = SmartY + (SmartSourceHeight * SmartScaleY * 0.5f);
        float radians = SmartRotation * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);

        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            float dy = y - centerY;
            for (int x = 0; x < Width; x++)
            {
                float dx = x - centerX;
                float u = (dx * cos) + (dy * sin);
                float v = (-dx * sin) + (dy * cos);
                float sourceX = (u / SmartScaleX) + (SmartSourceWidth * 0.5f);
                float sourceY = (v / SmartScaleY) + (SmartSourceHeight * 0.5f);

                if (SmartFlipX)
                {
                    sourceX = SmartSourceWidth - 1 - sourceX;
                }

                if (SmartFlipY)
                {
                    sourceY = SmartSourceHeight - 1 - sourceY;
                }

                if (sourceX < -0.5f
                    || sourceY < -0.5f
                    || sourceX > SmartSourceWidth - 0.5f
                    || sourceY > SmartSourceHeight - 0.5f)
                {
                    continue;
                }

                Pixels[row + x] = PerspectiveOps.Sample(
                    SmartSource,
                    SmartSourceWidth,
                    SmartSourceHeight,
                    sourceX,
                    sourceY);
            }
        }
    }

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
