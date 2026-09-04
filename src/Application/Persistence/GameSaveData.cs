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
public sealed record WorldSaveData(IReadOnlyList<string> DestroyedObjectIds);
public sealed record GameSaveData(
    int Version, PlayerSaveData Player, IReadOnlyList<OwnedSoulSaveData> Souls, IReadOnlyList<SoulBannerSaveData> SoulBanner,
    ProgressionSaveData? Progression, IReadOnlyList<SoulRuntimeSaveData>? SoulRuntime, PointProgressSaveData? Essence,
    PointProgressSaveData? Bloodline, PossessionRuntimeSaveData? Possession, WorldSaveData? World);

public static class GameSaveCodec
{
    public const int CurrentVersion = 6;
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public static string Serialize(GameSaveData save) => JsonSerializer.Serialize(save, WriteOptions);
    public static GameSaveData Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var version = Int(root, "version", -1); if (version is < 1 or > CurrentVersion) throw new InvalidDataException("Unsupported save data version.");
        if (!root.TryGetProperty("player", out var playerInput) || !root.TryGetProperty("souls", out var soulsInput) || soulsInput.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Malformed save data.");
        var bannersInput = ArrayAlias(root, "soulBanner", "honPhien", "formations") ?? throw new InvalidDataException("Malformed save data.");
        var player = new PlayerSaveData(Number(playerInput, "currentHp"), Number(playerInput, "maxHp"), OptionalInt(playerInput, "level"), OptionalInt(playerInput, "rank"), Text(playerInput, "titleDisplayName"), Text(playerInput, "title"), OptionalInt(playerInput, "xp"));
        var souls = new List<OwnedSoulSaveData>();
        foreach (var item in soulsInput.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("origin", out var origin)) continue;
            var id = Text(item, "id"); var configId = Text(origin, "configId"); var displayName = Text(origin, "displayName"); if (id is null || configId is null || displayName is null) continue;
            souls.Add(new(id, Text(item, "soulNatureId"), Int(item, "level", 1), Int(item, "xp", 0), new SoulOriginSaveData(Text(origin, "monsterUid") ?? "", configId, Text(origin, "species") ?? "", displayName, Int(origin, "rank", 1), Text(origin, "rankKey") ?? "", Text(origin, "rankDisplayName") ?? Text(origin, "rankDisplay") ?? "", Text(origin, "rankDisplay"))));
        }
        var banners = new List<SoulBannerSaveData>();
        foreach (var item in bannersInput.EnumerateArray()) { var tier = Text(item, "tier"); if (tier is null) continue; banners.Add(new(Text(item, "id") ?? "", tier, Int(item, "level", 1), Strings(item, "boundSouls"))); }
        return new GameSaveData(version, player, souls, banners, Progression(root), Runtime(root), Points(root, "essence"), Points(root, "bloodline"), Possession(root), World(root));
    }
    private static ProgressionSaveData? Progression(JsonElement root) { if (!root.TryGetProperty("progression", out var value) || value.ValueKind != JsonValueKind.Object) return null; var inventory = new Dictionary<string, int>(StringComparer.Ordinal); if (value.TryGetProperty("inventory", out var input) && input.ValueKind == JsonValueKind.Object) foreach (var item in input.EnumerateObject()) if (item.Value.TryGetInt32(out var count)) inventory[item.Name] = count; var buffs = new List<TimedBuffSaveData>(); if (value.TryGetProperty("playerBuffs", out var list) && list.ValueKind == JsonValueKind.Array) foreach (var item in list.EnumerateArray()) { var id = Text(item, "pillId"); if (id is not null) buffs.Add(new(id, Number(item, "remainingSeconds"))); } return new(inventory, buffs); }
    private static IReadOnlyList<SoulRuntimeSaveData>? Runtime(JsonElement root) { if (!root.TryGetProperty("soulRuntime", out var value) || value.ValueKind != JsonValueKind.Array) return null; return value.EnumerateArray().Select(item => new SoulRuntimeSaveData(Text(item, "soulId") ?? "", Number(item, "recoverySeconds"), OptionalNumber(item, "recoveryDurationSeconds"))).ToArray(); }
    private static PointProgressSaveData? Points(JsonElement root, string name) { if (!root.TryGetProperty(name, out var value) || !value.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Object) return null; var result = new Dictionary<string, int>(StringComparer.Ordinal); foreach (var item in points.EnumerateObject()) if (item.Value.TryGetDouble(out var number) && double.IsFinite(number) && number > 0) result[item.Name] = (int)System.Math.Truncate(number); return new(result); }
    private static PossessionRuntimeSaveData? Possession(JsonElement root) { if (!root.TryGetProperty("possession", out var value) || value.ValueKind != JsonValueKind.Object) return null; var soulId = Text(value, "soulId"); var profileId = Text(value, "profileId"); return soulId is null || profileId is null ? null : new(soulId, profileId, Number(value, "remainingSeconds")); }
    private static WorldSaveData? World(JsonElement root) => root.TryGetProperty("world", out var value) && value.ValueKind == JsonValueKind.Object ? new(Strings(value, "destroyedObjectIds")) : null;
    private static JsonElement? ArrayAlias(JsonElement root, params string[] names) { foreach (var name in names) if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array) return value; return null; }
    private static IReadOnlyList<string> Strings(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.String).Select(entry => entry.GetString()!).ToArray() : Array.Empty<string>();
    private static string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int Int(JsonElement item, string name, int fallback) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) ? (int)System.Math.Truncate(number) : fallback;
    private static int? OptionalInt(JsonElement item, string name) => item.TryGetProperty(name, out _) ? Int(item, name, 0) : null;
    private static double Number(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : double.NaN;
    private static double? OptionalNumber(JsonElement item, string name) { var value = Number(item, name); return double.IsFinite(value) ? value : null; }
}
