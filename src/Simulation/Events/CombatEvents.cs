using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Simulation.Events;

public enum CombatantKind { Player, Monster, Soul, Ally }
public enum Faction { Player, Ally, Enemy }
public sealed record CombatAttackEvent(string AttackerUid, CombatantKind AttackerKind, string TargetUid, CombatantKind TargetKind, double Amount);
public sealed record PlayerDamagedEvent(double Amount, double Hp, double MaxHp);
public sealed record PlayerDefeatedEvent(double Hp);
public sealed record MonsterSpawnedEvent(string Uid, string DefinitionId, string SpeciesId, int Rank, string RankKey, string RankDisplayName, int Level, Vec2 Position);
public sealed record MonsterDamagedEvent(string Uid, double Amount, double Hp, double MaxHp);
public sealed record MonsterDefeatedEvent(string Uid, string DefinitionId, string SpeciesId, int Rank, string RankKey, string RankDisplayName, int Level, Vec2 Position, string? SourceUid);
