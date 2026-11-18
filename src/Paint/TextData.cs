using System;
using System.Collections.Generic;

namespace PixelDotNet.Paint;

/// <summary>Editable vector text geometry and styling for a text layer.</summary>
public sealed class TextData
{
    /// <summary>Gets or sets the text to draw.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Gets or sets the font family index from <see cref="TextFonts"/>.</summary>
    public int FontIndex { get; set; }

    /// <summary>Gets or sets the font size in document pixels.</summary>
    public float Size { get; set; } = 64f;

    /// <summary>Gets or sets whether the bold face is used.</summary>
    public bool Bold { get; set; }

    /// <summary>Gets or sets whether the italic face is used.</summary>
    public bool Italic { get; set; }

    /// <summary>Gets or sets how the line is aligned against its anchor.</summary>
    public TextAlign Align { get; set; }

    /// <summary>Gets or sets the glyph colour.</summary>
    public uint Color { get; set; }

    /// <summary>Gets or sets the baseline X for straight text.</summary>
    public float X { get; set; }

    /// <summary>Gets or sets the baseline Y for straight text.</summary>
    public float Y { get; set; }

    /// <summary>Gets or sets the clockwise rotation in degrees.</summary>
    public float Rotation { get; set; }

    /// <summary>Gets or sets whether the glyphs are outlined.</summary>
    public bool Outline { get; set; }

    /// <summary>Gets or sets the outline thickness in pixels.</summary>
    public float OutlineWidth { get; set; } = 3f;

    /// <summary>Gets or sets the outline colour.</summary>
    public uint OutlineColor { get; set; } = Rgba.FromRgb(0x10, 0x10, 0x14);

    /// <summary>Gets or sets whether a shadow is drawn.</summary>
    public bool Shadow { get; set; }

    /// <summary>Gets or sets the horizontal shadow offset.</summary>
    public int ShadowX { get; set; } = 4;

    /// <summary>Gets or sets the vertical shadow offset.</summary>
    public int ShadowY { get; set; } = 4;

    /// <summary>Gets or sets the shadow softness.</summary>
    public float ShadowBlur { get; set; } = 6f;

    /// <summary>Gets or sets the shadow opacity in the 0-100 range.</summary>
    public int ShadowOpacity { get; set; } = 65;

    /// <summary>Gets or sets the shadow colour.</summary>
    public uint ShadowColor { get; set; }

    /// <summary>Gets or sets the name of the path shape the text follows, if any.</summary>
    public string? PathLayer { get; set; }

    /// <summary>Gets or sets where along the path the text starts, as a 0-1 fraction.</summary>
    public float PathOffset { get; set; }

    /// <summary>Gets or sets whether the attached path is closed.</summary>
    public bool PathClosed { get; set; } = true;

    /// <summary>Gets or sets extra space between glyphs on a path, in pixels.</summary>
    public float LetterSpacing { get; set; }

    /// <summary>Gets or sets a fallback copy of the path points for text on a path.</summary>
    public List<PathNode>? Path { get; set; }

    /// <summary>Gets whether this text follows a path.</summary>
    public bool HasPath => Path is { Count: >= 2 };

    /// <summary>Gets the outline and shadow settings passed to the rasteriser.</summary>
    public TextEffectStyle EffectStyle => new(
        Outline,
        OutlineWidth,
        OutlineColor,
        Shadow,
        ShadowX,
        ShadowY,
        ShadowBlur,
        ShadowOpacity,
        ShadowColor);

    /// <summary>Returns an independent copy of this text.</summary>
    public TextData Clone() => new()
    {
        Content = Content,
        FontIndex = FontIndex,
        Size = Size,
        Bold = Bold,
        Italic = Italic,
        Align = Align,
        Color = Color,
        X = X,
        Y = Y,
        Rotation = Rotation,
        Outline = Outline,
        OutlineWidth = OutlineWidth,
        OutlineColor = OutlineColor,
        Shadow = Shadow,
        ShadowX = ShadowX,
        ShadowY = ShadowY,
        ShadowBlur = ShadowBlur,
        ShadowOpacity = ShadowOpacity,
        ShadowColor = ShadowColor,
        PathLayer = PathLayer,
        PathOffset = PathOffset,
        PathClosed = PathClosed,
        LetterSpacing = LetterSpacing,
        Path = Path is null ? null : [.. Path],
    };

