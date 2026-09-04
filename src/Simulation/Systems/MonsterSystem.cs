using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems;

public readonly record struct SpawnArea(double X, double Y, double Width, double Height);
public sealed record MonsterSpawnOptions(int? Rank = null, int? Level = null, Vec2? Position = null);

public sealed class MonsterSystem
{
    private readonly Dictionary<string, MonsterState> _monsters = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly SeededRng _rng; private readonly GameDefinitions _definitions; private readonly SpawnArea _spawnArea;
    public MonsterSystem(EventBus events, UidGenerator uids, SeededRng rng, GameDefinitions definitions, SpawnArea spawnArea) { _events = events; _uids = uids; _rng = rng; _definitions = definitions; _spawnArea = spawnArea; }

    public MonsterState Spawn(string definitionId, MonsterSpawnOptions? options = null)
    {
        options ??= new MonsterSpawnOptions();
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

    public void Update(double deltaSeconds, PlayerState player)
    {
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

    public MonsterState? TakeDamage(string uid, double amount, string? sourceUid = null)
    {
        if (!_monsters.TryGetValue(uid, out var monster) || !monster.Alive || amount <= 0) return null;
        monster.CurrentHp = System.Math.Max(0, monster.CurrentHp - amount);
        _events.Publish(new MonsterDamagedEvent(uid, amount, monster.CurrentHp, monster.MaxHp));
        if (!monster.Alive)
        {
            monster.AiState = MonsterAiState.Dead;
            _events.Publish(new MonsterDefeatedEvent(uid, monster.DefinitionId, monster.SpeciesId, monster.Rank, CombatPowerRules.RankKey(monster.Rank), CombatPowerRules.RankDisplayName(monster.Rank), monster.Level, monster.Position, sourceUid));
        }
        return monster;
    }

    public MonsterState? Get(string uid) => _monsters.GetValueOrDefault(uid);
    public IReadOnlyList<MonsterState> AliveMonsters() => _monsters.Values.Where(value => value.Alive).ToArray();
    public void Clear() => _monsters.Clear();

    private static void RequireValidProgression(string speciesId, int level, int rank)
    {
        var expected = CombatPowerRules.GlobalLevelToRank(level);
        if (rank != expected) throw new InvalidOperationException($"Monster {speciesId} rank {rank} does not match global level {level} (expected {expected}).");
        var species = BalanceDefinition.SpeciesById(speciesId); var range = BalanceDefinition.TierRange(species.PowerTier);
        if (rank < range.MinimumRank || rank > range.MaximumRank) throw new InvalidOperationException($"Monster {speciesId} rank {rank} is outside {species.PowerTier} range [{range.MinimumRank}..{range.MaximumRank}].");
    }
}
