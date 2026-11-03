using Omega.AI.Gateway.Contracts;
using OmegaIllustrator.Core.Suggestions;

namespace OmegaIllustrator.Tests;

public sealed class SceneSuggestionServiceTests
{
    [Fact]
    public async Task SuggestAsync_returns_only_catalog_assets_and_a_supported_layout()
    {
        var gateway = new FakeAiGateway("""
            {"objects":[{"conceptId":"book","role":"main"},{"conceptId":"lamp","role":"background"}],"layoutPresetId":"under-heading","svg":"ignored"}
            """);
        var service = new SceneSuggestionService(gateway);

        var result = await service.SuggestAsync(CreateRequest());

        Assert.Equal(new SceneSuggestionObject("book", "main", "middle", 1, 0.12, 0, "focal", 0.7, 1), result.Objects[0] with { ColorAssignments = null });
        Assert.Equal(new SceneSuggestionObject("lamp", "background", "far", 0.62, 0.88, 0, "support", 0.7, 0.65), result.Objects[1] with { ColorAssignments = null });
        Assert.All(result.Objects, item => Assert.Empty(item.ColorAssignments!));
        Assert.Equal("under-heading", result.LayoutPresetId);
        Assert.Equal("test-model", result.ModelId);
        Assert.Equal("gpt-6.1-sol", gateway.LastRequest?.ModelId);
        Assert.Equal(AiResponseFormat.Text, gateway.LastRequest?.ResponseFormat);
        Assert.Contains("lamp", gateway.LastRequest!.Messages.Single(message => message.Role == AiMessageRole.User).Content);
    }

    [Fact]
    public async Task SuggestAsync_returns_focal_hierarchy_and_depth_plan_from_geometry_aware_request()
    {
        var gateway = new FakeAiGateway("""
            {"objects":[{"conceptId":"book","role":"main","depth":"middle","emphasis":1,"x":0.5,"y":0.58,"rotationDegrees":0},{"conceptId":"lamp","role":"background","depth":"far","emphasis":0.62,"x":0.28,"y":0.31,"rotationDegrees":-5}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            AvailableAssets =
            [
                new SceneSuggestionAsset("book", "Книга", "main", "L", ["учёба"], 1445.89, 976.66, 51, "wide layered silhouette"),
                new SceneSuggestionAsset("lamp", "Лампа", "background", "L", ["свет"], 945.94, 1128.99, 24, "tall articulated silhouette")
            ]
        };

        var result = await new SceneSuggestionService(gateway).SuggestAsync(request);
        var sentAssetData = gateway.LastRequest!.Messages.Single(message => message.Role == AiMessageRole.User).Content;

        Assert.Equal("middle", result.Objects[0].Depth);
        Assert.Equal(1, result.Objects[0].Emphasis);
        Assert.Equal("far", result.Objects[1].Depth);
        Assert.Equal(0.62, result.Objects[1].Emphasis);
        Assert.Equal(0.28, result.Objects[1].NormalizedX);
        Assert.Contains("1445.89", sentAssetData, StringComparison.Ordinal);
        Assert.Contains("tall articulated silhouette", sentAssetData, StringComparison.Ordinal);
        Assert.Contains("vectorPrimitiveCount", sentAssetData, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("far", 1, 0.5)]
    [InlineData("middle", 0.7, 0.5)]
    [InlineData("middle", 1, 0.02)]
    public async Task SuggestAsync_rejects_spatial_plans_without_one_visible_near_focal_object(string depth, double emphasis, double x)
    {
        var gateway = new FakeAiGateway($"{{\"focalConceptId\":\"book\",\"objects\":[{{\"conceptId\":\"book\",\"role\":\"main\",\"depth\":\"{depth}\",\"emphasis\":{emphasis.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"x\":{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}],\"layoutPresetId\":\"clustered\"}}");

        await Assert.ThrowsAsync<InvalidDataException>(() => new SceneSuggestionService(gateway).SuggestAsync(CreateRequest()));
    }

    [Fact]
    public async Task SuggestAsync_identifies_the_invalid_interest_score_in_diagnostics()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"book","objects":[{"conceptId":"book","role":"main","sceneFunction":"focal","relevance":0.2,"importance":1,"complexity":0.4,"depth":"middle","emphasis":1,"x":0.5}],"layoutPresetId":"clustered"}
            """);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => new SceneSuggestionService(gateway).SuggestAsync(CreateRequest()));

