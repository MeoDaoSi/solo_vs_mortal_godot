using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Events;

public enum CombatantKind { Player, Monster, Soul, Ally }
public enum Faction { Player, Ally, Enemy }
public sealed record CombatAttackEvent(string AttackerUid, CombatantKind AttackerKind, string TargetUid, CombatantKind TargetKind, double Amount);
public sealed record CanonicalDamageResolvedEvent(V25HitKey HitKey, string AttackerUid, string TargetUid, double RawDamage, double IncomingDamage, double ShieldAbsorbed, double HpLoss, bool Critical, bool Rejected, string? SkillId = null);
/// <summary>Simulation effect result used by long lived mastery credit; emitted at release, never by animation.</summary>
public sealed record CanonicalEffectResolvedEvent(
    string SkillId,
    string CastId,
    string SourceUid,
    string TargetUid,
    string EffectId,
    double Amount,
    bool Meaningful,
    long Tick);
public sealed record PlayerDamagedEvent(double Amount, double Hp, double MaxHp);
public sealed record PlayerDefeatedEvent(double Hp);
public sealed record MonsterSpawnedEvent(string Uid, string DefinitionId, string SpeciesId, int Rank, string RankKey, string RankDisplayName, int Level, Vec2 Position);
public sealed record MonsterDamagedEvent(string Uid, double Amount, double Hp, double MaxHp);
public sealed record MonsterDefeatedEvent(
    string Uid,
    string DefinitionId,
    string SpeciesId,
    int Rank,
    string RankKey,
    string RankDisplayName,
    int Level,
    Vec2 Position,
    string? SourceUid,
    string? EncounterId = null,
    string? TargetLifeUid = null,
    bool RewardEligible = true,
    V25EncounterType EncounterType = V25EncounterType.Normal);
