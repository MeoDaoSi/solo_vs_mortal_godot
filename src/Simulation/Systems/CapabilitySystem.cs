namespace SoloVsMortal.Simulation.Systems;

public enum CapabilitySource { Core, PermanentLearned, Equipment, Possession, Unique, ActorAI }
public sealed class CapabilitySystem
{
    private readonly Dictionary<CapabilitySource, Dictionary<string, IReadOnlySet<string>>> _sources = [];
    private IReadOnlySet<string>? _canonicalIds;

    /// <summary>Pins the capability vocabulary imported from the canonical bundle.</summary>
    public void ConfigureCanonical(IEnumerable<string> capabilityIds)
    {
        ArgumentNullException.ThrowIfNull(capabilityIds);
        var ids = capabilityIds.ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) throw new InvalidDataException("Canonical capability vocabulary cannot be empty.");
        if (_sources.Values.SelectMany(item => item.Values).SelectMany(item => item).Any(id => !ids.Contains(id)))
            throw new InvalidDataException("Existing capability source contains an ID outside the canonical vocabulary.");
        _canonicalIds = ids;
    }

    /// <summary>Compatibility bridge for the old single possession source.</summary>
    public void SetSource(CapabilitySource source, IReadOnlyList<string> capabilityIds) => SetSource(source, "legacy", capabilityIds);

    /// <summary>Replaces one source instance atomically; other grants remain intact.</summary>
    public void SetSource(CapabilitySource source, string sourceInstanceId, IReadOnlyList<string> capabilityIds)
    {
        if (string.IsNullOrWhiteSpace(sourceInstanceId)) throw new ArgumentException("Capability source instance is required.", nameof(sourceInstanceId));
        ArgumentNullException.ThrowIfNull(capabilityIds);
        var normalized = capabilityIds.ToHashSet(StringComparer.Ordinal);
        if (_canonicalIds is not null && normalized.Any(id => !_canonicalIds.Contains(id)))
            throw new InvalidDataException($"Capability source '{sourceInstanceId}' contains an unknown canonical capability.");
        if (!_sources.TryGetValue(source, out var instances)) _sources[source] = instances = new(StringComparer.Ordinal);
        if (normalized.Count == 0) instances.Remove(sourceInstanceId);
        else instances[sourceInstanceId] = normalized;
        if (instances.Count == 0) _sources.Remove(source);
    }

    public void RemoveSource(CapabilitySource source, string sourceInstanceId)
    {
        if (_sources.TryGetValue(source, out var instances))
        {
            instances.Remove(sourceInstanceId);
            if (instances.Count == 0) _sources.Remove(source);
        }
    }

    public bool Has(string capabilityId) => !string.IsNullOrWhiteSpace(capabilityId) && (_canonicalIds is null || _canonicalIds.Contains(capabilityId)) && _sources.Values.Any(instances => instances.Values.Any(source => source.Contains(capabilityId)));

    public bool IsCanonicalCapability(string capabilityId) => !string.IsNullOrWhiteSpace(capabilityId) && (_canonicalIds?.Contains(capabilityId) ?? false);

    public IReadOnlySet<string> Union() => _sources.Values.SelectMany(instances => instances.Values).SelectMany(source => source).ToHashSet(StringComparer.Ordinal);

    public IReadOnlyList<(CapabilitySource Source, string SourceInstanceId, IReadOnlySet<string> CapabilityIds)> Snapshot() =>
        _sources.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (pair.Key, item.Key, item.Value))).ToArray();
}
