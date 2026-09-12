using System.Collections.ObjectModel;
using System.Text.Json;
using Godot;

namespace SoloVsMortal.Presentation;

/// <summary>
/// Presentation-only physical measurements. Canvas dimensions are deliberately
/// separate from the intended on-map footprint: transparent padding is not a
/// statement about the size of a grave, shrine, or character.
/// </summary>
public sealed record PresentationVisualMetric(
    string AssetId,
    Vector2I CanvasSize,
    Rect2I OpaqueBounds,
    Vector2 GroundPivot,
    Vector2 IntendedFootprint,
    Rect2? CollisionFootprint)
{
    public float UniformScale => Mathf.Min(IntendedFootprint.X / OpaqueBounds.Size.X, IntendedFootprint.Y / OpaqueBounds.Size.Y);
}

public sealed record WorldScaleCategory(string Id, string Label, float MinHeightRatio, float MaxHeightRatio);

public sealed record WorldScaleSpecies(string SpeciesId, string Representation, int MinRank, int MaxRank, float VisualHeightRatio, string Category);

public sealed record WorldScaleObject(string AssetId, string Class, float VisualHeightRatio, Vector2 GroundFootprintTiles, string Anchor, float? ScaleOverride);

public sealed record WorldScaleOverride(string AssetId, float VisualHeightRatio);

public sealed record WorldScalePolicy(
    int PlayerBaselineVisibleHeightPx,
    IReadOnlyList<WorldScaleCategory> Categories,
    IReadOnlyList<WorldScaleSpecies> Species,
    IReadOnlyDictionary<string, WorldScaleObject> WorldObjects,
    IReadOnlyList<WorldScaleOverride> Overrides,
    string Status);

public sealed class PresentationVisualMetrics
{
    private const float InstanceOverrideMin = 0.8f;
    private const float InstanceOverrideMax = 1.2f;
    private readonly IReadOnlyDictionary<string, PresentationVisualMetric> _metrics;
    private readonly WorldScalePolicy _policy;
    private readonly HashSet<string> _overrideOverrideWarned = new(StringComparer.Ordinal);

    private PresentationVisualMetrics(int baseTileSize, IReadOnlyDictionary<string, PresentationVisualMetric> metrics, WorldScalePolicy policy)
    {
        BaseTileSize = baseTileSize;
        _metrics = metrics;
        _policy = policy;
    }

    public int BaseTileSize { get; }
    public WorldScalePolicy Policy => _policy;
    public bool TryGet(string assetId, out PresentationVisualMetric metric) => _metrics.TryGetValue(assetId, out metric!);
    public float ScaleFor(string assetId, float fallback = 1f) => TryGet(assetId, out var metric) ? metric.UniformScale : fallback;

    public float VisibleHeightFor(string assetId, float fallback = 0f) => TryResolveRatio(assetId, out var ratio) ? _policy.PlayerBaselineVisibleHeightPx * ratio : fallback;

    public float VisualScaleFor(string assetId, float fallback = 1f)
    {
        if (!TryResolveRatio(assetId, out var ratio) || !TryGet(assetId, out var metric)) return fallback;
        var intendedHeight = _policy.PlayerBaselineVisibleHeightPx * ratio;
        // New art is authored at its final integer raster height. Preserve its texel grid
        // when the physical policy rounds by at most half a pixel; do not stretch it again.
        return Math.Abs(intendedHeight - metric.OpaqueBounds.Size.Y) <= 0.5f
            ? 1f : intendedHeight / metric.OpaqueBounds.Size.Y;
    }

    public Vector2 GroundFootprintFor(string assetId, Vector2 fallback) =>
        _policy.WorldObjects.TryGetValue(assetId, out var obj) ? obj.GroundFootprintTiles * BaseTileSize : fallback;

    public float ResolveWorldScale(string assetId, float instanceOverride)
    {
        var clamped = Mathf.Clamp(instanceOverride, InstanceOverrideMin, InstanceOverrideMax);
        if (Math.Abs(clamped - instanceOverride) > 0.0001f && _overrideOverrideWarned.Add(assetId))
            GD.PushWarning($"WORLD_SCALE_OVERRIDE asset '{assetId}' instance override {instanceOverride:0.###} is outside safe range {InstanceOverrideMin:0.#}-{InstanceOverrideMax:0.#}; clamped to {clamped:0.###}.");
        return VisualScaleFor(assetId) * clamped;
    }

