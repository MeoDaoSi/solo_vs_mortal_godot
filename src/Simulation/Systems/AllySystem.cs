using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Simulation.Systems;

public sealed class AllySystem
{
    private const double CanonicalAggroRadius = 160;
    private const double CanonicalAssaultRadius = 320;
    private const double CanonicalFocusRadius = 480;
    private const int ThinkIntervalTicks = 6;
    private const int FocusDurationTicks = 480;
    private const int RecentAttackerTicks = 120;
    private const int PathFailureTicks = 120;
    private readonly Dictionary<string, AllyState> _allies = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly GameDefinitions _definitions; private readonly CanonicalContentRegistry? _canonical;
    private string? _canonicalPlayerUid;
    private long _simulationTick;
    private Func<Vec2, Vec2, double, double, Vec2>? _canonicalNextStep;
    private Func<Vec2, double, double, Vec2?>? _canonicalNearestFree;
    private Func<bool>? _canonicalCombatOrHazard;
    /// <summary>Environment movement is actor-scoped; it must not inherit Player capabilities.</summary>
    public Func<AllyState, double>? EnvironmentSpeedMultiplier { get; set; }
    private readonly IDisposable _damageSubscription;
    private readonly Dictionary<string, long> _recentPlayerAttackers = new(StringComparer.Ordinal);
    public AllySystem(EventBus events, UidGenerator uids, GameDefinitions definitions, CanonicalContentRegistry? canonical = null)
    {
        _events = events; _uids = uids; _definitions = definitions; _canonical = canonical;
        _damageSubscription = events.Subscribe<CanonicalDamageResolvedEvent>(OnCanonicalDamage);
    }

    public bool CanonicalMode => _canonical is not null;

