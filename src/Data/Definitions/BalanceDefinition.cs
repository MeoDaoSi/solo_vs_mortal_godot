using System.Collections.ObjectModel;

namespace SoloVsMortal.Data.Definitions;

public enum EntityType { Player, Monster, Soul }
public enum TierId { Tier1, Tier2, Tier3 }
public enum StatArchetypeId { Balanced, Offensive, Defensive }
public enum MaterialId { SpiritCrystal, BeastCore }
public enum PillId { BreakthroughMinor, BreakthroughMajor, Vitality, Power, Guard, Swift }

public sealed record StatRatios(double Hp, double Atk, double Def, double Speed);
public sealed record CpGrowth(double BaseCp, double RankMultiplier, double LevelGrowthRate, double LevelGrowthRate2);
public sealed record CombatPacing(double MoveSpeedPerSpeed, double AttackCooldownNumerator, double MinimumAttackCooldown);
public sealed record StatArchetypeDefinition(string DisplayName, StatRatios Ratio, StatRatios Growth);
public sealed record TierRankRangeDefinition(string DisplayName, int MinimumRank, int MaximumRank);
public sealed record SpeciesDefinition(
    string Id,
    string DisplayName,
    TierId PowerTier,
    StatArchetypeId StatArchetype,
    StatRatios StatModifier);
public sealed record ProgressionDefinition(
    int XpBase,
    int XpLinear,
    int XpQuadratic,
    int MonsterXpBase,
    int MonsterXpPerLevel,
    double BreakthroughBaseChance,
    double CrystalDropChance,
    double BeastCoreDropChance);
public sealed record PillDefinition(
    PillId Id,
    string StableId,
    string DisplayName,
    IReadOnlyDictionary<MaterialId, int> Recipe,
    double? BreakthroughChance = null,
    double? DurationSeconds = null,
    SoloVsMortal.Simulation.Rules.StatModifiers? Modifiers = null);

public static class BalanceDefinition
{
    public const int LevelsPerRank = 10;
    public const int MaximumRank = 9;
    public const int MaximumLevel = MaximumRank * LevelsPerRank;

    public static readonly StatRatios BaseStatRatio = new(1, 0.3, 0.1, 0.3);
    public static readonly ProgressionDefinition Progression = new(100, 25, 5, 25, 10, 0.2, 0.65, 0.2);

    public static readonly IReadOnlyDictionary<PillId, PillDefinition> Pills = ReadOnly(new Dictionary<PillId, PillDefinition>
    {
        [PillId.BreakthroughMinor] = new(PillId.BreakthroughMinor, "BREAKTHROUGH_MINOR", "Tiểu Phá Cảnh Đan", Recipe(3, 1), BreakthroughChance: 0.15),
        [PillId.BreakthroughMajor] = new(PillId.BreakthroughMajor, "BREAKTHROUGH_MAJOR", "Đại Phá Cảnh Đan", Recipe(7, 3), BreakthroughChance: 0.35),
        [PillId.Vitality] = new(PillId.Vitality, "VITALITY_PILL", "Sinh Mệnh Đan", Recipe(2, 1), DurationSeconds: 60, Modifiers: new(HpPercent: 0.2)),
        [PillId.Power] = new(PillId.Power, "POWER_PILL", "Cường Công Đan", Recipe(2, 1), DurationSeconds: 60, Modifiers: new(AtkPercent: 0.2)),
        [PillId.Guard] = new(PillId.Guard, "GUARD_PILL", "Hộ Thể Đan", Recipe(2, 1), DurationSeconds: 60, Modifiers: new(DefPercent: 0.2)),
        [PillId.Swift] = new(PillId.Swift, "SWIFT_PILL", "Tật Hành Đan", Recipe(2, 1), DurationSeconds: 60, Modifiers: new(SpeedPercent: 0.2)),
    });

    public static readonly IReadOnlyDictionary<EntityType, CpGrowth> Types = ReadOnly(new Dictionary<EntityType, CpGrowth>
    {
        [EntityType.Player] = new(100, 1.2, 0.005, 0.0000633),
        [EntityType.Monster] = new(118, 1.2, 0.005, 0.0000633),
        [EntityType.Soul] = new(84, 1.2, 0.005, 0.0000633),
    });

