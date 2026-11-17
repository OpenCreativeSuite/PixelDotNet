using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static StbTrueTypeSharp.StbTrueType;

namespace PixelDotNet.Paint;

/// <summary>
/// Imports font families from cdnfonts (or any @font-face stylesheet) and keeps a
/// local cache so they are only downloaded once.
/// </summary>
/// <remarks>
/// <para>
/// The remote stylesheet is saved verbatim, its @font-face rules are parsed, the
/// best regular/bold/italic/bold-italic faces are downloaded, WOFF is rebuilt as
/// sfnt for StbTrueType, and a small <c>font.css</c> with local URLs plus a
/// <c>.web-font-name</c> class is written next to the files. An index next to the
/// cache folders lets later launches restore every imported family.
/// </para>
/// <para>
/// There is deliberately no timer or background polling: the cache is read once
/// when <see cref="TextFonts"/> initialises, and an import is a single
/// synchronous job the options bar runs on a worker thread.
/// </para>
/// </remarks>
public static class FontCatalog
{
    private const string DefaultCssRoot = "https://fonts.cdnfonts.com/css/";

    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex FacePattern = new(
        @"@font-face\s*\{(?<body>[^}]*)\}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UrlPattern = new(
        @"url\(\s*(?:""(?<url>[^""]+)""|'(?<url>[^']+)'|(?<url>[^)\s]+))\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImportPattern = new(
        @"@import\s+(?:url\(\s*)?(?:""(?<url>[^""]+)""|'(?<url>[^']+)'|(?<url>[^)\s;]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions IndexOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Gets the folder that holds imported families and the index.</summary>
    public static string Root { get; } = DefaultRoot();

    /// <summary>Gets the path of the preview bitmap for a downloaded family, if any.</summary>
    public static string? PreviewPath(TextFont font)
    {
        if (font.Folder is null)
        {
            return null;
        }

        string path = Path.Combine(font.Folder, "preview.png");
        if (!File.Exists(path))
        {
            try
            {
                GeneratePreview(font, path);
            }
            catch
            {
                return null;
            }
        }

        return File.Exists(path) ? path : null;
    }

    /// <summary>Reads every cached family from the index, generating missing previews.</summary>
    public static IReadOnlyList<TextFont> LoadCached()
    {
        List<TextFont> result = [];
        foreach (FontEntry entry in ReadIndex())
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Slug))
            {
                continue;
            }

            string folder = Path.Combine(Root, entry.Slug);
            if (!File.Exists(Path.Combine(folder, entry.Regular)))
            {
                continue;
            }

