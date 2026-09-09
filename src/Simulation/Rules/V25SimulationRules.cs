using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;

namespace SoloVsMortal.Simulation.Rules;

public static class V25FixedPoint
{
    public const long ResourceScale = 1_000;
    public const long DensitySyncScale = 1_000_000;

    public static long RoundMilli(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return checked((long)System.Math.Round(value * ResourceScale, MidpointRounding.AwayFromZero));
    }

    public static long RoundMicro(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        return checked((long)System.Math.Round(value * DensitySyncScale, MidpointRounding.AwayFromZero));
    }

    public static double FromMilli(long value) => value / (double)ResourceScale;
    public static double FromMicro(long value) => value / (double)DensitySyncScale;
    public static double QuantizeMilli(double value) => FromMilli(RoundMilli(value));
}

public enum V25EntityKind { Player, Monster, Ally }
public enum V25DamageType { Physical, Fire, Frost, Toxic, Arcane }
public enum V25EncounterType { Normal, Elite, Boss, Arena, Debug }
public enum V25CastPhase { Windup, Active, Recovery, Finished, Canceled }

public readonly record struct V25EncounterFactors(double Hp, double Attack, double Xp, double Coins)
{
    public static V25EncounterFactors For(V25EncounterType type) => type switch
    {
        V25EncounterType.Elite => new(2, 1.15, 3, 3),
        V25EncounterType.Boss => new(8, 1.30, 12, 10),
        V25EncounterType.Arena or V25EncounterType.Debug => new(1, 1, 0, 0),
        _ => new(1, 1, 1, 1),
    };

    public bool GrantsCombatRewards => this != For(V25EncounterType.Arena) && this != For(V25EncounterType.Debug);
}

public readonly record struct V25ArchetypeRatios(double Hp, double Attack, double Defense, double Speed)
{
    public static V25ArchetypeRatios Parse(string archetype) => archetype switch
    {
        "Balanced" => new(1, 1, 1, 1),
        "Tank" => new(1.30, 0.80, 1.25, 0.85),
        "Bruiser" => new(1.15, 1.05, 1.10, 0.95),
        "Assassin" => new(0.80, 1.25, 0.80, 1.20),
        "Ranged" => new(0.90, 1.10, 0.90, 1.05),
        _ => throw new DefinitionException($"Unknown canonical stat archetype '{archetype}'."),
    };
}

public readonly record struct V25StatBlock(
    double MaxHp,
    double Attack,
    double Defense,
    double MoveSpeed,
    double AttackRate,
    double Cp,
    int PowerTier,
    int Rank,
    V25EncounterType EncounterType)
{
    public StatBlock ToLegacyBlock(int experienceReward = 0) => new(
        checked((int)System.Math.Max(1, System.Math.Round(MaxHp, MidpointRounding.AwayFromZero))),
        checked((int)System.Math.Max(0, System.Math.Round(Attack, MidpointRounding.AwayFromZero))),
        checked((int)System.Math.Max(0, System.Math.Round(Defense, MidpointRounding.AwayFromZero))),
        checked((int)System.Math.Max(1, System.Math.Round(MoveSpeed, MidpointRounding.AwayFromZero))),
        Defense,
        MoveSpeed,
        experienceReward);
}

public static class V25CombatRules
{
    private const double TierBase = 1.22;
    private const double RankBase = 1.38;
    private const double LevelSlope = 0.025;
    private const double LevelExponent = 1.10;

