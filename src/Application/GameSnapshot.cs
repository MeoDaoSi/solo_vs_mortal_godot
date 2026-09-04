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
    WorldSnapshot World,
    PlayerSnapshot Player,
    IReadOnlyList<MonsterSnapshot> Monsters,
    IReadOnlyList<AllySnapshot> Allies,
    IReadOnlyList<InventoryItemSnapshot> Inventory,
    IReadOnlyList<WorldSoulSnapshot> WorldSouls,
    IReadOnlyList<OwnedSoulSnapshot> OwnedSouls,
    IReadOnlyList<SoulBannerSnapshot> SoulBanners);

public sealed record PlayerSnapshot(string Uid, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, int Level, int Xp, int Rank, int Attack, int Defense, int Speed);
public sealed record MonsterSnapshot(string Uid, string DefinitionId, string SpeciesId, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, MonsterAiState AiState, int Level, int Rank);
public sealed record AllySnapshot(string Uid, string DefinitionId, string SpeciesId, string DisplayName, string SourceSoulId, Vec2 Position, double CurrentHp, double MaximumHp, AllyAiState AiState, int Level, int Rank);
public sealed record InventoryItemSnapshot(string StableId, int Count);
public sealed record WorldSoulSnapshot(string Id, string SoulNatureId, Vec2 Position, int OriginRank, string OriginSpeciesId);
public sealed record OwnedSoulSnapshot(string Id, string SoulNatureId, string DisplayName, int Level, int Xp, int OriginRank, string OriginSpeciesId);
public sealed record SoulBannerSnapshot(string Id, SoulBannerTier Tier, int Level, IReadOnlyList<string> BoundSoulIds, int UsedCapacity, int SlotLimit, int CapacityLimit, int ActiveLimit);
public sealed record WorldSnapshot(double Width, double Height, IReadOnlyList<Rect> BlockingRects);
public sealed record WorldObjectSnapshot(string Id, string Type, string AssetId, Vec2 Position, bool Blocking, bool Destroyed);
public sealed record AssetSnapshot(string Id, string File, int? FrameWidth, int? FrameHeight);
public sealed record AnimationClipSnapshot(string Id, double FrameRate, int Repeat, IReadOnlyList<string> Files);
public sealed record PlayerAnimationClipSnapshot(string Id, AssetSnapshot Asset, int DirectionRow, int FrameCount, double FrameRate, int Repeat, double Scale);
public sealed record DefeatedMonsterVisualSnapshot(string Uid, string SpeciesId, int Rank, Vec2 Position, double DurationSeconds);
