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
public sealed record MapObjectDefinition(string Id, string AssetId, string Type, Vec2 Position, string LayerId, bool Blocking, CollisionFootprintDefinition? Collision, MapInteractionDefinition? Interaction);
public sealed record MapSpawnDefinition(string Id, Vec2 Position, string? Facing);
public sealed record MapExitDefinition(string Id, string TargetMapId, string TargetSpawnId, Rect TriggerArea, string TransitionType);
public sealed record MapDefinition(
    string Id, string DisplayName, string Type, double Width, double Height, int TileSize,
    MapCameraDefinition Camera, IReadOnlyList<string> TerrainAssetIds, IReadOnlyList<string> ObjectAssetIds,
    IReadOnlyDictionary<string, MapLayerDefinition> Layers, IReadOnlyDictionary<string, MapObjectDefinition> Objects,
    IReadOnlyList<string> BlockedObjectIds, IReadOnlyList<MapSpawnDefinition> SpawnPoints, IReadOnlyList<MapExitDefinition> Exits);

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
            if (assetInput.TryGetProperty("background", out var background)) _ = assets.Get(StringValue(background, "assets.background"));
            var terrain = AssetIdArray(assetInput, "terrain", assets);
            var objectAssets = AssetIdArray(assetInput, "objects", assets);
            var layers = ParseLayers(root, assets);
            var objects = ParseObjects(root, layers, assets, soulNatures);
            var blockedObjects = ParseCollision(root, objects);
            var spawns = ParseSpawns(root, width, height);
            var exits = ParseExits(root);
            return new MapDefinition(id, Text(root, "displayName"), Text(root, "type"), width, height, Integer(root, "tileSize", 1), camera,
                terrain, objectAssets, layers, objects, blockedObjects, spawns, exits);
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

    private static IReadOnlyDictionary<string, MapObjectDefinition> ParseObjects(JsonElement root, IReadOnlyDictionary<string, MapLayerDefinition> layers, AssetDefinitions assets, SoulNatureDefinitions soulNatures)
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
            var definition = new MapObjectDefinition(id, assetId, Text(item, "type"), Vector(Object(item, "position")), layer, Boolean(item, "blocking"), collision, interaction);
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

    private static IReadOnlyList<MapExitDefinition> ParseExits(JsonElement root)
    {
        var result = new List<MapExitDefinition>(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Array(root, "exits").EnumerateArray())
        {
            var id = Text(item, "id"); if (!ids.Add(id)) throw new DefinitionException($"Duplicate map exit '{id}'.");
            result.Add(new MapExitDefinition(id, Text(item, "targetMap"), Text(item, "targetSpawn"), Rectangle(Object(item, "triggerArea")), Text(item, "transitionType")));
        }
        return result.AsReadOnly();
    }

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

    [GeneratedRegex("^[a-z][a-z0-9]*$")]
    private static partial Regex MapIdRegex();
}
