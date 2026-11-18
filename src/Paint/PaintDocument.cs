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
    private readonly uint[] _groupBuffer;
    private readonly uint[] _layerBuffer;
    private readonly byte[] _alphaBuffer;
    private readonly byte[] _alphaScratch;
    private readonly byte[] _alphaScratch2;
    private readonly byte[] _alphaScratch3;
    private readonly List<PaintRun> _runs = [];
    private PaintRun[] _runArray = [];
    private bool _dirty = true;

    /// <summary>Creates an empty document with no layers.</summary>
    public PaintDocument(int width, int height)
    {
        Width = width;
        Height = height;
        _composite = new uint[width * height];
        _groupBuffer = new uint[width * height];
        _layerBuffer = new uint[width * height];
        _alphaBuffer = new byte[width * height];
        _alphaScratch = new byte[width * height];
        _alphaScratch2 = new byte[width * height];
        _alphaScratch3 = new byte[width * height];
        Layers = [];
    }

    /// <summary>Gets the document width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the document height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the layer stack, bottom first.</summary>
    public List<PaintLayer> Layers { get; }

    /// <summary>Gets the layer groups in the document.</summary>
    /// <remarks>
    /// A group's members are contiguous in <see cref="Layers"/>. Groups may be
    /// empty, but every layer belongs to at most one group.
    /// </remarks>
    public List<LayerGroup> Groups { get; } = [];

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

        int index = 0;
        while (index < Layers.Count)
        {
            PaintLayer layer = Layers[index];
            LayerGroup? group = layer.Group;

            if (group is null)
            {
                CompositeLayerOnto(_composite, layer);
                index++;
                continue;
            }

            int end = index + 1;
            while (end < Layers.Count && ReferenceEquals(Layers[end].Group, group))
            {
                end++;
            }

            if (group.Visible && group.Opacity > 0f)
            {
                Array.Clear(_groupBuffer);
                for (int member = index; member < end; member++)
                {
                    CompositeLayerOnto(_groupBuffer, Layers[member]);
                }

                BlendGroup(group);
            }

            index = end;
        }
    }

    /// <summary>Composites one layer onto a target buffer.</summary>
    private void CompositeLayerOnto(uint[] target, PaintLayer layer)
    {
        if (!layer.Visible)
        {
            return;
        }

        if (layer.IsAdjustment)
        {
            if (layer.Opacity > 0f)
            {
                AdjustmentOps.Apply(target, layer);
            }

            return;
        }

        int layerAlpha = (int)MathF.Round(Math.Clamp(layer.Opacity, 0f, 1f) * 255f);
        if (layerAlpha <= 0)
        {
            return;
        }

        if (!layer.Style.HasAny)
        {
            BlendPixels(target, layer, layerAlpha);
            return;
        }

        Array.Clear(_layerBuffer);
        Array.Clear(_alphaBuffer);
        uint[] pixels = layer.Pixels;
        byte[]? mask = layer.Mask;
        bool clipped = layer.ClipToBelow;

        for (int i = 0; i < pixels.Length; i++)
        {
            uint source = pixels[i];
            int alpha = (int)(source >> 24);
            if (alpha == 0)
            {
                continue;
            }

            alpha = alpha * layerAlpha / 255;
            if (mask is not null)
            {
                alpha = alpha * mask[i] / 255;
            }

            if (clipped)
            {
                alpha = alpha * Rgba.A(target[i]) / 255;
            }

            if (alpha == 0)
            {
                continue;
            }

            _layerBuffer[i] = (source & 0x00FFFFFFu) | ((uint)alpha << 24);
            _alphaBuffer[i] = (byte)alpha;
        }

        ApplyStylesUnder(target, layer.Style, clipped);
        BlendPixels(target, _layerBuffer, layer);

        if (layer.Style.Stroke && layer.Style.StrokeInside)
        {
            ApplyInsideStroke(target, layer.Style);
        }
    }

    /// <summary>Blends a layer's pixels into a target buffer.</summary>
    private static void BlendPixels(uint[] target, PaintLayer layer, int layerAlpha)
    {
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
                alpha = alpha * Rgba.A(target[i]) / 255;
            }

            if (alpha == 0)
            {
                continue;
            }

            target[i] = mode == BlendMode.Normal || Rgba.A(target[i]) == 0
                ? Rgba.Over(target[i], source, alpha)
                : Rgba.Blend(target[i], source, alpha, mode);
        }
    }

    /// <summary>Blends a prepared pixel buffer into a target buffer.</summary>
    private static void BlendPixels(uint[] target, uint[] source, PaintLayer layer)
    {
        BlendMode mode = layer.Blend;

        for (int i = 0; i < source.Length; i++)
        {
            uint pixel = source[i];
            int alpha = (int)(pixel >> 24);
            if (alpha == 0)
            {
                continue;
            }

            target[i] = mode == BlendMode.Normal || Rgba.A(target[i]) == 0
                ? Rgba.Over(target[i], pixel, alpha)
                : Rgba.Blend(target[i], pixel, alpha, mode);
        }
    }

    /// <summary>Draws shadow, glow and outside stroke beneath the layer.</summary>
    private void ApplyStylesUnder(uint[] target, LayerStyle style, bool clipped)
    {
        if (style.DropShadow)
        {
            BlurAlpha(_alphaBuffer, _alphaScratch, (int)MathF.Round(style.DropShadowBlur));
            float radians = style.DropShadowAngle * MathF.PI / 180f;
            int offsetX = (int)MathF.Round(MathF.Cos(radians) * style.DropShadowDistance);
            int offsetY = (int)MathF.Round(MathF.Sin(radians) * style.DropShadowDistance);
            BlendStyle(
                target,
                _alphaScratch,
                style.DropShadowColor,
                style.DropShadowOpacity,
                clipped,
                offsetX,
                offsetY);
        }

        if (style.OuterGlow)
        {
            BlurAlpha(_alphaBuffer, _alphaScratch, (int)MathF.Round(style.OuterGlowSize));
            for (int i = 0; i < _alphaScratch.Length; i++)
            {
                _alphaScratch[i] = (byte)Math.Max(0, _alphaScratch[i] - _alphaBuffer[i]);
            }

            BlendStyle(
                target,
                _alphaScratch,
                style.OuterGlowColor,
                style.OuterGlowOpacity,
                clipped,
                0,
                0);
        }

        if (style.Stroke && !style.StrokeInside)
        {
            DilateAlpha(_alphaBuffer, _alphaScratch, (int)MathF.Round(style.StrokeWidth));
            for (int i = 0; i < _alphaScratch.Length; i++)
            {
                _alphaScratch[i] = (byte)Math.Max(0, _alphaScratch[i] - _alphaBuffer[i]);
            }

            BlendStyle(
                target,
                _alphaScratch,
                style.StrokeColor,
                style.StrokeOpacity,
                clipped,
                0,
                0);
        }
    }

    /// <summary>Draws an inside stroke over the layer pixels.</summary>
    private void ApplyInsideStroke(uint[] target, LayerStyle style)
    {
        for (int i = 0; i < _alphaScratch2.Length; i++)
        {
            _alphaScratch2[i] = (byte)(255 - _alphaBuffer[i]);
        }

        DilateAlpha(
            _alphaScratch2,
            _alphaScratch,
            (int)MathF.Round(style.StrokeWidth));

        for (int i = 0; i < _alphaScratch.Length; i++)
        {
            _alphaScratch[i] = (byte)Math.Max(
                0,
                _alphaBuffer[i] - (255 - _alphaScratch[i]));
        }

        BlendStyle(target, _alphaScratch, style.StrokeColor, style.StrokeOpacity, false, 0, 0);
    }

    /// <summary>Blends a coloured alpha mask into the target at an offset.</summary>
    private void BlendStyle(
        uint[] target,
        byte[] alpha,
        uint color,
        float opacity,
        bool clipped,
        int offsetX,
        int offsetY)
    {
        int strength = (int)MathF.Round(Math.Clamp(opacity, 0f, 1f) * 255f);
        strength = strength * Rgba.A(color) / 255;
        if (strength <= 0)
        {
            return;
        }

        for (int y = 0; y < Height; y++)
        {
            int sourceY = y - offsetY;
            if (sourceY < 0 || sourceY >= Height)
            {
                continue;
            }

            int targetRow = y * Width;
            int sourceRow = sourceY * Width;

            for (int x = 0; x < Width; x++)
            {
                int sourceX = x - offsetX;
                if (sourceX < 0 || sourceX >= Width)
                {
                    continue;
                }

                int a = alpha[sourceRow + sourceX];
                if (a == 0)
                {
                    continue;
                }

                a = a * strength / 255;
                if (clipped)
                {
                    a = a * Rgba.A(target[targetRow + x]) / 255;
                }

                if (a > 0)
                {
                    target[targetRow + x] = Rgba.Over(target[targetRow + x], color, a);
                }
            }
        }
    }

    /// <summary>Blurs an alpha mask with a separable box filter.</summary>
    private void BlurAlpha(byte[] source, byte[] destination, int radius)
    {
        if (radius <= 0)
        {
            Array.Copy(source, destination, source.Length);
            return;
        }

        radius = Math.Min(radius, 40);
        BoxBlurHorizontal(source, _alphaScratch2, radius);
        BoxBlurVertical(_alphaScratch2, destination, radius);
    }

    /// <summary>Horizontal box blur with clamped edges.</summary>
    private void BoxBlurHorizontal(byte[] source, byte[] destination, int radius)
    {
        int span = (radius * 2) + 1;

        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            int sum = 0;

            for (int x = -radius; x <= radius; x++)
            {
                sum += source[row + Math.Clamp(x, 0, Width - 1)];
            }

            for (int x = 0; x < Width; x++)
            {
                destination[row + x] = (byte)(sum / span);
                sum -= source[row + Math.Clamp(x - radius, 0, Width - 1)];
                sum += source[row + Math.Clamp(x + radius + 1, 0, Width - 1)];
            }
        }
    }

    /// <summary>Vertical box blur with clamped edges.</summary>
    private void BoxBlurVertical(byte[] source, byte[] destination, int radius)
    {
        int span = (radius * 2) + 1;

        for (int x = 0; x < Width; x++)
        {
            int sum = 0;

            for (int y = -radius; y <= radius; y++)
            {
                sum += source[(Math.Clamp(y, 0, Height - 1) * Width) + x];
            }

            for (int y = 0; y < Height; y++)
            {
                destination[(y * Width) + x] = (byte)(sum / span);
                sum -= source[(Math.Clamp(y - radius, 0, Height - 1) * Width) + x];
                sum += source[(Math.Clamp(y + radius + 1, 0, Height - 1) * Width) + x];
            }
        }
    }

    /// <summary>Dilates an alpha mask with a separable maximum filter.</summary>
    private void DilateAlpha(byte[] source, byte[] destination, int radius)
    {
        if (radius <= 0)
        {
            Array.Copy(source, destination, source.Length);
            return;
        }

        radius = Math.Min(radius, 40);
        int span = (radius * 2) + 1;

        for (int y = 0; y < Height; y++)
        {
            int row = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int best = 0;
                int start = Math.Max(0, x - radius);
                int end = Math.Min(Width - 1, x + radius);
                for (int sample = start; sample <= end; sample++)
                {
                    best = Math.Max(best, source[row + sample]);
                }

                _alphaScratch3[row + x] = (byte)best;
            }
        }

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                int best = 0;
                int start = Math.Max(0, y - radius);
                int end = Math.Min(Height - 1, y + radius);
                for (int sample = start; sample <= end; sample++)
                {
                    best = Math.Max(best, _alphaScratch3[(sample * Width) + x]);
                }

                destination[(y * Width) + x] = (byte)best;
            }
        }
    }

    /// <summary>Composites a finished group buffer over the document.</summary>
    private void BlendGroup(LayerGroup group)
    {
        int groupAlpha = (int)MathF.Round(Math.Clamp(group.Opacity, 0f, 1f) * 255f);
        if (groupAlpha <= 0)
        {
            return;
        }

        for (int i = 0; i < _composite.Length; i++)
        {
            uint source = _groupBuffer[i];
            int alpha = (int)(source >> 24);
            if (alpha == 0)
            {
                continue;
            }

            if (groupAlpha < 255)
            {
                alpha = alpha * groupAlpha / 255;
            }

            _composite[i] = Rgba.Over(_composite[i], source, alpha);
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
