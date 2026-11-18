using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PixelDotNet.Paint;

/// <summary>Reads basic SVG primitives and paths into editable shape layers.</summary>
public static class SvgImporter
{
    /// <summary>Gets the SVG file extension, including the dot.</summary>
    public const string Extension = ".svg";

    private static readonly Regex NumberPattern = new(
        @"[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PathTokenPattern = new(
        @"[MmZzLlHhVvCcSsQqTtAa]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TransformPattern = new(
        @"([a-zA-Z]+)\s*\(([^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Gets whether a file name has the SVG extension.</summary>
    public static bool IsSvg(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads an SVG file into a document of shape layers.</summary>
    /// <param name="path">SVG file to read.</param>
    /// <returns>A new document with one layer per imported primitive.</returns>
    /// <exception cref="InvalidDataException">The file is not readable SVG.</exception>
    public static PaintDocument Load(string path)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Load(path);
        }
        catch (XmlException error)
        {
            throw new InvalidDataException("The file is not valid SVG: " + error.Message, error);
        }

        XElement root = xml.Root
            ?? throw new InvalidDataException("The SVG file is empty.");

        if (!string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The file is not an SVG document.");
        }

        float width = ParseLength(root.Attribute("width")?.Value);
        float height = ParseLength(root.Attribute("height")?.Value);
        float[] viewBox = ParseNumbers(root.Attribute("viewBox")?.Value);

        float viewX = 0f;
        float viewY = 0f;
        float viewWidth = width;
        float viewHeight = height;

        if (viewBox.Length >= 4 && viewBox[2] > 0f && viewBox[3] > 0f)
        {
            viewX = viewBox[0];
            viewY = viewBox[1];
            viewWidth = viewBox[2];
            viewHeight = viewBox[3];
        }

        if (width <= 0f)
        {
            width = viewWidth;
        }

        if (height <= 0f)
        {
            height = viewHeight;
        }

        if (width <= 0f || height <= 0f)
        {
            width = 512f;
            height = 512f;
        }

        int documentWidth = Math.Clamp((int)MathF.Round(width), 1, 8192);
        int documentHeight = Math.Clamp((int)MathF.Round(height), 1, 8192);

        float scaleX = documentWidth / MathF.Max(1f, viewWidth);
        float scaleY = documentHeight / MathF.Max(1f, viewHeight);
        Matrix map = new(
            scaleX,
            0f,
            0f,
            scaleY,
            -viewX * scaleX,
            -viewY * scaleY);

        PaintDocument document = new(documentWidth, documentHeight);
        document.Layers.Add(new PaintLayer("Background", documentWidth, documentHeight));

        Walk(root, map, new SvgStyle(), document);

        document.ActiveIndex = document.Layers.Count - 1;
        document.MarkDirty();
        return document;
    }

    /// <summary>Walks an element and its children, accumulating transforms and styles.</summary>
    private static void Walk(
        XElement element,
        Matrix matrix,
        SvgStyle parentStyle,
        PaintDocument document)
    {
        string name = element.Name.LocalName;

        if (name is "defs" or "title" or "desc" or "metadata" or "style" or "clipPath")
        {
            return;
        }

        Matrix local = matrix.Multiply(ParseTransform(element.Attribute("transform")?.Value));
        SvgStyle style = Derive(element, parentStyle);

        switch (name)
        {
            case "svg":
            case "g":
            case "a":
            case "switch":
                foreach (XElement child in element.Elements())
                {
                    Walk(child, local, style, document);
                }

                return;
        }

        AddElement(element, name, local, style, document);

        if (name == "use")
        {
            return;
        }

        foreach (XElement child in element.Elements())
        {
            Walk(child, local, style, document);
        }
    }

    /// <summary>Converts a supported primitive into a shape layer.</summary>
    private static void AddElement(
        XElement element,
        string name,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        switch (name)
        {
            case "rect":
                AddRect(element, matrix, style, document);
                break;

            case "ellipse":
            case "circle":
                AddEllipse(element, name == "circle", matrix, style, document);
                break;

            case "line":
                AddLine(element, matrix, style, document);
                break;

            case "polygon":
            case "polyline":
                AddPolygon(element, name == "polygon", matrix, style, document);
                break;

            case "path":
                AddPath(element, matrix, style, document);
                break;

            case "image":
                AddImage(element, matrix, style, document);
                break;

            case "text":
                AddText(element, matrix, style, document);
                break;
        }
    }