            TextFont font = new(entry.Name, entry.Regular, entry.Bold, entry.Italic, entry.BoldItalic, folder);
            PreviewPath(font);
            result.Add(font);
        }

        return result;
    }

    /// <summary>Reuses a fully cached folder instead of downloading the same family twice.</summary>
    private static TextFont? TryReuse(string slug, string family, string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        List<FontEntry> entries = ReadIndex();
        FontEntry? entry = entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Slug, slug, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name, family, StringComparison.OrdinalIgnoreCase));
        string regular = entry?.Regular ?? "regular.ttf";
        if (!File.Exists(Path.Combine(folder, regular)))
        {
            return null;
        }

        string display = string.IsNullOrWhiteSpace(entry?.Name) ? family : entry.Name;
        string bold = Style(folder, entry?.Bold, regular);
        string italic = Style(folder, entry?.Italic, regular);
        string boldItalic = Style(folder, entry?.BoldItalic, bold);

        TextFont font = new(display, regular, bold, italic, boldItalic, folder);
        PreviewPath(font);
        if (entry is null)
        {
            entries.Add(new FontEntry(display, slug, regular, bold, italic, boldItalic));
            WriteIndex(entries);
        }

        return font;
    }

    /// <summary>Returns a cached style file, or the fallback when it is missing.</summary>
    private static string Style(string folder, string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) || !File.Exists(Path.Combine(folder, name)) ? fallback : name;

    /// <summary>
    /// Downloads a family from a cdnfonts URL, name, or any CSS URL and caches it.
    /// </summary>
    /// <param name="input">A stylesheet URL, a cdnfonts family page, or a family name.</param>
    /// <returns>The imported family, ready to add to the text-tool list.</returns>
    public static TextFont Import(string input)
    {
        Uri cssUri = ResolveCssUri(input);
        string css = FetchCss(cssUri, 0);
        List<FaceSpec> faces = ParseFaces(css, cssUri);
        if (faces.Count == 0)
        {
            throw new InvalidOperationException("No @font-face rules were found at that URL.");
        }

        string family = ChooseFamily(faces);
        List<FaceSpec> familyFaces = faces
            .Where(face => string.Equals(face.Family, family, StringComparison.OrdinalIgnoreCase))
            .ToList();

        FaceSpec regular = Pick(familyFaces, 400, italic: false) ?? familyFaces[0];
        FaceSpec? bold = Pick(familyFaces, 700, italic: false);
        FaceSpec? italic = Pick(familyFaces, 400, italic: true);
        FaceSpec? boldItalic = Pick(familyFaces, 700, italic: true);

        string slug = Slugify(family) + "-" + ShortHash(cssUri.ToString());
        string folder = Path.Combine(Root, slug);
        TextFont? cached = TryReuse(slug, family, folder);
        if (cached is not null)
        {
            return cached;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "source.css"), css);

        Dictionary<string, string> saved = new(StringComparer.OrdinalIgnoreCase);
        string regularFile = SaveFace(regular, "regular.ttf", folder, cssUri, saved);
        string boldFile = bold is null ? regularFile : SaveFace(bold, "bold.ttf", folder, cssUri, saved);
        string italicFile = italic is null ? regularFile : SaveFace(italic, "italic.ttf", folder, cssUri, saved);
        string boldItalicFile = boldItalic is null
            ? (bold is null ? regularFile : boldFile)
            : SaveFace(boldItalic, "bold-italic.ttf", folder, cssUri, saved);

        WriteLocalCss(folder, family, cssUri, regularFile, boldFile, italicFile, boldItalicFile);

        TextFont font = new(family, regularFile, boldFile, italicFile, boldItalicFile, folder);
        string preview = Path.Combine(folder, "preview.png");
        if (File.Exists(preview))
        {
            File.Delete(preview);
        }

        PreviewPath(font);

        List<FontEntry> entries = ReadIndex();
        entries.RemoveAll(entry =>
            string.Equals(entry.Slug, slug, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Name, family, StringComparison.OrdinalIgnoreCase));
        entries.Add(new FontEntry(family, slug, regularFile, boldFile, italicFile, boldItalicFile));
        WriteIndex(entries);
        return font;
    }

    /// <summary>Turns a family name, cdnfonts page or stylesheet URL into a CSS URL.</summary>
    private static Uri ResolveCssUri(string input)
    {
        string value = (input ?? string.Empty).Trim().Trim('"', '\'');
        if (value.Length == 0)
        {
            throw new InvalidOperationException("Enter a cdnfonts URL or a font name.");
        }

        if (value.StartsWith("fonts.cdnfonts.com/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("www.cdnfonts.com/", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            if (absolute.Host.EndsWith("cdnfonts.com", StringComparison.OrdinalIgnoreCase)
                && !absolute.AbsolutePath.StartsWith("/css/", StringComparison.OrdinalIgnoreCase))
            {
                string slug = absolute.AbsolutePath.Trim('/');
                if (slug.EndsWith(".font", StringComparison.OrdinalIgnoreCase))
                {
                    slug = slug[..^5];
                }

                int slash = slug.IndexOf('/');
                if (slash >= 0)
                {
                    slug = slug[..slash];
                }

                if (slug.Length > 0)
                {
                    return new Uri(DefaultCssRoot + Uri.EscapeDataString(slug));
                }
            }

            return absolute;
        }

        string name = value.Trim('/');
        if (name.EndsWith(".font", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^5];
        }

        int lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            name = name[(lastSlash + 1)..];
        }

        if (name.Length == 0)
        {
            throw new InvalidOperationException("Enter a cdnfonts URL or a font name.");
        }

        return new Uri(DefaultCssRoot + Uri.EscapeDataString(name.ToLowerInvariant()));
    }

    /// <summary>Fetches a stylesheet, following one level of @import.</summary>
    private static string FetchCss(Uri uri, int depth)
    {
        string css = FetchText(uri);
        if (depth >= 2)
        {
            return css;
        }

        StringBuilder combined = new(css);
        foreach (Match match in ImportPattern.Matches(css))
        {
            string target = match.Groups["url"].Value.Trim();
            if (!Uri.TryCreate(uri, target, out Uri? imported)
                || (imported.Scheme != Uri.UriSchemeHttp && imported.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            try
            {
                combined.Append('\n').Append(FetchCss(imported, depth + 1));
            }
            catch
            {
                // A missing optional import should not hide the main stylesheet.
            }
        }

        return combined.ToString();
    }

    /// <summary>Parses every usable @font-face rule from a stylesheet.</summary>
    private static List<FaceSpec> ParseFaces(string css, Uri baseUri)
    {
        List<FaceSpec> faces = [];
        foreach (Match match in FacePattern.Matches(css))
        {
            string body = match.Groups["body"].Value;
            string family = CleanFamily(Declaration(body, "font-family"));
            if (family.Length == 0)
            {
                continue;
            }

            string? source = ChooseSource(Declaration(body, "src"));
            if (source is null)
            {
                continue;
            }

            Uri resolved = Uri.TryCreate(source, UriKind.Absolute, out Uri? absolute)
                ? absolute
                : new Uri(baseUri, source);
            faces.Add(new FaceSpec(
                family,
                ParseWeight(Declaration(body, "font-weight")),
                IsItalic(Declaration(body, "font-style")),
                resolved.ToString()));
        }

        return faces;
    }

    /// <summary>Chooses the most complete family in a stylesheet.</summary>
    private static string ChooseFamily(List<FaceSpec> faces)
    {
        IGrouping<string, FaceSpec> best = faces
            .GroupBy(face => face.Family, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => Score(group))
            .ThenBy(group => group.Key.Length)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .First();
        return best.First().Family;
    }

    /// <summary>Scores how many of the four text styles a family offers.</summary>
    private static int Score(IEnumerable<FaceSpec> faces)
    {
        List<FaceSpec> list = faces.ToList();
        int score = 0;
        if (list.Any(face => !face.Italic && face.Weight == 400))
        {
            score += 8;
        }

        if (list.Any(face => !face.Italic && face.Weight == 700))
        {
            score += 4;
        }

        if (list.Any(face => face.Italic && face.Weight == 400))
        {
            score += 2;
        }

        if (list.Any(face => !face.Italic))
        {
            score += 1;
        }

        return score;
    }

    /// <summary>Picks the face nearest a weight/style pair.</summary>
    private static FaceSpec? Pick(List<FaceSpec> faces, int weight, bool italic) =>
        faces
            .Where(face => face.Italic == italic)
            .OrderBy(face => Math.Abs(face.Weight - weight))
            .FirstOrDefault();

    /// <summary>Saves one style, reusing the file when the URL is shared.</summary>
    private static string SaveFace(
        FaceSpec face,
        string fileName,
        string folder,
        Uri baseUri,
        Dictionary<string, string> saved)
    {
        if (saved.TryGetValue(face.Url, out string? existing))
        {
            return existing;
        }

        byte[] data = Download(face.Url, baseUri);
        byte[] sfnt = WoffFont.ToSfnt(data);
        ValidateSfnt(sfnt, face.Family);
        File.WriteAllBytes(Path.Combine(folder, fileName), sfnt);
        saved[face.Url] = fileName;
        return fileName;
    }

    /// <summary>Rejects files StbTrueType cannot open before they reach the UI.</summary>
    private static void ValidateSfnt(byte[] data, string family)
    {
        try
        {
            if (CreateFont(data, 0) is null)
            {
                throw new InvalidDataException($"The {family} font file is not a readable TrueType/OpenType font.");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"The {family} font file could not be read: {ex.Message}", ex);
        }
    }

    /// <summary>Downloads a font URL, including base64 data URIs.</summary>
    private static byte[] Download(string source, Uri baseUri)
    {
        if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = source.IndexOf(',');
            if (comma < 0 || !source[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Only base64 font data URIs are supported.");
            }

            return Convert.FromBase64String(source[(comma + 1)..]);
        }

        Uri uri = Uri.TryCreate(source, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(baseUri, source);
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        using HttpResponseMessage response = Client.Send(request);
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    }

    /// <summary>Fetches stylesheet text.</summary>
    private static string FetchText(Uri uri)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        using HttpResponseMessage response = Client.Send(request);
        response.EnsureSuccessStatusCode();
        using Stream stream = response.Content.ReadAsStream();
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Reads one declaration value from an @font-face body.</summary>
    private static string? Declaration(string body, string property)
    {
        Match match = Regex.Match(
            body,
            @"(?<![\w-])" + Regex.Escape(property) + @"\s*:\s*(?<value>[^;]+)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>Strips quotes and fallback families from a font-family value.</summary>
    private static string CleanFamily(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        int comma = text.IndexOf(',');
        if (comma >= 0)
        {
            text = text[..comma];
        }

        return text.Trim().Trim('"', '\'').Trim();
    }

    /// <summary>Chooses the best URL from a src declaration.</summary>
    private static string? ChooseSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string? best = null;
        int bestRank = -1;
        foreach (Match match in UrlPattern.Matches(value))
        {
            string url = match.Groups["url"].Value.Trim();
            if (url.Length == 0)
            {
                continue;
            }

            int rank = url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? 2
                : url.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ? 1
                : url.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ? 4
                : url.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? 5
                : url.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ? 5
                : 3;

            if (rank > bestRank)
            {
                best = url;
                bestRank = rank;
            }
        }

        return best;
    }

    /// <summary>Reads a CSS font weight.</summary>
    private static int ParseWeight(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (text.Contains("bold", StringComparison.OrdinalIgnoreCase))
        {
            return 700;
        }

        Match match = Regex.Match(text, @"\d+");
        return match.Success && int.TryParse(match.Value, out int weight)
            ? Math.Clamp(weight, 1, 1000)
            : 400;
    }

    /// <summary>Gets whether a CSS font-style requests an oblique or italic face.</summary>
    private static bool IsItalic(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        return text.Contains("italic", StringComparison.OrdinalIgnoreCase)
            || text.Contains("oblique", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes a local @font-face stylesheet and class next to the fonts.</summary>
    private static void WriteLocalCss(
        string folder,
        string family,
        Uri cssUri,
        string regular,
        string bold,
        string italic,
        string boldItalic)
    {
        string escaped = family.Replace("\\", "\\\\").Replace("\"", "\\\"");
        StringBuilder css = new();
        css.Append("/* Cached by PixelDotNet from ").Append(cssUri).AppendLine(" */");
        AppendFace(css, escaped, "normal", 400, regular);
        AppendFace(css, escaped, "normal", 700, bold);
        AppendFace(css, escaped, "italic", 400, italic);
        AppendFace(css, escaped, "italic", 700, boldItalic);
        css.Append(".web-font-").Append(Slugify(family))
            .Append(" { font-family: \"").Append(escaped).AppendLine("\"; }");
        File.WriteAllText(Path.Combine(folder, "font.css"), css.ToString());
    }

    /// <summary>Appends one local @font-face block.</summary>
    private static void AppendFace(StringBuilder css, string family, string style, int weight, string file)
    {
        css.Append("@font-face { font-family: \"").Append(family)
            .Append("\"; font-style: ").Append(style)
            .Append("; font-weight: ").Append(weight)
            .Append("; src: url(\"").Append(file)
            .AppendLine("\") format(\"truetype\"); }");
    }

    /// <summary>Renders a small white-on-transparent sample for the font menu.</summary>
    private static void GeneratePreview(TextFont font, string path)
    {
        const string Sample = "The quick brown fox";
        if (!TextRasterizer.Render(
            font,
            Sample,
            30f,
            false,
            false,
            TextAlign.Left,
            out byte[] coverage,
            out int width,
            out int height,
            out _,
            out _))
        {
            return;
        }

        int minX = width;
        int minY = height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (coverage[row + x] == 0)
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return;
        }

        int cropWidth = maxX - minX + 1;
        int cropHeight = maxY - minY + 1;
        uint[] pixels = new uint[cropWidth * cropHeight];
        for (int y = 0; y < cropHeight; y++)
        {
            int sourceRow = (minY + y) * width;
            int targetRow = y * cropWidth;
            for (int x = 0; x < cropWidth; x++)
            {
                byte alpha = coverage[sourceRow + minX + x];
                pixels[targetRow + x] = alpha == 0 ? 0u : ((uint)alpha << 24) | 0x00FFFFFFu;
            }
        }

        File.WriteAllBytes(path, PngWriter.Encode(pixels, cropWidth, cropHeight));
    }

    private static List<FontEntry> ReadIndex()
    {
        string path = Path.Combine(Root, "index.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<FontEntry>>(File.ReadAllText(path), IndexOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void WriteIndex(List<FontEntry> entries)
    {
        Directory.CreateDirectory(Root);
        string path = Path.Combine(Root, "index.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries, IndexOptions));
        File.Move(temporary, path, overwrite: true);
    }

    private static string DefaultRoot()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
        {
            local = AppContext.BaseDirectory;
        }

        return Path.Combine(local, "PixelDotNet", "fonts");
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PixelDotNet/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/css,*/*;q=0.8");
        return client;
    }

    private static string ShortHash(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static string Slugify(string value)
    {
        StringBuilder builder = new();
        bool dash = false;
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }

        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "font" : slug;
    }

    /// <summary>One @font-face rule from the remote stylesheet.</summary>
    private sealed record FaceSpec(string Family, int Weight, bool Italic, string Url);
}

/// <summary>One cached family in the on-disk index.</summary>
public sealed record FontEntry(
    string Name,
    string Slug,
    string Regular,
    string Bold,
    string Italic,
    string BoldItalic);
