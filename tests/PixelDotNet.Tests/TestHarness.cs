using Donkeh.Testing;
using Donkeh.UILab;
using PixelDotNet.Components;
using PixelDotNet.Paint;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace PixelDotNet.Tests;

/// <summary>Shared fixtures for the paint-engine and component tests.</summary>
internal static class TestHarness
{
    /// <summary>Creates a document with a filled background and one empty ink layer.</summary>
    public static PaintDocument OpaqueDocument(int width = 32, int height = 24, uint? color = null)
    {
        PaintDocument document = new(width, height);
        PaintLayer background = new("Background", width, height, isBackground: true);
        background.Fill(color ?? Rgba.White);
        PaintLayer ink = new("Layer 1", width, height);
        document.Layers.Add(background);
        document.Layers.Add(ink);
        document.ActiveIndex = 1;
        document.MarkDirty();
        return document;
    }

    /// <summary>Creates a session over a small opaque document.</summary>
    public static PaintSession Session(int width = 32, int height = 24) =>
        new(OpaqueDocument(width, height), "Test");

    /// <summary>Wraps a component in the editor provider the application uses.</summary>
    public static Node WithSession(PaintSession session, Node child) =>
        Provider(EditorContext.Value, session, child);

    /// <summary>Mounts a component against a supplied session.</summary>
    public static ComponentTestHarness Render(PaintSession session, Node child) =>
        ComponentTest.Render(WithSession(session, child));

    /// <summary>Re-renders a mounted component after it mutated the session.</summary>
    public static void Reload<T>(ComponentTestHarness page, PaintSession session)
        where T : Component, new() =>
        page.Update(WithSession(session, C<T>()));

    /// <summary>Fills a layer with a deterministic two-colour pattern.</summary>
    public static void FillPattern(PaintLayer layer)
    {
        for (int y = 0; y < layer.Height; y++)
        {
            for (int x = 0; x < layer.Width; x++)
            {
                layer.Put(x, y, ((x / 2) + (y / 2)) % 2 == 0
                    ? Rgba.FromRgb(0x20 + (x * 4), 0x40 + (y * 3), 0x90)
                    : Rgba.FromRgb(0xE0, 0x30 + (x * 2), 0x20 + (y * 6)));
            }
        }
    }

    /// <summary>Returns whether any pixel in the layer has changed from the snapshot.</summary>
    public static bool Differs(uint[] left, uint[] right)
    {
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>A temporary folder that is removed after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "PixelDotNetTests",
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the actual assertion result.
        }
    }
}
