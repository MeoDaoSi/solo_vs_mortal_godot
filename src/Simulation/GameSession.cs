using SoloVsMortal.Core.Loop;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation;

/// <summary>Headless owner and tick coordinator for a single gameplay session.</summary>
public sealed class GameSession
{
    private readonly SimulationClock _clock = new();

    public GameSessionState State { get; } = new();

    public double ElapsedSeconds => _clock.Time;

    public void Start()
    {
        State.TransitionTo(GameStage.Loading);
        State.TransitionTo(GameStage.Playing);
    }

    public void Tick(double deltaSeconds)
    {
        if (State.Stage == GameStage.Playing)
        {
            _clock.Tick(deltaSeconds);
        }
    }
}
