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
        SoulNatureDefinitions soulNatures,
        AssetDefinitions assets,
        CharacterAnimationDefinitions characterAnimations,
        IReadOnlyDictionary<string, MapDefinition> maps,
        WorldMapDefinitions worldMap)
    {
        Player = player;
        Soul = soul;
        _monsters = monsters;
        _bannersById = bannersById;
        SoulNatures = soulNatures;
        Assets = assets;
        CharacterAnimations = characterAnimations;
        Maps = maps;
        WorldMap = worldMap;
        _bannersByTier = new ReadOnlyDictionary<SoulBannerTier, SoulBannerDefinition>(
            bannersById.Values.ToDictionary(item => item.Tier));
    }

    public PlayerDefinition Player { get; }
    public SoulDefinition Soul { get; }
    public SoulNatureDefinitions SoulNatures { get; }
    public AssetDefinitions Assets { get; }
    public CharacterAnimationDefinitions CharacterAnimations { get; }
    public IReadOnlyDictionary<string, MapDefinition> Maps { get; }
    public WorldMapDefinitions WorldMap { get; }
    public IEnumerable<MonsterDefinition> Monsters => _monsters.Values;
    public IEnumerable<SoulBannerDefinition> SoulBanners => _bannersById.Values;

    /// <summary>Compatibility metadata for every canonical species. Combat stats, drops and skills remain owned by V2.5.</summary>
    public GameDefinitions WithCanonicalRoster(V25.CanonicalContentRegistry canonical)
    {
        var roster = _monsters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var soulNatures = WithCanonicalSoulNatures(canonical);
        foreach (var species in canonical.Content.Species)
        {
            var style = canonical.Content.CombatStyles.First(s => s.Id == species.CombatStyleId);
            var definitionId = $"mon_{species.Id}";
            // This record is an adapter for legacy render/Soul view models only. None of the
            // authored legacy monster behavior is inherited by a V2.5 species: canonical
            // combat, drops, capability and skills come from CanonicalContentRegistry.
            roster[definitionId] = new MonsterDefinition(
                definitionId,
                species.Id,
                species.Name,
                CanonicalSoulNatureId(species.Id),
                1,
                new LevelRangeDefinition(1, canonical.Balance.MaxLevel),
                new AiDefinition(192, style.RangeUnits),
                new SoulDropDefinition(0, 1, Enumerable.Range(1, 9).ToDictionary(rank => rank.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => 0d, StringComparer.Ordinal)),
                new MonsterAssetDefinition("", new Dictionary<string, string>(StringComparer.Ordinal), null),
                new MonsterAudioDefinition("", "", "", ""));
        }
        return new GameDefinitions(Player, Soul, roster, _bannersById, soulNatures, Assets, CharacterAnimations, Maps, WorldMap);
    }

    private SoulNatureDefinitions WithCanonicalSoulNatures(V25.CanonicalContentRegistry canonical)
    {
        const string traitId = "V25_CANONICAL";
        const string costId = "V25_CANONICAL_SOUL";
        var traits = SoulNatures.Traits.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var costs = SoulNatures.CostProfiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var natures = SoulNatures.Natures.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        traits[traitId] = new NamedDefinition(traitId, "Canonical V2.5");
        costs[costId] = new SoulCostProfileDefinition(costId, "Canonical V2.5", 1);
        foreach (var species in canonical.Content.Species)
        {
            var id = CanonicalSoulNatureId(species.Id);
            natures[id] = new SoulNatureDefinition(id, species.Name, new[] { traitId }, costId, null, null);
        }
        return new SoulNatureDefinitions(
            SoulNatures.SchemaVersion,
            new ReadOnlyDictionary<string, NamedDefinition>(traits),
            SoulNatures.Capabilities,
            new ReadOnlyDictionary<string, SoulCostProfileDefinition>(costs),
            SoulNatures.DevourXpProfiles,
            SoulNatures.EssenceProfiles,
            SoulNatures.BloodlineProfiles,
            SoulNatures.PossessionProfiles,
            new ReadOnlyDictionary<string, SoulNatureDefinition>(natures));
    }

    private static string CanonicalSoulNatureId(string speciesId) => $"V25_{speciesId.ToUpperInvariant()}";

    public MonsterDefinition Monster(string id) => Lookup(_monsters, id, "monster");
    public SoulBannerDefinition SoulBanner(string id) => Lookup(_bannersById, id, "Soul Banner");
    public SoulBannerDefinition SoulBanner(SoulBannerTier tier) => Lookup(_bannersByTier, tier, "Soul Banner tier");
    public SoulBannerDefinition StarterSoulBanner => SoulBanner(SoulBannerTier.NhapMon);
    public MapDefinition Map(string id) => Lookup(Maps, id, "map");
    public RegionDefinition StarterRegion => WorldMap.Region(WorldMap.StarterRegionId);
    public MapDefinition DefaultMap => Map(StarterRegion.MapContentId);

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
        var soulNatures = SoulNatureDefinitionLoader.Load(Path.Combine(directory, "soulNatures.json"));
        var monsters = ParseMonsters(ReadRoot(directory, "monsters.json"), soulNatures.Natures.Keys.ToHashSet(StringComparer.Ordinal));
        var soul = ParseSoul(ReadRoot(directory, "soul.json"));
        var banners = ParseBanners(ReadRoot(directory, "soulBanner.json"));
        var assets = AssetDefinitionLoader.Load(Path.GetFullPath(Path.Combine(directory, "..", "asset-manifest.json")));
        var animations = CharacterAnimationDefinitionLoader.Load(Path.Combine(directory, "characterAnimations.json"));
        ValidateAnimationReferences(monsters, animations, assets);
        var worldMap = WorldMapDefinitionLoader.Load(Path.Combine(directory, "worldMap.json"));
        var maps = new Dictionary<string, MapDefinition>(StringComparer.Ordinal);
        foreach (var region in worldMap.Regions.Values)
        {
            if (region.MapDefinitionFile is null) continue;
            var mapPath = Path.Combine(directory, region.MapDefinitionFile.Replace('/', Path.DirectorySeparatorChar));
            var map = MapDefinitionLoader.Load(mapPath, assets, soulNatures);
            if (!string.Equals(map.Id, region.MapContentId, StringComparison.Ordinal)) throw new DefinitionException($"Region '{region.Id}' map content '{region.MapContentId}' does not match map definition '{map.Id}'.");
            if (!maps.TryAdd(map.Id, map)) throw new DefinitionException($"Duplicate map content ID '{map.Id}'.");
        }
        if (!maps.ContainsKey(worldMap.Region(worldMap.StarterRegionId).MapContentId)) throw new DefinitionException("Starter region map content is missing.");
        return new GameDefinitions(player, soul, monsters, banners, soulNatures, assets, animations, new ReadOnlyDictionary<string, MapDefinition>(maps), worldMap);
    }

    private static void ValidateAnimationReferences(
        IReadOnlyDictionary<string, MonsterDefinition> monsters,
        CharacterAnimationDefinitions animations,
        AssetDefinitions assets)
    {
        foreach (var monster in monsters.Values)
            if (!animations.MonstersBySpecies.ContainsKey(monster.SpeciesId))
                throw new DefinitionException($"Monster '{monster.Id}' has no animation descriptor for species '{monster.SpeciesId}'.");

        var player = animations.Player;
        foreach (var form in Enumerable.Range(1, player.FormCount))
        foreach (var action in player.Actions.Values)
        {
            var assetId = player.AssetIdPattern
                .Replace("{form}", form.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{action}", action.AssetAction, StringComparison.Ordinal);
            _ = assets.Get(assetId);
        }
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
