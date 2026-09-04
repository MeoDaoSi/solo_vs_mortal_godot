using SoloVsMortal.Simulation;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Application;

/// <summary>Command/query boundary exposed to Presentation.</summary>
public sealed class GameApplication
{
    private readonly GameSession _session;

    public GameApplication(GameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public static GameApplication CreateFromDefinitionsDirectory(string directory) =>
        new(new GameSession(GameDefinitionLoader.LoadFromDirectory(directory)));

    public void Start() => _session.Start();

    public void Tick(double deltaSeconds) => _session.Tick(deltaSeconds);
    public void SetInput(Vec2 move, bool attackPressed) => _session.SetInput(move, attackPressed);
    public string SpawnMonster(string definitionId, int? level = null, Vec2? position = null) => _session.SpawnMonster(definitionId, level, position).Uid;
    public void AddPlayerXp(double amount) => _session.Progression.AddPlayerXp(amount);
    public bool AttemptPlayerBreakthrough(PillId? pillId = null) => _session.Progression.AttemptPlayerBreakthrough(pillId);
    public bool Craft(PillId pillId, int amount = 1) => _session.Progression.Craft(pillId, amount);
    public bool UsePlayerStatPill(PillId pillId) => _session.Progression.UsePlayerStatPill(pillId);

    public GameSnapshot Snapshot() => new(
        _session.State.Stage,
        _session.ElapsedSeconds,
        _session.Definitions.Monsters.Count(),
        _session.Definitions.SoulBanners.Count(),
        _session.Definitions.SoulNatures.Natures.Count,
        _session.Definitions.SoulNatures.Capabilities.Count,
        new PlayerSnapshot(_session.Player.State.Uid, _session.Player.State.Position, _session.Player.State.CurrentHp, _session.Player.State.MaxHp, _session.Player.State.Alive, _session.Player.State.Level, _session.Player.State.Xp, _session.Player.State.Rank, _session.Player.State.Stats.Atk, _session.Player.State.Stats.Def, _session.Player.State.Stats.Speed),
        _session.Monsters.AliveMonsters().Select(monster => new MonsterSnapshot(monster.Uid, monster.DefinitionId, monster.SpeciesId, monster.Position, monster.CurrentHp, monster.MaxHp, monster.Alive, monster.AiState, monster.Level, monster.Rank)).ToArray(),
        _session.Progression.InventorySnapshot().Select(item => new InventoryItemSnapshot(item.StableId, item.Count)).ToArray());
}
