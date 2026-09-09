using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.State;

public enum MonsterAiState { Idle, Chase, Attack, Hit, Dying, Dead, Return }

public sealed class MonsterState
{
    internal MonsterState(string uid, string definitionId, string speciesId, int rank, int level, StatBlock stats, Vec2 position,
        int powerTier = 1, string archetype = "Balanced", string combatStyleId = "fist", string? signatureSkillId = null,
        V25EncounterType encounterType = V25EncounterType.Normal, bool rewardEligible = true)
    {
        Uid = uid; DefinitionId = definitionId; SpeciesId = speciesId; Rank = rank; Level = level; Stats = stats; Position = position; MaxHp = stats.Hp;
        HomePosition = position; CurrentHp = MaxHp; PowerTier = powerTier; Archetype = archetype; CombatStyleId = combatStyleId; SignatureSkillId = signatureSkillId;
        EncounterType = encounterType; RewardEligible = rewardEligible;
    }
    public string Uid { get; }
    public string DefinitionId { get; }
    public string SpeciesId { get; }
    public CombatantKind Kind => CombatantKind.Monster;
    public Faction Faction => Faction.Enemy;
    public int Rank { get; }
    public int Level { get; }
    public StatBlock Stats { get; }
    public int PowerTier { get; }
    public string Archetype { get; }
    public string CombatStyleId { get; }
    public string? SignatureSkillId { get; }
    public double MaxHp { get; }
    public double CurrentHp { get; internal set; }
    public V25ShieldStore Shields { get; } = new();
    public double Shield { get => Shields.Total(Uid); internal set => Shields.SetLegacy(Uid, value); }
    public V25EncounterType EncounterType { get; internal set; } = V25EncounterType.Normal;
    public string EncounterId { get; internal set; } = "";
    public bool RewardEligible { get; internal set; }
    /// <summary>Boss-only stagger meter and lock timers, all measured in canonical fixed ticks.</summary>
    public int StaggerPoints { get; internal set; }
    public int StaggerImmuneTicks { get; internal set; }
    public int StaggerRecoveryTicks { get; internal set; }
    public int BossPatternIndex { get; internal set; }
    public string? BossStoryInstanceId { get; internal set; }
    public string TargetLifeUid => Uid;
    public V25StatusStore Statuses { get; } = new();
    public Vec2 Position { get; internal set; }
    public Vec2 HomePosition { get; internal set; }
    public string? TargetUid { get; internal set; }
    public bool IsReturning { get; internal set; }
    public MonsterAiState AiState { get; internal set; }
    public double AttackCooldown { get; internal set; }
    public bool Alive => CurrentHp > 0 && AiState != MonsterAiState.Dead;
}
