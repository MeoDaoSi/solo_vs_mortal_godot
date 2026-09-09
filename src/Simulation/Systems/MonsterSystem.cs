using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Simulation.Systems;

public readonly record struct SpawnArea(double X, double Y, double Width, double Height);
public sealed record MonsterSpawnOptions(
    int? Rank = null,
    int? Level = null,
    Vec2? Position = null,
    V25EncounterType EncounterType = V25EncounterType.Normal,
    bool RewardEligible = true,
    string? EncounterId = null);

public sealed class MonsterSystem
{
    private const double CanonicalAggroRadius = 192;
    private readonly Dictionary<string, MonsterState> _monsters = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly SeededRng _rng; private readonly GameDefinitions _definitions; private readonly CanonicalContentRegistry? _canonical; private SpawnArea _spawnArea;
    public MonsterSystem(EventBus events, UidGenerator uids, SeededRng rng, GameDefinitions definitions, SpawnArea spawnArea, CanonicalContentRegistry? canonical = null) { _events = events; _uids = uids; _rng = rng; _definitions = definitions; _spawnArea = spawnArea; _canonical = canonical; }

    public bool CanonicalMode => _canonical is not null;

    public void SetSpawnArea(SpawnArea spawnArea) => _spawnArea = spawnArea;

    public MonsterState Spawn(string definitionId, MonsterSpawnOptions? options = null)
    {
        options ??= new MonsterSpawnOptions();
        if (CanonicalMode) return SpawnCanonical(definitionId, options);
        var definition = _definitions.Monster(definitionId);
        var level = options.Level ?? (options.Rank is { } requestedRank ? CombatPowerRules.RankStartLevel(requestedRank) : _rng.Int(definition.LevelRange.Minimum, definition.LevelRange.Maximum));
        var rank = options.Rank ?? CombatPowerRules.GlobalLevelToRank(level);
        RequireValidProgression(definition.SpeciesId, level, rank);
        var stats = CombatPowerRules.GetStatBlock(new EntityStatContext(EntityType.Monster, level, rank, definition.SpeciesId));
        var position = options.Position ?? new Vec2(_spawnArea.X + _rng.Next() * _spawnArea.Width, _spawnArea.Y + _rng.Next() * _spawnArea.Height);
        var state = new MonsterState(_uids.Create("monster"), definition.Id, definition.SpeciesId, rank, level, stats, position);
        _monsters.Add(state.Uid, state);
        _events.Publish(new MonsterSpawnedEvent(state.Uid, definition.Id, definition.SpeciesId, rank, CombatPowerRules.RankKey(rank), CombatPowerRules.RankDisplayName(rank), level, position));
        return state;
    }

