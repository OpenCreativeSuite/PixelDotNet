using System;
using System.Collections.Generic;
using System.IO;

namespace PixelDotNet.Paint;

/// <summary>Horizontal alignment of the text line relative to its anchor.</summary>
public enum TextAlign
{
    /// <summary>The anchor is the left edge of the line.</summary>
    Left,

    /// <summary>The anchor is the centre of the line.</summary>
    Center,

    /// <summary>The anchor is the right edge of the line.</summary>
    Right,
}

/// <summary>A font family the text tool can use, with its four style files.</summary>
/// <param name="Name">Display name shown in the options bar.</param>
/// <param name="Regular">File name of the regular weight.</param>
/// <param name="Bold">File name of the bold weight.</param>
/// <param name="Italic">File name of the italic weight.</param>
/// <param name="BoldItalic">File name of the bold italic weight.</param>
/// <param name="Folder">
/// Absolute folder holding the four files, for downloaded families. When null the
/// files are looked up in the system font folder.
/// </param>
public sealed record TextFont(
    string Name,
    string Regular,
    string Bold,
    string Italic,
    string BoldItalic,
    string? Folder = null)
{
    /// <summary>Gets whether the family lives in the downloaded-font cache.</summary>
    public bool IsDownloaded => Folder is not null;

    /// <summary>Gets the absolute path of one of the style files.</summary>
    public string FilePath(string file)
    {
        if (Path.IsPathRooted(file))
        {
            return file;
        }

        string folder = Folder ?? Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return Path.Combine(folder, file);
    }
}

/// <summary>The font families the text tool offers.</summary>
/// <remarks>
/// Built-in families are looked up in the system font folder, so the list is a
/// best-effort catalogue: a missing family falls back to its regular face, then
/// to Segoe UI. Families imported from cdnfonts are appended after the built-ins
/// and carry the absolute folder their cached files were saved to.
/// </remarks>
public static class TextFonts
{
    /// <summary>Left edge of the font popup in app-local pixels.</summary>
    public const float MenuX = 380f;

    /// <summary>Top edge of the font popup in app-local pixels.</summary>
    public const float MenuY = 74f;

    /// <summary>Width of the font popup in app-local pixels.</summary>
    public const float MenuWidth = 430f;

    private static readonly TextFont[] BuiltIn =
    [
        new("Segoe UI", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        new("Arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        new("Verdana", "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
        new("Tahoma", "tahoma.ttf", "tahomabd.ttf", "tahoma.ttf", "tahomabd.ttf"),
        new("Calibri", "calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf"),
        new("Candara", "candara.ttf", "candarab.ttf", "candarai.ttf", "candaraz.ttf"),
        new("Palatino Linotype", "pala.ttf", "palab.ttf", "palai.ttf", "palabi.ttf"),
        new("Georgia", "georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf"),
        new("Times New Roman", "times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf"),
        new("Trebuchet MS", "trebuc.ttf", "trebucbd.ttf", "trebucit.ttf", "trebucbi.ttf"),
        new("Consolas", "consola.ttf", "consolab.ttf", "consolai.ttf", "consolaz.ttf"),
        new("Courier New", "cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf"),
        new("Comic Sans MS", "comic.ttf", "comicbd.ttf", "comici.ttf", "comicz.ttf"),
    ];

    private static readonly List<TextFont> Downloaded = [];
    private static TextFont[] _all = BuiltIn;

    static TextFonts()
    {
        try
        {
            foreach (TextFont font in FontCatalog.LoadCached())
            {
                Add(font);
            }
        }
        catch
        {
            // A broken or unreadable cache must never stop the app from starting.
        }

        Rebuild();
    }

    /// <summary>Gets every offered family, built-ins first and downloads after.</summary>
    public static TextFont[] All => _all;

    /// <summary>Gets a family by index, clamped into range.</summary>
    public static TextFont At(int index) => All[Math.Clamp(index, 0, All.Length - 1)];

    /// <summary>
    /// Adds a downloaded family and returns its index. A previous import with the
    /// same cache folder or display name is replaced.
    /// </summary>
    public static int Add(TextFont font)
    {
        string name = font.Name;
        if (Array.Exists(BuiltIn, builtIn => string.Equals(builtIn.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name += " (CDN)";
        }

        TextFont stored = font with { Name = name };
        Downloaded.RemoveAll(existing =>
            string.Equals(existing.Folder, stored.Folder, StringComparison.OrdinalIgnoreCase)
            || string.Equals(existing.Name, stored.Name, StringComparison.OrdinalIgnoreCase));
        Downloaded.Add(stored);
        Rebuild();
        return Array.FindIndex(_all, candidate => ReferenceEquals(candidate, stored));
    }

    private static void Rebuild()
    {
        TextFont[] combined = new TextFont[BuiltIn.Length + Downloaded.Count];
        BuiltIn.CopyTo(combined, 0);
        Downloaded.CopyTo(combined, BuiltIn.Length);
        _all = combined;
    }
}