    public IReadOnlyList<string> ValidateWorldScalePolicy()
    {
        var issues = new List<string>();
        var categories = _policy.Categories.ToDictionary(category => category.Id, StringComparer.Ordinal);
        foreach (var obj in _policy.WorldObjects.Values)
        {
            if (!categories.TryGetValue(obj.Class, out var category))
            {
                issues.Add($"WORLD_SCALE_POLICY UNKNOWN_CLASS asset '{obj.AssetId}' class '{obj.Class}' not declared in categories.");
                continue;
            }
            if (obj.VisualHeightRatio < category.MinHeightRatio || obj.VisualHeightRatio > category.MaxHeightRatio)
                issues.Add($"WORLD_SCALE_POLICY RATIO_RANGE asset '{obj.AssetId}' ratio {obj.VisualHeightRatio:0.00} outside class '{obj.Class}' [{category.MinHeightRatio:0.00}, {category.MaxHeightRatio:0.00}].");
            if (obj.Class == "architectural_prop" && obj.ScaleOverride is null && obj.VisualHeightRatio >= 0.9f && obj.VisualHeightRatio <= 1.1f)
                issues.Add($"WORLD_SCALE_POLICY ARCHITECTURAL_PROP_NEAR_PLAYER asset '{obj.AssetId}' ratio {obj.VisualHeightRatio:0.00} approximates player (1.00) without an explicit override.");
        }
        foreach (var species in _policy.Species)
        {
            if (!categories.TryGetValue(species.Category, out var category))
            {
                issues.Add($"WORLD_SCALE_POLICY UNKNOWN_CATEGORY species '{species.SpeciesId}/{species.Representation}' category '{species.Category}' not declared in categories.");
                continue;
            }
            if (species.VisualHeightRatio < category.MinHeightRatio || species.VisualHeightRatio > category.MaxHeightRatio)
                issues.Add($"WORLD_SCALE_POLICY RATIO_RANGE species '{species.SpeciesId}/{species.Representation}' rank [{species.MinRank}, {species.MaxRank}] ratio {species.VisualHeightRatio:0.00} outside category '{species.Category}' [{category.MinHeightRatio:0.00}, {category.MaxHeightRatio:0.00}].");
        }
        foreach (var overrideEntry in _policy.Overrides)
        {
            if (overrideEntry.VisualHeightRatio <= 0f)
                issues.Add($"WORLD_SCALE_POLICY INVALID_OVERRIDE asset '{overrideEntry.AssetId}' override ratio {overrideEntry.VisualHeightRatio:0.00} must be positive.");
        }
        return issues;
    }

