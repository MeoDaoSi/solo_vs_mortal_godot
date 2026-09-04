using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public sealed class EssenceSystem
{
    private readonly Dictionary<string, int> _points = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly SoulNatureDefinitions _definitions; private readonly PlayerModifierSystem _modifiers;
    public EssenceSystem(EventBus events, SoulNatureDefinitions definitions, PlayerModifierSystem modifiers) { _events = events; _definitions = definitions; _modifiers = modifiers; }
    public int Count(string profileId) => _points.GetValueOrDefault(profileId);
    public int RemainingCapacity(string profileId) { var profile = Profile(profileId); return System.Math.Max(0, Cap(profile) - Count(profileId)); }
    public IReadOnlyDictionary<string, int> Snapshot() => new Dictionary<string, int>(_points, StringComparer.Ordinal);
    public int Add(string profileId, double amount)
    {
        var profile = Profile(profileId); var gained = double.IsFinite(amount) ? System.Math.Max(0, (int)System.Math.Truncate(amount)) : 0;
        if (gained == 0) return Count(profileId);
        var previous = Count(profileId); var total = System.Math.Min(Cap(profile), previous + gained);
        if (total == previous) return previous;
        _points[profileId] = total; Refresh(); _events.Publish(new EssenceGainedEvent(profileId, profile.DisplayName, total - previous, total));
        foreach (var milestone in profile.Milestones.Where(item => previous < item.RequiredPoints && total >= item.RequiredPoints)) _events.Publish(new EssenceMilestoneReachedEvent(profileId, milestone.DisplayName, milestone.RequiredPoints));
        return total;
    }
    public void Restore(IReadOnlyDictionary<string, int>? points) { _points.Clear(); if (points is not null) foreach (var profile in _definitions.EssenceProfiles.Values) if (points.GetValueOrDefault(profile.Id) > 0) _points[profile.Id] = System.Math.Min(Cap(profile), points[profile.Id]); Refresh(); }
    private ModifierProfileDefinition Profile(string id) => _definitions.EssenceProfiles.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException($"Unknown Essence profile: {id}.");
    private static int Cap(ModifierProfileDefinition profile) => profile.Milestones[^1].RequiredPoints;
    private void Refresh() => _modifiers.SetSource(PlayerModifierSource.Essence, _definitions.EssenceProfiles.Values.Select(profile => profile.Milestones.LastOrDefault(item => Count(profile.Id) >= item.RequiredPoints)).Where(item => item is not null).Select(item => ModifierRules.FromDefinition(item!.Modifiers)).ToArray());
}
