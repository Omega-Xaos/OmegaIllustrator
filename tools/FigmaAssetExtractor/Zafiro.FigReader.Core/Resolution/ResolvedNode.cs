using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Model;

namespace Zafiro.FigReader.Core.Resolution;

/// <summary>
/// A node of an instance's <em>resolved</em> subtree: the symbol content an INSTANCE renders once its
/// overrides and component-property assignments have been applied. Unlike a raw <see cref="FigmaNode"/>,
/// it carries the effective <see cref="Properties"/> (bounds, fills, typography, ...), the <see cref="Text"/>
/// actually shown, the effective <see cref="Visible"/> flag and, for nested instances, the swapped component
/// (<see cref="SwapComponentName"/>).
/// </summary>
public sealed class ResolvedNode
{
    /// <summary>The underlying symbol-space node (identity, name and structure).</summary>
    public required FigmaNode Source { get; init; }

    /// <summary>
    /// The node's effective fields: <see cref="Source"/>'s raw fields with the instance's derived layout and
    /// explicit overrides applied. Positional bounds are in the symbol's local space.
    /// </summary>
    public required KiwiObject Properties { get; init; }

    /// <summary>Node type (FRAME, TEXT, INSTANCE, VECTOR, ...), copied from <see cref="Source"/>.</summary>
    public required string? Type { get; init; }

    /// <summary>Node name, copied from <see cref="Source"/>.</summary>
    public string? Name { get; init; }

    /// <summary>The effective visibility after overrides (<see langword="false"/> hides the node).</summary>
    public bool Visible { get; init; } = true;

    /// <summary>For TEXT nodes, the effective characters after overrides / text component properties.</summary>
    public string? Text { get; set; }

    /// <summary>For INSTANCE nodes that are instance-swapped, the id of the swapped component.</summary>
    public string? SwapComponentId { get; set; }

    /// <summary>For INSTANCE nodes that are instance-swapped, the name of the swapped component.</summary>
    public string? SwapComponentName { get; set; }

    /// <summary>
    /// For INSTANCE nodes swapped to a component missing from the file, the id of that component. The node is
    /// then expanded with its original component.
    /// </summary>
    public string? UnresolvedSwapId { get; set; }

    /// <summary>Resolved children.</summary>
    public List<ResolvedNode> Children { get; } = new();
}
