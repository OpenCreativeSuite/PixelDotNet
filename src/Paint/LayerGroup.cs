namespace PixelDotNet.Paint;

/// <summary>
/// A folder in the layer stack. Groups own a contiguous run of layers and can
/// be hidden or faded as a unit.
/// </summary>
/// <remarks>
/// Groups are deliberately one level deep: a layer belongs to at most one group.
/// That keeps the compositor and the flat <see cref="PaintDocument.Layers"/>
/// order simple while covering the main organising workflow.
/// </remarks>
public sealed class LayerGroup
{
    /// <summary>Creates an empty group.</summary>
    /// <param name="name">Name shown in the layer panel.</param>
    public LayerGroup(string name)
    {
        Name = name;
    }

    /// <summary>Gets or sets the group name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets whether the group contributes to the image.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Gets or sets the group opacity in the 0-1 range.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Gets or sets whether the group's layers are shown in the panel.</summary>
    public bool Expanded { get; set; } = true;
}
