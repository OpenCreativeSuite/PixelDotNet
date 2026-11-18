using System;
using System.Collections.Generic;
using PixelDotNet.Components;

namespace PixelDotNet.Help;

/// <summary>Preferred anchor for a tooltip bubble.</summary>
public enum TipPlacement
{
    /// <summary>Let the overlay choose the side with room.</summary>
    Auto,

    /// <summary>Put the bubble to the right of the control.</summary>
    Right,

    /// <summary>Put the bubble below the control.</summary>
    Below,

    /// <summary>Put the bubble to the left of the control.</summary>
    Left,

    /// <summary>Put the bubble above the control.</summary>
    Above,
}

/// <summary>The tooltip currently shown over the canvas chrome.</summary>
/// <param name="Icon">Font Awesome class for the icon.</param>
/// <param name="Title">Name of the control.</param>
/// <param name="Hint">Keyboard shortcut, or an empty string.</param>
/// <param name="HelpId">Help article opened by the chevron.</param>
/// <param name="Placement">Preferred side for the bubble.</param>
/// <param name="ShowChevron">Whether the bubble opens the detailed help modal.</param>
/// <param name="X">Left edge of the hovered control in viewport pixels.</param>
/// <param name="Y">Top edge of the hovered control in viewport pixels.</param>
/// <param name="Width">Width of the hovered control in viewport pixels.</param>
/// <param name="Height">Height of the hovered control in viewport pixels.</param>
public sealed record TipState(
    string Icon,
    string Title,
    string Hint,
    string HelpId,
    TipPlacement Placement,
    bool ShowChevron,
    float X,
    float Y,
    float Width,
    float Height);

/// <summary>Long-form documentation shown in the help modal.</summary>
public sealed record HelpTopic(
    string Icon,
    string Title,
    string Shortcut,
    string Summary,
    string[] HowItWorks,
    string[] Tips);

/// <summary>The help articles behind the tooltip chevron buttons.</summary>
public static class HelpCatalog
{
    private static readonly Dictionary<string, HelpTopic> Topics = new(StringComparer.OrdinalIgnoreCase);

