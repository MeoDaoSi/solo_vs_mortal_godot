using SoloVsMortal.Core.Loop;
using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Simulation;

/// <summary>Headless owner and tick coordinator for a single gameplay session.</summary>
public sealed class GameSession : IDisposable
{
    private readonly SimulationClock _clock = new();
    private Vec2 _moveInput;
    private bool _attackPressed;

    public GameSession(GameDefinitions definitions, uint seed = 1)
    {
        Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        Events = new EventBus();
        var uids = new UidGenerator();
        var rng = new SeededRng(seed);
        Player = new PlayerSystem(Events, uids, definitions.Player, new Vec2(480, 280), new PlayerBounds(960, 540));
        Monsters = new MonsterSystem(Events, uids, rng, definitions, new SpawnArea(0, 0, 960, 540));
        Combat = new CombatSystem(Events, Player, Monsters);
    }

    public GameSessionState State { get; } = new();
    public GameDefinitions Definitions { get; }
    public EventBus Events { get; }
    public PlayerSystem Player { get; }
    public MonsterSystem Monsters { get; }
    public CombatSystem Combat { get; }

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
            Player.Update(deltaSeconds, _moveInput);
            Monsters.Update(deltaSeconds, Player.State);
            Combat.Update(deltaSeconds, _attackPressed);
            _clock.Tick(deltaSeconds);
        }
    }

    public void SetInput(Vec2 move, bool attackPressed) { _moveInput = move; _attackPressed = attackPressed; }
    public MonsterState SpawnMonster(string definitionId, int? level = null, Vec2? position = null) => Monsters.Spawn(definitionId, new MonsterSpawnOptions(Level: level, Position: position));
    public void Dispose() => Combat.Dispose();
}
