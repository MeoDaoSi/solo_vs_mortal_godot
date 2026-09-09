using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Data.Definitions.V25;

public sealed record CanonicalNewGameDefinition(
    int Level,
    int BannerRank,
    int Coins,
    string CheckpointId,
    string Hp,
    string Spirit,
    IReadOnlyList<string> OwnedSpecies,
    string? SelectedSpeciesId,
    string PositionRegion,
    IReadOnlyList<int> PositionTile,
    IReadOnlyList<string> LearnedSkillIds);

public sealed record CanonicalProfileDefinition(
    string Id,
    int MaxPlayerLevel,
    int MaxBannerRank,
    int MaxEquipmentRank,
    int MaxArenaRank,
    IReadOnlyList<string> RegionIds,
    IReadOnlyList<string> SpeciesIds,
    IReadOnlyList<string> UniqueIds);

public sealed record CanonicalSpeciesDefinition(
    string Id,
    string Name,
    int PowerTier,
    string Archetype,
    string CombatStyleId,
    string CapabilityId,
    string SignatureSkillId,
    string HomeRegionId,
    string Role,
    IReadOnlyList<string> EligibleProfiles,
    double SoulYieldMultiplier,
    double NormalPossessionProfileMultiplier);

public sealed record CanonicalHazardAreaDefinition(string Chunk, IReadOnlyList<int> RectTiles);

public sealed record CanonicalRegionDefinition(
    string Id,
    string Name,
    int Rank,
    IReadOnlyList<int> Levels,
    IReadOnlyList<string> HomeSpecies,
    string BossId,
    string BossName,
    string HostSpeciesId,
    string DominantPaletteRamp,
    IReadOnlyList<string> Profiles,
    string? NextRegionId,
    IReadOnlyList<string> PortalRequirements,
    string? RequiredCapabilityForMainPath,
    string SecretCapability,
    string HazardType,
    CanonicalHazardAreaDefinition? HazardArea);

public sealed record CanonicalEquipmentDefinition(
    string Id,
    string Name,
    string Family,
    int Rank,
    string Slot,
    bool TwoHanded,
    IReadOnlyDictionary<string, double> Modifiers,
    string? GrantedSkillId,
    string? CombatStyleId,
    int BuyPrice,
    int SellPrice,
    bool WorldVisual,
    string IconAssetId,
    IReadOnlyList<string> Profiles);

public sealed record CanonicalSkillDefinition(
    string Id,
    string Name,
    string FunctionalCategory,
    int BaseRank,
    int MaxMasteryRank,
    string MasteryMetric,
    string TargetMode,
    string Shape,
    double Power,
    string DamageType,
    IReadOnlyList<string> Effects,
    int WindupMs,
    int ActiveMs,
    int RecoveryMs,
    int CooldownMs,
    double RangeUnits,
    double RadiusUnits,
    double ArcDegrees,
    int MaxTargets,
    double SpiritCost,
    IReadOnlyList<string> RequiredWeaponFamilies,
    string DefaultSourceKind,
    int? UnlockPlayerRank,
    string VisualProfileId,
    bool? UsesCombatStyle,
    string? Beneficiary,
    double? ShieldMaxHpFraction,
    string EffectTargetRule,
    string TargetFaction,
    bool EffectRankScaling,
    double? TargetHpFractionMax,
    IReadOnlyDictionary<string, double>? PassiveModifiers,
    IReadOnlyList<string>? SourceKindsAllowed);

public sealed record CanonicalCombatStyleDefinition(
    string Id,
    int WindupMs,
    int ActiveMs,
    int RecoveryMs,
    double RangeUnits,
    double ArcDegrees,
    string Shape,
    double Power,
    string DamageType,
    int MaxTargets);

public sealed record CanonicalConsumableDefinition(
    string Id,
    string Name,
    string Resource,
    double MaxFraction,
    int Price,
    int StackMax,
    int SharedCooldownMs,
    string AssetId);

public sealed record CanonicalUniquePowerDefinition(
    string Id,
    string Name,
    string Kind,
    int PowerRank,
    string HostBossId,
    string SkillId,
    IReadOnlyList<string> RequiredFacts,
    int RitualMs,
    IReadOnlyList<string> Profiles,
    string ReceiptId);

public sealed record CanonicalQuestObjectiveDefinition(string Event, string Target, int Count);
public sealed record CanonicalWorldSoulRewardDefinition(string SpeciesId, int SourceLevel);
public sealed record CanonicalQuestRewardsDefinition(
    int Xp,
    int Coin,
    double Density,
    CanonicalWorldSoulRewardDefinition? WorldSoul,
    string? DensityTarget,
    int? RegionSeal);
public sealed record CanonicalQuestDefinition(
    string Id,
    string Name,
    string RegionId,
    IReadOnlyList<string> Prerequisites,
    CanonicalQuestObjectiveDefinition Objective,
    bool AutoReward,
    CanonicalQuestRewardsDefinition Rewards);

public sealed record CanonicalSyncSourceDefinition(
    string Id,
    string SpeciesId,
    string SourceKey,
    string SourceVersion,
    string Event,
    string Target,
    int RequiredCount,
    double Gain,
    bool RequiresOwned,
    double RequiresSyncAtLeast,
    string? RequiredCapability,
    int InteractMs,
    string ClaimAt,
    bool RequiresCurrentSpeciesPossession);

public sealed record CanonicalEncounterDefinition(
    string Id,
    string GroupId,
    string RegionId,
    string Chunk,
    string SpeciesId,
    int Level,
    string EncounterType,
    int? CenterIndex,
    IReadOnlyList<int>? OffsetTiles,
    IReadOnlyList<int>? CenterTile,
    bool RewardEligible,
    IReadOnlyList<string> Pattern,
    double? CircleRadius);

public sealed record CanonicalTutorialDefinition(
    string Chunk,
    IReadOnlyList<int> CenterTile,
    string SpeciesId,
    int Level,
    int Count,
    string GroupId);

public sealed record CanonicalArenaDefinition(
    string Chunk,
    IReadOnlyList<int> EntryTile,
    IReadOnlyList<IReadOnlyList<int>> SpawnTiles,
    IReadOnlyList<int> BoundsTiles);

public sealed record CanonicalSecretPocketDefinition(
    int SpeciesIndex,
    string Chunk,
    IReadOnlyList<int> StartTile,
    IReadOnlyList<int> EndTile,
    IReadOnlyList<IReadOnlyList<int>> DetourTiles,
    IReadOnlyList<int> InteractTile,
    int ShortcutSpanTiles);

public sealed record CanonicalLayoutBlueprintDefinition(
    IReadOnlyList<int> ChunkTiles,
    int TileSize,
    IReadOnlyDictionary<string, IReadOnlyList<int>> ChunkGrid,
    IReadOnlyList<int> ShrineTile,
    IReadOnlyDictionary<string, IReadOnlyList<int>> NpcTiles,
    IReadOnlyList<IReadOnlyList<int>> EncounterCenters,
    IReadOnlyList<int> EliteTile,
    IReadOnlyList<int> BossTile,
    IReadOnlyList<int> LandmarkTile,
    IReadOnlyDictionary<string, IReadOnlyList<int>> ChestTiles,
    CanonicalTutorialDefinition Tutorial,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> Roads,
    IReadOnlyList<int> EntryTile,
    IReadOnlyList<int> ExitTile,
    IReadOnlyList<IReadOnlyList<int>> ExitRoad,
    int RoadWidthTiles,
    CanonicalArenaDefinition Arena,
    IReadOnlyList<CanonicalSecretPocketDefinition> SecretPockets,
    int SafeCampRadiusTiles,
    int OuterWallThicknessTiles);

public sealed record CanonicalContentDocument(
    string ContentVersion,
    string BalanceVersion,
    CanonicalNewGameDefinition NewGame,
    IReadOnlyDictionary<string, CanonicalProfileDefinition> Profiles,
    IReadOnlyList<CanonicalSpeciesDefinition> Species,
    IReadOnlyList<CanonicalRegionDefinition> Regions,
    IReadOnlyList<CanonicalEquipmentDefinition> Equipment,
    IReadOnlyList<CanonicalSkillDefinition> Skills,
    IReadOnlyList<CanonicalCombatStyleDefinition> CombatStyles,
    IReadOnlyList<CanonicalConsumableDefinition> Consumables,
    IReadOnlyList<CanonicalUniquePowerDefinition> UniquePowers,
    IReadOnlyList<CanonicalQuestDefinition> Quests,
    IReadOnlyList<CanonicalSyncSourceDefinition> SyncSources,
    IReadOnlyList<CanonicalEncounterDefinition> Encounters,
    CanonicalLayoutBlueprintDefinition LayoutBlueprint,
    IReadOnlyList<string> NpcIds,
    IReadOnlyList<string> Capabilities);

