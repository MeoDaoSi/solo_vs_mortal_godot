using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public enum PlayerModifierSource { TemporaryPills, Essence, Bloodline, Possession, Equipment, PassiveSkills, DebugTestBoost }

/// <summary>Owns independent modifier-source contributions; PlayerSystem owns the resulting stats.</summary>
public sealed class PlayerModifierSystem
{
    private readonly Dictionary<PlayerModifierSource, IReadOnlyList<StatModifiers>> _sources = [];
    private readonly PlayerSystem _player;

    public PlayerModifierSystem(PlayerSystem player) => _player = player ?? throw new ArgumentNullException(nameof(player));

    public void SetSource(PlayerModifierSource source, IReadOnlyList<StatModifiers> modifiers)
    {
        ArgumentNullException.ThrowIfNull(modifiers);
        if (modifiers.Count == 0) _sources.Remove(source);
        else _sources[source] = modifiers.ToArray();
        Recompute();
    }

    public IReadOnlyList<StatModifiers> Snapshot(PlayerModifierSource source) =>
        _sources.TryGetValue(source, out var values) ? values.ToArray() : Array.Empty<StatModifiers>();

    public IReadOnlyDictionary<PlayerModifierSource, IReadOnlyList<StatModifiers>> SnapshotAll() =>
        _sources.OrderBy(entry => entry.Key).ToDictionary(entry => entry.Key, entry => (IReadOnlyList<StatModifiers>)entry.Value.ToArray());

    public void Recompute()
    {
        var modifiers = _sources.OrderBy(entry => entry.Key).SelectMany(entry => entry.Value).ToArray();
        if (_player.CanonicalMode) _player.SetCanonicalModifiers(modifiers);
        else _player.RecomputeStats(modifiers);
    }
}
