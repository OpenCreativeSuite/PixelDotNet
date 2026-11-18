using System;
using System.Collections.Generic;

namespace PixelDotNet.Paint;

/// <summary>A document-space guide line.</summary>
/// <param name="Horizontal">True for a horizontal guide, false for vertical.</param>
/// <param name="Position">Position in document pixels.</param>
public readonly record struct Guide(bool Horizontal, int Position);

/// <summary>
/// A layered raster document with a cached composite and a cached run list.
/// </summary>
/// <remarks>
/// <para>
/// Everything the canvas draws comes from <see cref="Runs"/>: horizontal spans of
/// one colour covering the composited image. Painting a stroke only marks the
/// document dirty, so the pixels are composited and re-segmented at most once per
/// frame instead of once per pointer event.
/// </para>
/// <para>
/// The composite keeps straight alpha, so untouched areas stay transparent and the
/// canvas can show its transparency checker through them. Fully transparent runs
/// are dropped, which keeps an empty document free and keeps the drawn instance
/// count proportional to actual artwork. If a very noisy image would exceed the run
/// budget the segmenter retries with fewer colour bits, trading a little banding
/// for a bounded frame cost.
/// </para>
/// </remarks>
public sealed class PaintDocument
{
    /// <summary>
    /// Run count above which the segmenter quantises colours harder. Smooth art
    /// such as a blurred layer can have tens of thousands of colour runs, so the
    /// cap leaves room for a full-quality frame before trading banding for cost.
    /// </summary>
    private const int MaxRuns = 40000;

    /// <summary>Channel bits to drop, in retry order. Zero keeps full 8-bit colour.</summary>
    private static readonly int[] QuantShifts = [0, 1, 3, 5];

    private readonly uint[] _composite;
    private readonly List<PaintRun> _runs = [];
    private PaintRun[] _runArray = [];
    private bool _dirty = true;

    /// <summary>Creates an empty document with no layers.</summary>
    public PaintDocument(int width, int height)
    {
        Width = width;
        Height = height;
        _composite = new uint[width * height];
        Layers = [];
    }

    /// <summary>Gets the document width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the document height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the layer stack, bottom first.</summary>
    public List<PaintLayer> Layers { get; }

    /// <summary>Gets the document-space guide lines.</summary>
    public List<Guide> Guides { get; } = [];

    /// <summary>Gets or sets the index of the layer paint operations target.</summary>
    public int ActiveIndex { get; set; }

    /// <summary>Gets the layer paint operations currently target.</summary>
    public PaintLayer Active => Layers[Math.Clamp(ActiveIndex, 0, Layers.Count - 1)];

    /// <summary>Gets the composited run list, refreshing the caches first.</summary>
    public PaintRun[] Runs
    {
        get
        {
            Refresh();
            return _runArray;
        }
    }

    /// <summary>Gets the composited pixels. Refreshes the caches first.</summary>
    public uint[] Composite
    {
        get
        {
            Refresh();
            return _composite;
        }
    }

    /// <summary>Marks the composite and run caches stale.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>Reads one composited pixel, for the eyedropper.</summary>
    public uint CompositeAt(int x, int y)
    {
        Refresh();
        return _composite[(y * Width) + x];
    }

    /// <summary>Counts the composited luminance into 64 buckets for the histogram.</summary>
    public void BuildHistogram(int[] buckets)
    {
        Refresh();
        Array.Clear(buckets, 0, buckets.Length);

        int stride = buckets.Length;
        for (int i = 0; i < _composite.Length; i++)
        {
            uint color = _composite[i];
            if (Rgba.A(color) == 0)
            {
                continue;
            }

            int luminance = ((Rgba.R(color) * 77) + (Rgba.G(color) * 150) + (Rgba.B(color) * 29)) >> 8;
            buckets[luminance * stride / 256]++;
        }
    }

    /// <summary>Rebuilds the composite and runs when the document is dirty.</summary>
    private void Refresh()
    {
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        CompositeLayers();
        BuildRuns();
    }