        Assert.Contains("relevance", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestAsync_returns_interest_scores_relations_density_and_a_supported_palette()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"book","density":"rich","suggestedPaletteId":"palette-3","rationale":"Puzzle pieces rest on the book to connect learning with problem solving.","objects":[{"conceptId":"book","role":"main","sceneFunction":"reading","relevance":0.98,"importance":1,"complexity":0.27,"depth":"middle","emphasis":1,"x":0.5},{"conceptId":"puzzle","role":"main","sceneFunction":"meaning","relevance":0.91,"importance":0.82,"complexity":0.72,"depth":"near","emphasis":0.72,"x":0.52}],"relationships":[{"sourceConceptId":"puzzle","targetConceptId":"book","type":"rests_on","strength":0.9}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            AvailableAssets =
            [
                new SceneSuggestionAsset("book", "Книга", "main", "L", ["учёба"], 1445.89, 976.66, 51, "wide book contour"),
                new SceneSuggestionAsset("puzzle", "Пазлы", "main", "L", ["решение"], 544.16, 313.53, 18, "two interlocking shapes")
            ],
            AvailablePalettes = [new("palette-1", "violet"), new("palette-2", "peach"), new("palette-3", "turquoise")]
        };

        var result = await new SceneSuggestionService(gateway).SuggestAsync(request);

        Assert.Equal("rich", result.Density);
        Assert.Equal("palette-3", result.PaletteId);
        Assert.Contains("problem solving", result.Rationale, StringComparison.Ordinal);
        Assert.Equal(0.98, result.Objects[0].Relevance);
        Assert.Equal(0.82, result.Objects[1].Importance);
        Assert.Equal(0.27, result.Objects[0].Complexity);
        Assert.Equal(0.72, result.Objects[1].Complexity);
        Assert.Equal("focal", result.Objects[0].SceneFunction);
        Assert.Equal(new SceneRelationship("puzzle", "book", "rests_on", 0.9), Assert.Single(result.Relationships!));
    }

    [Fact]
    public async Task SuggestAsync_connects_disconnected_relationship_components_to_the_focal_object()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"book","objects":[{"conceptId":"book","role":"main","sceneFunction":"focal","relevance":1,"importance":1,"depth":"middle","emphasis":1,"x":0.5,"y":0.5},{"conceptId":"phone","role":"main","sceneFunction":"digital_reference","relevance":0.8,"importance":0.7,"depth":"near","emphasis":0.7,"x":0.55,"y":0.55},{"conceptId":"puzzle","role":"main","sceneFunction":"problem_solving","relevance":0.8,"importance":0.6,"depth":"near","emphasis":0.65,"x":0.58,"y":0.57}],"relationships":[{"sourceConceptId":"puzzle","targetConceptId":"phone","type":"connects_to","strength":0.75}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            Description = "Учебный цифровой проект с логической задачей",
            MinimumObjects = 3,
            AvailableAssets =
            [
                new SceneSuggestionAsset("book", "Книга", "main", "L"),
                new SceneSuggestionAsset("phone", "Телефон", "main", "L"),
                new SceneSuggestionAsset("puzzle", "Пазлы", "main", "L")
            ]
        };

        var result = await new SceneSuggestionService(gateway).SuggestAsync(request);

