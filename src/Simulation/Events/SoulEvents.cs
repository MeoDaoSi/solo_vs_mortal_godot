using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.Events;

public sealed record SoulGeneratedEvent(string MonsterUid, string SoulId, string RankKey, Vec2 Position);
public sealed record SoulAcquiredEvent(string SoulId);
public sealed record SoulLostEvent(string SoulId);
public sealed record SoulXpGainedEvent(string SoulId, int Amount, int Level, int Xp, int XpToNext);
public sealed record SoulLevelUpEvent(string SoulId, int Level);
public sealed record SoulBannerCreatedEvent(string SoulBannerId, SoulBannerTier Tier);
public sealed record SoulBoundEvent(string SoulId, string SoulBannerId, SoulBannerTier Tier, int Level, int SlotIndex);
public sealed record SoulUnboundEvent(string SoulId, string SoulBannerId);
public sealed record AllySpawnedEvent(string Uid, string DefinitionId, string SpeciesId, int Rank, int Level, Vec2 Position);
public sealed record AllyDefeatedEvent(string Uid, string DefinitionId, string SpeciesId, Vec2 Position);
public sealed record SoulSummonedEvent(string SoulId, string SummonUid, string SoulBannerId, Vec2 Position);
public sealed record SoulUnsummonedEvent(string SoulId, string SummonUid);
public sealed record SoulDispersedEvent(string SoulId, double DurationSeconds);
public sealed record SoulRecoveredEvent(string SoulId);
