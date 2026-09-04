using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Data.Definitions;

public sealed record MapCameraDefinition(Rect Bounds, double DefaultZoom, Vec2? FollowOffset);
public sealed record MapTileDefinition(double X, double Y, string AssetId, bool Collision);
public sealed record MapLayerDefinition(string Id, string Type, int ZIndex, IReadOnlyList<MapTileDefinition> Tiles, string? ImageAssetId);
public sealed record CollisionFootprintDefinition(double OffsetX, double OffsetY, double Width, double Height);
public sealed record MapInteractionDefinition(string Id, string DisplayName, string RequiredCapabilityId, double Radius, string Outcome);
public sealed record MapObjectDefinition(string Id, string AssetId, string Type, Vec2 Position, string LayerId, bool Blocking, CollisionFootprintDefinition? Collision, MapInteractionDefinition? Interaction, string? ZoneId = null, double PresentationScale = 1);
public sealed record MapSpawnDefinition(string Id, Vec2 Position, string? Facing);
public sealed record MapExitDefinition(string Id, string TargetMapId, string TargetSpawnId, Rect TriggerArea, string TransitionType);
public sealed record MapZoneDefinition(string Id, string Type, Rect Bounds, IReadOnlyList<string> PlacementRuleIds);
public sealed record MapPlacementRuleDefinition(string Id, string Category, double MinimumSpacing, double SpawnClearance);
public sealed record MapDefinition(
    string Id, string DisplayName, string Type, double Width, double Height, int TileSize,
    MapCameraDefinition Camera, string? BackgroundAssetId, IReadOnlyList<string> TerrainAssetIds, IReadOnlyList<string> ObjectAssetIds,
    IReadOnlyDictionary<string, MapLayerDefinition> Layers, IReadOnlyDictionary<string, MapObjectDefinition> Objects,
    IReadOnlyList<string> BlockedObjectIds, IReadOnlyList<MapSpawnDefinition> SpawnPoints, IReadOnlyList<MapExitDefinition> Exits,
    IReadOnlyDictionary<string, MapZoneDefinition> Zones, IReadOnlyDictionary<string, MapPlacementRuleDefinition> PlacementRules,
    IReadOnlyDictionary<string, double> ObjectScaleDefaults, double SpawnSafetyRadius)
{
    public MapSpawnDefinition Spawn(string id) => SpawnPoints.FirstOrDefault(item => item.Id == id)
        ?? throw new KeyNotFoundException($"Unknown spawn point '{id}' in map '{Id}'.");
}

