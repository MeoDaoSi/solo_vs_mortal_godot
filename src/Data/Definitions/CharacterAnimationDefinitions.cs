using System.Collections.ObjectModel;
using System.Text.Json;

namespace SoloVsMortal.Data.Definitions;

public sealed record AnimationActionDefinition(string Id, string DisplayName, string AssetAction, int Columns, double FrameRate, int Repeat, IReadOnlyDictionary<string, int> FramesByDirection);
public sealed record PlayerAnimationDefinition(string Id, string DisplayName, int FormCount, int RanksPerForm, string AssetIdPattern, string AnimationKeyPattern, int CellWidth, int CellHeight, double OriginX, double OriginY, double Scale, IReadOnlyDictionary<string, int> Directions, IReadOnlyDictionary<string, AnimationActionDefinition> Actions);
public sealed record MonsterFormDefinition(string Directory, string FilePrefix, string DisplayName);
public sealed record MonsterAnimationDefinition(string Id, string SpeciesId, string DisplayName, string RootDirectory, string FramePathPattern, int SourceFrameSize, int CellSize, double Scale, IReadOnlyList<MonsterFormDefinition> Forms);
public sealed record MonsterAnimationActionDefinition(string Id, string DisplayName, string Directory, int FrameCount, double FrameRate, int Repeat);
public sealed record CharacterAnimationDefinitions(int SchemaVersion, PlayerAnimationDefinition Player, IReadOnlyDictionary<string, MonsterAnimationDefinition> MonstersBySpecies, IReadOnlyDictionary<string, MonsterAnimationActionDefinition> MonsterActions);

public static class CharacterAnimationDefinitionLoader
{
    private static readonly string[] Directions = ["front", "back", "left", "right"];
    private static readonly string[] RequiredMonsterActions = ["idle", "walk", "attack", "hit", "death"];

