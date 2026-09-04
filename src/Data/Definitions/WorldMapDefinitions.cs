using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Data.Definitions;

/// <summary>Immutable authored metadata for one selectable world-map region.</summary>
public sealed record RegionDefinition(
    string Id,
    string DisplayName,
    string ShortDescription,
    string Story,
    string MapContentId,
    string? MapDefinitionFile,
    string? ScenePath,
    Vec2 WorldMapPosition,
    double WorldMapRadius,
    string Biome,
    bool StarterCandidate,
    bool Available,
    LevelRangeDefinition? RecommendedLevelRange,
    string DefaultSpawnId,
    IReadOnlyList<string> TravelConditionIds,
    IReadOnlyList<string> Tags);

public sealed record WorldMapDefinitions(
    int SchemaVersion,
    string StarterRegionId,
    IReadOnlyDictionary<string, RegionDefinition> Regions)
{
    public RegionDefinition Region(string id) => Regions.TryGetValue(id, out var value)
        ? value
        : throw new KeyNotFoundException($"Unknown region '{id}'.");
}

public static partial class WorldMapDefinitionLoader
{
    public static WorldMapDefinitions Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var schema = Integer(root, "schemaVersion", 1);
            var starter = Text(root, "starterRegionId");
            var regionsInput = Array(root, "regions");
            var regions = new Dictionary<string, RegionDefinition>(StringComparer.Ordinal);
            foreach (var item in regionsInput.EnumerateArray())
            {
                var id = Text(item, "id");
                if (!RegionIdRegex().IsMatch(id)) throw new DefinitionException($"Invalid region ID '{id}'.");
                var recommended = ParseRecommendedLevel(item);
                var mapDefinitionFile = OptionalSafeRelativePath(item, "mapDefinitionFile");
                var scenePath = OptionalText(item, "scenePath");
                var conditions = OptionalStringArray(item, "travelConditionIds");
                var tags = OptionalStringArray(item, "tags");
                var region = new RegionDefinition(
                    id,
                    Text(item, "displayName"),
                    Text(item, "shortDescription"),
                    Text(item, "story"),
                    Text(item, "mapContentId"),
                    mapDefinitionFile,
                    scenePath,
                    Vector(Object(item, "worldMapPosition")),
                    Number(item, "worldMapRadius", 1),
                    Text(item, "biome"),
                    Boolean(item, "starterCandidate"),
                    Boolean(item, "available"),
                    recommended,
                    Text(item, "defaultSpawnId"),
                    conditions,
                    tags);
                if (region.WorldMapPosition.X < -1 || region.WorldMapPosition.X > 1 || region.WorldMapPosition.Y < -1 || region.WorldMapPosition.Y > 1)
                    throw new DefinitionException($"Region '{id}' worldMapPosition must be normalized within [-1, 1].");
                if (region.Available && region.MapDefinitionFile is null)
                    throw new DefinitionException($"Available region '{id}' must provide mapDefinitionFile.");
                if (!regions.TryAdd(id, region)) throw new DefinitionException($"Duplicate region ID '{id}'.");
            }

            if (!regions.TryGetValue(starter, out var starterRegion)) throw new DefinitionException($"Starter region '{starter}' is not registered.");
            if (!starterRegion.StarterCandidate) throw new DefinitionException($"Starter region '{starter}' must be marked starterCandidate.");
            if (!starterRegion.Available || starterRegion.MapDefinitionFile is null) throw new DefinitionException($"Starter region '{starter}' must have playable content.");
            return new WorldMapDefinitions(schema, starter, new ReadOnlyDictionary<string, RegionDefinition>(regions));
        }
        catch (DefinitionException) { throw; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new DefinitionException($"Cannot load world map definitions from '{path}'.", exception);
        }
    }

    private static LevelRangeDefinition? ParseRecommendedLevel(JsonElement item)
    {
        if (!item.TryGetProperty("recommendedLevelRange", out var input)) return null;
        var range = new LevelRangeDefinition(Integer(input, "min", 1), Integer(input, "max", 1));
        if (range.Maximum < range.Minimum) throw new DefinitionException("recommendedLevelRange.max must be >= min.");
        return range;
    }

    private static string? OptionalSafeRelativePath(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var input) || input.ValueKind == JsonValueKind.Null) return null;
        if (input.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(input.GetString())) throw new DefinitionException($"'{property}' must be a non-empty string when present.");
        var value = input.GetString()!;
        var normalized = value.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Contains("..", StringComparer.Ordinal) || normalized.StartsWith('/')) throw new DefinitionException($"Region '{Text(item, "id")}' has an unsafe {property}.");
        return normalized;
    }

    private static IReadOnlyList<string> OptionalStringArray(JsonElement item, string property) =>
        !item.TryGetProperty(property, out var value) ? System.Array.Empty<string>() : value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(entry => entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString()))
            ? value.EnumerateArray().Select(entry => entry.GetString()!).Distinct(StringComparer.Ordinal).ToArray()
            : throw new DefinitionException($"'{property}' must be an array of non-empty strings.");

    private static JsonElement Object(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new DefinitionException($"'{property}' must be an object.");
    private static JsonElement Array(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new DefinitionException($"'{property}' must be an array.");
    private static string Text(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new DefinitionException($"Missing non-empty string '{property}'.");
    private static string? OptionalText(JsonElement item, string property) => !item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null ? null : value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : throw new DefinitionException($"'{property}' must be a non-empty string when present.");
    private static int Integer(JsonElement item, string property, int minimum) => item.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be an integer >= {minimum}.");
    private static double Number(JsonElement item, string property, double minimum) => item.TryGetProperty(property, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be a finite number >= {minimum}.");
    private static bool Boolean(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new DefinitionException($"'{property}' must be boolean.");
    private static Vec2 Vector(JsonElement item) => new(Number(item, "x", double.NegativeInfinity), Number(item, "y", double.NegativeInfinity));

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex RegionIdRegex();
}
