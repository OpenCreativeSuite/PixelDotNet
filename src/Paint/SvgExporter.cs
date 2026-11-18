using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PixelDotNet.Paint;

/// <summary>Writes the document as an SVG with raster content embedded and shapes kept vector.</summary>
public static class SvgExporter
{
    /// <summary>Gets the SVG file extension, including the dot.</summary>
    public const string Extension = ".svg";

    /// <summary>Writes the document to an SVG file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="document">Document to export.</param>
    public static void Save(string path, PaintDocument document)
    {
        uint[] raster = CompositeWithoutShapes(document);
        bool hasRaster = false;
        for (int i = 0; i < raster.Length; i++)
        {
            if ((raster[i] >> 24) != 0)
            {
                hasRaster = true;
                break;
            }
        }

        using StreamWriter writer = new(path, false, new UTF8Encoding(false));
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        writer.WriteLine(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" "
            + $"xmlns:xlink=\"http://www.w3.org/1999/xlink\" "
            + $"width=\"{document.Width}\" height=\"{document.Height}\" "
            + $"viewBox=\"0 0 {document.Width} {document.Height}\">");

        if (hasRaster)
        {
            string base64 = Convert.ToBase64String(
                ImageExporter.Encode(raster, document.Width, document.Height, ImageFormat.Png));
            writer.WriteLine(
                $"<image x=\"0\" y=\"0\" width=\"{document.Width}\" "
                + $"height=\"{document.Height}\" "
                + $"xlink:href=\"data:image/png;base64,{base64}\"/>");
        }

        for (int i = 0; i < document.Layers.Count; i++)
        {
            PaintLayer layer = document.Layers[i];
            if (!layer.IsShapeLayer || !layer.Visible || layer.Shape is not { } shape)
            {
                continue;
            }

            float opacity = layer.Opacity;
            if (layer.Group is { } group)
            {
                if (!group.Visible)
                {
                    continue;
                }

                opacity *= group.Opacity;
            }

            WriteShape(writer, shape, opacity);
        }

        writer.WriteLine("</svg>");
    }

    /// <summary>Composites every non-shape layer for the embedded raster.</summary>
    private static uint[] CompositeWithoutShapes(PaintDocument document)
    {
        PaintDocument raster = new(document.Width, document.Height);
        foreach (PaintLayer layer in document.Layers)
        {
            if (!layer.IsShapeLayer)
            {
                raster.Layers.Add(layer);
            }
        }

        raster.ActiveIndex = 0;
        raster.MarkDirty();
        return raster.Composite;
    }

    /// <summary>Writes one shape layer as an SVG element.</summary>
    private static void WriteShape(StreamWriter writer, ShapeData shape, float opacity)
    {
        string fill = shape.Fill ? Colour(shape.FillColor) : "none";
        string stroke = Colour(shape.StrokeColor);
        float fillOpacity = shape.Fill ? (Rgba.A(shape.FillColor) / 255f) * opacity : 0f;
        float strokeOpacity = (Rgba.A(shape.StrokeColor) / 255f) * opacity;
        float left = MathF.Min(shape.X, shape.X + shape.Width);
        float top = MathF.Min(shape.Y, shape.Y + shape.Height);
        float width = MathF.Abs(shape.Width);
        float height = MathF.Abs(shape.Height);

        switch (shape.Kind)
        {
            case ShapeKind.Rectangle:
                writer.WriteLine(
                    $"<rect x=\"{F(left)}\" y=\"{F(top)}\" width=\"{F(width)}\" height=\"{F(height)}\" "
                    + $"fill=\"{fill}\" fill-opacity=\"{F(fillOpacity)}\" "
                    + $"stroke=\"{stroke}\" stroke-opacity=\"{F(strokeOpacity)}\" "
                    + $"stroke-width=\"{F(shape.StrokeWidth)}\"/>");
                break;

            case ShapeKind.Ellipse:
                writer.WriteLine(
                    $"<ellipse cx=\"{F(left + (width * 0.5f))}\" cy=\"{F(top + (height * 0.5f))}\" "
                    + $"rx=\"{F(width * 0.5f)}\" ry=\"{F(height * 0.5f)}\" "
                    + $"fill=\"{fill}\" fill-opacity=\"{F(fillOpacity)}\" "
                    + $"stroke=\"{stroke}\" stroke-opacity=\"{F(strokeOpacity)}\" "
                    + $"stroke-width=\"{F(shape.StrokeWidth)}\"/>");
                break;

            case ShapeKind.Line:
                writer.WriteLine(
                    $"<line x1=\"{F(left)}\" y1=\"{F(top)}\" x2=\"{F(left + width)}\" y2=\"{F(top + height)}\" "
                    + $"stroke=\"{stroke}\" stroke-opacity=\"{F(strokeOpacity)}\" "
                    + $"stroke-width=\"{F(shape.StrokeWidth)}\"/>");
                break;

            case ShapeKind.Path when shape.Points is { Count: >= 2 } points:
                writer.WriteLine(
                    $"<path d=\"{PathData(points)}\" "
                    + $"fill=\"{fill}\" fill-opacity=\"{F(fillOpacity)}\" "
                    + $"stroke=\"{stroke}\" stroke-opacity=\"{F(strokeOpacity)}\" "
                    + $"stroke-width=\"{F(shape.StrokeWidth)}\"/>");
                break;
        }
    }

    /// <summary>Builds SVG path data from Bezier nodes.</summary>
    private static string PathData(System.Collections.Generic.IReadOnlyList<PathNode> points)
    {
        StringBuilder data = new();
        data.Append("M ").Append(F(points[0].X)).Append(' ').Append(F(points[0].Y));

        for (int i = 0; i < points.Count; i++)
        {
            PathNode a = points[i];
            PathNode b = points[(i + 1) % points.Count];
            if (a.HasHandles || b.HasHandles)
            {
                data.Append(" C ")
                    .Append(F(a.OutX)).Append(' ').Append(F(a.OutY)).Append(' ')
                    .Append(F(b.InX)).Append(' ').Append(F(b.InY)).Append(' ')
                    .Append(F(b.X)).Append(' ').Append(F(b.Y));
            }
            else
            {
                data.Append(" L ").Append(F(b.X)).Append(' ').Append(F(b.Y));
            }
        }

        data.Append(" Z");
        return data.ToString();
    }

    /// <summary>Formats a colour as an SVG hex value.</summary>
    private static string Colour(uint color) =>
        "#" + (color & 0x00FFFFFFu).ToString("X6", CultureInfo.InvariantCulture);

    /// <summary>Formats a number for SVG attributes.</summary>
    private static string F(float value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