    public static double ComputeCp(V25EntityKind kind, int powerTier, int rank, int level, CanonicalBalanceDocument? balance = null)
    {
        if (level is < 1 or > 90) throw new ArgumentOutOfRangeException(nameof(level));
        var expectedRank = (level - 1) / 10 + 1;
        if (rank != expectedRank) throw new InvalidDataException($"Level {level} requires canonical rank {expectedRank}, got {rank}.");
        if (powerTier is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(powerTier));
        var cpDefinition = balance?.Cp;
        var entityFactor = kind switch
        {
            V25EntityKind.Player => cpDefinition?.PlayerFactor ?? 1.0,
            V25EntityKind.Monster => cpDefinition?.MonsterFactor ?? 0.75,
            V25EntityKind.Ally => cpDefinition?.AllyFactor ?? 0.60,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var cp = (cpDefinition?.Base ?? 100) * entityFactor
            * System.Math.Pow(cpDefinition?.TierBase ?? TierBase, powerTier - 1)
            * System.Math.Pow(cpDefinition?.RankBase ?? RankBase, rank - 1)
            * System.Math.Pow(1 + (cpDefinition?.LevelSlope ?? LevelSlope) * (level - 1), cpDefinition?.LevelExponent ?? LevelExponent);
        if (!double.IsFinite(cp) || cp <= 0) throw new InvalidDataException("Canonical CP overflowed or became non-positive.");
        return cp;
    }

    public static V25StatBlock ComputeStats(V25EntityKind kind, int powerTier, string archetype, int rank, int level, V25EncounterType encounter = V25EncounterType.Normal, CanonicalBalanceDocument? balance = null)
    {
        var ratios = V25ArchetypeRatios.Parse(archetype);
        var cp = ComputeCp(kind, powerTier, rank, level, balance);
        var factors = V25EncounterFactors.For(encounter);
        var hpPerCp = balance?.HpPerCp ?? 6;
        var atkPerCp = balance?.AtkPerCp ?? 0.8;
        var defPerSqrtCp = balance?.DefPerSqrtCp ?? 20;
        return new(
            Round(hpPerCp * cp * ratios.Hp * factors.Hp),
            Round(atkPerCp * cp * ratios.Attack * factors.Attack),
            defPerSqrtCp * System.Math.Sqrt(cp) * ratios.Defense,
            130 * ratios.Speed,
            1,
            cp,
            powerTier,
            rank,
            encounter);
    }

    public static double RankPowerScale(int effectiveRank) => effectiveRank is >= 1 and <= 9 ? 1 + 0.20 * (effectiveRank - 1) : throw new ArgumentOutOfRangeException(nameof(effectiveRank));

    public static int MillisecondsToTicksCeil(int milliseconds)
    {
        if (milliseconds < 0) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        return checked((milliseconds * 60 + 999) / 1000);
    }

    public static double Round(double value) => System.Math.Round(value, MidpointRounding.AwayFromZero);
}

public readonly record struct V25HitKey(string CastId, string TargetLifeUid, int HitIndex);
public readonly record struct V25DamageResult(
    V25HitKey HitKey,
    double RawDamage,
    double MitigatedDamage,
    double IncomingDamage,
    double ShieldAbsorbed,
    double HpLoss,
    bool Critical,
    bool Rejected,
    string? RejectionReason);

public static class V25DamageResolver
{
    public static V25DamageResult Resolve(
        V25HitKey hitKey,
        double attackerAttack,
        double skillPower,
        double rankPowerScale,
        double encounterAttackFactor,
        V25DamageType damageType,
        double targetDefense,
        IReadOnlyDictionary<V25DamageType, double>? resistances,
        IReadOnlyList<double>? defensiveReductions,
        double shield,
        double hp,
        bool critical,
        bool invulnerable = false,
        bool immune = false,
        double armorDenominator = 600,
        double criticalMultiplier = 1.5)
    {
        if (invulnerable || immune) return new(hitKey, 0, 0, 0, 0, 0, false, true, immune ? "Immune" : "Invulnerable");
        if (!double.IsFinite(attackerAttack) || !double.IsFinite(skillPower) || !double.IsFinite(rankPowerScale) || attackerAttack < 0 || skillPower < 0)
            return new(hitKey, 0, 0, 0, 0, 0, false, true, "InvalidOffense");
        if (!double.IsFinite(encounterAttackFactor) || encounterAttackFactor < 0) return new(hitKey, 0, 0, 0, 0, 0, false, true, "InvalidEncounterFactor");
        if (!double.IsFinite(armorDenominator) || armorDenominator <= 0 || !double.IsFinite(criticalMultiplier) || criticalMultiplier < 1)
            return new(hitKey, 0, 0, 0, 0, 0, false, true, "InvalidDamageConstants");
        var raw = attackerAttack * skillPower * rankPowerScale * encounterAttackFactor * (critical ? criticalMultiplier : 1);
        if (!double.IsFinite(raw)) return new(hitKey, 0, 0, 0, 0, 0, false, true, "OffenseOverflow");
        var defense = double.IsFinite(targetDefense) ? System.Math.Max(0, targetDefense) : throw new InvalidDataException("Target defense is not finite.");
        var armorMultiplier = 1 - System.Math.Min(defense / (defense + armorDenominator), 0.75);
        var resistance = resistances?.GetValueOrDefault(damageType) ?? 0;
        if (!double.IsFinite(resistance)) throw new InvalidDataException("Target resistance is not finite.");
        resistance = System.Math.Clamp(resistance, 0, 0.75);
        var defensiveMultiplier = 1.0;
        foreach (var reduction in defensiveReductions ?? Array.Empty<double>())
        {
            if (!double.IsFinite(reduction)) throw new InvalidDataException("Defensive reduction is not finite.");
            defensiveMultiplier *= 1 - System.Math.Clamp(reduction, 0, 0.75);
        }
        defensiveMultiplier = System.Math.Max(0.25, defensiveMultiplier);
        var mitigated = raw * armorMultiplier * (1 - resistance) * defensiveMultiplier;
        // Canonical damage has a one-resource-unit minimum before shield absorption.
        // There is deliberately no floor after the shield: a full shielded hit still causes zero HP loss.
        var incoming = mitigated > 0 ? System.Math.Max(1, V25FixedPoint.QuantizeMilli(mitigated)) : 0;
        var safeShield = V25FixedPoint.QuantizeMilli(System.Math.Max(0, shield));
        var safeHp = V25FixedPoint.QuantizeMilli(System.Math.Max(0, hp));
        var shieldAbsorbed = V25FixedPoint.QuantizeMilli(System.Math.Min(safeShield, incoming));
        var hpLoss = V25FixedPoint.QuantizeMilli(System.Math.Min(safeHp, System.Math.Max(0, incoming - shieldAbsorbed)));
        return new(hitKey, raw, mitigated, incoming, shieldAbsorbed, hpLoss, critical, false, null);
    }
}

public readonly record struct V25XpGainResult(int Level, int Xp, int AcceptedXp, int DiscardedXp, bool BreakthroughReady, IReadOnlyList<int> LevelsGained);

public static class V25ProgressionRules
{
    public static int RankFromLevel(int level)
    {
        if (level is < 1 or > 90) throw new ArgumentOutOfRangeException(nameof(level), "Level must be an integer in [1..90].");
        return (level - 1) / 10 + 1;
    }

