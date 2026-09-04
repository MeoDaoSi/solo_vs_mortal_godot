using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public sealed class CombatSystem : IDisposable
{
    private readonly EventBus _events; private readonly PlayerSystem _player; private readonly MonsterSystem _monsters; private readonly IDisposable _attackSubscription;
    public CombatSystem(EventBus events, PlayerSystem player, MonsterSystem monsters) { _events = events; _player = player; _monsters = monsters; _attackSubscription = events.Subscribe<CombatAttackEvent>(OnAttack); }

    public void Update(double deltaSeconds, bool attackPressed)
    {
        var player = _player.State;
        player.AttackCooldown = System.Math.Max(0, player.AttackCooldown - deltaSeconds);
        if (!attackPressed || !player.Alive || player.AttackCooldown > 0) return;
        var target = _monsters.AliveMonsters().Select(monster => (monster, distance: monster.Position.DistanceTo(player.Position))).Where(item => item.distance <= player.AttackRange).OrderBy(item => item.distance).FirstOrDefault().monster;
        if (target is null) return;
        player.AttackCooldown = CombatPowerRules.GetAttackCooldown(EntityType.Player, player.Stats.SpeedRaw);
        var damage = CombatRules.CalculateDamage(player.Stats.Atk, target.Stats.DefRaw, CombatRules.BasicAttackMultiplier);
        _events.Publish(new CombatAttackEvent(player.Uid, CombatantKind.Player, target.Uid, CombatantKind.Monster, damage.FinalDamage));
        _monsters.TakeDamage(target.Uid, damage.FinalDamage, player.Uid);
    }

    private void OnAttack(CombatAttackEvent attack)
    {
        if (attack.AttackerKind != CombatantKind.Monster || attack.TargetKind != CombatantKind.Player) return;
        var monster = _monsters.Get(attack.AttackerUid);
        if (monster?.Alive == true && _player.State.Alive) _player.TakeDamage(attack.Amount);
    }

    public void Dispose() => _attackSubscription.Dispose();
}
