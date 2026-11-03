using System.Text.Json;
using System.Text.Json.Nodes;
using Zafiro.FigReader.Core.Geometry;
using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Model;
using Zafiro.FigReader.Core.Resolution;

namespace Zafiro.FigReader.Core.Extraction;

/// <summary>
/// High-level facade over <see cref="FigFile"/> / <see cref="FigmaDocument"/> used by the MCP tools.
/// Decoded documents are cached by absolute path so the (potentially multi-second) decode happens once.
/// Each cache entry remembers the file's size and last write time, so a file overwritten at the same
/// path is decoded again instead of serving the stale document.
/// The most recently loaded document becomes the implicit target for tools that omit a path.
/// </summary>
public sealed class FigmaService
{
    private readonly Dictionary<string, CachedDocument> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private string? _currentPath;

    public FigmaDocument Load(string path, bool forceReload = false)
    {
        var full = Path.GetFullPath(ExpandHome(path));
        lock (_gate)
        {
            var stamp = FileStamp.Of(full);
            if (forceReload || !_cache.TryGetValue(full, out var cached) || cached.Stamp != stamp)
            {
                cached = new CachedDocument(FigmaDocument.Build(FigFile.Load(full)), stamp);
                _cache[full] = cached;
            }

            _currentPath = full;
            return cached.Document;
        }
    }

    public FigmaDocument Resolve(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Load(path);
        }

        string? current;
        lock (_gate)
        {
            current = _currentPath;
        }