    /// <summary>Copies every setting from another text object.</summary>
    public void CopyFrom(TextData source)
    {
        Content = source.Content;
        FontIndex = source.FontIndex;
        Size = source.Size;
        Bold = source.Bold;
        Italic = source.Italic;
        Align = source.Align;
        Color = source.Color;
        X = source.X;
        Y = source.Y;
        Rotation = source.Rotation;
        Outline = source.Outline;
        OutlineWidth = source.OutlineWidth;
        OutlineColor = source.OutlineColor;
        Shadow = source.Shadow;
        ShadowX = source.ShadowX;
        ShadowY = source.ShadowY;
        ShadowBlur = source.ShadowBlur;
        ShadowOpacity = source.ShadowOpacity;
        ShadowColor = source.ShadowColor;
        PathLayer = source.PathLayer;
        PathOffset = source.PathOffset;
        PathClosed = source.PathClosed;
        LetterSpacing = source.LetterSpacing;
        Path = source.Path is null ? null : [.. source.Path];
    }
}

/// <summary>Rasterises text-layer data into its pixel cache.</summary>
public static class TextOps
{
    /// <summary>Rebuilds a text layer's pixels from its editable text data.</summary>
    /// <param name="layer">The text layer to render.</param>
    /// <param name="document">Document used to resolve a live text-on-path source.</param>
    public static void Render(PaintLayer layer, PaintDocument? document = null)
    {
        if (layer.Text is not { } text)
        {
            return;
        }

        Array.Clear(layer.Pixels);

        if (text.Content.Length == 0 || text.Size < 1f)
        {
            return;
        }

        if (ResolvePath(document, text) is { } path)
        {
            RenderOnPath(layer, text, path.Nodes, path.Closed);
            return;
        }

        RenderStraight(layer, text);
    }

    /// <summary>A path resolved either from a live layer or the text's own copy.</summary>
    public readonly record struct TextPathSource(
        IReadOnlyList<PathNode> Nodes,
        bool Closed);

    /// <summary>Finds the path the text should follow, preferring the live layer.</summary>
    public static TextPathSource? ResolvePath(
        PaintDocument? document,
        TextData text)
    {
        if (document is not null && !string.IsNullOrEmpty(text.PathLayer))
        {
            foreach (PaintLayer layer in document.Layers)
            {
                if (layer.IsShapeLayer
                    && layer.Shape is { Kind: ShapeKind.Path, Points: { Count: >= 2 } points }
                    && string.Equals(layer.Name, text.PathLayer, StringComparison.Ordinal))
                {
                    return new TextPathSource(points, layer.Shape.Closed);
                }
            }
        }

        return text.Path is { Count: >= 2 } fallback
            ? new TextPathSource(fallback, text.PathClosed)
            : null;
    }

    /// <summary>Draws one straight line at its baseline anchor.</summary>
    private static void RenderStraight(PaintLayer layer, TextData text)
    {
        if (!TextRasterizer.RenderStyled(
            TextFonts.At(text.FontIndex),
            text.Content,
            text.Size,
            text.Bold,
            text.Italic,
            text.Align,
            text.EffectStyle,
            out TextArtwork? artwork)
            || artwork is null
            || artwork.Fill.Length == 0)
        {
            return;
        }

        float centerX = text.X + artwork.OriginX + (artwork.Width * 0.5f);
        float centerY = text.Y + artwork.OriginY + (artwork.Height * 0.5f);
        OverlayArtwork(layer, text.EffectStyle, artwork, centerX, centerY, text.Rotation, text.Color);
    }

