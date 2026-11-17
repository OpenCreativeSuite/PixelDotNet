using Donkeh.UILab;

namespace PixelDotNet.Paint;

/// <summary>
/// Provides the live <see cref="PaintSession"/> to every component below the app
/// root, so panels never have to thread the model through props.
/// </summary>
public static class EditorContext
{
    /// <summary>The session context. The default keeps components usable outside a provider.</summary>
    public static readonly Context<PaintSession> Value = new(PaintSession.CreateDemo());
}