public static partial class MapDefinitionLoader
{
    public static MapDefinition Load(string path, AssetDefinitions assets, SoulNatureDefinitions soulNatures)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var id = Text(root, "id");
            if (!MapIdRegex().IsMatch(id)) throw new DefinitionException($"Invalid map ID '{id}'.");
            var dimensions = Object(root, "dimensions");
            var width = Number(dimensions, "width", 1);
            var height = Number(dimensions, "height", 1);
            var cameraInput = Object(root, "cameraSettings");
            var camera = new MapCameraDefinition(Rectangle(Object(cameraInput, "bounds")), Number(cameraInput, "defaultZoom", 0.01), OptionalVector(cameraInput, "followOffset"));
            var assetInput = Object(root, "assets");
            var backgroundAssetId = assetInput.TryGetProperty("background", out var background) ? StringValue(background, "assets.background") : null;
            if (backgroundAssetId is not null) _ = assets.Get(backgroundAssetId);
            var terrain = AssetIdArray(assetInput, "terrain", assets);
            var objectAssets = AssetIdArray(assetInput, "objects", assets);
            var layers = ParseLayers(root, assets);
            var zones = ParseZones(root, width, height);
            var placementRules = ParsePlacementRules(root);
            ValidateZoneRules(zones, placementRules);
            var scales = ParseObjectScales(root);
            var objects = ParseObjects(root, layers, zones, scales, assets, soulNatures);
            var blockedObjects = ParseCollision(root, objects);
            var spawns = ParseSpawns(root, width, height);
            var exits = ParseExits(root, width, height);
            var spawnSafetyRadius = ParseSpawnSafetyRadius(root);
            ValidatePlacement(objects, blockedObjects, spawns, zones, placementRules, width, height, spawnSafetyRadius);
            return new MapDefinition(id, Text(root, "displayName"), Text(root, "type"), width, height, Integer(root, "tileSize", 1), camera, backgroundAssetId,
                terrain, objectAssets, layers, objects, blockedObjects, spawns, exits, zones, placementRules, scales, spawnSafetyRadius);
        }
        catch (DefinitionException) { throw; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new DefinitionException($"Cannot load map definition from '{path}'.", exception);
        }
    }

    private static IReadOnlyDictionary<string, MapLayerDefinition> ParseLayers(JsonElement root, AssetDefinitions assets)
    {
        var result = new Dictionary<string, MapLayerDefinition>(StringComparer.Ordinal);
        foreach (var item in Array(root, "layers").EnumerateArray())
        {
            var id = Text(item, "id");
            var type = Text(item, "type");
            if (type is not ("tile" or "image" or "object")) throw new DefinitionException($"Invalid layer type '{type}'.");
            var tiles = new List<MapTileDefinition>();
            if (type == "tile")
            {
                foreach (var tile in Array(item, "tiles").EnumerateArray())
                {
                    var assetId = Text(tile, "assetKey"); _ = assets.Get(assetId);
                    tiles.Add(new MapTileDefinition(Number(tile, "x", double.NegativeInfinity), Number(tile, "y", double.NegativeInfinity), assetId, OptionalBoolean(tile, "collision")));
                }
                if (tiles.Count == 0) throw new DefinitionException($"Tile layer '{id}' cannot be empty.");
            }
            string? imageAsset = null;
            if (type == "image") { var image = Object(item, "image"); imageAsset = Text(image, "assetKey"); _ = assets.Get(imageAsset); }
            if (!result.TryAdd(id, new MapLayerDefinition(id, type, Integer(item, "zIndex", 0), tiles.AsReadOnly(), imageAsset))) throw new DefinitionException($"Duplicate map layer '{id}'.");
        }
        return new ReadOnlyDictionary<string, MapLayerDefinition>(result);
    }

    private static IReadOnlyDictionary<string, MapObjectDefinition> ParseObjects(JsonElement root, IReadOnlyDictionary<string, MapLayerDefinition> layers, IReadOnlyDictionary<string, MapZoneDefinition> zones, IReadOnlyDictionary<string, double> scales, AssetDefinitions assets, SoulNatureDefinitions soulNatures)
    {
        var result = new Dictionary<string, MapObjectDefinition>(StringComparer.Ordinal);
        var interactionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Array(root, "objects").EnumerateArray())
        {
            var id = Text(item, "id"); var assetId = Text(item, "assetKey"); _ = assets.Get(assetId);
            var layer = Text(item, "layer"); if (!layers.ContainsKey(layer)) throw new DefinitionException($"Object '{id}' references unknown layer '{layer}'.");
            CollisionFootprintDefinition? collision = null;
            if (item.TryGetProperty("collision", out var collisionInput))
            {
                if (Text(collisionInput, "shape") != "rectangle") throw new DefinitionException($"Object '{id}' collision must be rectangle.");
                collision = new CollisionFootprintDefinition(Number(collisionInput, "offsetX", double.NegativeInfinity), Number(collisionInput, "offsetY", double.NegativeInfinity), Number(collisionInput, "width", 1), Number(collisionInput, "height", 1));
            }
            MapInteractionDefinition? interaction = null;
            if (item.TryGetProperty("interaction", out var interactionInput))
            {
                var interactionId = Text(interactionInput, "id");
                if (!interactionIds.Add(interactionId)) throw new DefinitionException($"Duplicate map interaction ID '{interactionId}'.");
                var capability = Text(interactionInput, "requiredCapabilityId");
                if (!soulNatures.Capabilities.ContainsKey(capability)) throw new DefinitionException($"Interaction '{interactionId}' references unknown capability '{capability}'.");
                var outcome = Text(interactionInput, "outcome");
                if (outcome != "DESTROY_OBJECT") throw new DefinitionException($"Unsupported interaction outcome '{outcome}'.");
                interaction = new MapInteractionDefinition(interactionId, Text(interactionInput, "displayName"), capability, Number(interactionInput, "interactionRadius", 0), outcome);
            }
            var zoneId = OptionalText(item, "zoneId");
            if (zoneId is not null && !zones.ContainsKey(zoneId)) throw new DefinitionException($"Object '{id}' references unknown zone '{zoneId}'.");
            var objectType = Text(item, "type");
            if (zoneId is not null && !zones[zoneId].Bounds.Contains(Vector(Object(item, "position")).X, Vector(Object(item, "position")).Y)) throw new DefinitionException($"Object '{id}' lies outside zone '{zoneId}'.");
            var scale = item.TryGetProperty("scale", out var scaleInput) ? NumberValue(scaleInput, "scale", 0.01) : scales.GetValueOrDefault(objectType, 1);
            var definition = new MapObjectDefinition(id, assetId, objectType, Vector(Object(item, "position")), layer, Boolean(item, "blocking"), collision, interaction, zoneId, scale);
            if (!result.TryAdd(id, definition)) throw new DefinitionException($"Duplicate map object '{id}'.");
        }
        return new ReadOnlyDictionary<string, MapObjectDefinition>(result);
    }

    private static IReadOnlyList<string> ParseCollision(JsonElement root, IReadOnlyDictionary<string, MapObjectDefinition> objects)
    {
        var collision = Object(root, "collision");
        var blocked = StringArray(collision, "blockedObjects");
        foreach (var id in blocked)
        {
            if (!objects.TryGetValue(id, out var item)) throw new DefinitionException($"Collision references unknown object '{id}'.");
            if (!item.Blocking) throw new DefinitionException($"Collision references non-blocking object '{id}'.");
        }
        EnsureUnique(blocked, "blocked object");
        _ = StringArray(collision, "blockedTiles");
        if (!collision.TryGetProperty("specialZones", out var zones) || zones.ValueKind != JsonValueKind.Array) throw new DefinitionException("collision.specialZones must be an array.");
        return blocked;
    }

    private static IReadOnlyList<MapSpawnDefinition> ParseSpawns(JsonElement root, double width, double height)
    {
        var result = new List<MapSpawnDefinition>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Array(root, "spawnPoints").EnumerateArray())
        {
            var id = Text(item, "id"); if (!ids.Add(id)) throw new DefinitionException($"Duplicate spawn point '{id}'.");
            var position = new Vec2(Number(item, "x", 0), Number(item, "y", 0));
            if (position.X > width || position.Y > height) throw new DefinitionException($"Spawn point '{id}' lies outside the map.");
            result.Add(new MapSpawnDefinition(id, position, OptionalText(item, "facing")));
        }
        if (result.Count == 0) throw new DefinitionException("Map must contain a spawn point.");
        return result.AsReadOnly();
    }

    private static IReadOnlyList<MapExitDefinition> ParseExits(JsonElement root, double width, double height)
    {
        var result = new List<MapExitDefinition>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Array(root, "exits").EnumerateArray())
        {
            var id = Text(item, "id"); if (!ids.Add(id)) throw new DefinitionException($"Duplicate map exit '{id}'.");
            var trigger = Rectangle(Object(item, "triggerArea"));
            if (trigger.X < 0 || trigger.Y < 0 || trigger.X + trigger.Width > width || trigger.Y + trigger.Height > height)
                throw new DefinitionException($"Map exit '{id}' lies outside the map bounds.");
            result.Add(new MapExitDefinition(id, Text(item, "targetMap"), Text(item, "targetSpawn"), trigger, Text(item, "transitionType")));
        }
        return result.AsReadOnly();
    }

    private static IReadOnlyDictionary<string, MapZoneDefinition> ParseZones(JsonElement root, double width, double height)
    {
        if (!root.TryGetProperty("zones", out var input)) return new ReadOnlyDictionary<string, MapZoneDefinition>(new Dictionary<string, MapZoneDefinition>(StringComparer.Ordinal));
        if (input.ValueKind != JsonValueKind.Array) throw new DefinitionException("'zones' must be an array.");
        var result = new Dictionary<string, MapZoneDefinition>(StringComparer.Ordinal);
        foreach (var item in input.EnumerateArray())
        {
            var id = Text(item, "id"); var bounds = Rectangle(Object(item, "bounds"));
            if (bounds.X < 0 || bounds.Y < 0 || bounds.X + bounds.Width > width || bounds.Y + bounds.Height > height) throw new DefinitionException($"Zone '{id}' lies outside the map bounds.");
            var rules = item.TryGetProperty("placementRuleIds", out var ruleInput) ? StringArrayValue(ruleInput, $"zone '{id}' placementRuleIds") : System.Array.Empty<string>();
            if (!result.TryAdd(id, new MapZoneDefinition(id, Text(item, "type"), bounds, rules))) throw new DefinitionException($"Duplicate map zone '{id}'.");
        }
        return new ReadOnlyDictionary<string, MapZoneDefinition>(result);
    }

    private static IReadOnlyDictionary<string, MapPlacementRuleDefinition> ParsePlacementRules(JsonElement root)
    {
        if (!root.TryGetProperty("placementRules", out var input)) return new ReadOnlyDictionary<string, MapPlacementRuleDefinition>(new Dictionary<string, MapPlacementRuleDefinition>(StringComparer.Ordinal));
        if (input.ValueKind != JsonValueKind.Array) throw new DefinitionException("'placementRules' must be an array.");
        var result = new Dictionary<string, MapPlacementRuleDefinition>(StringComparer.Ordinal);
        foreach (var item in input.EnumerateArray())
        {
            var id = Text(item, "id"); var rule = new MapPlacementRuleDefinition(id, Text(item, "category"), Number(item, "minimumSpacing", 0), Number(item, "spawnClearance", 0));
            if (!result.TryAdd(id, rule)) throw new DefinitionException($"Duplicate placement rule '{id}'.");
        }
        return new ReadOnlyDictionary<string, MapPlacementRuleDefinition>(result);
    }

    private static void ValidateZoneRules(IReadOnlyDictionary<string, MapZoneDefinition> zones, IReadOnlyDictionary<string, MapPlacementRuleDefinition> rules)
    {
        foreach (var zone in zones.Values)
            foreach (var ruleId in zone.PlacementRuleIds)
                if (!rules.ContainsKey(ruleId)) throw new DefinitionException($"Zone '{zone.Id}' references unknown placement rule '{ruleId}'.");
    }

    private static IReadOnlyDictionary<string, double> ParseObjectScales(JsonElement root)
    {
        if (!root.TryGetProperty("objectScaleDefaults", out var input)) return new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(StringComparer.Ordinal));
        if (input.ValueKind != JsonValueKind.Object) throw new DefinitionException("'objectScaleDefaults' must be an object.");
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var item in input.EnumerateObject()) result[item.Name] = NumberValue(item.Value, $"objectScaleDefaults.{item.Name}", 0.01);
        return new ReadOnlyDictionary<string, double>(result);
    }

    private static double ParseSpawnSafetyRadius(JsonElement root) => root.TryGetProperty("placementPolicy", out var policy) && policy.ValueKind == JsonValueKind.Object
        ? Number(policy, "spawnSafetyRadius", 0)
        : 48;

    private static void ValidatePlacement(IReadOnlyDictionary<string, MapObjectDefinition> objects, IReadOnlyList<string> blockedIds, IReadOnlyList<MapSpawnDefinition> spawns, IReadOnlyDictionary<string, MapZoneDefinition> zones, IReadOnlyDictionary<string, MapPlacementRuleDefinition> rules, double width, double height, double spawnSafetyRadius)
    {
        var blockers = blockedIds.Select(id => objects[id]).Where(item => item.Collision is not null).ToArray();
        foreach (var item in objects.Values)
        {
            if (item.Position.X < 0 || item.Position.Y < 0 || item.Position.X > width || item.Position.Y > height) throw new DefinitionException($"Map object '{item.Id}' lies outside the map bounds.");
            if (item.Collision is not { } collision) continue;
            var rect = new Rect(item.Position.X + collision.OffsetX, item.Position.Y + collision.OffsetY, collision.Width, collision.Height);
            if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > width || rect.Y + rect.Height > height) throw new DefinitionException($"Collision for map object '{item.Id}' lies outside the map bounds.");
        }
        foreach (var spawn in spawns)
        {
            var zoneClearance = zones.Values
                .Where(zone => zone.Bounds.Contains(spawn.Position.X, spawn.Position.Y))
                .SelectMany(zone => zone.PlacementRuleIds)
                .Where(rules.ContainsKey)
                .Select(ruleId => rules[ruleId].SpawnClearance)
                .DefaultIfEmpty(0)
                .Max();
            var requiredClearance = System.Math.Max(spawnSafetyRadius, zoneClearance);
            if (blockers.Any(item => item.Collision is { } collision && new Rect(item.Position.X + collision.OffsetX, item.Position.Y + collision.OffsetY, collision.Width, collision.Height).OverlapsCircle(spawn.Position.X, spawn.Position.Y, requiredClearance)))
                throw new DefinitionException($"Spawn point '{spawn.Id}' is inside the blocking spawn safety radius.");
        }
        for (var i = 0; i < blockers.Length; i++)
        for (var j = i + 1; j < blockers.Length; j++)
        {
            var a = blockers[i].Collision!; var b = blockers[j].Collision!;
            var first = new Rect(blockers[i].Position.X + a.OffsetX, blockers[i].Position.Y + a.OffsetY, a.Width, a.Height);
            var second = new Rect(blockers[j].Position.X + b.OffsetX, blockers[j].Position.Y + b.OffsetY, b.Width, b.Height);
            if (first.Overlaps(second)) throw new DefinitionException($"Blocking objects '{blockers[i].Id}' and '{blockers[j].Id}' overlap.");
            if (blockers[i].ZoneId is not { } zoneId || blockers[j].ZoneId != zoneId || !zones.TryGetValue(zoneId, out var zone)) continue;
            var minimumSpacing = zone.PlacementRuleIds.Where(rules.ContainsKey).Select(ruleId => rules[ruleId].MinimumSpacing).DefaultIfEmpty(0).Max();
            if (minimumSpacing > 0 && Expand(first, minimumSpacing).Overlaps(second)) throw new DefinitionException($"Blocking objects '{blockers[i].Id}' and '{blockers[j].Id}' violate zone '{zoneId}' minimum spacing.");
        }
    }

    private static Rect Expand(Rect rect, double amount) => new(rect.X - amount, rect.Y - amount, rect.Width + amount * 2, rect.Height + amount * 2);

    private static IReadOnlyList<string> AssetIdArray(JsonElement item, string property, AssetDefinitions assets) { var values = StringArray(item, property); foreach (var id in values) _ = assets.Get(id); return values; }
    private static IReadOnlyList<string> StringArray(JsonElement item, string property) { var values = Array(item, property).EnumerateArray().Select(value => StringValue(value, property)).ToArray(); return System.Array.AsReadOnly(values); }
    private static void EnsureUnique(IReadOnlyList<string> values, string label) { if (values.Distinct(StringComparer.Ordinal).Count() != values.Count) throw new DefinitionException($"Duplicate {label}."); }
    private static Vec2 Vector(JsonElement item) => new(Number(item, "x", double.NegativeInfinity), Number(item, "y", double.NegativeInfinity));
    private static Vec2? OptionalVector(JsonElement item, string property) => item.TryGetProperty(property, out var value) ? Vector(value) : null;
    private static Rect Rectangle(JsonElement item) => new(Number(item, "x", double.NegativeInfinity), Number(item, "y", double.NegativeInfinity), Number(item, "width", 1), Number(item, "height", 1));
    private static JsonElement Object(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new DefinitionException($"'{property}' must be an object.");
    private static JsonElement Array(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new DefinitionException($"'{property}' must be an array.");
    private static string Text(JsonElement item, string property) => item.TryGetProperty(property, out var value) ? StringValue(value, property) : throw new DefinitionException($"Missing string '{property}'.");
    private static string StringValue(JsonElement value, string label) => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new DefinitionException($"'{label}' must be a non-empty string.");
    private static string? OptionalText(JsonElement item, string property) => item.TryGetProperty(property, out var value) ? StringValue(value, property) : null;
    private static double Number(JsonElement item, string property, double minimum) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be a finite number >= {minimum}.");
    private static int Integer(JsonElement item, string property, int minimum) => item.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be an integer >= {minimum}.");
    private static bool Boolean(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new DefinitionException($"'{property}' must be boolean.");
    private static bool OptionalBoolean(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    private static double NumberValue(JsonElement value, string label, double minimum) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum ? number : throw new DefinitionException($"'{label}' must be a finite number >= {minimum}.");
    private static IReadOnlyList<string> StringArrayValue(JsonElement value, string label) => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) ? value.EnumerateArray().Select(item => item.GetString()!).ToArray() : throw new DefinitionException($"'{label}' must be an array of non-empty strings.");

    [GeneratedRegex("^[a-z][a-z0-9]*$")]
    private static partial Regex MapIdRegex();
}
