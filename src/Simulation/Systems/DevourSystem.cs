using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;

namespace SoloVsMortal.Simulation.Systems;

public enum DevourMode { CultivationXp, Essence, Bloodline }
public enum DevourFailure { SoulNotFound, SoulEquipped, SoulNotReady, ModeUnavailable }
public sealed record DevourPreview(DevourMode Mode, string DisplayName, double Reward, string ProfileId);
public sealed record DevourResult(bool Success, string? SoulId = null, DevourMode? Mode = null, double Reward = 0, string? ProfileId = null, DevourFailure? Failure = null);

/// <summary>Coordinates the irreversible Soul removal only after guards and reward resolution succeed.</summary>
public sealed class DevourSystem
{
    private readonly EventBus _events; private readonly GameDefinitions _definitions; private readonly SoulSystem _souls; private readonly SoulBannerSystem _banners; private readonly SummonSystem _summons; private readonly EssenceSystem _essence; private readonly BloodlineSystem _bloodline; private readonly Action<double> _addPlayerXp;
    public DevourSystem(EventBus events, GameDefinitions definitions, SoulSystem souls, SoulBannerSystem banners, SummonSystem summons, EssenceSystem essence, BloodlineSystem bloodline, Action<double> addPlayerXp) { _events = events; _definitions = definitions; _souls = souls; _banners = banners; _summons = summons; _essence = essence; _bloodline = bloodline; _addPlayerXp = addPlayerXp; }
    public IReadOnlyList<DevourPreview> Previews(string soulId)
    {
        var soul = _souls.OwnedSoul(soulId); if (soul is null) return [];
        var devour = _definitions.SoulNatures.Natures[soul.SoulNatureId].Devour; if (devour is null) return [];
        var result = new List<DevourPreview>();
        if (devour.XpProfileId is { } xpId)
        {
            var profile = _definitions.SoulNatures.DevourXpProfiles[xpId]; var reward = System.Math.Floor(profile.BaseXp + profile.XpPerSoulLevel * soul.Level + profile.XpPerOriginRank * soul.Origin.Rank + 0.5);
            result.Add(new(DevourMode.CultivationXp, "Tu Vi Hồn Tướng", System.Math.Max(1, reward), profile.Id));
        }
        if (devour.EssenceProfileId is { } essenceId && devour.EssenceContribution is { } essenceAmount)
        { var reward = System.Math.Min(essenceAmount, _essence.RemainingCapacity(essenceId)); if (reward > 0) result.Add(new(DevourMode.Essence, "Tinh Hoa", reward, essenceId)); }
        if (devour.BloodlineProfileId is { } bloodlineId && devour.BloodlineContribution is { } bloodlineAmount)
        { var reward = System.Math.Min(bloodlineAmount, _bloodline.RemainingCapacity(bloodlineId)); if (reward > 0) result.Add(new(DevourMode.Bloodline, "Huyết Mạch", reward, bloodlineId)); }
        return result;
    }
    public DevourResult Execute(string soulId, DevourMode mode)
    {
        var soul = _souls.OwnedSoul(soulId); if (soul is null) return new(false, Failure: DevourFailure.SoulNotFound);
        var starter = _banners.Starter(); if (starter is not null && _banners.IsBound(soulId, starter.Id)) return new(false, Failure: DevourFailure.SoulEquipped);
        if (_summons.Runtime(soulId).Status != SoulRuntimeStatus.Ready) return new(false, Failure: DevourFailure.SoulNotReady);
        var preview = Previews(soulId).FirstOrDefault(item => item.Mode == mode); if (preview is null) return new(false, Failure: DevourFailure.ModeUnavailable);
        if (!_souls.RemoveOwned(soulId)) return new(false, Failure: DevourFailure.SoulNotFound);
        if (mode == DevourMode.CultivationXp) _addPlayerXp(preview.Reward); else if (mode == DevourMode.Essence) _essence.Add(preview.ProfileId, preview.Reward); else _bloodline.Add(preview.ProfileId, preview.Reward);
        _events.Publish(new SoulDevouredEvent(soulId, mode.ToString(), preview.ProfileId, preview.Reward)); return new(true, soulId, mode, preview.Reward, preview.ProfileId);
    }
}
