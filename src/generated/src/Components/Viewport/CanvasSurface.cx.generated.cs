using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>Thickness of both ruler bands, shared with the scroll layout.</summary>
    internal const float RulerSize = 30f;

    /// <inheritdoc/>
    public override Node Render()
    {
        PaintSession editor = UseContext(EditorContext.Value);
        PaintDocument document = editor.Document;
        float zoom = MathF.Max(editor.Zoom, 0.05f);

        int pixelWidth = Math.Max(16, (int)MathF.Round(document.Width * zoom));
        int pixelHeight = Math.Max(16, (int)MathF.Round(document.Height * zoom));

        Ref<ElementNode> paintRef = UseRef<ElementNode>();
        Node paint = PaintCanvas(editor, pixelWidth, pixelHeight, paintRef);

        if (!editor.ShowRulers)
        {
            return paint;
        }

        return _.H("div", new() { className = "canvas-frame" }, _.H("div", new() { className = "ruler-strip" }, Canvas(new CanvasProps
                {
                    className = "ruler-corner",
                    Attributes = Ui.Size((int)RulerSize, (int)RulerSize),
                    OnDraw = context => context.Clear(0xFF121318),
                }), Canvas(new CanvasProps
                {
                    className = "ruler ruler-top",
                    Attributes = Ui.Size(pixelWidth, (int)RulerSize),
                    OnDraw = context => DrawRuler(context, editor, horizontal: true, pixelWidth),
                    OnPointerDown = (x, y) => editor.PlaceGuide(horizontal: false, x / zoom),
                })), _.H("div", new() { className = "ruler-strip" }, Canvas(new CanvasProps
                {
                    className = "ruler ruler-left",
                    Attributes = Ui.Size((int)RulerSize, pixelHeight),
                    OnDraw = context => DrawRuler(context, editor, horizontal: false, pixelHeight),
                    OnPointerDown = (x, y) => editor.PlaceGuide(horizontal: true, y / zoom),
                }), paint));
    }

    /// <summary>Builds the interactive artwork canvas.</summary>
    private static Node PaintCanvas(
        PaintSession editor,
        int pixelWidth,
        int pixelHeight,
        Ref<ElementNode> paintRef)
    {
        Node canvas = Canvas(new CanvasProps
        {
            className = "paint-canvas " + CursorClass(editor.Tool),
            // Focusable so clicking the artwork keeps the stage's key handler
            // live: tool shortcuts, copy and undo all arrive while painting.
            TabIndex = 0,
            Attributes = new()
            {
                ["width"] = pixelWidth.ToString(CultureInfo.InvariantCulture),
                ["height"] = pixelHeight.ToString(CultureInfo.InvariantCulture),
            },
            OnDraw = context => Draw(context, editor, paintRef.Value),
            OnPointerDown = (x, y) =>
            {
                if (editor.IsMeshWarpOpen)
                {
                    float zoom = MathF.Max(editor.Zoom, 0.05f);
                    editor.BeginMeshWarpDrag(x / zoom, y / zoom, zoom);
                    return;
                }

                if (InputState.Control && editor.Tool is PaintTool.CloneStamp or PaintTool.HealingBrush)
                {
                    editor.SetCloneSource(x, y);
                }
                else
                {
                    editor.PointerDown(x, y);
                }
            },
            OnPointerMove = (x, y) =>
            {
                if (ViewportPan.Handle())
                {
                    return;
                }

                if (editor.IsMeshWarpOpen)
                {
                    float zoom = MathF.Max(editor.Zoom, 0.05f);
                    if (editor.MeshWarpActiveNode >= 0)
                    {
                        editor.UpdateMeshWarpDrag(x / zoom, y / zoom);
                    }

                    return;
                }

                editor.PointerMove(x, y);
            },
            OnPointerUp = (x, y) =>
            {
                if (editor.IsMeshWarpOpen)
                {
                    editor.EndMeshWarpDrag();
                    return;
                }

                editor.PointerUp(x, y);
            },
            OnScroll = (horizontal, vertical) =>
            {
                if (InputState.Control)
                {
                    editor.ZoomByWheel(vertical);
                }
                else if (InputState.Alt)
                {
                    editor.NudgeBrushSize(vertical);
                }
            },
            OnContextMenu = menu => editor.SecondaryAction(menu.X, menu.Y),
        });

        if (canvas is ElementNode element)
        {
            element.BindRef(paintRef);
        }

        return canvas;
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
    private static void Draw(CanvasContext context, PaintSession editor, ElementNode? canvasNode)
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

        DrawGuides(context, editor, zoom);
        DrawSelectionOverlay(context, editor, zoom);
        DrawMarquee(context, editor, zoom);
        DrawLassoPreview(context, editor, zoom);
        DrawCloneSource(context, editor, zoom);
        DrawTextTransform(context, editor, zoom);
        DrawBrushOutline(context, editor, zoom, canvasNode);
        DrawMeshWarp(context, editor, zoom);
        DrawPenPath(context, editor, zoom);
        DrawPathNodes(context, editor, zoom);
    }

    /// <summary>Draws path anchors, handles and the highlighted outline.</summary>
    private static void DrawPathNodes(CanvasContext context, PaintSession editor, float zoom)
    {
        if (!editor.IsPathNodeEditing || editor.PathNodeShape is not { } shape)
        {
            return;
        }

        if (shape.Points is not { Count: >= 2 } points)
        {
            return;
        }

        List<(float X, float Y)> flat = ShapeOps.Flatten(shape);
        for (int i = 0; i + 1 < flat.Count; i++)
        {
            DrawLine(
                context,
                flat[i].X * zoom,
                flat[i].Y * zoom,
                flat[i + 1].X * zoom,
                flat[i + 1].Y * zoom,
                0x99FF2D87);
        }

        for (int i = 0; i < points.Count; i++)
        {
            PathNode node = points[i];
            float x = node.X * zoom;
            float y = node.Y * zoom;

            if (node.HasHandles)
            {
                DrawLine(context, x, y, node.InX * zoom, node.InY * zoom, 0xCCFF2D87);
                DrawLine(context, x, y, node.OutX * zoom, node.OutY * zoom, 0xCCFF2D87);
                context.FillEllipse(
                    new Rect((node.InX * zoom) - 3f, (node.InY * zoom) - 3f, 6f, 6f),
                    0xFFFF2D87);
                context.FillEllipse(
                    new Rect((node.OutX * zoom) - 3f, (node.OutY * zoom) - 3f, 6f, 6f),
                    0xFFFF2D87);
            }

            bool selected = i == editor.SelectedPathNode;
            float size = selected ? 13f : 9f;
            context.FillEllipse(
                new Rect(x - (size * 0.5f), y - (size * 0.5f), size, size),
                selected ? 0xFFFF2D87 : 0xFFFFFFFF);
            context.FillEllipse(
                new Rect(x - 2.5f, y - 2.5f, 5f, 5f),
                0xFF06070A);
        }
    }

    /// <summary>Draws the in-progress pen path and its anchor points.</summary>
    private static void DrawPenPath(CanvasContext context, PaintSession editor, float zoom)
    {
        if (!editor.IsPenActive || editor.Tool != PaintTool.Pen)
        {
            return;
        }

        IReadOnlyList<PathNode> points = editor.PenPoints;
        const uint Line = 0xCCFF2D87;

        for (int i = 0; i + 1 < points.Count; i++)
        {
            DrawLine(
                context,
                points[i].X * zoom,
                points[i].Y * zoom,
                points[i + 1].X * zoom,
                points[i + 1].Y * zoom,
                Line);
        }

        PathNode last = points[^1];
        DrawLine(
            context,
            last.X * zoom,
            last.Y * zoom,
            editor.PenPreviewX * zoom,
            editor.PenPreviewY * zoom,
            0x99FF2D87);

        for (int i = 0; i < points.Count; i++)
        {
            PathNode node = points[i];
            float x = node.X * zoom;
            float y = node.Y * zoom;
            bool first = i == 0 && points.Count >= 3;

            if (node.HasHandles)
            {
                DrawLine(context, x, y, node.InX * zoom, node.InY * zoom, 0x99FF2D87);
                DrawLine(context, x, y, node.OutX * zoom, node.OutY * zoom, 0x99FF2D87);
                context.FillEllipse(
                    new Rect((node.InX * zoom) - 2f, (node.InY * zoom) - 2f, 4f, 4f),
                    0xFFFF2D87);
                context.FillEllipse(
                    new Rect((node.OutX * zoom) - 2f, (node.OutY * zoom) - 2f, 4f, 4f),
                    0xFFFF2D87);
            }

            context.FillEllipse(
                new Rect(x - 4.5f, y - 4.5f, 9f, 9f),
                first ? 0xFFFF2D87 : 0xFFFFFFFF);
            context.FillEllipse(
                new Rect(x - 2.5f, y - 2.5f, 5f, 5f),
                0xFF06070A);
        }
    }

    /// <summary>Draws the mesh warp grid and its draggable control points.</summary>
    private static void DrawMeshWarp(CanvasContext context, PaintSession editor, float zoom)
    {
        if (!editor.IsMeshWarpOpen)
        {
            return;
        }

        int points = MeshWarpOps.Points;
        const uint Line = 0xCCFF2D87;
        const uint Node = 0xFFFFFFFF;
        const uint ActiveNode = 0xFFFF2D87;

        for (int row = 0; row < points; row++)
        {
            for (int column = 0; column < points; column++)
            {
                int node = (row * points) + column;
                (float x, float y) = editor.MeshNodePosition(node);
                float canvasX = x * zoom;
                float canvasY = y * zoom;

                if (column < points - 1)
                {
                    (float nextX, float nextY) = editor.MeshNodePosition(node + 1);
                    DrawLine(context, canvasX, canvasY, nextX * zoom, nextY * zoom, Line);
                }

                if (row < points - 1)
                {
                    (float nextX, float nextY) = editor.MeshNodePosition(node + points);
                    DrawLine(context, canvasX, canvasY, nextX * zoom, nextY * zoom, Line);
                }
            }
        }

        for (int node = 0; node < points * points; node++)
        {
            (float x, float y) = editor.MeshNodePosition(node);
            float canvasX = x * zoom;
            float canvasY = y * zoom;
            uint color = node == editor.MeshWarpActiveNode ? ActiveNode : Node;
            context.FillEllipse(
                new Rect(canvasX - 5f, canvasY - 5f, 10f, 10f),
                0xCC101215);
            context.FillEllipse(
                new Rect(canvasX - 3.5f, canvasY - 3.5f, 7f, 7f),
                color);
        }
    }

    /// <summary>Draws a short line as a run of small rectangles.</summary>
    private static void DrawLine(
        CanvasContext context,
        float x1,
        float y1,
        float x2,
        float y2,
        uint color)
    {
        float dx = x2 - x1;
        float dy = y2 - y1;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        int steps = Math.Max(1, (int)MathF.Ceiling(length / 3f));

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            context.FillRect(
                x1 + (dx * t) - 1f,
                y1 + (dy * t) - 1f,
                2f,
                2f,
                color);
        }
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
        const uint Accent = 0xFFFF2D87;

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
        context.FillRect(point.X - 4.5f, point.Y - 4.5f, 9f, 9f, 0xFF05060A);
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

    /// <summary>Draws the document guide lines over the artwork.</summary>
    private static void DrawGuides(CanvasContext context, PaintSession editor, float zoom)
    {
        if (!editor.ShowGuides)
        {
            return;
        }

        float width = editor.Document.Width * zoom;
        float height = editor.Document.Height * zoom;

        foreach (Guide guide in editor.Document.Guides)
        {
            if (guide.Horizontal)
            {
                float y = guide.Position * zoom;
                context.FillRect(0f, y, width, 1.5f, 0xCCFF2D87);
            }
            else
            {
                float x = guide.Position * zoom;
                context.FillRect(x, 0f, 1.5f, height, 0xCCFF2D87);
            }
        }
    }

    /// <summary>Draws one ruler with ticks and document-space labels.</summary>
    private static void DrawRuler(
        CanvasContext context,
        PaintSession editor,
        bool horizontal,
        float length)
    {
        context.Clear(0xFF121318);
        float zoom = MathF.Max(editor.Zoom, 0.05f);

        // Pick a step whose on-screen spacing leaves room for a label.
        int[] steps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];
        int step = steps[^1];
        foreach (int candidate in steps)
        {
            if (candidate * zoom >= 46f)
            {
                step = candidate;
                break;
            }
        }

        int documentLength = horizontal ? editor.Document.Width : editor.Document.Height;
        var labelStyle = new TextStyle(
            "Segoe UI",
            9.5f,
            400,
            false,
            0xFF9CA3B1,
            0f,
            1.1f,
            Donkeh.LayoutLab.TextAlign.Left,
            TextDecoration.None,
            WhiteSpace.NoWrap,
            Direction.LeftToRight);

        // A little breathing room inside the bar keeps labels off the edges now
        // that the ruler is thicker.
        const float LabelInset = 4f;
        const float TickLength = 7f;

        for (int position = 0; position <= documentLength; position += step)
        {
            float offset = position * zoom;
            if (offset > length)
            {
                break;
            }

            if (horizontal)
            {
                context.FillRect(offset, RulerSize - TickLength, 1f, TickLength, 0xFF8B919F);
                context.DrawText(
                    new Rect(
                        offset + LabelInset,
                        RulerSize - TickLength - 12f,
                        46f,
                        12f),
                    position.ToString(CultureInfo.InvariantCulture),
                    labelStyle);
            }
            else
            {
                context.FillRect(RulerSize - TickLength, offset, TickLength, 1f, 0xFF8B919F);
                context.DrawText(
                    new Rect(LabelInset, offset + 3f, RulerSize - LabelInset - 2f, 12f),
                    position.ToString(CultureInfo.InvariantCulture),
                    labelStyle);
            }
        }

        if (horizontal)
        {
            context.FillRect(0f, RulerSize - 1f, length, 1f, 0xFF06070A);
        }
        else
        {
            context.FillRect(RulerSize - 1f, 0f, 1f, length, 0xFF06070A);
        }
    }

    /// <summary>Draws the soft pink selection tint.</summary>
    private static void DrawSelectionOverlay(CanvasContext context, PaintSession editor, float zoom)
    {
        if (editor.Selection is not { } selection || selection.IsEmpty)
        {
            return;
        }

        const uint Tint = 0x33FF2D87;

        if (selection.Shape == SelectionShape.Mask && selection.Mask is { } mask)
        {
            int width = selection.Width;
            int height = selection.Height;

            for (int row = 0; row < height; row++)
            {
                int rowStart = row * width;
                int column = 0;

                while (column < width)
                {
                    while (column < width && mask[rowStart + column] == 0)
                    {
                        column++;
                    }

                    int start = column;
                    while (column < width && mask[rowStart + column] != 0)
                    {
                        column++;
                    }

                    if (column > start)
                    {
                        context.FillRect(
                            (selection.X + start) * zoom,
                            (selection.Y + row) * zoom,
                            ((column - start) * zoom) + 0.5f,
                            zoom + 0.5f,
                            Tint);
                    }
                }
            }

            return;
        }

        if (selection.Shape == SelectionShape.Ellipse)
        {
            context.FillEllipse(
                new Rect(
                    selection.X * zoom,
                    selection.Y * zoom,
                    selection.Width * zoom,
                    selection.Height * zoom),
                Tint);
            return;
        }

        context.FillRect(
            selection.X * zoom,
            selection.Y * zoom,
            selection.Width * zoom,
            selection.Height * zoom,
            Tint);
    }

    /// <summary>Draws the selection dashes and, with the Move tool, its resize handles.</summary>
    private static void DrawMarquee(CanvasContext context, PaintSession editor, float zoom)
    {
        if (editor.Selection is not { } selection)
        {
            return;
        }

        if (selection.Shape == SelectionShape.Mask)
        {
            DrawMaskMarquee(context, selection, zoom);
        }
        else if (selection.Shape == SelectionShape.Ellipse)
        {
            DrawEllipseMarquee(context, selection, zoom);
        }
        else
        {
            float left = selection.X * zoom;
            float top = selection.Y * zoom;
            float right = selection.Right * zoom;
            float bottom = selection.Bottom * zoom;

            Dash(context, left, top, right, top);
            Dash(context, right, top, right, bottom);
            Dash(context, right, bottom, left, bottom);
            Dash(context, left, bottom, left, top);
        }

        if (editor.Tool == PaintTool.Move && selection.CanResize)
        {
            DrawHandles(
                context,
                selection.X * zoom,
                selection.Y * zoom,
                selection.Right * zoom,
                selection.Bottom * zoom);
        }
    }

    /// <summary>Draws the boundary of an arbitrary selection mask.</summary>
    private static void DrawMaskMarquee(CanvasContext context, Selection selection, float zoom)
    {
        if (selection.Mask is not { } mask)
        {
            return;
        }

        int width = selection.Width;
        int height = selection.Height;
        int drawn = 0;
        const int Limit = 24000;

        for (int row = 0; row < height && drawn < Limit; row++)
        {
            int rowStart = row * width;
            for (int column = 0; column < width && drawn < Limit; column++)
            {
                if (mask[rowStart + column] == 0)
                {
                    continue;
                }

                float x = (selection.X + column) * zoom;
                float y = (selection.Y + row) * zoom;
                uint color = (((selection.X + column) + (selection.Y + row)) / 4) % 2 == 0
                    ? 0xFF101010
                    : 0xFFFFFFFF;

                if (column == 0 || mask[rowStart + column - 1] == 0)
                {
                    context.FillRect(x, y, 1.5f, zoom + 0.5f, color);
                    drawn++;
                }

                if (column == width - 1 || mask[rowStart + column + 1] == 0)
                {
                    context.FillRect(x + zoom - 1.5f, y, 1.5f, zoom + 0.5f, color);
                    drawn++;
                }

                if (row == 0 || mask[rowStart + column - width] == 0)
                {
                    context.FillRect(x, y, zoom + 0.5f, 1.5f, color);
                    drawn++;
                }

                if (row == height - 1 || mask[rowStart + column + width] == 0)
                {
                    context.FillRect(x, y + zoom - 1.5f, zoom + 0.5f, 1.5f, color);
                    drawn++;
                }
            }
        }
    }

    /// <summary>Draws an elliptical selection as a dashed closed curve.</summary>
    private static void DrawEllipseMarquee(CanvasContext context, Selection selection, float zoom)
    {
        float left = selection.X * zoom;
        float top = selection.Y * zoom;
        float right = selection.Right * zoom;
        float bottom = selection.Bottom * zoom;
        float centerX = (left + right) * 0.5f;
        float centerY = (top + bottom) * 0.5f;
        float radiusX = (right - left) * 0.5f;
        float radiusY = (bottom - top) * 0.5f;
        if (radiusX <= 0f || radiusY <= 0f)
        {
            return;
        }

        int steps = Math.Clamp((int)(MathF.PI * (radiusX + radiusY) * 0.5f / 6f), 24, 240);
        (float X, float Y) previous = (centerX + radiusX, centerY);

        for (int step = 1; step <= steps; step++)
        {
            float angle = step * MathF.PI * 2f / steps;
            (float X, float Y) current = (
                centerX + (radiusX * MathF.Cos(angle)),
                centerY + (radiusY * MathF.Sin(angle)));
            Dash(context, previous.X, previous.Y, current.X, current.Y);
            previous = current;
        }
    }

    /// <summary>Draws the open freehand path while a lasso drag is in progress.</summary>
    private static void DrawLassoPreview(CanvasContext context, PaintSession editor, float zoom)
    {
        IReadOnlyList<(float X, float Y)> points = editor.LassoPoints;
        if (points.Count < 2)
        {
            return;
        }

        (float X, float Y) previous = (points[0].X * zoom, points[0].Y * zoom);
        for (int index = 1; index < points.Count; index++)
        {
            (float X, float Y) current = (points[index].X * zoom, points[index].Y * zoom);
            Stroke(context, previous, current, index % 2 == 0 ? 0xFFFFFFFF : 0xFF101010);
            previous = current;
        }

        if (points.Count > 3)
        {
            Stroke(context, previous, (points[0].X * zoom, points[0].Y * zoom), 0xFF9CA3B1);
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
            context.FillRect(x - 4.5f, y - 4.5f, 9f, 9f, 0xFF05060A);
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

    /// <summary>Draws a small crosshair at the clone/healing sample point.</summary>
    private static void DrawCloneSource(CanvasContext context, PaintSession editor, float zoom)
    {
        if (!editor.HasCloneSource || editor.Tool is not (PaintTool.CloneStamp or PaintTool.HealingBrush))
        {
            return;
        }

        float x = ((editor.CloneMarkerX >= 0 ? editor.CloneMarkerX : editor.CloneSourceX) + 0.5f) * zoom;
        float y = ((editor.CloneMarkerY >= 0 ? editor.CloneMarkerY : editor.CloneSourceY) + 0.5f) * zoom;
        const uint Dark = 0xFF05060A;
        const uint Light = 0xFFFFFFFF;

        context.FillRect(x - 8f, y - 1f, 16f, 2f, Dark);
        context.FillRect(x - 1f, y - 8f, 2f, 16f, Dark);
        context.FillRect(x - 7f, y - 0.5f, 14f, 1f, Light);
        context.FillRect(x - 0.5f, y - 7f, 1f, 14f, Light);
        context.FillRect(x - 1.5f, y - 1.5f, 3f, 3f, 0xFFFF2D87);
    }

    /// <summary>Projects the window pointer into a canvas's local coordinates.</summary>
    /// <param name="canvas">The paint canvas.</param>
    /// <param name="localX">Receives the pointer X relative to the canvas border box.</param>
    /// <param name="localY">Receives the pointer Y relative to the canvas border box.</param>
    /// <returns>Whether the pointer is inside the canvas.</returns>
    /// <remarks>Node bounds are painted coordinates, so they can be compared
    /// against the window pointer directly, even after scrolling.</remarks>
    private static bool TryGetPointerOnCanvas(
        ElementNode canvas,
        out float localX,
        out float localY)
    {
        localX = InputState.PointerX - canvas.PositionX;
        localY = InputState.PointerY - canvas.PositionY;

        return localX >= 0f && localY >= 0f
            && localX < canvas.Width && localY < canvas.Height;
    }

    /// <summary>Draws a dotted ring under the pointer at the current brush size.</summary>
    /// <remarks>
    /// The ring is positioned from the window pointer and the canvas box, not
    /// from the last press. Hover does not dispatch pointer moves to the
    /// component, so that is the only position available when no stroke is
    /// active. It also hides naturally as soon as the pointer leaves the canvas.
    /// </remarks>
    private static void DrawBrushOutline(
        CanvasContext context,
        PaintSession editor,
        float zoom,
        ElementNode? canvasNode)
    {
        if (editor.Tool is not (
            PaintTool.Brush
            or PaintTool.Eraser
            or PaintTool.Line
            or PaintTool.CloneStamp
            or PaintTool.HealingBrush
            or PaintTool.DodgeBurn
            or PaintTool.QuickSelect))
        {
            return;
        }

        // A pan or a mesh drag owns the pointer; a brush ring is visual noise.
        if (InputState.Middle || editor.IsMeshWarpOpen)
        {
            return;
        }

        // Fallback for the frame before the canvas ref is bound.
        float localX = (editor.CursorX + 0.5f) * zoom;
        float localY = (editor.CursorY + 0.5f) * zoom;

        if (canvasNode is { Width: > 0f, Height: > 0f }
            && TryGetPointerOnCanvas(canvasNode, out float pointerX, out float pointerY))
        {
            localX = pointerX;
            localY = pointerY;
        }
        else if (canvasNode is not null)
        {
            return;
        }

        int x = (int)MathF.Floor(localX / zoom);
        int y = (int)MathF.Floor(localY / zoom);
        if (x < 0 || y < 0 || x >= editor.Document.Width || y >= editor.Document.Height)
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
