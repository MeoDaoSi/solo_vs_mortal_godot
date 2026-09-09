using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public sealed class CombatSystem : IDisposable
{
    private readonly EventBus _events; private readonly PlayerSystem _player; private readonly MonsterSystem _monsters; private readonly IDisposable _attackSubscription; private readonly V25CombatCoordinator? _canonical;
    public CombatSystem(EventBus events, PlayerSystem player, MonsterSystem monsters, UidGenerator? uids = null, CanonicalContentRegistry? canonical = null, AllySystem? allies = null, Pcg32Streams? streams = null)
    {
        _events = events; _player = player; _monsters = monsters; _attackSubscription = events.Subscribe<CombatAttackEvent>(OnAttack);
        if (canonical is not null && uids is not null && allies is not null)
            _canonical = new V25CombatCoordinator(events, uids, player, monsters, allies, canonical, streams ?? new Pcg32Streams(1));
    }

    public string? BufferedSkillId => _canonical?.BufferedSkillId;
    public int BufferedSkillTicks => _canonical?.BufferedSkillTicks ?? 0;
    public void RestoreInputBuffer(string? skillId, int ticks) => (_canonical ?? throw new InvalidOperationException("Canonical combat is not enabled.")).RestoreInputBuffer(skillId, ticks);
    public bool IsActionLocked(string uid) => _canonical?.ActiveCasts.Any(cast => cast.CasterUid == uid) == true;
    public bool CanonicalMode => _canonical is not null;
    public IReadOnlyList<V25CastView> ActiveCasts => _canonical?.ActiveCasts ?? Array.Empty<V25CastView>();
    public IReadOnlyList<V25ProjectileView> ActiveProjectiles => _canonical?.ActiveProjectiles ?? Array.Empty<V25ProjectileView>();
    public IReadOnlyList<V25CooldownView> Cooldowns => _canonical?.Cooldowns ?? Array.Empty<V25CooldownView>();
    public IReadOnlyList<V25HitKeyView> HitKeys => _canonical?.HitKeys ?? Array.Empty<V25HitKeyView>();
    public IReadOnlyList<V25KnockbackView> Knockbacks => _canonical?.Knockbacks ?? Array.Empty<V25KnockbackView>();
    public bool CanStartCanonicalDodge => _canonical?.CanStartPlayerDodge ?? true;
    public void RequestPlayerSkill(string skillId) => (_canonical ?? throw new InvalidOperationException("Canonical combat is not enabled.")).RequestPlayerSkill(skillId);
    public void PrepareCanonicalFixedTick(long tick) => _canonical?.PrepareFixedTick(tick);
    public void RestoreCanonicalRuntime(IReadOnlyList<V25CastView> casts, IReadOnlyList<V25ProjectileView> projectiles,
        IReadOnlyList<V25CooldownView> cooldowns, IReadOnlyList<V25HitKeyView> hitKeys, IReadOnlyList<V25KnockbackView>? knockbacks = null) => (_canonical ?? throw new InvalidOperationException("Canonical combat is not enabled.")).RestoreRuntime(casts, projectiles, cooldowns, hitKeys, knockbacks);
    public void ClearCanonicalRuntime() => _canonical?.ClearRuntime();
    public void CancelUnreleasedCanonicalCasts(string sourceUid) => _canonical?.CancelUnreleasedCasts(sourceUid);
    public void ConfigureCanonicalPlayerSkillGrant(Func<string, bool> grant) => _canonical?.ConfigurePlayerSkillGrant(grant);
    public void ConfigureCanonicalPlayerSkillRank(Func<string, int> rank) => _canonical?.ConfigurePlayerSkillRank(rank);
    public void AdvanceCanonicalMovement(long tick) => _canonical?.AdvanceCanonicalMovement(tick);

    public void UpdateCanonical(long tick, Vec2 moveInput, Vec2 aim, bool attackHeld, bool dodgeStarted = false) => _canonical?.Tick(tick, moveInput, aim, attackHeld, dodgeStarted);

    public void Update(double deltaSeconds, bool attackPressed)
    {
        if (_canonical is not null) return;
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
        // Canonical damage is already committed by V25CombatCoordinator. Keeping the
        // prototype event listener active here would apply every monster hit twice.
        if (_canonical is not null) return;
        if (attack.AttackerKind != CombatantKind.Monster || attack.TargetKind != CombatantKind.Player) return;
        var monster = _monsters.Get(attack.AttackerUid);
        if (monster?.Alive == true && _player.State.Alive) _player.TakeDamage(attack.Amount);
    }

    public void Dispose() => _attackSubscription.Dispose();
}
