using OmegaIllustrator.Core.Suggestions;

namespace OmegaIllustrator.Web.Services;

public sealed record ScenePlacement(double X, double Y, double Width, double Height, double Emphasis = 1, double RotationDegrees = 0, double ZOrder = 0, bool IntentionalOverlap = false, double Importance = 0.5);

public sealed record SceneObjectGeometry(
    double SourceWidth,
    double SourceHeight,
    string Role,
    double NormalizedX = 0.5,
    string Depth = "middle",
    double Emphasis = 1,
    double RotationDegrees = 0,
    string? ConceptId = null,
    double Importance = 0.5,
    double NormalizedY = 0.5);

public static class SceneLayoutEngine
{
    private static readonly string[] DepthOrder = ["far", "middle", "near"];
    private static readonly HashSet<string> OverlayRelations = new(["rests_on", "inside", "overlaps"], StringComparer.Ordinal);
    private static readonly HashSet<string> ClusterRelations = new(["beside", "pairs_with", "connects_to"], StringComparer.Ordinal);
    private static readonly HashSet<string> BehindRelations = new(["behind"], StringComparer.Ordinal);
    private static readonly HashSet<string> LightingRelations = new(["illuminates"], StringComparer.Ordinal);

    public static IReadOnlyList<ScenePlacement> Arrange(double width, double height, IReadOnlyList<SceneObjectGeometry> objects, string preset) =>
        Arrange(width, height, objects, "balanced", preset);

