using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.State.V25;
using SoloVsMortal.Data.Definitions.V25;

namespace SoloVsMortal.Simulation.Systems;

public enum BindSoulFailure { BannerNotFound, SoulNotOwned, SoulAlreadyBound, SlotLimitReached, CapacityReached }
public sealed record BindSoulResult(bool Success, BindSoulFailure? Failure = null, int? SlotIndex = null);
public enum UnbindSoulFailure { BannerNotFound, SoulNotBound, SoulActive }
public sealed record UnbindSoulResult(bool Success, UnbindSoulFailure? Failure = null);

public sealed class SoulBannerSystem
{
    private readonly Dictionary<string, SoulBannerState> _banners = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly GameDefinitions _definitions; private readonly SoulSystem _souls;
    private CanonicalContentRegistry? _canonical;
    private Func<int>? _playerRank;
    private Func<string>? _regionId;
    private Func<bool>? _atShrine;
    private Func<string, bool>? _hasFact;
    private readonly Dictionary<string, V25BannerGateReceipt> _canonicalReceipts = new(StringComparer.Ordinal);
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

    public int CanonicalBannerRank => _souls.CanonicalBannerRank;
    public IReadOnlyList<V25BannerGateReceipt> CanonicalUpgradeReceipts => _canonicalReceipts.Values.OrderBy(item => item.GateId, StringComparer.Ordinal).ToArray();

    public void ConfigureCanonical(CanonicalContentRegistry canonical, Func<int> playerRank, Func<string> regionId, Func<bool> atShrine, Func<string, bool> hasFact)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _playerRank = playerRank ?? throw new ArgumentNullException(nameof(playerRank));
        _regionId = regionId ?? throw new ArgumentNullException(nameof(regionId));
        _atShrine = atShrine ?? throw new ArgumentNullException(nameof(atShrine));
        _hasFact = hasFact ?? throw new ArgumentNullException(nameof(hasFact));
        _souls.SetCanonicalBannerRank(1);
    }

    /// <summary>Canonical rank gate; it never spends Density, currency, or Soul ownership.</summary>
    public V25BannerUpgradeResult UpgradeCanonical()
    {
        if (_canonical is null || _playerRank is null || _regionId is null || _atShrine is null || _hasFact is null)
            throw new InvalidOperationException("Canonical Banner is not configured.");
        var current = _souls.CanonicalBannerRank;
        var max = _canonical.Profile(_canonical.ActiveProfileId).MaxBannerRank;
        if (current >= max) return new(false, current, null, Failure: V25BannerUpgradeFailure.AtProfileCap);
        var next = checked(current + 1);
        if (_playerRank() < next) return new(false, current, next, Failure: V25BannerUpgradeFailure.PlayerRankTooLow);
        if (!_atShrine()) return new(false, current, next, Failure: V25BannerUpgradeFailure.NotAtShrine);
        var regionId = _regionId();
        var region = _canonical.Content.Regions.FirstOrDefault(item => item.Id == regionId);
        if (region is null) return new(false, current, next, Failure: V25BannerUpgradeFailure.InvalidRegion);
        var gateId = $"banner.r{current}.upgrade";
        var receiptId = $"banner.upgrade.r{current}";
        if (_canonicalReceipts.ContainsKey(receiptId) || _souls.CanonicalBannerRank >= next)
            return new(true, _souls.CanonicalBannerRank, null, gateId, receiptId, AlreadyApplied: true);
        if (!_hasFact($"{region.BossId}.clear")) return new(false, current, next, gateId, receiptId, V25BannerUpgradeFailure.BossProofMissing);
        var threshold = V25DensityMath.ToMicroPoints(Math.Min(100, 15 * current));
        var eligible = _souls.CanonicalDensity?.Ownership.Values.Any(state => state.IsOwned && state.PowerTier <= current && state.CommittedDensityMicro >= threshold) == true;
        if (!eligible) return new(false, current, next, gateId, receiptId, V25BannerUpgradeFailure.NoEligibleOwnedSpecies);
        var receipt = new V25BannerGateReceipt(gateId, _canonical.Content.ContentVersion, receiptId, current, next);
        // The gate has no partial mutation: rank, receipt, and event are committed as one logical
        // operation after every prerequisite has been evaluated.
        _souls.SetCanonicalBannerRank(next);
        _canonicalReceipts.Add(receiptId, receipt);
        _events.Publish(new V25BannerUpgradeCommittedEvent(gateId, receiptId, current, next));
        return new(true, next, null, gateId, receiptId);
    }

    public void RestoreCanonicalState(int rank, IEnumerable<V25BannerGateReceipt> receipts)
    {
        if (_canonical is null) throw new InvalidOperationException("Canonical Banner is not configured.");
        ArgumentNullException.ThrowIfNull(receipts);
        var staged = receipts.ToArray();
        if (rank < 1 || rank > _canonical.Profile(_canonical.ActiveProfileId).MaxBannerRank || staged.Select(item => item.ReceiptId).Distinct(StringComparer.Ordinal).Count() != staged.Length)
            throw new InvalidDataException("Canonical Banner restore is invalid.");
        var map = new Dictionary<string, V25BannerGateReceipt>(StringComparer.Ordinal);
        foreach (var receipt in staged)
        {
            if (receipt is null || receipt.FromRank < 1 || receipt.ToRank != receipt.FromRank + 1 || receipt.ToRank > rank || receipt.GateId != $"banner.r{receipt.FromRank}.upgrade" || receipt.ReceiptId != $"banner.upgrade.r{receipt.FromRank}" || receipt.GateVersion != _canonical.Content.ContentVersion)
                throw new InvalidDataException("Canonical Banner gate receipt identity/version is invalid.");
            map.Add(receipt.ReceiptId, receipt);
        }
        for (var from = 1; from < rank; from++) if (!map.ContainsKey($"banner.upgrade.r{from}")) throw new InvalidDataException("Canonical Banner restore is missing a prior upgrade receipt.");
        _souls.SetCanonicalBannerRank(rank);
        _canonicalReceipts.Clear(); foreach (var receipt in map) _canonicalReceipts.Add(receipt.Key, receipt.Value);
    }

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