public sealed record CanonicalFixedPointDefinition(int HpSpiritDamage, int DensitySync);
public sealed record CanonicalCpDefinition(double Base, double PlayerFactor, double MonsterFactor, double AllyFactor, double TierBase, double RankBase, double LevelSlope, double LevelExponent);
public sealed record CanonicalXpDefinition(double Base, double Exponent, double KillDivisor);
public sealed record CanonicalDensityDefinition(double GainBase, double RankExponent, double SourceLevelDivisor, IReadOnlyList<int> Gates, int MaxPending, int EffectiveCombatCap, IReadOnlyList<IReadOnlyList<double>> ProjectionKnots, IReadOnlyList<double> Relevance);
public sealed record CanonicalSyncBalanceDefinition(IReadOnlyList<double> Milestones, IReadOnlyList<double> SourceGains);
public sealed record CanonicalSummonBalanceDefinition(double CapacityBase, double CapacityPerLevel, double RankCapacityBonus, double RegenCapacityFractionPerMinute, double RegenPerRankPerMinute, double DrainBasePerMinute, double DrainTierExponent, double DrainDensityLogFactor, int RecoveryMs, double RecoveredVitality, double RestVitalityPerSecond);
public sealed record CanonicalPossessionBalanceDefinition(double BurdenPt, double BurdenRank, double DurationBaseSeconds, double DurationSync, IReadOnlyList<double> DurationRange, double CooldownBaseSeconds, double CooldownSync, IReadOnlyList<double> CooldownRange, double TransferBase, double TransferSync, double TransferCapPermanentStat);
public sealed record CanonicalEquipmentBalanceDefinition(double RankScaleSlope, double PriceBase, double PriceRankExponent, double SellFraction);
public sealed record CanonicalCombatBalanceDefinition(int DodgeMs, int DodgeInvulnerableMs, int DodgeCooldownMs, double DodgeDistanceUnits, double ProjectileSpeed, double ProjectileRadius, int CombatExitMs);
public sealed record CanonicalSaveBalanceDefinition(int SchemaVersion, bool OfflineTimers, int DeathDelayMs, double RespawnHpFraction, double RespawnSpiritFraction);
public sealed record CanonicalBalanceDocument(
    string BalanceVersion,
    string Status,
    int SimulationHz,
    double WorldUnitsPerMeter,
    CanonicalFixedPointDefinition FixedPoint,
    int MaxLevel,
    int MaxRank,
    CanonicalCpDefinition Cp,
    double HpPerCp,
    double AtkPerCp,
    double DefPerSqrtCp,
    double ArmorDenominator,
    double CritChance,
    double CritMultiplier,
    CanonicalXpDefinition Xp,
    CanonicalDensityDefinition Density,
    CanonicalSyncBalanceDefinition Sync,
    CanonicalSummonBalanceDefinition Summon,
    CanonicalPossessionBalanceDefinition Possession,
    CanonicalEquipmentBalanceDefinition Equipment,
    CanonicalCombatBalanceDefinition Combat,
    CanonicalSaveBalanceDefinition Save);

public sealed record CanonicalAssetRequirement(
    string AssetId,
    string Group,
    string Family,
    string Role,
    string Representation,
    int? Rank,
    string Clip,
    string Direction,
    string DefinitionState,
    string ScopeSource,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<int> Durations,
    string Status,
    IReadOnlyList<string> Profiles,
    string ProductionLane,
    JsonElement Contract);

public sealed record CanonicalAssetRequirementsDocument(
    int SchemaVersion,
    string ContentVersion,
    IReadOnlyList<string> Profiles,
    IReadOnlyList<CanonicalAssetRequirement> Assets,
    string Note);

public sealed record CanonicalStyleRoleDefinition(
    IReadOnlyList<int> Canvas,
    IReadOnlyList<int> DefaultPivot,
    string Alpha,
    bool RequiresTransparency,
    int MaxColors);

public sealed record CanonicalStyleLockDefinition(
    string StyleId,
    string StyleVersion,
    string Status,
    string MasterSetVersion,
    string CalibratedAt,
    string CalibrationEvidence,
    string Camera,
    IReadOnlyList<int> Viewport,
    int TileSize,
    double NativePixelsPerWorldUnit,
    string LightDirection,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Palette,
    IReadOnlyDictionary<string, CanonicalStyleRoleDefinition> Roles);

public sealed class CanonicalContentRegistry
{
    internal CanonicalContentRegistry(
        string revision,
        IReadOnlyDictionary<string, string> pinnedHashes,
        CanonicalContentDocument content,
        CanonicalBalanceDocument balance,
        CanonicalAssetRequirementsDocument assetRequirements,
        CanonicalStyleLockDefinition styleLock,
        string sourceDirectory)
    {
        Revision = revision;
        PinnedHashes = pinnedHashes;
        Content = content;
        Balance = balance;
        AssetRequirements = assetRequirements;
        StyleLock = styleLock;
        SourceDirectory = sourceDirectory;
    }

    public string ActiveProfileId { get; internal set; } = "beta_01";
    public string Revision { get; }
    public IReadOnlyDictionary<string, string> PinnedHashes { get; }
    public CanonicalContentDocument Content { get; }
    public CanonicalBalanceDocument Balance { get; }
    public CanonicalAssetRequirementsDocument AssetRequirements { get; }
    public CanonicalStyleLockDefinition StyleLock { get; }
    public string SourceDirectory { get; }

    public CanonicalProfileDefinition Profile(string id) => Content.Profiles.TryGetValue(id, out var profile)
        ? profile
        : throw new DefinitionException($"Unknown canonical profile '{id}'.");

    public CanonicalContentRegistry WithProfile(string profileId)
    {
        _ = Profile(profileId);
        return new CanonicalContentRegistry(Revision, PinnedHashes, Content, Balance, AssetRequirements, StyleLock, SourceDirectory) { ActiveProfileId = profileId };
    }

    public CanonicalProfileDefinition BetaProfile => Profile("beta_01");

    public IReadOnlyList<CanonicalSpeciesDefinition> SpeciesForProfile(string profileId)
    {
        var profile = Profile(profileId);
        var allowed = profile.SpeciesIds.ToHashSet(StringComparer.Ordinal);
        return Content.Species.Where(species => allowed.Contains(species.Id) && species.EligibleProfiles.Contains(profileId, StringComparer.Ordinal)).ToArray();
    }

    public IReadOnlyList<CanonicalRegionDefinition> RegionsForProfile(string profileId)
    {
        var profile = Profile(profileId);
        var allowed = profile.RegionIds.ToHashSet(StringComparer.Ordinal);
        return Content.Regions.Where(region => allowed.Contains(region.Id) && region.Profiles.Contains(profileId, StringComparer.Ordinal)).ToArray();
    }

    public IReadOnlyList<CanonicalEquipmentDefinition> EquipmentForProfile(string profileId)
    {
        Profile(profileId);
        return Content.Equipment.Where(item => item.Profiles.Contains(profileId, StringComparer.Ordinal)).ToArray();
    }

    public IReadOnlyList<CanonicalSkillDefinition> SkillsForProfile(string profileId)
    {
        var profile = Profile(profileId);
        var maxPlayerRank = V25PlayerRankFromLevel(profile.MaxPlayerLevel);
        return Content.Skills.Where(skill => skill.DefaultSourceKind is "Core" or "PermanentLearned"
            && (skill.UnlockPlayerRank is null || skill.UnlockPlayerRank <= maxPlayerRank)).ToArray();
    }

    public IReadOnlyList<CanonicalSkillDefinition> LearnedSkillsForProfile(string profileId) =>
        SkillsForProfile(profileId).Where(skill => skill.DefaultSourceKind == "PermanentLearned").ToArray();

    public IReadOnlyList<CanonicalSkillDefinition> CoreSkillsForProfile(string profileId) =>
        SkillsForProfile(profileId).Where(skill => skill.DefaultSourceKind == "Core").ToArray();

    public IReadOnlyList<CanonicalSkillDefinition> ActorSkillsForProfile(string profileId) =>
        SpeciesForProfile(profileId).Select(species => Content.Skills.First(skill => skill.Id == species.SignatureSkillId)).ToArray();

    public IReadOnlyList<CanonicalSkillDefinition> UniqueSkillsForProfile(string profileId)
    {
        var profile = Profile(profileId);
        var uniqueIds = profile.UniqueIds.ToHashSet(StringComparer.Ordinal);
        var skillIds = Content.UniquePowers.Where(power => uniqueIds.Contains(power.Id)).Select(power => power.SkillId).ToHashSet(StringComparer.Ordinal);
        return Content.Skills.Where(skill => skillIds.Contains(skill.Id)).ToArray();
    }

    private static int V25PlayerRankFromLevel(int level) => level < 1 ? throw new DefinitionException("Profile maxPlayerLevel must be positive.") : (level - 1) / 10 + 1;

    public IReadOnlyList<CanonicalAssetRequirement> AssetsForProfile(string profileId) =>
        AssetRequirements.Assets.Where(asset => asset.Profiles.Contains(profileId, StringComparer.Ordinal)).ToArray();
}

