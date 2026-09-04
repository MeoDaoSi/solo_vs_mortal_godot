using System.Collections.ObjectModel;
using System.Text.Json;

namespace SoloVsMortal.Data.Definitions;

public sealed record PlayerDefinition(string Id, string DisplayName, double AttackRange);
public sealed record LevelRangeDefinition(int Minimum, int Maximum);
public sealed record AiDefinition(double AggroRadius, double AttackRange);
public sealed record SoulDropDefinition(double Chance, int MaximumCount, IReadOnlyDictionary<string, double> RankChanceScale);
public sealed record MonsterAssetDefinition(string SpriteId, IReadOnlyDictionary<string, string> Animations, string? ShadowId);
public sealed record MonsterAudioDefinition(string SpawnId, string AttackId, string HitId, string DeathId);
public sealed record MonsterDefinition(
    string Id,
    string SpeciesId,
    string DisplayName,
    string SoulNatureId,
    int DefaultRank,
    LevelRangeDefinition LevelRange,
    AiDefinition Ai,
    SoulDropDefinition SoulDrop,
    MonsterAssetDefinition Assets,
    MonsterAudioDefinition Audio);
public sealed record SoulSummonDefinition(
    AiDefinition Ai,
    double StabilityRecoverySeconds,
    double AreaRadius,
    double FollowMinimumDistance,
    double LeashRadius,
    double ArriveDistance);
public sealed record SoulDefinition(double PickupRadius, IReadOnlyDictionary<string, string> OrbByRank, SoulSummonDefinition Summon, int MaximumLevel);
public enum SoulBannerTier { NhapMon, LinhNgoc, ThienLinh }
public sealed record SoulBannerBonuses(double SoulHpPercent, double SoulAtkPercent);
public sealed record SoulBannerDefinition(
    string Id,
    SoulBannerTier Tier,
    string DisplayName,
    string BannerAssetId,
    int MaximumLevel,
    int BaseSlotLimit,
    int SlotLimitPerLevel,
    int BaseCapacity,
    int CapacityPerLevel,
    int BaseActiveLimit,
    int ActiveLimitPerLevel,
    SoulBannerBonuses BaseBonuses,
    SoulBannerBonuses BonusPerLevel,
    double CaptureModifier,
    double SummonModifier);

public sealed class GameDefinitions
{
    private readonly IReadOnlyDictionary<string, MonsterDefinition> _monsters;
    private readonly IReadOnlyDictionary<string, SoulBannerDefinition> _bannersById;
    private readonly IReadOnlyDictionary<SoulBannerTier, SoulBannerDefinition> _bannersByTier;

    internal GameDefinitions(
        PlayerDefinition player,
        SoulDefinition soul,
        IReadOnlyDictionary<string, MonsterDefinition> monsters,
        IReadOnlyDictionary<string, SoulBannerDefinition> bannersById,
        IReadOnlySet<string> soulNatureIds)
    {
        Player = player;
        Soul = soul;
        _monsters = monsters;
        _bannersById = bannersById;
        SoulNatureIds = soulNatureIds;
        _bannersByTier = new ReadOnlyDictionary<SoulBannerTier, SoulBannerDefinition>(
            bannersById.Values.ToDictionary(item => item.Tier));
    }

    public PlayerDefinition Player { get; }
    public SoulDefinition Soul { get; }
    public IReadOnlySet<string> SoulNatureIds { get; }
    public IEnumerable<MonsterDefinition> Monsters => _monsters.Values;
    public IEnumerable<SoulBannerDefinition> SoulBanners => _bannersById.Values;

    public MonsterDefinition Monster(string id) => Lookup(_monsters, id, "monster");
    public SoulBannerDefinition SoulBanner(string id) => Lookup(_bannersById, id, "Soul Banner");
    public SoulBannerDefinition SoulBanner(SoulBannerTier tier) => Lookup(_bannersByTier, tier, "Soul Banner tier");
    public SoulBannerDefinition StarterSoulBanner => SoulBanner(SoulBannerTier.NhapMon);

    private static TValue Lookup<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source, TKey key, string label)
        where TKey : notnull => source.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"Unknown {label}: {key}.");
}

public sealed class DefinitionException : Exception
{
    public DefinitionException(string message) : base(message) { }
    public DefinitionException(string message, Exception innerException) : base(message, innerException) { }
}

