using System;
using System.Collections.Generic;
using System.IO;
using static StbTrueTypeSharp.StbTrueType;

namespace PixelDotNet.Paint;

/// <summary>Rasterises single-line text into a coverage bitmap and blits it transformed.</summary>
/// <remarks>
/// Text is rendered once into a colourless coverage bitmap, so changing the paint
/// colour, position, size or angle only re-blits it. Size changes re-render the
/// bitmap at the new font size, which keeps the glyphs crisp; the angle is applied
/// during the blit so rotation never touches the cached coverage.
/// </remarks>
public static class TextRasterizer
{
    private static readonly Dictionary<(string RegularPath, bool Bold, bool Italic), FontFace?> Faces = [];
    private static readonly object FaceLock = new();

    /// <summary>Gets whether a usable font was found for the requested style.</summary>
    public static bool CanRender(TextFont font, bool bold, bool italic) => Face(font, bold, italic) is not null;

    /// <summary>Renders text into a coverage bitmap laid out around the baseline origin.</summary>
    /// <param name="font">The font family to use.</param>
    /// <param name="text">The single line of text.</param>
    /// <param name="size">Font size in document pixels.</param>
    /// <param name="bold">Whether to use the bold face.</param>
    /// <param name="italic">Whether to use the italic face.</param>
    /// <param name="align">How the line is aligned against the anchor.</param>
    /// <param name="coverage">One byte of coverage per bitmap pixel, row major.</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="originX">Bitmap left, relative to the text origin.</param>
    /// <param name="originY">Bitmap top, relative to the text origin (the baseline).</param>
    /// <returns>False when no font could be loaded.</returns>
    public static bool Render(
        TextFont font,
        string text,
        float size,
        bool bold,
        bool italic,
        TextAlign align,
        out byte[] coverage,
        out int width,
        out int height,
        out int originX,
        out int originY)
    {
        coverage = [];
        width = 0;
        height = 0;
        originX = 0;
        originY = 0;

        if (string.IsNullOrEmpty(text) || size < 1f)
        {
            return true;
        }

        if (Face(font, bold, italic) is not { } face)
        {
            return false;
        }

        float scale = stbtt_ScaleForPixelHeight(face.Info, Math.Clamp(size, 4f, 400f));
        int pad = Math.Max(2, (int)MathF.Round(size * 0.2f));

        int ascent;
        int descent;
        int lineGap;
        unsafe
        {
            stbtt_GetFontVMetrics(face.Info, &ascent, &descent, &lineGap);
        }

        float baselinePixels = ascent * scale;
        float lineHeight = (ascent - descent) * scale;
        float advance = MeasureAdvance(face.Info, text, scale);

        int bitmapWidth = Math.Min(8192, (int)MathF.Ceiling(advance) + (pad * 2));
        int bitmapHeight = Math.Min(8192, (int)MathF.Ceiling(lineHeight) + (pad * 2));
        if (bitmapWidth <= 0 || bitmapHeight <= 0)
        {
            return true;
        }

        coverage = new byte[bitmapWidth * bitmapHeight];
        int baselineY = pad + (int)MathF.Round(baselinePixels);

        unsafe
        {
            int penX = pad;
            int previous = 0;

            foreach (char character in text)
            {
                if (character is '\r' or '\n')
                {
                    continue;
                }

                int codepoint = character;
                if (previous != 0)
                {
                    penX += (int)MathF.Round(stbtt_GetCodepointKernAdvance(face.Info, previous, codepoint) * scale);
                }

                DrawGlyph(coverage, bitmapWidth, bitmapHeight, face.Info, codepoint, penX, baselineY, scale);

                int advanceWidth;
                int bearing;
                stbtt_GetCodepointHMetrics(face.Info, codepoint, &advanceWidth, &bearing);
                penX += (int)MathF.Round(advanceWidth * scale);
                previous = codepoint;
            }
        }

        width = bitmapWidth;
        height = bitmapHeight;
        originX = -pad - (align switch
        {
            TextAlign.Center => (int)MathF.Round(advance * 0.5f),
            TextAlign.Right => (int)MathF.Round(advance),
            _ => 0,
        });
        originY = -baselineY;
        return true;
    }

