using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.State;

public enum MonsterAiState { Idle, Chase, Attack, Hit, Dying, Dead }

public sealed class MonsterState
{
    internal MonsterState(string uid, string definitionId, string speciesId, int rank, int level, StatBlock stats, Vec2 position)
    {
        Uid = uid; DefinitionId = definitionId; SpeciesId = speciesId; Rank = rank; Level = level; Stats = stats; Position = position; MaxHp = stats.Hp; CurrentHp = MaxHp;
    }
    public string Uid { get; }
    public string DefinitionId { get; }
    public string SpeciesId { get; }
    public CombatantKind Kind => CombatantKind.Monster;
    public Faction Faction => Faction.Enemy;
    public int Rank { get; }
    public int Level { get; }
    public StatBlock Stats { get; }
    public double MaxHp { get; }
    public double CurrentHp { get; internal set; }
    public Vec2 Position { get; internal set; }
    public MonsterAiState AiState { get; internal set; }
    public double AttackCooldown { get; internal set; }
    public bool Alive => CurrentHp > 0 && AiState != MonsterAiState.Dead;
}