    public static long XpRequirement(int level, CanonicalXpDefinition? definition = null)
    {
        if (level is < 1 or > 89) throw new ArgumentOutOfRangeException(nameof(level), "Global XP requirement is defined for Level 1..89.");
        return checked((long)System.Math.Round((definition?.Base ?? 80) * System.Math.Pow(level, definition?.Exponent ?? 1.55), MidpointRounding.AwayFromZero));
    }

    public static double Relevance(int playerLevel, int monsterLevel)
    {
        var difference = monsterLevel - playerLevel;
        return difference >= -5 ? 1 : difference >= -10 ? 0.5 : difference >= -20 ? 0.1 : 0;
    }

    public static int KillXp(int playerLevel, int monsterLevel, V25EncounterType encounterType, int profileMaxLevel, CanonicalXpDefinition? definition = null)
    {
        if (playerLevel >= profileMaxLevel) return 0;
        var requirement = XpRequirement(System.Math.Clamp(monsterLevel, 1, 89), definition);
        var multiplier = V25EncounterFactors.For(encounterType).Xp;
        var relevance = Relevance(playerLevel, monsterLevel);
        return checked((int)System.Math.Max(0, System.Math.Round(requirement / (definition?.KillDivisor ?? 8) * multiplier * relevance, MidpointRounding.AwayFromZero)));
    }

