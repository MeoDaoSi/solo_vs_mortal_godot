using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public enum StartPossessionFailure { SoulNotFound, SoulNotBound, SoulNotReady, PossessionUnavailable, PossessionAlreadyActive }
public sealed record StartPossessionResult(bool Success, string? SoulId = null, string? ProfileId = null, StartPossessionFailure? Failure = null);
public sealed record PossessionSaveData(string SoulId, string ProfileId, double RemainingSeconds);

public sealed class PossessionSystem
{
    private PossessionSaveData? _active;
    private readonly EventBus _events; private readonly SoulNatureDefinitions _definitions; private readonly SoulSystem _souls; private readonly SoulBannerSystem _banners; private readonly SummonSystem _runtime; private readonly PlayerModifierSystem _modifiers; private readonly CapabilitySystem _capabilities;
    public PossessionSystem(EventBus events, SoulNatureDefinitions definitions, SoulSystem souls, SoulBannerSystem banners, SummonSystem runtime, PlayerModifierSystem modifiers, CapabilitySystem capabilities) { _events = events; _definitions = definitions; _souls = souls; _banners = banners; _runtime = runtime; _modifiers = modifiers; _capabilities = capabilities; }
    public string? ActiveSoulId => _active?.SoulId;
    public double RemainingSeconds => _active?.RemainingSeconds ?? 0;
    public PossessionSaveData? Snapshot() => _active;
    public StartPossessionResult Start(string soulId)
    {
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
        if (_active is null) return false; var active = _active; var profile = _definitions.PossessionProfiles[active.ProfileId]; _active = null; Clear(); _runtime.EndPossession(active.SoulId, profile.CooldownSeconds); _events.Publish(new PossessionEndedEvent(active.SoulId, active.ProfileId, profile.CooldownSeconds)); return true;
    }
    public void Update(double deltaSeconds) { if (_active is null || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return; _active = _active with { RemainingSeconds = System.Math.Max(0, _active.RemainingSeconds - deltaSeconds) }; if (_active.RemainingSeconds == 0) End(); }
    public void Restore(PossessionSaveData? data)
    {
        _active = null; Clear(); if (data is null || !double.IsFinite(data.RemainingSeconds) || data.RemainingSeconds <= 0) return;
        var soul = _souls.OwnedSoul(data.SoulId); var banner = _banners.Starter(); if (soul is null || banner is null || !_banners.IsBound(data.SoulId, banner.Id)) return;
        if (!_definitions.PossessionProfiles.TryGetValue(data.ProfileId, out var profile) || _definitions.Natures[soul.SoulNatureId].PossessionProfileId != data.ProfileId || !_runtime.BeginPossession(data.SoulId)) return;
        _active = data with { RemainingSeconds = System.Math.Min(profile.DurationSeconds, data.RemainingSeconds) }; Apply(profile);
    }
    private void Apply(PossessionProfileDefinition profile) { _modifiers.SetSource(PlayerModifierSource.Possession, [ModifierRules.FromDefinition(profile.Modifiers)]); _capabilities.SetSource(CapabilitySource.Possession, profile.CapabilityIds); }
    private void Clear() { _modifiers.SetSource(PlayerModifierSource.Possession, Array.Empty<StatModifiers>()); _capabilities.SetSource(CapabilitySource.Possession, Array.Empty<string>()); }
}
