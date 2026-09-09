namespace SoloVsMortal.Core.Loop;

/// <summary>
/// Fixed domain clock. Godot's fixed-physics callback is only the outer driver; gameplay systems advance
/// exclusively in these 60 Hz steps so a variable render delta cannot change cast, damage, or timer order.
/// </summary>
public sealed class SimulationClock
{
    public const int SimulationHz = 60;
    public const double FixedDeltaSeconds = 1.0 / SimulationHz;

    private readonly List<ISimulationTickable> _systems = [];
    private double _accumulator;

    public double Time { get; private set; }
    public long TickCount { get; private set; }
    public double AccumulatorSeconds => _accumulator;

    public void Restore(long tickCount)
    {
        if (tickCount < 0) throw new ArgumentOutOfRangeException(nameof(tickCount));
        TickCount = tickCount;
        Time = tickCount * FixedDeltaSeconds;
        _accumulator = 0;
    }

    public void Add(ISimulationTickable system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    public bool Remove(ISimulationTickable system) => _systems.Remove(system);

    public void Clear() => _systems.Clear();

    /// <summary>Advances by whole fixed ticks and returns the number of executed domain ticks.</summary>
    public int Advance(double deltaSeconds, Action<double>? fixedStep = null)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return 0;
        _accumulator += deltaSeconds;
        var steps = 0;
        while (_accumulator + 1e-12 >= FixedDeltaSeconds)
        {
            _accumulator -= FixedDeltaSeconds;
            Time += FixedDeltaSeconds;
            TickCount = checked(TickCount + 1);
            if (fixedStep is not null) fixedStep(FixedDeltaSeconds);
            else foreach (var system in _systems.ToArray()) system.Update(FixedDeltaSeconds);
            steps++;
        }
        return steps;
    }

    /// <summary>Compatibility entrypoint; it still executes fixed steps rather than variable-delta updates.</summary>
    public void Tick(double deltaSeconds) => Advance(deltaSeconds);
}