        Assert.Contains(result.Relationships!, relation => relation.SourceConceptId == "puzzle" && relation.TargetConceptId == "phone");
        Assert.Contains(result.Relationships!, relation => relation.SourceConceptId == "phone" && relation.TargetConceptId == "book");
    }

    [Fact]
    public async Task SuggestAsync_connects_a_financial_wallet_to_a_learning_book()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"book","objects":[{"conceptId":"book","role":"main","sceneFunction":"focal","relevance":1,"importance":1,"depth":"middle","emphasis":1,"x":0.5,"y":0.5},{"conceptId":"wallet","role":"main","sceneFunction":"financial_context","relevance":0.8,"importance":0.7,"depth":"near","emphasis":0.7,"x":0.62,"y":0.58}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            Description = "Изучение финансового проекта",
            MinimumObjects = 2,
            AvailableAssets =
            [
                new SceneSuggestionAsset("book", "Книга", "main", "L"),
                new SceneSuggestionAsset("wallet", "Кошелёк", "main", "L")
            ]
        };

        var result = await new SceneSuggestionService(gateway).SuggestAsync(request);

        Assert.Contains(result.Relationships!, relation =>
            relation.SourceConceptId == "wallet" && relation.TargetConceptId == "book" && relation.Type == "beside");
    }

    [Fact]
    public async Task SuggestAsync_rejects_a_plan_with_unbridgeable_objects()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"coin","objects":[{"conceptId":"coin","role":"main","sceneFunction":"focal","relevance":1,"importance":1,"depth":"middle","emphasis":1,"x":0.5,"y":0.5},{"conceptId":"flowers","role":"main","sceneFunction":"mood","relevance":0.8,"importance":0.6,"depth":"near","emphasis":0.65,"x":0.62,"y":0.58}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            Description = "Монета и цветы",
            MinimumObjects = 2,
            AvailableAssets =
            [
                new SceneSuggestionAsset("coin", "Монета", "main", "L"),
                new SceneSuggestionAsset("flowers", "Цветы", "main", "L")
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new SceneSuggestionService(gateway).SuggestAsync(request));
    }

    [Fact]
    public async Task SuggestAsync_uses_background_variants_for_secondary_objects_when_available()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"book","objects":[{"conceptId":"book","role":"main","sceneFunction":"focal","relevance":1,"importance":1,"depth":"middle","emphasis":1,"x":0.5},{"conceptId":"lamp","role":"main","sceneFunction":"support","relevance":0.9,"importance":0.7,"depth":"far","emphasis":0.7,"x":0.25}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            AvailableAssets =
            [
                new SceneSuggestionAsset("book", "Книга", "main", "L"),
                new SceneSuggestionAsset("lamp", "Лампа", "main", "L"),
                new SceneSuggestionAsset("lamp", "Лампа", "background", "L")
            ]
        };

        var result = await new SceneSuggestionService(gateway).SuggestAsync(request);

        Assert.Equal("main", result.Objects.Single(item => item.ConceptId == "book").Role);
        Assert.Equal("background", result.Objects.Single(item => item.ConceptId == "lamp").Role);
    }

    [Fact]
    public async Task SuggestAsync_rejects_fewer_than_the_requested_minimum_objects()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"headphones","objects":[{"conceptId":"headphones","role":"main","sceneFunction":"focal","relevance":1,"importance":1,"depth":"middle","emphasis":1,"x":0.5},{"conceptId":"book","role":"main","sceneFunction":"support","relevance":0.8,"importance":0.6,"depth":"near","emphasis":0.7,"x":0.7}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            MinimumObjects = 3,
            AvailableAssets =
            [
                new SceneSuggestionAsset("headphones", "Наушники", "main", "L"),
                new SceneSuggestionAsset("book", "Книга", "main", "L"),
                new SceneSuggestionAsset("flowers", "Цветы", "main", "L")
            ]
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => new SceneSuggestionService(gateway).SuggestAsync(request));
    }

    [Fact]
    public async Task SuggestAsync_prompt_contains_object_meaning_matrix_and_four_object_preference()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"book","objects":[{"conceptId":"book","role":"main","sceneFunction":"focal","relevance":1,"importance":1,"depth":"middle","emphasis":1,"x":0.5},{"conceptId":"lamp","role":"background","sceneFunction":"support","relevance":0.8,"importance":0.6,"depth":"far","emphasis":0.7,"x":0.25},{"conceptId":"flowers","role":"main","sceneFunction":"atmosphere","relevance":0.75,"importance":0.5,"depth":"far","emphasis":0.65,"x":0.75}],"layoutPresetId":"clustered"}
            """);

        var request = CreateRequest() with
        {
            Description = "Идеи для продуктивной учёбы и уютной атмосферы с цветами",
            MinimumObjects = 3,
            AvailableAssets =
            [
                new SceneSuggestionAsset("book", "Книга", "main", "L"),
                new SceneSuggestionAsset("lamp", "Лампа", "main", "L"),
                new SceneSuggestionAsset("lamp", "Лампа", "background", "L"),
                new SceneSuggestionAsset("flowers", "Цветы", "main", "L"),
                new SceneSuggestionAsset("flowers", "Цветы", "background", "L")
            ]
        };

        await new SceneSuggestionService(gateway).SuggestAsync(request);

        var instructions = gateway.LastRequest!.Messages.Single(message => message.Role == AiMessageRole.System).Content;
        Assert.Contains("at least three", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferredObjectCount=", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("flowers", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("puzzle", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wallet", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"complexity\":", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("four or more", instructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestAsync_honors_manual_density_and_palette_choices_over_model_recommendations()
    {
        var gateway = new FakeAiGateway("""
            {"focalConceptId":"book","density":"rich","suggestedPaletteId":"palette-3","objects":[{"conceptId":"book","role":"main","sceneFunction":"focal","relevance":0.95,"importance":1,"depth":"middle","emphasis":1,"x":0.5}],"layoutPresetId":"clustered"}
            """);
        var request = CreateRequest() with
        {
            RequestedDensity = "airy",
            RequestedPaletteId = "palette-2",
            AvailablePalettes = [new("palette-2", "warm peach"), new("palette-3", "cool turquoise")]
        };

        var result = await new SceneSuggestionService(gateway).SuggestAsync(request);

        Assert.Equal("airy", result.Density);
        Assert.Equal("palette-2", result.PaletteId);
        var promptPayload = gateway.LastRequest!.Messages.Single(message => message.Role == AiMessageRole.User).Content;
        Assert.Contains("requestedDensity", promptPayload, StringComparison.Ordinal);
        Assert.Contains("warm peach", promptPayload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ghost", "main", "clustered")]
    [InlineData("book", "background", "clustered")]
    [InlineData("book", "main", "draw-new-svg")]
    public async Task SuggestAsync_rejects_unavailable_assets_roles_and_layouts(string conceptId, string role, string layout)
    {
        var gateway = new FakeAiGateway($"{{\"objects\":[{{\"conceptId\":\"{conceptId}\",\"role\":\"{role}\"}}],\"layoutPresetId\":\"{layout}\"}}");

        await Assert.ThrowsAsync<InvalidDataException>(() => new SceneSuggestionService(gateway).SuggestAsync(CreateRequest()));
    }

    [Fact]
    public async Task SuggestAsync_rejects_background_when_user_disabled_background_objects()
    {
        var gateway = new FakeAiGateway("{\"objects\":[{\"conceptId\":\"lamp\",\"role\":\"background\"}],\"layoutPresetId\":\"clustered\"}");

        await Assert.ThrowsAsync<InvalidDataException>(() => new SceneSuggestionService(gateway).SuggestAsync(CreateRequest(allowBackgrounds: false)));
    }

    [Fact]
    public async Task SuggestAsync_keeps_user_selected_layout_instead_of_model_override()
    {
        var gateway = new FakeAiGateway("{\"objects\":[{\"conceptId\":\"book\",\"role\":\"main\"}],\"layoutPresetId\":\"offset\"}");

        var result = await new SceneSuggestionService(gateway).SuggestAsync(CreateRequest(layout: "under-heading"));

        Assert.Equal("under-heading", result.LayoutPresetId);
    }

    [Fact]
    public async Task SuggestAsync_accepts_a_json_fenced_text_response_and_ignores_extra_fields()
    {
        var gateway = new FakeAiGateway("```json\n{\"objects\":[{\"conceptId\":\"book\",\"role\":\"main\"}],\"layoutPresetId\":\"clustered\",\"command\":\"ignored\"}\n```");

        var result = await new SceneSuggestionService(gateway).SuggestAsync(CreateRequest());

        Assert.Single(result.Objects);
        Assert.Equal("book", result.Objects[0].ConceptId);
    }

    [Fact]
    public async Task SuggestAsync_does_not_send_a_request_when_profile_has_no_available_models()
    {
        var gateway = new FakeAiGateway("{}", []);

        await Assert.ThrowsAsync<InvalidDataException>(() => new SceneSuggestionService(gateway).SuggestAsync(CreateRequest()));

        Assert.Null(gateway.LastRequest);
    }

    [Fact]
    public async Task SuggestAsync_uses_preferred_model_when_it_is_available()
    {
        var gateway = new FakeAiGateway(
            "{\"objects\":[{\"conceptId\":\"book\",\"role\":\"main\"}],\"layoutPresetId\":\"clustered\"}",
            [new AiModelInfo("gpt-6.1-sol"), new AiModelInfo("gpt-6-luna")]);

        await new SceneSuggestionService(gateway).SuggestAsync(CreateRequest(modelId: "gpt-6-luna"));

        Assert.Equal("gpt-6-luna", gateway.LastRequest?.ModelId);
    }

    [Fact]
    public async Task SuggestAsync_accepts_all_ten_distinct_catalog_concepts()
    {
        var concepts = new[] { "book", "phone", "puzzle", "coin", "headphones", "lamp", "flowers", "wallet", "folder", "pen" };
        var assets = concepts.Select(concept => new SceneSuggestionAsset(concept, concept, "main", "L")).ToArray();
        var objects = string.Join(',', concepts.Select((concept, index) => $"{{\"conceptId\":\"{concept}\",\"role\":\"main\",\"emphasis\":{(index == 0 ? "1.0" : "0.7")},\"depth\":\"middle\"}}"));
        var relationships = """
            ,"relationships":[{"sourceConceptId":"phone","targetConceptId":"book","type":"beside","strength":0.7},{"sourceConceptId":"puzzle","targetConceptId":"book","type":"rests_on","strength":0.7},{"sourceConceptId":"coin","targetConceptId":"wallet","type":"rests_on","strength":0.7},{"sourceConceptId":"headphones","targetConceptId":"book","type":"pairs_with","strength":0.7},{"sourceConceptId":"lamp","targetConceptId":"book","type":"illuminates","strength":0.7},{"sourceConceptId":"flowers","targetConceptId":"book","type":"beside","strength":0.7},{"sourceConceptId":"wallet","targetConceptId":"book","type":"beside","strength":0.7},{"sourceConceptId":"folder","targetConceptId":"book","type":"beside","strength":0.7},{"sourceConceptId":"pen","targetConceptId":"book","type":"rests_on","strength":0.7}]
            """;
        var gateway = new FakeAiGateway($"{{\"focalConceptId\":\"book\",\"objects\":[{objects}]{relationships},\"layoutPresetId\":\"clustered\"}}");
        var request = CreateRequest(maximumObjects: 10) with
        {
            Description = "Книга, телефон, пазлы, монета, наушники, лампа, цветы, кошелёк, папка и ручка",
            MinimumObjects = 10,
            AvailableAssets = assets
        };

        var result = await new SceneSuggestionService(gateway).SuggestAsync(request);

        Assert.Equal(10, result.Objects.Count);
    }

    [Fact]
    public async Task SuggestAsync_falls_back_to_first_available_model_when_preference_is_unavailable()
    {
        var gateway = new FakeAiGateway(
            "{\"objects\":[{\"conceptId\":\"book\",\"role\":\"main\"}],\"layoutPresetId\":\"clustered\"}",
            [new AiModelInfo("gpt-6-luna")]);

        await new SceneSuggestionService(gateway).SuggestAsync(CreateRequest(modelId: "removed-model"));

        Assert.Equal("gpt-6-luna", gateway.LastRequest?.ModelId);
    }

    private static SceneSuggestionRequest CreateRequest(string layout = "auto", bool allowBackgrounds = true, int maximumObjects = 10, string? modelId = null) => new(
        "Идеи для продуктивной учёбы",
        [
            new("book", "Книга", "main", "L", ["учёба", "чтение"]),
            new("lamp", "Лампа", "main", "L", ["работа", "свет"]),
            new("lamp", "Лампа", "background", "L", ["работа", "свет"])
        ],
        "L",
        "square",
        layout,
        allowBackgrounds,
        MaximumObjects: maximumObjects,
        PreferredModelId: modelId);

    private sealed class FakeAiGateway(string response, IReadOnlyList<AiModelInfo>? models = null) : IAiGateway
    {
        public AiRequest? LastRequest { get; private set; }

        public Task<IReadOnlyList<AiModelInfo>> GetModelsAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(models ?? (IReadOnlyList<AiModelInfo>)[new AiModelInfo("gpt-6.1-sol")]);
        public Task<AiConnectionTest> TestConnectionAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiResponse(response, "test-model"));
        }

        public async IAsyncEnumerable<AiStreamEvent> StreamAsync(AiRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
