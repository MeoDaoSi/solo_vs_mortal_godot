using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems;

public enum SoulRuntimeStatus { Ready, Summoned, Dispersed, Possessed }
public sealed record SoulRuntimeView(string SoulId, SoulRuntimeStatus Status, string? SummonUid, double RecoverySeconds, double Stability);
public sealed record DispersedSoulSaveData(string SoulId, double RecoverySeconds, double? RecoveryDurationSeconds = null);
public enum SummonFailure { BannerNotFound, SoulNotBound, AlreadySummoned, Dispersed, ActiveLimitReached, SoulNotFound, Possessed }
public sealed record SummonResult(bool Success, string? SummonUid = null, SummonFailure? Failure = null);

public sealed class SummonSystem : IDisposable
{
    private readonly Dictionary<string, string> _summonBySoul = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _soulBySummon = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bannerBySoul = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _recovery = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _recoveryDuration = new(StringComparer.Ordinal);
    private readonly HashSet<string> _possessed = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly GameDefinitions _definitions; private readonly SoulSystem _souls; private readonly SoulBannerSystem _banners; private readonly AllySystem _allies; private readonly IDisposable _defeatSubscription;
    public SummonSystem(EventBus events, GameDefinitions definitions, SoulSystem souls, SoulBannerSystem banners, AllySystem allies) { _events = events; _definitions = definitions; _souls = souls; _banners = banners; _allies = allies; _defeatSubscription = events.Subscribe<AllyDefeatedEvent>(OnAllyDefeated); }

    public SummonResult Summon(string soulId, string bannerId, Vec2 position)
    {
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

    public bool UnsummonSoul(string soulId)
    {
        if (!_summonBySoul.Remove(soulId, out var uid)) return false;
        _soulBySummon.Remove(uid); _bannerBySoul.Remove(soulId); _allies.Remove(uid); _events.Publish(new SoulUnsummonedEvent(soulId, uid)); return true;
    }

    public void Update(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        foreach (var entry in _recovery.ToArray()) { var next = System.Math.Max(0, entry.Value - deltaSeconds); if (next == 0) { _recovery.Remove(entry.Key); _recoveryDuration.Remove(entry.Key); _events.Publish(new SoulRecoveredEvent(entry.Key)); } else _recovery[entry.Key] = next; }
    }

    public SoulRuntimeView Runtime(string soulId)
    {
        if (_possessed.Contains(soulId)) return new(soulId, SoulRuntimeStatus.Possessed, null, 0, 1);
        if (_summonBySoul.TryGetValue(soulId, out var uid)) return new(soulId, SoulRuntimeStatus.Summoned, uid, 0, 1);
        var remaining = _recovery.GetValueOrDefault(soulId); var duration = _recoveryDuration.GetValueOrDefault(soulId, _definitions.Soul.Summon.StabilityRecoverySeconds);
        return new(soulId, remaining > 0 ? SoulRuntimeStatus.Dispersed : SoulRuntimeStatus.Ready, null, remaining, remaining > 0 && duration > 0 ? System.Math.Clamp(1 - remaining / duration, 0, 1) : 1);
    }

    public int ActiveCount => _summonBySoul.Count;
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
    public bool BeginPossession(string soulId) { if (_souls.OwnedSoul(soulId) is null || Runtime(soulId).Status != SoulRuntimeStatus.Ready) return false; return _possessed.Add(soulId); }
    public void EndPossession(string soulId, double cooldownSeconds) { if (!_possessed.Remove(soulId)) return; if (cooldownSeconds > 0) { _recovery[soulId] = cooldownSeconds; _recoveryDuration[soulId] = cooldownSeconds; _events.Publish(new SoulDispersedEvent(soulId, cooldownSeconds)); } }
    private void OnAllyDefeated(AllyDefeatedEvent defeated) { if (!_soulBySummon.Remove(defeated.Uid, out var soulId)) return; _summonBySoul.Remove(soulId); _bannerBySoul.Remove(soulId); var duration = _definitions.Soul.Summon.StabilityRecoverySeconds; _recovery[soulId] = duration; _recoveryDuration[soulId] = duration; _events.Publish(new SoulDispersedEvent(soulId, duration)); }
    public void Dispose() => _defeatSubscription.Dispose();
}