    public static readonly IReadOnlyDictionary<EntityType, CombatPacing> CombatPacingByType = ReadOnly(new Dictionary<EntityType, CombatPacing>
    {
        [EntityType.Player] = new(140.0 / 30.0, 15, 0.15),
        [EntityType.Monster] = new(55.0 / 35.4, 35.4, 0.2),
        [EntityType.Soul] = new(60.0 / 25.2, 25.2, 0.15),
    });

    public static readonly IReadOnlyDictionary<StatArchetypeId, StatArchetypeDefinition> StatArchetypes = ReadOnly(new Dictionary<StatArchetypeId, StatArchetypeDefinition>
    {
        [StatArchetypeId.Balanced] = new("Cân Bằng", new(1, 1, 1, 1), new(0, 0, 0, 0)),
        [StatArchetypeId.Offensive] = new("Công Kích", new(0.9, 1.3, 0.8, 1.2), new(0, 0, 0, 0)),
        [StatArchetypeId.Defensive] = new("Phòng Thủ", new(1.5, 0.7, 1.8, 0.8), new(0, 0, 0, 0)),
    });

    public static readonly IReadOnlyDictionary<TierId, TierRankRangeDefinition> TierRankRanges = ReadOnly(new Dictionary<TierId, TierRankRangeDefinition>
    {
        [TierId.Tier1] = new("Phàm Chủng", 1, 3),
        [TierId.Tier2] = new("Linh Chủng", 4, 6),
        [TierId.Tier3] = new("Thần Chủng", 7, 9),
    });

    public static readonly IReadOnlyDictionary<string, SpeciesDefinition> Species =
        new ReadOnlyDictionary<string, SpeciesDefinition>(new Dictionary<string, SpeciesDefinition>(StringComparer.Ordinal)
        {
            ["SKELETON"] = SpeciesOf("SKELETON", "Skeleton", TierId.Tier1, StatArchetypeId.Balanced),
            ["GOBLIN"] = SpeciesOf("GOBLIN", "Goblin", TierId.Tier1, StatArchetypeId.Defensive),
            ["GOLEM"] = SpeciesOf("GOLEM", "Golem Đất", TierId.Tier2, StatArchetypeId.Defensive),
            ["WOLF"] = SpeciesOf("WOLF", "Wolf", TierId.Tier2, StatArchetypeId.Defensive),
            ["DRAGON"] = SpeciesOf("DRAGON", "Dragon", TierId.Tier3, StatArchetypeId.Offensive),
        });

    public static readonly IReadOnlyDictionary<int, string> RankDisplayNames = ReadOnly(
        Enumerable.Range(1, MaximumRank).ToDictionary(rank => rank, rank => $"Cấp Bậc {rank}"));

    public static CpGrowth Type(EntityType type) => Types[type];
    public static CombatPacing Pacing(EntityType type) => CombatPacingByType[type];
    public static StatArchetypeDefinition Archetype(StatArchetypeId id) => StatArchetypes[id];
    public static TierRankRangeDefinition TierRange(TierId id) => TierRankRanges[id];

    public static SpeciesDefinition SpeciesById(string id) =>
        Species.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"No species configured for '{id}'.");

    public static bool IsValidRank(int rank) => rank is >= 1 and <= MaximumRank;
    public static bool IsValidLevel(int level) => level is >= 1 and <= MaximumLevel;

    public static void RequireValidRank(int rank)
    {
        if (!IsValidRank(rank)) throw new ArgumentOutOfRangeException(nameof(rank), rank, $"Rank must be in [1..{MaximumRank}].");
    }

    public static void RequireValidLevel(int level)
    {
        if (!IsValidLevel(level)) throw new ArgumentOutOfRangeException(nameof(level), level, $"Level must be in [1..{MaximumLevel}].");
    }

    private static SpeciesDefinition SpeciesOf(string id, string displayName, TierId tier, StatArchetypeId archetype) =>
        new(id, displayName, tier, archetype, new StatRatios(1, 1, 1, 1));

    private static IReadOnlyDictionary<MaterialId, int> Recipe(int crystals, int cores) =>
        ReadOnly(new Dictionary<MaterialId, int> { [MaterialId.SpiritCrystal] = crystals, [MaterialId.BeastCore] = cores });

    private static ReadOnlyDictionary<TKey, TValue> ReadOnly<TKey, TValue>(Dictionary<TKey, TValue> values)
        where TKey : notnull => new(values);
}
