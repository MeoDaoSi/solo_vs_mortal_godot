using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Simulation.Systems;

public readonly record struct PlayerBounds(double Width, double Height);

public sealed class PlayerSystem
{
    public const double BodyRadius = 12;
    public const double CanonicalBodyRadius = 10;
    private readonly EventBus _events;
    private readonly UidGenerator _uids;
    private PlayerBounds _bounds;
    private IReadOnlyList<Rect> _colliders;
    private readonly CanonicalContentRegistry? _canonical;
    private IReadOnlyList<StatModifiers> _canonicalModifiers = Array.Empty<StatModifiers>();

    public PlayerSystem(EventBus events, UidGenerator uids, PlayerDefinition definition, Vec2 start, PlayerBounds bounds, IReadOnlyList<Rect>? colliders = null, CanonicalContentRegistry? canonical = null)
    {
        _events = events;
        _uids = uids;
        _bounds = bounds;
        _colliders = colliders ?? Array.Empty<Rect>();
        _canonical = canonical;
        var stats = canonical is null
            ? CombatPowerRules.GetStatBlock(new EntityStatContext(EntityType.Player, 1, 1, Archetype: StatArchetypeId.Balanced))
            : V25CombatRules.ComputeStats(V25EntityKind.Player, 1, "Balanced", 1, 1, balance: canonical?.Balance).ToLegacyBlock();
        State = new PlayerState(uids.Create("player"), start, stats, definition.AttackRange)
        {
            CombatStyleId = canonical is null ? "fist" : "sword",
            MaxSpirit = canonical is null ? 0 : CanonicalMaxSpirit(1, 1),
            CurrentSpirit = canonical is null ? 0 : CanonicalMaxSpirit(1, 1),
        };
    }

