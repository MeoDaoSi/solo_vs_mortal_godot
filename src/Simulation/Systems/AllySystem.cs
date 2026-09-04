using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems;

public sealed class AllySystem
{
    private readonly Dictionary<string, AllyState> _allies = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly GameDefinitions _definitions;
    public AllySystem(EventBus events, UidGenerator uids, GameDefinitions definitions) { _events = events; _uids = uids; _definitions = definitions; }

    public AllyState SpawnSoul(OwnedSoulState soul, SoulBannerState banner, Vec2 position)
    {
        var monster = _definitions.Monster(soul.Origin.MonsterDefinitionId);
        var stats = CombatPowerRules.GetStatBlock(new EntityStatContext(EntityType.Soul, soul.Level, soul.Origin.Rank, soul.Origin.SpeciesId),
            [new StatModifiers(HpPercent: banner.Computed.SoulHpPercent, AtkPercent: banner.Computed.SoulAtkPercent), new StatModifiers(Scalar: banner.Computed.SummonModifier)]);
        var ally = new AllyState(_uids.Create("ally"), monster.Id, soul.Origin.SpeciesId, soul.Origin.DisplayName, soul.Id, soul.Origin.Rank, soul.Level, stats, _definitions.Soul.Summon.Ai, position);
        _allies.Add(ally.Uid, ally); _events.Publish(new AllySpawnedEvent(ally.Uid, monster.Id, ally.SpeciesId, ally.Rank, ally.Level, position)); return ally;
    }

    public void Update(double deltaSeconds, MonsterSystem monsters, Vec2 playerPosition)
    {
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

    public AllyState? TakeDamage(string uid, double amount)
    {
        if (!_allies.TryGetValue(uid, out var ally) || !ally.Alive || amount <= 0) return null;
        ally.CurrentHp = System.Math.Max(0, ally.CurrentHp - amount);
        if (!ally.Alive) { ally.AiState = AllyAiState.Dead; _allies.Remove(uid); _events.Publish(new AllyDefeatedEvent(uid, ally.DefinitionId, ally.SpeciesId, ally.Position)); }
        return ally;
    }
    public bool Remove(string uid) => _allies.Remove(uid);
    public AllyState? Get(string uid) => _allies.GetValueOrDefault(uid);
    public IReadOnlyList<AllyState> AliveAllies() => _allies.Values.Where(ally => ally.Alive).ToArray();
}
