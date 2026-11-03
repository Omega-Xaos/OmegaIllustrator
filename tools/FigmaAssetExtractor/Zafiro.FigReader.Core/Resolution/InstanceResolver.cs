using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Model;

namespace Zafiro.FigReader.Core.Resolution;

/// <summary>
/// Expands a Figma INSTANCE into the subtree it actually renders. Instances do not store their
/// children directly; they reference a SYMBOL and carry a set of overrides. This resolver walks the
/// referenced symbol's subtree and applies:
/// <list type="bullet">
/// <item><c>symbolData.symbolOverrides[]</c> keyed by <c>guidPath</c> (text, visibility, size, fills,
///   typography, ...);</item>
/// <item><c>derivedSymbolData[]</c>, the layout Figma computed for the instance (resolved size and
///   position), with explicit overrides taking precedence;</item>
/// <item><c>componentPropAssignments[]</c> providing component-property values for the layers bound to
///   them through <c>componentPropRefs</c>: text (<c>value.textValue</c>) and instance-swaps
///   (<c>value.guidValue</c>, e.g. an icon).</item>
/// </list>
/// A nested instance renders the component assigned to its bound instance-swap property, otherwise the
/// <c>overriddenSymbolID</c> of the override matching its path, otherwise its own component. A swap to the
/// component the layer already uses is not a swap.
/// Nested instances are resolved recursively, with the outer instance's deeper overrides rebased into
/// the nested symbol's coordinate space. Overrides from an outer instance win over the component's own,
/// field by field.
/// </summary>
public sealed class InstanceResolver
{
    private const int MaxDepth = 40;

    // Fields of derivedSymbolData entries that describe effective layout. Other derived fields (geometry
    // blobs, text layout) are rendering caches, not node properties.
    private static readonly string[] DerivedLayoutFields = ["size", "transform"];

    private readonly FigmaDocument _doc;

    public InstanceResolver(FigmaDocument doc) => _doc = doc;

    /// <summary>
    /// Resolves the subtree an INSTANCE renders, expanding at most <paramref name="maxDepth"/> levels of
    /// resolved children (the default expands everything). Returns <see langword="null"/> for
    /// non-instances or when the referenced symbol cannot be found.
    /// </summary>
    public ResolvedNode? Resolve(FigmaNode instance, int maxDepth = int.MaxValue)
    {
        if (instance.Type != "INSTANCE")
        {
            return null;
        }

        var symbolId = SymbolIdOf(instance);
        if (symbolId is null)
        {
            return null;
        }

        return ExpandInstance(
            instance,
            symbolId,
            new Dictionary<string, KiwiObject>(StringComparer.Ordinal),
            ParseAssignments(instance.Raw.GetList("componentPropAssignments")),
            new HashSet<string>(StringComparer.Ordinal),
            0,
            maxDepth);
    }

    /// <summary>
    /// The number of resolved children an INSTANCE would have, computed cheaply (the referenced
    /// symbol's direct child count) without expanding the subtree.
    /// </summary>
    public int ResolvedChildCount(FigmaNode instance)
    {
        var symbolId = SymbolIdOf(instance);
        return symbolId is not null && _doc.FindById(symbolId) is { } symbol ? symbol.Children.Count : 0;
    }

    private ResolvedNode ExpandInstance(
        FigmaNode instance,
        string symbolId,
        IReadOnlyDictionary<string, KiwiObject> inheritedOverrides,
        IReadOnlyDictionary<string, KiwiObject> assignments,
        HashSet<string> visited,
        int depth,
        int remaining)
    {
        var root = new ResolvedNode
        {
            Source = instance,
            Properties = instance.Raw,
            Type = instance.Type,
            Name = instance.Name,
            Visible = instance.Raw.GetBool("visible") != false,
        };

        var symbol = _doc.FindById(symbolId);
        if (symbol is null || remaining <= 0 || depth > MaxDepth || !visited.Add(symbolId))
        {
            return root;
        }

        // The instance's own path-keyed overrides only match its *default* symbol; when swapped
        // (symbolId differs) they no longer apply, so rely on the inherited (outer) overrides only.
        var overrides = symbolId == SymbolIdOf(instance)
            ? MergeOverrides(ParseOverrides(instance), inheritedOverrides)
            : new Dictionary<string, KiwiObject>(inheritedOverrides, StringComparer.Ordinal);

        foreach (var child in symbol.Children)
        {
            root.Children.Add(Build(child, overrides, assignments, visited, depth + 1, remaining - 1));
        }

        visited.Remove(symbolId);
        return root;
    }

