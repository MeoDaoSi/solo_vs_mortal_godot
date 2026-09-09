using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoloVsMortal.Application.Persistence;

public sealed record PlayerSaveData(double CurrentHp, double MaxHp, int? Level, int? Rank, string? TitleDisplayName, string? Title, int? Xp);
public sealed record SoulOriginSaveData(string MonsterUid, string ConfigId, string Species, string DisplayName, int Rank, string RankKey, string RankDisplayName, string? RankDisplay = null);
public sealed record OwnedSoulSaveData(string Id, string? SoulNatureId, int Level, int Xp, SoulOriginSaveData Origin);
public sealed record SoulBannerSaveData(string Id, string Tier, int Level, IReadOnlyList<string> BoundSouls);
public sealed record SoulRuntimeSaveData(string SoulId, double RecoverySeconds, double? RecoveryDurationSeconds);
public sealed record PointProgressSaveData(IReadOnlyDictionary<string, int> Points);
public sealed record PossessionRuntimeSaveData(string SoulId, string ProfileId, double RemainingSeconds);
public sealed record WorldSaveData(IReadOnlyList<string> DestroyedObjectIds, string? CurrentRegionId = null);
public sealed record GameSaveData(
    int Version, PlayerSaveData Player, IReadOnlyList<OwnedSoulSaveData> Souls, IReadOnlyList<SoulBannerSaveData> SoulBanner,
    ProgressionSaveData? Progression, IReadOnlyList<SoulRuntimeSaveData>? SoulRuntime, PointProgressSaveData? Essence,
    PointProgressSaveData? Bloodline, PossessionRuntimeSaveData? Possession, WorldSaveData? World);