    public static PresentationVisualMetrics Load(string manifestPath, string policyPath, CanonicalAssetCatalog catalog)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        Require(root, "schemaVersion", JsonValueKind.Number).GetInt32().ThrowUnless(1, "Unsupported presentation visual metrics schema.");
        var baseTileSize = Require(root, "baseTileSize", JsonValueKind.Number).GetInt32();
        if (baseTileSize != 32) throw new InvalidDataException("Presentation visual metrics require BASE_TILE=32.");
        var values = Require(root, "assets", JsonValueKind.Array);
        var metrics = new Dictionary<string, PresentationVisualMetric>(StringComparer.Ordinal);
        foreach (var item in values.EnumerateArray())
        {
            var assetId = Require(item, "assetId", JsonValueKind.String).GetString() ?? throw new InvalidDataException("Metric assetId cannot be empty.");
            if (!catalog.TryGet(assetId, out var asset)) throw new InvalidDataException($"Presentation metric references absent canonical asset '{assetId}'.");
            var canvas = Pair(Require(item, "canvasSize", JsonValueKind.Array), "canvasSize", 1);
            if (canvas != asset.FrameSize) throw new InvalidDataException($"Metric canvas for '{assetId}' differs from its canonical frame size.");
            var opaque = Quad(Require(item, "opaqueBounds", JsonValueKind.Array), "opaqueBounds");
            if (opaque.Position.X < 0 || opaque.Position.Y < 0 || opaque.End.X > canvas.X || opaque.End.Y > canvas.Y)
                throw new InvalidDataException($"Metric opaque bounds for '{assetId}' are outside its canvas.");
            var pivot = Pair(Require(item, "groundPivot", JsonValueKind.Array), "groundPivot", 0);
            if (new Vector2(pivot.X, pivot.Y) != asset.Pivot) throw new InvalidDataException($"Metric pivot for '{assetId}' differs from canonical provenance.");
            var footprint = Pair(Require(item, "intendedFootprint", JsonValueKind.Array), "intendedFootprint", 1);
            Rect2? collision = null;
            if (item.TryGetProperty("collisionFootprint", out var collisionValue) && collisionValue.ValueKind != JsonValueKind.Null)
            {
                var rectangle = Quad(collisionValue, "collisionFootprint");
                collision = new Rect2(rectangle.Position, rectangle.Size);
            }
            var metric = new PresentationVisualMetric(assetId, canvas, opaque, new Vector2(pivot.X, pivot.Y), new Vector2(footprint.X, footprint.Y), collision);
            if (!metrics.TryAdd(assetId, metric)) throw new InvalidDataException($"Duplicate presentation metric '{assetId}'.");
        }
        var policy = LoadPolicy(policyPath, catalog, metrics);
        return new PresentationVisualMetrics(baseTileSize, new ReadOnlyDictionary<string, PresentationVisualMetric>(metrics), policy);
    }

    private static WorldScalePolicy LoadPolicy(string policyPath, CanonicalAssetCatalog catalog, IReadOnlyDictionary<string, PresentationVisualMetric> metrics)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(policyPath));
        var root = document.RootElement;
        Require(root, "schemaVersion", JsonValueKind.Number).GetInt32().ThrowUnless(1, "Unsupported world scale policy schema.");
        var baseline = Require(root, "playerBaselineVisibleHeightPx", JsonValueKind.Number).GetInt32();
        if (baseline <= 0) throw new InvalidDataException("World scale policy requires a positive player baseline visible height.");
        var categories = new List<WorldScaleCategory>();
        foreach (var item in Require(root, "categories", JsonValueKind.Array).EnumerateArray())
        {
            var id = Require(item, "id", JsonValueKind.String).GetString() ?? throw new InvalidDataException("World scale category id cannot be empty.");
            var label = item.TryGetProperty("label", out var labelValue) && labelValue.ValueKind == JsonValueKind.String ? labelValue.GetString() : id;
            var min = Require(item, "minHeightRatio", JsonValueKind.Number).GetSingle();
            var max = Require(item, "maxHeightRatio", JsonValueKind.Number).GetSingle();
            if (min <= 0f || max < min) throw new InvalidDataException($"World scale category '{id}' requires 0 < minHeightRatio <= maxHeightRatio.");
            categories.Add(new WorldScaleCategory(id, label ?? id, min, max));
        }
        var categoriesById = categories.ToDictionary(category => category.Id, StringComparer.Ordinal);
        var species = new List<WorldScaleSpecies>();
        foreach (var item in Require(root, "species", JsonValueKind.Array).EnumerateArray())
        {
            var speciesId = Require(item, "speciesId", JsonValueKind.String).GetString() ?? throw new InvalidDataException("World scale speciesId cannot be empty.");
            var representation = Require(item, "representation", JsonValueKind.String).GetString() ?? throw new InvalidDataException("World scale species representation cannot be empty.");
            var rankRange = Require(item, "rankRange", JsonValueKind.Array);
            if (rankRange.GetArrayLength() != 2 || !rankRange[0].TryGetInt32(out var minRank) || !rankRange[1].TryGetInt32(out var maxRank) || minRank < 1 || maxRank < minRank)
                throw new InvalidDataException($"World scale species '{speciesId}/{representation}' requires a valid rankRange.");
            var ratio = Require(item, "visualHeightRatio", JsonValueKind.Number).GetSingle();
            if (ratio <= 0f) throw new InvalidDataException($"World scale species '{speciesId}/{representation}' requires a positive visualHeightRatio.");
            var category = Require(item, "category", JsonValueKind.String).GetString() ?? throw new InvalidDataException($"World scale species '{speciesId}/{representation}' requires a category.");
            if (!categoriesById.ContainsKey(category)) throw new InvalidDataException($"World scale species '{speciesId}/{representation}' references unknown category '{category}'.");
            species.Add(new WorldScaleSpecies(speciesId, representation, minRank, maxRank, ratio, category));
        }
        var worldObjects = new Dictionary<string, WorldScaleObject>(StringComparer.Ordinal);
        foreach (var item in Require(root, "worldObjects", JsonValueKind.Array).EnumerateArray())
        {
            var assetId = Require(item, "assetId", JsonValueKind.String).GetString() ?? throw new InvalidDataException("World scale object assetId cannot be empty.");
            if (!catalog.TryGet(assetId, out _)) throw new InvalidDataException($"World scale policy references absent canonical asset '{assetId}'.");
            if (!metrics.ContainsKey(assetId)) throw new InvalidDataException($"World scale policy object '{assetId}' has no measurement facts.");
            var className = Require(item, "class", JsonValueKind.String).GetString() ?? throw new InvalidDataException($"World scale object '{assetId}' requires a class.");
            if (!categoriesById.ContainsKey(className)) throw new InvalidDataException($"World scale object '{assetId}' references unknown class '{className}'.");
            var ratio = Require(item, "visualHeightRatio", JsonValueKind.Number).GetSingle();
            if (ratio <= 0f) throw new InvalidDataException($"World scale object '{assetId}' requires a positive visualHeightRatio.");
            var tiles = FloatPair(Require(item, "groundFootprintTiles", JsonValueKind.Array), "groundFootprintTiles", 0f);
            var anchor = item.TryGetProperty("anchor", out var anchorValue) && anchorValue.ValueKind == JsonValueKind.String ? anchorValue.GetString() : "feet_or_base";
            float? scaleOverride = null;
            if (item.TryGetProperty("scaleOverride", out var overrideValue) && overrideValue.ValueKind == JsonValueKind.Number)
                scaleOverride = overrideValue.GetSingle();
            var entry = new WorldScaleObject(assetId, className, ratio, new Vector2(tiles.X, tiles.Y), anchor ?? "feet_or_base", scaleOverride);
            if (!worldObjects.TryAdd(assetId, entry)) throw new InvalidDataException($"Duplicate world scale object '{assetId}'.");
        }
        var overrides = new List<WorldScaleOverride>();
        if (root.TryGetProperty("overrides", out var overridesValue) && overridesValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in overridesValue.EnumerateArray())
            {
                var assetId = Require(item, "assetId", JsonValueKind.String).GetString() ?? throw new InvalidDataException("World scale override assetId cannot be empty.");
                var ratio = Require(item, "visualHeightRatio", JsonValueKind.Number).GetSingle();
                if (ratio <= 0f) throw new InvalidDataException($"World scale override '{assetId}' requires a positive visualHeightRatio.");
                overrides.Add(new WorldScaleOverride(assetId, ratio));
            }
        }
        var status = root.TryGetProperty("status", out var statusValue) && statusValue.ValueKind == JsonValueKind.String ? statusValue.GetString() : "proposed";
        return new WorldScalePolicy(baseline, categories, species, new ReadOnlyDictionary<string, WorldScaleObject>(worldObjects), overrides, status ?? "proposed");
    }

    private bool TryResolveRatio(string assetId, out float ratio)
    {
        if (_policy.WorldObjects.TryGetValue(assetId, out var obj))
        {
            ratio = obj.VisualHeightRatio;
            return true;
        }
        foreach (var overrideEntry in _policy.Overrides)
        {
            if (overrideEntry.AssetId == assetId)
            {
                ratio = overrideEntry.VisualHeightRatio;
                return true;
            }
        }
        var species = ResolveSpecies(assetId);
        if (species is not null)
        {
            ratio = species.VisualHeightRatio;
            return true;
        }
        ratio = 0f;
        return false;
    }

    private WorldScaleSpecies? ResolveSpecies(string assetId)
    {
        if (assetId.StartsWith("player.base.", StringComparison.Ordinal))
            return _policy.Species.FirstOrDefault(species => species.SpeciesId == "player");
        var segments = assetId.Split('.');
        if (segments.Length < 5 || segments[0] != "soul") return null;
        var speciesId = segments[1];
        if (!segments[2].StartsWith("rank", StringComparison.Ordinal) || !int.TryParse(segments[2].AsSpan(4), out var rank)) return null;
        var representation = segments[3];
        return _policy.Species.FirstOrDefault(species => species.SpeciesId == speciesId && species.Representation == representation && rank >= species.MinRank && rank <= species.MaxRank);
    }

    private static JsonElement Require(JsonElement item, string name, JsonValueKind kind) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : throw new InvalidDataException($"Presentation visual metrics requires '{name}'.");

    private static Vector2I Pair(JsonElement values, string name, int minimum)
    {
        if (values.GetArrayLength() != 2 || !values[0].TryGetInt32(out var x) || !values[1].TryGetInt32(out var y) || x < minimum || y < minimum)
            throw new InvalidDataException($"Presentation visual metrics '{name}' must be an integer pair >= {minimum}.");
        return new Vector2I(x, y);
    }

    private static Vector2 FloatPair(JsonElement values, string name, float minimum)
    {
        if (values.GetArrayLength() != 2 || !values[0].TryGetSingle(out var x) || !values[1].TryGetSingle(out var y) || !float.IsFinite(x) || !float.IsFinite(y) || x < minimum || y < minimum)
            throw new InvalidDataException($"World scale policy '{name}' must be a finite float pair >= {minimum}.");
        return new Vector2(x, y);
    }

    private static Rect2I Quad(JsonElement values, string name)
    {
        if (values.GetArrayLength() != 4 || !values[0].TryGetInt32(out var x) || !values[1].TryGetInt32(out var y) || !values[2].TryGetInt32(out var width) || !values[3].TryGetInt32(out var height) || width <= 0 || height <= 0)
            throw new InvalidDataException($"Presentation visual metrics '{name}' must be [x, y, width, height].");
        return new Rect2I(x, y, width, height);
    }
}

internal static class PresentationVisualMetricExtensions
{
    internal static void ThrowUnless(this int value, int expected, string message)
    {
        if (value != expected) throw new InvalidDataException(message);
    }
}