    /// <summary>Composites every visible layer over a transparent base.</summary>
    /// <remarks>
    /// The base is transparent so the canvas can show its checker grid through
    /// empty areas. A blend mode needs a backdrop: over fully transparent pixels
    /// the layer is simply laid down with normal alpha instead of being mixed with
    /// black, which is what makes a Multiply layer over nothing look right.
    /// </remarks>
    private void CompositeLayers()
    {
        Array.Clear(_composite);

        for (int index = 0; index < Layers.Count; index++)
        {
            PaintLayer layer = Layers[index];
            if (!layer.Visible)
            {
                continue;
            }

            if (layer.IsAdjustment)
            {
                if (layer.Opacity <= 0f)
                {
                    continue;
                }

                AdjustmentOps.Apply(_composite, layer);
                continue;
            }

            int layerAlpha = (int)MathF.Round(Math.Clamp(layer.Opacity, 0f, 1f) * 255f);
            if (layerAlpha <= 0)
            {
                continue;
            }

            uint[] pixels = layer.Pixels;
            byte[]? mask = layer.Mask;
            bool clipped = layer.ClipToBelow;
            BlendMode mode = layer.Blend;

            for (int i = 0; i < pixels.Length; i++)
            {
                uint source = pixels[i];
                int alpha = (int)(source >> 24);
                if (alpha == 0)
                {
                    continue;
                }

                if (layerAlpha < 255)
                {
                    alpha = alpha * layerAlpha / 255;
                }

                if (mask is not null)
                {
                    alpha = alpha * mask[i] / 255;
                }

                if (clipped)
                {
                    alpha = alpha * Rgba.A(_composite[i]) / 255;
                }

                if (alpha == 0)
                {
                    continue;
                }

                _composite[i] = mode == BlendMode.Normal || Rgba.A(_composite[i]) == 0
                    ? Rgba.Over(_composite[i], source, alpha)
                    : Rgba.Blend(_composite[i], source, alpha, mode);
            }
        }
    }

    /// <summary>Segments the composite, retrying with coarser colour when needed.</summary>
    private void BuildRuns()
    {
        for (int attempt = 0; attempt < QuantShifts.Length; attempt++)
        {
            Segment(QuantShifts[attempt]);
            if (_runs.Count <= MaxRuns)
            {
                break;
            }
        }

        _runArray = [.. _runs];
    }

    /// <summary>Emits horizontal runs of equal colour, skipping fully transparent ones.</summary>
    private void Segment(int shift)
    {
        _runs.Clear();
        int width = Width;

        for (int y = 0; y < Height; y++)
        {
            int row = y * width;
            int start = 0;
            uint color = Quantize(_composite[row], shift);

            for (int x = 1; x < width; x++)
            {
                uint next = Quantize(_composite[row + x], shift);
                if (next == color)
                {
                    continue;
                }

                Emit(start, y, x - start, color);
                start = x;
                color = next;
            }

            Emit(start, y, width - start, color);
        }
    }

    /// <summary>Appends a run unless it is empty or fully transparent.</summary>
    private void Emit(int x, int y, int width, uint color)
    {
        if (width <= 0 || Rgba.A(color) == 0)
        {
            return;
        }

        _runs.Add(new PaintRun(x, y, width, color));
    }

    /// <summary>Drops the low bits of each colour channel, rounding and leaving alpha exact.</summary>
    /// <remarks>
    /// Alpha is never quantised: lowering an opaque pixel even slightly would let
    /// the transparency checker show through the artwork. Rounding rather than
    /// truncating keeps the average brightness of a quantised gradient intact.
    /// </remarks>
    private static uint Quantize(uint color, int shift)
    {
        if (shift == 0)
        {
            return color;
        }

        int step = 1 << shift;
        int half = step >> 1;

        return Rgba.Pack(
            Rgba.A(color),
            QuantiseChannel(Rgba.R(color), step, half),
            QuantiseChannel(Rgba.G(color), step, half),
            QuantiseChannel(Rgba.B(color), step, half));
    }

    /// <summary>Rounds one channel to a multiple of the step, clamped to 255.</summary>
    private static int QuantiseChannel(int value, int step, int half) =>
        Math.Min(255, ((value + half) / step) * step);
}
