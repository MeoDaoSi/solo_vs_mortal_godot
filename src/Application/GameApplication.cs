using SoloVsMortal.Simulation;

namespace SoloVsMortal.Application;

/// <summary>Command/query boundary exposed to Presentation.</summary>
public sealed class GameApplication
{
    private readonly GameSession _session;

    public GameApplication(GameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public void Start() => _session.Start();

    public void Tick(double deltaSeconds) => _session.Tick(deltaSeconds);

    public GameSnapshot Snapshot() => new(_session.State.Stage, _session.ElapsedSeconds);
}
