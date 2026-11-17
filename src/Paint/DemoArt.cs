using System;

namespace PixelDotNet.Paint;

/// <summary>
/// Builds the sample artwork the app opens with, and blank documents for the
/// New action.
/// </summary>
/// <remarks>
/// The sample is deliberately built from flat shapes and vertical gradients so the
/// run segmenter stays small: great for showing the canvas, the layer panel and the
/// eyedropper working on the first frame. Every shape is expressed relative to the
/// document size, so changing the defaults keeps the composition intact.
/// </remarks>
public static class DemoArt
{
    /// <summary>The document size the sample artwork uses.</summary>
    public const int DefaultWidth = 768;

    /// <summary>The document height the sample artwork uses.</summary>
    public const int DefaultHeight = 576;

    /// <summary>Builds the layered sample artwork.</summary>
    public static PaintDocument BuildDocument()
    {
        PaintDocument document = new(DefaultWidth, DefaultHeight);
        PaintLayer sky = new("Sky", DefaultWidth, DefaultHeight, isBackground: true);
        PaintLayer peaks = new("Peaks", DefaultWidth, DefaultHeight);
        PaintLayer ink = new("Ink", DefaultWidth, DefaultHeight);

        document.Layers.Add(sky);
        document.Layers.Add(peaks);
        document.Layers.Add(ink);
        document.ActiveIndex = 2;

        float height = DefaultHeight;
        float width = DefaultWidth;

        // Night sky, lit from the horizon.
        PaintOps.VerticalGradient(sky, 0, DefaultHeight, Rgba.FromRgb(0x08, 0x0D, 0x1E), Rgba.FromRgb(0x2C, 0x5C, 0x93));

        // Stars, deterministic so every launch looks the same. Denser and tighter
        // near the top, sparse toward the horizon glow.
        Random random = new(20240517);
        int starCount = DefaultWidth * DefaultHeight / 2400;
        for (int i = 0; i < starCount; i++)
        {
            int x = random.Next(DefaultWidth);
            int y = random.Next(8, (int)(height * 0.46f));
            int alpha = random.Next(55, 225);
            PaintOps.Blend(sky, x, y, Rgba.FromRgb(0xD7, 0xE3, 0xFF), alpha);
        }

        // Moon: overlapping translucent discs approximate a smooth radial glow,
        // then the disc itself sits on top. Keep the ring count low: every ring
        // adds thousands of paint runs and pushes the document past its run cap.
        float moonX = width * 0.70f;
        float moonY = height * 0.29f;
        const int HaloSteps = 8;
        for (int step = HaloSteps; step >= 1; step--)
        {
            float t = step / (float)HaloSteps;
            PaintOps.Disc(
                sky,
                moonX,
                moonY,
                width * (0.070f + (0.062f * t)),
                Rgba.FromRgb(0xF6, 0xDC, 0xA4),
                9);
        }

        PaintOps.Disc(sky, moonX, moonY, width * 0.0810f, Rgba.FromRgb(0xF7, 0xD9, 0x92), 238);
        PaintOps.Disc(sky, moonX - (width * 0.005f), moonY - (height * 0.004f), width * 0.0690f, Rgba.FromRgb(0xFF, 0xEC, 0xBE), 255);

        // Three ridges, far to near.
        PaintOps.Ridge(peaks, height * 0.56f, width * 0.068f, 0.12f, 1.15f, Rgba.FromRgb(0x4C, 0x5E, 0x89));
        PaintOps.Ridge(peaks, height * 0.67f, width * 0.078f, 0.58f, 0.85f, Rgba.FromRgb(0x30, 0x40, 0x66));
        PaintOps.Ridge(peaks, height * 0.77f, width * 0.052f, 0.93f, 1.65f, Rgba.FromRgb(0x18, 0x22, 0x3B));

        // Water plane with a moon reflection.
        PaintOps.Rect(peaks, 0, (int)(height * 0.855f), DefaultWidth, DefaultHeight, Rgba.FromRgb(0x10, 0x19, 0x2E));
        int reflectionX = (int)(width * 0.70f);
        Random ripples = new(7193);
        int rippleY = (int)(height * 0.864f);
        for (int i = 0; i < 16 && rippleY < DefaultHeight - 3; i++)
        {
            int band = (int)(width * 0.018f) + (i * (int)(width * 0.0068f));
            int thickness = Math.Max(1, (int)(height * 0.0066f) - (i / 8));
            uint stripe = Rgba.Lerp(
                Rgba.FromRgb(0xF7, 0xD9, 0x92),
                Rgba.FromRgb(0x10, 0x19, 0x2E),
                0.16f + (i * 0.052f));

            // Break each ripple into a few dashes so the path shimmers instead of
            // reading as a solid triangle.
            int segments = 2 + (i / 4);
            int segmentWidth = (band * 2) / segments;
            for (int s = 0; s < segments; s++)
            {
                int inset = ripples.Next(0, 4);
                int dash = Math.Max(2, segmentWidth - (inset * 2));
                int x = reflectionX - band + (s * segmentWidth) + inset + ripples.Next(-1, 2);
                PaintOps.Rect(peaks, x, rippleY, dash, thickness, stripe);
            }

            rippleY += thickness + 1 + (i / 4);
        }

        // And a horizon line to separate water from sky.
        PaintOps.Rect(peaks, 0, (int)(height * 0.852f), DefaultWidth, Math.Max(2, (int)(height * 0.004f)), Rgba.FromRgb(0x33, 0x4C, 0x74));

        document.MarkDirty();
        return document;
    }

    /// <summary>Builds a blank document: an opaque white background and one empty layer.</summary>
    public static PaintDocument BuildBlank(int width, int height)
    {
        int safeWidth = Math.Clamp(width, 32, 2048);
        int safeHeight = Math.Clamp(height, 32, 2048);

        PaintDocument document = new(safeWidth, safeHeight);
        PaintLayer background = new("Background", safeWidth, safeHeight, isBackground: true);
        background.Fill(Rgba.White);
        PaintLayer ink = new("Layer 1", safeWidth, safeHeight);

        document.Layers.Add(background);
        document.Layers.Add(ink);
        document.ActiveIndex = 1;
        document.MarkDirty();
        return document;
    }
}
