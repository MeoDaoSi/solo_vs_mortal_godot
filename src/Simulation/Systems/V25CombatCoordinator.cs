using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems;

/// <summary>
/// Fixed tick V2.5 combat owner. Cast acceptance, release, active pose, recovery and projectile travel are
/// simulation operations; presentation animation never emits damage. The coordinator is intentionally separate
/// from the prototype CombatSystem so a loaded legacy save can keep its old compatibility path.
/// </summary>
public sealed class V25CombatCoordinator
{
    private readonly EventBus _events;
    private readonly UidGenerator _uids;
    private readonly PlayerSystem _player;
    private readonly MonsterSystem _monsters;
    private readonly AllySystem _allies;
    private readonly CanonicalContentRegistry _canonical;
    private readonly Pcg32Streams _streams;
    private readonly Dictionary<string, ActiveCast> _casts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string SourceUid, string SkillId), int> _cooldowns = new();
    private readonly List<Projectile> _projectiles = [];
    private readonly HashSet<V25HitKey> _hitKeys = [];
    private readonly Dictionary<string, KnockbackState> _knockbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _castSkillIds = new(StringComparer.Ordinal);
    private string? _requestedPlayerSkill;
    private int _requestedPlayerSkillTicks;
    private long? _preparedTick;
    private Vec2 _latestAim;
    private Vec2 _latestMove;
    private Func<string, bool>? _playerSkillGrant;
    private Func<string, int>? _playerSkillRank;

    public V25CombatCoordinator(EventBus events, UidGenerator uids, PlayerSystem player, MonsterSystem monsters, AllySystem allies, CanonicalContentRegistry canonical, Pcg32Streams streams)
    {
        _events = events;
        _uids = uids;
        _player = player;
        _monsters = monsters;
        _allies = allies;
        _canonical = canonical;
        _streams = streams;
    }

    public string? BufferedSkillId => _requestedPlayerSkill;
    public int BufferedSkillTicks => _requestedPlayerSkill is null ? 0 : _requestedPlayerSkillTicks;
    public void RestoreInputBuffer(string? skillId, int remainingTicks)
    {
        if (remainingTicks is < 0 or > 6 || skillId is null && remainingTicks != 0 || skillId is not null && (remainingTicks == 0 || FindSkill(skillId) is null))
            throw new InvalidDataException("Invalid buffered combat intent.");
        _requestedPlayerSkill = skillId;
        _requestedPlayerSkillTicks = remainingTicks;
    }

    public IReadOnlyList<V25CastView> ActiveCasts => _casts.Values.Select(cast => cast.View()).OrderBy(cast => cast.CastId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25ProjectileView> ActiveProjectiles => _projectiles.Select(projectile => projectile.View()).OrderBy(projectile => projectile.CastId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25CooldownView> Cooldowns => _cooldowns.OrderBy(entry => entry.Key.SourceUid, StringComparer.Ordinal).ThenBy(entry => entry.Key.SkillId, StringComparer.Ordinal)
        .Select(entry => new V25CooldownView(entry.Key.SourceUid, entry.Key.SkillId, entry.Value)).ToArray();
    public IReadOnlyList<V25HitKeyView> HitKeys => _hitKeys.OrderBy(key => key.CastId, StringComparer.Ordinal).ThenBy(key => key.TargetLifeUid, StringComparer.Ordinal).ThenBy(key => key.HitIndex)
        .Select(key => new V25HitKeyView(key.CastId, key.TargetLifeUid, key.HitIndex)).ToArray();
    public IReadOnlyList<V25KnockbackView> Knockbacks => _knockbacks.Values.OrderBy(item => item.TargetUid, StringComparer.Ordinal)
        .Select(item => item.View()).ToArray();

    /// <summary>Dodge may interrupt only a released basic recovery, per the canonical action-lock rule.</summary>
    public bool CanStartPlayerDodge => !_casts.Values.Any(cast => cast.SourceUid == _player.State.Uid &&
        !(cast.Spec.SkillId == "player_basic_attack" && cast.Released && cast.ElapsedTicks >= cast.Spec.WindupTicks + cast.Spec.ActiveTicks));

    /// <summary>Restores the current cast/projectile transaction boundary after all actor state has staged.</summary>
    public void RestoreRuntime(IReadOnlyList<V25CastView> casts, IReadOnlyList<V25ProjectileView> projectiles,
        IReadOnlyList<V25CooldownView> cooldowns, IReadOnlyList<V25HitKeyView> hitKeys, IReadOnlyList<V25KnockbackView>? knockbacks = null)
    {
        ArgumentNullException.ThrowIfNull(casts); ArgumentNullException.ThrowIfNull(projectiles); ArgumentNullException.ThrowIfNull(cooldowns); ArgumentNullException.ThrowIfNull(hitKeys);
        if (_casts.Count != 0 || _projectiles.Count != 0 || _cooldowns.Count != 0 || _hitKeys.Count != 0 || _knockbacks.Count != 0)
            throw new InvalidOperationException("Canonical runtime restore requires an empty combat transaction boundary.");
        var stagedCasts = new List<ActiveCast>();
        var castIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in casts)
        {
            RequireRuntimeId(view.CastId, "castId"); RequireRuntimeId(view.CasterUid, "casterUid"); RequireRuntimeId(view.SkillId, "skillId");
            if (!castIds.Add(view.CastId) || view.AcceptedTick < 0 || view.ReleaseTick < view.AcceptedTick || view.EndTick < view.ReleaseTick || view.ElapsedTicks < 0 || !Enum.IsDefined(view.SourceKind) || view.Phase is V25CastPhase.Finished or V25CastPhase.Canceled)
                throw new InvalidDataException($"Runtime cast '{view.CastId}' is invalid.");
            if (!TryGetActor(view.CasterUid, out var source) || source.Kind != view.SourceKind) throw new InvalidDataException($"Runtime cast '{view.CastId}' source is missing.");
            if (!view.Released && !source.Alive) throw new InvalidDataException($"Runtime cast '{view.CastId}' retains an unreleased cast from a dead source.");
            var skill = FindSkill(view.SkillId) ?? throw new InvalidDataException($"Runtime cast '{view.CastId}' skill is unknown.");
            if (view.TargetUid is not null && !TryGetActor(view.TargetUid, out _)) throw new InvalidDataException($"Runtime cast '{view.CastId}' target is missing.");
            var groundPoint = view.GroundPoint;
            var direction = view.Aim.Normalized();
            if (direction == Vec2.Zero) throw new InvalidDataException($"Runtime cast '{view.CastId}' direction is zero.");
            var spec = RestoreCastSpec(view, source, skill);
            var expectedReleaseTick = checked(view.AcceptedTick + spec.WindupTicks);
            var expectedEndTick = checked(expectedReleaseTick + spec.ActiveTicks + spec.RecoveryTicks);
            if (view.ReleaseTick != expectedReleaseTick || view.EndTick != expectedEndTick || !view.Released && view.Phase != V25CastPhase.Windup || view.Released && view.Phase == V25CastPhase.Windup || !view.Released && view.ElapsedTicks >= spec.WindupTicks || view.Released && view.ElapsedTicks < spec.WindupTicks)
                throw new InvalidDataException($"Runtime cast '{view.CastId}' phase/timing does not match its canonical skill duration.");
            if (view.ElapsedTicks > spec.WindupTicks + spec.ActiveTicks + spec.RecoveryTicks) throw new InvalidDataException($"Runtime cast '{view.CastId}' elapsed time exceeds its authored duration.");
            var cast = new ActiveCast(view.CastId, view.CasterUid, view.SourceKind, spec, direction, view.TargetUid, groundPoint, view.AcceptedTick)
            { ElapsedTicks = view.ElapsedTicks, Released = view.Released };
            stagedCasts.Add(cast);
        }
        var stagedProjectiles = new List<Projectile>();
        var projectileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var view in projectiles)
        {
            RequireRuntimeId(view.CastId, "projectile.castId"); RequireRuntimeId(view.CasterUid, "projectile.casterUid"); RequireRuntimeId(view.SkillId, "projectile.skillId");
            if (!projectileIds.Add(view.CastId) || view.RemainingTicks < 0 || view.ReleasedTick < 0 || view.HitIndex < 0 || view.Travelled < 0 || !Enum.IsDefined(view.SourceKind)) throw new InvalidDataException($"Runtime projectile '{view.CastId}' is invalid.");
            // A released projectile always retains release offense, including when its caster
            // still exists but has changed stats since release. Never re-snapshot on load.
            if (view.SourceAttack < 0 || view.SourceRank is < 1 or > 9 || !double.IsFinite(view.SourceAttack) || !double.IsFinite(view.SourceDefense) || !double.IsFinite(view.SourceEncounterAttackFactor) || view.SourceEncounterAttackFactor < 0)
                throw new InvalidDataException($"Runtime projectile '{view.CastId}' has an invalid offense snapshot.");
            if (TryGetActor(view.CasterUid, out var liveSource) && liveSource.Kind != view.SourceKind)
                throw new InvalidDataException($"Runtime projectile '{view.CastId}' source kind does not match its actor.");
            var faction = view.SourceKind switch { V25EntityKind.Monster => Faction.Enemy, V25EntityKind.Player => Faction.Player, _ => Faction.Ally };
            var source = new ActorView(view.CasterUid, view.SourceKind, faction, true, view.Position, view.SourceAttack, view.SourceDefense, 1, 1, 0, view.SourceRank, view.SourceEncounterAttackFactor,
                view.SourceCombatStyleId ?? "fist", new(), false);
            var skill = FindSkill(view.SkillId) ?? throw new InvalidDataException($"Runtime projectile '{view.CastId}' skill is unknown.");
            var direction = view.Direction.Normalized();
            if (direction == Vec2.Zero) throw new InvalidDataException($"Runtime projectile '{view.CastId}' direction is zero.");
            var spec = MakeSpec(skill, source, skill.Id == "player_basic_attack");
            stagedProjectiles.Add(new Projectile(view.CastId, source, spec, view.Position, direction, view.RemainingTicks, view.ReleasedTick) { Travelled = view.Travelled, HitIndex = view.HitIndex });
        }
        var stagedCooldowns = new Dictionary<(string SourceUid, string SkillId), int>();
        foreach (var cooldown in cooldowns)
        {
            RequireRuntimeId(cooldown.SourceUid, "cooldown.sourceUid"); RequireRuntimeId(cooldown.SkillId, "cooldown.skillId");
            if (cooldown.RemainingTicks <= 0 || !stagedCooldowns.TryAdd((cooldown.SourceUid, cooldown.SkillId), cooldown.RemainingTicks)) throw new InvalidDataException("Runtime cooldown is invalid or duplicated.");
            if ((!TryGetActor(cooldown.SourceUid, out _) && !_monsters.HasDormantActor(cooldown.SourceUid)) || FindSkill(cooldown.SkillId) is null) throw new InvalidDataException("Runtime cooldown references an unknown actor or skill.");
        }
        var stagedHitKeys = new HashSet<V25HitKey>();
        foreach (var key in hitKeys)
        {
            RequireRuntimeId(key.CastId, "hitKey.castId"); RequireRuntimeId(key.TargetLifeUid, "hitKey.targetLifeUid");
            if (key.HitIndex < 0 || !stagedHitKeys.Add(new V25HitKey(key.CastId, key.TargetLifeUid, key.HitIndex))) throw new InvalidDataException("Runtime hit key is invalid or duplicated.");
        }
        var stagedKnockbacks = new Dictionary<string, KnockbackState>(StringComparer.Ordinal);
        foreach (var view in knockbacks ?? Array.Empty<V25KnockbackView>())
        {
            RequireRuntimeId(view.TargetUid, "knockback.targetUid");
            if (!double.IsFinite(view.Direction.X) || !double.IsFinite(view.Direction.Y)) throw new InvalidDataException($"Runtime knockback for '{view.TargetUid}' has a non-finite direction.");
            if (!stagedKnockbacks.TryAdd(view.TargetUid, new KnockbackState(view.TargetUid, view.Direction.Normalized(), view.RemainingDistance, view.RemainingTicks)) ||
                view.RemainingTicks <= 0 || view.RemainingDistance <= 0 || !double.IsFinite(view.RemainingDistance) || view.Direction.Normalized() == Vec2.Zero ||
                !TryGetActor(view.TargetUid, out var knockbackTarget) || !knockbackTarget.Alive || knockbackTarget.Kind == V25EntityKind.Monster && _monsters.Get(view.TargetUid)?.EncounterType == V25EncounterType.Boss)
                throw new InvalidDataException($"Runtime knockback for '{view.TargetUid}' is invalid.");
        }
        foreach (var cast in stagedCasts) { _casts.Add(cast.CastId, cast); _castSkillIds[cast.CastId] = cast.Spec.SkillId; }
        _projectiles.AddRange(stagedProjectiles); foreach (var entry in stagedCooldowns) _cooldowns.Add(entry.Key, entry.Value); foreach (var key in stagedHitKeys) _hitKeys.Add(key); foreach (var entry in stagedKnockbacks) _knockbacks.Add(entry.Key, entry.Value);
        _uids.Observe(casts.Select(item => item.CastId).Concat(projectiles.Select(item => item.CastId)).FirstOrDefault() ?? "");
        foreach (var id in casts.Select(item => item.CastId).Concat(projectiles.Select(item => item.CastId))) _uids.Observe(id);
    }

    public void ClearRuntime()
    {
        _casts.Clear(); _projectiles.Clear(); _cooldowns.Clear(); _hitKeys.Clear(); _knockbacks.Clear(); _castSkillIds.Clear();
        _requestedPlayerSkill = null; _requestedPlayerSkillTicks = 0; _preparedTick = null;
    }

    public void ConfigurePlayerSkillGrant(Func<string, bool> grant) => _playerSkillGrant = grant ?? throw new ArgumentNullException(nameof(grant));
    public void ConfigurePlayerSkillRank(Func<string, int> rank) => _playerSkillRank = rank ?? throw new ArgumentNullException(nameof(rank));

    /// <summary>Possession revoke cancels only casts that have not released; projectiles retain their offense snapshot.</summary>
    public void CancelUnreleasedCasts(string sourceUid)
    {
        foreach (var cast in _casts.Values.Where(item => item.SourceUid == sourceUid && !item.Released).ToArray()) _casts.Remove(cast.CastId);
        if (sourceUid == _player.State.Uid) _requestedPlayerSkill = null;
    }

    /// <summary>Advances displacement effects in the canonical actor movement phase.</summary>
    public void AdvanceCanonicalMovement(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        foreach (var item in _knockbacks.Values.ToArray())
        {
            if (!TryGetActor(item.TargetUid, out var target) || !target.Alive || item.RemainingTicks <= 0 || item.RemainingDistance <= 0)
            {
                _knockbacks.Remove(item.TargetUid);
                continue;
            }
            var intended = Math.Min(item.StepDistance, item.RemainingDistance);
            var start = target.Position;
            MoveDash(target, intended, item.Direction);
            var moved = start.DistanceTo(TryGetActor(item.TargetUid, out var movedTarget) ? movedTarget.Position : start);
            item.RemainingDistance = Math.Max(0, item.RemainingDistance - moved);
            item.RemainingTicks--;
            if (moved <= 0 || item.RemainingTicks <= 0 || item.RemainingDistance <= 0) _knockbacks.Remove(item.TargetUid);
        }
    }

    /// <summary>Runs the expiry/DoT boundary before actors process movement and intent for this tick.</summary>
    public void PrepareFixedTick(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (_preparedTick == tick) return;
        ExpireTimersAndDot(tick);
        _monsters.AdvanceCanonicalTimers();
        _preparedTick = tick;
    }

    public void RequestPlayerSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId)) throw new ArgumentException("Skill ID is required.", nameof(skillId));
        _requestedPlayerSkill = skillId;
        _requestedPlayerSkillTicks = 6; // canonical 100 ms input buffer at 60 fixed ticks.
    }

    /// <summary>Advance exactly one already-authorized 60 Hz domain tick.</summary>
    public void Tick(long tick, Vec2 moveInput, Vec2 aim, bool attackHeld, bool dodgeStarted = false)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        _latestAim = aim;
        _latestMove = moveInput;
        if (dodgeStarted) CancelBasicRecovery();
        if (_requestedPlayerSkill is not null && _requestedPlayerSkillTicks <= 0) _requestedPlayerSkill = null;

        // Canonical order: expiry and due DoT, then intent/accept, then release/projectile/damage.
        PrepareFixedTick(tick);
        _preparedTick = null;
        DecrementCooldowns();
        AcceptPlayerIntent(tick, moveInput, aim, attackHeld);
        AcceptActorIntents(tick);
        AdvanceCasts(tick);
        AdvanceProjectiles(tick);
        if (_requestedPlayerSkill is not null && --_requestedPlayerSkillTicks == 0) _requestedPlayerSkill = null;
    }

    private void CancelBasicRecovery()
    {
        foreach (var cast in _casts.Values.Where(item => item.SourceKind == V25EntityKind.Player && item.Spec.SkillId == "player_basic_attack" && item.Released && item.ElapsedTicks >= item.Spec.WindupTicks + item.Spec.ActiveTicks).ToArray())
            _casts.Remove(cast.CastId); // accepted cooldown/cost remain committed.
    }

    private void ExpireTimersAndDot(long tick)
    {
        _player.State.Shields.Expire(tick);
        _player.State.Statuses.Tick(tick, status => ApplyDot(GetActor(_player.State.Uid), status, tick));
        foreach (var monster in _monsters.AllMonsters())
        {
            monster.Shields.Expire(tick);
            monster.Statuses.Tick(tick, status => ApplyDot(GetActor(monster.Uid), status, tick));
        }
        foreach (var ally in _allies.AliveAllies())
        {
            ally.Shields.Expire(tick);
            ally.Statuses.Tick(tick, status => ApplyDot(GetActor(ally.Uid), status, tick));
        }
    }

    private void DecrementCooldowns()
    {
        foreach (var key in _cooldowns.Keys.ToArray())
        {
            var remaining = _cooldowns[key] - 1;
            if (remaining <= 0) _cooldowns.Remove(key);
            else _cooldowns[key] = remaining;
        }
    }

    private void AcceptPlayerIntent(long tick, Vec2 moveInput, Vec2 aim, bool attackHeld)
    {
        var player = GetActor(_player.State.Uid);
        if (!player.Alive || player.Statuses.Has(player.Uid, "stun") || _player.State.DodgeRemainingTicks > 0 || (!attackHeld && _requestedPlayerSkill is null) || HasCast(player.Uid)) return;
        var skillId = _requestedPlayerSkill ?? "player_basic_attack";
        var skill = FindSkill(skillId);
        if (skill is null) { _requestedPlayerSkill = null; return; }
        if (!IsPlayerSkillExposed(skill)) { _requestedPlayerSkill = null; return; }
        var spec = MakeSpec(skill, player, isBasic: skill.Id == "player_basic_attack");
        var direction = ResolveDirection(player, aim, moveInput);
        var target = skill.TargetMode == "TargetEnemy" ? SelectTargetAtAccept(player, spec, direction) : null;
        Vec2? groundPoint = skill.TargetMode == "GroundPoint" ? aim : null;
        if (groundPoint is { } point && player.Position.DistanceTo(point) > spec.RangeUnits) return;
        if (AcceptCast(tick, player, spec, direction, target?.Uid, groundPoint)) _requestedPlayerSkill = null;
    }

    private void AcceptActorIntents(long tick)
    {
        foreach (var monster in _monsters.AliveMonsters())
        {
            if (monster.IsReturning || monster.AiState is MonsterAiState.Idle or MonsterAiState.Return) continue;
            var source = GetActor(monster.Uid);
            if (monster.TargetUid is null || !TryGetActor(monster.TargetUid, out var target) || !target.Alive) continue;
            if (HasCast(monster.Uid)) continue;
            if (monster.EncounterType == V25EncounterType.Boss)
            {
                var pattern = monster.BossPatternIndex;
                var liveAdds = _monsters.AliveMonsters().Count(item => item.EncounterId == $"bossadd.{monster.BossStoryInstanceId}");
                if (pattern == 2 && liveAdds >= 2) pattern = 0;
                var bossSpec = MakeBossPatternSpec(source, pattern, monster);
                var bossDirection = ResolveDirection(source, target.Position, Vec2.Zero);
                var ground = pattern == 1 ? target.Position : (Vec2?)null; // target position snapshot at accept/windup start.
                if (AcceptCast(tick, source, bossSpec, bossDirection, null, ground)) monster.BossPatternIndex = (monster.BossPatternIndex + 1) % 3;
                continue;
            }
            var signature = monster.SignatureSkillId is null ? null : FindSkill(monster.SignatureSkillId);
            var hostileTarget = target.Alive && source.Position.DistanceTo(target.Position) <= 192 && IsAllowedTarget(source, target, "Hostile") ? target : (ActorView?)null;
            var useSignature = signature is not null && !HasCooldown(monster.Uid, signature.Id) &&
                !(signature.TargetMode == "Self" && (source.Statuses.Has(source.Uid, "guard") || source.Statuses.Has(source.Uid, "ward"))) &&
                CanUseSignature(source, signature, hostileTarget);
            var skill = useSignature ? signature : FindSkill("player_basic_attack");
            if (skill is null || !CanActorIntent(source, skill)) continue;
            var spec = MakeSpec(skill, source, !useSignature);
            if (!useSignature && source.Position.DistanceTo(target.Position) > spec.RangeUnits + 10) continue;
            if (skill.TargetMode is "TargetEnemy" && !ValidTargetAtAccept(source, target, skill)) continue;
            if (skill.TargetHpFractionMax is { } maxFraction && target.CurrentHp / target.MaxHp > maxFraction) continue;
            var direction = ResolveDirection(source, target.Position, Vec2.Zero);
            Vec2? groundPoint = skill.TargetMode == "GroundPoint" ? target.Position : null;
            AcceptCast(tick, source, spec, direction, skill.TargetMode == "TargetEnemy" ? target.Uid : null, groundPoint);
        }

        foreach (var ally in _allies.AliveAllies())
        {
            var source = GetActor(ally.Uid);
            if (HasCast(ally.Uid) || ally.AiState is AllyAiState.Return or AllyAiState.Follow or AllyAiState.Acquire) continue;
            var signature = ally.SignatureSkillId is null ? null : FindSkill(ally.SignatureSkillId);
            var useSignature = signature is not null && !HasCooldown(ally.Uid, signature.Id) &&
                !(signature.TargetMode == "Self" && (source.Statuses.Has(source.Uid, "guard") || source.Statuses.Has(source.Uid, "ward")));
            var skill = useSignature ? signature : FindSkill("player_basic_attack");
            if (skill is null || !CanActorIntent(source, skill)) continue;
            var target = ally.TargetUid is { } selectedTarget && TryGetActor(selectedTarget, out var selectedActor) && selectedActor.Alive
                ? selectedActor
                : _monsters.AliveMonsters().Select(item => GetActor(item.Uid)).OrderBy(item => item.Position.DistanceTo(source.Position)).ThenBy(item => item.Uid, StringComparer.Ordinal).FirstOrDefault();
            var hostileTarget = !string.IsNullOrEmpty(target.Uid) && IsAllowedTarget(source, target, "Hostile") && source.Position.DistanceTo(target.Position) <= 320 ? target : (ActorView?)null;
            useSignature = useSignature && CanUseSignature(source, signature!, hostileTarget);
            skill = useSignature ? signature : FindSkill("player_basic_attack");
            if (skill is null || !CanActorIntent(source, skill)) continue;
            var spec = MakeSpec(skill, source, !useSignature);
            if (string.IsNullOrEmpty(target.Uid) || !useSignature && target.Position.DistanceTo(source.Position) > spec.RangeUnits + 10 || skill.TargetMode == "TargetEnemy" && !ValidTargetAtAccept(source, target, skill)) continue;
            if (skill.TargetHpFractionMax is { } maxFraction && target.CurrentHp / target.MaxHp > maxFraction) continue;
            var direction = ResolveDirection(source, target.Position, Vec2.Zero);
            Vec2? groundPoint = skill.TargetMode == "GroundPoint" ? target.Position : null;
            AcceptCast(tick, source, spec, direction, skill.TargetMode == "TargetEnemy" ? target.Uid : null, groundPoint);
        }
    }

    private bool CanActorIntent(ActorView source, CanonicalSkillDefinition skill) =>
        source.Alive && !(source.Kind == V25EntityKind.Monster && _monsters.Get(source.Uid)?.StaggerRecoveryTicks > 0) && !source.Statuses.Has(source.Uid, "stun") &&
        (source.Faction != Faction.Enemy || _player.State.Alive);

    private bool CanUseSignature(ActorView source, CanonicalSkillDefinition signature, ActorView? hostileTarget)
    {
        if (signature.TargetMode == "Self" && signature.Effects.Any(effect => effect is "heal" or "ward" or "guard" or "haste" or "veil" or "cleanse") && hostileTarget is null) return false;
        if (signature.TargetMode == "Self" && signature.Effects.Contains("heal", StringComparer.Ordinal) && source.CurrentHp / source.MaxHp >= 0.70) return false;
        if (signature.TargetHpFractionMax is { } targetMax && hostileTarget is { } target && target.CurrentHp / target.MaxHp > targetMax) return false;
        return true;
    }

    private bool AcceptCast(long tick, ActorView source, CastSpec spec, Vec2 direction, string? targetUid, Vec2? groundPoint = null)
    {
        if (!source.Alive || source.Statuses.Has(source.Uid, "stun") || HasCast(source.Uid) || HasCooldown(source.Uid, spec.SkillId)) return false;
        if (spec.Skill.TargetMode == "TargetEnemy" && (string.IsNullOrEmpty(targetUid) || !TryGetActor(targetUid, out var target) || !ValidTargetAtAccept(source, target, spec.Skill))) return false;
        if (spec.Skill.TargetMode == "GroundPoint" && (groundPoint is null || source.Position.DistanceTo(groundPoint.Value) > spec.RangeUnits)) return false;
        if (spec.SpiritCost > 0 && source.Kind == V25EntityKind.Player && !_player.TrySpendSpirit(spec.SpiritCost)) return false;
        var castId = _uids.Create("cast");
        var cast = new ActiveCast(castId, source.Uid, source.Kind, spec, direction, targetUid, groundPoint, tick);
        _casts.Add(castId, cast);
        _castSkillIds[castId] = spec.SkillId;
        if (spec.CooldownTicks > 0) _cooldowns[(source.Uid, spec.SkillId)] = spec.CooldownTicks;
        return true;
    }

    private void AdvanceCasts(long tick)
    {
        foreach (var cast in _casts.Values.ToArray())
        {
            // A release earlier in this deterministic pass may have staggered the
            // source and removed its windup/recovery casts. Do not process the
            // stale snapshot entry after that cancellation.
            if (!_casts.ContainsKey(cast.CastId)) continue;
            if (!cast.Released && (!TryGetActor(cast.SourceUid, out var source) || !source.Alive || _monsters.Get(cast.SourceUid)?.IsReturning == true || _allies.Get(cast.SourceUid)?.AiState == AllyAiState.Return || source.Statuses.Has(source.Uid, "stun")))
            {
                _casts.Remove(cast.CastId); // accepted cost/cooldown remain consumed on cancel
                continue;
            }

            if (!cast.Released && cast.ElapsedTicks >= cast.Spec.WindupTicks)
            {
                cast.Released = true;
                ReleaseCast(cast, tick);
            }

            if (cast.ElapsedTicks >= cast.Spec.WindupTicks + cast.Spec.ActiveTicks + cast.Spec.RecoveryTicks)
                _casts.Remove(cast.CastId);
            else cast.ElapsedTicks++;
        }
    }

    private void ReleaseCast(ActiveCast cast, long tick)
    {
        if (!TryGetActor(cast.SourceUid, out var source) || !source.Alive) return;
        cast.Direction = source.Kind == V25EntityKind.Player ? ResolveDirection(source, _latestAim, _latestMove) : cast.Direction;
        source.Statuses.RemoveEffects(source.Uid, "veil");
        var spec = cast.Spec;
        if (spec.Shape == "SummonAdds")
        {
            var boss = _monsters.Get(source.Uid);
            if (boss is null || boss.EncounterType != V25EncounterType.Boss) return;
            var region = _canonical.Content.Regions.First(item => item.BossId == boss.EncounterId);
            var live = _monsters.AliveMonsters().Count(item => item.EncounterId == $"bossadd.{boss.BossStoryInstanceId}");
            foreach (var speciesId in region.HomeSpecies.Take(Math.Max(0, 2 - live)))
            {
                var offset = live++ == 0 ? -32 : 32;
                _monsters.Spawn(speciesId, new MonsterSpawnOptions(Level: boss.Level, Position: new Vec2(boss.Position.X + offset, boss.Position.Y + 32), EncounterType: V25EncounterType.Debug, RewardEligible: false, EncounterId: $"bossadd.{boss.BossStoryInstanceId}"));
            }
            return;
        }
        if (spec.Shape.Equals("Projectile", StringComparison.OrdinalIgnoreCase))
        {
            var lifetimeMs = checked((int)System.Math.Ceiling(spec.RangeUnits / _canonical.Balance.Combat.ProjectileSpeed * 1000) + 100);
            _projectiles.Add(new Projectile(cast.CastId, source, spec, source.Position, cast.Direction, V25CombatRules.MillisecondsToTicksCeil(lifetimeMs), tick));
            return;
        }
        if (spec.Shape.Equals("Dash", StringComparison.OrdinalIgnoreCase))
        {
            MoveDash(source, spec.RangeUnits, cast.Direction);
            if (TryGetActor(cast.SourceUid, out var movedSource)) source = movedSource;
        }
        if (spec.Skill.TargetMode == "Self" && spec.Shape.Equals("Circle", StringComparison.OrdinalIgnoreCase))
        {
            // Self-targeted circles are still real release hits when their faction is Hostile
            // (Whirlwind/Chaos/Despair). Covenant circles use the same geometry for allied effects.
            var circleTargets = QueryTargets(source, spec, source.Position, cast.Direction).Take(spec.MaxTargets).ToArray();
            var circleHitIndex = 0;
            foreach (var target in circleTargets)
            {
                if (spec.Skill.TargetFaction == "Hostile") ApplyDirect(cast.CastId, source, target, spec, circleHitIndex++, tick);
                else ApplyEffects(cast.CastId, source, target, spec, tick);
            }
            return;
        }
        if (spec.Skill.TargetMode is "Self" || spec.Shape.Equals("Self", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var target in TargetsForSelf(source, spec.Skill)) ApplyEffects(cast.CastId, source, target, spec, tick);
            return;
        }

        if (spec.Skill.TargetMode == "TargetEnemy")
        {
            // Targeted casts keep the accepted identity, then validate range/LOS again at release.
            if (cast.TargetUid is not null && TryGetActor(cast.TargetUid, out var target) && ValidTargetAtAccept(source, target, spec.Skill))
                ApplyDirect(cast.CastId, source, target, spec, 0, tick);
            return;
        }

        var origin = cast.GroundPoint ?? source.Position;
        var targets = QueryTargets(source, spec, origin, cast.Direction).Take(spec.MaxTargets).ToArray();
        var hitIndex = 0;
        foreach (var target in targets) ApplyDirect(cast.CastId, source, target, spec, hitIndex++, tick);
    }

    private void AdvanceProjectiles(long tick)
    {
        var step = _canonical.Balance.Combat.ProjectileSpeed / SoloVsMortal.Core.Loop.SimulationClock.SimulationHz;
        foreach (var projectile in _projectiles.ToArray())
        {
            projectile.RemainingTicks--;
            var previous = projectile.Position;
            projectile.Position = new Vec2(previous.X + projectile.Direction.X * step, previous.Y + projectile.Direction.Y * step);
            projectile.Travelled += step;
            if (!_player.IsSegmentFree(previous, projectile.Position))
            {
                _projectiles.Remove(projectile);
                continue;
            }
            var target = QueryProjectileTargets(projectile.Source, projectile.Spec, previous, projectile.Position).FirstOrDefault();
            if (target.Uid is not null)
            {
                ApplyDirect(projectile.CastId, projectile.Source, target, projectile.Spec, projectile.HitIndex++, tick);
                _projectiles.Remove(projectile);
                continue;
            }
            if (projectile.RemainingTicks <= 0 || projectile.Travelled >= projectile.Spec.RangeUnits) _projectiles.Remove(projectile);
        }
    }

    private void ApplyDirect(string castId, ActorView source, ActorView target, CastSpec spec, int hitIndex, long tick)
    {
        if (!target.Alive || !IsAllowedTarget(source, target, spec.Skill.TargetFaction)) return;
        if (spec.Skill.TargetHpFractionMax is { } fraction && target.CurrentHp / target.MaxHp > fraction) return;
        // Invulnerability is a precondition failure. It must not consume the crit stream or a hit key.
        if (target.Invulnerable) return;
        var key = new V25HitKey(castId, target.Uid, hitIndex);
        if (!_hitKeys.Add(key)) return;
        var critical = _streams.CombatCrit.Chance(_canonical.Balance.CritChance);
        var result = V25DamageResolver.Resolve(key, source.Attack, spec.Power, spec.RankPowerScale, source.EncounterAttackFactor,
            ParseDamageType(spec.DamageType), target.Defense, null, target.Statuses.DefensiveReductions(target.Uid), target.Shield, target.CurrentHp,
            critical, target.Invulnerable, armorDenominator: _canonical.Balance.ArmorDenominator, criticalMultiplier: _canonical.Balance.CritMultiplier);
        if (result.Rejected) return;
        ApplyDamage(target, result, source.Uid);
        _events.Publish(new CanonicalDamageResolvedEvent(result.HitKey, source.Uid, target.Uid, result.RawDamage, result.IncomingDamage, result.ShieldAbsorbed, result.HpLoss, result.Critical, result.Rejected, spec.SkillId));
        _events.Publish(new CombatAttackEvent(source.Uid, ToCombatant(source.Kind), target.Uid, ToCombatant(target.Kind), result.IncomingDamage));
        ApplyEffects(castId, source, target, spec, tick);
    }

    private void ApplyDamage(ActorView target, V25DamageResult result, string sourceUid)
    {
        switch (target.Kind)
        {
            case V25EntityKind.Player:
                _player.ApplyCanonicalDamage(result.ShieldAbsorbed, result.HpLoss);
                if (!_player.State.Alive) CancelRuntimeForDeath(target.Uid, clearProjectiles: true);
                break;
            case V25EntityKind.Monster:
                if (_monsters.ApplyCanonicalDamage(target.Uid, result, sourceUid) is { Alive: false }) CancelRuntimeForDeath(target.Uid);
                break;
            case V25EntityKind.Ally:
                if (_allies.ApplyCanonicalDamage(target.Uid, result) is { Alive: false }) CancelRuntimeForDeath(target.Uid);
                break;
        }
    }

    private void CancelRuntimeForDeath(string sourceUid, bool clearProjectiles = false)
    {
        foreach (var cast in _casts.Values.Where(item => item.SourceUid == sourceUid).ToArray()) _casts.Remove(cast.CastId);
        // Released projectiles survive their caster; only player-death cleanup clears the battlefield.
        _knockbacks.Remove(sourceUid);
        if (clearProjectiles) _projectiles.Clear();
        if (sourceUid == _player.State.Uid) _requestedPlayerSkill = null;
    }

    private void ApplyEffects(string castId, ActorView source, ActorView target, CastSpec spec, long tick)
    {
        var hitTarget = target;
        foreach (var effect in spec.Skill.Effects)
        {
            // Buffs belong to the caster, debuffs to the hit target. Explicit allied
            // circles (Covenant) retain their authored recipient selection.
            var recipient = effect is "heal" or "ward" or "guard" or "haste" or "veil" or "cleanse"
                && spec.Skill.TargetFaction == "Hostile" ? source : hitTarget;
            if (!TryGetActor(recipient.Uid, out target) || !target.Alive) continue;
            switch (effect)
            {
                case "heal":
                {
                    var healed = Heal(target, target.MaxHp * 0.20 * spec.RankPowerScale);
                    if (healed > 0) _events.Publish(new CanonicalEffectResolvedEvent(spec.SkillId, castId, source.Uid, target.Uid, effect, healed, true, tick));
                    break;
                }
                case "ward":
                {
                    var shieldFraction = spec.Skill.ShieldMaxHpFraction ?? 0.20;
                    var shielded = AddShield(castId, target, target.MaxHp * shieldFraction * (spec.Skill.EffectRankScaling ? spec.RankPowerScale : 1), tick);
                    target.Statuses.Apply(castId, target.Uid, "ward", tick, 0, source.Attack);
                    if (shielded > 0) _events.Publish(new CanonicalEffectResolvedEvent(spec.SkillId, castId, source.Uid, target.Uid, effect, shielded, true, tick));
                    break;
                }
                case "guard":
                    target.Statuses.Apply(castId, target.Uid, "guard", tick, 0, source.Attack);
                    _events.Publish(new CanonicalEffectResolvedEvent(spec.SkillId, castId, source.Uid, target.Uid, effect, 0, true, tick));
                    break;
                case "cleanse":
                {
                    var before = target.Statuses.Snapshot(target.Uid).Count(status => status.EffectId is "burn" or "poison" or "chill");
                    target.Statuses.RemoveEffects(target.Uid, "burn", "poison", "chill");
                    var removed = target.Statuses.Snapshot(target.Uid).Count(status => status.EffectId is "burn" or "poison" or "chill");
                    var cleansed = Math.Max(0, before - removed);
                    if (cleansed > 0) _events.Publish(new CanonicalEffectResolvedEvent(spec.SkillId, castId, source.Uid, target.Uid, effect, cleansed, true, tick));
                    break;
                }
                case "knockback": StartKnockback(target, source.Position); break;
                case "burn":
                case "poison":
                case "chill":
                case "stun":
                case "haste":
                case "veil":
                case "taunt":
                    if (effect == "stun" && target.Kind == V25EntityKind.Monster && _monsters.ApplyCanonicalStagger(target.Uid))
                    {
                        foreach (var cast in _casts.Values.Where(item => item.SourceUid == target.Uid).ToArray()) _casts.Remove(cast.CastId);
                        _events.Publish(new CanonicalEffectResolvedEvent(spec.SkillId, castId, source.Uid, target.Uid, effect, 1, true, tick));
                    }
                    else if (effect != "stun" || target.Kind != V25EntityKind.Monster || _monsters.Get(target.Uid)?.EncounterType != V25EncounterType.Boss)
                    {
                        var dotRatio = effect switch { "burn" => 0.15, "poison" => 0.10, _ => spec.Power * 0.15 };
                        if (effect != "taunt" || (target.Kind == V25EntityKind.Monster && _monsters.Get(target.Uid)?.EncounterType != V25EncounterType.Boss && target.Position.DistanceTo(source.Position) <= 96))
                            target.Statuses.Apply(effect == "taunt" ? source.Uid : castId, target.Uid, effect, tick, dotRatio, source.Attack);
                        if (effect == "stun") _events.Publish(new CanonicalEffectResolvedEvent(spec.SkillId, castId, source.Uid, target.Uid, effect, 1, true, tick));
                    }
                    break;
            }
        }
    }

    private void ApplyDot(ActorView target, V25StatusInstance status, long tick)
    {
        if (!target.Alive || status.Potency <= 0) return;
        var type = status.EffectId == "burn" ? V25DamageType.Fire : V25DamageType.Toxic;
        var key = new V25HitKey(status.SourceId, target.Uid, checked((int)Math.Min(int.MaxValue, tick)));
        if (!_hitKeys.Add(key)) return;
        var result = V25DamageResolver.Resolve(key, status.SnapshotAttack, status.Potency, 1, 1, type, target.Defense, null,
            target.Statuses.DefensiveReductions(target.Uid), target.Shield, target.CurrentHp, false, target.Invulnerable,
            armorDenominator: _canonical.Balance.ArmorDenominator, criticalMultiplier: _canonical.Balance.CritMultiplier);
        if (!result.Rejected)
        {
            ApplyDamage(target, result, status.SourceId);
            _events.Publish(new CanonicalDamageResolvedEvent(result.HitKey, status.SourceId, target.Uid, result.RawDamage, result.IncomingDamage, result.ShieldAbsorbed, result.HpLoss, false, false,
                _castSkillIds.GetValueOrDefault(status.SourceId)));
        }
    }

    private IEnumerable<ActorView> TargetsForSelf(ActorView source, CanonicalSkillDefinition skill)
    {
        if (skill.TargetFaction is "Allied" or "PlayerAndAllies") return AllActors().Where(target => AreAllied(source, target));
        return new[] { source };
    }

    private IEnumerable<ActorView> QueryTargets(ActorView source, CastSpec spec, Vec2 origin, Vec2 direction)
    {
        return AllActors().Where(target => target.Alive && IsAllowedTarget(source, target, spec.Skill.TargetFaction)).Where(target =>
        {
            var delta = new Vec2(target.Position.X - origin.X, target.Position.Y - origin.Y);
            var distance = delta.Length;
            if (spec.Shape.Equals("Circle", StringComparison.OrdinalIgnoreCase)) return distance <= spec.RadiusUnits + 10;
            if (distance > spec.RangeUnits + 10) return false;
            if (spec.ArcDegrees <= 0) return true;
            var normalized = delta.Normalized();
            var dot = System.Math.Clamp(normalized.X * direction.X + normalized.Y * direction.Y, -1, 1);
            return System.Math.Acos(dot) * 180 / System.Math.PI <= spec.ArcDegrees / 2;
        }).OrderBy(target => target.Position.DistanceTo(origin)).ThenBy(target => target.Uid, StringComparer.Ordinal);
    }

    private IEnumerable<ActorView> QueryProjectileTargets(ActorView source, CastSpec spec, Vec2 start, Vec2 end)
    {
        return AllActors().Where(target => target.Alive && IsAllowedTarget(source, target, spec.Skill.TargetFaction))
            .Where(target => DistanceToSegment(target.Position, start, end) <= _canonical.Balance.Combat.ProjectileRadius + 10)
            .OrderBy(target => target.Position.DistanceTo(start)).ThenBy(target => target.Uid, StringComparer.Ordinal);
    }

    private bool ValidTargetAtAccept(ActorView source, ActorView target, CanonicalSkillDefinition skill)
    {
        if (!target.Alive || !IsAllowedTarget(source, target, skill.TargetFaction)) return false;
        var distance = source.Position.DistanceTo(target.Position);
        return distance <= Math.Max(1, skill.RangeUnits) + 10 && _player.IsSegmentFree(source.Position, target.Position);
    }

    private bool IsAllowedTarget(ActorView source, ActorView target, string targetFaction) => targetFaction switch
    {
        "Self" => target.Uid == source.Uid,
        "Hostile" => !AreAllied(source, target),
        "Allied" or "PlayerAndAllies" => AreAllied(source, target),
        _ => !AreAllied(source, target),
    };

    private static bool AreAllied(ActorView left, ActorView right) => left.Faction == right.Faction || (left.Faction != Faction.Enemy && right.Faction != Faction.Enemy);

    private ActorView? SelectTargetAtAccept(ActorView source, CastSpec spec, Vec2 direction) =>
        QueryTargets(source, spec, source.Position, direction).FirstOrDefault() is { } target && !string.IsNullOrEmpty(target.Uid) ? target : null;

    private bool IsPlayerSkillExposed(CanonicalSkillDefinition skill)
    {
        if (_playerSkillGrant?.Invoke(skill.Id) == true) return true;
        if (!_canonical.SkillsForProfile(_canonical.ActiveProfileId).Any(item => item.Id == skill.Id)) return false;
        if (skill.DefaultSourceKind == "Core") return true;
        if (skill.DefaultSourceKind != "PermanentLearned") return false;
        if (skill.UnlockPlayerRank is { } unlock && _player.State.Rank < unlock) return false;
        // The starter learned list is the only canonical loadout available before Part 5.
        return _canonical.Content.NewGame.LearnedSkillIds.Contains(skill.Id, StringComparer.Ordinal);
    }

    private IEnumerable<ActorView> AllActors()
    {
        yield return GetActor(_player.State.Uid);
        foreach (var monster in _monsters.AllMonsters()) yield return GetActor(monster.Uid);
        foreach (var ally in _allies.AliveAllies()) yield return GetActor(ally.Uid);
    }

    private CastSpec MakeSpec(CanonicalSkillDefinition skill, ActorView source, bool isBasic)
    {
        if (isBasic)
        {
            var style = _canonical.Content.CombatStyles.FirstOrDefault(item => item.Id == source.CombatStyleId)
                ?? throw new InvalidDataException($"Unknown combat style: {source.CombatStyleId}.");
            return new(skill with { Shape = style.Shape }, style.WindupMs, style.ActiveMs, style.RecoveryMs, style.RangeUnits, 0, style.ArcDegrees, style.MaxTargets, style.Power, style.DamageType, 0, 1);
        }
        var effectiveRank = skill.EffectRankScaling
            ? source.Kind == V25EntityKind.Player && _playerSkillRank is not null
                ? _playerSkillRank(skill.Id)
                : source.Rank
            : 1;
        return new(skill, skill.WindupMs, skill.ActiveMs, skill.RecoveryMs, skill.RangeUnits, skill.RadiusUnits, skill.ArcDegrees,
            Math.Max(1, skill.MaxTargets), skill.Power, skill.DamageType, skill.SpiritCost, V25CombatRules.RankPowerScale(effectiveRank));
    }

    private CanonicalSkillDefinition? FindSkill(string id) => _canonical.Content.Skills.FirstOrDefault(skill => skill.Id == id);

    private bool HasCast(string sourceUid) => _casts.Values.Any(cast => cast.SourceUid == sourceUid);
    private bool HasCooldown(string sourceUid, string? skillId) => skillId is not null && _cooldowns.ContainsKey((sourceUid, skillId));
    private static void RequireRuntimeId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.:-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Runtime {label} is invalid.");
    }

    private ActorView GetActor(string uid) => TryGetActor(uid, out var actor) ? actor : new ActorView("", V25EntityKind.Player, Faction.Enemy, false, Vec2.Zero, 0, 0, 0, 0, 0, 1, 1, "fist", new(), false);

    private bool TryGetActor(string uid, out ActorView actor)
    {
        if (_player.State.Uid == uid) { var state = _player.State; actor = new(state.Uid, V25EntityKind.Player, Faction.Player, state.Alive, state.Position, state.Stats.Atk, state.Stats.DefRaw, state.CurrentHp, state.MaxHp, state.Shield, state.Rank, 1, state.CombatStyleId, state.Statuses, _player.IsInvulnerable); return true; }
        if (_monsters.Get(uid) is { } monster) { actor = new(monster.Uid, V25EntityKind.Monster, Faction.Enemy, monster.Alive, monster.Position, monster.Stats.Atk, monster.Stats.DefRaw, monster.CurrentHp, monster.MaxHp, monster.Shield, monster.Rank, V25EncounterFactors.For(monster.EncounterType).Attack, monster.CombatStyleId, monster.Statuses, monster.IsReturning); return true; }
        if (_allies.Get(uid) is { } ally) { actor = new(ally.Uid, V25EntityKind.Ally, Faction.Ally, ally.Alive, ally.Position, ally.Stats.Atk, ally.Stats.DefRaw, ally.CurrentHp, ally.MaxHp, ally.Shield, ally.Rank, 1, ally.CombatStyleId, ally.Statuses, false); return true; }
        actor = default;
        return false;
    }

    private Vec2 ResolveDirection(ActorView source, Vec2 aim, Vec2 move)
    {
        var candidate = new Vec2(aim.X - source.Position.X, aim.Y - source.Position.Y);
        if (candidate == Vec2.Zero) candidate = move;
        if (candidate == Vec2.Zero && source.Kind == V25EntityKind.Player) candidate = _player.State.Facing;
        if (candidate == Vec2.Zero) candidate = new Vec2(1, 0);
        return candidate.Normalized();
    }

    private void MoveDash(ActorView source, double distance, Vec2 direction)
    {
        switch (source.Kind)
        {
            case V25EntityKind.Player: _player.TryMoveSwept(direction, distance); break;
            case V25EntityKind.Monster when _monsters.Get(source.Uid) is { } monster: monster.Position = _player.NonPlayerSweptPosition(monster.Position, direction, distance); break;
            case V25EntityKind.Ally when _allies.Get(source.Uid) is { } ally: ally.Position = _player.NonPlayerSweptPosition(ally.Position, direction, distance); break;
        }
    }

    private void StartKnockback(ActorView target, Vec2 from)
    {
        if (target.Kind == V25EntityKind.Monster && _monsters.Get(target.Uid)?.EncounterType == V25EncounterType.Boss) return;
        var direction = new Vec2(target.Position.X - from.X, target.Position.Y - from.Y).Normalized();
        if (direction == Vec2.Zero) return;
        var ticks = V25CombatRules.MillisecondsToTicksCeil(200);
        _knockbacks[target.Uid] = new KnockbackState(target.Uid, direction, 48, ticks);
    }

    private double Heal(ActorView target, double amount)
    {
        if (!target.Alive || amount <= 0) return 0;
        var before = target.CurrentHp;
        var current = V25FixedPoint.QuantizeMilli(Math.Min(target.MaxHp, target.CurrentHp + amount));
        if (target.Kind == V25EntityKind.Player) _player.State.CurrentHp = current;
        else if (target.Kind == V25EntityKind.Monster && _monsters.Get(target.Uid) is { } monster) monster.CurrentHp = current;
        else if (target.Kind == V25EntityKind.Ally && _allies.Get(target.Uid) is { } ally) ally.CurrentHp = current;
        return Math.Max(0, current - before);
    }

    private CastSpec MakeBossPatternSpec(ActorView source, int pattern, MonsterState boss)
    {
        var basic = FindSkill("player_basic_attack") ?? throw new InvalidDataException("Canonical basic skill is missing.");
        var recoveryScale = boss.CurrentHp <= boss.MaxHp * 0.5 ? 0.8 : 1.0;
        return pattern switch
        {
            0 => new(basic with { TargetMode = "Direction", Shape = "Cone" }, 700, 100, (int)Math.Round(800 * recoveryScale), 80, 0, 90, 8, 1.5, "Physical", 0, 1),
            1 => new(basic with { TargetMode = "GroundPoint", Shape = "Circle" }, 1000, 100, (int)Math.Round(900 * recoveryScale), 192, boss.EncounterId == "boss.r09" ? 96 : 64, 360, 8, 1.8, "Physical", 0, 1),
            2 => new(basic with { TargetMode = "Self", Shape = "SummonAdds" }, 900, 100, (int)Math.Round(1000 * recoveryScale), 0, 0, 0, 1, 0, "Physical", 0, 1),
            _ => throw new InvalidDataException("Boss pattern index is invalid."),
        };
    }

    private CastSpec RestoreCastSpec(V25CastView view, ActorView source, CanonicalSkillDefinition skill)
    {
        var boss = source.Kind == V25EntityKind.Monster ? _monsters.Get(source.Uid) : null;
        if (boss?.EncounterType != V25EncounterType.Boss) return MakeSpec(skill, source, skill.Id == "player_basic_attack");
        for (var pattern = 0; pattern < 3; pattern++)
        {
            var candidate = MakeBossPatternSpec(source, pattern, boss);
            if (view.ReleaseTick == view.AcceptedTick + candidate.WindupTicks && view.EndTick == view.ReleaseTick + candidate.ActiveTicks + candidate.RecoveryTicks && (pattern == 1) == (view.GroundPoint is not null)) return candidate;
        }
        throw new InvalidDataException($"Runtime boss cast '{view.CastId}' does not match pattern A/B/C.");
    }

    private double AddShield(string sourceId, ActorView target, double amount, long tick)
    {
        if (!target.Alive || amount <= 0) return 0;
        var before = target.Shield;
        switch (target.Kind)
        {
            case V25EntityKind.Player: _player.State.Shields.Apply(sourceId, target.Uid, tick, 180, amount, target.MaxHp); break;
            case V25EntityKind.Monster when _monsters.Get(target.Uid) is { } monster: monster.Shields.Apply(sourceId, target.Uid, tick, 180, amount, target.MaxHp); break;
            case V25EntityKind.Ally when _allies.Get(target.Uid) is { } ally: ally.Shields.Apply(sourceId, target.Uid, tick, 180, amount, target.MaxHp); break;
        }
        return Math.Max(0, target.Shield - before);
    }

    private static double DistanceToSegment(Vec2 point, Vec2 start, Vec2 end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= double.Epsilon) return point.DistanceTo(start);
        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared, 0, 1);
        return point.DistanceTo(new Vec2(start.X + t * dx, start.Y + t * dy));
    }

    private static V25DamageType ParseDamageType(string value) => value switch
    {
        "Physical" => V25DamageType.Physical,
        "Fire" => V25DamageType.Fire,
        "Frost" => V25DamageType.Frost,
        "Toxic" => V25DamageType.Toxic,
        "Arcane" => V25DamageType.Arcane,
        _ => throw new DefinitionException($"Unknown canonical damage type '{value}'.")
    };

    private static CombatantKind ToCombatant(V25EntityKind kind) => kind switch
    {
        V25EntityKind.Player => CombatantKind.Player,
        V25EntityKind.Monster => CombatantKind.Monster,
        V25EntityKind.Ally => CombatantKind.Soul,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private readonly record struct ActorView(string Uid, V25EntityKind Kind, Faction Faction, bool Alive, Vec2 Position, double Attack, double Defense, double CurrentHp, double MaxHp, double Shield, int Rank, double EncounterAttackFactor, string CombatStyleId, V25StatusStore Statuses, bool Invulnerable);

    private sealed record CastSpec(CanonicalSkillDefinition Skill, int WindupMs, int ActiveMs, int RecoveryMs, double RangeUnits, double RadiusUnits, double ArcDegrees, int MaxTargets, double Power, string DamageType, double SpiritCost, double RankPowerScale)
    {
        public string SkillId => Skill.Id;
        public string Shape => Skill.Shape;
        public int WindupTicks => V25CombatRules.MillisecondsToTicksCeil(WindupMs);
        public int ActiveTicks => V25CombatRules.MillisecondsToTicksCeil(ActiveMs);
        public int RecoveryTicks => V25CombatRules.MillisecondsToTicksCeil(RecoveryMs);
        public int CooldownTicks => V25CombatRules.MillisecondsToTicksCeil(Skill.CooldownMs);
    }

    private sealed class ActiveCast
    {
        public ActiveCast(string castId, string sourceUid, V25EntityKind sourceKind, CastSpec spec, Vec2 direction, string? targetUid, Vec2? groundPoint, long acceptedTick)
        { CastId = castId; SourceUid = sourceUid; SourceKind = sourceKind; Spec = spec; Direction = direction; TargetUid = targetUid; GroundPoint = groundPoint; AcceptedTick = acceptedTick; }
        public string CastId { get; }
        public string SourceUid { get; }
        public V25EntityKind SourceKind { get; }
        public CastSpec Spec { get; }
        public Vec2 Direction { get; set; }
        public string? TargetUid { get; }
        public Vec2? GroundPoint { get; }
        public long AcceptedTick { get; }
        public int ElapsedTicks { get; set; }
        public bool Released { get; set; }
        public V25CastView View()
        {
            var phase = !Released ? V25CastPhase.Windup : ElapsedTicks < Spec.WindupTicks + Spec.ActiveTicks ? V25CastPhase.Active : V25CastPhase.Recovery;
            return new(CastId, SourceUid, Spec.SkillId, AcceptedTick, AcceptedTick + Spec.WindupTicks, AcceptedTick + Spec.WindupTicks + Spec.ActiveTicks + Spec.RecoveryTicks, phase, Direction, TargetUid,
                SourceKind, GroundPoint, Released, ElapsedTicks);
        }
    }

    private sealed class Projectile
    {
        public Projectile(string castId, ActorView source, CastSpec spec, Vec2 position, Vec2 direction, int remainingTicks, long releasedTick)
        { CastId = castId; Source = source; Spec = spec; Position = position; Direction = direction; RemainingTicks = remainingTicks; ReleasedTick = releasedTick; }
        public string CastId { get; }
        public ActorView Source { get; }
        public CastSpec Spec { get; }
        public Vec2 Position { get; set; }
        public Vec2 Direction { get; }
        public double Travelled { get; set; }
        public int RemainingTicks { get; set; }
        public int HitIndex { get; set; }
        public long ReleasedTick { get; }
        public V25ProjectileView View() => new(CastId, Source.Uid, Position, Travelled, RemainingTicks, ReleasedTick, HitIndex, Spec.SkillId, Source.Kind, Direction,
            Source.Attack, Source.Defense, Source.Rank, Source.EncounterAttackFactor, Source.CombatStyleId);
    }

    private sealed class KnockbackState
    {
        public KnockbackState(string targetUid, Vec2 direction, double remainingDistance, int remainingTicks)
        {
            TargetUid = targetUid; Direction = direction.Normalized(); RemainingDistance = remainingDistance; RemainingTicks = remainingTicks;
        }
        public string TargetUid { get; }
        public Vec2 Direction { get; }
        public double RemainingDistance { get; set; }
        public int RemainingTicks { get; set; }
        public double StepDistance => RemainingDistance / Math.Max(1, RemainingTicks);
        public V25KnockbackView View() => new(TargetUid, Direction, RemainingDistance, RemainingTicks);
    }
}

public sealed record V25CastView(string CastId, string CasterUid, string SkillId, long AcceptedTick, long ReleaseTick, long EndTick, V25CastPhase Phase, Vec2 Aim, string? TargetUid,
    V25EntityKind SourceKind = V25EntityKind.Player, Vec2? GroundPoint = null, bool Released = false, int ElapsedTicks = 0);
public sealed record V25ProjectileView(string CastId, string CasterUid, Vec2 Position, double Travelled, int RemainingTicks,
    long ReleasedTick = 0, int HitIndex = 0, string SkillId = "", V25EntityKind SourceKind = V25EntityKind.Player, Vec2 Direction = default,
    double SourceAttack = 0, double SourceDefense = 0, int SourceRank = 1, double SourceEncounterAttackFactor = 1, string? SourceCombatStyleId = null);
public sealed record V25CooldownView(string SourceUid, string SkillId, int RemainingTicks);
public sealed record V25HitKeyView(string CastId, string TargetLifeUid, int HitIndex);
public sealed record V25KnockbackView(string TargetUid, Vec2 Direction, double RemainingDistance, int RemainingTicks);