    static HelpCatalog()
    {
        Add("tool.move", Icons.Move, "Move", "V",
            "Moves selected pixels, resizes selections, or drags the active layer.",
            [
                "With a selection, dragging inside it lifts its pixels off the active layer and puts them down at the new position.",
                "Rectangle and elliptical selections show eight handles for resizing; lasso and wand selections can be moved but not resized.",
                "With no selection, dragging moves the whole active layer.",
            ],
            [
                "Small drags snap to document pixels.",
                "Switch layers with a selection active to move the same region on another layer.",
            ]);

        Add("tool.marquee", Icons.Marquee, "Marquee", "M",
            "Selects a rectangular region for painting, fills, effects and clipboard work.",
            [
                "Drag from one corner to the other. The dashed outline and blue tint show the selected pixels.",
                "Every editing tool is clipped to the selection and stays on the active layer.",
            ],
            [
                "Select all fills the whole canvas with the selection.",
                "Press Esc to deselect.",
            ]);

        Add("tool.ellipseSelect", Icons.EllipseSelect, "Elliptical Select", "Q",
            "Selects an ellipse for circular highlights, vignettes and cut-outs.",
            [
                "Drag a bounding box; the ellipse is inscribed in it.",
                "With the Move tool, drag a handle to resize the ellipse and its pixels together.",
            ],
            [
                "Perfect for spotlights and soft circular masks.",
                "Press Esc to clear the selection.",
            ]);

        Add("tool.lasso", Icons.Lasso, "Lasso Select", "F",
            "Draws a freehand selection that closes when you release the pointer.",
            [
                "Drag a loose outline around the subject. The path closes automatically.",
                "Lasso selections can be moved with the Move tool but have no resize handles.",
            ],
            [
                "Draw slightly outside the subject to leave room for soft edges.",
                "Press Esc to cancel.",
            ]);

        Add("tool.QuickSelect", Icons.QuickSelect, "Quick Selection", "A",
            "Brush over an object to grow a selection through similar, connected pixels.",
            [
                "Each dab samples the composited colour and adds its connected region to the selection.",
                "Size controls the sampling brush; Tolerance controls how different a pixel may be and still join the region.",
                "Turn on Subtract, or hold Alt, to remove regions from the selection.",
            ],
            [
                "Start with a small brush on a clean edge; the region usually grows far beyond the dab.",
                "Lower the tolerance when a selection leaks into the background.",
                "Deselect first to start a fresh selection.",
            ]);

        Add("tool.magicWand", Icons.MagicWand, "Magic Wand", "W",
            "Selects similar colours on the active layer with one click.",
            [
                "Tolerance controls how many colour steps count as a match. Contiguous limits the result to the region touching the click.",
                "The wand always samples the active layer, so switch layers if the colour lives elsewhere.",
            ],
            [
                "Raise tolerance for gradients and skies; lower it for flat colour separation.",
                "Use Edit > Copy Merged when you need the visible result instead.",
            ]);

        Add("tool.brush", Icons.Brush, "Brush", "B",
            "Paints with the foreground colour using a round, hardness-controlled tip.",
            [
                "Size is the diameter in document pixels, hardness sets the edge falloff and opacity caps stroke coverage.",
                "Strokes rebuild from a snapshot, so overlapping stamps never darken twice. Painting is clipped to the selection.",
            ],
            [
                "Press [ and ], or Alt+scroll over the canvas, to resize the brush.",
                "Use low opacity to build smooth colour gradually.",
            ]);

        Add("tool.eraser", Icons.Eraser, "Eraser", "E",
            "Removes paint from the active layer.",
            [
                "On normal layers the eraser creates transparency; on the background layer it paints white.",
                "Size, hardness and opacity behave like the brush, and the selection clips the result.",
            ],
            [
                "Erase on a separate layer to keep the artwork below intact.",
                "Use a soft eraser for fades and a hard one for cut-outs.",
            ]);

        Add("tool.CloneStamp", Icons.CloneStamp, "Clone Stamp", "C",
            "Copies pixels from a sampled source point to wherever you paint.",
            [
                "Ctrl-click to set the sample point, then paint. The offset from the first stroke point is preserved.",
                "The sample is taken from the active layer snapshot at the start of each stroke, so one clone stroke never feeds back into itself.",
                "Size, hardness and opacity control the stamp.",
            ],
            [
                "Use a soft brush for seamless blending.",
                "Move the crosshair often to avoid repeating the same texture.",
                "Clone on a new layer when you want to keep the original edit reversible.",
            ]);

        Add("tool.HealingBrush", Icons.HealingBrush, "Healing Brush", "H",
            "Paints sampled texture while matching the destination tone.",
            [
                "Ctrl-click to set the sample source, then paint over the blemish.",
                "The sampled colour is shifted so its luminance matches the destination, which softens tonal seams.",
                "Uses the same size, hardness and opacity as the brush.",
            ],
            [
                "Best for skin, skies and other smooth areas.",
                "Use a small brush and short strokes for control.",
                "For exact texture copying, use the Clone Stamp instead.",
            ]);

        Add("tool.DodgeBurn", Icons.DodgeBurn, "Dodge / Burn", "J",
            "Lightens or darkens pixels with brush strokes.",
            [
                "Choose Dodge to lighten or Burn to darken, then paint.",
                "Size and hardness shape the stroke; Strength controls how quickly the tone changes.",
                "Dodging and burning never changes hue or saturation.",
            ],
            [
                "Use low strength and build the effect in several passes.",
                "Dodge highlights and burn shadows for more depth.",
                "Works well inside a selection for controlled local contrast.",
            ]);

        Add("tool.line", Icons.Line, "Line", "L",
            "Drags a straight stroke with the current brush size and opacity.",
            [
                "Press at the start and drag to the end; the preview follows the pointer.",
                "Weight sets thickness, opacity sets strength and the selection clips the line.",
            ],
            [
                "Great for hatching and technical detail.",
                "Low opacity makes useful construction lines.",
            ]);

        Add("tool.rectangle", Icons.Rectangle, "Rectangle", "R",
            "Draws a filled or outlined rectangle.",
            [
                "Drag a bounding box. Choose Fill or Stroke in the options bar.",
                "Width sets stroke thickness; opacity and the selection both apply.",
            ],
            [
                "Stroke mode is ideal for frames and UI panels.",
                "Fill mode blocks in large areas quickly.",
            ]);

        Add("tool.ellipse", Icons.Ellipse, "Ellipse", "O",
            "Draws a filled or outlined ellipse.",
            [
                "Drag a bounding box; the shape is the ellipse inscribed in it.",
                "Fill, Stroke and Width work exactly like the rectangle tool.",
            ],
            [
                "Use a stroked ellipse for clean rings and lenses.",
                "Pair it with the elliptical selection for matching shapes.",
            ]);

        Add("tool.gradient", Icons.Gradient, "Gradient", "G",
            "Drags a linear gradient from the foreground to the background colour.",
            [
                "The drag direction and length define the ramp.",
                "Opacity controls strength and the selection clips the result.",
            ],
            [
                "Short drags make tight bands; long drags make gentle fades.",
                "Press X to swap the two colours and reverse the gradient.",
            ]);

        Add("tool.bucket", Icons.Bucket, "Paint Bucket", "K",
            "Flood-fills a connected region with the foreground colour.",
            [
                "Connected pixels within Tolerance are replaced. The fill is clipped to the selection.",
                "Low tolerance respects hard edges; high tolerance blends across gradients.",
            ],
            [
                "Raise tolerance for scanned line art.",
                "Fill on a separate layer to keep the line work editable.",
            ]);

        Add("tool.eyedropper", Icons.Eyedropper, "Eyedropper", "I",
            "Samples a visible colour into the foreground colour.",
            [
                "Click the canvas to pick the composited colour at that pixel.",
                "Right-click also picks a colour, or zooms out with the Zoom tool.",
            ],
            [
                "Pick a colour, then press X to swap it with the background.",
                "The status bar shows the picked hex value.",
            ]);

        Add("tool.text", Icons.Text, "Text", "T",
            "Places live text with font, size, alignment and effects.",
            [
                "Click to set the baseline, type in the options bar, then drag the handles to move, scale or rotate. Place bakes it in; Cancel discards it.",
                "The FX panel adds outlines, shadows, glows and presets with separate fill, outline and shadow colours.",
            ],
            [
                "Use Neon or Retro for an instant poster look.",
                "Place text on its own layer so it stays easy to restyle.",
            ]);

        Add("tool.zoom", Icons.Zoom, "Zoom", "Z",
            "Zooms the canvas in or out around the pointer.",
            [
                "Click to zoom in and right-click to zoom out.",
                "Zooming changes only the view, never the document pixels.",
            ],
            [
                "Ctrl+scroll zooms; Alt+scroll changes the brush size.",
                "Ctrl++ and Ctrl+- step through the zoom stops.",
            ]);

        Add("tab.layers", Icons.Flatten, "Layers", "",
            "The layer stack, masks, blending, opacity and layer actions.",
            [
                "Layers are listed top to bottom. Click one to make it active; painting and effects target only that layer.",
                "Blend mode and opacity control how the layer mixes with the layers below.",
                "Masks hide parts of a layer non-destructively, and Clip below restricts a layer to the visible content beneath it.",
                "Drag a row to reorder it; drop it on a group header to move it inside that folder.",
                "Right-click a layer, group or empty list space for its context actions.",
            ],
            [
                "Keep text, ink and background art on separate layers.",
                "Click a mask thumbnail to paint on the mask with the brush or eraser.",
            ]);

        Add("tab.colour", Icons.Palette, "Colour", "",
            "Edits the foreground, outline and shadow colours.",
            [
                "The spectrum and hue strip set saturation, hue and value; the channel sliders give precise RGB control.",
                "Fill, Outline and Shadow choose which text colour the panel edits.",
            ],
            [
                "Click a swatch to use it immediately.",
                "Press X to swap foreground and background, D to reset.",
            ]);

        Add("tab.adjust", Icons.Sliders, "Adjust", "",
            "Live brightness, contrast, hue, saturation, lightness, levels and curves.",
            [
                "Every control previews on the active layer or inside the selection and rebuilds from the starting pixels, so changes never stack.",
                "Levels set a black point, white point and midtone gamma. Curves map input tone to output tone with draggable points.",
                "The Adjustment Layer buttons add non-destructive Levels or Curves layers above the active layer; they affect everything below them.",
                "Apply keeps one undo step; Reset restores the original pixels.",
            ],
            [
                "Small saturation changes often help more than large ones.",
                "Use a curve point in the shadows and one in the highlights for controlled contrast.",
                "Use a selection to adjust only one region.",
            ]);

        Add("tab.history", Icons.History, "History", "",
            "The undo timeline for the current document.",
            [
                "Click any step to travel back or forward in time. The list keeps the last 24 steps.",
                "Live previews are not added as separate steps until you commit them.",
            ],
            [
                "Ctrl+Z undoes; Ctrl+Y or Ctrl+Shift+Z redoes.",
                "Clicking an older step is faster than repeated undo.",
            ]);

        Add("tab.effects", Icons.Sparkles, "Effects", "",
            "Live filters from blur and sharpen to pixelate, emboss and noise.",
            [
                "Pick an effect to preview it, then drag the amount slider. Each change rebuilds from the pixels before the effect.",
                "Apply bakes it into the active layer or selection; Cancel restores.",
            ],
            [
                "Use a selection to restrict an effect to one area.",
                "Apply on a duplicate layer to keep the original.",
            ]);

        Add("menu.file", Icons.Save, "File", "",
            "Document creation, opening, saving and closing.",
            [
                "Open and Save use the in-app file browser and can write PNG, JPEG, BMP or TGA.",
                "Open Project and Save Project use the .pxd format, which keeps layers, masks, groups, adjustment layers and guides.",
                "Batch Process resizes and converts every supported image in a folder.",
                "Right-click a file row to rename, copy, cut, paste or delete it.",
            ],
            [
                "Ctrl+S opens the save browser; Ctrl+O opens the load browser.",
                "Save a project before closing if you still need the layer stack.",
            ]);

        Add("menu.edit", Icons.Sliders, "Edit", "",
            "Undo history and clipboard operations.",
            [
                "Cut, Copy and Paste work on the active layer and respect the selection shape.",
                "Copy Merged copies the visible composite; Copy stays on the active layer.",
                "Content-Aware Fill reconstructs a selection from the pixels around it.",
                "Perspective Transform moves the four corners of the layer or selection with a live preview.",
                "Mesh Warp drags a three-by-three grid of control points over the layer or selection.",
            ],
            [
                "Paste always creates a new layer.",
                "Copy Merged is handy for flattening a look into a new layer.",
            ]);

        Add("menu.image", Icons.Document, "Image", "",
            "Colour operations, resizing and image-wide commands.",
            [
                "Invert flips every colour channel; Desaturate removes colour while keeping luminance.",
                "Flip Horizontal and Flip Vertical mirror the active layer or selection bounds.",
                "Image Size scales the artwork; Canvas Size changes the document bounds without scaling.",
                "Colour Tools groups balance, threshold, gradient map, replace colour, red-eye removal, channel mixer, selective colour and channel splitting.",
            ],
            [
                "Invert makes quick negatives and mask-like looks.",
                "Canvas Size uses an anchor to decide which side keeps the extra space.",
                "Flatten only when you no longer need individual layers.",
            ]);

        Add("menu.layer", Icons.Flatten, "Layer", "",
            "Create, duplicate, group, merge and delete layers.",
            [
                "New Layer adds an empty layer above the active one; Duplicate copies the active layer.",
                "New Group wraps the active layer in a folder; Ungroup takes it back out.",
                "Layer Style adds a non-destructive shadow, stroke or glow.",
                "Merge Down combines into the layer below; Delete removes the active layer.",
            ],
            [
                "Ctrl+Shift+N adds, Ctrl+J duplicates, Ctrl+E merges down.",
                "Keep effects and text on their own layers.",
            ]);

        Add("menu.select", Icons.Marquee, "Select", "",
            "Selection-wide commands.",
            [
                "Select All selects the whole document; Deselect clears the selection.",
                "Fill with Foreground fills the selected region on the active layer.",
            ],
            [
                "Esc deselects from anywhere.",
                "Selections are document-wide but edits always target the active layer.",
            ]);

        Add("menu.view", Icons.Zoom, "View", "",
            "Zoom, rulers, guides, snapping and document navigation.",
            [
                "Zoom In, Zoom Out and Actual Pixels change only the view.",
                "Toggle Grid shows an eight-pixel guide grid; Toggle Rulers and Toggle Guides show layout aids.",
                "Toggle Snapping makes moved selections and layers snap to guides and canvas edges; Clear Guides removes all guides.",
            ],
            [
                "Drag from a ruler to place a guide.",
                "Ctrl+scroll zooms; Alt+scroll changes the brush size.",
                "Snapping uses a small on-screen threshold and scales with zoom.",
            ]);

        Add("menu.help", Icons.Info, "Help", "",
            "Help options, including the hover tooltips.",
            [
                "Enable Tooltips turns the full hover name-and-chevron bubbles on.",
                "Disable Tooltips hides the chevrons and detailed help, but tool names still appear when hovering the tool rail.",
                "Tooltips start disabled so the chrome stays quiet until you ask for them.",
            ],
            [
                "Enable tooltips when learning a tool, then disable them for uninterrupted painting.",
                "The Help menu itself has no tooltip until tooltips are enabled.",
            ]);

        Add("quick.undo", Icons.Undo, "Undo", "Z",
            "Reverses the last committed action.",
            ["Undo walks back through the history timeline for the current document."],
            ["Ctrl+Z works everywhere.", "Open History to jump back several steps at once."]);

        Add("quick.redo", Icons.Redo, "Redo", "Y",
            "Reapplies the last undone action.",
            ["Redo moves forward one step; a new edit clears the redo branch."],
            ["Ctrl+Y and Ctrl+Shift+Z both redo."]);

        Add("quick.save", Icons.Save, "Save PNG", "Ctrl+S",
            "Writes the current document to a PNG in your Pictures folder.",
            ["The export is the visible composite, including transparency where no layer covers it."],
            ["PNG keeps transparency and hard pixel edges.", "Use File for JPEG, BMP and TGA."]);

        Add("layer.new", Icons.Plus, "New Layer", "Ctrl+Shift+N",
            "Adds an empty layer above the active layer.",
            ["The new layer becomes active so the next stroke lands on it."],
            ["Keep ink, colour and text separate.", "Duplicate before destructive effects."]);

        Add("layer.duplicate", Icons.Duplicate, "Duplicate Layer", "Ctrl+J",
            "Copies the active layer, including pixels and blend settings.",
            ["The copy is inserted above the original and becomes active."],
            ["Duplicate then lower opacity for a glow.", "Compare two colour directions safely."]);

        Add("layer.merge", Icons.MergeDown, "Merge Down", "Ctrl+E",
            "Merges the active layer into the layer below it.",
            ["The two layers become one and the merge is a single undo step."],
            ["Merge after you are happy with the composition."]);

        Add("layer.delete", Icons.Delete, "Delete Layer", "",
            "Removes the active layer from the document.",
            ["The document always keeps at least one layer."],
            ["Hide a layer instead when comparing."]);

        Add("layer.lock", Icons.Lock, "Lock Layer", "",
            "Prevents edits to a layer.",
            ["A locked layer refuses painting, fills, effects, adjustments and moves."],
            ["Lock finished art before adding overlays."]);

        Add("layer.visible", Icons.Eye, "Layer Visibility", "",
            "Shows or hides a layer in the composite.",
            ["Hidden layers do not contribute to the canvas or to Copy Merged."],
            ["Hide layers to inspect what sits underneath."]);

        Add("layer.mask", Icons.Mask, "Layer Mask", "",
            "A greyscale mask controls how much of a layer is visible.",
            [
                "White reveals the layer, black hides it and grey partially reveals it.",
                "Press the mask button to add a mask, then click the mask thumbnail to paint on it with the brush or eraser.",
                "Right-click the mask thumbnail to remove the mask.",
            ],
            [
                "Paint soft edges with a feathered brush instead of erasing pixels.",
                "Masks are undoable and never destroy the layer pixels.",
            ]);

        Add("layer.style", Icons.Sparkles, "Layer Style", "",
            "Non-destructive drop shadow, stroke and outer glow around a layer.",
            [
                "Styles redraw from the layer's pixels, so painting or masking the layer updates them.",
                "Drop Shadow offsets a blurred copy in the shadow colour.",
                "Stroke can sit outside or inside the layer edge.",
                "Outer Glow fades a coloured halo outward from the layer edge.",
            ],
            [
                "Use a modest shadow distance and blur for a lifted look.",
                "A white inside stroke separates artwork from a busy background.",
                "Styles are saved in .pxd projects and survive undo.",
            ]);

        Add("layer.group", Icons.FolderPlus, "New Group", "",
            "Puts the active layer inside a new folder.",
            [
                "New layers created while a grouped layer is active join the same group.",
                "The group header can be collapsed, hidden and faded as a unit.",
            ],
            ["Group related artwork so the stack stays readable."]);

        Add("layer.ungroup", Icons.FolderMinus, "Ungroup", "",
            "Takes the active layer out of its group.",
            ["When the last layer leaves a group, the empty folder is removed."],
            ["Ungroup before merging layers that should sit at the top level."]);

        Add("layer.expand", Icons.ChevronDown, "Expand Group", "",
            "Shows or hides the layers inside a group.",
            ["Collapsed groups still composite their layers normally."],
            ["Collapse finished groups to save panel space."]);

        Add("layer.groupVisible", Icons.Eye, "Group Visibility", "",
            "Shows or hides every layer in a group at once.",
            ["Hiding a group is equivalent to hiding each of its layers."],
            ["Toggle a group to compare it against the rest of the image."]);

        Add("status.zoomOut", Icons.Minus, "Zoom Out", "Ctrl+-",
            "Steps the canvas zoom down one stop.",
            ["Zooming changes only the view, never the document resolution."],
            ["Ctrl+0 returns to 100%."]);

        Add("status.zoomIn", Icons.Plus, "Zoom In", "Ctrl++",
            "Steps the canvas zoom up one stop.",
            ["Zooming changes only the view, never the document resolution."],
            ["Zoom in to inspect hard pixel edges."]);

        Add("status.zoomReset", Icons.Zoom, "Actual Pixels", "Ctrl+0",
            "Returns the view to one document pixel per screen pixel.",
            ["This is the true 100% view of the artwork."],
            ["Ctrl+0 works from the canvas or the status bar."]);

        Add("options.bold", Icons.Text, "Bold", "",
            "Uses the bold face of the selected family.",
            ["Bold is a font style, not a stroke effect."],
            ["For a heavier outline, use the FX outline instead."]);

        Add("options.italic", Icons.Text, "Italic", "",
            "Uses the italic face of the selected family.",
            ["Rotation is separate: use the text transform handle to angle a whole line."],
            ["Combine bold and italic for emphasis."]);

        Add("options.alignLeft", Icons.AlignLeft, "Align Left", "",
            "Anchors the text line's left edge at the click point.",
            ["Alignment is part of the text render and updates the live preview."],
            ["Safest choice for paragraphs and labels."]);

        Add("options.alignCenter", Icons.AlignCenter, "Centre", "",
            "Centres the text line on the click point.",
            ["Centring is useful for badges and titles."],
            ["Place the anchor at the visual centre of the artwork."]);

        Add("options.alignRight", Icons.AlignRight, "Align Right", "",
            "Anchors the text line's right edge at the click point.",
            ["Right alignment grows text leftward from a fixed point."],
            ["Handy for captions that run toward a margin."]);

        Add("options.addFont", Icons.Plus, "Add Font", "",
            "Downloads a font family from cdnfonts and makes it available to the text tool.",
            [
                "Paste a cdnfonts URL or a family name into the row and press Add.",
                "The font is added to the picker for this session.",
            ],
            ["Use a direct cdnfonts link when you know the family you want."]);

        Add("options.fx", Icons.Sparkles, "Text Effects", "",
            "Opens the outline, shadow, glow and preset panel for the text tool.",
            ["Presets set both effects at once; the sliders refine them."],
            ["Set shadow X and Y to 0 and raise Blur for a glow."]);

        Add("options.place", Icons.Text, "Place Text", "Enter",
            "Bakes the live text preview into the active layer.",
            ["Place ends the preview and clears the text field as one undo step."],
            ["Place text on its own layer so it stays easy to move."]);

        Add("options.cancel", Icons.Close, "Cancel Text", "",
            "Discards the live text preview and restores the layer.",
            ["Cancel restores the pixels captured before the preview started."],
            ["Escape is a quick way to abandon a text experiment."]);

        Add("options.selectAll", Icons.Marquee, "Select All", "",
            "Selects the entire document.",
            ["Every editing operation is then clipped to the whole canvas."],
            ["A full selection plus Fill changes the background quickly."]);

        Add("options.deselect", Icons.Close, "Deselect", "Esc",
            "Clears the active selection.",
            ["Without a selection, painting and effects can reach the whole active layer."],
            ["Press Esc from anywhere."]);
    }

    /// <summary>Gets the help article for an id, or a generic fallback.</summary>
    public static HelpTopic Find(string id)
    {
        if (!string.IsNullOrEmpty(id) && Topics.TryGetValue(id, out HelpTopic? topic))
        {
            return topic;
        }

        return new HelpTopic(
            Icons.Info,
            "Help",
            string.Empty,
            "This control belongs to the current tool or panel.",
            ["Use the surrounding options to change how the active tool behaves."],
            ["Hover other controls to see their names, then open the chevron for details."]);
    }

    private static void Add(
        string id,
        string icon,
        string title,
        string shortcut,
        string summary,
        string[] howItWorks,
        string[] tips) =>
        Topics[id] = new HelpTopic(icon, title, shortcut, summary, howItWorks, tips);
}