    private MonsterState SpawnCanonical(string definitionId, MonsterSpawnOptions options)
    {
        MonsterDefinition definition;
        try { definition = _definitions.Monster(definitionId); }
        catch (KeyNotFoundException)
        {
            definition = _definitions.Monsters.FirstOrDefault(item => item.SpeciesId.Equals(definitionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Unknown monster definition or species '{definitionId}'.");
        }
        var speciesId = definition.SpeciesId.ToLowerInvariant();
        var species = _canonical!.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == speciesId)
            ?? throw new DefinitionException($"Canonical profile beta_01 does not expose species '{speciesId}'.");
        var region = _canonical.Content.Regions.First(item => item.Id == species.HomeRegionId);
        var defaultLevel = region.Levels.Count == 0 ? 1 : region.Levels[0];
        var level = options.Level ?? (options.Rank is { } requestedRank ? (requestedRank - 1) * 10 + 1 : defaultLevel);
        var rank = options.Rank ?? V25ProgressionRules.RankFromLevel(level);
        if (level < 1 || level > _canonical.Balance.MaxLevel || rank != V25ProgressionRules.RankFromLevel(level))
            throw new DefinitionException($"Canonical monster progression {level}/{rank} is invalid.");
        var stats = V25CombatRules.ComputeStats(V25EntityKind.Monster, species.PowerTier, species.Archetype, rank, level, options.EncounterType, _canonical.Balance).ToLegacyBlock();
        var position = options.Position ?? new Vec2(_spawnArea.X + _rng.Next() * _spawnArea.Width, _spawnArea.Y + _rng.Next() * _spawnArea.Height);
        var uid = _uids.Create("monster");
        var state = new MonsterState(uid, definition.Id, species.Id, rank, level, stats, position,
            species.PowerTier, species.Archetype, species.CombatStyleId, species.SignatureSkillId, options.EncounterType,
            options.RewardEligible && V25EncounterFactors.For(options.EncounterType).GrantsCombatRewards)
        { EncounterId = options.EncounterId ?? $"encounter.{uid}", BossStoryInstanceId = options.EncounterType == V25EncounterType.Boss ? $"story.{options.EncounterId ?? uid}" : null };
        _monsters.Add(state.Uid, state);
        _events.Publish(new MonsterSpawnedEvent(state.Uid, definition.Id, species.Id, rank, CombatPowerRules.RankKey(rank), CombatPowerRules.RankDisplayName(rank), level, position));
        return state;
    }

    public void Update(double deltaSeconds, PlayerState player, AllySystem? allies = null, PlayerSystem? movement = null, Func<string, bool>? actionLocked = null)
    {
        if (CanonicalMode)
        {
            UpdateCanonical(deltaSeconds, player, allies, movement, actionLocked);
            return;
        }
        foreach (var monster in _monsters.Values)
        {
            if (!monster.Alive) continue;
            var attackReady = monster.AttackCooldown <= 0;
            monster.AttackCooldown = System.Math.Max(0, monster.AttackCooldown - deltaSeconds);
            var definition = _definitions.Monster(monster.DefinitionId);
            var distance = monster.Position.DistanceTo(player.Position);
            if (!player.Alive || distance > definition.Ai.AggroRadius) { monster.AiState = MonsterAiState.Idle; continue; }
            if (distance <= definition.Ai.AttackRange)
            {
                if (attackReady)
                {
                    monster.AttackCooldown = CombatPowerRules.GetAttackCooldown(EntityType.Monster, monster.Stats.SpeedRaw);
                    var damage = CombatRules.CalculateDamage(monster.Stats.Atk, player.Stats.DefRaw, CombatRules.BasicAttackMultiplier);
                    _events.Publish(new CombatAttackEvent(monster.Uid, CombatantKind.Monster, player.Uid, CombatantKind.Player, damage.FinalDamage));
                }
                monster.AiState = MonsterAiState.Attack;
            }
            else { monster.AiState = MonsterAiState.Chase; monster.Position = monster.Position.MoveTowards(player.Position, CombatPowerRules.GetMovementSpeed(EntityType.Monster, monster.Stats.SpeedRaw) * deltaSeconds); }
        }
    }

    private void UpdateCanonical(double deltaSeconds, PlayerState player, AllySystem? allies, PlayerSystem? movement, Func<string, bool>? actionLocked)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        var candidates = new List<(string Uid, Vec2 Position, V25StatusStore Statuses)>();
        if (player.Alive) candidates.Add((player.Uid, player.Position, player.Statuses));
        if (allies is not null) candidates.AddRange(allies.AliveAllies().Select(ally => (ally.Uid, ally.Position, ally.Statuses)));
        foreach (var monster in _monsters.Values)
        {
            if (!monster.Alive) continue;
            var speed = Math.Clamp(monster.Stats.SpeedRaw * (monster.Statuses.Has(monster.Uid, "haste") ? 1.20 : 1) * (monster.Statuses.Has(monster.Uid, "chill") ? 0.75 : 1), 40, 240);
            if (monster.Position.DistanceTo(monster.HomePosition) > 384) monster.IsReturning = true;
            if (monster.IsReturning)
            {
                monster.TargetUid = null; monster.AiState = MonsterAiState.Return;
                var delta = new Vec2(monster.HomePosition.X - monster.Position.X, monster.HomePosition.Y - monster.Position.Y);
                var distance = Math.Min(delta.Length, speed * deltaSeconds);
                monster.Position = movement is not null
                    ? movement.FindReachableNextStep(monster.Position, monster.HomePosition, 18, distance)
                    : V25Navigation.NextStep(monster.Position, monster.HomePosition, distance, new V25WorldBounds(_spawnArea.Width, _spawnArea.Height), Array.Empty<Core.Math.Rect>(), 18);
                if (monster.Position.DistanceTo(monster.HomePosition) <= 1)
                {
                    monster.Position = monster.HomePosition; monster.CurrentHp = monster.MaxHp;
                    monster.IsReturning = false; monster.AiState = MonsterAiState.Idle;
                }
                continue; // life UID and reward budgets are retained.
            }
            if (monster.StaggerRecoveryTicks > 0 || monster.Statuses.Has(monster.Uid, "stun")) { monster.AiState = MonsterAiState.Hit; continue; }
            bool Reachable((string Uid, Vec2 Position, V25StatusStore Statuses) value) => value.Position.DistanceTo(monster.HomePosition) <= 384 && (movement is null || movement.IsSegmentFree(monster.Position, value.Position));
            var taunter = monster.EncounterType == V25EncounterType.Boss ? null : monster.Statuses.Snapshot(monster.Uid).FirstOrDefault(status => status.EffectId == "taunt")?.SourceId;
            var target = candidates.FirstOrDefault(value => value.Uid == taunter && Reachable(value));
            if (target.Uid is null) target = candidates.FirstOrDefault(value => value.Uid == monster.TargetUid && Reachable(value));
            if (target.Uid is null)
                target = candidates.Where(value => monster.Position.DistanceTo(value.Position) <= CanonicalAggroRadius && Reachable(value) && (!value.Statuses.Has(value.Uid, "veil") || monster.Position.DistanceTo(value.Position) <= 48))
                    .OrderBy(value => monster.Position.DistanceTo(value.Position)).ThenBy(value => value.Uid, StringComparer.Ordinal).FirstOrDefault();
            if (target.Uid is null)
            {
                if (monster.TargetUid is not null && monster.Position.DistanceTo(monster.HomePosition) > 1) monster.IsReturning = true;
                monster.TargetUid = null; monster.AiState = monster.IsReturning ? MonsterAiState.Return : MonsterAiState.Idle; continue;
            }
            monster.TargetUid = target.Uid;
            var style = _canonical!.Content.CombatStyles.First(item => item.Id == monster.CombatStyleId);
            if (monster.Position.DistanceTo(target.Position) <= style.RangeUnits + 10 || actionLocked?.Invoke(monster.Uid) == true) { monster.AiState = MonsterAiState.Attack; continue; }
            monster.AiState = MonsterAiState.Chase;
            var direction = new Vec2(target.Position.X - monster.Position.X, target.Position.Y - monster.Position.Y).Normalized();
            monster.Position = movement?.SweptPosition(monster.Position, direction, speed * deltaSeconds) ?? monster.Position.MoveTowards(target.Position, speed * deltaSeconds);
        }
    }