    public static IReadOnlyList<ScenePlacement> Arrange(
        double width,
        double height,
        IReadOnlyList<SceneObjectGeometry> objects,
        string density,
        string preset,
        IReadOnlyList<SceneRelationship>? relationships = null,
        SceneCompositionIntent? compositionIntent = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (!double.IsFinite(width) || !double.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(width));
        if (objects.Count == 0) return [];
        if (objects.Any(item => !double.IsFinite(item.SourceWidth) || !double.IsFinite(item.SourceHeight) || item.SourceWidth <= 0 || item.SourceHeight <= 0))
            throw new ArgumentException("Every object must have positive finite source dimensions.", nameof(objects));
        if (objects.Any(item => !double.IsFinite(item.NormalizedX) || item.NormalizedX is < 0.12 or > 0.88 ||
            !double.IsFinite(item.NormalizedY) || item.NormalizedY is < 0.12 or > 0.88 ||
            !double.IsFinite(item.Emphasis) || item.Emphasis is < 0.45 or > 1 ||
            !double.IsFinite(item.RotationDegrees) || item.RotationDegrees is < -15 or > 15 ||
            !double.IsFinite(item.Importance) || item.Importance is < 0 or > 1))
            throw new ArgumentException("Every object must have valid spatial and emphasis values.", nameof(objects));

        var intent = compositionIntent ?? new SceneCompositionIntent("general_scene", "neutral_or_user_defined", "balanced_natural_grouping", "balanced_spacing");
        var compositionRelationships = (relationships ?? []).Select(relation => relation.Type == "supports"
            ? new SceneRelationship(relation.TargetConceptId, relation.SourceConceptId, "rests_on", relation.Strength)
            : relation).ToArray();
        var planned = ApplyRelationshipHints(ApplySpatialIntent(objects, density, intent), compositionRelationships);
        var overlays = compositionRelationships.Where(relation => OverlayRelations.Contains(relation.Type)).ToArray();
        var overlaySources = overlays.Select(relation => relation.SourceConceptId).ToHashSet(StringComparer.Ordinal);
        var clusters = compositionRelationships
            .Where(relation => ClusterRelations.Contains(relation.Type) && !overlaySources.Contains(relation.SourceConceptId))
            .ToArray();
        var clusterSources = clusters.Select(relation => relation.SourceConceptId).ToHashSet(StringComparer.Ordinal);
        var behind = compositionRelationships
            .Where(relation => BehindRelations.Contains(relation.Type) && !overlaySources.Contains(relation.SourceConceptId) && !clusterSources.Contains(relation.SourceConceptId))
            .GroupBy(relation => relation.SourceConceptId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(relation => relation.Strength).First())
            .ToArray();
        var behindSources = behind.Select(relation => relation.SourceConceptId).ToHashSet(StringComparer.Ordinal);
        var lighting = compositionRelationships
            .Where(relation => LightingRelations.Contains(relation.Type) && !overlaySources.Contains(relation.SourceConceptId) && !clusterSources.Contains(relation.SourceConceptId) && !behindSources.Contains(relation.SourceConceptId))
            .GroupBy(relation => relation.SourceConceptId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(relation => relation.Strength).First())
            .ToArray();
        var lightingSources = lighting.Select(relation => relation.SourceConceptId).ToHashSet(StringComparer.Ordinal);
        var freeIndices = Enumerable.Range(0, planned.Length).Where(index => planned[index].ConceptId is not string conceptId || !overlaySources.Contains(conceptId) && !clusterSources.Contains(conceptId) && !behindSources.Contains(conceptId) && !lightingSources.Contains(conceptId)).ToArray();
        if (freeIndices.Length == 0)
        {
            var anchorIndex = Enumerable.Range(0, planned.Length).MaxBy(index => planned[index].Importance);
            freeIndices = [anchorIndex];
            var anchorConcept = planned[anchorIndex].ConceptId ?? "";
            overlaySources.Remove(anchorConcept);
            clusterSources.Remove(anchorConcept);
            behindSources.Remove(anchorConcept);
            lightingSources.Remove(anchorConcept);
        }

        var maxColumns = width / height > 1.35 ? 4 : width / height < 0.8 ? 2 : 3;
        var rows = BuildRows(planned, freeIndices, maxColumns);
        var densityGapRatio = density switch { "airy" => 0.14, "rich" => 0.035, _ => 0.055 };
        var gapRatio = intent.Grouping switch
        {
            "compact_group" => Math.Min(densityGapRatio, 0.025),
            "airy_group" => Math.Max(densityGapRatio, 0.12),
            _ => densityGapRatio
        };
        var unit = planned.Max(item => Math.Max(item.SourceWidth, item.SourceHeight));
        var gap = unit * gapRatio;
        var rowHeights = rows.Select(row => row.Max(index => planned[index].SourceHeight)).ToArray();
        var rowWidths = rows.Select(row => row.Sum(index => planned[index].SourceWidth) + gap * (row.Count - 1)).ToArray();
        var groupWidth = rowWidths.Max();
        var groupHeight = rowHeights.Sum() + gap * (rows.Count - 1);
        foreach (var relation in overlays)
        {
            var sourceIndex = FindIndex(planned, relation.SourceConceptId);
            var targetIndex = FindIndex(planned, relation.TargetConceptId);
            if (sourceIndex < 0 || targetIndex < 0) continue;
            groupWidth = Math.Max(groupWidth, planned[targetIndex].SourceWidth + planned[sourceIndex].SourceWidth * 0.35);
            groupHeight = Math.Max(groupHeight, Math.Max(planned[targetIndex].SourceHeight, planned[sourceIndex].SourceHeight * 1.25));
        }
        foreach (var relation in behind)
        {
            var sourceIndex = FindIndex(planned, relation.SourceConceptId);
            var targetIndex = FindIndex(planned, relation.TargetConceptId);
            if (sourceIndex < 0 || targetIndex < 0) continue;
            groupWidth = Math.Max(groupWidth, planned[targetIndex].SourceWidth + planned[sourceIndex].SourceWidth * 0.45);
            groupHeight = Math.Max(groupHeight, planned[targetIndex].SourceHeight + planned[sourceIndex].SourceHeight * 0.22);
        }
        foreach (var cluster in clusters.GroupBy(relation => relation.TargetConceptId, StringComparer.Ordinal))
        {
            var targetIndex = FindIndex(planned, cluster.Key);
            if (targetIndex < 0) continue;
            var sourceIndices = cluster.Select(relation => FindIndex(planned, relation.SourceConceptId)).Where(index => index >= 0).ToArray();
            if (sourceIndices.Length == 0) continue;
            groupWidth = Math.Max(groupWidth, planned[targetIndex].SourceWidth + sourceIndices.Max(index => planned[index].SourceWidth) + gap * 2);
            groupHeight = Math.Max(groupHeight, planned[targetIndex].SourceHeight + sourceIndices.Max(index => planned[index].SourceHeight) + gap * 2);
        }
        foreach (var group in lighting.GroupBy(relation => relation.TargetConceptId, StringComparer.Ordinal))
        {
            var targetIndex = FindIndex(planned, group.Key);
            if (targetIndex < 0) continue;
            var sourceIndices = group.Select(relation => FindIndex(planned, relation.SourceConceptId)).Where(index => index >= 0).ToArray();
            if (sourceIndices.Length == 0) continue;
            groupWidth = Math.Max(groupWidth, planned[targetIndex].SourceWidth + sourceIndices.Max(index => planned[index].SourceWidth) + gap);
            groupHeight = Math.Max(groupHeight, planned[targetIndex].SourceHeight + sourceIndices.Max(index => planned[index].SourceHeight) + gap * 2);
        }

        var fitWidth = width * (density switch { "airy" => 0.76, "rich" => 0.87, _ => 0.80 });
        var fitHeightRatio = density switch { "airy" => 0.70, "rich" => 0.82, _ => 0.76 };
        var fitHeight = height * (preset == "under-heading" ? Math.Min(0.58, fitHeightRatio) : fitHeightRatio);
        var scale = Math.Min(fitWidth / groupWidth, fitHeight / groupHeight) * 0.94;
        var scaledHeight = groupHeight * scale;
        var centerY = height * (preset == "under-heading" ? 0.68 : 0.53);
        var minY = preset == "under-heading" ? height * 0.32 : height * 0.12;
        var maxY = height * 0.90;
        centerY = Math.Clamp(centerY, minY + scaledHeight / 2, maxY - scaledHeight / 2);

        var result = new ScenePlacement[planned.Length];
        var rowCenters = PlanRowCenters(rows, planned, rowHeights, scale, gap * scale, minY, maxY, centerY, height);
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var itemWidths = row.Select(index => planned[index].SourceWidth * scale).ToArray();
            var positions = new double[row.Count];
            for (var itemPosition = 0; itemPosition < row.Count; itemPosition++)
            {
                var index = row[itemPosition];
                var item = planned[index];
                var itemWidth = itemWidths[itemPosition];
                var itemHeight = item.SourceHeight * scale;
                var halfWidth = itemWidth / 2;
                var remainingWidth = itemWidths.Skip(itemPosition + 1).Sum() + gap * scale * (row.Count - itemPosition - 1);
                var minimumX = itemPosition == 0 ? halfWidth + width * 0.03 : positions[itemPosition - 1] + itemWidths[itemPosition - 1] / 2 + gap * scale + halfWidth;
                var maximumX = width - width * 0.03 - halfWidth - remainingWidth;
                positions[itemPosition] = Math.Clamp(item.NormalizedX * width, minimumX, Math.Max(minimumX, maximumX));
                result[index] = new ScenePlacement(positions[itemPosition], rowCenters[rowIndex], itemWidth, itemHeight, item.Emphasis, item.RotationDegrees, DepthZOrder(item.Depth, item.Importance), Importance: item.Importance);
            }
            var sceneAnchorIndex = Enumerable.Range(0, planned.Length)
                .OrderByDescending(index => planned[index].Importance)
                .ThenByDescending(index => planned[index].Role == "main")
                .First();
            var anchorPosition = row.IndexOf(sceneAnchorIndex);
            var hasSceneAnchor = anchorPosition >= 0;
            if (!hasSceneAnchor)
                anchorPosition = row.IndexOf(row.OrderByDescending(index => planned[index].Importance)
                    .ThenByDescending(index => planned[index].Role == "main").First());
            var desiredCenter = hasSceneAnchor
                ? planned[sceneAnchorIndex].NormalizedX * width + (preset == "offset" ? width * 0.05 : 0)
                : row.Average(index => planned[index].NormalizedX) * width + (preset == "offset" ? width * 0.05 : 0);
            var actualCenter = hasSceneAnchor ? positions[anchorPosition] : positions.Average();
            var leftEdge = positions[0] - itemWidths[0] / 2;
            var rightEdge = positions[^1] + itemWidths[^1] / 2;
            var shift = Math.Clamp(desiredCenter - actualCenter, width * 0.03 - leftEdge, width * 0.97 - rightEdge);
            for (var itemPosition = 0; itemPosition < row.Count; itemPosition++)
            {
                var index = row[itemPosition];
                result[index] = result[index] with { X = result[index].X + shift };
            }
        }

