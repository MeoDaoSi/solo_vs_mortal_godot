namespace SoloVsMortal.Core.Loop;

public sealed class SimulationClock
{
    private readonly List<ISimulationTickable> _systems = [];

    public double Time { get; private set; }

    public void Add(ISimulationTickable system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
    }

    public bool Remove(ISimulationTickable system) => _systems.Remove(system);

    public void Clear() => _systems.Clear();

    public void Tick(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return;
        }

        Time += deltaSeconds;
        foreach (var system in _systems)
        {
            system.Update(deltaSeconds);
        }
    }
}
