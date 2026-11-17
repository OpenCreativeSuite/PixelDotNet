using System;
using System.Globalization;
using System.IO;
using Donkeh.LayoutLab;
using Donkeh.UILab;
using Donkeh.Windowing;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;

namespace PixelDotNet.Components;

/// <summary>
/// The document view. A single canvas draws the composited run list every frame,
/// overlays the marquee and brush outline, and forwards pointer input to the session
/// in document coordinates.
/// </summary>
/// <remarks>
/// The canvas box is sized through its <c>width</c>/<c>height</c> attributes, which
/// the layout engine reads as the element's intrinsic size. That is how zoom stays
/// a pure model value with no inline styles: change the zoom, the attribute changes,
/// layout resizes the canvas and the scroll container follows. Author CSS has to
/// clear the user-agent 300x150 default first or the attributes are never consulted.
/// </remarks>
public sealed class CanvasSurface : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        PaintDocument document = editor.Document;
        float zoom = MathF.Max(editor.Zoom, 0.05f);

        int pixelWidth = Math.Max(16, (int)MathF.Round(document.Width * zoom));
        int pixelHeight = Math.Max(16, (int)MathF.Round(document.Height * zoom));

        return Canvas(new CanvasProps
        {
            ClassName = "paint-canvas " + CursorClass(editor.Tool),
            // Focusable so clicking the artwork keeps the stage's key handler
            // live: tool shortcuts, copy and undo all arrive while painting.
            TabIndex = 0,
            Attributes = new()
            {
                ["width"] = pixelWidth.ToString(CultureInfo.InvariantCulture),
                ["height"] = pixelHeight.ToString(CultureInfo.InvariantCulture),
            },
            OnDraw = context => Draw(context, editor),
            OnPointerDown = (x, y) => editor.PointerDown(x, y),
            OnPointerMove = (x, y) => editor.PointerMove(x, y),
            OnPointerUp = (x, y) => editor.PointerUp(x, y),
            OnContextMenu = menu => editor.SecondaryAction(menu.X, menu.Y),
        });
    }

    /// <summary>Maps the active tool onto the canvas cursor class.</summary>
    private static string CursorClass(PaintTool tool) => tool switch
    {
        PaintTool.Move => "is-move",
        PaintTool.Zoom => "is-zoom",
        PaintTool.Text => "is-text",
        _ => "is-draw",
    };

    /// <summary>Draws the artwork, then the marquee and the brush outline.</summary>
    private static void Draw(CanvasContext context, PaintSession editor)
    {
        PaintDocument document = editor.Document;
        float zoom = MathF.Max(editor.Zoom, 0.05f);
        float width = document.Width * zoom;
        float height = document.Height * zoom;

        DrawTransparency(context, width, height, zoom);

        PaintRun[] runs = document.Runs;
        for (int i = 0; i < runs.Length; i++)
        {
            PaintRun run = runs[i];

            // Half a pixel of overlap keeps neighbouring runs from showing seams
            // where their float edges meet.
            context.FillRect(
                run.X * zoom,
                run.Y * zoom,
                (run.Width * zoom) + 0.5f,
                zoom + 0.5f,
                run.Color);
        }

        if (editor.ShowGrid)
        {
            DrawGrid(context, document, zoom);
        }

        DrawMarquee(context, editor, zoom);
        DrawTextTransform(context, editor, zoom);
        DrawBrushOutline(context, editor, zoom);
    }

    /// <summary>Draws the font picker over the stage, each row previewed in its own face.</summary>
    internal static void DrawFontMenu(CanvasContext context, PaintSession editor)
    {
        float x = TextFonts.MenuX;
        float y = TextFonts.MenuY;
        float width = TextFonts.MenuWidth;
        int count = editor.FontMatchCount;
        int rows = editor.FontMenuBodyRows;
        float height = TextFonts.MenuHeight(count);

        context.FillRect(x - 1f, y - 1f, width + 2f, height + 2f, 0xFF0B0C0E);
        context.FillRect(x, y, width, height, 0xFF26292E);

        // Search strip: typed letters filter the list live, with the match count
        // on the right. It behaves like the search field in paint.NET's picker.
        float searchTop = y + TextFonts.MenuPadding;
        float searchLeft = x + 8f;
        float searchWidth = width - 16f;
        float searchHeight = TextFonts.MenuSearchHeight - 8f;
        context.FillRect(searchLeft, searchTop, searchWidth, searchHeight, 0xFF1B1E23);
        context.FillRect(searchLeft, searchTop, searchWidth, 1f, 0xFF3A3F47);
        context.FillRect(searchLeft, searchTop + searchHeight - 1f, searchWidth, 1f, 0xFF3A3F47);
        context.FillRect(searchLeft, searchTop, 1f, searchHeight, 0xFF3A3F47);
        context.FillRect(searchLeft + searchWidth - 1f, searchTop, 1f, searchHeight, 0xFF3A3F47);

        // Small drawn magnifier: the canvas text renderer does not have the icon
        // font loaded, so an icon-font glyph would come out as a tofu box.
        float iconX = searchLeft + 10f;
        float iconY = searchTop + 8f;
        context.FillEllipse(new Rect(iconX, iconY, 8f, 8f), 0xFF8B929C);
        context.FillEllipse(new Rect(iconX + 1.5f, iconY + 1.5f, 5f, 5f), 0xFF1B1E23);
        for (int step = 0; step < 4; step++)
        {
            context.FillRect(iconX + 7f + step, iconY + 7f + step, 1.8f, 1.8f, 0xFF8B929C);
        }

        string query = editor.FontSearch;
        context.DrawText(
            new Rect(searchLeft + 30f, searchTop + 7f, searchWidth - 126f, 18f),
            query.Length == 0 ? "Search fonts…" : query,
            new TextStyle("Segoe UI", 12.5f, 400, false, query.Length == 0 ? 0xFF7F8792 : 0xFFE6EAF0,
                0f, 18f, Donkeh.LayoutLab.TextAlign.Start, TextDecoration.None, WhiteSpace.Normal, Direction.LeftToRight));

        string matchLabel = query.Length == 0
            ? count + " fonts"
            : count == 1
                ? "1 match"
                : count + " matches";
        context.DrawText(
            new Rect(searchLeft + searchWidth - 102f, searchTop + 8f, 92f, 16f),
            matchLabel,
            new TextStyle("Segoe UI", 10.5f, 400, false, count == 0 ? 0xFFE27777 : 0xFF8B929C,
                0f, 16f, Donkeh.LayoutLab.TextAlign.End, TextDecoration.None, WhiteSpace.Normal, Direction.LeftToRight));

        bool overflow = editor.FontMenuHasOverflow;
        float sampleWidth = width - 184f - (overflow ? 14f : 0f);
        if (count == 0)
        {
            context.DrawText(
                new Rect(x + 16f, TextFonts.BodyTop + 8f, width - 32f, 18f),
                "No fonts match that search",
                new TextStyle("Segoe UI", 12f, 400, false, 0xFF8B929C, 0f, 18f,
                    Donkeh.LayoutLab.TextAlign.Start, TextDecoration.None, WhiteSpace.Normal, Direction.LeftToRight));
        }
        else
        {
            int scroll = Math.Clamp(editor.FontMenuScroll, 0, editor.FontMenuMaxScroll);
            for (int slot = 0; slot < rows; slot++)
            {
                int match = scroll + slot;
                if (match >= count)
                {
                    break;
                }

                int index = editor.FontMatchAt(match);
                float top = TextFonts.BodyTop + (slot * TextFonts.MenuRowHeight);
                bool selected = index == editor.TextFontIndex;

                if (selected)
                {
                    context.FillRect(x + 4f, top + 1f, width - 8f, TextFonts.MenuRowHeight - 2f, 0xFF2B3A4E);
                }

                TextFont font = TextFonts.All[index];
                uint nameColor = selected ? 0xFFFFFFFF : 0xFFE6EAF0;
                uint sampleColor = selected ? 0xFFDCEAFF : 0xFFC3C8D1;

                // The family name is set in its own face, like the paint.NET picker.
                // Downloaded families are not known to the UI's font manager, so their
                // names are set in Segoe UI and the sample is a pre-rendered bitmap.
                string nameFace = font.IsDownloaded ? "Segoe UI" : font.Name;
                context.DrawText(
                    new Rect(x + 10f, top + 8f, 156f, 18f),
                    font.Name,
                    new TextStyle(nameFace, 13.5f, 400, false, nameColor, 0f, 17f,
                        Donkeh.LayoutLab.TextAlign.Start, TextDecoration.None, WhiteSpace.Normal, Direction.LeftToRight));

                Rect sampleRect = new(x + 174f, top + 6f, sampleWidth, 21f);
                string? preview = FontCatalog.PreviewPath(font);
                if (preview is not null && File.Exists(preview))
                {
                    context.DrawImage(sampleRect, preview, ObjectFit.Contain, sampleColor);
                }
                else
                {
                    context.DrawText(
                        new Rect(x + 174f, top + 3f, sampleWidth, 28f),
                        "The quick brown fox",
                        new TextStyle(nameFace, 18f, 400, false, sampleColor, 0f, 22f,
                            Donkeh.LayoutLab.TextAlign.Start, TextDecoration.None, WhiteSpace.Normal, Direction.LeftToRight));
                }
            }
        }

        if (overflow)
        {
            float trackX = TextFonts.ScrollbarX;
            float trackTop = TextFonts.BodyTop + 4f;
            float trackHeight = (rows * TextFonts.MenuRowHeight) - 8f;
            context.FillRect(trackX, trackTop, TextFonts.MenuScrollbarWidth, trackHeight, 0xFF1C1F24);

            int maximum = editor.FontMenuMaxScroll;
            float thumbHeight = MathF.Max(24f, trackHeight * TextFonts.MaxVisibleRows / count);
            float thumbTop = trackTop;
            if (maximum > 0)
            {
                thumbTop += (trackHeight - thumbHeight) * (editor.FontMenuScroll / (float)maximum);
            }

            context.FillRect(trackX, thumbTop, TextFonts.MenuScrollbarWidth, thumbHeight, 0xFF5A616C);
        }

        // Last row: a route to the cdnfonts import controls in the options bar.
        float footerTop = TextFonts.BodyTop + (rows * TextFonts.MenuRowHeight);
        context.FillRect(x + 8f, footerTop, width - 16f, 1f, 0xFF3A3F47);
        context.DrawText(
            new Rect(x + 10f, footerTop + 9f, width - 20f, 18f),
            "+  Add a font from cdnfonts…",
            new TextStyle("Segoe UI", 12f, 400, false, 0xFF8FB8E8, 0f, 17f,
                Donkeh.LayoutLab.TextAlign.Start, TextDecoration.None, WhiteSpace.Normal, Direction.LeftToRight));
        context.DrawText(
            new Rect(x + width - 218f, footerTop + 10f, 208f, 16f),
            "type to search  ·  ↑↓  ·  Enter",
            new TextStyle("Segoe UI", 9.5f, 400, false, 0xFF7F8792, 0f, 16f,
                Donkeh.LayoutLab.TextAlign.End, TextDecoration.None, WhiteSpace.Normal, Direction.LeftToRight));
    }

    /// <summary>Draws the live text box, its corner handles and the rotate handle.</summary>
    private static void DrawTextTransform(CanvasContext context, PaintSession editor, float zoom)
    {
        if (!editor.HasTextPreview || editor.TextContent.Length == 0 || editor.TextExtent.X <= 0f)
        {
            return;
        }

        (float centerX, float centerY) = editor.TextCenter;
        (float halfWidth, float halfHeight) = editor.TextExtent;
        float radians = editor.TextRotation * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        const uint Accent = 0xFF2F8DF0;

        (float X, float Y) Point(float localX, float localY) => (
            (centerX + (localX * cos) - (localY * sin)) * zoom,
            (centerY + (localX * sin) + (localY * cos)) * zoom);

        (float X, float Y) topLeft = Point(-halfWidth, -halfHeight);
        (float X, float Y) topRight = Point(halfWidth, -halfHeight);
        (float X, float Y) bottomRight = Point(halfWidth, halfHeight);
        (float X, float Y) bottomLeft = Point(-halfWidth, halfHeight);

        Stroke(context, topLeft, topRight, Accent);
        Stroke(context, topRight, bottomRight, Accent);
        Stroke(context, bottomRight, bottomLeft, Accent);
        Stroke(context, bottomLeft, topLeft, Accent);

        (float X, float Y) topCenter = Point(0f, -halfHeight);
        (float X, float Y) rotate = Point(0f, -halfHeight - (18f / zoom));
        Stroke(context, topCenter, rotate, Accent);

        Handle(context, topLeft);
        Handle(context, topRight);
        Handle(context, bottomRight);
        Handle(context, bottomLeft);
        Handle(context, rotate);
    }

    /// <summary>Draws a thin line between two screen points.</summary>
    private static void Stroke(CanvasContext context, (float X, float Y) from, (float X, float Y) to, uint color)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        int steps = (int)MathF.Ceiling(MathF.Sqrt((dx * dx) + (dy * dy)));
        if (steps <= 0)
        {
            return;
        }

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            context.FillRect(from.X + (dx * t), from.Y + (dy * t), 1.5f, 1.5f, color);
        }
    }

    /// <summary>Draws one white transform handle with a dark rim.</summary>
    private static void Handle(CanvasContext context, (float X, float Y) point)
    {
        context.FillRect(point.X - 4.5f, point.Y - 4.5f, 9f, 9f, 0xFF0B0C0E);
        context.FillRect(point.X - 3f, point.Y - 3f, 6f, 6f, 0xFFFFFFFF);
    }

    /// <summary>
    /// Paints the grey and white transparency grid under the artwork.
    /// </summary>
    /// <remarks>
    /// The cell grows with the zoom and is clamped, so a deeply zoomed canvas
    /// keeps roughly the same number of cells instead of turning into thousands of
    /// tiny rectangles.
    /// </remarks>
    private static void DrawTransparency(CanvasContext context, float width, float height, float zoom)
    {
        const uint Light = 0xFFFFFFFF;
        const uint Dark = 0xFFC8C8C8;
        float cell = Math.Clamp(8f * zoom, 8f, 32f);

        context.Clear(Light);

        int columns = (int)MathF.Ceiling(width / cell);
        int rows = (int)MathF.Ceiling(height / cell);

        for (int row = 0; row < rows; row++)
        {
            float top = row * cell;
            float cellHeight = MathF.Min(cell, height - top);

            for (int column = row & 1; column < columns; column += 2)
            {
                float left = column * cell;
                context.FillRect(left, top, MathF.Min(cell, width - left), cellHeight, Dark);
            }
        }
    }

    /// <summary>Draws an eight-pixel guide grid over the artwork.</summary>
    private static void DrawGrid(CanvasContext context, PaintDocument document, float zoom)
    {
        float width = document.Width * zoom;
        float height = document.Height * zoom;
        const uint line = 0x22000000;

        for (int x = 8; x < document.Width; x += 8)
        {
            context.FillRect(x * zoom, 0f, 1f, height, line);
        }

        for (int y = 8; y < document.Height; y += 8)
        {
            context.FillRect(0f, y * zoom, width, 1f, line);
        }
    }

    /// <summary>Draws the selection dashes and, with the Move tool, its resize handles.</summary>
    private static void DrawMarquee(CanvasContext context, PaintSession editor, float zoom)
    {
        if (editor.Selection is not { } selection)
        {
            return;
        }

        float left = selection.X * zoom;
        float top = selection.Y * zoom;
        float right = selection.Right * zoom;
        float bottom = selection.Bottom * zoom;

        Dash(context, left, top, right, top);
        Dash(context, right, top, right, bottom);
        Dash(context, right, bottom, left, bottom);
        Dash(context, left, bottom, left, top);

        if (editor.Tool == PaintTool.Move)
        {
            DrawHandles(context, left, top, right, bottom);
        }
    }

    /// <summary>Draws the eight white resize handles around the marquee.</summary>
    private static void DrawHandles(CanvasContext context, float left, float top, float right, float bottom)
    {
        float midX = (left + right) * 0.5f;
        float midY = (top + bottom) * 0.5f;
        (float X, float Y)[] anchors =
        [
            (left, top),
            (midX, top),
            (right, top),
            (left, midY),
            (right, midY),
            (left, bottom),
            (midX, bottom),
            (right, bottom),
        ];

        foreach ((float x, float y) in anchors)
        {
            context.FillRect(x - 4.5f, y - 4.5f, 9f, 9f, 0xFF0B0C0E);
            context.FillRect(x - 3f, y - 3f, 6f, 6f, 0xFFFFFFFF);
        }
    }

    /// <summary>Draws one dashed edge of the marquee.</summary>
    private static void Dash(CanvasContext context, float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1f)
        {
            return;
        }

        float ux = dx / length;
        float uy = dy / length;
        int steps = (int)(length / 8f);
        bool dark = false;

        for (int i = 0; i <= steps; i++)
        {
            float start = i * 8f;
            float size = MathF.Min(4f, length - start);
            if (size <= 0f)
            {
                break;
            }

            context.FillRect(
                x0 + (ux * start),
                y0 + (uy * start) - (uy == 0f ? 0f : 1f),
                ux == 0f ? 2f : size,
                uy == 0f ? 2f : size,
                dark ? 0xFF101010 : 0xFFFFFFFF);

            dark = !dark;
        }
    }

    /// <summary>Draws a dotted ring around the pointer at the current brush size.</summary>
    private static void DrawBrushOutline(CanvasContext context, PaintSession editor, float zoom)
    {
        if (editor.Tool is not (PaintTool.Brush or PaintTool.Eraser or PaintTool.Line))
        {
            return;
        }

        if (editor.CursorX < 0 || editor.CursorY < 0)
        {
            return;
        }

        int x = editor.CursorX;
        int y = editor.CursorY;
        if (x >= editor.Document.Width || y >= editor.Document.Height)
        {
            return;
        }

        float radius = MathF.Max(1.5f, editor.BrushSize * 0.5f * zoom);
        float centerX = ((x + 0.5f) * zoom);
        float centerY = ((y + 0.5f) * zoom);
        const int Steps = 40;

        for (int i = 0; i < Steps; i++)
        {
            float angle = i / (float)Steps * MathF.PI * 2f;
            float px = centerX + (MathF.Cos(angle) * radius);
            float py = centerY + (MathF.Sin(angle) * radius);
            context.FillRect(px - 1.5f, py - 1.5f, 3f, 3f, 0xB0000000);
            context.FillRect(px - 0.75f, py - 0.75f, 1.5f, 1.5f, 0xFFFFFFFF);
        }
    }
}
