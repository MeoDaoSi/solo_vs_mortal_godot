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
        WorldMap = new WorldMapSystem(definitions);
        var map = WorldMap.CurrentMap; var playerSpawn = map.Spawn(WorldMap.CurrentRegion.DefaultSpawnId).Position;
        Player = new PlayerSystem(Events, uids, definitions.Player, playerSpawn, new PlayerBounds(map.Width, map.Height));
        Monsters = new MonsterSystem(Events, uids, rng, definitions, new SpawnArea(0, 0, map.Width, map.Height));
        Combat = new CombatSystem(Events, Player, Monsters);
        Souls = new SoulSystem(Events, uids, rng, definitions);
        SoulBanners = new SoulBannerSystem(Events, uids, definitions, Souls);
        SoulBanners.CreateStarter();
        Allies = new AllySystem(Events, uids, definitions);
        Summons = new SummonSystem(Events, definitions, Souls, SoulBanners, Allies);
        PlayerModifiers = new PlayerModifierSystem(Player);
        Progression = new ProgressionSystem(Events, rng, Player, PlayerModifiers);
        Essence = new EssenceSystem(Events, definitions.SoulNatures, PlayerModifiers);
        Bloodline = new BloodlineSystem(Events, definitions.SoulNatures, PlayerModifiers);
        Capabilities = new CapabilitySystem();
        Possession = new PossessionSystem(Events, definitions.SoulNatures, Souls, SoulBanners, Summons, PlayerModifiers, Capabilities);
        World = new WorldInteractionSystem(Events, map, Capabilities);
        Devouring = new DevourSystem(Events, definitions, Souls, SoulBanners, Summons, Essence, Bloodline, Progression.AddPlayerXp);
        Player.SetColliders(World.BlockingRects());
        _worldSubscription = Events.Subscribe<Simulation.Events.WorldObjectDestroyedEvent>(_ => Player.SetColliders(World.BlockingRects()));
    }

    public GameSessionState State { get; } = new();
    public GameDefinitions Definitions { get; }
    public WorldMapSystem WorldMap { get; }
    public EventBus Events { get; }
    public PlayerSystem Player { get; }
    public MonsterSystem Monsters { get; }
    public CombatSystem Combat { get; }
    public SoulSystem Souls { get; }
    public SoulBannerSystem SoulBanners { get; }
    public AllySystem Allies { get; }
    public SummonSystem Summons { get; }
    public PlayerModifierSystem PlayerModifiers { get; }
    public ProgressionSystem Progression { get; }
    public EssenceSystem Essence { get; }
    public BloodlineSystem Bloodline { get; }
    public CapabilitySystem Capabilities { get; }
    public PossessionSystem Possession { get; }
    public WorldInteractionSystem World { get; }
    public DevourSystem Devouring { get; }
    private readonly IDisposable _worldSubscription;

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
            Allies.Update(deltaSeconds, Monsters, Player.State.Position);
            Summons.Update(deltaSeconds);
            Progression.Update(deltaSeconds);
            Possession.Update(deltaSeconds);
            _clock.Tick(deltaSeconds);
        }
    }

    public void SetInput(Vec2 move, bool attackPressed) { _moveInput = move; _attackPressed = attackPressed; }
    public MonsterState SpawnMonster(string definitionId, int? level = null, Vec2? position = null) => Monsters.Spawn(definitionId, new MonsterSpawnOptions(Level: level, Position: position));
    public RegionTravelResult TravelToRegion(string regionId)
    {
        var previousRegionId = WorldMap.CurrentRegionId;
        var result = WorldMap.TravelTo(regionId);
        if (!result.Success || result.EntrySpawn is null) return result;
        if (!string.Equals(previousRegionId, WorldMap.CurrentRegionId, StringComparison.Ordinal))
        {
            World.SetMap(WorldMap.CurrentMap);
            Monsters.Clear();
            Monsters.SetSpawnArea(new SpawnArea(0, 0, WorldMap.CurrentMap.Width, WorldMap.CurrentMap.Height));
            Summons.ClearActiveForMapChange();
            Player.SetBounds(new PlayerBounds(WorldMap.CurrentMap.Width, WorldMap.CurrentMap.Height));
            Player.SetColliders(World.BlockingRects());
        }
        Player.SetPosition(result.EntrySpawn.Position);
        return result;
    }
    public void Dispose() { _worldSubscription.Dispose(); Summons.Dispose(); Progression.Dispose(); Souls.Dispose(); Combat.Dispose(); }
}
