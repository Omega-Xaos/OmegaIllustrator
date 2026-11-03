using OmegaIllustrator.Web.Services;
using OmegaIllustrator.Core.Suggestions;

namespace OmegaIllustrator.Tests;

public sealed class SceneLayoutEngineTests
{
    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(1200, 630)]
    [InlineData(800, 1200)]
    public void Arrange_fits_ten_objects_inside_canvas_without_overlapping_boxes_and_preserves_source_scale(double width, double height)
    {
        var geometry = SourceGeometry();

        var placements = SceneLayoutEngine.Arrange(width, height, geometry, "clustered");

        Assert.Equal(10, placements.Count);
        for (var index = 0; index < placements.Count; index++)
        {
            var current = placements[index];
            Assert.True(current.X - current.Width / 2 >= 0);
            Assert.True(current.Y - current.Height / 2 >= 0);
            Assert.True(current.X + current.Width / 2 <= width);
            Assert.True(current.Y + current.Height / 2 <= height);
            for (var otherIndex = index + 1; otherIndex < placements.Count; otherIndex++)
                Assert.False(Overlaps(current, placements[otherIndex]));
        }

        var scale = placements[0].Width / geometry[0].SourceWidth;
        for (var index = 0; index < geometry.Count; index++)
        {
            Assert.Equal(scale, placements[index].Width / geometry[index].SourceWidth, precision: 10);
            Assert.Equal(scale, placements[index].Height / geometry[index].SourceHeight, precision: 10);
        }
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(1200, 630)]
    [InlineData(800, 1200)]
    public void Under_heading_keeps_the_composition_below_the_title_safe_area(double width, double height)
    {
        var geometry = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.5, "middle", 1),
            new SceneObjectGeometry(945.94, 1128.99, "background", 0.3, "far", 0.62),
            new SceneObjectGeometry(508.43, 285.4, "background", 0.7, "far", 0.58)
        };
        var placements = SceneLayoutEngine.Arrange(width, height, geometry, "under-heading");

        Assert.All(placements, item => Assert.True(item.Y - item.Height / 2 >= height * 0.32));
    }

    [Fact]
    public void Clustered_three_item_scene_keeps_support_objects_close_to_the_main_object()
    {
        var geometry = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.5, "middle", 1),
            new SceneObjectGeometry(945.94, 1128.99, "background", 0.3, "far", 0.62),
            new SceneObjectGeometry(508.43, 285.4, "background", 0.7, "far", 0.58)
        };

        var placements = SceneLayoutEngine.Arrange(1000, 1000, geometry, "clustered");
        var groupWidth = placements.Max(item => item.X + item.Width / 2) - placements.Min(item => item.X - item.Width / 2);
        var groupHeight = placements.Max(item => item.Y + item.Height / 2) - placements.Min(item => item.Y - item.Height / 2);

        Assert.True(placements[1].ZOrder < placements[0].ZOrder);
        Assert.True(placements[2].ZOrder < placements[0].ZOrder);
        Assert.Equal(placements[0].Y, placements[1].Y, precision: 8);
        Assert.Equal(placements[0].Y, placements[2].Y, precision: 8);
        Assert.True(groupWidth <= 800);
        Assert.True(groupHeight <= 800);
    }

    [Fact]
    public void Arrange_uses_ai_depth_and_emphasis_to_build_a_spatial_hierarchy()
    {
        var objects = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.50, "middle", 1.00, 0),
            new SceneObjectGeometry(945.94, 1128.99, "background", 0.25, "far", 0.62, -5),
            new SceneObjectGeometry(508.43, 285.4, "background", 0.75, "near", 0.55, 4)
        };

        var placements = SceneLayoutEngine.Arrange(1000, 1000, objects, "clustered");

        Assert.Equal(placements[0].Y, placements[1].Y, precision: 8);
        Assert.Equal(placements[0].Y, placements[2].Y, precision: 8);
        Assert.True(placements[1].ZOrder < placements[0].ZOrder);
        Assert.True(placements[0].ZOrder < placements[2].ZOrder);
        Assert.Equal(0.62, placements[1].Emphasis);
        Assert.Equal(-5, placements[1].RotationDegrees);
        Assert.Equal(0.50, placements[0].X / 1000, precision: 2);
        Assert.False(Overlaps(placements[0], placements[1]));
        Assert.False(Overlaps(placements[0], placements[2]));
        Assert.False(Overlaps(placements[1], placements[2]));
        Assert.Equal(placements[0].Width / objects[0].SourceWidth, placements[1].Width / objects[1].SourceWidth, precision: 10);
        Assert.Equal(placements[0].Width / objects[0].SourceWidth, placements[2].Width / objects[2].SourceWidth, precision: 10);
    }

    [Fact]
    public void Arrange_places_an_object_on_its_named_support_only_when_ai_returns_that_relation()
    {
        var objects = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.5, "middle", 1, 0, "book"),
            new SceneObjectGeometry(544.16, 313.53, "main", 0.5, "near", 0.72, 0, "puzzle")
        };

        var placements = SceneLayoutEngine.Arrange(1000, 1000, objects, "balanced", "clustered", [new("puzzle", "book", "rests_on", 0.9)]);

        Assert.True(placements[1].IntentionalOverlap);
        Assert.True(placements[1].ZOrder > placements[0].ZOrder);
        Assert.InRange(Math.Abs(placements[1].X - placements[0].X), 0, placements[0].Width * 0.2);
        Assert.True(Overlaps(placements[0], placements[1]));
        Assert.Equal(placements[0].Width / objects[0].SourceWidth, placements[1].Width / objects[1].SourceWidth, precision: 10);
    }

    [Fact]
    public void Arrange_places_an_illuminating_lamp_behind_the_focal_surface()
    {
        var objects = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.5, "middle", 1, 0, "book"),
            new SceneObjectGeometry(945.94, 1128.99, "background", 0.25, "far", 0.62, 0, "lamp")
        };

        var placements = SceneLayoutEngine.Arrange(1000, 1000, objects, "balanced", "clustered", [new("lamp", "book", "illuminates", 0.9)]);

        Assert.True(placements[1].Y < placements[0].Y);
        Assert.True(placements[1].ZOrder < placements[0].ZOrder);
        Assert.False(Overlaps(placements[0], placements[1]));
        Assert.Equal(500, ((placements.Min(item => item.X - item.Width / 2) + placements.Max(item => item.X + item.Width / 2)) / 2), precision: 6);
        Assert.Equal(500, ((placements.Min(item => item.Y - item.Height / 2) + placements.Max(item => item.Y + item.Height / 2)) / 2), precision: 6);
    }

    [Fact]
    public void Arrange_uses_a_reasonable_shared_scale_for_a_two_object_surface_cluster()
    {
        var objects = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.5, "middle", 1, 0, "book", 1, 0.5),
            new SceneObjectGeometry(1240.62, 658.59, "main", 0.5, "near", 0.7, 0, "folder", 0.7, 0.5)
        };

        var placements = SceneLayoutEngine.Arrange(1000, 1000, objects, "balanced", "clustered", [new("folder", "book", "beside", 0.8)]);

        Assert.True(placements[0].Width >= 380);
        Assert.Equal(placements[0].Width / objects[0].SourceWidth, placements[1].Width / objects[1].SourceWidth, precision: 10);
        Assert.Equal(placements[0].Height / objects[0].SourceHeight, placements[1].Height / objects[1].SourceHeight, precision: 10);
    }

    [Fact]
    public void Arrange_depth_changes_layer_order_without_creating_a_separate_spatial_row()
    {
        var objects = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.5, "far", 1, 0, "book", 1, 0.5),
            new SceneObjectGeometry(945.94, 1128.99, "background", 0.5, "near", 0.6, 0, "lamp", 0.5, 0.5)
        };

        var placements = SceneLayoutEngine.Arrange(1000, 1000, objects, "balanced", "clustered");

        Assert.Equal(placements[0].Y, placements[1].Y, precision: 8);
        Assert.True(placements[0].ZOrder < placements[1].ZOrder);
    }

    [Fact]
    public void Arrange_positions_nested_clusters_after_their_support_is_placed()
    {
        var objects = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.5, "middle", 1, 0, "book", 1, 0.5),
            new SceneObjectGeometry(796.53, 461.67, "main", 0.58, "middle", 0.7, 0, "phone", 0.7, 0.55),
            new SceneObjectGeometry(544.16, 313.53, "main", 0.62, "near", 0.65, 0, "puzzle", 0.6, 0.58)
        };
        var relationships = new[]
        {
            new SceneRelationship("puzzle", "phone", "connects_to", 0.75),
            new SceneRelationship("phone", "book", "beside", 0.75)
        };

        var placements = SceneLayoutEngine.Arrange(1000, 1000, objects, "balanced", "clustered", relationships);

        Assert.Equal(objects.Length, placements.Count);
        Assert.All(placements, placement =>
        {
            Assert.InRange(placement.X - placement.Width / 2, 0, 1000);
            Assert.InRange(placement.Y - placement.Height / 2, 0, 1000);
            Assert.InRange(placement.X + placement.Width / 2, 0, 1000);
            Assert.InRange(placement.Y + placement.Height / 2, 0, 1000);
        });
    }

    [Fact]
    public void Rich_density_uses_tighter_spacing_without_changing_relative_object_scale()
    {
        var objects = new[]
        {
            new SceneObjectGeometry(1445.89, 976.66, "main", 0.4, "middle", 1, 0, "book"),
            new SceneObjectGeometry(945.94, 1128.99, "background", 0.6, "far", 0.65, 0, "lamp")
        };

        var airy = SceneLayoutEngine.Arrange(1000, 1000, objects, "airy", "clustered");
        var rich = SceneLayoutEngine.Arrange(1000, 1000, objects, "rich", "clustered");

        var airyGap = (airy[0].Y - airy[0].Height / 2) - (airy[1].Y + airy[1].Height / 2);
        var richGap = (rich[0].Y - rich[0].Height / 2) - (rich[1].Y + rich[1].Height / 2);
        Assert.True(richGap < airyGap);
        Assert.Equal(rich[0].Width / objects[0].SourceWidth, rich[1].Width / objects[1].SourceWidth, precision: 10);
    }

    private static IReadOnlyList<SceneObjectGeometry> SourceGeometry() =>
    [
        new(1445.89, 976.66, "main"),
        new(796.53, 461.67, "main"),
        new(544.16, 313.53, "main"),
        new(286.68, 188.5, "main"),
        new(888.15, 691.54, "main"),
        new(945.94, 1128.99, "main"),
        new(590.57, 747.11, "background"),
        new(630.42, 396.31, "background"),
        new(1240.62, 658.59, "background"),
        new(508.43, 285.4, "background")
    ];

    private static bool Overlaps(ScenePlacement first, ScenePlacement second) =>
        Math.Abs(first.X - second.X) < (first.Width + second.Width) / 2 &&
        Math.Abs(first.Y - second.Y) < (first.Height + second.Height) / 2;
}
