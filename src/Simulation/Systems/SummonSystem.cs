using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.State.V25;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Systems.V25;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public enum SoulRuntimeStatus { Ready, Summoned, Dispersed, Possessed }
public sealed record SoulRuntimeView(string SoulId, SoulRuntimeStatus Status, string? SummonUid, double RecoverySeconds, double Stability);
public sealed record DispersedSoulSaveData(string SoulId, double RecoverySeconds, double? RecoveryDurationSeconds = null);
public enum SummonFailure { BannerNotFound, SoulNotBound, AlreadySummoned, Dispersed, ActiveLimitReached, SoulNotFound, Possessed, BannerRankTooLow, InsufficientSpirit, NoSpawnSpace, InvalidState }
public sealed record SummonResult(bool Success, string? SummonUid = null, SummonFailure? Failure = null);
public sealed record CanonicalSummonAllResult(IReadOnlyDictionary<string, SummonResult> Results);

public sealed class SummonSystem : IDisposable
{
    private readonly Dictionary<string, string> _summonBySoul = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _soulBySummon = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bannerBySoul = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _recovery = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _recoveryDuration = new(StringComparer.Ordinal);
    private readonly HashSet<string> _possessed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, V25SummonStateSnapshot> _canonicalState = new(StringComparer.Ordinal);
    private CanonicalContentRegistry? _canonical;
    private PlayerSystem? _canonicalPlayer;
    private Func<bool>? _canonicalPossessionActive;
    private Func<bool>? _canonicalAtShrine96;
    private Func<bool>? _canonicalCombatOrHazard;
    private readonly EventBus _events; private readonly GameDefinitions _definitions; private readonly SoulSystem _souls; private readonly SoulBannerSystem _banners; private readonly AllySystem _allies; private readonly IDisposable _defeatSubscription; private readonly IDisposable _unreachableSubscription;
    public SummonSystem(EventBus events, GameDefinitions definitions, SoulSystem souls, SoulBannerSystem banners, AllySystem allies) { _events = events; _definitions = definitions; _souls = souls; _banners = banners; _allies = allies; _defeatSubscription = events.Subscribe<AllyDefeatedEvent>(OnAllyDefeated); _unreachableSubscription = events.Subscribe<AllyUnreachableEvent>(OnAllyUnreachable); }

    public SummonResult Summon(string soulId, string bannerId, Vec2 position)
    {
        if (_souls.CanonicalMode) return SummonCanonical(soulId, position);
        var banner = _banners.Get(bannerId); if (banner is null) return new(false, Failure: SummonFailure.BannerNotFound);
        if (!banner.BoundSoulIds.Contains(soulId)) return new(false, Failure: SummonFailure.SoulNotBound);
        if (_summonBySoul.ContainsKey(soulId)) return new(false, Failure: SummonFailure.AlreadySummoned);
        if (_recovery.GetValueOrDefault(soulId) > 0) return new(false, Failure: SummonFailure.Dispersed);
        if (_possessed.Contains(soulId)) return new(false, Failure: SummonFailure.Possessed);
        if (_bannerBySoul.Values.Count(id => id == bannerId) >= banner.Computed.ActiveLimit) return new(false, Failure: SummonFailure.ActiveLimitReached);
        var soul = _souls.OwnedSoul(soulId); if (soul is null) return new(false, Failure: SummonFailure.SoulNotFound);
        var ally = _allies.SpawnSoul(soul, banner, position); _summonBySoul[soulId] = ally.Uid; _soulBySummon[ally.Uid] = soulId; _bannerBySoul[soulId] = bannerId;
        _events.Publish(new SoulSummonedEvent(soulId, ally.Uid, bannerId, position)); return new(true, ally.Uid);
    }