    /// <summary>Adds basic SVG text, including text referenced through a textPath.</summary>
    private static void AddText(
        XElement element,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        XElement? textPath = element.Elements()
            .FirstOrDefault(child => child.Name.LocalName == "textPath");
        string raw = textPath?.Value ?? element.Value;
        string content = string.Join(
            " ",
            raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (content.Length == 0)
        {
            return;
        }

        string? family = element.Attribute("font-family")?.Value
            ?? StyleValue(element, "font-family");
        float size = ParseLength(
            element.Attribute("font-size")?.Value ?? StyleValue(element, "font-size"));
        float scale = MathF.Sqrt(MathF.Abs(matrix.Determinant));
        if (size <= 0f)
        {
            size = 32f;
        }

        string? weight = element.Attribute("font-weight")?.Value
            ?? StyleValue(element, "font-weight");
        string? slant = element.Attribute("font-style")?.Value
            ?? StyleValue(element, "font-style");
        string? anchor = element.Attribute("text-anchor")?.Value
            ?? StyleValue(element, "text-anchor");

        TextData data = new()
        {
            Content = content,
            FontIndex = FindFont(family),
            Size = Math.Clamp(size * scale, 4f, 400f),
            Bold = string.Equals(weight, "bold", StringComparison.OrdinalIgnoreCase)
                || (float.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out float numericWeight)
                    && numericWeight >= 600f),
            Italic = string.Equals(slant, "italic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(slant, "oblique", StringComparison.OrdinalIgnoreCase),
            Align = anchor switch
            {
                "middle" => TextAlign.Center,
                "end" => TextAlign.Right,
                _ => TextAlign.Left,
            },
            Color = style.HasFill
                ? ParseColor(style.Fill, style.FillOpacity * style.Opacity)
                : 0xFF000000u,
        };

        if (textPath is not null)
        {
            string? href = textPath.Attribute("href")?.Value
                ?? textPath.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"))?.Value;
            XElement? pathElement = href is { Length: > 1 } && href[0] == '#'
                ? element.Document?.Root?.Descendants().FirstOrDefault(
                    child => child.Attribute("id")?.Value == href[1..])
                : null;

            if (pathElement is not null && pathElement.Name.LocalName == "path")
            {
                List<(List<PathNode> Nodes, bool Closed)> subpaths =
                    ParsePath(pathElement.Attribute("d")?.Value ?? string.Empty);
                if (subpaths.Count > 0)
                {
                    List<PathNode> nodes = [];
                    foreach (PathNode node in subpaths[0].Nodes)
                    {
                        nodes.Add(TransformNode(node, matrix));
                    }

                    data.Path = nodes;
                    data.PathClosed = subpaths[0].Closed;
                    string? offset = textPath.Attribute("startOffset")?.Value;
                    if (offset is not null)
                    {
                        data.PathOffset = Math.Clamp(ParseLength(offset) / 100f, 0f, 1f);
                    }
                }
            }
        }

        if (data.Path is null)
        {
            (float x, float y) = matrix.Apply(Number(element, "x"), Number(element, "y"));
            data.X = x;
            data.Y = y;
            data.Rotation = MathF.Atan2(matrix.B, matrix.A) * 180f / MathF.PI;
        }

        PaintLayer layer = new(UniqueName(document, "Text"), document.Width, document.Height);
        layer.AdoptText(data);
        document.Layers.Add(layer);
    }

    /// <summary>Finds the closest built-in font family, or the first family.</summary>
    private static int FindFont(string? family)
    {
        if (!string.IsNullOrWhiteSpace(family))
        {
            string name = family.Split(',')[0].Trim().Trim('"', '\'');
            for (int i = 0; i < TextFonts.All.Length; i++)
            {
                if (string.Equals(TextFonts.All[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            for (int i = 0; i < TextFonts.All.Length; i++)
            {
                if (TextFonts.All[i].Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(TextFonts.All[i].Name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return 0;
    }

    /// <summary>Reads one declaration out of an element's inline style attribute.</summary>
    private static string? StyleValue(XElement element, string key)
    {
        string? style = element.Attribute("style")?.Value;
        if (string.IsNullOrWhiteSpace(style))
        {
            return null;
        }

        foreach (string declaration in style.Split(';'))
        {
            int colon = declaration.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            if (string.Equals(declaration[..colon].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                return declaration[(colon + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>Adds a rect primitive, falling back to a path when it is rotated.</summary>
    private static void AddRect(
        XElement element,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        float x = Number(element, "x");
        float y = Number(element, "y");
        float width = Number(element, "width");
        float height = Number(element, "height");
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        (float X, float Y)[] corners =
        [
            matrix.Apply(x, y),
            matrix.Apply(x + width, y),
            matrix.Apply(x + width, y + height),
            matrix.Apply(x, y + height),
        ];

        if (matrix.IsAxisAligned)
        {
            float left = MathF.Min(MathF.Min(corners[0].X, corners[1].X), MathF.Min(corners[2].X, corners[3].X));
            float right = MathF.Max(MathF.Max(corners[0].X, corners[1].X), MathF.Max(corners[2].X, corners[3].X));
            float top = MathF.Min(MathF.Min(corners[0].Y, corners[1].Y), MathF.Min(corners[2].Y, corners[3].Y));
            float bottom = MathF.Max(MathF.Max(corners[0].Y, corners[1].Y), MathF.Max(corners[2].Y, corners[3].Y));

            AddShape(
                document,
                "Rectangle",
                style,
                matrix,
                shape =>
                {
                    shape.Kind = ShapeKind.Rectangle;
                    shape.X = left;
                    shape.Y = top;
                    shape.Width = right - left;
                    shape.Height = bottom - top;
                });
            return;
        }

        List<PathNode> nodes =
        [
            new(corners[0].X, corners[0].Y),
            new(corners[1].X, corners[1].Y),
            new(corners[2].X, corners[2].Y),
            new(corners[3].X, corners[3].Y),
        ];

        AddPathShape(document, "Rectangle", nodes, closed: true, style, matrix);
    }

    /// <summary>Adds a circle or ellipse primitive.</summary>
    private static void AddEllipse(
        XElement element,
        bool circle,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        float cx = Number(element, "cx");
        float cy = Number(element, "cy");
        float rx = circle ? Number(element, "r") : Number(element, "rx");
        float ry = circle ? rx : Number(element, "ry");
        if (rx <= 0f || ry <= 0f)
        {
            return;
        }

        if (matrix.IsAxisAligned)
        {
            (float X, float Y) a = matrix.Apply(cx - rx, cy - ry);
            (float X, float Y) b = matrix.Apply(cx + rx, cy + ry);
            float left = MathF.Min(a.X, b.X);
            float right = MathF.Max(a.X, b.X);
            float top = MathF.Min(a.Y, b.Y);
            float bottom = MathF.Max(a.Y, b.Y);

            AddShape(
                document,
                "Ellipse",
                style,
                matrix,
                shape =>
                {
                    shape.Kind = ShapeKind.Ellipse;
                    shape.X = left;
                    shape.Y = top;
                    shape.Width = right - left;
                    shape.Height = bottom - top;
                });
            return;
        }

        const float Kappa = 0.55228475f;
        float kx = rx * Kappa;
        float ky = ry * Kappa;
        PathNode[] local =
        [
            new(cx, cy - ry) { InX = cx - kx, InY = cy - ry, OutX = cx + kx, OutY = cy - ry },
            new(cx + rx, cy) { InX = cx + rx, InY = cy - ky, OutX = cx + rx, OutY = cy + ky },
            new(cx, cy + ry) { InX = cx + kx, InY = cy + ry, OutX = cx - kx, OutY = cy + ry },
            new(cx - rx, cy) { InX = cx - rx, InY = cy + ky, OutX = cx - rx, OutY = cy - ky },
        ];

        List<PathNode> nodes = [];
        foreach (PathNode node in local)
        {
            nodes.Add(TransformNode(node, matrix));
        }

        AddPathShape(document, "Ellipse", nodes, closed: true, style, matrix);
    }

    /// <summary>Adds a line as an open two-point path.</summary>
    private static void AddLine(
        XElement element,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        (float X, float Y) a = matrix.Apply(Number(element, "x1"), Number(element, "y1"));
        (float X, float Y) b = matrix.Apply(Number(element, "x2"), Number(element, "y2"));
        AddPathShape(
            document,
            "Line",
            [new PathNode(a.X, a.Y), new PathNode(b.X, b.Y)],
            closed: false,
            style,
            matrix);
    }

    /// <summary>Adds polygon or polyline points as a path.</summary>
    private static void AddPolygon(
        XElement element,
        bool closed,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        float[] numbers = ParseNumbers(element.Attribute("points")?.Value);
        if (numbers.Length < 4)
        {
            return;
        }

        List<PathNode> nodes = [];
        for (int i = 0; i + 1 < numbers.Length; i += 2)
        {
            (float x, float y) = matrix.Apply(numbers[i], numbers[i + 1]);
            nodes.Add(new PathNode(x, y));
        }

        AddPathShape(document, closed ? "Polygon" : "Polyline", nodes, closed, style, matrix);
    }

    /// <summary>Adds every subpath in an SVG path as its own shape layer.</summary>
    private static void AddPath(
        XElement element,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        string data = element.Attribute("d")?.Value ?? string.Empty;
        foreach ((List<PathNode> nodes, bool closed) in ParsePath(data))
        {
            if (nodes.Count < 2)
            {
                continue;
            }

            List<PathNode> transformed = [];
            foreach (PathNode node in nodes)
            {
                transformed.Add(TransformNode(node, matrix));
            }

            AddPathShape(document, "Path", transformed, closed, style, matrix);
        }
    }

    /// <summary>Adds an embedded raster image, usually the PNG an export writes.</summary>
    private static void AddImage(
        XElement element,
        Matrix matrix,
        SvgStyle style,
        PaintDocument document)
    {
        string? href = element.Attribute("href")?.Value
            ?? element.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"))?.Value;
        if (string.IsNullOrWhiteSpace(href)
            || !href.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int comma = href.IndexOf(',');
        if (comma < 0 || !href[..comma].Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(href[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return;
        }

        (int imageWidth, int imageHeight, uint[] pixels) = ImageLoader.Decode(bytes);
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        float x = Number(element, "x");
        float y = Number(element, "y");
        float width = Number(element, "width");
        float height = Number(element, "height");

        if (width <= 0f)
        {
            width = imageWidth;
        }

        if (height <= 0f)
        {
            height = imageHeight;
        }

        if (!matrix.TryInvert(out Matrix inverse))
        {
            return;
        }

        (float X, float Y) a = matrix.Apply(x, y);
        (float X, float Y) b = matrix.Apply(x + width, y);
        (float X, float Y) c = matrix.Apply(x + width, y + height);
        (float X, float Y) d = matrix.Apply(x, y + height);

        int minX = Math.Clamp((int)MathF.Floor(MathF.Min(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X))), 0, document.Width - 1);
        int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X))), 0, document.Width - 1);
        int minY = Math.Clamp((int)MathF.Floor(MathF.Min(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y))), 0, document.Height - 1);
        int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y))), 0, document.Height - 1);

        if (minX > maxX || minY > maxY)
        {
            return;
        }

        PaintLayer layer = new(UniqueName(document, "Image"), document.Width, document.Height);
        int opacity = Math.Clamp((int)MathF.Round(style.Opacity * 255f), 0, 255);

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                (float ux, float uy) = inverse.Apply(px + 0.5f, py + 0.5f);
                float u = (ux - x) / width;
                float v = (uy - y) / height;
                if (u < 0f || v < 0f || u > 1f || v > 1f)
                {
                    continue;
                }

                int sx = Math.Clamp((int)(u * imageWidth), 0, imageWidth - 1);
                int sy = Math.Clamp((int)(v * imageHeight), 0, imageHeight - 1);
                uint source = pixels[(sy * imageWidth) + sx];
                int alpha = Rgba.A(source) * opacity / 255;
                if (alpha <= 0)
                {
                    continue;
                }

                int index = (py * document.Width) + px;
                layer.Pixels[index] = Rgba.Over(layer.Pixels[index], source, alpha);
            }
        }

        document.Layers.Add(layer);
    }

    /// <summary>Creates and installs a shape layer with the inherited paint.</summary>
    private static void AddShape(
        PaintDocument document,
        string name,
        SvgStyle style,
        Matrix matrix,
        Action<ShapeData> configure)
    {
        ShapeData shape = NewShape(style, matrix);
        configure(shape);
        InstallShape(document, name, shape);
    }

    /// <summary>Creates and installs an open or closed path shape layer.</summary>
    private static void AddPathShape(
        PaintDocument document,
        string name,
        List<PathNode> nodes,
        bool closed,
        SvgStyle style,
        Matrix matrix)
    {
        if (nodes.Count < 2)
        {
            return;
        }

        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;

        foreach (PathNode node in nodes)
        {
            left = MathF.Min(left, node.X);
            top = MathF.Min(top, node.Y);
            right = MathF.Max(right, node.X);
            bottom = MathF.Max(bottom, node.Y);
        }

        ShapeData shape = NewShape(style, matrix);
        shape.Kind = ShapeKind.Path;
        if (!closed)
        {
            shape.Fill = false;
        }

        shape.X = left;
        shape.Y = top;
        shape.Width = right - left;
        shape.Height = bottom - top;
        shape.Points = nodes;
        shape.Closed = closed;
        InstallShape(document, name, shape);
    }

    /// <summary>Builds shape paint from an SVG style.</summary>
    private static ShapeData NewShape(SvgStyle style, Matrix matrix)
    {
        bool fill = style.HasFill;
        bool stroke = style.HasStroke;
        float scale = MathF.Sqrt(MathF.Abs(matrix.Determinant));

        return new ShapeData
        {
            Fill = fill,
            FillColor = fill
                ? ParseColor(style.Fill, style.FillOpacity * style.Opacity)
                : 0u,
            Stroke = stroke,
            StrokeWidth = MathF.Max(1f, style.StrokeWidth * scale),
            StrokeColor = stroke
                ? ParseColor(style.Stroke, style.StrokeOpacity * style.Opacity)
                : 0u,
        };
    }

    /// <summary>Adds a configured shape layer to the document.</summary>
    private static void InstallShape(PaintDocument document, string name, ShapeData shape)
    {
        if (!shape.Fill && !shape.Stroke)
        {
            return;
        }

        PaintLayer layer = new(UniqueName(document, name), document.Width, document.Height);
        layer.AdoptShape(shape);
        document.Layers.Add(layer);
    }

    /// <summary>Returns a layer name that is not already used.</summary>
    private static string UniqueName(PaintDocument document, string preferred)
    {
        int index = 1;
        string name = preferred;

        while (document.Layers.Exists(layer => layer.Name == name))
        {
            index++;
            name = preferred + " " + index;
        }

        return name;
    }

    /// <summary>Builds a style by inheriting and then overriding from an element.</summary>
    private static SvgStyle Derive(XElement element, SvgStyle parent)
    {
        SvgStyle style = parent.Clone();
        Dictionary<string, string> values = [];

        string? styleText = element.Attribute("style")?.Value;
        if (!string.IsNullOrWhiteSpace(styleText))
        {
            foreach (string declaration in styleText.Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                string key = declaration[..colon].Trim().ToLowerInvariant();
                string value = declaration[(colon + 1)..].Trim();
                if (key.Length > 0)
                {
                    values[key] = value;
                }
            }
        }

        foreach (string key in new[]
        {
            "fill", "stroke", "stroke-width", "fill-opacity", "stroke-opacity", "opacity",
        })
        {
            XAttribute? attribute = element.Attribute(key);
            if (attribute is not null && !values.ContainsKey(key))
            {
                values[key] = attribute.Value;
            }
        }

        if (values.TryGetValue("fill", out string? fill))
        {
            style.Fill = fill;
        }

        if (values.TryGetValue("stroke", out string? stroke))
        {
            style.Stroke = stroke;
        }

        if (values.TryGetValue("stroke-width", out string? width))
        {
            float parsed = ParseLength(width);
            if (parsed > 0f)
            {
                style.StrokeWidth = parsed;
            }
        }

        if (values.TryGetValue("fill-opacity", out string? fillOpacity))
        {
            style.FillOpacity = ParseOpacity(fillOpacity);
        }

        if (values.TryGetValue("stroke-opacity", out string? strokeOpacity))
        {
            style.StrokeOpacity = ParseOpacity(strokeOpacity);
        }

        if (values.TryGetValue("opacity", out string? opacity))
        {
            style.Opacity *= ParseOpacity(opacity);
        }

        return style;
    }

    /// <summary>Parses the transform list into one affine matrix.</summary>
    private static Matrix ParseTransform(string? value)
    {
        Matrix result = Matrix.Identity;
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (Match match in TransformPattern.Matches(value))
        {
            float[] numbers = ParseNumbers(match.Groups[2].Value);
            if (numbers.Length == 0)
            {
                continue;
            }

            Matrix local = match.Groups[1].Value.ToLowerInvariant() switch
            {
                "translate" => new Matrix(
                    1f,
                    0f,
                    0f,
                    1f,
                    numbers[0],
                    numbers.Length > 1 ? numbers[1] : 0f),
                "scale" => new Matrix(
                    numbers[0],
                    0f,
                    0f,
                    numbers.Length > 1 ? numbers[1] : numbers[0],
                    0f,
                    0f),
                "rotate" => Rotation(
                    numbers[0],
                    numbers.Length > 2 ? numbers[1] : 0f,
                    numbers.Length > 2 ? numbers[2] : 0f),
                "skewx" => new Matrix(
                    1f,
                    0f,
                    MathF.Tan(numbers[0] * MathF.PI / 180f),
                    1f,
                    0f,
                    0f),
                "skewy" => new Matrix(
                    1f,
                    MathF.Tan(numbers[0] * MathF.PI / 180f),
                    0f,
                    1f,
                    0f,
                    0f),
                "matrix" when numbers.Length >= 6 => new Matrix(
                    numbers[0],
                    numbers[1],
                    numbers[2],
                    numbers[3],
                    numbers[4],
                    numbers[5]),
                _ => Matrix.Identity,
            };

            result = result.Multiply(local);
        }

        return result;
    }

    /// <summary>Builds a rotation matrix around an optional centre.</summary>
    private static Matrix Rotation(float degrees, float cx, float cy)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Matrix(
            cos,
            sin,
            -sin,
            cos,
            cx - (cos * cx) + (sin * cy),
            cy - (sin * cx) - (cos * cy));
    }

    /// <summary>Transforms a cubic node's anchor and both handles.</summary>
    private static PathNode TransformNode(PathNode node, Matrix matrix)
    {
        (float x, float y) = matrix.Apply(node.X, node.Y);
        (float inX, float inY) = matrix.Apply(node.InX, node.InY);
        (float outX, float outY) = matrix.Apply(node.OutX, node.OutY);
        return new PathNode
        {
            X = x,
            Y = y,
            InX = inX,
            InY = inY,
            OutX = outX,
            OutY = outY,
        };
    }

    /// <summary>Parses SVG path data into closed and open subpaths.</summary>
    private static List<(List<PathNode> Nodes, bool Closed)> ParsePath(string data)
    {
        List<(List<PathNode> Nodes, bool Closed)> result = [];
        MatchCollection tokens = PathTokenPattern.Matches(data);
        int index = 0;
        List<PathNode>? nodes = null;
        float px = 0f;
        float py = 0f;
        float startX = 0f;
        float startY = 0f;
        float lastCx = 0f;
        float lastCy = 0f;
        float lastQx = 0f;
        float lastQy = 0f;
        char command = '\0';
        char previous = '\0';

        float Next() =>
            index < tokens.Count
                ? float.Parse(tokens[index++].Value, CultureInfo.InvariantCulture)
                : 0f;

        void EnsurePath()
        {
            nodes ??= [new PathNode(px, py)];
        }

        while (index < tokens.Count)
        {
            string token = tokens[index].Value;
            char first = token[0];
            if (char.IsLetter(first))
            {
                command = first;
                index++;

                if (command is 'Z' or 'z')
                {
                    if (nodes is { Count: >= 2 })
                    {
                        PathNode firstNode = nodes[0];
                        PathNode lastNode = nodes[^1];
                        if (MathF.Abs(lastNode.X - firstNode.X) < 0.001f
                            && MathF.Abs(lastNode.Y - firstNode.Y) < 0.001f)
                        {
                            firstNode.InX = lastNode.InX;
                            firstNode.InY = lastNode.InY;
                            nodes[0] = firstNode;
                            nodes.RemoveAt(nodes.Count - 1);
                        }

                        if (nodes.Count >= 2)
                        {
                            result.Add((nodes, true));
                        }
                    }

                    nodes = null;
                    px = startX;
                    py = startY;
                    previous = command;
                }

                continue;
            }

            if (command == '\0')
            {
                index++;
                continue;
            }

            bool relative = char.IsLower(command);
            char upper = char.ToUpperInvariant(command);
            char actual = command;

            switch (upper)
            {
                case 'M':
                {
                    float x = Next() + (relative ? px : 0f);
                    float y = Next() + (relative ? py : 0f);
                    if (nodes is { Count: >= 1 })
                    {
                        if (nodes.Count >= 2)
                        {
                            result.Add((nodes, false));
                        }
                    }

                    nodes = [new PathNode(x, y)];
                    px = x;
                    py = y;
                    startX = x;
                    startY = y;
                    lastCx = x;
                    lastCy = y;
                    lastQx = x;
                    lastQy = y;
                    command = relative ? 'l' : 'L';
                    previous = 'M';
                    break;
                }

                case 'L':
                {
                    float x = Next() + (relative ? px : 0f);
                    float y = Next() + (relative ? py : 0f);
                    EnsurePath();
                    nodes!.Add(new PathNode(x, y));
                    px = x;
                    py = y;
                    break;
                }

                case 'H':
                {
                    float x = Next() + (relative ? px : 0f);
                    EnsurePath();
                    nodes!.Add(new PathNode(x, py));
                    px = x;
                    break;
                }

                case 'V':
                {
                    float y = Next() + (relative ? py : 0f);
                    EnsurePath();
                    nodes!.Add(new PathNode(px, y));
                    py = y;
                    break;
                }

                case 'C':
                {
                    float c1x = Next() + (relative ? px : 0f);
                    float c1y = Next() + (relative ? py : 0f);
                    float c2x = Next() + (relative ? px : 0f);
                    float c2y = Next() + (relative ? py : 0f);
                    float x = Next() + (relative ? px : 0f);
                    float y = Next() + (relative ? py : 0f);
                    EnsurePath();
                    PathNode from = nodes![^1];
                    from.OutX = c1x;
                    from.OutY = c1y;
                    nodes[^1] = from;
                    nodes.Add(new PathNode { X = x, Y = y, InX = c2x, InY = c2y, OutX = x, OutY = y });
                    px = x;
                    py = y;
                    lastCx = c2x;
                    lastCy = c2y;
                    break;
                }

                case 'S':
                {
                    bool smooth = previous is 'C' or 'c' or 'S' or 's';
                    float c1x = smooth ? (px * 2f) - lastCx : px;
                    float c1y = smooth ? (py * 2f) - lastCy : py;
                    float c2x = Next() + (relative ? px : 0f);
                    float c2y = Next() + (relative ? py : 0f);
                    float x = Next() + (relative ? px : 0f);
                    float y = Next() + (relative ? py : 0f);
                    EnsurePath();
                    PathNode from = nodes![^1];
                    from.OutX = c1x;
                    from.OutY = c1y;
                    nodes[^1] = from;
                    nodes.Add(new PathNode { X = x, Y = y, InX = c2x, InY = c2y, OutX = x, OutY = y });
                    px = x;
                    py = y;
                    lastCx = c2x;
                    lastCy = c2y;
                    break;
                }

                case 'Q':
                {
                    float qx = Next() + (relative ? px : 0f);
                    float qy = Next() + (relative ? py : 0f);
                    float x = Next() + (relative ? px : 0f);
                    float y = Next() + (relative ? py : 0f);
                    AddQuadratic(ref nodes, ref px, ref py, qx, qy, x, y);
                    lastQx = qx;
                    lastQy = qy;
                    break;
                }

                case 'T':
                {
                    bool smooth = previous is 'Q' or 'q' or 'T' or 't';
                    float qx = smooth ? (px * 2f) - lastQx : px;
                    float qy = smooth ? (py * 2f) - lastQy : py;
                    float x = Next() + (relative ? px : 0f);
                    float y = Next() + (relative ? py : 0f);
                    AddQuadratic(ref nodes, ref px, ref py, qx, qy, x, y);
                    lastQx = qx;
                    lastQy = qy;
                    break;
                }

                case 'A':
                {
                    _ = Next();
                    _ = Next();
                    _ = Next();
                    _ = Next();
                    _ = Next();
                    float x = Next() + (relative ? px : 0f);
                    float y = Next() + (relative ? py : 0f);
                    EnsurePath();
                    nodes!.Add(new PathNode(x, y));
                    px = x;
                    py = y;
                    break;
                }

                default:
                    index++;
                    break;
            }

            previous = actual;
        }

        if (nodes is { Count: >= 2 })
        {
            result.Add((nodes, false));
        }

        return result;
    }

    /// <summary>Converts one quadratic segment into a cubic node pair.</summary>
    private static void AddQuadratic(
        ref List<PathNode>? nodes,
        ref float px,
        ref float py,
        float qx,
        float qy,
        float x,
        float y)
    {
        float c1x = px + (2f / 3f * (qx - px));
        float c1y = py + (2f / 3f * (qy - py));
        float c2x = x + (2f / 3f * (qx - x));
        float c2y = y + (2f / 3f * (qy - y));
        nodes ??= [new PathNode(px, py)];
        PathNode from = nodes[^1];
        from.OutX = c1x;
        from.OutY = c1y;
        nodes[^1] = from;
        nodes.Add(new PathNode { X = x, Y = y, InX = c2x, InY = c2y, OutX = x, OutY = y });
        px = x;
        py = y;
    }

    /// <summary>Reads a numeric attribute, returning zero when it is absent.</summary>
    private static float Number(XElement element, string name) =>
        ParseLength(element.Attribute(name)?.Value);

    /// <summary>Parses a length, dropping any unit suffix or percentage.</summary>
    private static float ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0f;
        }

        Match match = NumberPattern.Match(value);
        return match.Success
            ? float.Parse(match.Value, CultureInfo.InvariantCulture)
            : 0f;
    }

    /// <summary>Parses an opacity in the 0-1 or 0-100 range.</summary>
    private static float ParseOpacity(string value)
    {
        float parsed = ParseLength(value);
        if (value.TrimEnd().EndsWith('%'))
        {
            parsed /= 100f;
        }

        return Math.Clamp(parsed, 0f, 1f);
    }

    /// <summary>Finds every numeric token in a string.</summary>
    private static float[] ParseNumbers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        MatchCollection matches = NumberPattern.Matches(value);
        float[] numbers = new float[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            numbers[i] = float.Parse(matches[i].Value, CultureInfo.InvariantCulture);
        }

        return numbers;
    }

    /// <summary>Converts a CSS colour to packed ARGB with an opacity multiplier.</summary>
    private static uint ParseColor(string value, float opacity)
    {
        string text = value.Trim();
        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return 0u;
        }

        if (text.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            text = "#808080";
        }

        int alpha = Math.Clamp((int)MathF.Round(opacity * 255f), 0, 255);

        if (text[0] == '#')
        {
            string hex = text[1..];
            if (hex.Length == 3)
            {
                int r = Convert.ToInt32(new string(hex[0], 2), 16);
                int g = Convert.ToInt32(new string(hex[1], 2), 16);
                int b = Convert.ToInt32(new string(hex[2], 2), 16);
                return Rgba.WithAlpha(alpha, Rgba.FromRgb(r, g, b));
            }

            if (hex.Length is 6 or 8 && int.TryParse(
                hex[..6],
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out int rgb))
            {
                if (hex.Length == 8)
                {
                    alpha = Math.Clamp(
                        (int)MathF.Round(alpha * (Convert.ToInt32(hex[6..], 16) / 255f)),
                        0,
                        255);
                }

                return Rgba.WithAlpha(
                    alpha,
                    Rgba.FromRgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF));
            }

            return Rgba.WithAlpha(alpha, Rgba.FromRgb(0, 0, 0));
        }

        if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
        {
            float[] parts = ParseNumbers(text);
            if (parts.Length >= 3)
            {
                return Rgba.WithAlpha(
                    alpha,
                    Rgba.FromRgb(
                        Math.Clamp((int)parts[0], 0, 255),
                        Math.Clamp((int)parts[1], 0, 255),
                        Math.Clamp((int)parts[2], 0, 255)));
            }

            return Rgba.WithAlpha(alpha, Rgba.FromRgb(0, 0, 0));
        }

        uint named = text.ToLowerInvariant() switch
        {
            "black" => Rgba.FromRgb(0, 0, 0),
            "white" => Rgba.FromRgb(255, 255, 255),
            "red" => Rgba.FromRgb(220, 40, 40),
            "green" => Rgba.FromRgb(40, 160, 60),
            "blue" => Rgba.FromRgb(40, 80, 220),
            "yellow" => Rgba.FromRgb(240, 220, 40),
            "orange" => Rgba.FromRgb(240, 150, 40),
            "purple" => Rgba.FromRgb(140, 60, 190),
            "pink" => Rgba.FromRgb(240, 140, 180),
            "gray" or "grey" => Rgba.FromRgb(128, 128, 128),
            "cyan" or "aqua" => Rgba.FromRgb(40, 200, 220),
            "magenta" or "fuchsia" => Rgba.FromRgb(220, 40, 200),
            "transparent" => 0u,
            _ => Rgba.FromRgb(0, 0, 0),
        };

        return named == 0u ? 0u : Rgba.WithAlpha(alpha, named);
    }