    public IReadOnlyList<MonsterNavigationState> NavigationSnapshot() => _monsters.Values.Select(monster => new MonsterNavigationState(monster.Uid, monster.HomePosition.X, monster.HomePosition.Y, monster.TargetUid, monster.IsReturning)).ToArray();
    public void RestoreNavigation(IReadOnlyList<MonsterNavigationState> states)
    {
        if (states.Count != _monsters.Count || states.Select(state => state.Uid).Distinct(StringComparer.Ordinal).Count() != states.Count)
            throw new InvalidDataException("Monster navigation snapshot coverage is invalid.");
        foreach (var state in states)
        {
            if (!_monsters.TryGetValue(state.Uid, out var monster) || !double.IsFinite(state.HomeX) || !double.IsFinite(state.HomeY) || state.HomeX < _spawnArea.X || state.HomeY < _spawnArea.Y || state.HomeX > _spawnArea.X + _spawnArea.Width || state.HomeY > _spawnArea.Y + _spawnArea.Height)
                throw new InvalidDataException("Monster home position is invalid.");
            monster.HomePosition = new Vec2(state.HomeX, state.HomeY); monster.TargetUid = state.TargetUid; monster.IsReturning = state.IsReturning;
        }
    }

    public MonsterState? TakeDamage(string uid, double amount, string? sourceUid = null)
    {
        if (!_monsters.TryGetValue(uid, out var monster) || !monster.Alive || amount <= 0) return null;
        monster.CurrentHp = System.Math.Max(0, monster.CurrentHp - amount);
        _events.Publish(new MonsterDamagedEvent(uid, amount, monster.CurrentHp, monster.MaxHp));
        if (!monster.Alive)
        {
            monster.AiState = MonsterAiState.Dead;
            _events.Publish(new MonsterDefeatedEvent(uid, monster.DefinitionId, monster.SpeciesId, monster.Rank, CombatPowerRules.RankKey(monster.Rank), CombatPowerRules.RankDisplayName(monster.Rank), monster.Level, monster.Position, sourceUid, monster.EncounterId, monster.TargetLifeUid, monster.RewardEligible, monster.EncounterType));
        }
        return monster;
    }

