using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using System.Xml.Linq;
using Zafiro.FigReader.Core;
using Zafiro.FigReader.Core.Extraction;
using Zafiro.FigReader.Core.Geometry;
using Zafiro.FigReader.Core.Kiwi;
using Zafiro.FigReader.Core.Model;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: FigmaAssetExtractor <input.fig> <manifest.json> [output-dir]");
    return 2;
}

var figPath = Path.GetFullPath(args[0]);
var manifestPath = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args.Length > 2 ? args[2] : "Assets");
Directory.CreateDirectory(output);
using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
var manifestRoot = manifest.RootElement;
var assets = manifestRoot.GetProperty("assets").EnumerateArray().ToArray();
var palettes = manifestRoot.GetProperty("palettes").EnumerateArray().ToArray();
var examples = manifestRoot.GetProperty("exampleCompositions").EnumerateArray().ToArray();
var file = FigFile.Load(figPath);
var doc = FigmaDocument.Build(file);
var report = new List<object>();
var diagnostics = new JsonArray();
var catalog = new JsonArray();
XNamespace svgNs = "http://www.w3.org/2000/svg";

foreach (var asset in assets)
{
    var id = asset.GetProperty("id").GetString()!;
    var nodeId = asset.GetProperty("figmaNodeId").GetString()!;
    var node = doc.FindById(nodeId);
    if (node is null)
    {
        report.Add(new { id, nodeId, status = "Missing", reason = "Node ID not found" });
        continue;
    }

    var nodes = new[] { node }.Concat(node.Descendants()).ToArray();
    var pathRows = new JsonArray();
    var paintRows = new JsonArray();
    var counts = new Dictionary<string, int>();
    var sourceColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var svgElements = new List<XElement>();
    var groupsByNode = new Dictionary<string, XElement>(StringComparer.Ordinal);
    var limitations = new HashSet<string>(StringComparer.Ordinal);
    var visualFeatureRows = new JsonArray();
    foreach (var child in nodes)
    {
        counts[child.Type ?? "?"] = counts.GetValueOrDefault(child.Type ?? "?") + 1;
        var simplified = FigmaExtractor.Simplify(child, 0, doc.Variables, includeChildren: false);
        var fills = simplified["fills"] as JsonArray;
        var strokes = simplified["strokes"] as JsonArray;
        var rawFills = child.Raw.GetList("fillPaints");
        var rawStrokes = child.Raw.GetList("strokePaints");
        if (rawFills is { Count: > 0 })
            paintRows.Add(new JsonObject { ["nodeId"] = child.Id, ["fillPaints"] = KiwiJson.ToJson(rawFills), ["strokePaints"] = KiwiJson.ToJson(rawStrokes) });
        var fill = fills?.FirstOrDefault()?.GetValue<string>();
        var stroke = strokes?.FirstOrDefault()?.GetValue<string>();
        foreach (var color in (fills ?? []).Concat(strokes ?? []).Select(value => value?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)))
            sourceColors.Add(color!);
        var fillPaintTypes = (rawFills ?? []).OfType<KiwiObject>().Select(paint => paint.GetString("type") ?? "UNKNOWN").ToArray();
        var strokePaintTypes = (rawStrokes ?? []).OfType<KiwiObject>().Select(paint => paint.GetString("type") ?? "UNKNOWN").ToArray();
        if (fillPaintTypes.Any(type => type != "SOLID") || strokePaintTypes.Any(type => type != "SOLID"))
            limitations.Add("Non-solid paint needs SVG gradient or image-fill recovery");
        if (fillPaintTypes.Length > 1 || strokePaintTypes.Length > 1)
            limitations.Add("Multiple paints on one node need paint-to-geometry mapping");
        var visualProperties = new JsonObject();
        foreach (var propertyName in new[] { "mask", "maskType", "isMask", "clipsContent", "frameMaskDisabled", "effects", "blendMode", "blendModeForStyle", "strokePaints", "gradientHandlePositions", "imageRef" })
        {
            if (!child.Raw.Fields.TryGetValue(propertyName, out var propertyValue) || !HasMeaningfulVisualValue(propertyValue)) continue;
            visualProperties[propertyName] = KiwiJson.ToJson(propertyValue, new KiwiJsonOptions { MaxDepth = 8, MaxArrayLength = 16 });
        }
        if (visualProperties.Count > 0)
            visualFeatureRows.Add(new JsonObject { ["nodeId"] = child.Id, ["nodeName"] = child.Name, ["nodeType"] = child.Type, ["properties"] = visualProperties });
        var rawTransform = ReferenceEquals(child, node) ? null : child.Raw.GetObject("transform");
        var transform = rawTransform is null ? null : $"matrix({N(rawTransform.GetNumber("m00") ?? 1)} {N(rawTransform.GetNumber("m10") ?? 0)} {N(rawTransform.GetNumber("m01") ?? 0)} {N(rawTransform.GetNumber("m11") ?? 1)} {N(rawTransform.GetNumber("m02") ?? 0)} {N(rawTransform.GetNumber("m12") ?? 0)})";
        var group = new XElement(svgNs + "g", new XAttribute("data-node-id", child.Id));
        if (!string.IsNullOrWhiteSpace(transform)) group.SetAttributeValue("transform", transform);
        if (simplified["opacity"] is JsonNode opacity) group.SetAttributeValue("opacity", opacity.ToJsonString());
        if (simplified["visible"]?.GetValue<bool>() == false) group.SetAttributeValue("display", "none");
        foreach (var geometryField in new[] { "fillGeometry", "strokeGeometry" })
        {
            var paths = VectorPathDecoder.Decode(file, child, geometryField);
            foreach (var path in paths)
            {
                pathRows.Add(new JsonObject
                {
                    ["nodeId"] = child.Id, ["nodeName"] = child.Name, ["nodeType"] = child.Type,
                    ["geometry"] = geometryField, ["svgPath"] = path.Svg, ["fillRule"] = path.FillRule,
                    ["nodePaints"] = fills?.DeepClone(),
                    ["strokePaints"] = simplified["strokes"]?.DeepClone(),
                    ["transform"] = simplified["x"] is null ? null : new JsonObject { ["x"] = simplified["x"]!.DeepClone(), ["y"] = simplified["y"]?.DeepClone() },
                    ["bounds"] = new JsonObject { ["x"] = path.MinX, ["y"] = path.MinY,
                        ["width"] = path.MaxX - path.MinX, ["height"] = path.MaxY - path.MinY }
                });
                var d = path.Svg.Length > 3 ? path.Svg[3..].Trim() : path.Svg;
                var p = new XElement(svgNs + "path", new XAttribute("d", d), new XAttribute("fill-rule", path.FillRule == "F0" ? "evenodd" : "nonzero"));
                p.SetAttributeValue("fill", geometryField == "fillGeometry" ? fill ?? "none" : stroke ?? "none");
                group.Add(p);
            }
        }
        groupsByNode[child.Id] = group;
        if (child.Raw.GetList("effects")?.Count > 0) limitations.Add("Effects are not represented by the current decoder exporter");
        if (!ReferenceEquals(child, node) && child.Raw.GetBool("clipsContent") == true)
            limitations.Add("Frame clipping semantics require SVG clip-path recovery");
        if (ReferenceEquals(child, node) && child.Raw.GetBool("frameMaskDisabled") == true)
            limitations.Add("Source frame allows overflow beyond its bounds");
        if (child.Raw.GetBool("isMask") == true || child.Raw.GetBool("mask") == true || child.Raw.GetString("maskType") is { } maskType && maskType is not "NONE" and not "NORMAL")
            limitations.Add("Figma mask semantics require SVG mask recovery");
    }

    foreach (var child in nodes)
    {
        if (!groupsByNode.TryGetValue(child.Id, out var group)) continue;
        if (child.Parent is not null && groupsByNode.TryGetValue(child.Parent.Id, out var parentGroup))
            parentGroup.Add(group);
        else
            svgElements.Add(group);
    }

    var directory = Path.Combine(output, asset.GetProperty("role").GetString() == "main" ? "Main" : "Background", asset.GetProperty("size").GetString()!);
    Directory.CreateDirectory(directory);
    var svgPath = Path.Combine(directory, id + ".svg");
    var width = asset.GetProperty("sourceDimensions").GetProperty("width").GetDouble();
    var height = asset.GetProperty("sourceDimensions").GetProperty("height").GetDouble();
    var svg = new XDocument(new XElement(svgNs + "svg",
        new XAttribute("viewBox", $"0 0 {N(width)} {N(height)}"), new XAttribute("width", N(width)), new XAttribute("height", N(height)), svgElements));
    svg.Save(svgPath);

    diagnostics.Add(new JsonObject
    {
        ["assetId"] = id, ["manifestNodeId"] = nodeId, ["name"] = node.Name,
        ["type"] = node.Type, ["subtreeNodeCount"] = nodes.Length,
        ["sourceFrameTransform"] = KiwiJson.ToJson(node.Raw.GetObject("transform")),
        ["nodeTypes"] = JsonSerializer.SerializeToNode(counts),
        ["rawFieldNames"] = new JsonArray(nodes.SelectMany(item => item.Raw.Fields.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
        ["visualFeatures"] = visualFeatureRows, ["paintData"] = paintRows, ["paths"] = pathRows
    });
    var status = pathRows.Count == 0 ? "Failed" : limitations.Count == 0 ? "Extracted" : "Partial";
    var reason = status switch
    {
        "Extracted" => "Vector geometry and supported paints exported; visual comparison with the source remains pending",
        "Partial" => string.Join("; ", limitations),
        _ => "No vector geometry decoded"
    };
    report.Add(new { id, nodeId, status,
        subtreeNodes = nodes.Length, vectorPaths = pathRows.Count,
        limitations = limitations.ToArray(), svg = Path.GetRelativePath(output, svgPath), reason });
    catalog.Add(new JsonObject
    {
        ["id"] = id,
        ["concept"] = asset.GetProperty("concept").GetString(),
        ["name"] = asset.GetProperty("label").GetString(),
        ["role"] = asset.GetProperty("role").GetString(),
        ["size"] = asset.GetProperty("size").GetString(),
        ["sourceNodeId"] = nodeId,
        ["sourceFrameName"] = asset.GetProperty("sourceFrameName").GetString(),
        ["source"] = manifestRoot.GetProperty("source").GetString(),
        ["viewBox"] = $"0 0 {N(width)} {N(height)}",
        ["tags"] = JsonSerializer.SerializeToNode(asset.GetProperty("tags")),
        ["availablePalettes"] = new JsonArray(palettes.Select(p => (JsonNode?)JsonValue.Create(p.GetProperty("id").GetString())).ToArray()),
        ["sourceColors"] = new JsonArray(sourceColors.Order(StringComparer.OrdinalIgnoreCase).Select(color => (JsonNode?)JsonValue.Create(color)).ToArray()),
        ["colorRoles"] = BuildColorRoles(sourceColors),
        ["svg"] = Path.GetRelativePath(output, svgPath).Replace('\\', '/'),
        ["status"] = status,
        ["vectorPaths"] = pathRows.Count,
        ["reason"] = reason
    });
}

var paletteDirectory = Path.Combine(output, "Palettes");
Directory.CreateDirectory(paletteDirectory);
var paletteCatalog = new JsonArray();
foreach (var palette in palettes)
{
    var entry = new JsonObject
    {
        ["id"] = palette.GetProperty("id").GetString(),
        ["sourceNodeId"] = palette.GetProperty("figmaNodeId").GetString(),
        ["background"] = new JsonObject
        {
            ["bg1"] = palette.GetProperty("backgroundColorRoles")[0].GetString(),
            ["bg2"] = palette.GetProperty("backgroundColorRoles")[1].GetString(),
            ["bg3"] = palette.GetProperty("backgroundColorRoles")[2].GetString()
        },
        ["objectOnly"] = new JsonObject
        {
            ["accent1"] = palette.GetProperty("objectOnlyColorRoles")[0].GetString(),
            ["accent2"] = palette.GetProperty("objectOnlyColorRoles")[1].GetString(),
            ["accent3"] = palette.GetProperty("objectOnlyColorRoles")[2].GetString()
        },
        ["neutral"] = JsonSerializer.SerializeToNode(palette.GetProperty("neutralRoles"))
    };
    paletteCatalog.Add(entry.DeepClone());
    await File.WriteAllTextAsync(Path.Combine(paletteDirectory, $"{entry["id"]!.GetValue<string>()}.json"), entry.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}
await File.WriteAllTextAsync(Path.Combine(paletteDirectory, "catalog.json"), paletteCatalog.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

var conceptByLabel = assets
    .GroupBy(asset => asset.GetProperty("label").GetString()!, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(group => group.Key, group => group.First().GetProperty("concept").GetString()!, StringComparer.OrdinalIgnoreCase);
var exampleCatalog = new JsonArray();
foreach (var example in examples.Select((value, index) => (value, index)))
{
    var nodeId = example.value.GetProperty("figmaNodeId").GetString()!;
    var sourceNode = doc.FindById(nodeId);
    var canvas = sourceNode;
    while (canvas is not null && canvas.Type != "CANVAS") canvas = canvas.Parent;
    var section = sourceNode;
    while (section is not null && section.Type != "SECTION") section = section.Parent;
    var namedElements = example.value.GetProperty("namedElements").EnumerateArray().Select(item => item.GetString()!).ToArray();
    var concepts = namedElements.Where(conceptByLabel.ContainsKey).Select(label => conceptByLabel[label]).Distinct(StringComparer.Ordinal).ToArray();
    var unresolvedElements = namedElements.Where(label => !conceptByLabel.ContainsKey(label)).ToArray();
    var width = example.value.GetProperty("width").GetDouble();
    var height = example.value.GetProperty("height").GetDouble();
    exampleCatalog.Add(new JsonObject
    {
        ["id"] = $"example-{example.index + 1:00}",
        ["sourceNodeId"] = nodeId,
        ["sourceName"] = sourceNode?.Name,
        ["sourcePageId"] = canvas?.Id,
        ["sourcePageName"] = canvas?.Name,
        ["sourceSectionId"] = section?.Id,
        ["sourceSectionName"] = section?.Name,
        ["width"] = width,
        ["height"] = height,
        ["format"] = width == height ? "square" : width > height ? "landscape" : "portrait",
        ["namedElements"] = new JsonArray(namedElements.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
        ["concepts"] = new JsonArray(concepts.Select(concept => (JsonNode?)JsonValue.Create(concept)).ToArray()),
        ["unresolvedElements"] = new JsonArray(unresolvedElements.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
        ["sourceNodeFound"] = sourceNode is not null
    });
}
var exampleDirectory = Path.Combine(output, "Examples");
Directory.CreateDirectory(exampleDirectory);
await File.WriteAllTextAsync(Path.Combine(exampleDirectory, "catalog.json"), exampleCatalog.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

await File.WriteAllTextAsync(Path.Combine(output, "node-diagnostics.json"), diagnostics.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(output, "extraction-report.json"), JsonSerializer.Serialize(new
{
    source = figPath, documentNodeCount = doc.NodeCount, targetCount = assets.Length,
    foundCount = report.Count(x => !JsonSerializer.Serialize(x).Contains("Missing", StringComparison.Ordinal)),
    extractedCount = report.Count(x => JsonSerializer.Serialize(x).Contains("\"status\":\"Extracted\"", StringComparison.Ordinal)),
    partialCount = report.Count(x => JsonSerializer.Serialize(x).Contains("\"status\":\"Partial\"", StringComparison.Ordinal)),
    failedCount = report.Count(x => JsonSerializer.Serialize(x).Contains("\"status\":\"Failed\"", StringComparison.Ordinal)),
    paletteCount = palettes.Length,
    exampleCount = examples.Length,
    metadata = new[] { "Palettes/catalog.json", "Examples/catalog.json" },
    visualVerification = "Pending source-image comparison",
    assets = report
}, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(output, "catalog.json"), catalog.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
var exportedCards = catalog.Count(item => item?["status"]?.GetValue<string>() == "Extracted");
var cards = new System.Text.StringBuilder($"<!doctype html><meta charset=\"utf-8\"><title>Omega Illustrator assets</title><style>body{{font:15px system-ui;background:#eee;margin:24px}}.grid{{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:16px}}.card{{background:white;padding:16px;border-radius:12px}}.preview{{height:220px;background:#fafafa;display:grid;place-items:center}}.preview img{{width:100%;height:100%;object-fit:contain}}small{{color:#555}}</style><h1>Figma asset extraction</h1><p>Exported: {exportedCards} / {assets.Length}. Vector paths are kept editable. Visual comparison with the source remains pending.</p><div class=\"grid\">");
foreach (var item in catalog)
{
    var id = item!["id"]!.GetValue<string>();
    var svg = item["svg"]!.GetValue<string>();
    var nodeId = item["sourceNodeId"]!.GetValue<string>();
    var status = item["status"]!.GetValue<string>();
    var reason = item["reason"]!.GetValue<string>();
    var vectorPaths = item["vectorPaths"]!.GetValue<int>();
    var svgBytes = await File.ReadAllBytesAsync(Path.Combine(output, svg.Replace('/', Path.DirectorySeparatorChar)));
    var embeddedSvg = Convert.ToBase64String(svgBytes);
    cards.Append("<article class=\"card\"><h2>").Append(System.Net.WebUtility.HtmlEncode(id)).Append("</h2><div class=\"preview\"><img alt=\"").Append(System.Net.WebUtility.HtmlEncode(id)).Append(" preview\" src=\"data:image/svg+xml;base64,").Append(embeddedSvg).Append("\"></div><small>Node ").Append(System.Net.WebUtility.HtmlEncode(nodeId)).Append(" · ").Append(System.Net.WebUtility.HtmlEncode(status)).Append(" · ").Append(vectorPaths).Append(" vector paths · ").Append(System.Net.WebUtility.HtmlEncode(reason)).Append("</small></article>");
}
cards.Append("</div>");
await File.WriteAllTextAsync(Path.Combine(output, "index.html"), cards.ToString());
Console.WriteLine($"Decoded {doc.NodeCount} nodes; wrote diagnostics for {report.Count} manifest entries to {output}");
return 0;

static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);

static JsonArray BuildColorRoles(IEnumerable<string> colors)
{
    var knownRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["#D7CDF5"] = "background.bg1", ["#AA9BF0"] = "background.bg2", ["#7864D7"] = "background.bg3",
        ["#F9ECB0"] = "objectOnly.accent1", ["#F9A3AA"] = "objectOnly.accent2", ["#FFE3E1"] = "objectOnly.accent3",
        ["#181817"] = "neutral.outline", ["#FFFFFF"] = "neutral.white"
    };
    return new JsonArray(colors.Order(StringComparer.OrdinalIgnoreCase).Select(color => (JsonNode?)new JsonObject
    {
        ["sourceColor"] = color,
        ["role"] = knownRoles.GetValueOrDefault(color),
        ["behavior"] = knownRoles.ContainsKey(color) ? "palette-role" : "preserve-source-color"
    }).ToArray());
}

static bool HasMeaningfulVisualValue(object? value) => value switch
{
    null => false,
    bool flag => flag,
    string text => !string.IsNullOrWhiteSpace(text) && text is not "NONE" and not "NORMAL",
    System.Collections.ICollection collection => collection.Count > 0,
    _ => true
};