    private ResolvedNode Build(
        FigmaNode node,
        IReadOnlyDictionary<string, KiwiObject> overrides,
        IReadOnlyDictionary<string, KiwiObject> assignments,
        HashSet<string> visited,
        int depth,
        int remaining)
    {
        // Within a symbol, Figma keys each override by the target node's own guid; only nested
        // instances add segments (the instance chain). So a node is matched by its id, and frame
        // nesting does not extend the key.
        overrides.TryGetValue(node.Id, out var ov);

        var visible = ov?.GetBool("visible") ?? node.Raw.GetBool("visible") ?? true;
        var resolved = new ResolvedNode
        {
            Source = node,
            Properties = ov is null ? node.Raw : Overlay(node.Raw, ov),
            Type = node.Type,
            Name = node.Name,
            Visible = visible,
        };

        if (node.Type == "TEXT")
        {
            resolved.Text = ov?.GetObject("textData")?.GetString("characters")
                            ?? AssignedValue(node, "TEXT_DATA", assignments)?.GetObject("textValue")?.GetString("characters")
                            ?? node.Raw.GetObject("textData")?.GetString("characters");
        }

        if (node.Type == "INSTANCE")
        {
            var nodeAssignments = Merge(
                ParseAssignments(node.Raw.GetList("componentPropAssignments")),
                ParseAssignments(ov?.GetList("componentPropAssignments")));

            // The enclosing instance's property assignment wins over a swap override: Figma drives a layer
            // bound to an instance-swap property through that property.
            var defaultSymbol = SymbolIdOf(node);
            var swapGuid = AssignedValue(node, "OVERRIDDEN_SYMBOL_ID", assignments)?.GetObject("guidValue")
                           ?? ov?.GetObject("overriddenSymbolID");
            var swapId = swapGuid is null ? null : FigmaDocument.FormatGuid(swapGuid);
            var effectiveSymbol = defaultSymbol;
            if (swapId is not null && swapId != defaultSymbol)
            {
                if (_doc.FindById(swapId) is { } target)
                {
                    resolved.SwapComponentId = swapId;
                    resolved.SwapComponentName = target.Name;
                    effectiveSymbol = swapId;
                }
                else
                {
                    resolved.UnresolvedSwapId = swapId;
                }
            }

            if (remaining > 0)
            {
                var nested = Rebase(overrides, node.Id);
                if (effectiveSymbol is not null && depth <= MaxDepth)
                {
                    var expanded = ExpandInstance(node, effectiveSymbol, nested, nodeAssignments, visited, depth + 1, remaining);
                    resolved.Children.AddRange(expanded.Children);
                }
            }
        }
        else if (remaining > 0)
        {
            foreach (var child in node.Children)
            {
                resolved.Children.Add(Build(child, overrides, assignments, visited, depth + 1, remaining - 1));
            }
        }

        return resolved;
    }

    private static string? SymbolIdOf(FigmaNode instance)
    {
        var guid = instance.Raw.GetObject("symbolData")?.GetObject("symbolID");
        return guid is null ? null : FigmaDocument.FormatGuid(guid);
    }

    private static Dictionary<string, KiwiObject> ParseOverrides(FigmaNode instance)
    {
        var symbolData = instance.Raw.GetObject("symbolData");
        var derived = ParseByGuidPath(instance.Raw.GetList("derivedSymbolData"), DerivedLayoutFields);
        var explicitOverrides = ParseByGuidPath(symbolData?.GetList("symbolOverrides"), fields: null);
        return MergeOverrides(derived, explicitOverrides);
    }

