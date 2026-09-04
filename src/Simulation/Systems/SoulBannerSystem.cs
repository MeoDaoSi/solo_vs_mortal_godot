using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems;

public enum BindSoulFailure { BannerNotFound, SoulNotOwned, SoulAlreadyBound, SlotLimitReached, CapacityReached }
public sealed record BindSoulResult(bool Success, BindSoulFailure? Failure = null, int? SlotIndex = null);
public enum UnbindSoulFailure { BannerNotFound, SoulNotBound }
public sealed record UnbindSoulResult(bool Success, UnbindSoulFailure? Failure = null);

public sealed class SoulBannerSystem
{
    private readonly Dictionary<string, SoulBannerState> _banners = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly GameDefinitions _definitions; private readonly SoulSystem _souls;
    public SoulBannerSystem(EventBus events, UidGenerator uids, GameDefinitions definitions, SoulSystem souls) { _events = events; _uids = uids; _definitions = definitions; _souls = souls; }

    public SoulBannerState CreateStarter() => Create(_definitions.StarterSoulBanner);
    public SoulBannerState Create(SoulBannerDefinition definition, int level = 1)
    {
        var safeLevel = System.Math.Clamp(level, 1, definition.MaximumLevel);
        var state = new SoulBannerState(_uids.Create("soulBanner"), definition.Id, definition.Tier, safeLevel, SoulBannerRules.Compute(definition, safeLevel));
        _banners.Add(state.Id, state); _events.Publish(new SoulBannerCreatedEvent(state.Id, state.Tier)); return state;
    }

    public SoulBannerState? Get(string id) => _banners.GetValueOrDefault(id);
    public SoulBannerState? Starter() => _banners.Values.FirstOrDefault();
    public IReadOnlyList<SoulBannerState> Banners() => _banners.Values.ToArray();

    public BindSoulResult Bind(string soulId, string bannerId)
    {
        if (!_banners.TryGetValue(bannerId, out var banner)) return new(false, BindSoulFailure.BannerNotFound);
        var cost = _souls.Cost(soulId); if (cost is null) return new(false, BindSoulFailure.SoulNotOwned);
        if (banner.BoundSoulIds.Contains(soulId)) return new(false, BindSoulFailure.SoulAlreadyBound);
        if (banner.BoundSoulIds.Count >= banner.Computed.SlotLimit) return new(false, BindSoulFailure.SlotLimitReached);
        if (UsedCapacity(banner) + cost > banner.Computed.CapacityLimit) return new(false, BindSoulFailure.CapacityReached);
        banner.MutableBoundSoulIds.Add(soulId); var slot = banner.BoundSoulIds.Count - 1;
        _events.Publish(new SoulBoundEvent(soulId, bannerId, banner.Tier, banner.Level, slot)); return new(true, SlotIndex: slot);
    }

    public UnbindSoulResult Unbind(string soulId, string bannerId)
    {
        if (!_banners.TryGetValue(bannerId, out var banner)) return new(false, UnbindSoulFailure.BannerNotFound);
        if (!banner.MutableBoundSoulIds.Remove(soulId)) return new(false, UnbindSoulFailure.SoulNotBound);
        _events.Publish(new SoulUnboundEvent(soulId, bannerId)); return new(true);
    }

    public int UsedCapacity(SoulBannerState banner) => banner.BoundSoulIds.Sum(soulId => _souls.Cost(soulId) ?? 0);
    public bool IsBound(string soulId, string bannerId) => _banners.TryGetValue(bannerId, out var banner) && banner.BoundSoulIds.Contains(soulId);
    public void Clear() => _banners.Clear();
    public void RestoreBindings(string bannerId, IEnumerable<string> soulIds)
    {
        if (!_banners.TryGetValue(bannerId, out var banner)) return; banner.MutableBoundSoulIds.Clear();
        foreach (var soulId in soulIds.Distinct(StringComparer.Ordinal))
        {
            var cost = _souls.Cost(soulId); if (cost is null) continue;
            if (banner.BoundSoulIds.Count >= banner.Computed.SlotLimit) break;
            if (UsedCapacity(banner) + cost > banner.Computed.CapacityLimit) continue;
            banner.MutableBoundSoulIds.Add(soulId);
        }
    }
}