    public void ConfigureCanonical(CanonicalContentRegistry canonical, PlayerSystem player, Func<bool> possessionActive,
        Func<bool>? atShrine96 = null, Func<bool>? combatOrHazard = null)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _canonicalPlayer = player ?? throw new ArgumentNullException(nameof(player));
        _canonicalPossessionActive = possessionActive ?? throw new ArgumentNullException(nameof(possessionActive));
        _canonicalAtShrine96 = atShrine96;
        _canonicalCombatOrHazard = combatOrHazard;
    }

    /// <summary>Recalls all living canonical Allies at a Spirit zero crossing.</summary>
    public int RecallAllLivingForSpirit() => RecallAllLiving("spirit_empty");

    /// <summary>Player death recalls living Allies while preserving their HP ratio and skill cooldown.</summary>
    public int RecallAllLivingForDeath() => RecallAllLiving("player_death");

    private int RecallAllLiving(string reason)
    {
        if (!_souls.CanonicalMode) return 0;
        var recalled = 0;
        foreach (var state in _canonicalState.Values.Where(item => item.Mode == V25SoulRuntimeMode.Summoned).ToArray())
            if (RecallCanonical(state.SpeciesId)) recalled++;
        return recalled;
    }

    public IReadOnlyList<V25SummonStateSnapshot> CanonicalSnapshot()
    {
        if (_canonical is null || _souls.CanonicalDensity is null) return Array.Empty<V25SummonStateSnapshot>();
        return _souls.CanonicalDensity.Ownership.Values.Where(item => item.IsOwned).OrderBy(item => item.SpeciesId, StringComparer.Ordinal).Select(item => _canonicalState.GetValueOrDefault(item.SpeciesId) ?? new V25SummonStateSnapshot(item.SpeciesId, V25SoulRuntimeMode.Ready, null, V25FixedPoint.DensitySyncScale, 0, 1, 0)).ToArray();
    }

    private SummonResult SummonCanonical(string soulId, Vec2 requestedPosition)
    {
        if (_canonical is null || _canonicalPlayer is null || _canonicalPossessionActive is null || _souls.CanonicalDensity is null) return new(false, Failure: SummonFailure.InvalidState);
        var speciesId = soulId.StartsWith("owned.", StringComparison.Ordinal) ? soulId[6..] : soulId;
        var species = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == speciesId);
        var ownership = species is null ? null : _souls.CanonicalDensity.GetOwnership(species.Id);
        if (species is null || ownership is null || !ownership.IsOwned || _souls.CanonicalOwnedSpecies(species.Id) is not { } soul) return new(false, Failure: SummonFailure.SoulNotFound);
        if (species.PowerTier > _banners.CanonicalBannerRank) return new(false, Failure: SummonFailure.BannerRankTooLow);
        var existing = _canonicalState.GetValueOrDefault(species.Id);
        if (existing?.Mode == V25SoulRuntimeMode.Summoned) return new(false, Failure: SummonFailure.AlreadySummoned);
        if (existing?.Mode == V25SoulRuntimeMode.Dispersed) return new(false, Failure: SummonFailure.Dispersed);
        if (_canonicalPossessionActive()) return new(false, Failure: SummonFailure.Possessed);
        if (!_canonicalPlayer.State.Alive || _canonicalPlayer.State.CurrentSpirit <= 0) return new(false, Failure: SummonFailure.InsufficientSpirit);
        var placement = _canonicalPlayer.FindNearestFree(requestedPosition, 18, 48);
        if (placement is null) return new(false, Failure: SummonFailure.NoSpawnSpace);
        var ally = _allies.SpawnSoul(soul, _banners.Starter()!, placement.Value);
        var state = new V25SummonStateSnapshot(species.Id, V25SoulRuntimeMode.Summoned, ally.Uid, V25FixedPoint.DensitySyncScale, 0, 1, ally.AttackCooldown);
        _canonicalState[species.Id] = state;
        _summonBySoul[soul.Id] = ally.Uid; _soulBySummon[ally.Uid] = soul.Id;
        _events.Publish(new SoulSummonedEvent(soul.Id, ally.Uid, _banners.Starter()?.Id ?? "canonical.banner", placement.Value));
        return new(true, ally.Uid);
    }

    public bool UnsummonSoul(string soulId)
    {
        if (_souls.CanonicalMode) return RecallCanonical(soulId);
        if (!_summonBySoul.Remove(soulId, out var uid)) return false;
        _soulBySummon.Remove(uid); _bannerBySoul.Remove(soulId); _allies.Remove(uid); _events.Publish(new SoulUnsummonedEvent(soulId, uid)); return true;
    }

    /// <summary>Summons owned species in sorted SpeciesId order; each result is independent.</summary>
    public CanonicalSummonAllResult SummonAllCanonical(Vec2 center)
    {
        if (!_souls.CanonicalMode) return new(new Dictionary<string, SummonResult>(StringComparer.Ordinal));
        var results = new Dictionary<string, SummonResult>(StringComparer.Ordinal);
        var owned = _souls.CanonicalDensity?.Ownership.Values.Where(item => item.IsOwned).OrderBy(item => item.SpeciesId, StringComparer.Ordinal).ToArray() ?? Array.Empty<V25SpeciesOwnershipSnapshot>();
        for (var index = 0; index < owned.Length; index++)
        {
            var angle = Math.PI / 2 - (index % 16) * 2 * Math.PI / 16;
            var radius = 36 + (index / 6) * 24;
            var position = new Vec2(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            var soul = _souls.CanonicalOwnedSpecies(owned[index].SpeciesId);
            if (soul is not null) results.Add(owned[index].SpeciesId, Summon(soul.Id, "canonical.banner", position));
        }
        return new(results);
    }

    private bool RecallCanonical(string soulId)
    {
        var speciesId = soulId.StartsWith("owned.", StringComparison.Ordinal) ? soulId[6..] : soulId;
        if (!_canonicalState.TryGetValue(speciesId, out var state) || state.Mode != V25SoulRuntimeMode.Summoned || state.ActiveAllyUid is null) return false;
        var ally = _allies.Get(state.ActiveAllyUid);
        var soul = _souls.CanonicalOwnedSpecies(speciesId);
        if (ally is null || soul is null) return false;
        var ratio = ally.MaxHp > 0 ? Math.Clamp(ally.CurrentHp / ally.MaxHp, 0, 1) : state.HpRatio;
        var next = state with { Mode = V25SoulRuntimeMode.Ready, ActiveAllyUid = null, HpRatio = ratio, AttackCooldown = Math.Max(0, ally.AttackCooldown), RecoveryTicks = 0 };
        _canonicalState[speciesId] = next;
        _summonBySoul.Remove(soul.Id); _soulBySummon.Remove(ally.Uid); _allies.Remove(ally.Uid);
        _events.Publish(new SoulUnsummonedEvent(soul.Id, ally.Uid));
        return true;
    }

    public void Update(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        foreach (var entry in _recovery.ToArray()) { var next = System.Math.Max(0, entry.Value - deltaSeconds); if (next == 0) { _recovery.Remove(entry.Key); _recoveryDuration.Remove(entry.Key); _events.Publish(new SoulRecoveredEvent(entry.Key)); } else _recovery[entry.Key] = next; }
        if (_souls.CanonicalMode)
        {
            var ticks = Math.Max(1, (int)Math.Round(deltaSeconds * 60, MidpointRounding.AwayFromZero));
            foreach (var pair in _canonicalState.ToArray())
            {
                if (pair.Value.Mode == V25SoulRuntimeMode.Dispersed)
                {
                    var remaining = Math.Max(0, pair.Value.RecoveryTicks - ticks);
                    _canonicalState[pair.Key] = remaining == 0 ? pair.Value with { Mode = V25SoulRuntimeMode.Ready, RecoveryTicks = 0, VitalityMicro = V25FixedPoint.RoundMicro(_canonical?.Balance.Summon.RecoveredVitality ?? 0.25) } : pair.Value with { RecoveryTicks = remaining };
                }
                else if (pair.Value.Mode == V25SoulRuntimeMode.Ready && _canonicalAtShrine96 is not null && _canonicalCombatOrHazard is not null && _canonicalAtShrine96() && !_canonicalCombatOrHazard() && pair.Value.VitalityMicro < V25FixedPoint.DensitySyncScale)
                {
                    // Fixed-point vitality is the persisted authority. At 60 Hz, round the
                    // authored 0.05/s increment once per tick; the cap prevents drift.
                    var recovered = V25FixedPoint.RoundMicro(0.05 * deltaSeconds);
                    _canonicalState[pair.Key] = pair.Value with { VitalityMicro = Math.Min(V25FixedPoint.DensitySyncScale, pair.Value.VitalityMicro + recovered) };
                }
            }
        }
    }

    public SoulRuntimeView Runtime(string soulId)
    {
        if (_souls.CanonicalMode)
        {
            var speciesId = soulId.StartsWith("owned.", StringComparison.Ordinal) ? soulId[6..] : soulId;
            if (_canonicalState.TryGetValue(speciesId, out var canonical))
                return new(soulId, canonical.Mode switch { V25SoulRuntimeMode.Summoned => SoulRuntimeStatus.Summoned, V25SoulRuntimeMode.Dispersed => SoulRuntimeStatus.Dispersed, V25SoulRuntimeMode.Possessed => SoulRuntimeStatus.Possessed, _ => SoulRuntimeStatus.Ready }, canonical.ActiveAllyUid, canonical.RecoveryTicks / 60.0, V25FixedPoint.FromMicro(canonical.VitalityMicro));
        }
        if (_possessed.Contains(soulId)) return new(soulId, SoulRuntimeStatus.Possessed, null, 0, 1);
        if (_summonBySoul.TryGetValue(soulId, out var uid)) return new(soulId, SoulRuntimeStatus.Summoned, uid, 0, 1);
        var remaining = _recovery.GetValueOrDefault(soulId); var duration = _recoveryDuration.GetValueOrDefault(soulId, _definitions.Soul.Summon.StabilityRecoverySeconds);
        return new(soulId, remaining > 0 ? SoulRuntimeStatus.Dispersed : SoulRuntimeStatus.Ready, null, remaining, remaining > 0 && duration > 0 ? System.Math.Clamp(1 - remaining / duration, 0, 1) : 1);
    }

    public int ActiveCount => _summonBySoul.Count;
    public void RestoreCanonicalActiveLink(string soulId, string allyUid, string? bannerId = null)
    {
        if (string.IsNullOrWhiteSpace(soulId) || string.IsNullOrWhiteSpace(allyUid)) throw new InvalidDataException("Canonical summon link identity is missing.");
        if (_summonBySoul.ContainsKey(soulId) || _soulBySummon.ContainsKey(allyUid)) throw new InvalidDataException("Canonical summon link is duplicated.");
        _summonBySoul[soulId] = allyUid; _soulBySummon[allyUid] = soulId;
        if (!string.IsNullOrWhiteSpace(bannerId)) _bannerBySoul[soulId] = bannerId;
    }

    public void RestoreCanonicalState(IEnumerable<V25SummonStateSnapshot> snapshots)
    {
        if (_souls.CanonicalMode is false || _canonical is null) throw new InvalidOperationException("Canonical summon is not enabled.");
        ArgumentNullException.ThrowIfNull(snapshots);
        var owned = _souls.CanonicalDensity?.Ownership.Values.Where(item => item.IsOwned).Select(item => item.SpeciesId).ToHashSet(StringComparer.Ordinal) ?? [];
        var staged = snapshots.ToArray();
        if (staged.Select(item => item.SpeciesId).Distinct(StringComparer.Ordinal).Count() != staged.Length || !owned.SetEquals(staged.Select(item => item.SpeciesId)))
            throw new InvalidDataException("Canonical summon snapshot must cover each owned species exactly once.");
        var activeUids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in staged)
        {
            if (state is null || state.RecoveryTicks < 0 || !double.IsFinite(state.HpRatio) || state.HpRatio is < 0 or > 1 || !double.IsFinite(state.AttackCooldown) || state.AttackCooldown < 0 || state.VitalityMicro is < 0 or > V25FixedPoint.DensitySyncScale)
                throw new InvalidDataException("Canonical summon state is invalid.");
            _ = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == state.SpeciesId) ?? throw new InvalidDataException($"Unknown summon species '{state.SpeciesId}'.");
            if (state.Mode == V25SoulRuntimeMode.Summoned)
            {
                if (state.ActiveAllyUid is null || !activeUids.Add(state.ActiveAllyUid) || _allies.Get(state.ActiveAllyUid) is not { } ally || ally.SpeciesId != state.SpeciesId)
                    throw new InvalidDataException($"Summoned species '{state.SpeciesId}' has no matching ally runtime.");
                if (state.RecoveryTicks != 0 || state.VitalityMicro <= 0) throw new InvalidDataException("Summoned species recovery/vitality is inconsistent.");
            }
            else if (state.ActiveAllyUid is not null) throw new InvalidDataException("Only Summoned species may link an Ally runtime.");
            if (state.Mode == V25SoulRuntimeMode.Dispersed && (state.RecoveryTicks <= 0 || state.VitalityMicro != 0)) throw new InvalidDataException("Dispersed species state is inconsistent.");
            if (state.Mode == V25SoulRuntimeMode.Ready && state.RecoveryTicks != 0 || state.Mode == V25SoulRuntimeMode.Ready && state.VitalityMicro <= 0) throw new InvalidDataException("Ready species state is inconsistent.");
        }
        _canonicalState.Clear(); foreach (var state in staged) _canonicalState.Add(state.SpeciesId, state);
        _summonBySoul.Clear(); _soulBySummon.Clear();
        foreach (var state in staged.Where(item => item.Mode == V25SoulRuntimeMode.Summoned))
        {
            var soul = _souls.CanonicalOwnedSpecies(state.SpeciesId) ?? throw new InvalidDataException("Canonical summon link has no owned Soul.");
            _summonBySoul.Add(soul.Id, state.ActiveAllyUid!); _soulBySummon.Add(state.ActiveAllyUid!, soul.Id);
        }
    }
    public void ClearActiveForMapChange()
    {
        foreach (var uid in _soulBySummon.Keys.ToArray()) _allies.Remove(uid);
        _summonBySoul.Clear(); _soulBySummon.Clear(); _bannerBySoul.Clear();
    }
    public IReadOnlyList<DispersedSoulSaveData> DispersedSnapshot() => _recovery.Select(entry => new DispersedSoulSaveData(entry.Key, entry.Value, _recoveryDuration.GetValueOrDefault(entry.Key, _definitions.Soul.Summon.StabilityRecoverySeconds))).ToArray();
    public void RestoreDispersed(IEnumerable<DispersedSoulSaveData>? snapshot)
    {
        foreach (var uid in _soulBySummon.Keys.ToArray()) _allies.Remove(uid);
        _summonBySoul.Clear(); _soulBySummon.Clear(); _bannerBySoul.Clear(); _recovery.Clear(); _recoveryDuration.Clear(); _possessed.Clear();
        if (snapshot is null) return;
        foreach (var entry in snapshot)
        {
            if (_souls.OwnedSoul(entry.SoulId) is null || !double.IsFinite(entry.RecoverySeconds)) continue;
            var duration = entry.RecoveryDurationSeconds is { } configured && double.IsFinite(configured) ? System.Math.Max(0, configured) : _definitions.Soul.Summon.StabilityRecoverySeconds;
            var remaining = System.Math.Min(duration, System.Math.Max(0, entry.RecoverySeconds)); if (remaining <= 0) continue;
            _recovery[entry.SoulId] = remaining; _recoveryDuration[entry.SoulId] = System.Math.Max(remaining, duration);
        }
    }
    public bool BeginPossession(string soulId)
    {
        if (!_souls.CanonicalMode)
        {
            if (_souls.OwnedSoul(soulId) is null || Runtime(soulId).Status != SoulRuntimeStatus.Ready) return false;
            return _possessed.Add(soulId);
        }
        var speciesId = soulId.StartsWith("owned.", StringComparison.Ordinal) ? soulId[6..] : soulId;
        if (_souls.CanonicalOwnedSpecies(speciesId) is null || _canonicalState.GetValueOrDefault(speciesId) is { Mode: V25SoulRuntimeMode.Summoned or V25SoulRuntimeMode.Dispersed or V25SoulRuntimeMode.Possessed }) return false;
        var state = _canonicalState.GetValueOrDefault(speciesId) ?? new V25SummonStateSnapshot(speciesId, V25SoulRuntimeMode.Ready, null, V25FixedPoint.DensitySyncScale, 0, 1, 0);
        if (state.VitalityMicro <= 0) return false;
        _canonicalState[speciesId] = state with { Mode = V25SoulRuntimeMode.Possessed, ActiveAllyUid = null, RecoveryTicks = 0 };
        return true;
    }
    public void EndPossession(string soulId, double cooldownSeconds)
    {
        if (_souls.CanonicalMode)
        {
            var speciesId = soulId.StartsWith("owned.", StringComparison.Ordinal) ? soulId[6..] : soulId;
            if (_canonicalState.GetValueOrDefault(speciesId) is { Mode: V25SoulRuntimeMode.Possessed } state)
                _canonicalState[speciesId] = state with { Mode = V25SoulRuntimeMode.Ready, RecoveryTicks = 0 };
            return;
        }
        if (!_possessed.Remove(soulId)) return;
        if (cooldownSeconds > 0) { _recovery[soulId] = cooldownSeconds; _recoveryDuration[soulId] = cooldownSeconds; _events.Publish(new SoulDispersedEvent(soulId, cooldownSeconds)); }
    }
    private void OnAllyDefeated(AllyDefeatedEvent defeated)
    {
        if (!_soulBySummon.Remove(defeated.Uid, out var soulId)) return;
        _summonBySoul.Remove(soulId); _bannerBySoul.Remove(soulId);
        if (_souls.CanonicalMode)
        {
            var speciesId = _souls.OwnedSoul(soulId)?.Origin.SpeciesId;
            if (speciesId is not null)
            {
                var ticks = defeated.RecoveryMs is > 0 ? V25CombatRules.MillisecondsToTicksCeil(defeated.RecoveryMs.Value) : V25CombatRules.MillisecondsToTicksCeil(_canonical?.Balance.Summon.RecoveryMs ?? 20000);
                _canonicalState[speciesId] = new V25SummonStateSnapshot(speciesId, V25SoulRuntimeMode.Dispersed, null, 0, ticks, 0, 0);
            }
        }
        else
        {
            var duration = defeated.RecoveryMs is > 0 ? defeated.RecoveryMs.Value / 1000.0 : _definitions.Soul.Summon.StabilityRecoverySeconds; _recovery[soulId] = duration; _recoveryDuration[soulId] = duration;
        }
        _events.Publish(new SoulDispersedEvent(soulId, defeated.RecoveryMs is > 0 ? defeated.RecoveryMs.Value / 1000.0 : _definitions.Soul.Summon.StabilityRecoverySeconds));
    }
    private void OnAllyUnreachable(AllyUnreachableEvent unreachable)
    {
        if (_souls.CanonicalMode) _ = RecallCanonical(unreachable.SoulId);
    }

    public void Dispose() { _defeatSubscription.Dispose(); _unreachableSubscription.Dispose(); }
}