    public static V25XpGainResult AddXp(int level, int xp, int amount, int profileMaxLevel, bool breakthroughReady = false, CanonicalXpDefinition? definition = null)
    {
        if (level is < 1 or > 90 || xp < 0 || amount < 0 || profileMaxLevel is < 1 or > 90) throw new InvalidDataException("Invalid canonical player progression input.");
        if (level >= profileMaxLevel) return new(level, xp, 0, amount, false, Array.Empty<int>());
        var available = checked(xp + amount);
        var accepted = amount;
        var discarded = 0;
        var gained = new List<int>();
        while (level < profileMaxLevel)
        {
            var requirement = checked((int)XpRequirement(level, definition));
            if (level % 10 == 0)
            {
                if (available < requirement) break;
                xp = requirement;
                discarded = checked(discarded + available - requirement);
                available = requirement;
                breakthroughReady = true;
                break;
            }
            if (available < requirement) { xp = available; available = 0; break; }
            available -= requirement;
            xp = 0;
            level++;
            gained.Add(level);
            breakthroughReady = false;
        }
        if (level >= profileMaxLevel)
        {
            discarded = checked(discarded + available);
            xp = 0;
            breakthroughReady = false;
        }
        accepted = checked(amount - discarded);
        return new(level, xp, accepted, discarded, breakthroughReady, gained.AsReadOnly());
    }
}

public sealed record V25StatusInstance(
    string SourceId,
    string TargetUid,
    string EffectId,
    long ExpireTick,
    long NextDotTick,
    int Stacks,
    double Potency,
    double SnapshotAttack);

public sealed record V25ShieldInstance(string SourceId, string TargetUid, long ExpireTick, double Amount);

/// <summary>Canonical shield ownership. Amounts are source keyed so refresh and expiry remain deterministic.</summary>
public sealed class V25ShieldStore
{
    public void Clear() => _shields.Clear();
    private readonly Dictionary<(string Target, string Source), V25ShieldInstance> _shields = [];

    public double Total(string targetUid) => _shields.Values.Where(shield => shield.TargetUid == targetUid).Sum(shield => shield.Amount);
    public IReadOnlyList<V25ShieldInstance> Snapshot(string targetUid) => _shields.Values.Where(shield => shield.TargetUid == targetUid)
        .OrderBy(shield => shield.ExpireTick).ThenBy(shield => shield.SourceId, StringComparer.Ordinal).ToArray();

