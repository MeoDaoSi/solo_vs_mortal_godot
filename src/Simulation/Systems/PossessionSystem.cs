using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.State.V25;

namespace SoloVsMortal.Simulation.Systems;

public enum StartPossessionFailure { SoulNotFound, SoulNotBound, SoulNotReady, PossessionUnavailable, PossessionAlreadyActive }
public sealed record StartPossessionResult(bool Success, string? SoulId = null, string? ProfileId = null, StartPossessionFailure? Failure = null);
public sealed record PossessionSaveData(string SoulId, string ProfileId, double RemainingSeconds);
public sealed record V25PossessionSnapshot(
    string SourceInstanceId,
    string SoulId,
    string SpeciesId,
    int SoulLevel,
    int SoulRank,
    long SyncMicro,
    IReadOnlyList<string> MilestoneIds,
    double SoulBaseHp,
    double SoulBaseAttack,
    double SoulBaseDefense,
    double SoulBaseMoveSpeed,
    double TransferHp,
    double TransferAttack,
    double TransferDefense,
    double TransferMoveSpeed,
    string SignatureSkillId,
    string CapabilityId,
    double DurationSeconds,
    double RemainingSeconds,
    double CooldownSeconds,
    long StartedTick);
public sealed record V25PossessionCooldownState(string SpeciesId, double RemainingSeconds);

public sealed class PossessionSystem
{
    private PossessionSaveData? _active;
    private V25PossessionSnapshot? _canonicalActive;
    // Canonical gameplay time is expressed in 60 Hz domain ticks.  Seconds remain at the
    // application boundary for HUD/save compatibility, always derived from these integers.
    private readonly Dictionary<string, int> _canonicalCooldowns = new(StringComparer.Ordinal);
    private int _canonicalActiveRemainingTicks;
    private CanonicalContentRegistry? _canonical;
    private Func<string, long>? _canonicalSync;
    private Func<string, IReadOnlySet<string>>? _canonicalMilestones;
    private Func<bool>? _canonicalActionLocked;
    private Func<long>? _canonicalTickProvider;
    private Action<string>? _canonicalCancelUnreleased;
    private Action<string>? _canonicalCapabilityRevoked;
    private int _canonicalTransitionTicks;
    private readonly EventBus _events; private readonly SoulNatureDefinitions _definitions; private readonly SoulSystem _souls; private readonly SoulBannerSystem _banners; private readonly SummonSystem _runtime; private readonly PlayerModifierSystem _modifiers; private readonly CapabilitySystem _capabilities; private readonly PlayerSystem _player;
    public PossessionSystem(EventBus events, SoulNatureDefinitions definitions, SoulSystem souls, SoulBannerSystem banners, SummonSystem runtime, PlayerModifierSystem modifiers, CapabilitySystem capabilities, PlayerSystem? player = null) { _events = events; _definitions = definitions; _souls = souls; _banners = banners; _runtime = runtime; _modifiers = modifiers; _capabilities = capabilities; _player = player ?? throw new ArgumentNullException(nameof(player)); }
    public string? ActiveSoulId => _canonicalActive?.SoulId ?? _active?.SoulId;
    public double RemainingSeconds => _canonicalActive is null ? _active?.RemainingSeconds ?? 0 : SecondsFromTicks(_canonicalActiveRemainingTicks);
    public bool CanonicalTransitionLocked => _canonicalTransitionTicks > 0;
    /// <summary>Remaining fixed ticks of the post-possession action lock. This is save state,
    /// not a presentation debounce: suspend must not clear the 300 ms transition boundary.</summary>
    public int CanonicalTransitionLockTicks => _canonicalTransitionTicks;
    public PossessionSaveData? Snapshot() => _active;
    public V25PossessionSnapshot? CanonicalSnapshot => _canonicalActive is { } active
        ? active with { RemainingSeconds = SecondsFromTicks(_canonicalActiveRemainingTicks) }
        : null;
    public IReadOnlyList<V25PossessionCooldownState> CanonicalCooldowns => _canonicalCooldowns.OrderBy(item => item.Key, StringComparer.Ordinal)
        .Select(item => new V25PossessionCooldownState(item.Key, SecondsFromTicks(item.Value))).ToArray();

