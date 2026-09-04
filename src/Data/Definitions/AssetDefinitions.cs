using System.Collections.ObjectModel;
using System.Text.Json;

namespace SoloVsMortal.Data.Definitions;

public enum AssetKind { Image, SpriteSheet }
public sealed record AssetDefinition(string Id, AssetKind Kind, string File, int? FrameWidth, int? FrameHeight);
public sealed record AssetDefinitions(int SchemaVersion, IReadOnlyDictionary<string, AssetDefinition> Assets)
{
    public AssetDefinition Get(string id) => Assets.TryGetValue(id, out var value)
        ? value
        : throw new KeyNotFoundException($"Unknown asset ID '{id}'.");
}

public static class AssetDefinitionLoader
{
    public static AssetDefinitions Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var schema = RequiredInteger(root, "schemaVersion", 1);
            if (!root.TryGetProperty("assets", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new DefinitionException("Asset manifest 'assets' must be an array.");
            var assets = new Dictionary<string, AssetDefinition>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                var id = RequiredText(item, "id");
                var kind = RequiredText(item, "type") switch
                {
                    "image" => AssetKind.Image,
                    "spritesheet" => AssetKind.SpriteSheet,
                    var value => throw new DefinitionException($"Unknown asset type '{value}' for '{id}'."),
                };
                var file = RequiredText(item, "file").Replace('\\', '/');
                if (Path.IsPathRooted(file) || file.Split('/').Contains("..", StringComparer.Ordinal) || file.StartsWith("/", StringComparison.Ordinal))
                    throw new DefinitionException($"Asset '{id}' must use a safe relative file path.");
                int? width = null;
                int? height = null;
                if (kind == AssetKind.SpriteSheet)
                {
                    width = RequiredInteger(item, "frameWidth", 1);
                    height = RequiredInteger(item, "frameHeight", 1);
                }
                if (!assets.TryAdd(id, new AssetDefinition(id, kind, file, width, height)))
                    throw new DefinitionException($"Duplicate asset ID '{id}'.");
            }
            return new AssetDefinitions(schema, new ReadOnlyDictionary<string, AssetDefinition>(assets));
        }
        catch (DefinitionException) { throw; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new DefinitionException($"Cannot load asset definitions from '{path}'.", exception);
        }
    }

    private static string RequiredText(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new DefinitionException($"Asset field '{property}' must be a non-empty string.");
    private static int RequiredInteger(JsonElement item, string property, int minimum) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= minimum ? number : throw new DefinitionException($"Asset field '{property}' must be an integer >= {minimum}.");
}