    public void Apply(string sourceId, string targetUid, long currentTick, long durationTicks, double grant, double maxHp)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetUid) || !double.IsFinite(grant) || grant <= 0 || !double.IsFinite(maxHp) || maxHp <= 0) return;
        var cap = maxHp * 0.5;
        var key = (targetUid, sourceId);
        var expiry = checked(currentTick + Math.Max(1, durationTicks));
        var amount = V25FixedPoint.QuantizeMilli(grant);
        if (_shields.TryGetValue(key, out var previous))
        {
            amount = Math.Max(previous.Amount, grant); // same source refreshes; it never stacks.
            expiry = Math.Max(previous.ExpireTick, expiry);
        }
        var other = Total(targetUid) - (_shields.TryGetValue(key, out previous) ? previous.Amount : 0);
        amount = V25FixedPoint.QuantizeMilli(Math.Min(amount, Math.Max(0, cap - other)));
        if (amount <= 0) { _shields.Remove(key); return; }
        _shields[key] = new(sourceId, targetUid, expiry, amount);
    }

    public double Consume(string targetUid, double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) return 0;
        var remaining = V25FixedPoint.QuantizeMilli(amount);
        var absorbed = 0.0;
        foreach (var shield in _shields.Values.Where(item => item.TargetUid == targetUid).OrderBy(item => item.ExpireTick).ThenBy(item => item.SourceId, StringComparer.Ordinal).ToArray())
        {
            if (remaining <= 0) break;
            var taken = V25FixedPoint.QuantizeMilli(Math.Min(shield.Amount, remaining));
            absorbed += taken;
            remaining -= taken;
            var left = V25FixedPoint.QuantizeMilli(shield.Amount - taken);
            var key = (shield.TargetUid, shield.SourceId);
            if (left <= 0) _shields.Remove(key);
            else _shields[key] = shield with { Amount = left };
        }
        return absorbed;
    }

    public void Expire(long currentTick)
    {
        foreach (var shield in _shields.Values.Where(item => item.ExpireTick <= currentTick).ToArray())
            _shields.Remove((shield.TargetUid, shield.SourceId));
    }

    /// <summary>Stages and replaces one actor's source-keyed shields without recalculating authored values.</summary>
    public void Restore(string targetUid, IReadOnlyList<V25ShieldInstance> snapshot, long currentTick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid); ArgumentNullException.ThrowIfNull(snapshot);
        var staged = snapshot.ToArray();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shield in staged)
        {
            if (shield is null || shield.TargetUid != targetUid || string.IsNullOrWhiteSpace(shield.SourceId) || !sourceIds.Add(shield.SourceId) || shield.ExpireTick < currentTick || !double.IsFinite(shield.Amount) || shield.Amount <= 0)
                throw new InvalidDataException($"Invalid canonical shield snapshot for '{targetUid}'.");
        }
        foreach (var old in _shields.Values.Where(item => item.TargetUid == targetUid).ToArray()) _shields.Remove((old.TargetUid, old.SourceId));
        foreach (var shield in staged) _shields[(shield.TargetUid, shield.SourceId)] = shield;
    }

    /// <summary>Compatibility bridge for old state restore; canonical effects use Apply instead.</summary>
    public void SetLegacy(string targetUid, double amount)
    {
        foreach (var shield in _shields.Values.Where(item => item.TargetUid == targetUid).ToArray()) _shields.Remove((shield.TargetUid, shield.SourceId));
        if (double.IsFinite(amount) && amount > 0) _shields[(targetUid, "legacy")] = new("legacy", targetUid, long.MaxValue, V25FixedPoint.QuantizeMilli(amount));
    }
}

/// <summary>Mutable status ownership remains in the combat system; no status callback emits gameplay damage.</summary>
public sealed class V25StatusStore
{
    private readonly Dictionary<(string Target, string Effect, string Source), V25StatusInstance> _statuses = [];

    public IReadOnlyList<V25StatusInstance> Snapshot(string targetUid) => _statuses.Values.Where(status => status.TargetUid == targetUid).OrderBy(status => status.EffectId, StringComparer.Ordinal).ThenBy(status => status.SourceId, StringComparer.Ordinal).ToArray();