    public PlayerState State { get; }
    public bool CanonicalMode => _canonical is not null;
    public Func<Vec2, bool>? TerrainEntryAllowed { get; set; }
    public double EnvironmentMoveMultiplier { get; set; } = 1;
    public double MovementSpeed => CanonicalMode
        ? EnvironmentMoveMultiplier * State.Stats.SpeedRaw * (State.Statuses.Has(State.Uid, "haste") ? 1.20 : 1) * (State.Statuses.Has(State.Uid, "chill") ? 0.75 : 1)
        : CombatPowerRules.GetMovementSpeed(EntityType.Player, State.Stats.SpeedRaw);
    public void SetColliders(IReadOnlyList<Rect> colliders) => _colliders = colliders ?? throw new ArgumentNullException(nameof(colliders));
    public void SetBounds(PlayerBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bounds));
        _bounds = bounds;
        SetPosition(State.Position);
    }
    public void SetPosition(Vec2 position) => State.Position = new Vec2(Vec2.Clamp(position.X, 0, _bounds.Width), Vec2.Clamp(position.Y, 0, _bounds.Height));
    public bool IsPositionFree(Vec2 position) => CanStand(Vec2.Clamp(position.X, 0, _bounds.Width), Vec2.Clamp(position.Y, 0, _bounds.Height));
    public bool IsPositionFree(Vec2 position, double radius) => V25Navigation.IsFree(new Vec2(Vec2.Clamp(position.X, 0, _bounds.Width), Vec2.Clamp(position.Y, 0, _bounds.Height)), new V25WorldBounds(_bounds.Width, _bounds.Height), _colliders, radius);

    public bool IsSegmentFree(Vec2 start, Vec2 end, double stepUnits = 2)
    {
        if (!CanonicalMode || !double.IsFinite(stepUnits) || stepUnits <= 0) return IsPositionFree(end);
        var distance = start.DistanceTo(end);
        var steps = System.Math.Max(1, (int)System.Math.Ceiling(distance / stepUnits));
        for (var index = 1; index <= steps; index++)
        {
            var t = index / (double)steps;
            var point = new Vec2(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            if (!IsPositionFree(point)) return false;
        }
        return true;
    }

    public bool IsSegmentFree(Vec2 start, Vec2 end, double radius, double stepUnits = 4) => V25Navigation.SegmentFree(start, end, new V25WorldBounds(_bounds.Width, _bounds.Height), _colliders, radius, stepUnits);
    public Vec2? FindNearestFree(Vec2 origin, double radius, double maxRadius = 48) => V25Navigation.FindNearestFree(origin, new V25WorldBounds(_bounds.Width, _bounds.Height), _colliders, radius, Math.Min(maxRadius, 48));
    public Vec2 FindReachableNextStep(Vec2 start, Vec2 goal, double radius, double distance) => V25Navigation.NextStep(start, goal, distance, new V25WorldBounds(_bounds.Width, _bounds.Height), _colliders, radius);

    public void Update(double deltaSeconds, Vec2 input)
    {
        if (!State.Alive || input == Vec2.Zero || CanonicalMode && (State.Statuses.Has(State.Uid, "stun") || State.DodgeRemainingTicks > 0)) return;
        var direction = input.Normalized();
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        if (input != Vec2.Zero) State.Facing = input.Normalized();
        var distance = MovementSpeed * deltaSeconds;
        var nextX = Vec2.Clamp(State.Position.X + direction.X * distance, 0, _bounds.Width);
        var nextY = Vec2.Clamp(State.Position.Y + direction.Y * distance, 0, _bounds.Height);
        if (CanStand(nextX, State.Position.Y)) State.Position = State.Position with { X = nextX };
        if (CanStand(State.Position.X, nextY)) State.Position = State.Position with { Y = nextY };
    }

    public void SetProgression(int level, int rank)
    {
        if (CanonicalMode)
        {
            if (level < 1 || level > _canonical!.Balance.MaxLevel || rank != V25ProgressionRules.RankFromLevel(level)) throw new InvalidDataException($"Canonical player progression {level}/{rank} is invalid.");
        }
        else
        {
            BalanceDefinition.RequireValidLevel(level);
            BalanceDefinition.RequireValidRank(rank);
            if (CombatPowerRules.GlobalLevelToRank(level) != rank) throw new InvalidOperationException($"Player level {level} does not belong to rank {rank}.");
        }
        State.Level = level;
        State.Rank = rank;
        RecomputeStats();
    }

    public void Restore(int level, int rank, int xp, double currentHp, string? titleDisplayName)
    {
        if (CanonicalMode)
        {
            if (level < 1 || level > _canonical!.Balance.MaxLevel || rank != V25ProgressionRules.RankFromLevel(level) || xp < 0 || !double.IsFinite(currentHp)) throw new InvalidDataException("Canonical player restore is invalid.");
        }
        else
        {
            if (level < 1 || level > BalanceDefinition.MaximumLevel) level = 1;
            var expectedRank = CombatPowerRules.GlobalLevelToRank(level);
            rank = rank >= 1 && rank <= BalanceDefinition.MaximumRank && rank == expectedRank ? rank : expectedRank;
        }
        State.Level = level; State.Rank = rank; State.Xp = System.Math.Max(0, xp);
        State.BreakthroughReady = false;
        RecomputeStats();
        if (currentHp < 0 || currentHp > State.MaxHp) throw new InvalidDataException("Player current HP is outside the authored maximum.");
        State.CurrentHp = currentHp;
        if (CanonicalMode) State.CurrentSpirit = System.Math.Min(State.CurrentSpirit, State.MaxSpirit);
        if (!string.IsNullOrWhiteSpace(titleDisplayName)) State.TitleDisplayName = titleDisplayName.Trim()[..System.Math.Min(120, titleDisplayName.Trim().Length)];
    }

    public void RecomputeStats(IReadOnlyList<StatModifiers>? modifiers = null)
    {
        var oldMaxHp = State.MaxHp;
        var oldMaxSpirit = State.MaxSpirit;
        if (CanonicalMode)
        {
            var v25 = V25CombatRules.ComputeStats(V25EntityKind.Player, 1, "Balanced", State.Rank, State.Level, balance: _canonical!.Balance);
            var applied = ApplyCanonicalModifiers(v25, _canonicalModifiers);
            State.Stats = new StatBlock(
                checked((int)Math.Max(1, Math.Round(applied.Hp, MidpointRounding.AwayFromZero))),
                checked((int)Math.Max(0, Math.Round(applied.Atk, MidpointRounding.AwayFromZero))),
                checked((int)Math.Max(0, Math.Round(applied.Def, MidpointRounding.AwayFromZero))),
                checked((int)Math.Max(1, Math.Round(applied.Speed, MidpointRounding.AwayFromZero))),
                applied.Def, applied.Speed, 0);
            State.MaxHp = applied.Hp;
            State.MaxSpirit = CanonicalMaxSpirit(State.Level, State.Rank);
            if (oldMaxHp <= 0) State.CurrentHp = State.MaxHp;
            else State.CurrentHp = System.Math.Min(State.CurrentHp, State.MaxHp);
            if (oldMaxSpirit <= 0) State.CurrentSpirit = State.MaxSpirit;
            else State.CurrentSpirit = System.Math.Min(State.CurrentSpirit, State.MaxSpirit);
            State.Shield = System.Math.Min(State.Shield, State.MaxHp * 0.5);
            return;
        }
        var ratio = State.MaxHp > 0 ? State.CurrentHp / State.MaxHp : 1;
        State.Stats = CombatPowerRules.GetStatBlock(new EntityStatContext(EntityType.Player, State.Level, State.Rank, Archetype: StatArchetypeId.Balanced), modifiers);
        State.MaxHp = State.Stats.Hp;
        State.CurrentHp = System.Math.Min(State.MaxHp, System.Math.Max(0, State.MaxHp * ratio));
    }

    internal void SetCanonicalModifiers(IReadOnlyList<StatModifiers> modifiers)
    {
        _canonicalModifiers = modifiers?.ToArray() ?? throw new ArgumentNullException(nameof(modifiers));
        RecomputeStats();
    }

    private static (double Hp, double Atk, double Def, double Speed) ApplyCanonicalModifiers(V25StatBlock baseStats, IReadOnlyList<StatModifiers> modifiers)
    {
        var scalar = 1.0; var hpFactor = 1.0; var atkFactor = 1.0; var defFactor = 1.0; var speedFactor = 1.0;
        var hpFlat = 0.0; var atkFlat = 0.0; var defFlat = 0.0; var speedFlat = 0.0;
        foreach (var modifier in modifiers)
        {
            scalar *= modifier.Scalar; hpFactor *= 1 + modifier.HpPercent; atkFactor *= 1 + modifier.AtkPercent;
            defFactor *= 1 + modifier.DefPercent; speedFactor *= 1 + modifier.SpeedPercent;
            hpFlat += modifier.HpFlat; atkFlat += modifier.AtkFlat; defFlat += modifier.DefFlat; speedFlat += modifier.SpeedFlat;
        }
        var hp = baseStats.MaxHp * hpFactor * scalar + hpFlat;
        var atk = baseStats.Attack * atkFactor * scalar + atkFlat;
        var def = baseStats.Defense * defFactor * scalar + defFlat;
        var speed = baseStats.MoveSpeed * speedFactor * scalar + speedFlat;
        if (!double.IsFinite(hp) || !double.IsFinite(atk) || !double.IsFinite(def) || !double.IsFinite(speed)) throw new InvalidDataException("Canonical player modifier result is not finite.");
        return (Math.Max(1, hp), Math.Max(0, atk), Math.Max(0, def), Math.Clamp(speed, 40, 240));
    }

    public bool TryStartDodge(Vec2 moveInput, Vec2 aim, int cooldownTicks, int durationTicks, int invulnerabilityTicks, double distance)
    {
        if (!CanonicalMode || !State.Alive || State.Statuses.Has(State.Uid, "stun") || State.DodgeCooldownTicks > 0 || State.DodgeRemainingTicks > 0 || durationTicks <= 0 || !double.IsFinite(distance) || distance <= 0) return false;
        var direction = moveInput != Vec2.Zero ? moveInput.Normalized() : new Vec2(aim.X - State.Position.X, aim.Y - State.Position.Y).Normalized();
        if (direction == Vec2.Zero) direction = State.Facing;
        State.Facing = direction;
        var stepDistance = distance / durationTicks;
        var firstPosition = SweptPosition(State.Position, direction, stepDistance);
        if (firstPosition == State.Position) return false;
        State.DodgeDirection = direction;
        State.DodgeDistanceRemaining = distance;
        State.DodgeStepDistance = stepDistance;
        State.DodgeCooldownTicks = cooldownTicks;
        State.DodgeRemainingTicks = durationTicks;
        State.DodgeInvulnerabilityTicks = invulnerabilityTicks;
        AdvanceDodgeStep();
        State.DodgeRemainingTicks = System.Math.Max(0, State.DodgeRemainingTicks - 1);
        return true;
    }

    public bool TryMoveSwept(Vec2 direction, double distance)
    {
        if (!CanonicalMode || !State.Alive || !double.IsFinite(distance) || distance <= 0) return false;
        var unit = direction.Normalized();
        if (unit == Vec2.Zero) return false;
        var start = State.Position;
        State.Position = SweptPosition(start, unit, distance);
        return State.Position != start;
    }

    /// <summary>Returns the furthest collision-free canonical point along a movement segment.</summary>
    public Vec2 SweptPosition(Vec2 start, Vec2 direction, double distance)
    {
        if (!CanonicalMode || !double.IsFinite(distance) || distance <= 0) return start;
        var unit = direction.Normalized();
        if (unit == Vec2.Zero) return start;
        var clampedStart = new Vec2(Vec2.Clamp(start.X, 0, _bounds.Width), Vec2.Clamp(start.Y, 0, _bounds.Height));
        var result = clampedStart;
        var steps = System.Math.Max(1, (int)System.Math.Ceiling(distance / 4));
        for (var i = 1; i <= steps; i++)
        {
            var progress = i / (double)steps;
            var x = Vec2.Clamp(clampedStart.X + unit.X * distance * progress, 0, _bounds.Width);
            var y = Vec2.Clamp(clampedStart.Y + unit.Y * distance * progress, 0, _bounds.Height);
            if (!CanStand(x, y)) break;
            result = new Vec2(x, y);
        }
        return result;
    }

    public void TickDodge()
    {
        if (State.DodgeCooldownTicks > 0) State.DodgeCooldownTicks--;
        if (State.DodgeRemainingTicks > 0)
        {
            AdvanceDodgeStep();
            State.DodgeRemainingTicks--;
            if (State.DodgeRemainingTicks == 0) State.DodgeDistanceRemaining = 0;
        }
        if (State.DodgeInvulnerabilityTicks > 0) State.DodgeInvulnerabilityTicks--;
    }

    public bool IsInvulnerable => State.DodgeInvulnerabilityTicks > 0;

    public void RestoreCanonicalRuntime(string uid, int level, int rank, long xp, double currentHp, double maxHp, double currentSpirit, double maxSpirit, string combatStyleId, double attackCooldown, bool breakthroughReady,
        int dodgeCooldownTicks, int dodgeRemainingTicks, int dodgeInvulnerabilityTicks, Vec2 dodgeDirection, double dodgeDistanceRemaining, double dodgeStepDistance,
        IReadOnlyList<V25StatusInstance> statuses, IReadOnlyList<V25ShieldInstance> shields, long currentTick, Vec2 facing = default)
    {
        if (!CanonicalMode) throw new InvalidOperationException("Canonical content is not enabled.");
        if (xp is < 0 or > int.MaxValue || string.IsNullOrWhiteSpace(combatStyleId) || !double.IsFinite(currentHp) || !double.IsFinite(maxHp) || !double.IsFinite(currentSpirit) || !double.IsFinite(maxSpirit) || !double.IsFinite(attackCooldown) || maxHp <= 0 || currentHp < 0 || currentHp > maxHp || maxSpirit < 0 || currentSpirit < 0 || currentSpirit > maxSpirit || attackCooldown < 0 || dodgeCooldownTicks < 0 || dodgeRemainingTicks < 0 || dodgeInvulnerabilityTicks < 0 || dodgeDistanceRemaining < 0 || dodgeStepDistance < 0 || !double.IsFinite(dodgeDistanceRemaining) || !double.IsFinite(dodgeStepDistance) || dodgeDirection == Vec2.Zero || !double.IsFinite(facing.X) || !double.IsFinite(facing.Y) || facing == Vec2.Zero)
            throw new InvalidDataException("Canonical player runtime state is invalid.");
        if (level < 1 || level > _canonical!.Balance.MaxLevel || rank != V25ProgressionRules.RankFromLevel(level)) throw new InvalidDataException("Canonical player progression is invalid.");
        var expected = V25CombatRules.ComputeStats(V25EntityKind.Player, 1, "Balanced", rank, level, balance: _canonical.Balance);
        if (maxHp + 0.001 < expected.MaxHp || maxSpirit <= 0) throw new InvalidDataException("Canonical player maximum resource does not match the pinned balance.");
        _uids.Observe(uid);
        State.RestoreIdentity(uid);
        State.Level = level; State.Rank = rank; State.Xp = checked((int)xp); State.BreakthroughReady = breakthroughReady; State.Stats = expected.ToLegacyBlock(); State.MaxHp = maxHp; State.MaxSpirit = maxSpirit; State.CombatStyleId = combatStyleId; State.AttackCooldown = attackCooldown;
        State.CurrentHp = V25FixedPoint.QuantizeMilli(currentHp); State.CurrentSpirit = V25FixedPoint.QuantizeMilli(currentSpirit);
        State.DodgeCooldownTicks = dodgeCooldownTicks; State.DodgeRemainingTicks = dodgeRemainingTicks; State.DodgeInvulnerabilityTicks = dodgeInvulnerabilityTicks; State.DodgeDirection = dodgeDirection.Normalized(); State.DodgeDistanceRemaining = V25FixedPoint.QuantizeMilli(dodgeDistanceRemaining); State.DodgeStepDistance = V25FixedPoint.QuantizeMilli(dodgeStepDistance); State.Facing = facing.Normalized();
        State.Statuses.Restore(uid, statuses, currentTick); State.Shields.Restore(uid, shields, currentTick);
    }

    public void ApplyCanonicalDamage(double shieldAbsorbed, double hpLoss)
    {
        if (!CanonicalMode) { TakeDamage(hpLoss); return; }
        State.Shields.Consume(State.Uid, System.Math.Max(0, shieldAbsorbed));
        if (hpLoss <= 0 || !State.Alive) return;
        State.CurrentHp = V25FixedPoint.QuantizeMilli(System.Math.Max(0, State.CurrentHp - hpLoss));
        if (!State.Alive) _events.Publish(new PlayerDefeatedEvent(State.CurrentHp));
        else _events.Publish(new PlayerDamagedEvent(hpLoss, State.CurrentHp, State.MaxHp));
    }

    public void AddCanonicalShield(double amount)
    {
        if (!CanonicalMode || !double.IsFinite(amount) || amount <= 0) return;
        State.Shield = System.Math.Min(State.MaxHp * 0.5, State.Shield + amount);
    }

    public bool TrySpendSpirit(double amount)
    {
        if (!CanonicalMode || !double.IsFinite(amount) || amount < 0 || State.CurrentSpirit < amount) return false;
        State.CurrentSpirit = V25FixedPoint.QuantizeMilli(State.CurrentSpirit - amount);
        return true;
    }

    public double SpiritRegenPercent => _canonicalModifiers.Sum(modifier => modifier.SpiritRegenPercent);
    public double SpiritRegenFlat => _canonicalModifiers.Sum(modifier => modifier.SpiritRegenFlat);
    private double CanonicalMaxSpirit(int level, int rank) => System.Math.Round((120 + 12 * (level - 1)) * (1 + 0.08 * (rank - 1)) * (1 + _canonicalModifiers.Sum(modifier => modifier.SpiritCapacityPercent)) + _canonicalModifiers.Sum(modifier => modifier.SpiritCapacityFlat), MidpointRounding.AwayFromZero);

    public void TakeDamage(double amount)
    {
        if (amount <= 0 || !State.Alive) return;
        State.CurrentHp = System.Math.Max(0, State.CurrentHp - amount);
        if (!State.Alive) _events.Publish(new PlayerDefeatedEvent(State.CurrentHp));
        else _events.Publish(new PlayerDamagedEvent(amount, State.CurrentHp, State.MaxHp));
    }

    private bool CanStand(double x, double y) => (TerrainEntryAllowed?.Invoke(new Vec2(x, y)) ?? true) && !_colliders.Any(rect => rect.OverlapsCircle(x, y, CanonicalMode ? CanonicalBodyRadius : BodyRadius));

    private void AdvanceDodgeStep()
    {
        if (!CanonicalMode || State.DodgeDistanceRemaining <= 0 || State.DodgeDirection == Vec2.Zero) return;
        var intended = System.Math.Min(State.DodgeStepDistance, State.DodgeDistanceRemaining);
        var start = State.Position;
        var destination = SweptPosition(start, State.DodgeDirection, intended);
        var moved = start.DistanceTo(destination);
        State.Position = destination;
        if (moved + 1e-9 < intended) State.DodgeDistanceRemaining = 0;
        else State.DodgeDistanceRemaining = System.Math.Max(0, State.DodgeDistanceRemaining - moved);
    }
}
