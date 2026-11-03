using Zafiro.FigReader.Core.Kiwi;

namespace Zafiro.FigReader.Core.Model;

/// <summary>
/// A single Figma node, wrapping its raw <see cref="KiwiObject"/> (<c>NodeChange</c>) and exposing
/// the structural fields needed to navigate the document. Property extraction (fills, text, layout…)
/// is done lazily from <see cref="Raw"/>.
/// </summary>
public sealed class FigmaNode
{
    private readonly List<FigmaNode> _children = new();

    public FigmaNode(string id, KiwiObject raw)
    {
        Id = id;
        Raw = raw;
    }

    /// <summary>Stable id formatted as <c>sessionID:localID</c>.</summary>
    public string Id { get; }

    public KiwiObject Raw { get; }

    public string? Name => Raw.GetString("name");

    public string? Type => Raw.GetString("type");

    public string? ParentId { get; internal set; }

    /// <summary>Fractional-index ordering key relative to siblings (Figma stores this as a string).</summary>
    public string Position { get; internal set; } = string.Empty;

    public FigmaNode? Parent { get; internal set; }

    public IReadOnlyList<FigmaNode> Children => _children;

    internal void AddChild(FigmaNode child) => _children.Add(child);

    internal void SortChildren() =>
        _children.Sort(static (a, b) => string.CompareOrdinal(a.Position, b.Position));

    public IEnumerable<FigmaNode> Descendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            foreach (var d in child.Descendants())
            {
                yield return d;
            }
        }
    }
}