    public void Apply(string sourceId, string targetUid, string effectId, long currentTick, double potency, double snapshotAttack)
    {
        var duration = effectId switch { "burn" => 180, "poison" => 240, "chill" => 120, "stun" => 30, "guard" => 120, "ward" => 180, "haste" => 180, "veil" => 180, "taunt" => 120, _ => 0 };
        if (duration <= 0) return;
        var key = (targetUid, effectId, sourceId);
        var expire = checked(currentTick + duration);
        var nextDot = effectId is "burn" or "poison" ? checked(currentTick + 60) : long.MaxValue;
        if (_statuses.TryGetValue(key, out var previous))
        {
            _statuses[key] = previous with { ExpireTick = System.Math.Max(previous.ExpireTick, expire), NextDotTick = System.Math.Min(previous.NextDotTick, nextDot), Potency = System.Math.Max(previous.Potency, potency), SnapshotAttack = System.Math.Max(previous.SnapshotAttack, snapshotAttack) };
            return;
        }
        if (effectId == "poison" && _statuses.Values.Count(status => status.TargetUid == targetUid && status.EffectId == effectId) >= 3)
        {
            var oldest = _statuses.Values.Where(status => status.TargetUid == targetUid && status.EffectId == effectId).OrderBy(status => status.ExpireTick).ThenBy(status => status.SourceId, StringComparer.Ordinal).First();
            _statuses.Remove((oldest.TargetUid, oldest.EffectId, oldest.SourceId));
        }
        if (effectId is "chill" or "stun" or "guard" or "haste" or "veil" or "taunt")
            foreach (var old in _statuses.Values.Where(status => status.TargetUid == targetUid && status.EffectId == effectId).ToArray()) _statuses.Remove((old.TargetUid, old.EffectId, old.SourceId));
        _statuses[key] = new(sourceId, targetUid, effectId, expire, nextDot, 1, potency, snapshotAttack);
    }

    public void RemoveEffects(string targetUid, params string[] effects)
    {
        var set = effects.ToHashSet(StringComparer.Ordinal);
        foreach (var status in _statuses.Values.Where(item => item.TargetUid == targetUid && set.Contains(item.EffectId)).ToArray()) _statuses.Remove((status.TargetUid, status.EffectId, status.SourceId));
    }

    public void RemoveSource(string targetUid, string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return;
        foreach (var status in _statuses.Values.Where(item => item.TargetUid == targetUid && item.SourceId == sourceId).ToArray())
            _statuses.Remove((status.TargetUid, status.EffectId, status.SourceId));
    }

    public void Tick(long currentTick, Action<V25StatusInstance> dot)
    {
        foreach (var status in _statuses.Values.ToArray())
        {
            var current = status;
            while (current.NextDotTick <= currentTick && current.NextDotTick <= current.ExpireTick)
            {
                dot(current);
                current = current with { NextDotTick = checked(current.NextDotTick + 60) };
            }
            if (currentTick >= current.ExpireTick) _statuses.Remove((current.TargetUid, current.EffectId, current.SourceId));
            else _statuses[(current.TargetUid, current.EffectId, current.SourceId)] = current;
        }
    }

    public IReadOnlyList<double> DefensiveReductions(string targetUid) => _statuses.Values.Where(status => status.TargetUid == targetUid && status.EffectId == "guard").Select(_ => 0.35).ToArray();
    public bool Has(string targetUid, string effectId) => _statuses.Values.Any(status => status.TargetUid == targetUid && status.EffectId == effectId);
    /// <summary>Restores exact timers/potency after the enclosing save has validated the full actor graph.</summary>
    public void Restore(string targetUid, IReadOnlyList<V25StatusInstance> snapshot, long currentTick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUid); ArgumentNullException.ThrowIfNull(snapshot);
        var staged = snapshot.ToArray();
        var keys = new HashSet<(string Effect, string Source)>();
        foreach (var status in staged)
        {
            if (status is null || status.TargetUid != targetUid || string.IsNullOrWhiteSpace(status.SourceId) || string.IsNullOrWhiteSpace(status.EffectId) || !keys.Add((status.EffectId, status.SourceId)) || status.ExpireTick < currentTick || status.NextDotTick < 0 || status.Stacks is < 1 or > 3 || !double.IsFinite(status.Potency) || !double.IsFinite(status.SnapshotAttack))
                throw new InvalidDataException($"Invalid canonical status snapshot for '{targetUid}'.");
        }
        foreach (var old in _statuses.Values.Where(item => item.TargetUid == targetUid).ToArray()) _statuses.Remove((old.TargetUid, old.EffectId, old.SourceId));
        foreach (var status in staged) _statuses[(status.TargetUid, status.EffectId, status.SourceId)] = status;
    }
    public void Clear() => _statuses.Clear();
}