public static class GameDefinitionLoader
{
    private static readonly string[] RequiredAnimations = ["idle", "walk", "attack", "hit", "death"];
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };

    public static GameDefinitions LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var player = ParsePlayer(ReadRoot(directory, "player.json"));
        var soulNatureIds = ParseStableIds(ReadRoot(directory, "soulNatures.json"), "natures");
        var monsters = ParseMonsters(ReadRoot(directory, "monsters.json"), soulNatureIds);
        var soul = ParseSoul(ReadRoot(directory, "soul.json"));
        var banners = ParseBanners(ReadRoot(directory, "soulBanner.json"));
        return new GameDefinitions(player, soul, monsters, banners, soulNatureIds);
    }

    private static JsonElement ReadRoot(string directory, string filename)
    {
        var path = Path.Combine(directory, filename);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), DocumentOptions);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new DefinitionException($"Cannot load definition file '{path}'.", exception);
        }
    }

    private static PlayerDefinition ParsePlayer(JsonElement root)
    {
        AssertObject(root, "player config");
        var combat = RequireObject(root, "combat");
        var attackRange = RequireNumber(combat, "attackRange", 0);
        if (attackRange <= 0) throw new DefinitionException("combat.attackRange must be greater than zero.");
        return new PlayerDefinition(RequireString(root, "id"), RequireString(root, "displayName"), attackRange);
    }

    private static IReadOnlyDictionary<string, MonsterDefinition> ParseMonsters(JsonElement root, IReadOnlySet<string> soulNatureIds)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new DefinitionException("monsters.json must contain an array.");
        var result = new Dictionary<string, MonsterDefinition>(StringComparer.Ordinal);
        foreach (var item in root.EnumerateArray())
        {
            AssertObject(item, "monster config");
            var id = RequireString(item, "id");
            if (!id.StartsWith("mon_", StringComparison.Ordinal)) throw new DefinitionException($"Monster ID must start with 'mon_': {id}.");
            var speciesId = RequireString(item, "species");
            _ = BalanceDefinition.SpeciesById(speciesId);
            var soulNatureId = RequireString(item, "soulNatureId");
            if (!soulNatureIds.Contains(soulNatureId)) throw new DefinitionException($"Unknown Soul Nature '{soulNatureId}' in monster '{id}'.");
            var rank = RequireInteger(item, "defaultRank", 1);
            BalanceDefinition.RequireValidRank(rank);
            var rangeObject = RequireObject(item, "levelRange");
            var range = new LevelRangeDefinition(RequireInteger(rangeObject, "min", 1), RequireInteger(rangeObject, "max", 1));
            if (range.Maximum < range.Minimum) throw new DefinitionException($"levelRange.max must be >= min for '{id}'.");
            var aiObject = RequireObject(item, "ai");
            var ai = new AiDefinition(RequireNumber(aiObject, "aggroRadius", 0), RequireNumber(aiObject, "attackRange", 0));
            var dropObject = RequireObject(item, "soulDrop");
            var chance = RequireNumber(dropObject, "chance", 0);
            if (chance > 1) throw new DefinitionException($"soulDrop.chance must be <= 1 for '{id}'.");
            var scales = ParseNumberMap(RequireObject(dropObject, "rankChanceScale"), 0, 1);
            if (!scales.ContainsKey(rank.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                throw new DefinitionException($"soulDrop.rankChanceScale lacks default rank {rank} for '{id}'.");
            var drop = new SoulDropDefinition(chance, RequireInteger(dropObject, "maxCount", 1), scales);
            var assetsObject = RequireObject(item, "assets");
            var animationsObject = RequireObject(assetsObject, "animations");
            var animations = RequiredAnimations.ToDictionary(key => key, key => RequireString(animationsObject, key), StringComparer.Ordinal);
            var assets = new MonsterAssetDefinition(RequireString(assetsObject, "sprite"), new ReadOnlyDictionary<string, string>(animations), OptionalString(assetsObject, "shadow"));
            var audioObject = RequireObject(item, "audio");
            var audio = new MonsterAudioDefinition(RequireString(audioObject, "spawn"), RequireString(audioObject, "attack"), RequireString(audioObject, "hit"), RequireString(audioObject, "death"));
            var definition = new MonsterDefinition(id, speciesId, RequireString(item, "displayName"), soulNatureId, rank, range, ai, drop, assets, audio);
            if (!result.TryAdd(id, definition)) throw new DefinitionException($"Duplicate monster ID '{id}'.");
        }
        return new ReadOnlyDictionary<string, MonsterDefinition>(result);
    }

    private static SoulDefinition ParseSoul(JsonElement root)
    {
        AssertObject(root, "Soul config");
        var orbs = ParseStringMap(RequireObject(root, "orbByRank"));
        foreach (var rank in Enumerable.Range(1, BalanceDefinition.MaximumRank))
            if (!orbs.ContainsKey(rank.ToString(System.Globalization.CultureInfo.InvariantCulture))) throw new DefinitionException($"No Soul orb configured for rank {rank}.");
        var summonObject = RequireObject(root, "summon");
        var aiObject = RequireObject(summonObject, "ai");
        var summon = new SoulSummonDefinition(
            new AiDefinition(RequireNumber(aiObject, "aggroRadius", 0), RequireNumber(aiObject, "attackRange", 0)),
            RequireNumber(summonObject, "stabilityRecoverySeconds", 0), RequireNumber(summonObject, "areaRadius", 0),
            RequireNumber(summonObject, "followMinDistance", 0), RequireNumber(summonObject, "leashRadius", 0), RequireNumber(summonObject, "arriveDistance", 0));
        var xpObject = RequireObject(root, "xp");
        return new SoulDefinition(RequireNumber(root, "pickupRadius", 0), orbs, summon, RequireInteger(xpObject, "maxLevel", 1));
    }

    private static IReadOnlyDictionary<string, SoulBannerDefinition> ParseBanners(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new DefinitionException("soulBanner.json must contain an array.");
        var result = new Dictionary<string, SoulBannerDefinition>(StringComparer.Ordinal);
        var tiers = new HashSet<SoulBannerTier>();
        foreach (var item in root.EnumerateArray())
        {
            var id = RequireString(item, "id");
            var tier = ParseBannerTier(RequireString(item, "tier"));
            if (!tiers.Add(tier)) throw new DefinitionException($"Duplicate Soul Banner tier '{tier}'.");
            var definition = new SoulBannerDefinition(id, tier, RequireString(item, "displayName"), RequireString(item, "banner"),
                RequireInteger(item, "maxLevel", 1), RequireInteger(item, "baseSlotLimit", 0), RequireInteger(item, "slotLimitPerLevel", 0),
                RequireInteger(item, "baseCapacity", 0), RequireInteger(item, "capacityPerLevel", 0), RequireInteger(item, "baseActiveLimit", 0),
                RequireInteger(item, "activeLimitPerLevel", 0), ParseBonuses(RequireObject(item, "baseBonuses")), ParseBonuses(RequireObject(item, "bonusPerLevel")),
                RequireNumber(item, "captureModifier", 0), RequireNumber(item, "summonModifier", 0));
            if (!result.TryAdd(id, definition)) throw new DefinitionException($"Duplicate Soul Banner ID '{id}'.");
        }
        if (!tiers.Contains(SoulBannerTier.NhapMon)) throw new DefinitionException("Starter Soul Banner tier NHAP_MON is missing.");
        return new ReadOnlyDictionary<string, SoulBannerDefinition>(result);
    }

    private static SoulBannerBonuses ParseBonuses(JsonElement item) => new(RequireNumber(item, "soulHpPercent", 0), RequireNumber(item, "soulAtkPercent", 0));
    private static SoulBannerTier ParseBannerTier(string value) => value switch { "NHAP_MON" => SoulBannerTier.NhapMon, "LINH_NGOC" => SoulBannerTier.LinhNgoc, "THIEN_LINH" => SoulBannerTier.ThienLinh, _ => throw new DefinitionException($"Unknown Soul Banner tier '{value}'.") };

    private static IReadOnlySet<string> ParseStableIds(JsonElement root, string collectionName)
    {
        var collection = root.TryGetProperty(collectionName, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new DefinitionException($"'{collectionName}' must be an array.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in collection.EnumerateArray())
        {
            var id = RequireString(item, "id");
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Z][A-Z0-9_]*$")) throw new DefinitionException($"Invalid stable ID '{id}'.");
            if (!result.Add(id)) throw new DefinitionException($"Duplicate stable ID '{id}'.");
        }
        return result;
    }

    private static IReadOnlyDictionary<string, double> ParseNumberMap(JsonElement root, double min, double max)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var value) || !double.IsFinite(value) || value < min || value > max)
                throw new DefinitionException($"'{property.Name}' must be a finite number in [{min}..{max}].");
            result.Add(property.Name, value);
        }
        return new ReadOnlyDictionary<string, double>(result);
    }

    private static IReadOnlyDictionary<string, string> ParseStringMap(JsonElement root)
    {
        var result = root.EnumerateObject().ToDictionary(property => property.Name, property => RequireStringValue(property.Value, property.Name), StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, string>(result);
    }

    private static JsonElement RequireObject(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new DefinitionException($"'{property}' must be an object.");
    private static void AssertObject(JsonElement value, string label) { if (value.ValueKind != JsonValueKind.Object) throw new DefinitionException($"{label} must be an object."); }
    private static string RequireString(JsonElement parent, string property) => parent.TryGetProperty(property, out var value) ? RequireStringValue(value, property) : throw new DefinitionException($"Missing string '{property}'.");
    private static string RequireStringValue(JsonElement value, string label) => value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()) ? value.GetString()! : throw new DefinitionException($"'{label}' must be a non-empty string.");
    private static string? OptionalString(JsonElement parent, string property) => !parent.TryGetProperty(property, out var value) ? null : RequireStringValue(value, property);
    private static double RequireNumber(JsonElement parent, string property, double minimum) => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be a finite number >= {minimum}.");
    private static int RequireInteger(JsonElement parent, string property, int minimum) { var number = RequireNumber(parent, property, minimum); return number == System.Math.Truncate(number) && number <= int.MaxValue ? (int)number : throw new DefinitionException($"'{property}' must be an integer."); }
}