    public static CharacterAnimationDefinitions Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var schema = Integer(root, "schemaVersion", 1);
            var player = ParsePlayer(Object(root, "player"));
            var monsterActions = Unique(Array(root, "monsterActions"), ParseMonsterAction, action => action.Id, "monster animation action");
            foreach (var required in RequiredMonsterActions)
                if (!monsterActions.ContainsKey(required)) throw new DefinitionException($"Required monster animation action '{required}' is missing.");
            var monsters = Unique(Array(root, "monsters"), ParseMonster, monster => monster.SpeciesId, "monster animation species");
            return new CharacterAnimationDefinitions(schema, player, monsters, monsterActions);
        }
        catch (DefinitionException) { throw; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new DefinitionException($"Cannot load character animations from '{path}'.", exception);
        }
    }

    private static PlayerAnimationDefinition ParsePlayer(JsonElement item)
    {
        if (Text(item, "source") != "spritesheet") throw new DefinitionException("Player animation source must be 'spritesheet'.");
        var formCount = Integer(item, "formCount", 1);
        var ranksPerForm = Integer(item, "ranksPerForm", 1);
        if (formCount * ranksPerForm < BalanceDefinition.MaximumRank) throw new DefinitionException("Player animation forms do not cover every rank.");
        var assetPattern = Pattern(item, "assetIdPattern", "{form}", "{action}");
        var keyPattern = Pattern(item, "animationKeyPattern", "{form}", "{direction}", "{action}");
        var cell = Object(item, "cell");
        var origin = Object(item, "origin");
        var directions = IntegerMap(Object(item, "directions"));
        foreach (var direction in Directions)
            if (!directions.ContainsKey(direction)) throw new DefinitionException($"Player animation direction '{direction}' is missing.");
        var actions = Unique(Array(item, "actions"), ParsePlayerAction, action => action.Id, "player animation action");
        return new PlayerAnimationDefinition(Text(item, "id"), Text(item, "displayName"), formCount, ranksPerForm, assetPattern, keyPattern,
            Integer(cell, "width", 1), Integer(cell, "height", 1), Number(origin, "x", 0), Number(origin, "y", 0), Number(item, "scale", double.Epsilon), directions, actions);
    }

    private static AnimationActionDefinition ParsePlayerAction(JsonElement item)
    {
        var frames = item.TryGetProperty("framesByDirection", out var value) ? IntegerMap(value, 1) : new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
        foreach (var direction in frames.Keys)
            if (!Directions.Contains(direction, StringComparer.Ordinal)) throw new DefinitionException($"Unknown animation direction '{direction}'.");
        return new AnimationActionDefinition(Text(item, "id"), Text(item, "displayName"), Text(item, "assetAction"), Integer(item, "columns", 1), Number(item, "frameRate", double.Epsilon), Integer(item, "repeat", -1), frames);
    }

    private static MonsterAnimationDefinition ParseMonster(JsonElement item)
    {
        if (Text(item, "source") != "frameSequence") throw new DefinitionException("Monster animation source must be 'frameSequence'.");
        var species = Text(item, "species");
        _ = BalanceDefinition.SpeciesById(species);
        var pattern = Pattern(item, "framePathPattern", "{rootDir}", "{formDirectory}", "{actionDirectory}", "{filePrefix}", "{frame}");
        var forms = Array(item, "forms").EnumerateArray().Select(form => new MonsterFormDefinition(Text(form, "directory"), Text(form, "filePrefix"), Text(form, "displayName"))).ToArray();
        if (forms.Length != 3) throw new DefinitionException($"Monster '{species}' must define exactly three forms.");
        return new MonsterAnimationDefinition(Text(item, "id"), species, Text(item, "displayName"), Text(item, "rootDir"), pattern, Integer(item, "sourceFrameSize", 1), Integer(item, "cellSize", 1), Number(item, "scale", double.Epsilon), System.Array.AsReadOnly(forms));
    }

    private static MonsterAnimationActionDefinition ParseMonsterAction(JsonElement item) => new(Text(item, "id"), Text(item, "displayName"), Text(item, "directory"), Integer(item, "frameCount", 1), Number(item, "frameRate", double.Epsilon), Integer(item, "repeat", -1));
    private static string Pattern(JsonElement item, string property, params string[] tokens) { var pattern = Text(item, property); foreach (var token in tokens) if (!pattern.Contains(token, StringComparison.Ordinal)) throw new DefinitionException($"Pattern '{property}' is missing token '{token}'."); return pattern; }
    private static IReadOnlyDictionary<string, T> Unique<T>(JsonElement items, Func<JsonElement, T> parse, Func<T, string> key, string label) { var result = new Dictionary<string, T>(StringComparer.Ordinal); foreach (var item in items.EnumerateArray()) { var parsed = parse(item); if (!result.TryAdd(key(parsed), parsed)) throw new DefinitionException($"Duplicate {label} '{key(parsed)}'."); } return new ReadOnlyDictionary<string, T>(result); }
    private static IReadOnlyDictionary<string, int> IntegerMap(JsonElement item, int minimum = 0) { if (item.ValueKind != JsonValueKind.Object) throw new DefinitionException("Expected an integer map."); var result = new Dictionary<string, int>(StringComparer.Ordinal); foreach (var property in item.EnumerateObject()) { if (!property.Value.TryGetInt32(out var value) || value < minimum) throw new DefinitionException($"'{property.Name}' must be an integer >= {minimum}."); result.Add(property.Name, value); } return new ReadOnlyDictionary<string, int>(result); }
    private static JsonElement Object(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new DefinitionException($"'{property}' must be an object.");
    private static JsonElement Array(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new DefinitionException($"'{property}' must be an array.");
    private static string Text(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new DefinitionException($"'{property}' must be a non-empty string.");
    private static double Number(JsonElement item, string property, double minimum) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be a finite number >= {minimum}.");
    private static int Integer(JsonElement item, string property, int minimum) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be an integer >= {minimum}.");
}