    /// <summary>Blits a coverage bitmap into a layer, rotated about its centre.</summary>
    public static void Blit(
        PaintLayer layer,
        uint[] source,
        Selection? selection,
        byte[] coverage,
        int width,
        int height,
        int originX,
        int originY,
        float centerX,
        float centerY,
        float rotation,
        uint color)
    {
        Array.Copy(source, layer.Pixels, source.Length);

        if (width <= 0 || height <= 0 || coverage.Length < width * height)
        {
            return;
        }

        float radians = rotation * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        float extentX = (MathF.Abs(cos) * halfWidth) + (MathF.Abs(sin) * halfHeight);
        float extentY = (MathF.Abs(sin) * halfWidth) + (MathF.Abs(cos) * halfHeight);

        int minX = Math.Max(0, (int)MathF.Floor(centerX - extentX) - 1);
        int maxX = Math.Min(layer.Width - 1, (int)MathF.Ceiling(centerX + extentX) + 1);
        int minY = Math.Max(0, (int)MathF.Floor(centerY - extentY) - 1);
        int maxY = Math.Min(layer.Height - 1, (int)MathF.Ceiling(centerY + extentY) + 1);
        uint[] pixels = layer.Pixels;

        for (int y = minY; y <= maxY; y++)
        {
            float dy = y + 0.5f - centerY;
            int row = y * layer.Width;

            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - centerX;

                // Inverse rotation: the point in the unrotated bitmap's space.
                float localX = (dx * cos) + (dy * sin);
                float localY = (-dx * sin) + (dy * cos);

                int sampleX = (int)MathF.Floor(localX + halfWidth);
                int sampleY = (int)MathF.Floor(localY + halfHeight);
                if (sampleX < 0 || sampleY < 0 || sampleX >= width || sampleY >= height)
                {
                    continue;
                }

                int coverageValue = coverage[(sampleY * width) + sampleX];
                if (coverageValue == 0)
                {
                    continue;
                }

                if (selection is not null && !selection.Contains(x, y))
                {
                    continue;
                }

                int index = row + x;
                pixels[index] = Rgba.Over(pixels[index], color, coverageValue);
            }
        }
    }

    /// <summary>Measures the advance width of a line in pixels.</summary>
    private static unsafe float MeasureAdvance(stbtt_fontinfo info, string text, float scale)
    {
        float advance = 0f;
        int previous = 0;

        foreach (char character in text)
        {
            if (character is '\r' or '\n')
            {
                continue;
            }

            int codepoint = character;
            if (previous != 0)
            {
                advance += stbtt_GetCodepointKernAdvance(info, previous, codepoint) * scale;
            }

            int advanceWidth;
            int bearing;
            stbtt_GetCodepointHMetrics(info, codepoint, &advanceWidth, &bearing);
            advance += advanceWidth * scale;
            previous = codepoint;
        }

        return advance;
    }

    /// <summary>Adds one glyph's coverage into the bitmap.</summary>
    private static unsafe void DrawGlyph(
        byte[] coverage,
        int bitmapWidth,
        int bitmapHeight,
        stbtt_fontinfo info,
        int codepoint,
        int penX,
        int baselineY,
        float scale)
    {
        int width;
        int height;
        int xOffset;
        int yOffset;
        byte* bitmap = stbtt_GetCodepointBitmap(info, scale, scale, codepoint, &width, &height, &xOffset, &yOffset);

        if (bitmap is null || width <= 0 || height <= 0)
        {
            return;
        }

        int left = penX + xOffset;
        int top = baselineY + yOffset;

        for (int row = 0; row < height; row++)
        {
            int targetY = top + row;
            if (targetY < 0 || targetY >= bitmapHeight)
            {
                continue;
            }

            byte* sourceRow = bitmap + (row * width);
            int targetRow = targetY * bitmapWidth;

            for (int column = 0; column < width; column++)
            {
                int targetX = left + column;
                if (targetX < 0 || targetX >= bitmapWidth)
                {
                    continue;
                }

                byte value = sourceRow[column];
                if (value > coverage[targetRow + targetX])
                {
                    coverage[targetRow + targetX] = value;
                }
            }
        }

        stbtt_FreeBitmap(bitmap, null);
    }

    /// <summary>Gets or loads the font face for a style, falling back through the family.</summary>
    private static FontFace? Face(TextFont font, bool bold, bool italic)
    {
        var key = (font.FilePath(font.Regular), bold, italic);
        lock (FaceLock)
        {
            if (Faces.TryGetValue(key, out FontFace? cached))
            {
                return cached;
            }

            string style = italic
                ? (bold ? font.BoldItalic : font.Italic)
                : (bold ? font.Bold : font.Regular);

            FontFace? face = Load([
                font.FilePath(style),
                font.FilePath(font.Regular),
                SystemFont("segoeui.ttf"),
                SystemFont("arial.ttf"),
                SystemFont("DejaVuSans.ttf"),
            ]);
            Faces[key] = face;
            return face;
        }
    }

    /// <summary>Gets the candidate path for one of the built-in system fonts.</summary>
    private static string SystemFont(string name)
    {
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return string.IsNullOrEmpty(folder) ? name : Path.Combine(folder, name);
    }

    /// <summary>Loads the first candidate path that exists.</summary>
    private static FontFace? Load(string[] candidates)
    {
        foreach (string path in candidates)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                byte[] data = File.ReadAllBytes(path);
                stbtt_fontinfo? info = CreateFont(data, 0);
                if (info is not null)
                {
                    return new FontFace(info, data);
                }
            }
            catch (IOException)
            {
                // A font that cannot be read is skipped; the next candidate may work.
            }
        }

        return null;
    }

    /// <summary>A loaded face and the bytes that back it.</summary>
    private sealed class FontFace(stbtt_fontinfo info, byte[] data)
    {
        public stbtt_fontinfo Info { get; } = info;

        public byte[] Data { get; } = data;
    }
}
