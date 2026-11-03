using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Model;

namespace Zafiro.FigReader.Core.Resolution;

/// <summary>
/// Resolves Figma variable bindings to concrete values. A bound paint stores a cached literal
/// <c>color</c> plus a <c>colorVar</c> whose value aliases a VARIABLE node; that variable may alias
/// further variables before reaching a concrete <c>colorValue</c>. Variables are found by guid or, for
/// library variables, by their <c>assetRef.key</c>. Each variable is read in the default mode of its
/// collection (the first mode by sort position); explicit per-node mode selection is not modelled.
/// </summary>
public sealed class VariableResolver
{
    private const int MaxAliasDepth = 32;

    private readonly FigmaDocument _doc;
    private readonly Dictionary<string, FigmaNode> _byKey = new(StringComparer.Ordinal);

    public VariableResolver(FigmaDocument doc)
    {
        _doc = doc;
        foreach (var node in doc.AllNodes)
        {
            if (node.Type is "VARIABLE" or "VARIABLE_SET" && node.Raw.GetString("key") is { Length: > 0 } key)
            {
                _byKey[key] = node;
            }
        }
    }

    /// <summary>
    /// The effective color of a paint: its bound variable's value when the binding resolves, otherwise the
    /// stored literal flagged with the reference that could not be resolved. Returns <see langword="null"/>
    /// for paints without a color (gradients, images).
    /// </summary>
    public PaintColor? PaintColor(KiwiObject paint)
    {
        var literal = paint.GetObject("color");
        if (paint.GetObject("colorVar") is not { } binding)
        {
            return literal is null ? null : new PaintColor(literal, UnresolvedReference: null);
        }

        if (ResolveColor(binding, 0) is { } resolved)
        {
            return new PaintColor(resolved, UnresolvedReference: null);
        }

        return literal is null ? null : new PaintColor(literal, Reference(binding) ?? "unknown");
    }

    private KiwiObject? ResolveColor(KiwiObject variableData, int depth)
    {
        var value = variableData.GetObject("value");
        if (value is null || depth > MaxAliasDepth)
        {
            return null;
        }

        if (value.GetObject("colorValue") is { } color)
        {
            return color;
        }

        var variable = value.GetObject("alias") is { } alias ? FindNode(alias) : null;
        return variable?.Type == "VARIABLE" && DefaultValue(variable) is { } next
            ? ResolveColor(next, depth + 1)
            : null;
    }

    private KiwiObject? DefaultValue(FigmaNode variable)
    {
        var entries = variable.Raw.GetObject("variableDataValues")?.GetList("entries")?.OfType<KiwiObject>().ToList();
        if (entries is null || entries.Count == 0)
        {
            return null;
        }

        var modeId = DefaultModeId(variable);
        var entry = entries.FirstOrDefault(e => e.GetObject("modeID") is { } id && FigmaDocument.FormatGuid(id) == modeId)
                    ?? entries[0];
        return entry.GetObject("variableData");
    }

    private string? DefaultModeId(FigmaNode variable)
    {
        var set = variable.Raw.GetObject("variableSetID") is { } reference ? FindNode(reference) : null;
        var mode = set?.Raw.GetList("variableSetModes")?
            .OfType<KiwiObject>()
            .OrderBy(m => m.GetString("sortPosition") ?? string.Empty, StringComparer.Ordinal)
            .FirstOrDefault();
        return mode?.GetObject("id") is { } id ? FigmaDocument.FormatGuid(id) : null;
    }

    /// <summary>Finds the node a <c>{ guid }</c> or <c>{ assetRef: { key } }</c> reference points at.</summary>
    private FigmaNode? FindNode(KiwiObject reference)
    {
        if (reference.GetObject("guid") is { } guid)
        {
            return _doc.FindById(FigmaDocument.FormatGuid(guid));
        }

        return reference.GetObject("assetRef")?.GetString("key") is { } key ? _byKey.GetValueOrDefault(key) : null;
    }

    private static string? Reference(KiwiObject binding)
    {
        var alias = binding.GetObject("value")?.GetObject("alias");
        if (alias?.GetObject("guid") is { } guid)
        {
            return FigmaDocument.FormatGuid(guid);
        }

        return alias?.GetObject("assetRef")?.GetString("key");
    }
}

/// <summary>
/// An effective paint color. <see cref="UnresolvedReference"/> is set when the paint is bound to a variable
/// that could not be resolved, in which case <see cref="Color"/> is only the stored (possibly stale) literal.
/// </summary>
public sealed record PaintColor(KiwiObject Color, string? UnresolvedReference);