    /// <summary>Inherited SVG paint and geometry settings.</summary>
    private sealed class SvgStyle
    {
        public string Fill { get; set; } = "black";

        public string Stroke { get; set; } = "none";

        public float FillOpacity { get; set; } = 1f;

        public float StrokeOpacity { get; set; } = 1f;

        public float StrokeWidth { get; set; } = 1f;

        public float Opacity { get; set; } = 1f;

        public bool HasFill =>
            !Fill.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !Fill.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            && FillOpacity > 0f
            && Opacity > 0f;

        public bool HasStroke =>
            !Stroke.Equals("none", StringComparison.OrdinalIgnoreCase)
            && !Stroke.Equals("transparent", StringComparison.OrdinalIgnoreCase)
            && StrokeOpacity > 0f
            && Opacity > 0f;

        public SvgStyle Clone() => new()
        {
            Fill = Fill,
            Stroke = Stroke,
            FillOpacity = FillOpacity,
            StrokeOpacity = StrokeOpacity,
            StrokeWidth = StrokeWidth,
            Opacity = Opacity,
        };
    }

    /// <summary>A 2D affine transform using SVG's a-f matrix order.</summary>
    private readonly record struct Matrix(
        float A,
        float B,
        float C,
        float D,
        float E,
        float F)
    {
        public static Matrix Identity => new(1f, 0f, 0f, 1f, 0f, 0f);

        public float Determinant => (A * D) - (B * C);

        public bool IsAxisAligned => MathF.Abs(B) < 0.0001f && MathF.Abs(C) < 0.0001f;

        public (float X, float Y) Apply(float x, float y) =>
            ((A * x) + (C * y) + E, (B * x) + (D * y) + F);

        public bool TryInvert(out Matrix inverse)
        {
            float determinant = Determinant;
            if (MathF.Abs(determinant) < 0.000001f)
            {
                inverse = Identity;
                return false;
            }

            float scale = 1f / determinant;
            inverse = new Matrix(
                D * scale,
                -B * scale,
                -C * scale,
                A * scale,
                ((C * F) - (D * E)) * scale,
                ((B * E) - (A * F)) * scale);
            return true;
        }

        public Matrix Multiply(Matrix other) => new(
            (A * other.A) + (C * other.B),
            (B * other.A) + (D * other.B),
            (A * other.C) + (C * other.D),
            (B * other.C) + (D * other.D),
            (A * other.E) + (C * other.F) + E,
            (B * other.E) + (D * other.F) + F);
    }
}