public static class GameSaveCodec
{
    public const int CurrentVersion = 6;
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static string Serialize(GameSaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return JsonSerializer.Serialize(save, WriteOptions);
    }
    public static GameSaveData Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Save data is empty.");
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Save data root must be an object.");
        var version = Int(root, "version", -1); if (version is < 1 or > CurrentVersion) throw new InvalidDataException("Unsupported save data version.");
        if (!root.TryGetProperty("player", out var playerInput) || playerInput.ValueKind != JsonValueKind.Object || !root.TryGetProperty("souls", out var soulsInput) || soulsInput.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Malformed save data.");
        var bannersInput = ArrayAlias(root, "soulBanner", "honPhien", "formations") ?? throw new InvalidDataException("Malformed save data.");
        var player = new PlayerSaveData(Number(playerInput, "currentHp"), Number(playerInput, "maxHp"), OptionalInt(playerInput, "level"), OptionalInt(playerInput, "rank"), Text(playerInput, "titleDisplayName"), Text(playerInput, "title"), OptionalInt(playerInput, "xp"));
        var souls = new List<OwnedSoulSaveData>();
        foreach (var item in soulsInput.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("origin", out var origin) || origin.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Malformed owned soul record.");
            var id = Text(item, "id"); var configId = Text(origin, "configId"); var displayName = Text(origin, "displayName"); if (id is null || configId is null || displayName is null) throw new InvalidDataException("Owned soul record is missing a required identity field.");
            souls.Add(new(id, Text(item, "soulNatureId"), Int(item, "level", 1), Int(item, "xp", 0), new SoulOriginSaveData(Text(origin, "monsterUid") ?? "", configId, Text(origin, "species") ?? "", displayName, Int(origin, "rank", 1), Text(origin, "rankKey") ?? "", Text(origin, "rankDisplayName") ?? Text(origin, "rankDisplay") ?? "", Text(origin, "rankDisplay"))));
        }
        var banners = new List<SoulBannerSaveData>();
        foreach (var item in bannersInput.EnumerateArray()) { if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Malformed Soul Banner record."); var tier = Text(item, "tier"); if (tier is null) throw new InvalidDataException("Soul Banner record is missing tier."); banners.Add(new(Text(item, "id") ?? "", tier, Int(item, "level", 1), Strings(item, "boundSouls"))); }
        return new GameSaveData(version, player, souls, banners, Progression(root), Runtime(root), Points(root, "essence"), Points(root, "bloodline"), Possession(root), World(root));
    }
    private static ProgressionSaveData? Progression(JsonElement root)
    {
        if (!root.TryGetProperty("progression", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("progression must be an object.");
        var inventory = new Dictionary<string, int>(StringComparer.Ordinal);
        if (value.TryGetProperty("inventory", out var input))
        {
            if (input.ValueKind != JsonValueKind.Object) throw new InvalidDataException("progression.inventory must be an object.");
            foreach (var item in input.EnumerateObject())
            {
                if (!item.Value.TryGetInt32(out var count)) throw new InvalidDataException($"Invalid inventory count for '{item.Name}'.");
                inventory.Add(item.Name, count);
            }
        }
        var buffs = new List<TimedBuffSaveData>();
        if (value.TryGetProperty("playerBuffs", out var list))
        {
            if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException("progression.playerBuffs must be an array.");
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Malformed player buff record.");
                var id = Text(item, "pillId") ?? throw new InvalidDataException("Player buff is missing pillId.");
                buffs.Add(new(id, Number(item, "remainingSeconds")));
            }
        }
        return new(inventory, buffs);
    }
    private static IReadOnlyList<SoulRuntimeSaveData>? Runtime(JsonElement root)
    {
        if (!root.TryGetProperty("soulRuntime", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("soulRuntime must be an array.");
        return value.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Malformed soul runtime record.");
            return new SoulRuntimeSaveData(Text(item, "soulId") ?? throw new InvalidDataException("Soul runtime is missing soulId."), Number(item, "recoverySeconds"), OptionalNumber(item, "recoveryDurationSeconds"));
        }).ToArray();
    }
    private static PointProgressSaveData? Points(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{name}.points must be an object.");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in points.EnumerateObject())
        {
            if (!item.Value.TryGetDouble(out var number) || !double.IsFinite(number) || number < 0 || number > int.MaxValue || number != System.Math.Truncate(number)) throw new InvalidDataException($"Invalid {name} point value for '{item.Name}'.");
            result.Add(item.Name, (int)number);
        }
        return new(result);
    }
    private static PossessionRuntimeSaveData? Possession(JsonElement root)
    {
        if (!root.TryGetProperty("possession", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("possession must be an object.");
        return new(Text(value, "soulId") ?? throw new InvalidDataException("Possession is missing soulId."), Text(value, "profileId") ?? throw new InvalidDataException("Possession is missing profileId."), Number(value, "remainingSeconds"));
    }
    private static WorldSaveData? World(JsonElement root)
    {
        if (!root.TryGetProperty("world", out var value)) return null;
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("world must be an object.");
        return new(Strings(value, "destroyedObjectIds"), Text(value, "currentRegionId"));
    }
    private static JsonElement? ArrayAlias(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value))
                return value.ValueKind == JsonValueKind.Array ? value : throw new InvalidDataException($"{name} must be an array.");
        return null;
    }
    private static IReadOnlyList<string> Strings(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return Array.Empty<string>();
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"{name} must be an array.");
        return value.EnumerateArray().Select((entry, index) => entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString()) ? entry.GetString()! : throw new InvalidDataException($"{name}[{index}] must be a non-empty string.")).ToArray();
    }
    private static string? Text(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : throw new InvalidDataException($"{name} must be a non-empty string.");
    }
    private static int Int(JsonElement item, string name, int fallback)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)) return fallback;
        if (!value.TryGetInt32(out var number)) throw new InvalidDataException($"{name} must be an integer.");
        return number;
    }
    private static int? OptionalInt(JsonElement item, string name) => item.TryGetProperty(name, out _) ? Int(item, name, 0) : null;
    private static double Number(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value) || !value.TryGetDouble(out var number) || !double.IsFinite(number)) return double.NaN;
        return number;
    }
    private static double? OptionalNumber(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) throw new InvalidDataException($"{name} must be a finite number.");
        return number;
    }
}
