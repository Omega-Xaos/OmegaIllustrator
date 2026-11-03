using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Resolution;

namespace Zafiro.FigReader.Core.Model;

/// <summary>
/// A navigable Figma document built from the flat <c>nodeChanges</c> list of a decoded
/// <see cref="FigFile"/>. Figma stores nodes as a flat CRDT log; each node references its parent
/// by GUID plus a fractional <c>position</c> used to order siblings. This class rebuilds the tree.
/// </summary>
public sealed class FigmaDocument
{
    private readonly Dictionary<string, FigmaNode> _byId;
    private readonly Lazy<VariableResolver> _variables;

    private FigmaDocument(FigFile file, Dictionary<string, FigmaNode> byId, FigmaNode? root, IReadOnlyList<FigmaNode> pages)
    {
        File = file;
        _byId = byId;
        Root = root;
        Pages = pages;
        _variables = new Lazy<VariableResolver>(() => new VariableResolver(this));
    }

    public FigFile File { get; }

    /// <summary>The DOCUMENT node, or null if not present.</summary>
    public FigmaNode? Root { get; }

    /// <summary>Top-level CANVAS nodes (pages).</summary>
    public IReadOnlyList<FigmaNode> Pages { get; }

    public IReadOnlyCollection<FigmaNode> AllNodes => _byId.Values;

    public int NodeCount => _byId.Count;

    public FigmaNode? FindById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Resolves variable bindings (e.g. bound paint colors) against this document's variables.</summary>
    public VariableResolver Variables => _variables.Value;

    public static FigmaDocument Build(FigFile file)
    {
        var nodeChanges = file.Message.GetList("nodeChanges") ?? new List<object?>();
        var byId = new Dictionary<string, FigmaNode>(nodeChanges.Count);

        foreach (var item in nodeChanges)
        {
            if (item is not KiwiObject node)
            {
                continue;
            }

            var guid = node.GetObject("guid");
            if (guid is null)
            {
                continue;
            }

            var id = FormatGuid(guid);
            // Later changes for the same guid win (last write wins, matching the CRDT log order).
            byId[id] = new FigmaNode(id, node);
        }

        // Link parents/children.
        foreach (var node in byId.Values)
        {
            var parentIndex = node.Raw.GetObject("parentIndex");
            var parentGuid = parentIndex?.GetObject("guid");
            if (parentGuid is null)
            {
                continue;
            }

            node.ParentId = FormatGuid(parentGuid);
            node.Position = parentIndex!.GetString("position") ?? string.Empty;
            if (byId.TryGetValue(node.ParentId, out var parent))
            {
                node.Parent = parent;
                parent.AddChild(node);
            }
        }

        foreach (var node in byId.Values)
        {
            node.SortChildren();
        }

        var root = byId.Values.FirstOrDefault(n => n.Type == "DOCUMENT")
                   ?? byId.Values.FirstOrDefault(n => n.Parent is null);

        var pages = (root?.Children ?? (IReadOnlyList<FigmaNode>)Array.Empty<FigmaNode>())
            .Where(n => n.Type == "CANVAS")
            .ToList();

        return new FigmaDocument(file, byId, root, pages);
    }

    public static string FormatGuid(KiwiObject guid)
    {
        var session = guid.GetNumber("sessionID") ?? 0;
        var local = guid.GetNumber("localID") ?? 0;
        return $"{(long)session}:{(long)local}";
    }
}
