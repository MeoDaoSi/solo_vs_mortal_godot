using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.State;

public enum AllyAiState { Idle, Chase, Attack, Return, Dead }

public sealed class AllyState
{
    internal AllyState(string uid, string definitionId, string speciesId, string displayName, string sourceSoulId, int rank, int level, StatBlock stats, AiDefinition ai, Vec2 position)
    { Uid = uid; DefinitionId = definitionId; SpeciesId = speciesId; DisplayName = displayName; SourceSoulId = sourceSoulId; Rank = rank; Level = level; Stats = stats; Ai = ai; Position = position; MaxHp = stats.Hp; CurrentHp = MaxHp; }
    public string Uid { get; }
    public string DefinitionId { get; }
    public string SpeciesId { get; }
    public string DisplayName { get; }
    public string SourceSoulId { get; }
    public CombatantKind Kind => CombatantKind.Soul;
    public Faction Faction => Faction.Player;
    public int Rank { get; }
    public int Level { get; }
    public StatBlock Stats { get; internal set; }
    public AiDefinition Ai { get; }
    public double MaxHp { get; internal set; }
    public double CurrentHp { get; internal set; }
    public Vec2 Position { get; internal set; }
    public Vec2 HomeOffset { get; internal set; }
    public string? TargetUid { get; internal set; }
    public AllyAiState AiState { get; internal set; }
    public double AttackCooldown { get; internal set; }
    public bool Alive => CurrentHp > 0 && AiState != AllyAiState.Dead;
}
