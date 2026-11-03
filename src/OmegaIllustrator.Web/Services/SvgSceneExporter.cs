using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OmegaIllustrator.Web.Services;

public sealed record SceneSvgItem(string FilePath, double X, double Y, double Width, double Height, double Rotation, string? InstanceId = null, string? ConceptId = null, string? Role = null, IReadOnlyDictionary<string, string>? PaletteMap = null, double Relevance = 0.5, double Importance = 0.5, double Complexity = 0.5);

public static class SvgSceneExporter
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private static readonly Regex CssSelectorReference = new(@"(?<![A-Za-z0-9_-])#(?<id>[A-Za-z_][A-Za-z0-9_.:-]*)(?=\s*(?:\{|\[|[>+~.,:]))", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UrlReference = new("url\\(\\s*(['\"]?)#(?<id>[^)'\"\\s]+)\\1\\s*\\)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Export(double width, double height, string background, string palette, IEnumerable<SceneSvgItem> items)
        => Export(width, height, background, PaletteMap(palette), items);

    public static string Export(double width, double height, string background, IReadOnlyDictionary<string, string> paletteMap, IEnumerable<SceneSvgItem> items)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var root = new XElement(Svg + "svg", new XAttribute("viewBox", $"0 0 {F(width)} {F(height)}"), new XAttribute("width", F(width)), new XAttribute("height", F(height)));
        root.Add(new XElement(Svg + "g", new XAttribute("id", "background"),
            new XElement(Svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", background))));
        var sceneObjects = new XElement(Svg + "g", new XAttribute("id", "scene-objects"));
        var itemIndex = 0;
        foreach (var item in items)
        {
            itemIndex++;
            var source = XDocument.Load(item.FilePath).Root ?? throw new InvalidDataException("SVG has no root element.");
            if (source.Name.LocalName != "svg") throw new InvalidDataException("Asset root is not SVG.");
            foreach (var element in source.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.Name.LocalName is "fill" or "stroke" or "stop-color" or "color" or "style" or "flood-color")
                        attribute.Value = ApplyPalette(attribute.Value, item.PaletteMap ?? paletteMap);
                }
                if (element.Name.LocalName == "style") element.Value = ApplyPalette(element.Value, item.PaletteMap ?? paletteMap);
            }

            var idMap = CreateIdMap(source, itemIndex, item.InstanceId);
            RewriteIdsAndReferences(source, idMap);

            var viewBox = (source.Attribute("viewBox")?.Value ?? "0 0 1 1").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            if (viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0) throw new InvalidDataException("SVG viewBox is invalid.");
            var scale = Math.Min(item.Width / viewBox[2], item.Height / viewBox[3]);
            var objectId = $"object-{CreateSafeId(item.InstanceId, itemIndex)}";
            var group = new XElement(Svg + "g",
                new XAttribute("id", objectId),
                item.ConceptId is null ? null : new XAttribute("data-concept", item.ConceptId),
                item.Role is null ? null : new XAttribute("data-role", item.Role),
                new XAttribute("data-relevance", F(item.Relevance)),
                new XAttribute("data-importance", F(item.Importance)),
                new XAttribute("data-complexity", F(item.Complexity)),
                new XAttribute("transform", $"translate({F(item.X)} {F(item.Y)}) rotate({F(item.Rotation)}) scale({scale.ToString("0.######", CultureInfo.InvariantCulture)}) translate({F(-viewBox[0] - viewBox[2] / 2)} {F(-viewBox[1] - viewBox[3] / 2)})"));
            var sourceGroup = new XElement(Svg + "g", source.Attributes()
                .Where(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.LocalName is not ("viewBox" or "width" or "height" or "x" or "y" or "version"))
                .Select(attribute => new XAttribute(attribute)));
            foreach (var child in source.Elements()) sourceGroup.Add(new XElement(child));
            group.Add(sourceGroup);
            sceneObjects.Add(group);
        }
        root.Add(sceneObjects);
        return new XDocument(root).ToString(SaveOptions.DisableFormatting);
    }

    private static Dictionary<string, string> CreateIdMap(XElement source, int itemIndex, string? instanceId)
    {
        var prefix = CreateSafeId(instanceId, itemIndex);
        return source.DescendantsAndSelf()
            .Attributes()
            .Where(attribute => attribute.Name.LocalName == "id")
            .Select(attribute => attribute.Value)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => $"{prefix}-{id}", StringComparer.Ordinal);
    }

    private static string CreateSafeId(string? value, int itemIndex)
    {
        var safeValue = Regex.Replace(value ?? "", "[^A-Za-z0-9_.-]", "-");
        return $"{(string.IsNullOrWhiteSpace(safeValue) ? "item" : safeValue)}-{itemIndex}";
    }

    private static void RewriteIdsAndReferences(XElement source, IReadOnlyDictionary<string, string> idMap)
    {
        foreach (var element in source.DescendantsAndSelf())
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.LocalName == "id") attribute.Value = idMap[attribute.Value];
            else if (attribute.Name.LocalName == "href" && attribute.Value.StartsWith('#') && idMap.TryGetValue(attribute.Value[1..], out var hrefReplacement))
                attribute.Value = $"#{hrefReplacement}";
            else if (attribute.Name.LocalName == "style" || attribute.Value.Contains("url(", StringComparison.OrdinalIgnoreCase))
                attribute.Value = RewriteUrlReferences(attribute.Value, idMap);
        }

        foreach (var style in source.DescendantsAndSelf().Where(element => element.Name.LocalName == "style"))
        {
            var css = RewriteUrlReferences(style.Value, idMap);
            style.Value = CssSelectorReference.Replace(css, match => idMap.TryGetValue(match.Groups["id"].Value, out var replacement)
                ? $"#{replacement}"
                : match.Value);
        }
    }

    private static string RewriteUrlReferences(string value, IReadOnlyDictionary<string, string> idMap) =>
        UrlReference.Replace(value, match => idMap.TryGetValue(match.Groups["id"].Value, out var replacement)
            ? $"url(#{replacement})"
            : match.Value);

    private static string ApplyPalette(string value, IReadOnlyDictionary<string, string> paletteMap)
    {
        foreach (var (source, replacement) in paletteMap)
            value = Regex.Replace(value, Regex.Escape(source), replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return value;
    }

    public static IReadOnlyDictionary<string, string> PaletteMap(string palette) => palette switch
    {
        "palette-2" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["#D7CDF5"] = "#FDD7B9", ["#AA9BF0"] = "#FAC896", ["#7864D7"] = "#D27532", ["#F9A3AA"] = "#80DADA", ["#FFE3E1"] = "#BEEBEB" },
        "palette-3" => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["#D7CDF5"] = "#BEEBEB", ["#AA9BF0"] = "#80DADA", ["#7864D7"] = "#5ABEBE", ["#F9ECB0"] = "#FFD4F2", ["#F9A3AA"] = "#6D7BFC", ["#FFE3E1"] = "#B6C6FE" },
        _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
