namespace SoloVsMortal.Simulation.State;

public sealed class GameSessionState
{
    public GameStage Stage { get; private set; } = GameStage.Boot;

    public void TransitionTo(GameStage next)
    {
        if (!CanTransition(Stage, next))
        {
            throw new InvalidOperationException($"Invalid game-stage transition: {Stage} -> {next}.");
        }

        Stage = next;
    }

    private static bool CanTransition(GameStage current, GameStage next) => (current, next) switch
    {
        (GameStage.Boot, GameStage.Loading) => true,
        (GameStage.Loading, GameStage.Playing) => true,
        (GameStage.Playing, GameStage.Defeated) => true,
        (GameStage.Defeated, GameStage.Loading) => true,
        _ => false,
    };
}