    public MonsterState? ApplyCanonicalDamage(string uid, V25DamageResult result, string? sourceUid = null)
    {
        if (!CanonicalMode || result.Rejected || !_monsters.TryGetValue(uid, out var monster) || !monster.Alive) return null;
        monster.Shields.Consume(monster.Uid, result.ShieldAbsorbed);
        monster.CurrentHp = V25FixedPoint.QuantizeMilli(System.Math.Max(0, monster.CurrentHp - result.HpLoss));
        if (result.IncomingDamage > 0) _events.Publish(new MonsterDamagedEvent(uid, result.IncomingDamage, monster.CurrentHp, monster.MaxHp));
        if (!monster.Alive)
        {
            monster.AiState = MonsterAiState.Dead;
            _events.Publish(new MonsterDefeatedEvent(uid, monster.DefinitionId, monster.SpeciesId, monster.Rank, CombatPowerRules.RankKey(monster.Rank), CombatPowerRules.RankDisplayName(monster.Rank), monster.Level, monster.Position, sourceUid, monster.EncounterId, monster.TargetLifeUid, monster.RewardEligible, monster.EncounterType));
        }
        return monster;
    }

    public MonsterState RestoreCanonicalRuntime(string uid, string definitionId, string speciesId, int level, int rank, int powerTier, string archetype,
        string combatStyleId, string? signatureSkillId, double positionX, double positionY, double currentHp, double maxHp, bool alive,
        string aiState, double attackCooldown, string? encounterId, V25EncounterType encounterType, bool rewardEligible,
        IReadOnlyList<V25StatusInstance> statuses, IReadOnlyList<V25ShieldInstance> shields, long currentTick,
        int staggerPoints = 0, int staggerImmuneTicks = 0, int staggerRecoveryTicks = 0, int bossPatternIndex = 0, string? bossStoryInstanceId = null)
    {
        if (!CanonicalMode) throw new InvalidOperationException("Canonical content is not enabled.");
        if (_monsters.ContainsKey(uid)) throw new InvalidDataException($"Duplicate canonical monster UID '{uid}'.");
        var definition = _definitions.Monster(definitionId);
        var species = _canonical!.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == speciesId)
            ?? throw new InvalidDataException($"Unknown canonical monster species '{speciesId}'.");
        if (species.PowerTier != powerTier || species.Archetype != archetype || species.CombatStyleId != combatStyleId || species.SignatureSkillId != signatureSkillId)
            throw new InvalidDataException($"Canonical monster '{uid}' definition does not match species '{speciesId}'.");
        if (level < 1 || level > _canonical.Balance.MaxLevel || rank != V25ProgressionRules.RankFromLevel(level) || !Enum.IsDefined(encounterType) || !double.IsFinite(positionX) || !double.IsFinite(positionY) || !double.IsFinite(currentHp) || !double.IsFinite(maxHp) || !double.IsFinite(attackCooldown) || currentHp < 0 || maxHp <= 0 || currentHp > maxHp || !alive && currentHp != 0 || alive && currentHp <= 0 || attackCooldown < 0 || string.IsNullOrWhiteSpace(encounterId) || staggerPoints is < 0 or > 100 || staggerImmuneTicks < 0 || staggerRecoveryTicks < 0 || bossPatternIndex is < 0 or > 2 || encounterType != V25EncounterType.Boss && (staggerPoints != 0 || staggerImmuneTicks != 0 || staggerRecoveryTicks != 0 || bossPatternIndex != 0 || bossStoryInstanceId is not null) || encounterType == V25EncounterType.Boss && string.IsNullOrWhiteSpace(bossStoryInstanceId))
            throw new InvalidDataException($"Canonical monster '{uid}' runtime state is invalid.");
        if (!Enum.TryParse<MonsterAiState>(aiState, ignoreCase: false, out var parsedAi)) throw new InvalidDataException($"Unknown monster AI state '{aiState}'.");
        var stats = V25CombatRules.ComputeStats(V25EntityKind.Monster, powerTier, archetype, rank, level, encounterType, _canonical.Balance).ToLegacyBlock();
        if (Math.Abs(stats.Hp - maxHp) > 0.001) throw new InvalidDataException($"Canonical monster '{uid}' maximum HP does not match pinned balance.");
        var state = new MonsterState(uid, definition.Id, species.Id, rank, level, stats, new Vec2(positionX, positionY), powerTier, archetype, combatStyleId, signatureSkillId, encounterType, rewardEligible)
        { EncounterId = encounterId, AiState = parsedAi, AttackCooldown = attackCooldown, CurrentHp = V25FixedPoint.QuantizeMilli(currentHp), StaggerPoints = staggerPoints, StaggerImmuneTicks = staggerImmuneTicks, StaggerRecoveryTicks = staggerRecoveryTicks, BossPatternIndex = bossPatternIndex, BossStoryInstanceId = bossStoryInstanceId };
        state.Statuses.Restore(uid, statuses, currentTick); state.Shields.Restore(uid, shields, currentTick);
        _monsters.Add(uid, state); _uids.Observe(uid);
        return state;
    }

    public MonsterState? Get(string uid) => _monsters.GetValueOrDefault(uid);
    public IReadOnlyList<MonsterState> AllMonsters() => _monsters.Values.ToArray();
    public IReadOnlyList<MonsterState> AliveMonsters() => _monsters.Values.Where(value => value.Alive).ToArray();
    public void Clear() => _monsters.Clear();

    /// <summary>Advances boss stagger timers once at the start of a canonical fixed tick.</summary>
    public void AdvanceCanonicalTimers()
    {
        if (!CanonicalMode) return;
        foreach (var monster in _monsters.Values.Where(monster => monster.EncounterType == V25EncounterType.Boss))
        {
            if (monster.StaggerImmuneTicks > 0) monster.StaggerImmuneTicks--;
            if (monster.StaggerRecoveryTicks > 0)
            {
                monster.StaggerRecoveryTicks--;
                if (monster.StaggerRecoveryTicks == 0 && monster.Alive) monster.AiState = MonsterAiState.Attack;
            }
        }
    }

    /// <summary>Applies one valid stun to a boss stagger meter; returns true when the meter breaks.</summary>
    public bool ApplyCanonicalStagger(string uid)
    {
        if (!CanonicalMode || !_monsters.TryGetValue(uid, out var monster) || !monster.Alive || monster.EncounterType != V25EncounterType.Boss || monster.StaggerImmuneTicks > 0) return false;
        monster.StaggerPoints = Math.Min(100, checked(monster.StaggerPoints + 25));
        if (monster.StaggerPoints < 100) return false;
        monster.StaggerPoints = 0;
        monster.StaggerImmuneTicks = V25CombatRules.MillisecondsToTicksCeil(4000);
        monster.StaggerRecoveryTicks = V25CombatRules.MillisecondsToTicksCeil(1500);
        monster.AiState = MonsterAiState.Hit;
        return true;
    }

    private static void RequireValidProgression(string speciesId, int level, int rank)
    {
        var expected = CombatPowerRules.GlobalLevelToRank(level);
        if (rank != expected) throw new InvalidOperationException($"Monster {speciesId} rank {rank} does not match global level {level} (expected {expected}).");
        var species = BalanceDefinition.SpeciesById(speciesId); var range = BalanceDefinition.TierRange(species.PowerTier);
        if (rank < range.MinimumRank || rank > range.MaximumRank) throw new InvalidOperationException($"Monster {speciesId} rank {rank} is outside {species.PowerTier} range [{range.MinimumRank}..{range.MaximumRank}].");
    }
}

public sealed record MonsterNavigationState(string Uid, double HomeX, double HomeY, string? TargetUid, bool IsReturning);
