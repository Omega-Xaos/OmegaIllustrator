using System.Text.Json;
using OmegaIllustrator.Core.Suggestions;

namespace OmegaIllustrator.Web.Services;

public sealed class SvgPaletteCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IReadOnlyDictionary<string, SvgPaletteDefinition> _palettes;

    private SvgPaletteCatalog(IEnumerable<SvgPaletteDefinition> palettes) =>
        _palettes = palettes.ToDictionary(palette => palette.Id, StringComparer.OrdinalIgnoreCase);

    public static SvgPaletteCatalog FromJson(string json)
    {
        var palettes = JsonSerializer.Deserialize<SvgPaletteDefinition[]>(json, JsonOptions)
            ?? throw new InvalidDataException("Palette catalog is empty or invalid.");
        if (palettes.Length == 0 || palettes.Any(palette => string.IsNullOrWhiteSpace(palette.Id)))
            throw new InvalidDataException("Palette catalog must contain entries with IDs.");
        return new SvgPaletteCatalog(palettes);
    }

    public string BackgroundColor(string paletteId) => GetPalette(paletteId).Background.Bg1;

    public string Accent3Color(string paletteId) => GetPalette(paletteId).ObjectOnly.Accent3;

    public IReadOnlyDictionary<string, string> CreateColorMap(string paletteId)
    {
        var source = GetPalette("palette-1");
        var target = GetPalette(paletteId);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddRoleMap(map, [source.Background.Bg1, source.Background.Bg2, source.Background.Bg3], [target.Background.Bg1, target.Background.Bg2, target.Background.Bg3]);
        AddRoleMap(map, [source.ObjectOnly.Accent1, source.ObjectOnly.Accent2, source.ObjectOnly.Accent3], [target.ObjectOnly.Accent1, target.ObjectOnly.Accent2, target.ObjectOnly.Accent3]);
        return map;
    }

    public IReadOnlyDictionary<string, string> CreateBackgroundObjectColorMap(string paletteId)
    {
        var source = GetPalette("palette-1");
        var target = GetPalette(paletteId);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddRoleMap(map,
            [source.Background.Bg1, source.Background.Bg2, source.Background.Bg3],
            [target.ObjectOnly.Accent3, target.ObjectOnly.Accent1, target.ObjectOnly.Accent2]);
        return map;
    }

    public IReadOnlyDictionary<string, string> CreateAssetColorMap(
        string paletteId,
        string assetRole,
        IEnumerable<SceneSuggestionColorRole>? sourceRoles,
        IReadOnlyDictionary<string, string>? accentAssignments = null)
    {
        var map = new Dictionary<string, string>(
            assetRole == "background" ? CreateBackgroundObjectColorMap(paletteId) : CreateColorMap(paletteId),
            StringComparer.OrdinalIgnoreCase);
        if (assetRole == "background") return map;

        foreach (var sourceRole in sourceRoles ?? [])
        {
            if (!string.Equals(sourceRole.Behavior, "palette-role", StringComparison.Ordinal)) continue;

            var targetRole = sourceRole.Role;
            if (sourceRole.Role is "objectOnly.accent1" or "objectOnly.accent2" or "objectOnly.accent3" &&
                accentAssignments?.TryGetValue(sourceRole.Role, out var assignedRole) == true &&
                assignedRole is "objectOnly.accent1" or "objectOnly.accent2" or "objectOnly.accent3")
                targetRole = assignedRole;

            if (TryGetColorForRole(paletteId, targetRole, out var targetColor))
                map[sourceRole.SourceColor] = targetColor;
        }
        return map;
    }

    public IReadOnlyDictionary<string, string> GetColorSlots(string paletteId)
    {
        var palette = GetPalette(paletteId);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["background.bg1"] = palette.Background.Bg1,
            ["background.bg2"] = palette.Background.Bg2,
            ["background.bg3"] = palette.Background.Bg3,
            ["objectOnly.accent1"] = palette.ObjectOnly.Accent1,
            ["objectOnly.accent2"] = palette.ObjectOnly.Accent2,
            ["objectOnly.accent3"] = palette.ObjectOnly.Accent3,
            ["neutral.outline"] = palette.Neutral.Outline,
            ["neutral.white"] = palette.Neutral.White
        };
    }

    public IReadOnlyList<ScenePaletteOption> ToSceneOptions() => _palettes.Keys
        .OrderBy(id => id, StringComparer.Ordinal)
        .Select(id =>
        {
            var slots = GetColorSlots(id);
            var description = id switch
            {
                "palette-1" => "сиреневая: мягкая, спокойная, творческая",
                "palette-2" => "персиковая: тёплая, дружелюбная, энергичная",
                "palette-3" => "бирюзовая: собранная, ясная, спокойная",
                _ => "доступная согласованная цветовая схема"
            };
            return new ScenePaletteOption(id, description, slots["background.bg1"],
                [slots["objectOnly.accent1"], slots["objectOnly.accent2"], slots["objectOnly.accent3"]],
                slots["background.bg2"], slots["background.bg3"], slots);
        })
        .ToArray();

    private bool TryGetColorForRole(string paletteId, string role, out string color)
    {
        var palette = GetPalette(paletteId);
        color = role switch
        {
            "background.bg1" => palette.Background.Bg1,
            "background.bg2" => palette.Background.Bg2,
            "background.bg3" => palette.Background.Bg3,
            "objectOnly.accent1" => palette.ObjectOnly.Accent1,
            "objectOnly.accent2" => palette.ObjectOnly.Accent2,
            "objectOnly.accent3" => palette.ObjectOnly.Accent3,
            "neutral.outline" => palette.Neutral.Outline,
            "neutral.white" => palette.Neutral.White,
            _ => ""
        };
        return color.Length > 0;
    }

    private SvgPaletteDefinition GetPalette(string paletteId) =>
        _palettes.TryGetValue(paletteId, out var palette)
            ? palette
            : throw new KeyNotFoundException($"Palette '{paletteId}' was not found.");

    private static void AddRoleMap(Dictionary<string, string> map, IEnumerable<string> sourceColors, IEnumerable<string> targetColors)
    {
        using var source = sourceColors.GetEnumerator();
        using var target = targetColors.GetEnumerator();
        while (source.MoveNext() && target.MoveNext())
        {
            var sourceColor = source.Current;
            var targetColor = target.Current;
            if (!string.Equals(sourceColor, targetColor, StringComparison.OrdinalIgnoreCase))
                map[sourceColor] = targetColor;
        }
    }
}

public sealed record SvgPaletteDefinition(string Id, SvgPaletteBackgroundColors Background, SvgPaletteObjectColors ObjectOnly, SvgPaletteNeutralColors Neutral);

public sealed record SvgPaletteBackgroundColors(string Bg1, string Bg2, string Bg3);

public sealed record SvgPaletteObjectColors(string Accent1, string Accent2, string Accent3);

public sealed record SvgPaletteNeutralColors(string Outline, string White);