        foreach (var cluster in clusters.GroupBy(relation => relation.TargetConceptId, StringComparer.Ordinal)
                     .OrderByDescending(group => planned[FindIndex(planned, group.Key)].Importance))
        {
            var targetIndex = FindIndex(planned, cluster.Key);
            if (targetIndex < 0 || result[targetIndex] is not { } targetPlacement) continue;
            var targetGeometry = planned[targetIndex];
            var groupRelations = cluster.ToArray();
            var occupied = new List<ScenePlacement> { targetPlacement };
            var usedAngles = new List<double>();
            for (var relationIndex = 0; relationIndex < groupRelations.Length; relationIndex++)
            {
                var relation = groupRelations[relationIndex];
                var sourceIndex = FindIndex(planned, relation.SourceConceptId);
                if (sourceIndex < 0 || sourceIndex == targetIndex) continue;
                var source = planned[sourceIndex];
                var dx = source.NormalizedX - targetGeometry.NormalizedX;
                var dy = source.NormalizedY - targetGeometry.NormalizedY;
                var angle = Math.Atan2(dy, dx);
                if (Math.Abs(dx) + Math.Abs(dy) < 0.08)
                    angle = -Math.PI / 2 + Math.PI * (relationIndex + 1) / (groupRelations.Length + 1);

                var childWidth = source.SourceWidth * scale;
                var childHeight = source.SourceHeight * scale;
                var candidate = new ScenePlacement(targetPlacement.X, targetPlacement.Y, childWidth, childHeight, source.Emphasis, source.RotationDegrees, Importance: source.Importance);
                var foundPosition = false;
                for (var attempt = 0; attempt < 16; attempt++)
                {
                    var candidateAngle = angle + attempt * Math.PI / 8;
                    if (usedAngles.Any(existing => AngularDistance(existing, candidateAngle) < Math.PI / 10)) continue;
                    var (x, y) = AdjacentCenter(targetPlacement, childWidth, childHeight, candidateAngle, gap * scale);
                    x = Math.Clamp(x, childWidth / 2 + width * 0.02, width - childWidth / 2 - width * 0.02);
                    y = Math.Clamp(y, childHeight / 2 + height * 0.02, height - childHeight / 2 - height * 0.02);
                    candidate = new ScenePlacement(x, y, childWidth, childHeight, source.Emphasis, source.RotationDegrees,
                        DepthZOrder(source.Depth, source.Importance), Importance: source.Importance);
                    if (occupied.All(existing => !Overlaps(candidate, existing)))
                    {
                        angle = candidateAngle;
                        foundPosition = true;
                        break;
                    }
                }
                if (!foundPosition)
                {
                    var (x, y) = AdjacentCenter(targetPlacement, childWidth, childHeight, angle, gap * scale);
                    candidate = candidate with
                    {
                        X = Math.Clamp(x, candidate.Width / 2 + width * 0.02, width - candidate.Width / 2 - width * 0.02),
                        Y = Math.Clamp(y, candidate.Height / 2 + height * 0.02, height - candidate.Height / 2 - height * 0.02),
                        ZOrder = DepthZOrder(source.Depth, source.Importance)
                    };
                }
                result[sourceIndex] = candidate;
                occupied.Add(candidate);
                usedAngles.Add(angle);
                clusterSources.Remove(relation.SourceConceptId);
            }
        }