        // Revalidate the implicit target too, so a re-exported file is picked up without a new load_file.
        return current is not null
            ? Load(current)
            : throw new InvalidOperationException("No Figma file loaded. Call 'load_file' first or pass a 'path'.");
    }

    public JsonObject Summary(FigmaDocument doc)
    {
        var typeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in doc.AllNodes)
        {
            var t = n.Type ?? "UNKNOWN";
            typeCounts[t] = typeCounts.GetValueOrDefault(t) + 1;
        }

        var typeJson = new JsonObject();
        foreach (var (t, c) in typeCounts.OrderByDescending(kv => kv.Value))
        {
            typeJson[t] = c;
        }

        var pages = new JsonArray();
        foreach (var page in doc.Pages)
        {
            pages.Add(new JsonObject
            {
                ["id"] = page.Id,
                ["name"] = page.Name,
                ["childCount"] = page.Children.Count,
            });
        }

        return new JsonObject
        {
            ["path"] = doc.File.Path,
            ["fileName"] = FileName(doc),
            ["isArchive"] = doc.File.IsArchive,
            ["nodeCount"] = doc.NodeCount,
            ["pageCount"] = doc.Pages.Count,
            ["blobCount"] = doc.File.BlobEntries.Count,
            ["nodeTypeCounts"] = typeJson,
            ["pages"] = pages,
        };
    }

    public JsonNode Metadata(FigmaDocument doc)
    {
        var result = new JsonObject
        {
            ["fileName"] = FileName(doc),
            ["nodeCount"] = doc.NodeCount,
            ["pageCount"] = doc.Pages.Count,
        };

        if (doc.File.MetaJson is { } meta)
        {
            try
            {
                result["meta"] = JsonNode.Parse(meta);
            }
            catch (JsonException)
            {
                result["meta"] = meta;
            }
        }

        return result;
    }

    public JsonNode NodeTree(FigmaDocument doc, string? nodeId, int depth)
    {
        var resolver = new InstanceResolver(doc);

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            var node = doc.FindById(nodeId!)
                ?? throw new KeyNotFoundException($"Node '{nodeId}' not found.");
            return FigmaExtractor.Simplify(node, depth, doc.Variables, includeChildren: true, resolver);
        }

        var roots = doc.Pages.Count > 0
            ? doc.Pages
            : (doc.Root is { } r ? new[] { r } : Array.Empty<FigmaNode>());

        var array = new JsonArray();
        foreach (var root in roots)
        {
            array.Add(FigmaExtractor.Simplify(root, depth, doc.Variables, includeChildren: true, resolver));
        }

        return array;
    }

    public JsonNode NodeDetail(FigmaDocument doc, string nodeId, bool raw)
    {
        var node = doc.FindById(nodeId)
            ?? throw new KeyNotFoundException($"Node '{nodeId}' not found.");

        if (raw)
        {
            return KiwiJson.ToJson(node.Raw, new KiwiJsonOptions { MaxDepth = 20, MaxArrayLength = 200 })
                   ?? new JsonObject();
        }

        var json = FigmaExtractor.Simplify(node, depth: 0, doc.Variables, includeChildren: false);
        AddContext(json, node);
        if (node.ParentId is not null)
        {
            json["parentId"] = node.ParentId;
        }

        // Instances render a symbol, not direct children: expose the resolved child list so callers can
        // read the effective labels, icons (swaps) and visibility without a separate get_node_tree call.
        if (node.Type == "INSTANCE" && new InstanceResolver(doc).Resolve(node, maxDepth: 2) is { } resolved && resolved.Children.Count > 0)
        {
            var children = new JsonArray();
            foreach (var child in resolved.Children)
            {
                children.Add(ResolvedSummary(child));
            }

            json["resolvedChildren"] = children;
        }
        else if (node.Children.Count > 0)
        {
            var children = new JsonArray();
            foreach (var child in node.Children)
            {
                children.Add(new JsonObject { ["id"] = child.Id, ["name"] = child.Name, ["type"] = child.Type });
            }

            json["children"] = children;
        }

        return json;
    }

    private static JsonObject ResolvedSummary(ResolvedNode node)
    {
        var json = new JsonObject { ["id"] = node.Source.Id, ["name"] = node.Name, ["type"] = node.Type };
        if (!node.Visible)
        {
            json["visible"] = false;
        }

        if (!string.IsNullOrEmpty(node.Text))
        {
            json["text"] = node.Text;
        }

        if (node.SwapComponentName is not null)
        {
            json["swappedTo"] = node.SwapComponentName;
        }

        if (node.UnresolvedSwapId is not null)
        {
            json["unresolvedSwap"] = node.UnresolvedSwapId;
        }

        if (node.Children.Count > 0)
        {
            json["childCount"] = node.Children.Count;
        }

        return json;
    }

    /// <summary>
    /// Decodes a node's vector geometry into SVG path(s). Returns each path's SVG data (with the
    /// <c>F0</c>/<c>F1</c> fill-rule prefix Avalonia understands) and its local bounds.
    /// </summary>
    public JsonObject Vector(FigmaDocument doc, string nodeId, string geometryField)
    {
        var node = doc.FindById(nodeId)
            ?? throw new KeyNotFoundException($"Node '{nodeId}' not found.");

        var paths = VectorPathDecoder.Decode(doc.File, node, geometryField);
        var array = new JsonArray();
        foreach (var path in paths)
        {
            array.Add(new JsonObject
            {
                ["fillRule"] = path.FillRule,
                ["bounds"] = new JsonObject
                {
                    ["x"] = Math.Round(path.MinX, 3),
                    ["y"] = Math.Round(path.MinY, 3),
                    ["width"] = Math.Round(path.MaxX - path.MinX, 3),
                    ["height"] = Math.Round(path.MaxY - path.MinY, 3),
                },
                ["path"] = path.Svg,
            });
        }

        return new JsonObject
        {
            ["id"] = node.Id,
            ["name"] = node.Name,
            ["type"] = node.Type,
            ["geometry"] = geometryField,
            ["paths"] = array,
        };
    }

    public JsonArray Text(FigmaDocument doc, string? nodeId)
    {
        var scope = ScopeNodes(doc, nodeId);
        var array = new JsonArray();
        foreach (var node in scope.Where(n => n.Type == "TEXT"))
        {
            var characters = node.Raw.GetObject("textData")?.GetString("characters");
            if (string.IsNullOrEmpty(characters))
            {
                continue;
            }

            var entry = new JsonObject
            {
                ["id"] = node.Id,
                ["name"] = node.Name,
                ["characters"] = characters,
            };

            if (FigmaExtractor.SimplifyText(node, doc.Variables) is { } style)
            {
                style.Remove("characters");
                entry["style"] = style;
            }

            array.Add(entry);
        }

        return array;
    }

    public JsonObject Styles(FigmaDocument doc)
    {
        var colorCounts = new Dictionary<string, ColorUsage>(StringComparer.Ordinal);
        var textStyles = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (var node in doc.AllNodes)
        {
            CountColors(node.Raw.GetList("fillPaints"), doc.Variables, colorCounts);
            CountColors(node.Raw.GetList("strokePaints"), doc.Variables, colorCounts);

            if (node.Type == "TEXT" && FigmaExtractor.SimplifyText(node, doc.Variables) is { } text)
            {
                var family = text["fontFamily"]?.GetValue<string>();
                var fstyle = text["fontStyle"]?.GetValue<string>();
                var size = text["fontSize"]?.GetValue<double>();
                var key = $"{family}|{fstyle}|{size}";
                if (!textStyles.ContainsKey(key))
                {
                    textStyles[key] = new JsonObject
                    {
                        ["fontFamily"] = family,
                        ["fontStyle"] = fstyle,
                        ["fontSize"] = size,
                    };
                }
            }
        }

        var colors = new JsonArray();
        foreach (var (hex, usage) in colorCounts.OrderByDescending(kv => kv.Value.Usages))
        {
            var entry = new JsonObject { ["color"] = hex, ["usages"] = usage.Usages };
            if (usage.UnresolvedVariableUsages > 0)
            {
                // These usages are bound to variables that could not be resolved: the stored literal may be stale.
                entry["unresolvedVariableUsages"] = usage.UnresolvedVariableUsages;
            }

            colors.Add(entry);
        }

        var styles = new JsonArray();
        foreach (var style in textStyles.Values)
        {
            styles.Add(style);
        }

        return new JsonObject { ["colors"] = colors, ["textStyles"] = styles };
    }

    public JsonArray ListImages(FigmaDocument doc)
    {
        var array = new JsonArray();
        foreach (var entry in doc.File.BlobEntries)
        {
            array.Add(new JsonObject
            {
                ["entry"] = entry,
                ["kind"] = entry.StartsWith("videos/", StringComparison.Ordinal) ? "video" : "image",
            });
        }

        return array;
    }

    public JsonObject ExportImage(FigmaDocument doc, string entry, string outputDirectory)
    {
        var name = entry.Contains('/') ? entry : $"images/{entry}";
        var bytes = doc.File.ReadBlob(name)
            ?? throw new KeyNotFoundException($"Blob '{entry}' not found in archive.");

        var dir = Path.GetFullPath(ExpandHome(outputDirectory));
        Directory.CreateDirectory(dir);
        var fileName = name.Replace('/', '_');
        var outPath = Path.Combine(dir, fileName);
        File.WriteAllBytes(outPath, bytes);

        return new JsonObject { ["entry"] = name, ["bytes"] = bytes.Length, ["path"] = outPath };
    }

    public JsonArray Search(FigmaDocument doc, string query, string? type, int limit, string? nodeId = null)
    {
        var array = new JsonArray();
        foreach (var node in ScopeNodes(doc, nodeId))
        {
            if (type is not null && !string.Equals(node.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var matchesName = string.IsNullOrEmpty(query) ||
                              (node.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
            var matchedText = matchesName || string.IsNullOrEmpty(query)
                ? null
                : FindMatchingText(node.Raw, query);

            if (matchesName || matchedText is not null)
            {
                var json = new JsonObject { ["id"] = node.Id, ["name"] = node.Name, ["type"] = node.Type };
                FigmaExtractor.AddBounds(json, node.Raw);
                AddContext(json, node);
                if (matchedText is not null)
                {
                    json["matchedText"] = matchedText;
                }

                array.Add(json);
                if (array.Count >= limit)
                {
                    break;
                }
            }
        }

        return array;
    }

    private static void AddContext(JsonObject json, FigmaNode node)
    {
        if (FindPage(node) is { } page)
        {
            json["pageId"] = page.Id;
            json["pageName"] = page.Name;
        }

        json["path"] = BuildPath(node);
    }

    private static FigmaNode? FindPage(FigmaNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current.Type == "CANVAS")
            {
                return current;
            }
        }

        return null;
    }

    private static string BuildPath(FigmaNode node)
    {
        var names = new Stack<string>();
        for (var current = node; current is not null; current = current.Parent)
        {
            if (!string.IsNullOrWhiteSpace(current.Name))
            {
                names.Push(current.Name!);
            }
        }

        return string.Join(" > ", names);
    }

    private static string? FindMatchingText(KiwiObject root, string query)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in EnumerateTextContent(root))
        {
            if (seen.Add(text) && text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTextContent(object? value)
    {
        switch (value)
        {
            case KiwiObject obj:
                foreach (var (key, fieldValue) in obj.Fields)
                {
                    if (key == "characters" && fieldValue is string { Length: > 0 } characters)
                    {
                        yield return characters;
                    }

                    foreach (var text in EnumerateTextContent(fieldValue))
                    {
                        yield return text;
                    }
                }

                break;
            case IReadOnlyList<object?> list:
                foreach (var item in list)
                {
                    foreach (var text in EnumerateTextContent(item))
                    {
                        yield return text;
                    }
                }

                break;
        }
    }

    private static IEnumerable<FigmaNode> ScopeNodes(FigmaDocument doc, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return doc.AllNodes;
        }

        var root = doc.FindById(nodeId!)
            ?? throw new KeyNotFoundException($"Node '{nodeId}' not found.");
        return new[] { root }.Concat(root.Descendants());
    }

    private static void CountColors(
        IReadOnlyList<object?>? paints,
        VariableResolver variables,
        Dictionary<string, ColorUsage> counts)
    {
        if (paints is null)
        {
            return;
        }

        foreach (var item in paints)
        {
            if (item is KiwiObject paint &&
                paint.GetString("type") == "SOLID" &&
                paint.GetBool("visible") != false &&
                variables.PaintColor(paint) is { } color)
            {
                var hex = FigmaExtractor.ColorToHex(color.Color, paint.GetNumber("opacity"));
                var usage = counts.GetValueOrDefault(hex);
                counts[hex] = new ColorUsage(
                    usage.Usages + 1,
                    usage.UnresolvedVariableUsages + (color.UnresolvedReference is null ? 0 : 1));
            }
        }
    }

    private static string FileName(FigmaDocument doc)
    {
        if (doc.File.MetaJson is { } meta)
        {
            try
            {
                var name = JsonNode.Parse(meta)?["file_name"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        return Path.GetFileNameWithoutExtension(doc.File.Path);
    }

    private static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;

    private readonly record struct ColorUsage(int Usages, int UnresolvedVariableUsages);

    private sealed record CachedDocument(FigmaDocument Document, FileStamp Stamp);

    private readonly record struct FileStamp(long Length, DateTime LastWriteTimeUtc)
    {
        public static FileStamp Of(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc) : default;
        }
    }
}
