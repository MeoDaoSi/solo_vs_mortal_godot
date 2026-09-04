namespace SoloVsMortal.Simulation.Systems;

public enum CapabilitySource { Possession }
public sealed class CapabilitySystem
{
    private readonly Dictionary<CapabilitySource, IReadOnlySet<string>> _sources = [];
    public void SetSource(CapabilitySource source, IReadOnlyList<string> capabilityIds) { if (capabilityIds.Count == 0) _sources.Remove(source); else _sources[source] = capabilityIds.ToHashSet(StringComparer.Ordinal); }
    public bool Has(string capabilityId) => _sources.Values.Any(source => source.Contains(capabilityId));
}
