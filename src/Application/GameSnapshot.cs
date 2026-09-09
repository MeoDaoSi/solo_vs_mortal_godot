using SoloVsMortal.Simulation.State;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.Rules;

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
    IReadOnlyList<SoulBannerSnapshot> SoulBanners,
    string CurrentRegionId);

public sealed record PlayerSnapshot(string Uid, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, int Level, int Xp, int Rank, int Attack, int Defense, int Speed,
    double CurrentSpirit = 0, double MaximumSpirit = 0, double Shield = 0, bool IsDodging = false, bool IsInvulnerable = false, bool BreakthroughReady = false);
public sealed record MonsterSnapshot(string Uid, string DefinitionId, string SpeciesId, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, MonsterAiState AiState, int Level, int Rank,
    double Shield = 0);
public sealed record AllySnapshot(string Uid, string DefinitionId, string SpeciesId, string DisplayName, string SourceSoulId, Vec2 Position, double CurrentHp, double MaximumHp, AllyAiState AiState, int Level, int Rank,
    double Shield = 0, double Vitality = 1);
public sealed record InventoryItemSnapshot(string StableId, int Count);
public sealed record WorldSoulSnapshot(string Id, string SoulNatureId, Vec2 Position, int OriginRank, string OriginSpeciesId);
public sealed record OwnedSoulSnapshot(string Id, string SoulNatureId, string DisplayName, int Level, int Xp, int OriginRank, string OriginSpeciesId);
public sealed record SoulBannerSnapshot(string Id, SoulBannerTier Tier, int Level, IReadOnlyList<string> BoundSoulIds, int UsedCapacity, int SlotLimit, int CapacityLimit, int ActiveLimit);
public sealed record WorldSnapshot(double Width, double Height, IReadOnlyList<Rect> BlockingRects);
public sealed record V25RuntimeSnapshot(
    long SimulationTick,
    IReadOnlyList<V25CastView> Casts,
    IReadOnlyList<V25ProjectileView> Projectiles,
    IReadOnlyDictionary<string, Pcg32State> RngStreams,
    IReadOnlyList<V25ActorRuntimeSnapshot>? Actors = null,
    IReadOnlyList<V25CooldownView>? Cooldowns = null,
    IReadOnlyList<V25HitKeyView>? HitKeys = null,
    IReadOnlyList<V25KnockbackView>? Knockbacks = null);
public sealed record V25ActorRuntimeSnapshot(
    string Uid,
    V25EntityKind Kind,
    string DefinitionId,
    string SpeciesId,
    Vec2 Position,
    double CurrentHp,
    double MaximumHp,
    double Shield,
    bool Alive,
    int Level,
    int Rank,
    string? TargetUid,
    string? EncounterId,
    string CombatStyleId,
    IReadOnlyList<V25StatusInstance> Statuses,
    IReadOnlyList<V25ShieldInstance> Shields,
    int DodgeCooldownTicks = 0,
    int DodgeRemainingTicks = 0,
    int DodgeInvulnerabilityTicks = 0,
    int PowerTier = 1,
    string Archetype = "Balanced",
    double CurrentSpirit = 0,
    double MaximumSpirit = 0,
    double Vitality = 1,
    int RecoveryTicks = 0,
    Vec2 DodgeDirection = default,
    double DodgeDistanceRemaining = 0,
    double DodgeStepDistance = 0,
    string? SignatureSkillId = null,
    V25EncounterType EncounterType = V25EncounterType.Normal,
    bool RewardEligible = true,
    string? SourceSoulId = null,
    string? DisplayName = null,
    double AttackCooldown = 0,
    string AiState = "Idle",
    bool BreakthroughReady = false,
    int StaggerPoints = 0,
    int StaggerImmuneTicks = 0,
    int StaggerRecoveryTicks = 0,
    Vec2 Facing = default,
    AllyAiMode AiMode = AllyAiMode.Guard,
    int ThinkTicks = 0,
    string? FocusTargetUid = null,
    int FocusRemainingTicks = 0,
    int PathFailTicks = 0,
    string? RecentAttackerUid = null,
    int RecentAttackerAgeTicks = 0,
    int BossPatternIndex = 0,
    string? BossStoryInstanceId = null);
public sealed record WorldObjectSnapshot(string Id, string Type, string AssetId, Vec2 Position, bool Blocking, bool Destroyed, string? ZoneId = null, double PresentationScale = 1, int ZIndex = 0);
public sealed record AssetSnapshot(string Id, string File, int? FrameWidth, int? FrameHeight);
public sealed record AnimationClipSnapshot(string Id, double FrameRate, int Repeat, IReadOnlyList<string> Files);
public sealed record PlayerAnimationClipSnapshot(string Id, AssetSnapshot Asset, int DirectionRow, int FrameCount, double FrameRate, int Repeat, double Scale);
public sealed record DefeatedMonsterVisualSnapshot(string Uid, string SpeciesId, int Rank, Vec2 Position, double DurationSeconds);
public sealed record WorldMapRegionSnapshot(
    string Id,
    string DisplayName,
    string ShortDescription,
    string Story,
    string MapContentId,
    string? ScenePath,
    Vec2 WorldMapPosition,
    double WorldMapRadius,
    string Biome,
    bool IsStarter,
    bool IsCurrent,
    bool IsAvailable,
    bool HasPlayableContent,
    bool CanTravel,
    int? RecommendedLevelMinimum,
    int? RecommendedLevelMaximum,
    IReadOnlyList<string> TravelConditionIds,
    IReadOnlyList<string> Tags);
