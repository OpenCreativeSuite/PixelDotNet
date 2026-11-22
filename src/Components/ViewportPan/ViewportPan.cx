using System;
using Donkeh.LayoutLab;
using Donkeh.UILab;
using Donkeh.Windowing;

namespace PixelDotNet.Components;

/// <summary>Middle-button panning for the document viewport.</summary>
/// <remarks>
/// <para>
/// The framework exposes button state, the window-space pointer position and
/// the viewport's scroll offsets; the gesture itself belongs to the editor, so
/// this service owns the drag state.
/// </para>
/// <para>
/// Deltas are measured in window space, never in the coordinates handed to a
/// pointer handler. Those are local to the painted box, and a pan scrolls that
/// box, so local deltas feed the scroll back into itself and the canvas shakes.
/// </para>
/// </remarks>
internal static class ViewportPan
{
    private static Ref<ElementNode>? _viewport;
    private static bool _panning;
    private static int _pressSequence;
    private static float _lastX;
    private static float _lastY;

    /// <summary>Points the pan gesture at the current viewport element.</summary>
    public static void Attach(Ref<ElementNode> viewport) => _viewport = viewport;

    /// <summary>
    /// Handles a pointer move. Returns true while the middle button owns the
    /// gesture, so callers can skip painting and other pointer work.
    /// </summary>
    public static bool Handle()
    {
        // No move is dispatched while the button is up, so the end of a gesture
        // is only observable as the next press. Without this the drag origin
        // survives into the new gesture and the view jumps on the first move.
        int pressSequence = InputState.PressSequence;

        if (pressSequence != _pressSequence)
        {
            _pressSequence = pressSequence;
            _panning = false;
        }

        if (!InputState.Middle)
        {
            _panning = false;
            InputState.CursorOverride = null;
            return false;
        }

        float x = InputState.PointerX;
        float y = InputState.PointerY;

        // The framework maps grab/grabbing onto the only hand shape backends
        // expose, so the cursor reads as a held hand for the whole gesture.
        InputState.CursorOverride = Cursor.Grabbing;

        if (!_panning)
        {
            _panning = true;
            _lastX = x;
            _lastY = y;
            return true;
        }

        float dx = x - _lastX;
        float dy = y - _lastY;
        _lastX = x;
        _lastY = y;

        if ((dx == 0f && dy == 0f) || _viewport?.Value is not { } viewport)
        {
            return true;
        }

        float maxX = MathF.Max(0f, viewport.ScrollWidth - viewport.ClientWidth);
        float maxY = MathF.Max(0f, viewport.ScrollHeight - viewport.ClientHeight);

        viewport.ScrollOffsetX = Math.Clamp(viewport.ScrollOffsetX - dx, 0f, maxX);
        viewport.ScrollOffsetY = Math.Clamp(viewport.ScrollOffsetY - dy, 0f, maxY);

        // Repaint only: the painter applies scroll offsets from the node and the
        // layout is still valid, so a full re-layout here would stutter.
        AppRootHost.Current?.RequestRepaint();
        return true;
    }
}
