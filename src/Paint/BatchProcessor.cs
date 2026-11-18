using System;
using System.Collections.Generic;
using System.IO;

namespace PixelDotNet.Paint;

/// <summary>Options for one batch run.</summary>
public sealed class BatchOptions
{
    /// <summary>Gets or sets the folder to read images from.</summary>
    public string SourceFolder { get; set; } = string.Empty;

    /// <summary>Gets or sets the folder to write results to.</summary>
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>Gets or sets the resize percentage, where 100 keeps the size.</summary>
    public int ScalePercent { get; set; } = 100;

    /// <summary>Gets or sets the output format.</summary>
    public ImageFormat Format { get; set; } = ImageFormat.Png;

    /// <summary>Gets or sets the suffix appended to each output name.</summary>
    public string Suffix { get; set; } = "-edited";

    /// <summary>Gets or sets whether existing output files are replaced.</summary>
    public bool Overwrite { get; set; }
}

/// <summary>The outcome of a batch run.</summary>
/// <param name="Found">Images discovered in the source folder.</param>
/// <param name="Written">Files written.</param>
/// <param name="Skipped">Files skipped because the target existed.</param>
/// <param name="Failed">Files that could not be processed.</param>
/// <param name="Message">A human-readable summary.</param>
public readonly record struct BatchResult(
    int Found,
    int Written,
    int Skipped,
    int Failed,
    string Message);

/// <summary>Applies a resize and format conversion to every image in a folder.</summary>
public static class BatchProcessor
{
    private const int MaxFiles = 500;

    /// <summary>Lists the supported images in a folder, sorted by name.</summary>
    public static string[] FindImages(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        try
        {
            List<string> images = [];
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (ImageLoader.IsImage(file))
                {
                    images.Add(file);
                }
            }

            images.Sort(StringComparer.OrdinalIgnoreCase);
            if (images.Count > MaxFiles)
            {
                images.RemoveRange(MaxFiles, images.Count - MaxFiles);
            }

            return [.. images];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Processes the images described by <paramref name="options"/>.</summary>
    public static BatchResult Run(BatchOptions options)
    {
        string source = options.SourceFolder.Trim();
        string output = options.OutputFolder.Trim();
        if (output.Length == 0)
        {
            output = source;
        }

        if (!Directory.Exists(source))
        {
            return new BatchResult(0, 0, 0, 0, "Source folder not found.");
        }

        try
        {
            Directory.CreateDirectory(output);
        }
        catch (Exception error)
        {
            return new BatchResult(0, 0, 0, 0, error.Message);
        }

        string[] files = FindImages(source);
        int scale = Math.Clamp(options.ScalePercent, 1, 400);
        int written = 0;
        int skipped = 0;
        int failed = 0;
        string firstError = string.Empty;

        foreach (string file in files)
        {
            try
            {
                string stem = Path.GetFileNameWithoutExtension(file);
                string target = Path.Combine(
                    output,
                    stem + options.Suffix + ImageExporter.Extension(options.Format));

                if (!options.Overwrite && File.Exists(target))
                {
                    skipped++;
                    continue;
                }

                (int width, int height, uint[] pixels) = ImageLoader.Load(file);
                int targetWidth = Math.Max(1, (int)MathF.Round(width * scale / 100f));
                int targetHeight = Math.Max(1, (int)MathF.Round(height * scale / 100f));

                if (targetWidth != width || targetHeight != height)
                {
                    pixels = ImageOps.Scale(pixels, width, height, targetWidth, targetHeight);
                }

                File.WriteAllBytes(
                    target,
                    ImageExporter.Encode(pixels, targetWidth, targetHeight, options.Format));
                written++;
            }
            catch (Exception error)
            {
                failed++;
                if (firstError.Length == 0)
                {
                    firstError = Path.GetFileName(file) + ": " + error.Message;
                }
            }
        }

        string message = $"Processed {written} of {files.Length} image(s).";
        if (skipped > 0)
        {
            message += $" {skipped} skipped.";
        }

        if (failed > 0)
        {
            message += $" {failed} failed.";
        }

        if (firstError.Length > 0)
        {
            message += " " + firstError;
        }

        return new BatchResult(files.Length, written, skipped, failed, message);
    }
}
