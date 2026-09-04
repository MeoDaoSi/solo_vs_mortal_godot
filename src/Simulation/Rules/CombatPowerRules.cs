using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.Rules;

public sealed record StatModifiers(
    double HpPercent = 0,
    double AtkPercent = 0,
    double DefPercent = 0,
    double SpeedPercent = 0,
    double HpFlat = 0,
    double AtkFlat = 0,
    double DefFlat = 0,
    double SpeedFlat = 0,
    double Scalar = 1);

public sealed record EntityStatContext(
    EntityType Type,
    int Level,
    int? Rank = null,
    string? SpeciesId = null,
    StatArchetypeId? Archetype = null,
    int ExperienceReward = 0);

public readonly record struct CombatStats(int Hp, int Atk, int Def, int Speed, double DefRaw, double SpeedRaw);
public readonly record struct StatBlock(int Hp, int Atk, int Def, int Speed, double DefRaw, double SpeedRaw, int ExperienceReward);

public static class CombatPowerRules
{
    private readonly record struct RawStats(double Hp, double Atk, double Def, double Speed);

    public static int RankStartLevel(int rank)
    {
        BalanceDefinition.RequireValidRank(rank);
        return (rank - 1) * BalanceDefinition.LevelsPerRank + 1;
    }

    public static int RankEndLevel(int rank) => RankStartLevel(rank) + BalanceDefinition.LevelsPerRank - 1;

    public static int GlobalLevelToRank(int level)
    {
        BalanceDefinition.RequireValidLevel(level);
        return (int)System.Math.Ceiling((double)level / BalanceDefinition.LevelsPerRank);
    }

    public static double GetCp(EntityType type, int level, int rank)
    {
        BalanceDefinition.RequireValidLevel(level);
        BalanceDefinition.RequireValidRank(rank);
        var config = BalanceDefinition.Type(type);
        var n = level - 1;
        return config.BaseCp
            * System.Math.Pow(config.RankMultiplier, rank - 1)
            * (1 + config.LevelGrowthRate * n + config.LevelGrowthRate2 * n * n);
    }

    public static StatBlock GetStatBlock(EntityStatContext context, IReadOnlyList<StatModifiers>? modifiers = null)
    {
        var rank = context.Rank ?? GlobalLevelToRank(context.Level);
        var raw = RawBaseStats(context.Type, context.Level, rank);
        raw = ApplyArchetype(raw, ResolveArchetype(context), context.Level);
        raw = ApplySpecies(raw, context.SpeciesId);
        raw = ApplyModifiers(raw, modifiers ?? Array.Empty<StatModifiers>());
        var rounded = Round(raw);
        return new StatBlock(rounded.Hp, rounded.Atk, rounded.Def, rounded.Speed, rounded.DefRaw, rounded.SpeedRaw, context.ExperienceReward);
    }

    public static double GetMovementSpeed(EntityType type, double speedRaw) =>
        System.Math.Max(0, speedRaw) * BalanceDefinition.Pacing(type).MoveSpeedPerSpeed;

    public static double GetAttackCooldown(EntityType type, double speedRaw)
    {
        if (!double.IsFinite(speedRaw) || speedRaw <= 0) return double.PositiveInfinity;
        var pacing = BalanceDefinition.Pacing(type);
        return System.Math.Max(pacing.MinimumAttackCooldown, pacing.AttackCooldownNumerator / speedRaw);
    }

    public static string RankDisplayName(int rank) =>
        BalanceDefinition.RankDisplayNames.TryGetValue(rank, out var name) ? name : $"Cấp Bậc {rank}";

    public static string RankKey(int rank) => rank.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static RawStats RawBaseStats(EntityType type, int level, int rank)
    {
        var cp = GetCp(type, level, rank);
        var ratio = BalanceDefinition.BaseStatRatio;
        return new(cp * ratio.Hp, cp * ratio.Atk, cp * ratio.Def, cp * ratio.Speed);
    }

    private static StatArchetypeId ResolveArchetype(EntityStatContext context)
    {
        if (context.Archetype is { } explicitArchetype) return explicitArchetype;
        if (context.SpeciesId is { } speciesId) return BalanceDefinition.SpeciesById(speciesId).StatArchetype;
        return StatArchetypeId.Balanced;
    }

    private static RawStats ApplyArchetype(RawStats raw, StatArchetypeId id, int level)
    {
        var definition = BalanceDefinition.Archetype(id);
        var ratio = definition.Ratio;
        var growth = definition.Growth;
        var scale = level - 1;
        return new(
            raw.Hp * ratio.Hp * (1 + growth.Hp * scale),
            raw.Atk * ratio.Atk * (1 + growth.Atk * scale),
            raw.Def * ratio.Def * (1 + growth.Def * scale),
            raw.Speed * ratio.Speed * (1 + growth.Speed * scale));
    }

    private static RawStats ApplySpecies(RawStats raw, string? speciesId)
    {
        if (speciesId is null) return raw;
        var modifier = BalanceDefinition.SpeciesById(speciesId).StatModifier;
        return new(raw.Hp * modifier.Hp, raw.Atk * modifier.Atk, raw.Def * modifier.Def, raw.Speed * modifier.Speed);
    }

    private static RawStats ApplyModifiers(RawStats raw, IReadOnlyList<StatModifiers> modifiers)
    {
        var scalar = 1.0;
        var hpFactor = 1.0;
        var atkFactor = 1.0;
        var defFactor = 1.0;
        var speedFactor = 1.0;
        var hpFlat = 0.0;
        var atkFlat = 0.0;
        var defFlat = 0.0;
        var speedFlat = 0.0;
        foreach (var modifier in modifiers)
        {
            scalar *= modifier.Scalar;
            hpFactor *= 1 + modifier.HpPercent;
            atkFactor *= 1 + modifier.AtkPercent;
            defFactor *= 1 + modifier.DefPercent;
            speedFactor *= 1 + modifier.SpeedPercent;
            hpFlat += modifier.HpFlat;
            atkFlat += modifier.AtkFlat;
            defFlat += modifier.DefFlat;
            speedFlat += modifier.SpeedFlat;
        }

        return new(
            raw.Hp * hpFactor * scalar + hpFlat,
            raw.Atk * atkFactor * scalar + atkFlat,
            raw.Def * defFactor * scalar + defFlat,
            raw.Speed * speedFactor * scalar + speedFlat);
    }

    private static CombatStats Round(RawStats raw) => new(
        (int)System.Math.Floor(raw.Hp),
        (int)System.Math.Floor(raw.Atk),
        (int)System.Math.Floor(raw.Def),
        (int)System.Math.Floor(raw.Speed),
        raw.Def,
        raw.Speed);
}
