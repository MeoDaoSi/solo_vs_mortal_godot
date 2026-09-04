using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.Events;

public enum CombatantKind { Player, Monster, Soul, Ally }
public enum Faction { Player, Ally, Enemy }
public sealed record CombatAttackEvent(string AttackerUid, CombatantKind AttackerKind, string TargetUid, CombatantKind TargetKind, double Amount);
public sealed record PlayerDamagedEvent(double Amount, double Hp, double MaxHp);
public sealed record PlayerDefeatedEvent(double Hp);
public sealed record MonsterSpawnedEvent(string Uid, string DefinitionId, string SpeciesId, int Rank, string RankKey, string RankDisplayName, int Level, Vec2 Position);
public sealed record MonsterDamagedEvent(string Uid, double Amount, double Hp, double MaxHp);
public sealed record MonsterDefeatedEvent(string Uid, string DefinitionId, string SpeciesId, int Rank, string RankKey, string RankDisplayName, int Level, Vec2 Position, string? SourceUid);
public sealed record PlayerXpGainedEvent(int Amount, int Level, int Xp, int XpToNext);
public sealed record PlayerLevelUpEvent(int Level, int Rank);
public sealed record BreakthroughResultEvent(bool Success, double Chance, int PreviousRank, int Rank, int Level, PillId? PillId);
public sealed record ItemGainedEvent(string StableItemId, int Amount, int Total);
public sealed record PillCraftedEvent(PillId PillId, int Amount);
public sealed record TemporaryPlayerBuffChangedEvent(PillId PillId, bool Active, double RemainingSeconds);
