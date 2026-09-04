using SoloVsMortal.Simulation.State;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Application;

public sealed record GameSnapshot(
    GameStage Stage,
    double ElapsedSeconds,
    int MonsterDefinitionCount,
    int SoulBannerDefinitionCount,
    int SoulNatureDefinitionCount,
    int CapabilityDefinitionCount,
    PlayerSnapshot Player,
    IReadOnlyList<MonsterSnapshot> Monsters,
    IReadOnlyList<InventoryItemSnapshot> Inventory,
    IReadOnlyList<WorldSoulSnapshot> WorldSouls,
    IReadOnlyList<OwnedSoulSnapshot> OwnedSouls,
    IReadOnlyList<SoulBannerSnapshot> SoulBanners);

public sealed record PlayerSnapshot(string Uid, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, int Level, int Xp, int Rank, int Attack, int Defense, int Speed);
public sealed record MonsterSnapshot(string Uid, string DefinitionId, string SpeciesId, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, MonsterAiState AiState, int Level, int Rank);
public sealed record InventoryItemSnapshot(string StableId, int Count);
public sealed record WorldSoulSnapshot(string Id, string SoulNatureId, Vec2 Position, int OriginRank, string OriginSpeciesId);
public sealed record OwnedSoulSnapshot(string Id, string SoulNatureId, int Level, int Xp, int OriginRank, string OriginSpeciesId);
public sealed record SoulBannerSnapshot(string Id, SoulBannerTier Tier, int Level, IReadOnlyList<string> BoundSoulIds, int UsedCapacity, int SlotLimit, int CapacityLimit, int ActiveLimit);