    private static Dictionary<string, KiwiObject> ParseByGuidPath(IReadOnlyList<object?>? entries, string[]? fields)
    {
        var map = new Dictionary<string, KiwiObject>(StringComparer.Ordinal);
        if (entries is null)
        {
            return map;
        }

        foreach (var item in entries)
        {
            if (item is not KiwiObject entry)
            {
                continue;
            }

            var guids = entry.GetObject("guidPath")?.GetList("guids");
            if (guids is null || guids.Count == 0)
            {
                continue;
            }

            var key = string.Join('/', guids.OfType<KiwiObject>().Select(FigmaDocument.FormatGuid));
            var values = entry.Fields.Where(f => f.Key != "guidPath" && (fields is null || fields.Contains(f.Key)));
            // A guidPath is unique per override; last one wins if duplicated.
            map[key] = new KiwiObject(entry.TypeName, values.ToDictionary(f => f.Key, f => f.Value));
        }

        return map;
    }

    private static Dictionary<string, KiwiObject> ParseAssignments(IReadOnlyList<object?>? assignments)
    {
        var map = new Dictionary<string, KiwiObject>(StringComparer.Ordinal);
        if (assignments is null)
        {
            return map;
        }

        foreach (var item in assignments)
        {
            if (item is not KiwiObject assignment)
            {
                continue;
            }

            var defId = assignment.GetObject("defID");
            var value = assignment.GetObject("value");
            if (defId is null || value is null)
            {
                continue;
            }

            map[FigmaDocument.FormatGuid(defId)] = value;
        }

        return map;
    }

    /// <summary>
    /// The value the enclosing instance assigns to the component property that <paramref name="node"/>'s
    /// <paramref name="field"/> is bound to (<c>TEXT_DATA</c>, <c>OVERRIDDEN_SYMBOL_ID</c>, ...), if any.
    /// </summary>
    private static KiwiObject? AssignedValue(
        FigmaNode node,
        string field,
        IReadOnlyDictionary<string, KiwiObject> assignments)
    {
        var refs = node.Raw.GetList("componentPropRefs");
        if (refs is null)
        {
            return null;
        }

        foreach (var item in refs)
        {
            if (item is not KiwiObject reference)
            {
                continue;
            }

            if (reference.GetString("componentPropNodeField") != field)
            {
                continue;
            }

            var defId = reference.GetObject("defID");
            if (defId is null)
            {
                continue;
            }

            if (assignments.TryGetValue(FigmaDocument.FormatGuid(defId), out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static Dictionary<string, KiwiObject> Rebase(
        IReadOnlyDictionary<string, KiwiObject> overrides,
        string instanceId)
    {
        var prefix = instanceId + "/";
        var map = new Dictionary<string, KiwiObject>(StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                map[key[prefix.Length..]] = value;
            }
        }

        return map;
    }

    private static Dictionary<string, KiwiObject> Merge(
        IReadOnlyDictionary<string, KiwiObject> own,
        IReadOnlyDictionary<string, KiwiObject> winning)
    {
        var map = new Dictionary<string, KiwiObject>(own, StringComparer.Ordinal);
        foreach (var (key, value) in winning)
        {
            map[key] = value;
        }

        return map;
    }

    /// <summary>Merges path-keyed overrides field by field; <paramref name="winning"/> fields take precedence.</summary>
    private static Dictionary<string, KiwiObject> MergeOverrides(
        IReadOnlyDictionary<string, KiwiObject> own,
        IReadOnlyDictionary<string, KiwiObject> winning)
    {
        var map = new Dictionary<string, KiwiObject>(own, StringComparer.Ordinal);
        foreach (var (key, value) in winning)
        {
            map[key] = map.TryGetValue(key, out var existing) ? Overlay(existing, value) : value;
        }

        return map;
    }

    /// <summary>Returns <paramref name="target"/> with every field of <paramref name="overlay"/> replacing its own.</summary>
    private static KiwiObject Overlay(KiwiObject target, KiwiObject overlay)
    {
        var fields = new Dictionary<string, object?>(target.Fields, StringComparer.Ordinal);
        foreach (var (name, value) in overlay.Fields)
        {
            fields[name] = value;
        }

        return new KiwiObject(target.TypeName, fields);
    }
}
