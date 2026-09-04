using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public enum PlayerModifierSource { TemporaryPills, Essence, Bloodline, Possession, DebugTestBoost }

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

    public void Recompute() => _player.RecomputeStats(_sources.Values.SelectMany(values => values).ToArray());
}