    public void ConfigureCanonical(CanonicalContentRegistry canonical, Func<string, long> sync, Func<string, IReadOnlySet<string>> milestones, Func<bool> actionLocked, Func<long>? tick = null, Action<string>? cancelUnreleased = null, Action<string>? capabilityRevoked = null)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _canonicalSync = sync ?? throw new ArgumentNullException(nameof(sync));
        _canonicalMilestones = milestones ?? throw new ArgumentNullException(nameof(milestones));
        _canonicalActionLocked = actionLocked ?? throw new ArgumentNullException(nameof(actionLocked));
        _canonicalTickProvider = tick ?? (() => 0);
        _canonicalCancelUnreleased = cancelUnreleased;
        _canonicalCapabilityRevoked = capabilityRevoked;
    }

    public bool IsSkillGranted(string skillId) => _canonicalActive is { } active && active.SignatureSkillId == skillId;
    /// <summary>Returns the captured Soul rank for its signature grant; inactive/non-signature skills remain basic rank.</summary>
    public int EffectiveSignatureRank(string skillId) => _canonicalActive is { } active && active.SignatureSkillId == skillId ? active.SoulRank : 1;
    public StartPossessionResult Start(string soulId)
    {
        if (_canonical is not null) return StartCanonical(soulId);
        if (_active is not null) return new(false, Failure: StartPossessionFailure.PossessionAlreadyActive);
        var soul = _souls.OwnedSoul(soulId); if (soul is null) return new(false, Failure: StartPossessionFailure.SoulNotFound);
        var banner = _banners.Starter(); if (banner is null || !_banners.IsBound(soulId, banner.Id)) return new(false, Failure: StartPossessionFailure.SoulNotBound);
        if (_runtime.Runtime(soulId).Status != SoulRuntimeStatus.Ready) return new(false, Failure: StartPossessionFailure.SoulNotReady);
        var nature = _definitions.Natures[soul.SoulNatureId]; if (nature.PossessionProfileId is null) return new(false, Failure: StartPossessionFailure.PossessionUnavailable);
        var profile = _definitions.PossessionProfiles[nature.PossessionProfileId]; if (!_runtime.BeginPossession(soulId)) return new(false, Failure: StartPossessionFailure.SoulNotReady);
        _active = new(soulId, profile.Id, profile.DurationSeconds); Apply(profile); _events.Publish(new PossessionStartedEvent(soulId, profile.Id, profile.DisplayName, profile.DurationSeconds)); return new(true, soulId, profile.Id);
    }
    public bool End()
    {
        if (_canonical is not null) return EndCanonical(false);
        if (_active is null) return false; var active = _active; var profile = _definitions.PossessionProfiles[active.ProfileId]; _active = null; Clear(); _runtime.EndPossession(active.SoulId, profile.CooldownSeconds); _events.Publish(new PossessionEndedEvent(active.SoulId, active.ProfileId, profile.CooldownSeconds)); return true;
    }
    public bool EndCanonicalForDeath() => _canonical is not null && EndCanonical(true);
    public void Update(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        if (_canonical is not null)
        {
            var elapsedTicks = ElapsedCanonicalTicks(deltaSeconds);
            _canonicalTransitionTicks = Math.Max(0, _canonicalTransitionTicks - elapsedTicks);
            foreach (var key in _canonicalCooldowns.Keys.ToArray())
            {
                var remaining = Math.Max(0, _canonicalCooldowns[key] - elapsedTicks);
                if (remaining == 0) _canonicalCooldowns.Remove(key); else _canonicalCooldowns[key] = remaining;
            }
            if (_canonicalActive is not null)
            {
                _canonicalActiveRemainingTicks = Math.Max(0, _canonicalActiveRemainingTicks - elapsedTicks);
                if (_canonicalActiveRemainingTicks == 0) EndCanonical(false);
            }
            return;
        }
        if (_active is { } legacy)
        {
            _active = legacy with { RemainingSeconds = Math.Max(0, legacy.RemainingSeconds - deltaSeconds) };
            if (_active.RemainingSeconds == 0) End();
        }
    }
    public void Restore(PossessionSaveData? data)
    {
        if (_canonical is not null) { RestoreCanonical(null, Array.Empty<V25PossessionCooldownState>()); return; }
        _active = null; Clear(); if (data is null || !double.IsFinite(data.RemainingSeconds) || data.RemainingSeconds <= 0) return;
        var soul = _souls.OwnedSoul(data.SoulId); var banner = _banners.Starter(); if (soul is null || banner is null || !_banners.IsBound(data.SoulId, banner.Id)) return;
        if (!_definitions.PossessionProfiles.TryGetValue(data.ProfileId, out var profile) || _definitions.Natures[soul.SoulNatureId].PossessionProfileId != data.ProfileId || !_runtime.BeginPossession(data.SoulId)) return;
        _active = data with { RemainingSeconds = System.Math.Min(profile.DurationSeconds, data.RemainingSeconds) }; Apply(profile);
    }
    public void RestoreCanonical(V25PossessionSnapshot? data, IEnumerable<V25PossessionCooldownState> cooldowns, int transitionLockTicks = 0)
    {
        if (_canonical is null || _canonicalSync is null || _canonicalMilestones is null) throw new InvalidOperationException("Canonical possession is not configured.");
        ArgumentNullException.ThrowIfNull(cooldowns);
        if (transitionLockTicks is < 0 or > 18) throw new InvalidDataException("Canonical possession transition lock is invalid.");
        var stagedCooldowns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in cooldowns)
        {
            if (row is null || !_canonical.SpeciesForProfile(_canonical.ActiveProfileId).Any(species => species.Id == row.SpeciesId) || !double.IsFinite(row.RemainingSeconds) || row.RemainingSeconds <= 0 || !stagedCooldowns.TryAdd(row.SpeciesId, CanonicalTimerTicks(row.RemainingSeconds)))
                throw new InvalidDataException("Canonical possession cooldown snapshot is invalid.");
        }
        V25PossessionSnapshot? staged = null;
        if (data is not null)
        {
            ValidateCanonicalSnapshot(data);
            var ownedSoul = _souls.CanonicalOwnedSpecies(data.SpeciesId);
            if (ownedSoul is null || !string.Equals(ownedSoul.Id, data.SoulId, StringComparison.Ordinal)) throw new InvalidDataException("Canonical possession source Soul is not owned.");
            if (stagedCooldowns.ContainsKey(data.SpeciesId)) throw new InvalidDataException("Active canonical possession cannot have a species cooldown.");
            staged = data;
        }
        _canonicalCooldowns.Clear(); foreach (var row in stagedCooldowns) _canonicalCooldowns.Add(row.Key, row.Value);
        _canonicalTransitionTicks = transitionLockTicks;
        _canonicalActive = null; _canonicalActiveRemainingTicks = 0; ClearCanonical();
        if (staged is not null)
        {
            if (_runtime.Runtime(staged.SoulId).Status != SoulRuntimeStatus.Possessed && !_runtime.BeginPossession(staged.SoulId)) throw new InvalidDataException("Canonical possession source is not Ready during restore.");
            _canonicalActive = staged;
            _canonicalActiveRemainingTicks = CanonicalTimerTicks(staged.RemainingSeconds);
            ApplyCanonical(staged);
        }
    }

    private StartPossessionResult StartCanonical(string soulId)
    {
        if (_canonical is null || _canonicalSync is null || _canonicalMilestones is null || _canonicalActionLocked is null) return new(false, Failure: StartPossessionFailure.PossessionUnavailable);
        if (_canonicalActive is not null || _active is not null) return new(false, Failure: StartPossessionFailure.PossessionAlreadyActive);
        if (!_player.State.Alive || _canonicalTransitionTicks > 0 || _canonicalActionLocked()) return new(false, Failure: StartPossessionFailure.SoulNotReady);
        var speciesId = soulId.StartsWith("owned.", StringComparison.Ordinal) ? soulId[6..] : soulId;
        var species = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == speciesId);
        var soul = species is null ? null : _souls.CanonicalOwnedSpecies(species.Id);
        var ownership = species is null ? null : _souls.CanonicalDensity?.GetOwnership(species.Id);
        if (species is null || soul is null || ownership is null) return new(false, Failure: StartPossessionFailure.SoulNotFound);
        if (!ownership.IsOwned || _banners.CanonicalBannerRank < species.PowerTier) return new(false, Failure: StartPossessionFailure.SoulNotBound);
        if (_runtime.Runtime(soul.Id).Status != SoulRuntimeStatus.Ready || _runtime.CanonicalSnapshot().FirstOrDefault(item => item.SpeciesId == species.Id)?.VitalityMicro <= 0)
            return new(false, Failure: StartPossessionFailure.SoulNotReady);
        if (!_souls.CanonicalMode) return new(false, Failure: StartPossessionFailure.PossessionUnavailable);
        if (_canonicalCooldowns.TryGetValue(species.Id, out var existingCooldown) && existingCooldown > 0)
            return new(false, Failure: StartPossessionFailure.SoulNotReady);
        if (!_runtime.BeginPossession(soul.Id)) return new(false, Failure: StartPossessionFailure.SoulNotReady);
        var syncMicro = Math.Clamp(_canonicalSync(species.Id), 0, 100 * V25FixedPoint.DensitySyncScale);
        var sync = V25FixedPoint.FromMicro(syncMicro);
        var milestones = _canonicalMilestones(species.Id).ToHashSet(StringComparer.Ordinal);
        var soulRank = ownership.SoulRank(_souls.CanonicalDensity!.DensityDefinition);
        var burden = 1 + _canonical.Balance.Possession.BurdenPt * (species.PowerTier - 1) + _canonical.Balance.Possession.BurdenRank * (soulRank - 1);
        var durationMultiplier = 1 + _canonical.Balance.Possession.DurationSync * sync + (milestones.Contains($"sync.{species.Id}.20") ? 0.05 : 0) + (milestones.Contains($"sync.{species.Id}.100") ? 0.10 : 0);
        var duration = Math.Clamp(_canonical.Balance.Possession.DurationBaseSeconds * durationMultiplier / burden, _canonical.Balance.Possession.DurationRange[0], _canonical.Balance.Possession.DurationRange[1]);
        var cooldownMultiplier = 1 - _canonical.Balance.Possession.CooldownSync * sync;
        var cooldown = Math.Clamp(_canonical.Balance.Possession.CooldownBaseSeconds * burden * cooldownMultiplier * (milestones.Contains($"sync.{species.Id}.60") ? 0.95 : 1), _canonical.Balance.Possession.CooldownRange[0], _canonical.Balance.Possession.CooldownRange[1]);
        var durationTicks = CanonicalTimerTicks(duration);
        var cooldownTicks = CanonicalTimerTicks(cooldown);
        duration = SecondsFromTicks(durationTicks);
        cooldown = SecondsFromTicks(cooldownTicks);
        var baseStats = V25CombatRules.ComputeStats(V25EntityKind.Ally, species.PowerTier, species.Archetype, soulRank, soul.Level, balance: _canonical.Balance);
        var playerStats = _canonicalPlayerStats();
        var transferMultiplier = 1 + (milestones.Contains($"sync.{species.Id}.40") ? 0.05 : 0) + (milestones.Contains($"sync.{species.Id}.80") ? 0.05 : 0);
        var transferRate = _canonical.Balance.Possession.TransferBase + _canonical.Balance.Possession.TransferSync * sync;
        var transferHp = Math.Min(baseStats.MaxHp * transferRate * transferMultiplier, playerStats.Hp * _canonical.Balance.Possession.TransferCapPermanentStat);
        var transferAtk = Math.Min(baseStats.Attack * transferRate * transferMultiplier, playerStats.Atk * _canonical.Balance.Possession.TransferCapPermanentStat);
        var transferDef = Math.Min(baseStats.Defense * transferRate * transferMultiplier, playerStats.Def * _canonical.Balance.Possession.TransferCapPermanentStat);
        var transferSpeed = Math.Min(baseStats.MoveSpeed * transferRate * transferMultiplier, playerStats.Speed * _canonical.Balance.Possession.TransferCapPermanentStat);
        var signature = species.SignatureSkillId;
        var sourceInstance = $"possession.{species.Id}.{_canonicalTick()}";
        var snapshot = new V25PossessionSnapshot(sourceInstance, soul.Id, species.Id, soul.Level, soulRank, syncMicro, milestones.Order(StringComparer.Ordinal).ToArray(), baseStats.MaxHp, baseStats.Attack, baseStats.Defense, baseStats.MoveSpeed,
            transferHp, transferAtk, transferDef, transferSpeed, signature, species.CapabilityId, duration, duration, cooldown, _canonicalTick());
        _canonicalActive = snapshot;
        _canonicalActiveRemainingTicks = durationTicks;
        ApplyCanonical(snapshot);
        _events.Publish(new PossessionStartedEvent(soul.Id, sourceInstance, species.Name, duration));
        return new(true, soul.Id, sourceInstance);
    }

    private bool EndCanonical(bool death)
    {
        if (_canonicalActive is not { } active) return false;
        _canonicalCancelUnreleased?.Invoke(_player.State.Uid);
        _player.State.Statuses.RemoveSource(_player.State.Uid, active.SourceInstanceId);
        _canonicalActive = null; _canonicalActiveRemainingTicks = 0; ClearCanonical(active.SourceInstanceId);
        if (active.SyncMicro >= 40 * V25FixedPoint.DensitySyncScale) _canonicalCapabilityRevoked?.Invoke(active.CapabilityId);
        _runtime.EndPossession(active.SoulId, 0);
        _canonicalCooldowns[active.SpeciesId] = CanonicalTimerTicks(active.CooldownSeconds);
        _canonicalTransitionTicks = Math.Max(_canonicalTransitionTicks, V25CombatRules.MillisecondsToTicksCeil(300));
        _events.Publish(new PossessionEndedEvent(active.SoulId, active.SourceInstanceId, active.CooldownSeconds));
        return true;
    }

    private void ApplyCanonical(V25PossessionSnapshot active)
    {
        _modifiers.SetSource(PlayerModifierSource.Possession,
            [new StatModifiers(HpFlat: active.TransferHp, AtkFlat: active.TransferAttack, DefFlat: active.TransferDefense, SpeedFlat: active.TransferMoveSpeed)]);
        if (active.SyncMicro >= 40 * V25FixedPoint.DensitySyncScale)
            _capabilities.SetSource(CapabilitySource.Possession, active.SourceInstanceId, [active.CapabilityId]);
        else _capabilities.RemoveSource(CapabilitySource.Possession, active.SourceInstanceId);
    }

    private void ClearCanonical(string? sourceInstanceId = null)
    {
        if (sourceInstanceId is null)
            foreach (var source in _capabilities.Snapshot().Where(item => item.Source == CapabilitySource.Possession).ToArray()) _capabilities.RemoveSource(source.Source, source.SourceInstanceId);
        else _capabilities.RemoveSource(CapabilitySource.Possession, sourceInstanceId);
        _modifiers.SetSource(PlayerModifierSource.Possession, Array.Empty<StatModifiers>());
    }

    private (double Hp, double Atk, double Def, double Speed) _canonicalPlayerStats() => (_player.State.MaxHp, _player.State.Stats.Atk, _player.State.Stats.DefRaw, _player.State.Stats.SpeedRaw);
    private long _canonicalTick() => _canonicalTickProvider?.Invoke() ?? 0;
    private static int ElapsedCanonicalTicks(double deltaSeconds) => Math.Max(1, checked((int)Math.Round(deltaSeconds * 60, MidpointRounding.AwayFromZero)));
    private static int CanonicalTimerTicks(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) throw new InvalidDataException("Canonical timer seconds are invalid.");
        // Gameplay timers originate in milliseconds/formula output and are rounded up once
        // when they enter the 60 Hz domain.  A tiny epsilon preserves exact 1/60 save values.
        return Math.Max(1, checked((int)Math.Ceiling(seconds * 60 - 1e-9)));
    }
    private static double SecondsFromTicks(int ticks) => ticks / 60.0;

    private void ValidateCanonicalSnapshot(V25PossessionSnapshot active)
    {
        if (_canonical is null || active is null || string.IsNullOrWhiteSpace(active.SourceInstanceId) || string.IsNullOrWhiteSpace(active.SoulId) || active.SoulLevel < 1 || active.SoulRank != V25ProgressionRules.RankFromLevel(active.SoulLevel) || active.SyncMicro < 0 || active.SyncMicro > 100 * V25FixedPoint.DensitySyncScale || !double.IsFinite(active.RemainingSeconds) || active.RemainingSeconds <= 0 || !double.IsFinite(active.DurationSeconds) || active.RemainingSeconds > active.DurationSeconds || !double.IsFinite(active.CooldownSeconds) || active.CooldownSeconds <= 0 || active.StartedTick < 0)
            throw new InvalidDataException("Canonical possession snapshot is invalid.");
        var species = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == active.SpeciesId)
            ?? throw new InvalidDataException($"Canonical possession species '{active.SpeciesId}' is unavailable.");
        if (!active.SourceInstanceId.StartsWith($"possession.{active.SpeciesId}.", StringComparison.Ordinal) || active.SignatureSkillId != species.SignatureSkillId || active.CapabilityId != species.CapabilityId)
            throw new InvalidDataException("Canonical possession source grant does not match species definition.");
        var baseStats = V25CombatRules.ComputeStats(V25EntityKind.Ally, species.PowerTier, species.Archetype, active.SoulRank, active.SoulLevel, balance: _canonical.Balance);
        if (Math.Abs(baseStats.MaxHp - active.SoulBaseHp) > 0.001 || Math.Abs(baseStats.Attack - active.SoulBaseAttack) > 0.001 || Math.Abs(baseStats.Defense - active.SoulBaseDefense) > 0.001 || Math.Abs(baseStats.MoveSpeed - active.SoulBaseMoveSpeed) > 0.001)
            throw new InvalidDataException("Canonical possession Soul base stats do not match its snapshot level/rank.");
        var permanent = _canonicalPlayerStats();
        var cap = _canonical.Balance.Possession.TransferCapPermanentStat;
        if (active.TransferHp < 0 || active.TransferHp > permanent.Hp * cap + 0.001 || active.TransferAttack < 0 || active.TransferAttack > permanent.Atk * cap + 0.001 || active.TransferDefense < 0 || active.TransferDefense > permanent.Def * cap + 0.001 || active.TransferMoveSpeed < 0 || active.TransferMoveSpeed > permanent.Speed * cap + 0.001)
            throw new InvalidDataException("Canonical possession transfer exceeds the permanent stat cap.");
    }
    private void Apply(PossessionProfileDefinition profile) { _modifiers.SetSource(PlayerModifierSource.Possession, [ModifierRules.FromDefinition(profile.Modifiers)]); _capabilities.SetSource(CapabilitySource.Possession, profile.CapabilityIds); }
    private void Clear() { _modifiers.SetSource(PlayerModifierSource.Possession, Array.Empty<StatModifiers>()); _capabilities.SetSource(CapabilitySource.Possession, Array.Empty<string>()); }
}
