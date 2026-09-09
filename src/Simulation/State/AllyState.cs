using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.State;

public enum AllyAiMode { Guard, Assault }
public enum AllyAiState { Idle, Follow, Acquire, Approach, Telegraph, Chase, Attack, Recover, Return, Dead }

public sealed class AllyState
{
    internal AllyState(string uid, string definitionId, string speciesId, string displayName, string sourceSoulId, int rank, int level, StatBlock stats, AiDefinition ai, Vec2 position,
        int powerTier = 1, string archetype = "Balanced", string combatStyleId = "fist", string? signatureSkillId = null)
    { Uid = uid; DefinitionId = definitionId; SpeciesId = speciesId; DisplayName = displayName; SourceSoulId = sourceSoulId; Rank = rank; Level = level; Stats = stats; Ai = ai; Position = position; MaxHp = stats.Hp; CurrentHp = MaxHp; PowerTier = powerTier; Archetype = archetype; CombatStyleId = combatStyleId; SignatureSkillId = signatureSkillId; }
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
    public int PowerTier { get; }
    public string Archetype { get; }
    public string CombatStyleId { get; }
    public string? SignatureSkillId { get; }
    public AiDefinition Ai { get; }
    public double MaxHp { get; internal set; }
    public double CurrentHp { get; internal set; }
    public V25ShieldStore Shields { get; } = new();
    public double Shield { get => Shields.Total(Uid); internal set => Shields.SetLegacy(Uid, value); }
    public V25StatusStore Statuses { get; } = new();
    public string TargetLifeUid => Uid;
    public double Vitality { get; internal set; } = 1;
    public int RecoveryTicks { get; internal set; }
    public Vec2 Position { get; internal set; }
    public Vec2 HomeOffset { get; internal set; }
    public string? TargetUid { get; internal set; }
    public AllyAiMode AiMode { get; internal set; } = AllyAiMode.Guard;
    public int ThinkTicks { get; internal set; }
    public string? FocusTargetUid { get; internal set; }
    public int FocusRemainingTicks { get; internal set; }
    public int PathFailTicks { get; internal set; }
    public long RecentAttackerTick { get; internal set; } = -1;
    public string? RecentAttackerUid { get; internal set; }
    public AllyAiState AiState { get; internal set; }
    public double AttackCooldown { get; internal set; }
    public bool Alive => CurrentHp > 0 && AiState != AllyAiState.Dead;
}
