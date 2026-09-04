using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.Events;

public sealed record PlayerXpGainedEvent(int Amount, int Level, int Xp, int XpToNext);
public sealed record PlayerLevelUpEvent(int Level, int Rank);
public sealed record BreakthroughResultEvent(bool Success, double Chance, int PreviousRank, int Rank, int Level, PillId? PillId);
public sealed record ItemGainedEvent(string StableItemId, int Amount, int Total);
public sealed record PillCraftedEvent(PillId PillId, int Amount);
public sealed record TemporaryPlayerBuffChangedEvent(PillId PillId, bool Active, double RemainingSeconds);