        foreach (var relation in lighting)
        {
            var sourceIndex = FindIndex(planned, relation.SourceConceptId);
            var targetIndex = FindIndex(planned, relation.TargetConceptId);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex || result[targetIndex] is not { } target) continue;
            var source = planned[sourceIndex];
            var childWidth = source.SourceWidth * scale;
            var childHeight = source.SourceHeight * scale;
            var side = source.NormalizedX < planned[targetIndex].NormalizedX ? -1 : 1;
            result[sourceIndex] = new ScenePlacement(
                target.X + side * ((target.Width + childWidth) / 2 + gap * scale),
                target.Y - (target.Height + childHeight) / 2 - gap * scale,
                childWidth,
                childHeight,
                source.Emphasis,
                source.RotationDegrees,
                target.ZOrder - 0.2,
                Importance: source.Importance);
            lightingSources.Remove(relation.SourceConceptId);
        }

        foreach (var relation in overlays)
        {
            var sourceIndex = FindIndex(planned, relation.SourceConceptId);
            var targetIndex = FindIndex(planned, relation.TargetConceptId);
            if (sourceIndex < 0 || targetIndex < 0 || result[sourceIndex] is not null && !overlaySources.Contains(relation.SourceConceptId)) continue;
            var source = planned[sourceIndex];
            var target = result[targetIndex];
            if (target is null) continue;
            var child = new ScenePlacement(0, 0, source.SourceWidth * scale, source.SourceHeight * scale, source.Emphasis, source.RotationDegrees, Importance: source.Importance);
            var targetGeometry = planned[targetIndex];
            var localX = Math.Clamp(source.NormalizedX - targetGeometry.NormalizedX, -0.35, 0.35) * target.Width;
            var localY = Math.Clamp(source.NormalizedY - targetGeometry.NormalizedY, -0.35, 0.35) * target.Height;
            child = relation.Type switch
            {
                "rests_on" => child with { X = target.X + localX, Y = target.Y - target.Height * (0.08 + relation.Strength * 0.10) + localY * 0.35, ZOrder = target.ZOrder + 1, IntentionalOverlap = true },
                "inside" => child with { X = target.X + localX * 0.35, Y = target.Y + localY * 0.25, ZOrder = target.ZOrder + 1, IntentionalOverlap = true },
                _ => child with { X = target.X + localX, Y = target.Y + localY * 0.3 + target.Height * (relation.Strength - 0.5) * 0.15, ZOrder = target.ZOrder + 1, IntentionalOverlap = true }
            };
            child = child with
            {
                X = Math.Clamp(child.X, child.Width / 2 + width * 0.02, width - child.Width / 2 - width * 0.02),
                Y = Math.Clamp(child.Y, child.Height / 2 + height * 0.02, height - child.Height / 2 - height * 0.02)
            };
            result[sourceIndex] = child;
            overlaySources.Remove(relation.SourceConceptId);
        }

        foreach (var relation in behind)
        {
            var sourceIndex = FindIndex(planned, relation.SourceConceptId);
            var targetIndex = FindIndex(planned, relation.TargetConceptId);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) continue;
            var source = planned[sourceIndex];
            if (result[targetIndex] is not { } target) continue;
            var childWidth = source.SourceWidth * scale;
            var childHeight = source.SourceHeight * scale;
            var side = source.NormalizedX < planned[targetIndex].NormalizedX ? -1 : 1;
            var xOffset = (target.Width + childWidth) / 2 - childWidth * 0.60;
            var behindObject = new ScenePlacement(
                Math.Clamp(target.X + side * xOffset, childWidth / 2 + width * 0.02, width - childWidth / 2 - width * 0.02),
                Math.Clamp(target.Y + target.Height * 0.16, childHeight / 2 + height * 0.02, height - childHeight / 2 - height * 0.02),
                childWidth,
                childHeight,
                source.Emphasis,
                source.RotationDegrees,
                target.ZOrder - 0.1,
                IntentionalOverlap: true,
                Importance: source.Importance);
            result[sourceIndex] = behindObject;
            behindSources.Remove(relation.SourceConceptId);
        }
        ResolveAccidentalOverlaps(result, planned, compositionRelationships, width, height, gap * scale, intent);
        if (lighting.Length > 0) CenterAndFitScene(result, width, height);
        return result;
    }

    private static void CenterAndFitScene(ScenePlacement[] placements, double canvasWidth, double canvasHeight)
    {
        var minimumX = placements.Min(placement => placement.X - placement.Width / 2);
        var maximumX = placements.Max(placement => placement.X + placement.Width / 2);
        var minimumY = placements.Min(placement => placement.Y - placement.Height / 2);
        var maximumY = placements.Max(placement => placement.Y + placement.Height / 2);
        var desiredX = canvasWidth / 2 - (minimumX + maximumX) / 2;
        var desiredY = canvasHeight / 2 - (minimumY + maximumY) / 2;
        var shiftX = ClampSceneShift(desiredX, minimumX, maximumX, canvasWidth);
        var shiftY = ClampSceneShift(desiredY, minimumY, maximumY, canvasHeight);

        for (var index = 0; index < placements.Length; index++)
            placements[index] = placements[index] with { X = placements[index].X + shiftX, Y = placements[index].Y + shiftY };
    }

    private static double ClampSceneShift(double desired, double minimum, double maximum, double canvasSize)
    {
        var lowerBound = canvasSize * 0.02 - minimum;
        var upperBound = canvasSize * 0.98 - maximum;
        return lowerBound <= upperBound ? Math.Clamp(desired, lowerBound, upperBound) : desired;
    }

    private static SceneObjectGeometry[] ApplySpatialIntent(
        IReadOnlyList<SceneObjectGeometry> objects,
        string density,
        SceneCompositionIntent intent)
    {
        if (objects.Count < 2) return objects.ToArray();

        var anchorIndex = Enumerable.Range(0, objects.Count)
            .OrderByDescending(index => objects[index].Importance)
            .ThenByDescending(index => objects[index].Role == "main")
            .First();
        var anchor = objects[anchorIndex];
        var spread = intent.Grouping switch
        {
            "compact_group" => 0.48,
            "airy_group" => 1.0,
            _ => density switch { "airy" => 0.72, "rich" => 0.48, _ => 0.58 }
        };

        if (intent.Organization == "orderly_connected_work_zone") spread = Math.Min(spread, 0.50);
        if (intent.Organization == "irregular_but_readable" && intent.Grouping != "compact_group") spread = 1.0;

        var planned = objects.ToArray();
        for (var index = 0; index < planned.Length; index++)
        {
            if (index == anchorIndex) continue;
            planned[index] = planned[index] with
            {
                NormalizedX = anchor.NormalizedX + (planned[index].NormalizedX - anchor.NormalizedX) * spread,
                NormalizedY = anchor.NormalizedY + (planned[index].NormalizedY - anchor.NormalizedY) * spread
            };
        }
        return planned;
    }

    private static void ResolveAccidentalOverlaps(
        ScenePlacement[] placements,
        IReadOnlyList<SceneObjectGeometry> objects,
        IReadOnlyList<SceneRelationship> relationships,
        double canvasWidth,
        double canvasHeight,
        double gap,
        SceneCompositionIntent intent)
    {
        var allowedOverlapPairs = relationships
            .Where(relation => OverlayRelations.Contains(relation.Type) || BehindRelations.Contains(relation.Type))
            .Select(relation => (First: FindIndex(objects, relation.SourceConceptId), Second: FindIndex(objects, relation.TargetConceptId)))
            .Where(pair => pair.First >= 0 && pair.Second >= 0)
            .Select(pair => pair.First < pair.Second ? (pair.First, pair.Second) : (pair.Second, pair.First))
            .ToHashSet();

        var minimumGap = Math.Max(Math.Min(canvasWidth, canvasHeight) * 0.012, gap * 0.35);
        for (var pass = 0; pass < placements.Length * 2; pass++)
        {
            var changed = false;
            for (var firstIndex = 0; firstIndex < placements.Length; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < placements.Length; secondIndex++)
                {
                    if (allowedOverlapPairs.Contains((firstIndex, secondIndex)) || !Overlaps(placements[firstIndex], placements[secondIndex])) continue;

                    var moveOrder = ComparePriority(objects[firstIndex], objects[secondIndex]) <= 0
                        ? new[] { firstIndex, secondIndex }
                        : new[] { secondIndex, firstIndex };
                    foreach (var movingIndex in moveOrder)
                    {
                        if (!CanMove(movingIndex, objects, relationships)) continue;
                        var fixedIndex = movingIndex == firstIndex ? secondIndex : firstIndex;
                        if (!TryResolvePlacement(movingIndex, fixedIndex, placements, objects, relationships, allowedOverlapPairs,
                                canvasWidth, canvasHeight, minimumGap, intent)) continue;
                        changed = true;
                        break;
                    }
                }
            }
            if (!changed) break;
        }
    }

    private static bool TryResolvePlacement(
        int movingIndex,
        int fixedIndex,
        ScenePlacement[] placements,
        IReadOnlyList<SceneObjectGeometry> objects,
        IReadOnlyList<SceneRelationship> relationships,
        IReadOnlySet<(int, int)> allowedOverlapPairs,
        double canvasWidth,
        double canvasHeight,
        double gap,
        SceneCompositionIntent intent)
    {
        var moving = placements[movingIndex];
        var anchor = placements[fixedIndex];
        var xDistance = (moving.Width + anchor.Width) / 2 + gap;
        var yDistance = (moving.Height + anchor.Height) / 2 + gap;
        var horizontalFirst = intent.Organization == "orderly_connected_work_zone" || intent.Grouping == "compact_group";
        var directions = horizontalFirst
            ? new (double X, double Y)[] { (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1) }
            : new (double X, double Y)[] { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };
        var awayX = Math.Sign(moving.X - anchor.X);
        var awayY = Math.Sign(moving.Y - anchor.Y);
        if (awayX != 0 || awayY != 0)
            directions = directions.OrderByDescending(direction => direction.X * awayX + direction.Y * awayY).ToArray();

        foreach (var direction in directions)
        {
            var candidate = moving with
            {
                X = anchor.X + direction.X * xDistance,
                Y = anchor.Y + direction.Y * yDistance
            };
            if (candidate.X - candidate.Width / 2 < canvasWidth * 0.02 || candidate.X + candidate.Width / 2 > canvasWidth * 0.98 ||
                candidate.Y - candidate.Height / 2 < canvasHeight * 0.02 || candidate.Y + candidate.Height / 2 > canvasHeight * 0.98 ||
                !KeepsProximityRelations(movingIndex, candidate, placements, objects, relationships, canvasWidth, canvasHeight))
                continue;

            var collision = false;
            for (var otherIndex = 0; otherIndex < placements.Length; otherIndex++)
            {
                if (otherIndex == movingIndex) continue;
                var pair = movingIndex < otherIndex ? (movingIndex, otherIndex) : (otherIndex, movingIndex);
                if (!allowedOverlapPairs.Contains(pair) && Overlaps(candidate, placements[otherIndex]))
                {
                    collision = true;
                    break;
                }
            }
            if (collision) continue;

            placements[movingIndex] = candidate;
            return true;
        }
        return false;
    }

    private static bool KeepsProximityRelations(
        int movingIndex,
        ScenePlacement candidate,
        IReadOnlyList<ScenePlacement> placements,
        IReadOnlyList<SceneObjectGeometry> objects,
        IReadOnlyList<SceneRelationship> relationships,
        double canvasWidth,
        double canvasHeight)
    {
        var concept = objects[movingIndex].ConceptId;
        if (concept is null) return true;
        foreach (var relation in relationships.Where(relation => relation.SourceConceptId == concept &&
                     (ClusterRelations.Contains(relation.Type) || LightingRelations.Contains(relation.Type) || OverlayRelations.Contains(relation.Type))))
        {
            var targetIndex = FindIndex(objects, relation.TargetConceptId);
            if (targetIndex < 0) continue;
            var target = placements[targetIndex];
            if (OverlayRelations.Contains(relation.Type))
            {
                if (!Overlaps(candidate, target)) return false;
                continue;
            }
            var maximumDistance = (candidate.Width + target.Width + candidate.Height + target.Height) / 2 + Math.Max(canvasWidth, canvasHeight) * 0.06;
            if (Math.Sqrt(Math.Pow(candidate.X - target.X, 2) + Math.Pow(candidate.Y - target.Y, 2)) > maximumDistance)
                return false;
        }
        return true;
    }

    private static bool CanMove(int index, IReadOnlyList<SceneObjectGeometry> objects, IReadOnlyList<SceneRelationship> relationships)
    {
        var concept = objects[index].ConceptId;
        if (concept is null) return true;
        return !relationships.Any(relation =>
            (relation.TargetConceptId == concept) ||
            (relation.SourceConceptId == concept && BehindRelations.Contains(relation.Type)));
    }

    private static int ComparePriority(SceneObjectGeometry first, SceneObjectGeometry second)
    {
        var firstPriority = first.Importance * 2 + first.Emphasis * 0.1 + (first.Role == "main" ? 0.12 : 0);
        var secondPriority = second.Importance * 2 + second.Emphasis * 0.1 + (second.Role == "main" ? 0.12 : 0);
        return firstPriority.CompareTo(secondPriority);
    }

    private static double[] PlanRowCenters(
        IReadOnlyList<List<int>> rows,
        IReadOnlyList<SceneObjectGeometry> objects,
        IReadOnlyList<double> rowHeights,
        double scale,
        double gap,
        double minY,
        double maxY,
        double fallbackCenter,
        double canvasHeight)
    {
        var centers = new double[rows.Count];
        if (rows.Count == 0) return centers;
        var minimumCenter = minY;
        for (var index = 0; index < rows.Count; index++)
        {
            var halfHeight = rowHeights[index] * scale / 2;
            var ideal = rows[index].Average(itemIndex => objects[itemIndex].NormalizedY) * canvasHeight;
            var lower = minimumCenter + halfHeight;
            var upper = maxY - halfHeight;
            centers[index] = Math.Clamp(ideal, lower, upper);
            if (index > 0)
                centers[index] = Math.Max(centers[index], centers[index - 1] + rowHeights[index - 1] * scale / 2 + halfHeight + gap);
        }

        var overflow = centers[^1] + rowHeights[^1] * scale / 2 - maxY;
        if (overflow > 0)
            for (var index = 0; index < centers.Length; index++) centers[index] -= overflow;
        var underflow = minY - (centers[0] - rowHeights[0] * scale / 2);
        if (underflow > 0)
            for (var index = 0; index < centers.Length; index++) centers[index] += underflow;

        if (centers.Any(center => !double.IsFinite(center)))
            Array.Fill(centers, fallbackCenter);
        return centers;
    }

    private static SceneObjectGeometry[] ApplyRelationshipHints(IReadOnlyList<SceneObjectGeometry> objects, IReadOnlyList<SceneRelationship> relationships)
    {
        var planned = objects.ToArray();
        foreach (var relation in relationships)
        {
            var sourceIndex = FindIndex(planned, relation.SourceConceptId);
            var targetIndex = FindIndex(planned, relation.TargetConceptId);
            if (sourceIndex < 0 || targetIndex < 0 || OverlayRelations.Contains(relation.Type) || ClusterRelations.Contains(relation.Type)) continue;
            var source = planned[sourceIndex];
            var target = planned[targetIndex];
            var depth = relation.Type is "behind" or "illuminates" ? "far" : target.Depth;
            var side = source.NormalizedX == target.NormalizedX
                ? (sourceIndex % 2 == 0 ? -1 : 1)
                : source.NormalizedX < target.NormalizedX ? -1 : 1;
            var x = relation.Type == "behind" ? Math.Clamp(target.NormalizedX + side * 0.18, 0.12, 0.88) : Math.Clamp(target.NormalizedX + side * 0.20, 0.12, 0.88);
            planned[sourceIndex] = source with { Depth = depth, NormalizedX = x };
        }
        return planned;
    }

    private static int FindIndex(IReadOnlyList<SceneObjectGeometry> objects, string conceptId) =>
        Enumerable.Range(0, objects.Count).FirstOrDefault(index => objects[index].ConceptId == conceptId, -1);

    private static (double X, double Y) AdjacentCenter(ScenePlacement target, double childWidth, double childHeight, double angle, double gap)
    {
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var horizontalExtent = (target.Width + childWidth) / 2 + gap;
        var verticalExtent = (target.Height + childHeight) / 2 + gap;
        if (Math.Abs(cosine) < 0.25)
            return (target.X, target.Y + Math.Sign(sine) * verticalExtent);
        if (Math.Abs(sine) < 0.25)
            return (target.X + Math.Sign(cosine) * horizontalExtent, target.Y);

        var distance = Math.Max(horizontalExtent / Math.Abs(cosine), verticalExtent / Math.Abs(sine));
        return (target.X + cosine * distance, target.Y + sine * distance);
    }

    private static double AngularDistance(double first, double second)
    {
        var distance = Math.Abs(first - second) % (2 * Math.PI);
        return Math.Min(distance, 2 * Math.PI - distance);
    }

    private static bool Overlaps(ScenePlacement first, ScenePlacement second) =>
        Math.Abs(first.X - second.X) < (first.Width + second.Width) / 2 &&
        Math.Abs(first.Y - second.Y) < (first.Height + second.Height) / 2;

    private static List<List<int>> BuildRows(IReadOnlyList<SceneObjectGeometry> objects, IReadOnlyList<int> includedIndices, int maxColumns)
    {
        var rows = new List<List<int>>();
        var orderedIndices = includedIndices
            .OrderBy(index => objects[index].NormalizedY)
            .ThenBy(index => objects[index].NormalizedX)
            .ThenByDescending(index => objects[index].Importance)
            .ToArray();
        var row = new List<int>();
        double rowY = 0;
        foreach (var index in orderedIndices)
        {
            if (row.Count == maxColumns || row.Count > 0 && Math.Abs(objects[index].NormalizedY - rowY / row.Count) > 0.16)
            {
                rows.Add(row);
                row = [];
                rowY = 0;
            }
            row.Add(index);
            rowY += objects[index].NormalizedY;
        }
        if (row.Count > 0) rows.Add(row);
        return rows;
    }

    private static double DepthZOrder(string depth, double importance) =>
        Math.Max(0, Array.IndexOf(DepthOrder, depth)) * 10 + importance;
}
