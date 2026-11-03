namespace OmegaIllustrator.Core.Suggestions;

public sealed record SceneSuggestionAsset(
    string ConceptId,
    string Name,
    string Role,
    string Size,
    IReadOnlyList<string>? Tags = null,
    double SourceWidth = 0,
    double SourceHeight = 0,
    int VectorPrimitiveCount = 0,
    string? ContourProfile = null,
    string? VisualRegions = null,
    IReadOnlyList<SceneSuggestionColorRole>? ColorRoles = null);

public sealed record SceneSuggestionRequest(
    string Description,
    IReadOnlyList<SceneSuggestionAsset> AvailableAssets,
    string Size,
    string CanvasFormat,
    string RequestedLayoutPresetId,
    bool AllowBackgrounds,
    int MaximumObjects = 10,
    string ProfileId = "chatgpt",
    string? PreferredModelId = null,
    string RequestedDensity = "auto",
    string RequestedPaletteId = "auto",
    IReadOnlyList<ScenePaletteOption>? AvailablePalettes = null,
    int MinimumObjects = 1,
    int? PreferredObjectCount = null,
    IReadOnlyList<SceneCompositionMemory>? RecentCompositions = null,
    double CanvasWidth = 1000,
    double CanvasHeight = 1000);

public sealed record SceneCompositionMemory(
    string FocalConceptId,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<string> RelationshipSignatures,
    IReadOnlyList<string> SpatialSignatures,
    string LayoutPresetId);

public sealed record ScenePaletteOption(
    string Id,
    string MoodDescription,
    string BackgroundSample = "",
    IReadOnlyList<string>? AccentSamples = null,
    string BackgroundShade = "",
    string BackgroundShadow = "",
    IReadOnlyDictionary<string, string>? ColorSlots = null);

public sealed record SceneSuggestionColorRole(string SourceColor, string Role, string Behavior);

public sealed record SceneSuggestionObject(
    string ConceptId,
    string Role,
    string Depth = "middle",
    double Emphasis = 1,
    double NormalizedX = 0.5,
    double RotationDegrees = 0,
    string SceneFunction = "support",
    double Relevance = 0.5,
    double Importance = 0.5,
    double Complexity = 0.5,
    double NormalizedY = 0.5,
    IReadOnlyDictionary<string, string>? ColorAssignments = null);

public sealed record SceneRelationship(string SourceConceptId, string TargetConceptId, string Type, double Strength = 0.75);

public sealed record SceneSuggestion(
    IReadOnlyList<SceneSuggestionObject> Objects,
    string LayoutPresetId,
    string ModelId,
    string? FocalConceptId = null,
    string Density = "balanced",
    string PaletteId = "palette-1",
    IReadOnlyList<SceneRelationship>? Relationships = null,
    string Rationale = "");