    public AllyState SpawnSoul(OwnedSoulState soul, SoulBannerState banner, Vec2 position)
    {
        var monster = _definitions.Monster(soul.Origin.MonsterDefinitionId);
        if (CanonicalMode)
        {
            var speciesId = soul.Origin.SpeciesId.ToLowerInvariant();
            var species = _canonical!.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == speciesId)
                ?? throw new DefinitionException($"Canonical profile beta_01 does not expose ally species '{speciesId}'.");
            var canonicalStats = V25CombatRules.ComputeStats(V25EntityKind.Ally, species.PowerTier, species.Archetype, soul.Origin.Rank, soul.Level, balance: _canonical.Balance).ToLegacyBlock();
            var canonicalAlly = new AllyState(_uids.Create("ally"), monster.Id, species.Id, soul.Origin.DisplayName, soul.Id, soul.Origin.Rank, soul.Level, canonicalStats, _definitions.Soul.Summon.Ai, position,
                species.PowerTier, species.Archetype, species.CombatStyleId, species.SignatureSkillId);
            _allies.Add(canonicalAlly.Uid, canonicalAlly);
            _events.Publish(new AllySpawnedEvent(canonicalAlly.Uid, monster.Id, canonicalAlly.SpeciesId, canonicalAlly.Rank, canonicalAlly.Level, position));
            return canonicalAlly;
        }
        var stats = CombatPowerRules.GetStatBlock(new EntityStatContext(EntityType.Soul, soul.Level, soul.Origin.Rank, soul.Origin.SpeciesId),
            [new StatModifiers(HpPercent: banner.Computed.SoulHpPercent, AtkPercent: banner.Computed.SoulAtkPercent), new StatModifiers(Scalar: banner.Computed.SummonModifier)]);
        var ally = new AllyState(_uids.Create("ally"), monster.Id, soul.Origin.SpeciesId, soul.Origin.DisplayName, soul.Id, soul.Origin.Rank, soul.Level, stats, _definitions.Soul.Summon.Ai, position);
        _allies.Add(ally.Uid, ally); _events.Publish(new AllySpawnedEvent(ally.Uid, monster.Id, ally.SpeciesId, ally.Rank, ally.Level, position)); return ally;
    }

    public void ConfigureCanonical(PlayerSystem player, Func<Vec2, Vec2, double, double, Vec2>? nextStep = null,
        Func<Vec2, double, double, Vec2?>? nearestFree = null, Func<bool>? combatOrHazard = null)
    {
        if (!CanonicalMode) throw new InvalidOperationException("Canonical Ally AI is not enabled.");
        _canonicalPlayerUid = player?.State.Uid ?? throw new ArgumentNullException(nameof(player));
        _canonicalNextStep = nextStep;
        _canonicalNearestFree = nearestFree;
        _canonicalCombatOrHazard = combatOrHazard;
    }

    public void Update(double deltaSeconds, MonsterSystem monsters, Vec2 playerPosition, long simulationTick = -1)
    {
        if (CanonicalMode)
        {
            if (simulationTick >= 0) _simulationTick = simulationTick;
            UpdateCanonical(deltaSeconds, monsters, playerPosition);
            return;
        }
        foreach (var ally in _allies.Values.ToArray())
        {
            if (!ally.Alive) continue;
            var ready = ally.AttackCooldown <= 0; ally.AttackCooldown = System.Math.Max(0, ally.AttackCooldown - deltaSeconds);
            var target = monsters.AliveMonsters().Select(monster => (monster, distance: monster.Position.DistanceTo(ally.Position))).Where(item => item.distance <= ally.Ai.AggroRadius).OrderBy(item => item.distance).FirstOrDefault();
            if (target.monster is not null)
            {
                ally.TargetUid = target.monster.Uid;
                if (target.distance <= ally.Ai.AttackRange)
                {
                    if (ready) { ally.AttackCooldown = CombatPowerRules.GetAttackCooldown(EntityType.Soul, ally.Stats.SpeedRaw); var damage = CombatRules.CalculateDamage(ally.Stats.Atk, target.monster.Stats.DefRaw, 1); _events.Publish(new CombatAttackEvent(ally.Uid, CombatantKind.Soul, target.monster.Uid, CombatantKind.Monster, damage.FinalDamage)); monsters.TakeDamage(target.monster.Uid, damage.FinalDamage, ally.Uid); }
                    ally.AiState = AllyAiState.Attack;
                }
                else { ally.AiState = AllyAiState.Chase; ally.Position = ally.Position.MoveTowards(target.monster.Position, CombatPowerRules.GetMovementSpeed(EntityType.Soul, ally.Stats.SpeedRaw) * deltaSeconds); }
                continue;
            }
            ally.TargetUid = null; var home = new Vec2(playerPosition.X + ally.HomeOffset.X, playerPosition.Y + ally.HomeOffset.Y);
            if (ally.Position.DistanceTo(home) <= _definitions.Soul.Summon.ArriveDistance) ally.AiState = AllyAiState.Idle;
            else { ally.AiState = AllyAiState.Return; ally.Position = ally.Position.MoveTowards(home, CombatPowerRules.GetMovementSpeed(EntityType.Soul, ally.Stats.SpeedRaw) * deltaSeconds); }
        }
    }

    private void OnCanonicalDamage(CanonicalDamageResolvedEvent damage)
    {
        if (!CanonicalMode || damage.Rejected || damage.HpLoss <= 0 || _canonicalPlayerUid is null || damage.TargetUid != _canonicalPlayerUid) return;
        _recentPlayerAttackers[damage.AttackerUid] = _simulationTick;
        foreach (var ally in _allies.Values)
        {
            ally.RecentAttackerUid = damage.AttackerUid;
            ally.RecentAttackerTick = _simulationTick;
        }
    }

    private void UpdateCanonical(double deltaSeconds, MonsterSystem monsters, Vec2 playerPosition)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        var ticks = Math.Max(1, (int)Math.Round(deltaSeconds * 60, MidpointRounding.AwayFromZero));
        foreach (var key in _recentPlayerAttackers.Where(pair => _simulationTick - pair.Value > RecentAttackerTicks).Select(pair => pair.Key).ToArray()) _recentPlayerAttackers.Remove(key);
        RebuildCanonicalFormation(playerPosition);
        foreach (var ally in _allies.Values.ToArray())
        {
            if (!ally.Alive) continue;
            ally.FocusRemainingTicks = Math.Max(0, ally.FocusRemainingTicks - ticks);
            if (ally.FocusRemainingTicks == 0) ally.FocusTargetUid = null;
            ally.ThinkTicks = Math.Max(0, ally.ThinkTicks - ticks);
            if (ally.Statuses.Has(ally.TargetLifeUid, "stun")) { ally.AiState = AllyAiState.Recover; continue; }

            var modeRadius = ally.AiMode == AllyAiMode.Assault ? CanonicalAssaultRadius : CanonicalAggroRadius;
            var distanceToPlayer = ally.Position.DistanceTo(playerPosition);
            var leash = ally.AiMode == AllyAiMode.Assault ? 512 : 384;
            var target = ally.TargetUid is { } locked && monsters.Get(locked) is { Alive: true } lockedMonster
                ? lockedMonster
                : null;
            if (ally.ThinkTicks == 0 || target is null || !IsTargetStillValid(ally, target, modeRadius))
            {
                target = SelectTarget(ally, monsters, modeRadius);
                ally.TargetUid = target?.Uid;
                ally.ThinkTicks = ThinkIntervalTicks;
                if (target is null) ally.AiState = AllyAiState.Acquire;
            }

            var home = new Vec2(playerPosition.X + ally.HomeOffset.X, playerPosition.Y + ally.HomeOffset.Y);
            if (distanceToPlayer > leash)
            {
                ally.TargetUid = null;
                ally.AiState = AllyAiState.Return;
                MoveToward(ally, home, CanonicalMoveSpeed(ally) * deltaSeconds, ticks, inCombat: false, playerPosition);
                continue;
            }
            if (target is not null)
            {
                var distance = target.Position.DistanceTo(ally.Position);
                var style = _canonical!.Content.CombatStyles.FirstOrDefault(item => item.Id == ally.CombatStyleId)
                    ?? _canonical.Content.CombatStyles.First(item => item.Id == "fist");
                if (distance <= style.RangeUnits + 10)
                {
                    // Canonical attack availability is owned by V25CombatCoordinator's
                    // tick-based cooldown ledger.  AttackCooldown is legacy-only state
                    // and must not influence the canonical actor intent state.
                    ally.AiState = AllyAiState.Attack;
                    ally.PathFailTicks = 0;
                }
                else
                {
                    ally.AiState = AllyAiState.Approach;
                    var speed = CanonicalMoveSpeed(ally);
                    MoveToward(ally, target.Position, speed * deltaSeconds, ticks, inCombat: true, playerPosition);
                }
                continue;
            }

            if (ally.Position.DistanceTo(home) <= _definitions.Soul.Summon.ArriveDistance) { ally.AiState = AllyAiState.Follow; ally.PathFailTicks = 0; }
            else
            {
                ally.AiState = AllyAiState.Follow;
                var speed = CanonicalMoveSpeed(ally);
                MoveToward(ally, home, speed * deltaSeconds, ticks, inCombat: false, playerPosition);
            }
        }
        EnforceFriendlySeparation();
    }

    private MonsterState? SelectTarget(AllyState ally, MonsterSystem monsters, double modeRadius)
    {
        if (ally.FocusTargetUid is { } focus && ally.FocusRemainingTicks > 0 && monsters.Get(focus) is { Alive: true } focused && focused.Position.DistanceTo(ally.Position) <= CanonicalFocusRadius)
            return focused;
        var recent = monsters.AliveMonsters().Where(monster => _recentPlayerAttackers.TryGetValue(monster.Uid, out var tick) && _simulationTick - tick <= RecentAttackerTicks && monster.Position.DistanceTo(ally.Position) <= modeRadius)
            .OrderBy(monster => monster.Position.DistanceTo(ally.Position)).ThenBy(monster => monster.Uid, StringComparer.Ordinal).FirstOrDefault();
        return recent ?? monsters.AliveMonsters().Where(monster => monster.Position.DistanceTo(ally.Position) <= modeRadius)
            .OrderBy(monster => monster.Position.DistanceTo(ally.Position)).ThenBy(monster => monster.Uid, StringComparer.Ordinal).FirstOrDefault();
    }

    private static bool IsTargetStillValid(AllyState ally, MonsterState target, double modeRadius) =>
        target.Alive && (ally.FocusTargetUid == target.Uid && ally.FocusRemainingTicks > 0 || target.Position.DistanceTo(ally.Position) <= modeRadius);

    private double CanonicalMoveSpeed(AllyState ally) =>
        ally.Stats.SpeedRaw * (ally.Statuses.Has(ally.TargetLifeUid, "haste") ? 1.20 : 1) *
        (ally.Statuses.Has(ally.TargetLifeUid, "chill") ? 0.75 : 1) * Math.Max(0, EnvironmentSpeedMultiplier?.Invoke(ally) ?? 1);

    private double CanonicalBodyRadius(AllyState ally)
    {
        var species = _canonical!.SpeciesForProfile(_canonical.ActiveProfileId).First(item => item.Id == ally.SpeciesId);
        return V25ActorBodyRadii.ForSpeciesRole(species.Role);
    }

    private void MoveToward(AllyState ally, Vec2 goal, double distance, int ticks, bool inCombat, Vec2 playerPosition)
    {
        if (distance <= 0) return;
        var start = ally.Position;
        var bodyRadius = CanonicalBodyRadius(ally);
        var next = _canonicalNextStep?.Invoke(start, goal, bodyRadius, distance) ?? start.MoveTowards(goal, distance);
        if (next == start && start.DistanceTo(goal) > _definitions.Soul.Summon.ArriveDistance)
        {
            ally.PathFailTicks = Math.Min(PathFailureTicks, checked(ally.PathFailTicks + ticks));
            if (ally.PathFailTicks >= PathFailureTicks)
            {
                ally.PathFailTicks = 0;
                if (inCombat)
                {
                    _events.Publish(new AllyUnreachableEvent(ally.Uid, ally.SourceSoulId, ally.Position));
                    return;
                }
                var rescue = _canonicalNearestFree?.Invoke(playerPosition, bodyRadius, 48);
                if (rescue is { } point) ally.Position = point;
            }
            return;
        }
        ally.Position = next;
        ally.PathFailTicks = 0;
    }

    private void EnforceFriendlySeparation()
    {
        if (!CanonicalMode) return;
        var alive = _allies.Values.Where(ally => ally.Alive).OrderBy(ally => ally.SpeciesId, StringComparer.Ordinal).ThenBy(ally => ally.Uid, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < alive.Length; i++)
            for (var j = i + 1; j < alive.Length; j++)
            {
                var left = alive[i]; var right = alive[j];
                var delta = new Vec2(right.Position.X - left.Position.X, right.Position.Y - left.Position.Y);
                var distance = delta.Length;
                if (distance >= 8) continue;
                var direction = distance <= double.Epsilon ? new Vec2(1, 0) : new Vec2(delta.X / distance, delta.Y / distance);
                var push = (8 - distance) * 0.5;
                left.Position = new Vec2(left.Position.X - direction.X * push, left.Position.Y - direction.Y * push);
                right.Position = new Vec2(right.Position.X + direction.X * push, right.Position.Y + direction.Y * push);
            }
    }

    /// <summary>Applies the authored sorted species formation around the current Player position.</summary>
    public void RebuildCanonicalFormation(Vec2 playerPosition)
    {
        if (!CanonicalMode) return;
        var ordered = _allies.Values.Where(ally => ally.Alive).OrderBy(ally => ally.SpeciesId, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var radius = 48 + 24 * (index / 6);
            var angle = Math.PI / 3 * (index % 6);
            ordered[index].HomeOffset = new Vec2(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }
    }

    public bool SetCanonicalMode(AllyAiMode mode, string? soulId = null)
    {
        if (!CanonicalMode || !Enum.IsDefined(mode)) return false;
        var selected = soulId is null ? _allies.Values : _allies.Values.Where(ally => ally.SourceSoulId == soulId);
        var changed = false;
        foreach (var ally in selected) { ally.AiMode = mode; ally.ThinkTicks = 0; changed = true; }
        return changed;
    }

    public bool FocusCanonical(string allyUid, string targetUid, MonsterSystem monsters, long simulationTick)
    {
        if (!CanonicalMode || simulationTick < 0 || !_allies.TryGetValue(allyUid, out var ally) || !ally.Alive || monsters.Get(targetUid) is not { Alive: true } target || target.Position.DistanceTo(ally.Position) > CanonicalFocusRadius)
            return false;
        ally.FocusTargetUid = targetUid; ally.FocusRemainingTicks = FocusDurationTicks; ally.TargetUid = targetUid; ally.ThinkTicks = 0; return true;
    }

    public AllyState? TakeDamage(string uid, double amount)
    {
        if (!_allies.TryGetValue(uid, out var ally) || !ally.Alive || amount <= 0) return null;
        ally.CurrentHp = System.Math.Max(0, ally.CurrentHp - amount);
        if (!ally.Alive) { ally.AiState = AllyAiState.Dead; _allies.Remove(uid); _events.Publish(new AllyDefeatedEvent(uid, ally.DefinitionId, ally.SpeciesId, ally.Position)); }
        return ally;
    }

    public void ApplyEnvironmentalDamage(string uid, double hpLoss)
    {
        if (!CanonicalMode || !_allies.TryGetValue(uid, out var ally) || !ally.Alive || hpLoss <= 0) return;
        ally.CurrentHp = V25FixedPoint.QuantizeMilli(System.Math.Max(0, ally.CurrentHp - hpLoss));
        if (ally.Alive) return;
        ally.Vitality = 0; ally.RecoveryTicks = V25CombatRules.MillisecondsToTicksCeil(_canonical!.Balance.Summon.RecoveryMs);
        ally.AiState = AllyAiState.Dead; _allies.Remove(uid);
        _events.Publish(new AllyDefeatedEvent(uid, ally.DefinitionId, ally.SpeciesId, ally.Position, _canonical.Balance.Summon.RecoveryMs));
    }

    public AllyState? ApplyCanonicalDamage(string uid, V25DamageResult result)
    {
        if (!CanonicalMode || result.Rejected || !_allies.TryGetValue(uid, out var ally) || !ally.Alive) return null;
        ally.Shields.Consume(ally.Uid, result.ShieldAbsorbed);
        ally.CurrentHp = V25FixedPoint.QuantizeMilli(System.Math.Max(0, ally.CurrentHp - result.HpLoss));
        if (!ally.Alive)
        {
            ally.Vitality = 0;
            ally.RecoveryTicks = V25CombatRules.MillisecondsToTicksCeil(_canonical!.Balance.Summon.RecoveryMs); // recovery is finalized by Soul/Summon in Part 4.
            ally.AiState = AllyAiState.Dead;
            _allies.Remove(uid);
            _events.Publish(new AllyDefeatedEvent(uid, ally.DefinitionId, ally.SpeciesId, ally.Position, _canonical!.Balance.Summon.RecoveryMs));
        }
        return ally;
    }

    public AllyState RestoreCanonicalRuntime(string uid, string definitionId, string speciesId, string displayName, string sourceSoulId, int level, int rank,
        int powerTier, string archetype, string combatStyleId, string? signatureSkillId, double positionX, double positionY, double currentHp, double maxHp,
        bool alive, string aiState, double attackCooldown, string? targetUid, double vitality, int recoveryTicks,
        IReadOnlyList<V25StatusInstance> statuses, IReadOnlyList<V25ShieldInstance> shields, long currentTick,
        AllyAiMode aiMode = AllyAiMode.Guard, int thinkTicks = 0, string? focusTargetUid = null, int focusRemainingTicks = 0,
        int pathFailTicks = 0, string? recentAttackerUid = null, int recentAttackerAgeTicks = 0)
    {
        if (!CanonicalMode) throw new InvalidOperationException("Canonical content is not enabled.");
        if (_allies.ContainsKey(uid)) throw new InvalidDataException($"Duplicate canonical ally UID '{uid}'.");
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(sourceSoulId)) throw new InvalidDataException($"Canonical ally '{uid}' is missing source identity.");
        var monster = _definitions.Monster(definitionId);
        var species = _canonical!.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == speciesId)
            ?? throw new InvalidDataException($"Unknown canonical ally species '{speciesId}'.");
        if (species.PowerTier != powerTier || species.Archetype != archetype || species.CombatStyleId != combatStyleId || species.SignatureSkillId != signatureSkillId)
            throw new InvalidDataException($"Canonical ally '{uid}' definition does not match species '{speciesId}'.");
        if (level < 1 || level > _canonical.Balance.MaxLevel || rank != V25ProgressionRules.RankFromLevel(level) || !double.IsFinite(positionX) || !double.IsFinite(positionY) || !double.IsFinite(currentHp) || !double.IsFinite(maxHp) || !double.IsFinite(attackCooldown) || currentHp < 0 || maxHp <= 0 || currentHp > maxHp || !alive && currentHp != 0 || alive && currentHp <= 0 || attackCooldown < 0 || !double.IsFinite(vitality) || vitality is < 0 or > 1 || recoveryTicks < 0)
            throw new InvalidDataException($"Canonical ally '{uid}' runtime state is invalid.");
        if (!Enum.TryParse<AllyAiState>(aiState, ignoreCase: false, out var parsedAi) || !Enum.IsDefined(aiMode) || thinkTicks < 0 || thinkTicks > ThinkIntervalTicks || focusRemainingTicks < 0 || focusRemainingTicks > FocusDurationTicks || pathFailTicks < 0 || pathFailTicks > PathFailureTicks || recentAttackerAgeTicks < 0 || recentAttackerAgeTicks > RecentAttackerTicks)
            throw new InvalidDataException($"Unknown or invalid ally AI state '{aiState}'.");
        var stats = V25CombatRules.ComputeStats(V25EntityKind.Ally, powerTier, archetype, rank, level, balance: _canonical.Balance).ToLegacyBlock();
        if (Math.Abs(stats.Hp - maxHp) > 0.001) throw new InvalidDataException($"Canonical ally '{uid}' maximum HP does not match pinned balance.");
        var state = new AllyState(uid, monster.Id, species.Id, displayName, sourceSoulId, rank, level, stats, _definitions.Soul.Summon.Ai, new Vec2(positionX, positionY), powerTier, archetype, combatStyleId, signatureSkillId)
        { AiState = parsedAi, AiMode = aiMode, ThinkTicks = thinkTicks, FocusTargetUid = focusTargetUid, FocusRemainingTicks = focusRemainingTicks,
            PathFailTicks = pathFailTicks, RecentAttackerUid = recentAttackerUid,
            RecentAttackerTick = recentAttackerUid is null ? -1 : Math.Max(0, currentTick - recentAttackerAgeTicks),
            AttackCooldown = attackCooldown, TargetUid = targetUid, Vitality = vitality, RecoveryTicks = recoveryTicks, CurrentHp = V25FixedPoint.QuantizeMilli(currentHp) };
        state.Statuses.Restore(uid, statuses, currentTick); state.Shields.Restore(uid, shields, currentTick);
        _allies.Add(uid, state);
        // Target selection reads this shared attacker index, not only the per-Ally display
        // fields. Rebuild it from persisted memory so a save/load cannot silently discard
        // the remaining 2-second protect-the-player priority.
        if (recentAttackerUid is not null && recentAttackerAgeTicks < RecentAttackerTicks)
        {
            var attackerTick = Math.Max(0, currentTick - recentAttackerAgeTicks);
            if (!_recentPlayerAttackers.TryGetValue(recentAttackerUid, out var existing) || attackerTick > existing)
                _recentPlayerAttackers[recentAttackerUid] = attackerTick;
        }
        _uids.Observe(uid);
        return state;
    }

    public bool Remove(string uid) => _allies.Remove(uid);
    public void Clear() { _allies.Clear(); _recentPlayerAttackers.Clear(); }
    public AllyState? Get(string uid) => _allies.GetValueOrDefault(uid);
    public IReadOnlyList<AllyState> AliveAllies() => _allies.Values.Where(ally => ally.Alive).ToArray();
}