    /// <summary>Places each glyph along the measured path with its tangent rotation.</summary>
    private static void RenderOnPath(
        PaintLayer layer,
        TextData text,
        IReadOnlyList<PathNode> nodes,
        bool closed)
    {
        List<(float X, float Y)> flat = ShapeOps.Flatten(nodes, closed);
        if (flat.Count < 2)
        {
            return;
        }

        float[] lengths = new float[flat.Count];
        float total = 0f;
        for (int i = 1; i < flat.Count; i++)
        {
            total += Distance(flat[i - 1], flat[i]);
            lengths[i] = total;
        }

        if (closed)
        {
            // The flattened path ends at the last anchor, so add the closing
            // segment to make the cursor wrap back to the beginning.
            total += Distance(flat[^1], flat[0]);
        }

        if (total < 1f)
        {
            return;
        }

        if (!TextRasterizer.MeasureGlyphs(
            TextFonts.At(text.FontIndex),
            text.Content,
            text.Size,
            text.Bold,
            text.Italic,
            out float[] advances))
        {
            return;
        }

        float cursor = Math.Clamp(text.PathOffset, 0f, 1f) * total;

        for (int i = 0; i < text.Content.Length; i++)
        {
            char character = text.Content[i];
            float advance = (i < advances.Length ? advances[i] : 0f) + text.LetterSpacing;
            float center = cursor + (advance * 0.5f);
            cursor += advance;

            if (char.IsWhiteSpace(character) || center < 0f || center > total)
            {
                continue;
            }

            (float gx, float gy, float angle) = PointAt(flat, lengths, total, center);

            if (!TextRasterizer.RenderStyled(
                TextFonts.At(text.FontIndex),
                character.ToString(),
                text.Size,
                text.Bold,
                text.Italic,
                TextAlign.Left,
                text.EffectStyle,
                out TextArtwork? artwork)
                || artwork is null
                || artwork.Fill.Length == 0)
            {
                continue;
            }

            float radians = angle * MathF.PI / 180f;
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            float localX = artwork.OriginX + (artwork.Width * 0.5f);
            float localY = artwork.OriginY + (artwork.Height * 0.5f);
            float centerX = gx + (localX * cos) - (localY * sin);
            float centerY = gy + (localX * sin) + (localY * cos);
            OverlayArtwork(layer, text.EffectStyle, artwork, centerX, centerY, angle, text.Color);
        }
    }

    /// <summary>Blits a rendered glyph set, including its outline and shadow.</summary>
    private static void OverlayArtwork(
        PaintLayer layer,
        TextEffectStyle style,
        TextArtwork artwork,
        float centerX,
        float centerY,
        float rotation,
        uint color)
    {
        if (artwork.Shadow is not null)
        {
            TextRasterizer.Overlay(
                layer,
                null,
                artwork.Shadow,
                artwork.Width,
                artwork.Height,
                centerX,
                centerY,
                rotation,
                style.ShadowTint);
        }

        if (artwork.Outline is not null)
        {
            TextRasterizer.Overlay(
                layer,
                null,
                artwork.Outline,
                artwork.Width,
                artwork.Height,
                centerX,
                centerY,
                rotation,
                style.OutlineColor);
        }

        TextRasterizer.Overlay(
            layer,
            null,
            artwork.Fill,
            artwork.Width,
            artwork.Height,
            centerX,
            centerY,
            rotation,
            color);
    }

    /// <summary>Finds the path point and tangent angle at a distance along it.</summary>
    private static (float X, float Y, float Angle) PointAt(
        List<(float X, float Y)> points,
        float[] lengths,
        float total,
        float distance)
    {
        for (int i = 1; i < points.Count; i++)
        {
            float start = lengths[i - 1];
            float end = lengths[i];
            if (distance <= end || i == points.Count - 1)
            {
                float span = MathF.Max(0.0001f, end - start);
                float t = Math.Clamp((distance - start) / span, 0f, 1f);
                (float x1, float y1) = points[i - 1];
                (float x2, float y2) = points[i];
                float x = x1 + ((x2 - x1) * t);
                float y = y1 + ((y2 - y1) * t);
                return (x, y, MathF.Atan2(y2 - y1, x2 - x1) * 180f / MathF.PI);
            }
        }

        (float fx, float fy) = points[0];
        return (fx, fy, 0f);
    }

    /// <summary>Measures the distance between two points.</summary>
    private static float Distance((float X, float Y) a, (float X, float Y) b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}