public static class CanonicalV25Loader
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow };
    private static readonly string LowerIdPattern = "^[a-z][a-z0-9_.-]*$";
    private static readonly string AssetIdPattern = "^[a-z][a-z0-9_.-]*$";

    public static CanonicalContentRegistry LoadFromDirectory(string directory, string profileId = "beta_01", string? expectedRevision = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new DefinitionException($"Canonical V2.5 bundle directory does not exist: '{root}'.");
        try
        {
            using var lockDocument = ReadDocument(root, "spec-lock.json");
            var lockRoot = lockDocument.RootElement;
            RequireObject(lockRoot, "spec-lock");
            Allow(lockRoot, "spec-lock", "revision", "contentVersion", "balanceVersion", "sha256");
            var revision = RequiredString(lockRoot, "revision");
            if (expectedRevision is not null && !string.Equals(revision, expectedRevision, StringComparison.Ordinal))
                throw new DefinitionException($"Canonical revision '{revision}' does not match expected '{expectedRevision}'.");
            var contentVersion = RequiredString(lockRoot, "contentVersion");
            var balanceVersion = RequiredString(lockRoot, "balanceVersion");
            var hashes = ParseHashes(RequiredObject(lockRoot, "sha256"));

            using var content = ReadPinned(root, "content.v2.5.json", "game_spec/content.v2.5.json", hashes);
            using var balance = ReadPinned(root, "balance.v2.5.json", "game_spec/balance.v2.5.json", hashes);
            using var assets = ReadPinned(root, "asset-requirements.v2.5.json", "game_spec/asset-requirements.v2.5.json", hashes);
            using var style = ReadPinned(root, "style-lock.json", "game_spec/style-lock.json", hashes);

            var contentValue = ParseContent(content.RootElement, contentVersion, balanceVersion);
            var balanceValue = ParseBalance(balance.RootElement, balanceVersion);
            var assetValue = ParseAssetRequirements(assets.RootElement, contentVersion);
            var styleValue = ParseStyle(style.RootElement);
            var registry = new CanonicalContentRegistry(revision, new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(hashes, StringComparer.Ordinal)), contentValue, balanceValue, assetValue, styleValue, root);
            if (balanceValue.SimulationHz != SoloVsMortal.Core.Loop.SimulationClock.SimulationHz)
                throw new DefinitionException($"Canonical simulationHz must be {SoloVsMortal.Core.Loop.SimulationClock.SimulationHz}, got {balanceValue.SimulationHz}.");
            if (balanceValue.FixedPoint.HpSpiritDamage != V25FixedPoint.ResourceScale || balanceValue.FixedPoint.DensitySync != V25FixedPoint.DensitySyncScale)
                throw new DefinitionException("Canonical fixed-point scales do not match the runtime operators.");
            _ = registry.Profile(profileId);
            registry.ActiveProfileId = profileId;
            ValidateCrossReferences(registry);
            return registry;
        }
        catch (DefinitionException) { throw; }
        catch (Exception exception) when (exception is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            throw new DefinitionException($"Cannot load canonical V2.5 bundle from '{root}'.", exception);
        }
    }

    private static CanonicalContentDocument ParseContent(JsonElement root, string expectedContentVersion, string expectedBalanceVersion)
    {
        RequireObject(root, "content.v2.5");
        Allow(root, "content.v2.5", "contentVersion", "balanceVersion", "newGame", "profiles", "species", "regions", "equipment", "skills", "combatStyles", "consumables", "uniquePowers", "quests", "syncSources", "encounters", "layoutBlueprint", "npcIds", "capabilities");
        var contentVersion = RequiredString(root, "contentVersion");
        var balanceVersion = RequiredString(root, "balanceVersion");
        RequireVersion(contentVersion, expectedContentVersion, "contentVersion");
        RequireVersion(balanceVersion, expectedBalanceVersion, "balanceVersion");
        var profilesObject = RequiredObject(root, "profiles");
        var profiles = new Dictionary<string, CanonicalProfileDefinition>(StringComparer.Ordinal);
        foreach (var property in profilesObject.EnumerateObject())
        {
            var id = LowerId(property.Name, "profile ID");
            var item = property.Value;
            RequireObject(item, $"profile '{id}'");
            Allow(item, $"profile '{id}'", "maxPlayerLevel", "maxBannerRank", "maxEquipmentRank", "maxArenaRank", "regionIds", "speciesIds", "uniqueIds");
            var value = new CanonicalProfileDefinition(id, RequiredInt(item, "maxPlayerLevel", 1), RequiredInt(item, "maxBannerRank", 1), RequiredInt(item, "maxEquipmentRank", 1), RequiredInt(item, "maxArenaRank", 1), LowerIds(RequiredArray(item, "regionIds"), "profile.regionIds"), LowerIds(RequiredArray(item, "speciesIds"), "profile.speciesIds"), LowerIds(RequiredArray(item, "uniqueIds"), "profile.uniqueIds"));
            if (!profiles.TryAdd(id, value)) throw new DefinitionException($"Duplicate profile '{id}'.");
        }
        return new CanonicalContentDocument(contentVersion, balanceVersion, ParseNewGame(RequiredObject(root, "newGame")),
            new ReadOnlyDictionary<string, CanonicalProfileDefinition>(profiles), ParseSpecies(RequiredArray(root, "species")), ParseRegions(RequiredArray(root, "regions")), ParseEquipment(RequiredArray(root, "equipment")), ParseSkills(RequiredArray(root, "skills")), ParseCombatStyles(RequiredArray(root, "combatStyles")), ParseConsumables(RequiredArray(root, "consumables")), ParseUniquePowers(RequiredArray(root, "uniquePowers")), ParseQuests(RequiredArray(root, "quests")), ParseSyncSources(RequiredArray(root, "syncSources")), ParseEncounters(RequiredArray(root, "encounters")), ParseLayout(RequiredObject(root, "layoutBlueprint")), LowerIds(RequiredArray(root, "npcIds"), "npcIds"), ParseCapabilities(RequiredArray(root, "capabilities")));
    }

    private static CanonicalNewGameDefinition ParseNewGame(JsonElement item)
    {
        Allow(item, "newGame", "level", "bannerRank", "coins", "checkpointId", "hp", "spirit", "ownedSpecies", "selectedSpeciesId", "positionRegion", "positionTile", "learnedSkillIds");
        var hp = RequiredString(item, "hp");
        var spirit = RequiredString(item, "spirit");
        if (!string.Equals(hp, "MaxHP", StringComparison.Ordinal) || !string.Equals(spirit, "MaxSpirit", StringComparison.Ordinal))
            throw new DefinitionException("newGame.hp and newGame.spirit must use the canonical MaxHP and MaxSpirit sentinels.");
        return new(RequiredInt(item, "level", 1), RequiredInt(item, "bannerRank", 1), RequiredInt(item, "coins", 0), LowerId(RequiredString(item, "checkpointId"), "checkpointId"), hp, spirit, LowerIds(RequiredArray(item, "ownedSpecies"), "newGame.ownedSpecies"), NullableLowerId(item, "selectedSpeciesId", "selectedSpeciesId"), LowerId(RequiredString(item, "positionRegion"), "positionRegion"), IntArray(RequiredArray(item, "positionTile"), "positionTile", 2), LowerIds(RequiredArray(item, "learnedSkillIds"), "newGame.learnedSkillIds"));
    }

    private static IReadOnlyList<CanonicalSpeciesDefinition> ParseSpecies(JsonElement array)
    {
        var result = new List<CanonicalSpeciesDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "species item");
            Allow(item, "species item", "id", "name", "powerTier", "archetype", "combatStyleId", "capabilityId", "signatureSkillId", "homeRegionId", "role", "eligibleProfiles", "soulYieldMultiplier", "normalPossessionProfileMultiplier");
            result.Add(new(LowerId(RequiredString(item, "id"), "species.id"), RequiredString(item, "name"), RequiredInt(item, "powerTier", 1), RequiredString(item, "archetype"), LowerId(RequiredString(item, "combatStyleId"), "species.combatStyleId"), RequiredString(item, "capabilityId"), LowerId(RequiredString(item, "signatureSkillId"), "species.signatureSkillId"), LowerId(RequiredString(item, "homeRegionId"), "species.homeRegionId"), RequiredString(item, "role"), LowerIds(RequiredArray(item, "eligibleProfiles"), "species.eligibleProfiles"), RequiredNumber(item, "soulYieldMultiplier", 0), RequiredNumber(item, "normalPossessionProfileMultiplier", 0)));
        }
        return Unique(result, item => item.Id, "species");
    }

    private static IReadOnlyList<CanonicalRegionDefinition> ParseRegions(JsonElement array)
    {
        var result = new List<CanonicalRegionDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "region item");
            Allow(item, "region item", "id", "name", "rank", "levels", "homeSpecies", "bossId", "bossName", "hostSpeciesId", "dominantPaletteRamp", "profiles", "nextRegionId", "portalRequirements", "requiredCapabilityForMainPath", "secretCapability", "hazardType", "hazardArea");
            CanonicalHazardAreaDefinition? hazard = null;
            if (NullableValue(item, "hazardArea") is { } hazardValue)
            {
                RequireObject(hazardValue, "region.hazardArea");
                Allow(hazardValue, "region.hazardArea", "chunk", "rectTiles");
                hazard = new(RequiredString(hazardValue, "chunk"), IntArray(RequiredArray(hazardValue, "rectTiles"), "region.hazardArea.rectTiles", 4));
            }
            result.Add(new(LowerId(RequiredString(item, "id"), "region.id"), RequiredString(item, "name"), RequiredInt(item, "rank", 1), IntArray(RequiredArray(item, "levels"), "region.levels", 2), LowerIds(RequiredArray(item, "homeSpecies"), "region.homeSpecies"), LowerId(RequiredString(item, "bossId"), "region.bossId"), RequiredString(item, "bossName"), LowerId(RequiredString(item, "hostSpeciesId"), "region.hostSpeciesId"), RequiredString(item, "dominantPaletteRamp"), LowerIds(RequiredArray(item, "profiles"), "region.profiles"), NullableLowerId(item, "nextRegionId", "region.nextRegionId"), StringArray(RequiredArray(item, "portalRequirements"), "region.portalRequirements"), NullableString(item, "requiredCapabilityForMainPath"), RequiredString(item, "secretCapability"), RequiredString(item, "hazardType"), hazard));
        }
        return Unique(result, item => item.Id, "regions");
    }

    private static IReadOnlyList<CanonicalEquipmentDefinition> ParseEquipment(JsonElement array)
    {
        var result = new List<CanonicalEquipmentDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "equipment item");
            Allow(item, "equipment item", "id", "name", "family", "rank", "slot", "twoHanded", "modifiers", "grantedSkillId", "combatStyleId", "buyPrice", "sellPrice", "worldVisual", "iconAssetId", "profiles");
            result.Add(new(LowerId(RequiredString(item, "id"), "equipment.id"), RequiredString(item, "name"), LowerId(RequiredString(item, "family"), "equipment.family"), RequiredInt(item, "rank", 1), RequiredString(item, "slot"), RequiredBool(item, "twoHanded"), NumberMap(RequiredObject(item, "modifiers"), "equipment.modifiers"), NullableLowerId(item, "grantedSkillId", "equipment.grantedSkillId"), NullableLowerId(item, "combatStyleId", "equipment.combatStyleId"), RequiredInt(item, "buyPrice", 0), RequiredInt(item, "sellPrice", 0), RequiredBool(item, "worldVisual"), LowerId(RequiredString(item, "iconAssetId"), "equipment.iconAssetId"), LowerIds(RequiredArray(item, "profiles"), "equipment.profiles")));
        }
        return Unique(result, item => item.Id, "equipment");
    }

    private static IReadOnlyList<CanonicalSkillDefinition> ParseSkills(JsonElement array)
    {
        var result = new List<CanonicalSkillDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "skill item");
            Allow(item, "skill item", "id", "name", "functionalCategory", "baseRank", "maxMasteryRank", "masteryMetric", "targetMode", "shape", "power", "damageType", "effects", "windupMs", "activeMs", "recoveryMs", "cooldownMs", "rangeUnits", "radiusUnits", "arcDegrees", "maxTargets", "spiritCost", "requiredWeaponFamilies", "defaultSourceKind", "unlockPlayerRank", "visualProfileId", "usesCombatStyle", "beneficiary", "shieldMaxHpFraction", "targetHpFractionMax", "passiveModifiers", "sourceKindsAllowed", "effectTargetRule", "targetFaction", "effectRankScaling");
            result.Add(new(LowerId(RequiredString(item, "id"), "skill.id"), RequiredString(item, "name"), RequiredString(item, "functionalCategory"), RequiredInt(item, "baseRank", 1), RequiredInt(item, "maxMasteryRank", 1), RequiredString(item, "masteryMetric"), RequiredString(item, "targetMode"), RequiredString(item, "shape"), RequiredNumber(item, "power", 0), RequiredString(item, "damageType"), StringArray(RequiredArray(item, "effects"), "skill.effects"), RequiredInt(item, "windupMs", 0), RequiredInt(item, "activeMs", 0), RequiredInt(item, "recoveryMs", 0), RequiredInt(item, "cooldownMs", 0), RequiredNumber(item, "rangeUnits", 0), RequiredNumber(item, "radiusUnits", 0), RequiredNumber(item, "arcDegrees", 0), RequiredInt(item, "maxTargets", 0), RequiredNumber(item, "spiritCost", 0), LowerIds(RequiredArray(item, "requiredWeaponFamilies"), "skill.requiredWeaponFamilies"), RequiredString(item, "defaultSourceKind"), NullableInt(item, "unlockPlayerRank"), LowerId(RequiredString(item, "visualProfileId"), "skill.visualProfileId"), NullableBool(item, "usesCombatStyle"), NullableString(item, "beneficiary"), NullableNumber(item, "shieldMaxHpFraction"), RequiredString(item, "effectTargetRule"), RequiredString(item, "targetFaction"), RequiredBool(item, "effectRankScaling"), NullableNumber(item, "targetHpFractionMax"), NullableNumberMap(item, "passiveModifiers", "skill.passiveModifiers"), NullableStringArray(item, "sourceKindsAllowed", "skill.sourceKindsAllowed")));
        }
        return Unique(result, item => item.Id, "skills");
    }

    private static IReadOnlyList<CanonicalCombatStyleDefinition> ParseCombatStyles(JsonElement array)
    {
        var result = new List<CanonicalCombatStyleDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "combatStyles item");
            Allow(item, "combatStyles item", "id", "windupMs", "activeMs", "recoveryMs", "rangeUnits", "arcDegrees", "shape", "power", "damageType", "maxTargets");
            result.Add(new(LowerId(RequiredString(item, "id"), "combatStyle.id"), RequiredInt(item, "windupMs", 0), RequiredInt(item, "activeMs", 0), RequiredInt(item, "recoveryMs", 0), RequiredNumber(item, "rangeUnits", 0), RequiredNumber(item, "arcDegrees", 0), RequiredString(item, "shape"), RequiredNumber(item, "power", 0), RequiredString(item, "damageType"), RequiredInt(item, "maxTargets", 0)));
        }
        return Unique(result, item => item.Id, "combatStyles");
    }

    private static IReadOnlyList<CanonicalConsumableDefinition> ParseConsumables(JsonElement array)
    {
        var result = new List<CanonicalConsumableDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "consumables item");
            Allow(item, "consumables item", "id", "name", "resource", "maxFraction", "price", "stackMax", "sharedCooldownMs", "assetId");
            result.Add(new(LowerId(RequiredString(item, "id"), "consumable.id"), RequiredString(item, "name"), RequiredString(item, "resource"), RequiredNumber(item, "maxFraction", 0), RequiredInt(item, "price", 0), RequiredInt(item, "stackMax", 1), RequiredInt(item, "sharedCooldownMs", 0), LowerId(RequiredString(item, "assetId"), "consumable.assetId")));
        }
        return Unique(result, item => item.Id, "consumables");
    }

    private static IReadOnlyList<CanonicalUniquePowerDefinition> ParseUniquePowers(JsonElement array)
    {
        var result = new List<CanonicalUniquePowerDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "uniquePower item");
            Allow(item, "uniquePower item", "id", "name", "kind", "powerRank", "hostBossId", "skillId", "requiredFacts", "ritualMs", "profiles", "receiptId");
            result.Add(new(LowerId(RequiredString(item, "id"), "uniquePower.id"), RequiredString(item, "name"), RequiredString(item, "kind"), RequiredInt(item, "powerRank", 1), LowerId(RequiredString(item, "hostBossId"), "uniquePower.hostBossId"), LowerId(RequiredString(item, "skillId"), "uniquePower.skillId"), LowerIds(RequiredArray(item, "requiredFacts"), "uniquePower.requiredFacts"), RequiredInt(item, "ritualMs", 0), LowerIds(RequiredArray(item, "profiles"), "uniquePower.profiles"), LowerId(RequiredString(item, "receiptId"), "uniquePower.receiptId")));
        }
        return Unique(result, item => item.Id, "uniquePowers");
    }

    private static IReadOnlyList<CanonicalQuestDefinition> ParseQuests(JsonElement array)
    {
        var result = new List<CanonicalQuestDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "quest item");
            Allow(item, "quest item", "id", "name", "regionId", "prerequisites", "objective", "autoReward", "rewards");
            var objective = RequiredObject(item, "objective");
            Allow(objective, "quest.objective", "event", "target", "count");
            var rewards = RequiredObject(item, "rewards");
            Allow(rewards, "quest.rewards", "xp", "coin", "density", "worldSoul", "densityTarget", "regionSeal");
            CanonicalWorldSoulRewardDefinition? worldSoul = null;
            if (NullableValue(rewards, "worldSoul") is { } soul)
            {
                RequireObject(soul, "quest.rewards.worldSoul");
                Allow(soul, "quest.rewards.worldSoul", "speciesId", "sourceLevel");
                worldSoul = new(LowerId(RequiredString(soul, "speciesId"), "quest.worldSoul.speciesId"), RequiredInt(soul, "sourceLevel", 1));
            }
            result.Add(new(LowerId(RequiredString(item, "id"), "quest.id"), RequiredString(item, "name"), LowerId(RequiredString(item, "regionId"), "quest.regionId"), LowerIds(RequiredArray(item, "prerequisites"), "quest.prerequisites"), new(RequiredString(objective, "event"), RequiredString(objective, "target"), RequiredInt(objective, "count", 1)), RequiredBool(item, "autoReward"), new(RequiredInt(rewards, "xp", 0), RequiredInt(rewards, "coin", 0), RequiredNumber(rewards, "density", 0), worldSoul, NullableString(rewards, "densityTarget"), NullableInt(rewards, "regionSeal"))));
        }
        return Unique(result, item => item.Id, "quests");
    }

    private static IReadOnlyList<CanonicalSyncSourceDefinition> ParseSyncSources(JsonElement array)
    {
        var result = new List<CanonicalSyncSourceDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "syncSource item");
            Allow(item, "syncSource item", "id", "speciesId", "sourceKey", "sourceVersion", "event", "target", "requiredCount", "gain", "requiresOwned", "requiresSyncAtLeast", "requiredCapability", "interactMs", "claimAt", "requiresCurrentSpeciesPossession");
            result.Add(new(LowerId(RequiredString(item, "id"), "sync.id"), LowerId(RequiredString(item, "speciesId"), "sync.speciesId"), LowerId(RequiredString(item, "sourceKey"), "sync.sourceKey"), RequiredString(item, "sourceVersion"), RequiredString(item, "event"), RequiredString(item, "target"), RequiredInt(item, "requiredCount", 0), RequiredNumber(item, "gain", 0), RequiredBool(item, "requiresOwned"), RequiredNumber(item, "requiresSyncAtLeast", 0), NullableString(item, "requiredCapability"), RequiredInt(item, "interactMs", 0), RequiredString(item, "claimAt"), RequiredBool(item, "requiresCurrentSpeciesPossession")));
        }
        return Unique(result, item => item.Id, "syncSources");
    }

    private static IReadOnlyList<CanonicalEncounterDefinition> ParseEncounters(JsonElement array)
    {
        var result = new List<CanonicalEncounterDefinition>();
        foreach (var item in array.EnumerateArray())
        {
            RequireObject(item, "encounter item");
            Allow(item, "encounter item", "id", "groupId", "regionId", "chunk", "speciesId", "level", "encounterType", "centerIndex", "offsetTiles", "centerTile", "rewardEligible", "pattern", "circleRadius");
            result.Add(new(LowerId(RequiredString(item, "id"), "encounter.id"), LowerId(RequiredString(item, "groupId"), "encounter.groupId"), LowerId(RequiredString(item, "regionId"), "encounter.regionId"), RequiredString(item, "chunk"), LowerId(RequiredString(item, "speciesId"), "encounter.speciesId"), RequiredInt(item, "level", 1), RequiredString(item, "encounterType"), NullableInt(item, "centerIndex"), NullableIntArray(item, "offsetTiles", "encounter.offsetTiles"), NullableIntArray(item, "centerTile", "encounter.centerTile"), RequiredBool(item, "rewardEligible"), StringArray(OptionalArray(item, "pattern") ?? EmptyArray("pattern"), "encounter.pattern"), NullableNumber(item, "circleRadius")));
        }
        return Unique(result, item => item.Id, "encounters");
    }

    private static CanonicalLayoutBlueprintDefinition ParseLayout(JsonElement item)
    {
        Allow(item, "layoutBlueprint", "chunkTiles", "tileSize", "chunkGrid", "shrineTile", "npcTiles", "encounterCenters", "eliteTile", "bossTile", "landmarkTile", "chestTiles", "tutorial", "roads", "entryTile", "exitTile", "exitRoad", "roadWidthTiles", "arena", "secretPockets", "safeCampRadiusTiles", "outerWallThicknessTiles");
        var chunkGrid = CoordinateMap(RequiredObject(item, "chunkGrid"), "layout.chunkGrid", new HashSet<string>(["Camp", "Field", "Ruins", "Sanctum"], StringComparer.Ordinal));
        var npcTiles = CoordinateMap(RequiredObject(item, "npcTiles"), "layout.npcTiles", new HashSet<string>(["an", "kha", "linh"], StringComparer.Ordinal));
        var chestTiles = CoordinateMap(RequiredObject(item, "chestTiles"), "layout.chestTiles", new HashSet<string>(["Camp", "Field", "Ruins"], StringComparer.Ordinal));
        var tutorial = RequiredObject(item, "tutorial");
        Allow(tutorial, "layout.tutorial", "chunk", "centerTile", "speciesId", "level", "count", "groupId");
        var arena = RequiredObject(item, "arena");
        Allow(arena, "layout.arena", "chunk", "entryTile", "spawnTiles", "boundsTiles");
        var pockets = new List<CanonicalSecretPocketDefinition>();
        foreach (var pocket in RequiredArray(item, "secretPockets").EnumerateArray())
        {
            RequireObject(pocket, "layout.secretPocket");
            Allow(pocket, "layout.secretPocket", "speciesIndex", "chunk", "startTile", "endTile", "detourTiles", "interactTile", "shortcutSpanTiles");
            pockets.Add(new(RequiredInt(pocket, "speciesIndex", 0), RequiredString(pocket, "chunk"), IntArray(RequiredArray(pocket, "startTile"), "secret.startTile", 2), IntArray(RequiredArray(pocket, "endTile"), "secret.endTile", 2), CoordinateArray(RequiredArray(pocket, "detourTiles"), "secret.detourTiles", 2), IntArray(RequiredArray(pocket, "interactTile"), "secret.interactTile", 2), RequiredInt(pocket, "shortcutSpanTiles", 0)));
        }
        return new(IntArray(RequiredArray(item, "chunkTiles"), "layout.chunkTiles", 2), RequiredInt(item, "tileSize", 1), chunkGrid, IntArray(RequiredArray(item, "shrineTile"), "layout.shrineTile", 2), npcTiles, CoordinateArray(RequiredArray(item, "encounterCenters"), "layout.encounterCenters", 2), IntArray(RequiredArray(item, "eliteTile"), "layout.eliteTile", 2), IntArray(RequiredArray(item, "bossTile"), "layout.bossTile", 2), IntArray(RequiredArray(item, "landmarkTile"), "layout.landmarkTile", 2), chestTiles, new(RequiredString(tutorial, "chunk"), IntArray(RequiredArray(tutorial, "centerTile"), "tutorial.centerTile", 2), LowerId(RequiredString(tutorial, "speciesId"), "tutorial.speciesId"), RequiredInt(tutorial, "level", 1), RequiredInt(tutorial, "count", 1), LowerId(RequiredString(tutorial, "groupId"), "tutorial.groupId")), NestedCoordinateArray(RequiredArray(item, "roads"), "layout.roads", 2), IntArray(RequiredArray(item, "entryTile"), "layout.entryTile", 2), IntArray(RequiredArray(item, "exitTile"), "layout.exitTile", 2), CoordinateArray(RequiredArray(item, "exitRoad"), "layout.exitRoad", 2), RequiredInt(item, "roadWidthTiles", 1), new(RequiredString(arena, "chunk"), IntArray(RequiredArray(arena, "entryTile"), "arena.entryTile", 2), CoordinateArray(RequiredArray(arena, "spawnTiles"), "arena.spawnTiles", 2), IntArray(RequiredArray(arena, "boundsTiles"), "arena.boundsTiles", 4)), pockets, RequiredInt(item, "safeCampRadiusTiles", 0), RequiredInt(item, "outerWallThicknessTiles", 0));
    }

    private static IReadOnlyList<string> ParseCapabilities(JsonElement array)
    {
        var result = StringArray(array, "capabilities");
        if (result.Count != result.Distinct(StringComparer.Ordinal).Count()) throw new DefinitionException("Duplicate canonical capability ID.");
        return result;
    }

    private static CanonicalBalanceDocument ParseBalance(JsonElement root, string expectedVersion)
    {
        RequireObject(root, "balance.v2.5");
        Allow(root, "balance.v2.5", "balanceVersion", "status", "simulationHz", "worldUnitsPerMeter", "fixedPoint", "maxLevel", "maxRank", "cp", "hpPerCP", "atkPerCP", "defPerSqrtCP", "armorDenominator", "critChance", "critMultiplier", "xp", "density", "sync", "summon", "possession", "equipment", "combat", "save");
        var version = RequiredString(root, "balanceVersion"); RequireVersion(version, expectedVersion, "balanceVersion");
        var fixedPoint = RequiredObject(root, "fixedPoint"); Allow(fixedPoint, "balance.fixedPoint", "hpSpiritDamage", "densitySync");
        var cp = RequiredObject(root, "cp"); Allow(cp, "balance.cp", "base", "playerFactor", "monsterFactor", "allyFactor", "tierBase", "rankBase", "levelSlope", "levelExponent");
        var xp = RequiredObject(root, "xp"); Allow(xp, "balance.xp", "base", "exponent", "killDivisor");
        var density = RequiredObject(root, "density"); Allow(density, "balance.density", "gainBase", "rankExponent", "sourceLevelDivisor", "gates", "maxPending", "effectiveCombatCap", "projectionKnots", "relevance");
        var sync = RequiredObject(root, "sync"); Allow(sync, "balance.sync", "milestones", "sourceGains");
        var summon = RequiredObject(root, "summon"); Allow(summon, "balance.summon", "capacityBase", "capacityPerLevel", "rankCapacityBonus", "regenCapacityFractionPerMinute", "regenPerRankPerMinute", "drainBasePerMinute", "drainTierExponent", "drainDensityLogFactor", "recoveryMs", "recoveredVitality", "restVitalityPerSecond");
        var possession = RequiredObject(root, "possession"); Allow(possession, "balance.possession", "burdenPT", "burdenRank", "durationBaseSeconds", "durationSync", "durationRange", "cooldownBaseSeconds", "cooldownSync", "cooldownRange", "transferBase", "transferSync", "transferCapPermanentStat");
        var equipment = RequiredObject(root, "equipment"); Allow(equipment, "balance.equipment", "rankScaleSlope", "priceBase", "priceRankExponent", "sellFraction");
        var combat = RequiredObject(root, "combat"); Allow(combat, "balance.combat", "dodgeMs", "dodgeInvulnerableMs", "dodgeCooldownMs", "dodgeDistanceUnits", "projectileSpeed", "projectileRadius", "combatExitMs");
        var save = RequiredObject(root, "save"); Allow(save, "balance.save", "schemaVersion", "offlineTimers", "deathDelayMs", "respawnHPFraction", "respawnSpiritFraction");
        return new(version, RequiredString(root, "status"), RequiredInt(root, "simulationHz", 1), RequiredNumber(root, "worldUnitsPerMeter", 0), new(RequiredInt(fixedPoint, "hpSpiritDamage", 1), RequiredInt(fixedPoint, "densitySync", 1)), RequiredInt(root, "maxLevel", 1), RequiredInt(root, "maxRank", 1), new(RequiredNumber(cp, "base", 0), RequiredNumber(cp, "playerFactor", 0), RequiredNumber(cp, "monsterFactor", 0), RequiredNumber(cp, "allyFactor", 0), RequiredNumber(cp, "tierBase", 0), RequiredNumber(cp, "rankBase", 0), RequiredNumber(cp, "levelSlope", 0), RequiredNumber(cp, "levelExponent", 0)), RequiredNumber(root, "hpPerCP", 0), RequiredNumber(root, "atkPerCP", 0), RequiredNumber(root, "defPerSqrtCP", 0), RequiredNumber(root, "armorDenominator", 0), RequiredNumber(root, "critChance", 0), RequiredNumber(root, "critMultiplier", 0), new(RequiredNumber(xp, "base", 0), RequiredNumber(xp, "exponent", 0), RequiredNumber(xp, "killDivisor", 0)), new(RequiredNumber(density, "gainBase", 0), RequiredNumber(density, "rankExponent", 0), RequiredNumber(density, "sourceLevelDivisor", 0), IntArray(RequiredArray(density, "gates"), "density.gates"), RequiredInt(density, "maxPending", 0), RequiredInt(density, "effectiveCombatCap", 0), DoubleMatrix(RequiredArray(density, "projectionKnots"), "density.projectionKnots", 2), DoubleArray(RequiredArray(density, "relevance"), "density.relevance")), new(DoubleArray(RequiredArray(sync, "milestones"), "sync.milestones"), DoubleArray(RequiredArray(sync, "sourceGains"), "sync.sourceGains")), new(RequiredNumber(summon, "capacityBase", 0), RequiredNumber(summon, "capacityPerLevel", 0), RequiredNumber(summon, "rankCapacityBonus", 0), RequiredNumber(summon, "regenCapacityFractionPerMinute", 0), RequiredNumber(summon, "regenPerRankPerMinute", 0), RequiredNumber(summon, "drainBasePerMinute", 0), RequiredNumber(summon, "drainTierExponent", 0), RequiredNumber(summon, "drainDensityLogFactor", 0), RequiredInt(summon, "recoveryMs", 0), RequiredNumber(summon, "recoveredVitality", 0), RequiredNumber(summon, "restVitalityPerSecond", 0)), new(RequiredNumber(possession, "burdenPT", 0), RequiredNumber(possession, "burdenRank", 0), RequiredNumber(possession, "durationBaseSeconds", 0), RequiredNumber(possession, "durationSync", 0), DoubleArray(RequiredArray(possession, "durationRange"), "possession.durationRange"), RequiredNumber(possession, "cooldownBaseSeconds", 0), RequiredNumber(possession, "cooldownSync", 0), DoubleArray(RequiredArray(possession, "cooldownRange"), "possession.cooldownRange"), RequiredNumber(possession, "transferBase", 0), RequiredNumber(possession, "transferSync", 0), RequiredNumber(possession, "transferCapPermanentStat", 0)), new(RequiredNumber(equipment, "rankScaleSlope", 0), RequiredNumber(equipment, "priceBase", 0), RequiredNumber(equipment, "priceRankExponent", 0), RequiredNumber(equipment, "sellFraction", 0)), new(RequiredInt(combat, "dodgeMs", 0), RequiredInt(combat, "dodgeInvulnerableMs", 0), RequiredInt(combat, "dodgeCooldownMs", 0), RequiredNumber(combat, "dodgeDistanceUnits", 0), RequiredNumber(combat, "projectileSpeed", 0), RequiredNumber(combat, "projectileRadius", 0), RequiredInt(combat, "combatExitMs", 0)), new(RequiredInt(save, "schemaVersion", 1), RequiredBool(save, "offlineTimers"), RequiredInt(save, "deathDelayMs", 0), RequiredNumber(save, "respawnHPFraction", 0), RequiredNumber(save, "respawnSpiritFraction", 0)));
    }

    private static CanonicalAssetRequirementsDocument ParseAssetRequirements(JsonElement root, string expectedContentVersion)
    {
        RequireObject(root, "asset-requirements.v2.5");
        Allow(root, "asset-requirements.v2.5", "schemaVersion", "contentVersion", "profiles", "assets", "note");
        var version = RequiredString(root, "contentVersion"); RequireVersion(version, expectedContentVersion, "asset-requirements.contentVersion");
        var assets = new List<CanonicalAssetRequirement>();
        foreach (var item in RequiredArray(root, "assets").EnumerateArray())
        {
            RequireObject(item, "asset requirement");
            Allow(item, "asset requirement", "assetId", "group", "family", "role", "representation", "rank", "clip", "direction", "definitionState", "scopeSource", "dependencies", "durations", "status", "profiles", "productionLane", "contract");
            var contract = RequiredObject(item, "contract"); ValidateContract(contract);
            assets.Add(new(AssetId(RequiredString(item, "assetId")), RequiredString(item, "group"), RequiredString(item, "family"), RequiredString(item, "role"), RequiredString(item, "representation"), NullableInt(item, "rank"), RequiredString(item, "clip"), RequiredString(item, "direction"), RequiredString(item, "definitionState"), RequiredString(item, "scopeSource"), StringArray(RequiredArray(item, "dependencies"), "asset.dependencies"), IntArray(RequiredArray(item, "durations"), "asset.durations"), RequiredString(item, "status"), LowerIds(RequiredArray(item, "profiles"), "asset.profiles"), RequiredString(item, "productionLane"), contract.Clone()));
        }
        return new(RequiredInt(root, "schemaVersion", 1), version, LowerIds(RequiredArray(root, "profiles"), "asset.profiles"), Unique(assets, item => item.AssetId, "asset requirements"), RequiredString(root, "note"));
    }

    private static void ValidateContract(JsonElement contract)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "anchor", "bitDepth", "bossDefinitionId", "capabilityId", "channels", "coverage", "damageType", "draw", "durationSeconds", "effects", "format", "frameCount", "frames", "gripAnchor", "itemDefinitionId", "loop", "motif", "notFootPivot", "rendering", "runtimeScaleUnits", "sampleRate", "shape", "speciesId", "text", "type", "usage", "use", "variant", "wangBits" };
        Allow(contract, "asset.contract", allowed.ToArray());
        foreach (var property in contract.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object) throw new DefinitionException($"asset.contract.{property.Name} has unsupported nested structure.");
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                if (property.Name == "effects")
                {
                    _ = StringArray(property.Value, $"asset.contract.{property.Name}");
                    continue;
                }
                if (property.Name == "durationSeconds")
                {
                    _ = DoubleArray(property.Value, $"asset.contract.{property.Name}");
                    continue;
                }
                throw new DefinitionException($"asset.contract.{property.Name} has unsupported nested structure.");
            }
        }
    }

    private static CanonicalStyleLockDefinition ParseStyle(JsonElement root)
    {
        RequireObject(root, "style-lock");
        Allow(root, "style-lock", "styleId", "styleVersion", "status", "masterSetVersion", "calibratedAt", "calibrationEvidence", "camera", "viewport", "tileSize", "nativePixelsPerWorldUnit", "lightDirection", "palette", "roles");
        var paletteObject = RequiredObject(root, "palette");
        var palette = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var property in paletteObject.EnumerateObject()) palette.Add(property.Name, StringArray(property.Value, $"palette.{property.Name}"));
        var rolesObject = RequiredObject(root, "roles");
        var roles = new Dictionary<string, CanonicalStyleRoleDefinition>(StringComparer.Ordinal);
        foreach (var property in rolesObject.EnumerateObject())
        {
            var item = property.Value; RequireObject(item, $"style role '{property.Name}'"); Allow(item, $"style role '{property.Name}'", "canvas", "defaultPivot", "alpha", "requiresTransparency", "maxColors");
            roles.Add(property.Name, new(IntArray(RequiredArray(item, "canvas"), $"role.{property.Name}.canvas", 2), IntArray(RequiredArray(item, "defaultPivot"), $"role.{property.Name}.defaultPivot", 2), RequiredString(item, "alpha"), RequiredBool(item, "requiresTransparency"), RequiredInt(item, "maxColors", 1)));
        }
        return new(RequiredString(root, "styleId"), RequiredString(root, "styleVersion"), RequiredString(root, "status"), RequiredString(root, "masterSetVersion"), RequiredString(root, "calibratedAt"), RequiredString(root, "calibrationEvidence"), RequiredString(root, "camera"), IntArray(RequiredArray(root, "viewport"), "style.viewport", 2), RequiredInt(root, "tileSize", 1), RequiredNumber(root, "nativePixelsPerWorldUnit", 0), RequiredString(root, "lightDirection"), new ReadOnlyDictionary<string, IReadOnlyList<string>>(palette), new ReadOnlyDictionary<string, CanonicalStyleRoleDefinition>(roles));
    }

    private static void ValidateCrossReferences(CanonicalContentRegistry registry)
    {
        var species = registry.Content.Species.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var regions = registry.Content.Regions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var skills = registry.Content.Skills.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var combatStyles = registry.Content.CombatStyles.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var uniquePowers = registry.Content.UniquePowers.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var quests = registry.Content.Quests.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var profilesById = registry.Content.Profiles;
        var profiles = registry.Content.Profiles;
        var newGame = registry.Content.NewGame;
        if (!regions.ContainsKey(newGame.PositionRegion)) throw new DefinitionException($"newGame references unknown position region '{newGame.PositionRegion}'.");
        foreach (var speciesId in newGame.OwnedSpecies)
            if (!species.ContainsKey(speciesId)) throw new DefinitionException($"newGame references unknown owned species '{speciesId}'.");
        if (newGame.SelectedSpeciesId is not null && !species.ContainsKey(newGame.SelectedSpeciesId))
            throw new DefinitionException($"newGame references unknown selected species '{newGame.SelectedSpeciesId}'.");
        foreach (var skillId in newGame.LearnedSkillIds)
            if (!skills.ContainsKey(skillId)) throw new DefinitionException($"newGame references unknown learned skill '{skillId}'.");
        foreach (var profile in profiles.Values)
        {
            foreach (var id in profile.RegionIds) if (!regions.ContainsKey(id)) throw new DefinitionException($"Profile '{profile.Id}' references unknown region '{id}'.");
            foreach (var id in profile.SpeciesIds) if (!species.ContainsKey(id)) throw new DefinitionException($"Profile '{profile.Id}' references unknown species '{id}'.");
            foreach (var id in profile.UniqueIds) if (!uniquePowers.ContainsKey(id)) throw new DefinitionException($"Profile '{profile.Id}' references unknown unique power '{id}'.");
        }
        foreach (var item in registry.Content.Species)
        {
            if (!regions.ContainsKey(item.HomeRegionId)) throw new DefinitionException($"Species '{item.Id}' references unknown home region '{item.HomeRegionId}'.");
            if (!skills.ContainsKey(item.SignatureSkillId)) throw new DefinitionException($"Species '{item.Id}' references unknown signature skill '{item.SignatureSkillId}'.");
            if (!combatStyles.ContainsKey(item.CombatStyleId)) throw new DefinitionException($"Species '{item.Id}' references unknown combat style '{item.CombatStyleId}'.");
            if (!registry.Content.Capabilities.Contains(item.CapabilityId, StringComparer.Ordinal)) throw new DefinitionException($"Species '{item.Id}' references unknown capability '{item.CapabilityId}'.");
            foreach (var profileId in item.EligibleProfiles) if (!profilesById.ContainsKey(profileId)) throw new DefinitionException($"Species '{item.Id}' references unknown profile '{profileId}'.");
        }
        foreach (var item in registry.Content.Equipment)
        {
            foreach (var profileId in item.Profiles) if (!profilesById.ContainsKey(profileId)) throw new DefinitionException($"Equipment '{item.Id}' references unknown profile '{profileId}'.");
            if (item.GrantedSkillId is not null && !skills.ContainsKey(item.GrantedSkillId)) throw new DefinitionException($"Equipment '{item.Id}' references unknown granted skill '{item.GrantedSkillId}'.");
            if (item.CombatStyleId is not null && !combatStyles.ContainsKey(item.CombatStyleId)) throw new DefinitionException($"Equipment '{item.Id}' references unknown combat style '{item.CombatStyleId}'.");
        }
        var assetIds = registry.AssetRequirements.Assets.Select(asset => asset.AssetId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in registry.Content.Equipment)
            if (!assetIds.Contains(item.IconAssetId)) throw new DefinitionException($"Equipment '{item.Id}' references unknown icon AssetId '{item.IconAssetId}'.");
        foreach (var item in registry.Content.Consumables)
            if (!assetIds.Contains(item.AssetId)) throw new DefinitionException($"Consumable '{item.Id}' references unknown AssetId '{item.AssetId}'.");
        foreach (var region in registry.Content.Regions)
        {
            if (!species.ContainsKey(region.HostSpeciesId)) throw new DefinitionException($"Region '{region.Id}' references unknown host species '{region.HostSpeciesId}'.");
            if (region.NextRegionId is not null && !regions.ContainsKey(region.NextRegionId)) throw new DefinitionException($"Region '{region.Id}' references unknown next region '{region.NextRegionId}'.");
            foreach (var id in region.HomeSpecies) if (!species.ContainsKey(id)) throw new DefinitionException($"Region '{region.Id}' references unknown home species '{id}'.");
            foreach (var profileId in region.Profiles) if (!profilesById.ContainsKey(profileId)) throw new DefinitionException($"Region '{region.Id}' references unknown profile '{profileId}'.");
            if (region.RequiredCapabilityForMainPath is not null && !registry.Content.Capabilities.Contains(region.RequiredCapabilityForMainPath, StringComparer.Ordinal)) throw new DefinitionException($"Region '{region.Id}' references unknown main-path capability '{region.RequiredCapabilityForMainPath}'.");
            if (!registry.Content.Capabilities.Contains(region.SecretCapability, StringComparer.Ordinal)) throw new DefinitionException($"Region '{region.Id}' references unknown secret capability '{region.SecretCapability}'.");
        }
        foreach (var source in registry.Content.SyncSources)
        {
            if (!species.ContainsKey(source.SpeciesId)) throw new DefinitionException($"Sync source '{source.Id}' references unknown species '{source.SpeciesId}'.");
            if (source.RequiredCapability is not null && !registry.Content.Capabilities.Contains(source.RequiredCapability, StringComparer.Ordinal)) throw new DefinitionException($"Sync source '{source.Id}' references unknown capability '{source.RequiredCapability}'.");
        }
        foreach (var encounter in registry.Content.Encounters)
            if (!species.ContainsKey(encounter.SpeciesId) || !regions.ContainsKey(encounter.RegionId)) throw new DefinitionException($"Encounter '{encounter.Id}' references an unknown species or region.");
        foreach (var power in registry.Content.UniquePowers)
        {
            if (!skills.ContainsKey(power.SkillId)) throw new DefinitionException($"Unique power '{power.Id}' references unknown skill '{power.SkillId}'.");
            foreach (var profileId in power.Profiles) if (!profilesById.ContainsKey(profileId)) throw new DefinitionException($"Unique power '{power.Id}' references unknown profile '{profileId}'.");
        }
        foreach (var quest in registry.Content.Quests)
        {
            if (!regions.ContainsKey(quest.RegionId)) throw new DefinitionException($"Quest '{quest.Id}' references unknown region '{quest.RegionId}'.");
            foreach (var prerequisite in quest.Prerequisites) if (!quests.ContainsKey(prerequisite)) throw new DefinitionException($"Quest '{quest.Id}' references unknown prerequisite '{prerequisite}'.");
            if (quest.Rewards.WorldSoul is { } reward && !species.ContainsKey(reward.SpeciesId)) throw new DefinitionException($"Quest '{quest.Id}' references unknown world-soul species '{reward.SpeciesId}'.");
        }
    }

    private static JsonDocument ReadDocument(string directory, string filename)
    {
        var path = Path.Combine(directory, filename);
        if (!File.Exists(path)) throw new DefinitionException($"Canonical bundle file is missing: '{path}'.");
        return JsonDocument.Parse(File.ReadAllBytes(path), DocumentOptions);
    }

    private static JsonDocument ReadPinned(string directory, string filename, string hashKey, IReadOnlyDictionary<string, string> hashes)
    {
        var path = Path.Combine(directory, filename);
        if (!hashes.TryGetValue(hashKey, out var expected)) throw new DefinitionException($"spec-lock.sha256 is missing '{hashKey}'.");
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : throw new DefinitionException($"Canonical bundle file is missing: '{path}'.");
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new DefinitionException($"Pinned hash mismatch for '{filename}': expected {expected}, got {actual}.");
        return JsonDocument.Parse(bytes, DocumentOptions);
    }

    private static IReadOnlyDictionary<string, string> ParseHashes(JsonElement root)
    {
        var expectedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "Solo_vs_Mortal_Gameplay_System_V2.5.md",
            "game_spec/content.v2.5.json",
            "game_spec/balance.v2.5.json",
            "game_spec/asset-requirements.v2.5.json",
            "game_spec/style-lock.json",
            "game_spec/acceptance-cases.v2.5.json",
        };
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!expectedKeys.Contains(property.Name)) throw new DefinitionException($"Unknown spec-lock.sha256 key '{property.Name}'.");
            var value = RequiredStringValue(property.Value, $"spec-lock.sha256.{property.Name}");
            if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character))) throw new DefinitionException($"Invalid SHA-256 for '{property.Name}'.");
            result.Add(property.Name, value.ToLowerInvariant());
        }
        foreach (var key in expectedKeys)
            if (!result.ContainsKey(key)) throw new DefinitionException($"spec-lock.sha256 is missing '{key}'.");
        return result;
    }

    private static void RequireVersion(string actual, string expected, string label)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal)) throw new DefinitionException($"{label} '{actual}' does not match pinned '{expected}'.");
    }

    private static void Allow(JsonElement value, string label, params string[] allowed)
    {
        var set = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!set.Contains(property.Name)) throw new DefinitionException($"Unknown field '{label}.{property.Name}'.");
    }

    private static void RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new DefinitionException($"{label} must be an object.");
    }

    private static JsonElement RequiredObject(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object) throw new DefinitionException($"Missing object '{property}'.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array) throw new DefinitionException($"Missing array '{property}'.");
        return value;
    }

    private static JsonElement? OptionalArray(JsonElement parent, string property) => parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value : null;
    private static JsonElement? NullableValue(JsonElement parent, string property) => !parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null ? null : value;
    private static JsonElement EmptyArray(string label) => JsonDocument.Parse("[]").RootElement.Clone();
    private static string RequiredString(JsonElement parent, string property) => parent.TryGetProperty(property, out var value) ? RequiredStringValue(value, property) : throw new DefinitionException($"Missing string '{property}'.");
    private static string RequiredStringValue(JsonElement value, string label) => value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()) ? value.GetString()! : throw new DefinitionException($"'{label}' must be a non-empty string.");
    private static string? NullableString(JsonElement parent, string property) => !parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null ? null : RequiredStringValue(value, property);
    private static int RequiredInt(JsonElement parent, string property, int minimum) { var value = RequiredNumber(parent, property, minimum); if (value != Math.Truncate(value) || value > int.MaxValue) throw new DefinitionException($"'{property}' must be an integer."); return (int)value; }
    private static int? NullableInt(JsonElement parent, string property) { if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null; return RequiredInt(parent, property, int.MinValue); }
    private static double RequiredNumber(JsonElement parent, string property, double minimum) { if (!parent.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < minimum) throw new DefinitionException($"'{property}' must be a finite number >= {minimum}."); return number; }
    private static double? NullableNumber(JsonElement parent, string property) { if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null; return RequiredNumber(parent, property, 0); }
    private static bool RequiredBool(JsonElement parent, string property) => parent.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new DefinitionException($"'{property}' must be a boolean.");
    private static bool? NullableBool(JsonElement parent, string property) => !parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null ? null : RequiredBool(parent, property);
    private static string? NullableLowerId(JsonElement parent, string property, string label) { var value = NullableString(parent, property); return value is null ? null : LowerId(value, label); }
    private static int[] IntArray(JsonElement array, string label, int? exactLength = null) { var values = array.EnumerateArray().Select((value, index) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw new DefinitionException($"'{label}[{index}]' must be an integer.")).ToArray(); if (exactLength is not null && values.Length != exactLength) throw new DefinitionException($"'{label}' must contain {exactLength} integers."); return values; }
    private static int[]? NullableIntArray(JsonElement parent, string property, string label) { if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null; if (value.ValueKind != JsonValueKind.Array) throw new DefinitionException($"'{label}' must be an array or null."); return IntArray(value, label); }
    private static double[] DoubleArray(JsonElement array, string label, int? innerLength = null) { var values = new List<double>(); foreach (var (value, index) in array.EnumerateArray().Select((value, index) => (value, index))) { if (value.ValueKind == JsonValueKind.Array) { var nested = value.EnumerateArray().Select((entry, nestedIndex) => entry.ValueKind == JsonValueKind.Number && entry.TryGetDouble(out var number) && double.IsFinite(number) ? number : throw new DefinitionException($"'{label}[{index}][{nestedIndex}]' must be finite.")).ToArray(); if (innerLength is not null && nested.Length != innerLength) throw new DefinitionException($"'{label}[{index}]' must contain {innerLength} numbers."); values.AddRange(nested); } else if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) values.Add(number); else throw new DefinitionException($"'{label}[{index}]' must be numeric."); } return values.ToArray(); }
    private static IReadOnlyList<string> StringArray(JsonElement array, string label) => array.EnumerateArray().Select((value, index) => value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()) ? value.GetString()! : throw new DefinitionException($"'{label}[{index}]' must be a non-empty string.")).ToArray();
    private static IReadOnlyList<string>? NullableStringArray(JsonElement parent, string property, string label)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new DefinitionException($"'{label}' must be an array or null.");
        return StringArray(value, label);
    }
    private static IReadOnlyList<string> LowerIds(JsonElement array, string label) => StringArray(array, label).Select(value => LowerId(value, label)).ToArray();
    private static IReadOnlyDictionary<string, double> NumberMap(JsonElement objectValue, string label) { var result = new Dictionary<string, double>(StringComparer.Ordinal); foreach (var property in objectValue.EnumerateObject()) result.Add(property.Name, RequiredNumberValue(property.Value, $"{label}.{property.Name}", 0)); return new ReadOnlyDictionary<string, double>(result); }
    private static IReadOnlyDictionary<string, double>? NullableNumberMap(JsonElement parent, string property, string label)
    {
        if (!parent.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object) throw new DefinitionException($"'{label}' must be an object or null.");
        return NumberMap(value, label);
    }
    private static IReadOnlyList<IReadOnlyList<double>> DoubleMatrix(JsonElement array, string label, int innerLength)
    {
        var result = new List<IReadOnlyList<double>>();
        foreach (var (value, index) in array.EnumerateArray().Select((value, index) => (value, index)))
        {
            if (value.ValueKind != JsonValueKind.Array) throw new DefinitionException($"'{label}[{index}]' must be an array.");
            var row = value.EnumerateArray().Select((entry, nestedIndex) => entry.ValueKind == JsonValueKind.Number && entry.TryGetDouble(out var number) && double.IsFinite(number) ? number : throw new DefinitionException($"'{label}[{index}][{nestedIndex}]' must be finite.")).ToArray();
            if (row.Length != innerLength) throw new DefinitionException($"'{label}[{index}]' must contain {innerLength} numbers.");
            result.Add(row);
        }
        return result;
    }
    private static double RequiredNumberValue(JsonElement value, string label, double minimum) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum ? number : throw new DefinitionException($"'{label}' must be finite and >= {minimum}.");
    private static IReadOnlyDictionary<string, IReadOnlyList<int>> CoordinateMap(JsonElement value, string label, IReadOnlySet<string> expectedKeys) { Allow(value, label, expectedKeys.ToArray()); var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal); foreach (var key in expectedKeys) result.Add(key, IntArray(RequiredArray(value, key), $"{label}.{key}", 2)); return new ReadOnlyDictionary<string, IReadOnlyList<int>>(result); }
    private static IReadOnlyList<IReadOnlyList<int>> CoordinateArray(JsonElement array, string label, int? exactLength = null) => array.EnumerateArray().Select((value, index) => (IReadOnlyList<int>)IntArray(value, $"{label}[{index}]", exactLength)).ToArray();
    private static IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> NestedCoordinateArray(JsonElement array, string label, int exactPointLength) => array.EnumerateArray().Select((value, index) => (IReadOnlyList<IReadOnlyList<int>>)CoordinateArray(value, $"{label}[{index}]", exactPointLength)).ToArray();
    private static string LowerId(string value, string label) { if (!System.Text.RegularExpressions.Regex.IsMatch(value, LowerIdPattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new DefinitionException($"{label} '{value}' must be a lowercase canonical ID."); return value; }
    private static string AssetId(string value) { if (!System.Text.RegularExpressions.Regex.IsMatch(value, AssetIdPattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new DefinitionException($"AssetId '{value}' must be lowercase and path-safe."); return value; }
    private static T[] Unique<T>(IEnumerable<T> values, Func<T, string> key, string label) { var result = values.ToArray(); var duplicate = result.GroupBy(key, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1); if (duplicate is not null) throw new DefinitionException($"Duplicate {label} ID '{duplicate.Key}'."); return result; }
    private static void ValidateContractField(JsonElement value, string label) { if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) throw new DefinitionException($"Nested contract field '{label}' is unsupported."); }
}
