using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Application.Persistence.V25;

/// <summary>Versioned V2.5 save records. These are intentionally separate from the legacy prototype v1-v6 payload.</summary>
public static class V25SaveFormat
{
    public const string FormatId = "svm.v2.5.save";
    public const int SchemaVersion = 1;
    public const string SpecRevision = "2026-09-08.closed-1";
    public const string ContentVersion = "svm-content-2.5.1";
    public const string BalanceVersion = "svm-balance-2.5.1";
}

public sealed record V25PlayerSaveState(
    int Level,
    long Xp,
    long CurrentHpMilli,
    long MaxHpMilli,
    long CurrentSpiritMilli,
    long MaxSpiritMilli,
    string RegionId,
    string CheckpointId,
    string Uid = "player_1");

public sealed record V25OwnedSpeciesSaveState(
    string SpeciesId,
    int PowerTier,
    long CommittedDensity,
    long PendingDensity,
    long SyncMicro,
    long VitalityMicro,
    string Mode,
    string? ActiveAllyUid,
    int PassedGateIndex = 0,
    IReadOnlyList<int>? ProofKeys = null);

public sealed record V25WorldSoulSaveState(
    string PickupId,
    string MonsterUid,
    string MonsterDefinitionId,
    string SpeciesId,
    string DisplayName,
    int Rank,
    string RankKey,
    string RankDisplayName,
    int SourceLevel,
    int SourceRank,
    double PositionX,
    double PositionY,
    string SoulNatureId,
    bool RewardEligible = true);

public sealed record V25SoulPitySaveState(string SpeciesId, int Counter);

public sealed record V25FactSaveRecord(
    string FactId,
    string ProducerId,
    string SourceId,
    string SourceVersion,
    long ProducedTick);

public sealed record V25BannerGateSaveRecord(
    string GateId,
    string GateVersion,
    string ReceiptId,
    int FromRank,
    int ToRank);

public sealed record V25SummonSaveState(
    string SpeciesId,
    int RecoveryTicks,
    double HpRatio,
    double AttackCooldown);

public sealed record V25PossessionSaveState(
    string SourceInstanceId,
    string SoulId,
    string SpeciesId,
    int SoulLevel,
    int SoulRank,
    long SyncMicro,
    IReadOnlyList<string> MilestoneIds,
    double SoulBaseHp,
    double SoulBaseAttack,
    double SoulBaseDefense,
    double SoulBaseMoveSpeed,
    double TransferHp,
    double TransferAttack,
    double TransferDefense,
    double TransferMoveSpeed,
    string SignatureSkillId,
    string CapabilityId,
    double DurationSeconds,
    double RemainingSeconds,
    double CooldownSeconds,
    long StartedTick);

public sealed record V25PossessionCooldownSaveState(string SpeciesId, double RemainingSeconds);

public sealed record V25SafeAnchorSaveState(
    double PositionX,
    double PositionY,
    long ConfirmedTick,
    string GateStateId,
    bool SafeWalkmesh);

public sealed record V25TraversalSaveState(
    V25SafeAnchorSaveState? SafeAnchor,
    int AnchorCandidateTicks,
    double CandidatePositionX,
    double CandidatePositionY,
    string? CandidateGateStateId,
    int FlightGraceTicks,
    int BreathingGraceTicks,
    string? ActiveTerrain,
    double ActiveWidthUnits,
    string? ActiveGateStateId,
    long ActiveStartedTick,
    int CrumblingTicks);

public sealed record V25ItemInstanceSaveState(string InstanceUid, string DefinitionId, int Count);
public sealed record V25EquippedItemSaveState(string Slot, string InstanceUid, string DefinitionId);
public sealed record V25InventorySaveState(
    long Coins,
    IReadOnlyList<V25ItemInstanceSaveState> Items,
    IReadOnlyList<V25ItemInstanceSaveState> Overflow,
    IReadOnlyList<V25EquippedItemSaveState> Equipped,
    int SharedPotionCooldownTicks, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int NextInstance = 0);

/// <summary>Canonical learned/grant loadout. Nullable slot entries preserve deliberate empty slots.</summary>
public sealed record V25SkillGrantSaveState(
    IReadOnlyList<string> LearnedSkillIds,
    IReadOnlyList<string?> ActiveSkillIds,
    IReadOnlyList<string?> PassiveSkillIds,
    IReadOnlyDictionary<string, int> PromotedRanks);

public sealed record V25MasteryCreditSaveState(
    string AwardId,
    string SkillId,
    string Metric,
    string CastId,
    string TargetLifeUid,
    long AmountMicro,
    long Tick,
    string SourceVersion);

public sealed record V25MasterySkillSaveState(
    string SkillId,
    string Metric,
    int PromotedRank,
    long ProgressMicro,
    IReadOnlyList<V25MasteryCreditSaveState> Credits);

public sealed record V25MasteryTargetBudgetSaveState(
    string TargetLifeUid,
    string EncounterId,
    long SpawnMaxHpMicro,
    long RemainingMicro,
    bool RewardEligible,
    V25EncounterType EncounterType);

public sealed record V25MasteryHealingDebtSaveState(string DebtId, long AmountMicro, long CreatedTick, long ExpireTick);

public sealed record V25MasterySaveState(
    IReadOnlyList<V25MasterySkillSaveState> Skills,
    IReadOnlyList<V25MasteryTargetBudgetSaveState> TargetBudgets,
    IReadOnlyList<V25MasteryHealingDebtSaveState> HealingDebts);

public sealed record V25QuestSaveState(
    string QuestId,
    V25QuestStatus Status,
    int ObjectiveProgress,
    int ObjectiveRequired,
    bool HintShown,
    string? RewardReceiptId);

public sealed record V25QuestSaveData(
    IReadOnlyList<V25QuestSaveState> Quests,
    IReadOnlyList<string> ObjectiveEventIds,
    IReadOnlyList<string> RewardReceipts,
    IReadOnlyList<string> HintedNpcIds);

public sealed record V25LootAwardSaveState(
    string AwardId,
    string TargetLifeUid,
    string EncounterId,
    V25EncounterType EncounterType,
    int Rank,
    long Coins,
    string? ItemDefinitionId,
    int? ItemRank,
    string SourceVersion);

public sealed record V25LootSaveData(IReadOnlyList<V25LootAwardSaveState> Awards);
public sealed record V25UniquePowerSaveState(string PowerId, string ReceiptId, long UnlockedTick, string HostBossId, int PowerRank);
public sealed record V25UniquePowerSaveData(IReadOnlyList<V25UniquePowerSaveState> Powers);
public sealed record V25WorldLifecycleSaveData(long WorldCycleId, IReadOnlyList<string> DefeatedEncounterIds,
    IReadOnlyList<string> ClearedGroupIds, IReadOnlyList<string> DiscoveredLandmarkIds, IReadOnlyList<string> OpenedChestIds, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, int>? HazardTicks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyList<V25RegionMonster>>? DormantRegions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? PickupRegions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyList<int>>? VisitedTiles = null);

public sealed record V25DensityAwardSaveRecord(
    string AwardId,
    string SourceId,
    string SourceVersion,
    long GrantedMicro,
    long AppliedCommitted,
    long AddedPending,
    long Discarded,
    IReadOnlyList<int> ProofKeys,
    string TransactionId = "",
    string SpeciesId = "",
    long ResultCommittedDensityMicro = 0,
    long ResultPendingDensityMicro = 0,
    int ResultPassedGateIndex = 0,
    int? SourceLevel = null,
    int? PreTransactionSoulRank = null);

public sealed record V25SyncAwardSaveRecord(
    string AwardId,
    string SourceKey,
    string SourceVersion,
    long AwardedMicroPoints,
    long CommitSequence);

public sealed record V25SpeciesSyncSaveState(
    string SpeciesId,
    IReadOnlyList<V25SyncAwardSaveRecord> Awards,
    IReadOnlyList<string> UnlockedMilestoneIds,
    IReadOnlyList<string> LegacyDedupKeys,
    IReadOnlyList<V25SyncSourceProgressSaveState>? Progress = null);

public sealed record V25SyncSourceProgressSaveState(string SourceId, int Count, IReadOnlyList<string> EventIds);

public sealed record V25ReceiptSaveRecord(string ReceiptId, string Kind, long CommitSequence);

/// <summary>
/// The live simulation portion of a V2.5 save. It deliberately contains only state owned by the
/// implemented fixed-tick runtime; future Soul/inventory/quest systems must add fields in a new schema.
/// Coordinates are doubles at the persistence boundary and resources/timers use explicit units.
/// </summary>
public sealed record V25RuntimeActorSaveState(
    string Uid,
    V25EntityKind Kind,
    string DefinitionId,
    string SpeciesId,
    double PositionX,
    double PositionY,
    long CurrentHpMilli,
    long MaxHpMilli,
    bool Alive,
    bool BreakthroughReady,
    int Level,
    int Rank,
    int PowerTier,
    string Archetype,
    string CombatStyleId,
    string? TargetUid,
    string? EncounterId,
    string? SignatureSkillId,
    string AiState,
    long AttackCooldownMilli,
    long CurrentSpiritMilli,
    long MaxSpiritMilli,
    int DodgeCooldownTicks,
    int DodgeRemainingTicks,
    int DodgeInvulnerabilityTicks,
    double DodgeDirectionX,
    double DodgeDirectionY,
    long DodgeDistanceRemainingMilli,
    long DodgeStepDistanceMilli,
    long VitalityMicro,
    int RecoveryTicks,
    V25EncounterType EncounterType,
    bool RewardEligible,
    string? SourceSoulId,
    string? DisplayName,
    IReadOnlyList<V25StatusInstance> Statuses,
    IReadOnlyList<V25ShieldInstance> Shields,
    int StaggerPoints = 0,
    int StaggerImmuneTicks = 0,
    int StaggerRecoveryTicks = 0,
    double FacingX = 0,
    double FacingY = 1,
    string AiMode = "Guard",
    int ThinkTicks = 0,
    string? FocusTargetUid = null,
    int FocusRemainingTicks = 0,
    int PathFailTicks = 0,
    string? RecentAttackerUid = null,
    int RecentAttackerAgeTicks = 0,
    int BossPatternIndex = 0,
    string? BossStoryInstanceId = null);

public sealed record V25CastSaveState(
    string CastId,
    string CasterUid,
    string SkillId,
    long AcceptedTick,
    long ReleaseTick,
    long EndTick,
    V25CastPhase Phase,
    double AimX,
    double AimY,
    string? TargetUid,
    V25EntityKind SourceKind,
    double? GroundX,
    double? GroundY,
    bool Released,
    int ElapsedTicks);

public sealed record V25ProjectileSaveState(
    string CastId,
    string CasterUid,
    string SkillId,
    V25EntityKind SourceKind,
    double PositionX,
    double PositionY,
    double DirectionX,
    double DirectionY,
    long TravelledMilli,
    int RemainingTicks,
    long ReleasedTick,
    int HitIndex,
    double SourceAttack = 0,
    double SourceDefense = 0,
    int SourceRank = 1,
    double SourceEncounterAttackFactor = 1,
    string? SourceCombatStyleId = null);

public sealed record V25CooldownSaveState(string SourceUid, string SkillId, int RemainingTicks);
public sealed record V25HitKeySaveState(string CastId, string TargetLifeUid, int HitIndex);
public sealed record V25KnockbackSaveState(string TargetUid, double DirectionX, double DirectionY, long RemainingDistanceMilli, int RemainingTicks);

public sealed record V25RuntimeSaveState(
    int SchemaVersion,
    long SimulationTick,
    IReadOnlyList<V25RuntimeActorSaveState> Actors,
    IReadOnlyList<V25CastSaveState> Casts,
    IReadOnlyList<V25ProjectileSaveState> Projectiles,
    IReadOnlyList<V25CooldownSaveState> Cooldowns,
    IReadOnlyList<V25HitKeySaveState> HitKeys,
    IReadOnlyList<V25KnockbackSaveState>? Knockbacks = null,
    int? RespawnTicks = null,
    string? BufferedSkillId = null,
    int BufferedSkillTicks = 0,
    IReadOnlyList<MonsterNavigationState>? MonsterNavigation = null);

public sealed record V25SaveDocument(
    string ProfileId,
    string UidNext,
    string CurrentRegionId,
    V25PlayerSaveState Player,
    IReadOnlyList<V25OwnedSpeciesSaveState> OwnedSpecies,
    IReadOnlyList<V25DensityAwardSaveRecord> DensityAwards,
    IReadOnlyList<V25SpeciesSyncSaveState> Sync,
    IReadOnlyList<V25ReceiptSaveRecord> Receipts,
    IReadOnlyList<string> CommittedFactIds,
    V25RuntimeSaveState? Runtime = null,
    IReadOnlyList<V25WorldSoulSaveState>? WorldSouls = null,
    IReadOnlyList<V25SoulPitySaveState>? SoulPity = null,
    IReadOnlyList<string>? ConsumedPickupIds = null,
    IReadOnlyList<string>? TutorialReceipts = null,
    IReadOnlyList<V25FactSaveRecord>? Facts = null,
    int BannerRank = 1,
    IReadOnlyList<V25BannerGateSaveRecord>? BannerReceipts = null,
    IReadOnlyList<V25SummonSaveState>? SummonStates = null,
    long SpiritRemainderMicro = 0,
    long SpiritRateRemainderMicro = 0,
    V25PossessionSaveState? Possession = null,
    IReadOnlyList<V25PossessionCooldownSaveState>? PossessionCooldowns = null,
    int? PossessionTransitionLockTicks = null,
    V25TraversalSaveState? Traversal = null,
    V25InventorySaveState? Inventory = null,
    V25SkillGrantSaveState? SkillGrants = null,
    V25MasterySaveState? Mastery = null,
    V25QuestSaveData? Quests = null,
    V25LootSaveData? Loot = null,
    V25UniquePowerSaveData? UniquePowers = null,
    V25WorldLifecycleSaveData? WorldLifecycle = null);

public sealed record V25SaveEnvelope(
    string Format,
    int SchemaVersion,
    string SpecRevision,
    string ContentVersion,
    string BalanceVersion,
    long CommitSequence,
    string TransactionId,
    V25SaveDocument Payload,
    string Checksum,
    string SaveId = "",
    long SimulationTick = 0,
    IReadOnlyDictionary<string, Pcg32State>? RngStreams = null);

public sealed record V25WalRecord(
    string Format,
    int SchemaVersion,
    string TransactionId,
    string State,
    long CommitSequence,
    string PayloadChecksum,
    string SaveId = "");

public static class V25SaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static string Serialize(V25SaveEnvelope envelope)
    {
        var normalized = Normalize(envelope);
        Validate(normalized);
        return JsonSerializer.Serialize(normalized, Options);
    }

    public static V25SaveEnvelope Deserialize(string json, CanonicalContentRegistry? registry = null)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("V2.5 save is empty.");
        try
        {
            var envelope = JsonSerializer.Deserialize<V25SaveEnvelope>(json, Options) ?? throw new InvalidDataException("V2.5 save envelope is null.");
            Validate(envelope, registry);
            var expectedChecksum = ComputeEnvelopeChecksum(envelope);
            if (!string.Equals(envelope.Checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("V2.5 save payload checksum mismatch.");
            return envelope;
        }
        catch (JsonException exception) { throw new InvalidDataException("Malformed V2.5 save envelope.", exception); }
        catch (NotSupportedException exception) { throw new InvalidDataException("Unsupported V2.5 save value.", exception); }
        catch (ArgumentException exception) { throw new InvalidDataException("Invalid V2.5 save value.", exception); }
        catch (OverflowException exception) { throw new InvalidDataException("V2.5 save numeric value overflowed.", exception); }
        catch (FormatException exception) { throw new InvalidDataException("V2.5 save numeric value has an invalid format.", exception); }
    }

    public static string ComputePayloadChecksum(V25SaveDocument payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Options));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>Integrity covers commit identity and sequence as well as payload, preventing ambiguous retries.</summary>
    public static string ComputeEnvelopeChecksum(V25SaveEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var input = new
        {
            envelope.Format,
            envelope.SchemaVersion,
            envelope.SpecRevision,
            envelope.ContentVersion,
            envelope.BalanceVersion,
            envelope.CommitSequence,
            envelope.TransactionId,
            envelope.Payload,
            envelope.SaveId,
            envelope.SimulationTick,
            envelope.RngStreams,
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input, Options));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static V25SaveEnvelope Normalize(V25SaveEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return envelope with { Checksum = ComputeEnvelopeChecksum(envelope) };
    }

    public static void Validate(V25SaveEnvelope envelope, CanonicalContentRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!string.Equals(envelope.Format, V25SaveFormat.FormatId, StringComparison.Ordinal)) throw new InvalidDataException($"Unsupported save format '{envelope.Format}'.");
        if (envelope.SchemaVersion != V25SaveFormat.SchemaVersion) throw new InvalidDataException($"Unsupported V2.5 save schema '{envelope.SchemaVersion}'.");
        if (!string.Equals(envelope.SpecRevision, V25SaveFormat.SpecRevision, StringComparison.Ordinal)) throw new InvalidDataException($"Save spec revision '{envelope.SpecRevision}' is not supported.");
        if (!string.Equals(envelope.ContentVersion, V25SaveFormat.ContentVersion, StringComparison.Ordinal)) throw new InvalidDataException($"Save content version '{envelope.ContentVersion}' is not supported.");
        if (!string.Equals(envelope.BalanceVersion, V25SaveFormat.BalanceVersion, StringComparison.Ordinal)) throw new InvalidDataException($"Save balance version '{envelope.BalanceVersion}' is not supported.");
        if (envelope.CommitSequence < 0) throw new InvalidDataException("Save commit sequence cannot be negative.");
        RequireId(envelope.TransactionId, "transactionId");
        var payload = envelope.Payload ?? throw new InvalidDataException("V2.5 save payload is missing.");
        RequireId(payload.ProfileId, "profileId");
        RequireId(payload.CurrentRegionId, "currentRegionId");
        if (string.IsNullOrWhiteSpace(payload.UidNext) || !ulong.TryParse(payload.UidNext, NumberStyles.None, CultureInfo.InvariantCulture, out _)) throw new InvalidDataException("uidNext must be a decimal uint64 string.");
        RequireId(envelope.SaveId, "saveId");
        if (envelope.SimulationTick < 0) throw new InvalidDataException("simulationTick cannot be negative.");
        if (envelope.RngStreams is null) throw new InvalidDataException("rngStreams is missing.");
        var expectedStreams = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [nameof(Pcg32Streams.CombatCrit)] = ((ulong)Pcg32StreamId.CombatCrit << 1) | 1UL,
            [nameof(Pcg32Streams.SoulDrop)] = ((ulong)Pcg32StreamId.SoulDrop << 1) | 1UL,
            [nameof(Pcg32Streams.ItemDrop)] = ((ulong)Pcg32StreamId.ItemDrop << 1) | 1UL,
            [nameof(Pcg32Streams.ItemFamily)] = ((ulong)Pcg32StreamId.ItemFamily << 1) | 1UL,
        };
        if (envelope.RngStreams.Count != expectedStreams.Count || envelope.RngStreams.Keys.Any(key => !expectedStreams.ContainsKey(key)))
            throw new InvalidDataException("V2.5 save must contain exactly the four named RNG streams.");
        foreach (var entry in envelope.RngStreams)
        {
            RequireId(entry.Key, "rng stream ID");
            if (entry.Value is null || !ulong.TryParse(entry.Value.State, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                || !ulong.TryParse(entry.Value.Increment, NumberStyles.None, CultureInfo.InvariantCulture, out var increment)
                || increment != expectedStreams[entry.Key])
                throw new InvalidDataException($"RNG stream '{entry.Key}' is invalid.");
        }
        RequireRecord(payload.Player, "player");
        ValidatePlayer(payload.Player);
        RequireCollection(payload.OwnedSpecies, "ownedSpecies");
        RequireCollection(payload.DensityAwards, "densityAwards");
        RequireCollection(payload.Sync, "sync");
        RequireCollection(payload.Receipts, "receipts");
        RequireCollection(payload.CommittedFactIds, "committedFactIds");
        var worldSouls = payload.WorldSouls ?? Array.Empty<V25WorldSoulSaveState>();
        var soulPity = payload.SoulPity ?? Array.Empty<V25SoulPitySaveState>();
        var consumedPickupIds = payload.ConsumedPickupIds ?? Array.Empty<string>();
        var tutorialReceipts = payload.TutorialReceipts ?? Array.Empty<string>();
        var facts = payload.Facts ?? Array.Empty<V25FactSaveRecord>();
        var bannerReceipts = payload.BannerReceipts ?? Array.Empty<V25BannerGateSaveRecord>();
        var summonStates = payload.SummonStates ?? Array.Empty<V25SummonSaveState>();
        if (payload.SpiritRemainderMicro is < -999 or > 999 || payload.SpiritRateRemainderMicro is < -3599 or > 3599) throw new InvalidDataException("Spirit fractional remainder is outside the canonical fixed-tick bounds.");
        var possessionCooldowns = payload.PossessionCooldowns ?? Array.Empty<V25PossessionCooldownSaveState>();
        ValidateUnique(worldSouls.Select(item => item?.PickupId ?? throw new InvalidDataException("worldSouls contains a null record.")), "world soul pickup");
        ValidateUnique(soulPity.Select(item => item?.SpeciesId ?? throw new InvalidDataException("soulPity contains a null record.")), "Soul pity species");
        ValidateUnique(consumedPickupIds, "consumed pickup");
        ValidateUnique(tutorialReceipts, "tutorial receipt");
        ValidateUnique(facts.Select(item => item?.FactId ?? throw new InvalidDataException("facts contains a null record.")), "fact");
        ValidateUnique(bannerReceipts.Select(item => item?.ReceiptId ?? throw new InvalidDataException("bannerReceipts contains a null record.")), "banner receipt");
        ValidateUnique(summonStates.Select(item => item?.SpeciesId ?? throw new InvalidDataException("summonStates contains a null record.")), "summon species");
        ValidateUnique(payload.OwnedSpecies.Select(item => item?.SpeciesId ?? throw new InvalidDataException("ownedSpecies contains a null record.")), "owned species");
        ValidateUnique(payload.Sync.Select(item => item?.SpeciesId ?? throw new InvalidDataException("sync contains a null record.")), "sync species");
        foreach (var species in payload.OwnedSpecies) { RequireRecord(species, "ownedSpecies"); RequireId(species.SpeciesId, "ownedSpecies.speciesId"); if (species.PowerTier is < 1 or > 9) throw new InvalidDataException("Owned species power tier is outside [1..9]."); ValidateNonNegative(species.CommittedDensity, "committedDensity"); ValidateNonNegative(species.PendingDensity, "pendingDensity"); ValidateNonNegative(species.SyncMicro, "syncMicro"); ValidateNonNegative(species.VitalityMicro, "vitalityMicro"); RequireMode(species.Mode); if (species.ActiveAllyUid is not null) RequireId(species.ActiveAllyUid, "activeAllyUid"); if (species.PassedGateIndex is < 0 or > 8) throw new InvalidDataException("Owned species PassedGateIndex is outside [0..8]."); var proofKeys = species.ProofKeys ?? Array.Empty<int>(); if (proofKeys.Distinct().Count() != proofKeys.Count || proofKeys.Any(key => key is < 1 or > 8) || proofKeys.Count < species.PassedGateIndex) throw new InvalidDataException("Owned species proof keys are invalid or incomplete for passed gates."); for (var gate = 1; gate <= species.PassedGateIndex; gate++) if (!proofKeys.Contains(gate)) throw new InvalidDataException("Owned species passed gate lacks its permanent proof key."); }
        foreach (var species in payload.OwnedSpecies)
        {
            if (species.VitalityMicro > V25FixedPoint.DensitySyncScale) throw new InvalidDataException("vitalityMicro cannot exceed 1,000,000.");
            if (species.Mode == "Summoned" && species.ActiveAllyUid is null) throw new InvalidDataException("Summoned species must have activeAllyUid.");
            if (species.Mode != "Summoned" && species.ActiveAllyUid is not null) throw new InvalidDataException("Only Summoned species may have activeAllyUid.");
            if (species.Mode == "Dispersed" && species.VitalityMicro != 0) throw new InvalidDataException("Dispersed species vitality must be zero.");
            if (species.Mode == "Ready" && species.VitalityMicro <= 0) throw new InvalidDataException("Ready species vitality must be positive.");
        }
        ValidateUnique(payload.DensityAwards.Select(item => item?.AwardId ?? throw new InvalidDataException("densityAwards contains a null record.")), "density awards");
        ValidateUnique(payload.DensityAwards.Select(item => item?.TransactionId ?? throw new InvalidDataException("densityAwards contains a null record.")), "density transactions");
        foreach (var award in payload.DensityAwards) { RequireRecord(award, "densityAwards"); RequireCollection(award.ProofKeys, "densityAwards.proofKeys"); RequireId(award.AwardId, "densityAwards.awardId"); RequireId(award.TransactionId, "densityAwards.transactionId"); RequireId(award.SpeciesId, "densityAwards.speciesId"); RequireId(award.SourceId, "densityAwards.sourceId"); RequireId(award.SourceVersion, "densityAwards.sourceVersion"); ValidateNonNegative(award.GrantedMicro, "densityAwards.grantedMicro"); ValidateNonNegative(award.AppliedCommitted, "densityAwards.appliedCommitted"); ValidateNonNegative(award.AddedPending, "densityAwards.addedPending"); ValidateNonNegative(award.Discarded, "densityAwards.discarded"); ValidateNonNegative(award.ResultCommittedDensityMicro, "densityAwards.resultCommittedDensityMicro"); ValidateNonNegative(award.ResultPendingDensityMicro, "densityAwards.resultPendingDensityMicro"); if (award.ResultPassedGateIndex is < 0 or > 8) throw new InvalidDataException("Density result passed gate is outside [0..8]."); if (award.SourceLevel is { } sourceLevel && sourceLevel is < 1 or > 90) throw new InvalidDataException("Density source level is outside [1..90]."); if (award.PreTransactionSoulRank is { } preRank && preRank is < 1 or > 9) throw new InvalidDataException("Density pre-transaction rank is outside [1..9]."); if (award.SourceLevel is null != (award.PreTransactionSoulRank is null)) throw new InvalidDataException("Density source provenance is incomplete."); if (checked(award.AppliedCommitted + award.AddedPending + award.Discarded) != award.GrantedMicro) throw new InvalidDataException("Density award accounting does not conserve the immutable grant."); if (award.ProofKeys.Distinct().Count() != award.ProofKeys.Count || award.ProofKeys.Any(key => key is < 1 or > 8)) throw new InvalidDataException("Density proof key must be a unique gate ID in [1..8]."); }
        foreach (var soul in worldSouls) { RequireRecord(soul, "worldSoul"); RequireId(soul.PickupId, "worldSoul.pickupId"); RequireId(soul.MonsterUid, "worldSoul.monsterUid"); RequireId(soul.MonsterDefinitionId, "worldSoul.monsterDefinitionId"); RequireId(soul.SpeciesId, "worldSoul.speciesId"); RequireText(soul.DisplayName, "worldSoul.displayName"); RequireId(soul.RankKey, "worldSoul.rankKey"); RequireText(soul.RankDisplayName, "worldSoul.rankDisplayName"); RequireId(soul.SoulNatureId, "worldSoul.soulNatureId"); if (soul.SourceLevel is < 1 or > 90 || soul.SourceRank != V25ProgressionRules.RankFromLevel(soul.SourceLevel) || soul.Rank != soul.SourceRank || !double.IsFinite(soul.PositionX) || !double.IsFinite(soul.PositionY)) throw new InvalidDataException($"World Soul '{soul.PickupId}' source or position is invalid."); }
        foreach (var pity in soulPity) { RequireRecord(pity, "soulPity"); RequireId(pity.SpeciesId, "soulPity.speciesId"); if (pity.Counter < 0 || pity.Counter > 10) throw new InvalidDataException("Soul pity counter is outside [0..10]."); }
        foreach (var id in consumedPickupIds) RequireId(id, "consumedPickupId");
        foreach (var receipt in tutorialReceipts) RequireId(receipt, "tutorialReceipt");
        foreach (var fact in facts)
        {
            RequireRecord(fact, "fact"); RequireId(fact.FactId, "fact.factId"); RequireId(fact.ProducerId, "fact.producerId");
            RequireId(fact.SourceId, "fact.sourceId"); RequireId(fact.SourceVersion, "fact.sourceVersion");
            if (fact.ProducedTick < 0) throw new InvalidDataException("Fact production tick cannot be negative.");
        }
        if (payload.BannerRank is < 1 or > 9) throw new InvalidDataException("BannerRank is outside [1..9].");
        foreach (var receipt in bannerReceipts)
        {
            RequireRecord(receipt, "bannerReceipt"); RequireId(receipt.GateId, "bannerReceipt.gateId"); RequireId(receipt.GateVersion, "bannerReceipt.gateVersion"); RequireId(receipt.ReceiptId, "bannerReceipt.receiptId");
            if (receipt.FromRank < 1 || receipt.ToRank != receipt.FromRank + 1 || receipt.ToRank > 9) throw new InvalidDataException("Banner gate receipt rank range is invalid.");
        }
        foreach (var state in summonStates)
        {
            RequireRecord(state, "summonState"); RequireId(state.SpeciesId, "summonState.speciesId");
            if (state.RecoveryTicks < 0 || !double.IsFinite(state.HpRatio) || state.HpRatio is < 0 or > 1 || !double.IsFinite(state.AttackCooldown) || state.AttackCooldown < 0)
                throw new InvalidDataException("Canonical summon state is invalid.");
        }
        ValidateUnique(possessionCooldowns.Select(item => item?.SpeciesId ?? throw new InvalidDataException("possessionCooldowns contains a null record.")), "possession cooldown species");
        foreach (var cooldown in possessionCooldowns)
        {
            RequireRecord(cooldown, "possessionCooldown"); RequireId(cooldown.SpeciesId, "possessionCooldown.speciesId");
            if (!double.IsFinite(cooldown.RemainingSeconds) || cooldown.RemainingSeconds <= 0 || cooldown.RemainingSeconds > 90) throw new InvalidDataException("Canonical possession cooldown is invalid.");
        }
        if (payload.PossessionTransitionLockTicks is not { } transitionLockTicks || transitionLockTicks is < 0 or > 18)
            throw new InvalidDataException("V2.5 save lacks a valid possession transition-lock timer; original preserved rather than clearing a pending transition.");
        if (payload.Possession is { } possession)
        {
            RequireRecord(possession, "possession"); RequireId(possession.SourceInstanceId, "possession.sourceInstanceId"); RequireId(possession.SoulId, "possession.soulId"); RequireId(possession.SpeciesId, "possession.speciesId");
            RequireCollection(possession.MilestoneIds, "possession.milestoneIds"); ValidateUnique(possession.MilestoneIds, "possession milestone"); foreach (var milestone in possession.MilestoneIds) RequireId(milestone, "possession.milestoneId");
            RequireId(possession.SignatureSkillId, "possession.signatureSkillId"); RequireId(possession.CapabilityId, "possession.capabilityId");
            if (possession.SoulLevel is < 1 or > 90 || possession.SoulRank != V25ProgressionRules.RankFromLevel(possession.SoulLevel) || possession.SyncMicro is < 0 or > 100 * V25FixedPoint.DensitySyncScale || possession.StartedTick < 0 || !double.IsFinite(possession.DurationSeconds) || possession.DurationSeconds is < 8 or > 45 || !double.IsFinite(possession.RemainingSeconds) || possession.RemainingSeconds <= 0 || possession.RemainingSeconds > possession.DurationSeconds || !double.IsFinite(possession.CooldownSeconds) || possession.CooldownSeconds is < 18 or > 90)
                throw new InvalidDataException("Canonical possession snapshot is invalid.");
            foreach (var value in new[] { possession.SoulBaseHp, possession.SoulBaseAttack, possession.SoulBaseDefense, possession.SoulBaseMoveSpeed, possession.TransferHp, possession.TransferAttack, possession.TransferDefense, possession.TransferMoveSpeed })
                if (!double.IsFinite(value) || value < 0) throw new InvalidDataException("Canonical possession stat snapshot is invalid.");
            if (possessionCooldowns.Any(cooldown => cooldown.SpeciesId == possession.SpeciesId)) throw new InvalidDataException("Active canonical possession cannot have a species cooldown.");
        }
        if (payload.Traversal is { } traversal)
        {
            if (traversal.AnchorCandidateTicks is < 0 or > 30 || traversal.FlightGraceTicks is < 0 or > 120 || traversal.BreathingGraceTicks is < 0 or > 180 || traversal.CrumblingTicks is < 0 or > 120 || !double.IsFinite(traversal.CandidatePositionX) || !double.IsFinite(traversal.CandidatePositionY) || !double.IsFinite(traversal.ActiveWidthUnits) || traversal.ActiveWidthUnits < 0 || traversal.ActiveStartedTick < 0)
                throw new InvalidDataException("Canonical traversal timers or coordinates are invalid.");
            if (traversal.ActiveTerrain is { } activeTerrain && (!Enum.TryParse<V25TerrainTag>(activeTerrain, ignoreCase: false, out var parsedTerrain) || parsedTerrain is V25TerrainTag.Wall or V25TerrainTag.LockedGate || string.IsNullOrWhiteSpace(traversal.ActiveGateStateId)))
                throw new InvalidDataException("Canonical traversal active terrain is invalid.");
            if (traversal.SafeAnchor is { } anchor)
            {
                if (!double.IsFinite(anchor.PositionX) || !double.IsFinite(anchor.PositionY) || anchor.ConfirmedTick < 0 || string.IsNullOrWhiteSpace(anchor.GateStateId) || !anchor.SafeWalkmesh)
                    throw new InvalidDataException("Canonical safe anchor is invalid.");
            }
            if (traversal.AnchorCandidateTicks > 0 && string.IsNullOrWhiteSpace(traversal.CandidateGateStateId)) throw new InvalidDataException("Canonical anchor candidate lacks gate state.");
        }
        if (payload.Inventory is { } inventory)
        {
            if (inventory.Coins < 0 || inventory.SharedPotionCooldownTicks is < 0 or > 600 || inventory.Items is null || inventory.Overflow is null || inventory.Equipped is null)
                throw new InvalidDataException("Canonical inventory state is invalid.");
            var allInstances = new HashSet<string>(StringComparer.Ordinal);
            ValidateInventoryItems(inventory.Items, allInstances, "inventory.items");
            ValidateInventoryItems(inventory.Overflow, allInstances, "inventory.overflow");
            if (inventory.Items.Count > 60) throw new InvalidDataException("Canonical inventory exceeds 60 slots.");
            var slots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var equipped in inventory.Equipped)
            {
                RequireRecord(equipped, "inventory.equipped"); RequireId(equipped.InstanceUid, "inventory.equipped.instanceUid"); RequireId(equipped.DefinitionId, "inventory.equipped.definitionId");
                if (!slots.Add(equipped.Slot) || !Enum.TryParse<V25EquipmentSlot>(equipped.Slot, ignoreCase: false, out _)) throw new InvalidDataException("Canonical equipped slot is invalid or duplicated.");
                if (!allInstances.Add(equipped.InstanceUid)) throw new InvalidDataException("Canonical inventory instance is present in multiple locations.");
            }
        }
        if (payload.SkillGrants is { } grants)
        {
            RequireCollection(grants.LearnedSkillIds, "skillGrants.learnedSkillIds");
            RequireCollection(grants.ActiveSkillIds, "skillGrants.activeSkillIds");
            RequireCollection(grants.PassiveSkillIds, "skillGrants.passiveSkillIds");
            if (grants.LearnedSkillIds.Count != grants.LearnedSkillIds.Distinct(StringComparer.Ordinal).Count()) throw new InvalidDataException("Canonical learned skills are duplicated.");
            if (grants.ActiveSkillIds.Count > 6 || grants.PassiveSkillIds.Count > 3) throw new InvalidDataException("Canonical skill loadout exceeds slot arrays.");
            ValidateUnique(grants.ActiveSkillIds.Where(id => id is not null)!.Cast<string>(), "active skill slot");
            ValidateUnique(grants.PassiveSkillIds.Where(id => id is not null)!.Cast<string>(), "passive skill slot");
            foreach (var id in grants.LearnedSkillIds) RequireId(id, "skillGrants.learnedSkillId");
            foreach (var id in grants.ActiveSkillIds.Concat(grants.PassiveSkillIds).Where(id => id is not null)) RequireId(id!, "skillGrants.slotSkillId");
            if (grants.PromotedRanks is null) throw new InvalidDataException("Canonical promoted skill ranks are missing.");
            foreach (var row in grants.PromotedRanks) { RequireId(row.Key, "skillGrants.promotedSkillId"); if (row.Value is < 1 or > 6) throw new InvalidDataException("Canonical promoted skill rank is invalid."); }
            if (grants.PromotedRanks.Keys.Any(id => !grants.LearnedSkillIds.Contains(id, StringComparer.Ordinal))) throw new InvalidDataException("Promoted skill is not learned.");
        }
        if (payload.Mastery is { } mastery)
        {
            RequireCollection(mastery.Skills, "mastery.skills"); RequireCollection(mastery.TargetBudgets, "mastery.targetBudgets"); RequireCollection(mastery.HealingDebts, "mastery.healingDebts");
            ValidateUnique(mastery.Skills.Select(item => item?.SkillId ?? throw new InvalidDataException("mastery.skills contains a null record.")), "mastery skill");
            ValidateUnique(mastery.TargetBudgets.Select(item => item?.TargetLifeUid ?? throw new InvalidDataException("mastery.targetBudgets contains a null record.")), "mastery target budget");
            ValidateUnique(mastery.HealingDebts.Select(item => item?.DebtId ?? throw new InvalidDataException("mastery.healingDebts contains a null record.")), "mastery healing debt");
            var masteryAwards = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skillState in mastery.Skills)
            {
                RequireRecord(skillState, "mastery.skill"); RequireId(skillState.SkillId, "mastery.skill.skillId"); RequireId(skillState.Metric, "mastery.skill.metric");
                if (skillState.PromotedRank is < 1 or > 6 || skillState.ProgressMicro < 0 || skillState.Credits is null) throw new InvalidDataException("Canonical mastery skill state is invalid.");
                ValidateUnique(skillState.Credits.Select(item => item?.AwardId ?? throw new InvalidDataException("mastery.credits contains a null record.")), "mastery credit");
                foreach (var credit in skillState.Credits)
                {
                    RequireRecord(credit, "mastery.credit"); RequireId(credit.AwardId, "mastery.credit.awardId"); RequireId(credit.SkillId, "mastery.credit.skillId"); RequireId(credit.Metric, "mastery.credit.metric"); RequireId(credit.CastId, "mastery.credit.castId"); RequireId(credit.TargetLifeUid, "mastery.credit.targetLifeUid"); RequireId(credit.SourceVersion, "mastery.credit.sourceVersion");
                    if (!masteryAwards.Add(credit.AwardId) || credit.SkillId != skillState.SkillId || credit.Metric != skillState.Metric || credit.AmountMicro <= 0 || credit.Tick < 0) throw new InvalidDataException("Canonical mastery credit identity or amount is invalid.");
                }
            }
            foreach (var budget in mastery.TargetBudgets)
            {
                RequireRecord(budget, "mastery.targetBudget"); RequireId(budget.TargetLifeUid, "mastery.targetBudget.targetLifeUid"); RequireId(budget.EncounterId, "mastery.targetBudget.encounterId");
                if (budget.SpawnMaxHpMicro <= 0 || budget.RemainingMicro < 0 || budget.RemainingMicro > budget.SpawnMaxHpMicro || !Enum.IsDefined(budget.EncounterType)) throw new InvalidDataException("Canonical mastery target budget is invalid.");
            }
            foreach (var debt in mastery.HealingDebts)
            {
                RequireRecord(debt, "mastery.healingDebt"); RequireId(debt.DebtId, "mastery.healingDebt.debtId");
                if (debt.AmountMicro <= 0 || debt.CreatedTick < 0 || debt.ExpireTick <= debt.CreatedTick || debt.ExpireTick - debt.CreatedTick > 1800) throw new InvalidDataException("Canonical mastery healing debt is invalid.");
            }
        }
        if (payload.Quests is { } quests)
        {
            RequireCollection(quests.Quests, "quests.quests"); RequireCollection(quests.ObjectiveEventIds, "quests.objectiveEventIds"); RequireCollection(quests.RewardReceipts, "quests.rewardReceipts"); RequireCollection(quests.HintedNpcIds, "quests.hintedNpcIds");
            ValidateUnique(quests.Quests.Select(item => item?.QuestId ?? throw new InvalidDataException("quests contains a null record.")), "quest");
            ValidateUnique(quests.ObjectiveEventIds, "quest objective event"); ValidateUnique(quests.RewardReceipts, "quest reward receipt"); ValidateUnique(quests.HintedNpcIds, "quest NPC hint");
            foreach (var quest in quests.Quests)
            {
                RequireRecord(quest, "quest"); RequireId(quest.QuestId, "quest.questId");
                if (!Enum.IsDefined(quest.Status) || quest.ObjectiveRequired <= 0 || quest.ObjectiveProgress < 0 || quest.ObjectiveProgress > quest.ObjectiveRequired || quest.Status == V25QuestStatus.Rewarded && string.IsNullOrWhiteSpace(quest.RewardReceiptId)) throw new InvalidDataException("Canonical quest row is invalid.");
                if (quest.RewardReceiptId is not null) RequireId(quest.RewardReceiptId, "quest.rewardReceiptId");
            }
            foreach (var eventId in quests.ObjectiveEventIds) RequireText(eventId, "quest.objectiveEventId");
            foreach (var receipt in quests.RewardReceipts) { RequireId(receipt, "quest.rewardReceipt"); if (!receipt.StartsWith("quest.reward.", StringComparison.Ordinal)) throw new InvalidDataException("Canonical quest receipt has an invalid namespace."); }
            foreach (var npc in quests.HintedNpcIds) if (npc is not ("an" or "kha" or "linh")) throw new InvalidDataException("Canonical quest NPC hint is unknown.");
        }
        if (payload.Loot is { } loot)
        {
            RequireCollection(loot.Awards, "loot.awards"); ValidateUnique(loot.Awards.Select(item => item?.AwardId ?? throw new InvalidDataException("loot.awards contains a null record.")), "loot award");
            foreach (var award in loot.Awards)
            {
                RequireRecord(award, "loot.award"); RequireId(award.AwardId, "loot.awardId"); RequireId(award.TargetLifeUid, "loot.targetLifeUid"); RequireId(award.EncounterId, "loot.encounterId"); RequireId(award.SourceVersion, "loot.sourceVersion");
                if (!Enum.IsDefined(award.EncounterType) || award.Rank is < 1 or > 9 || award.Coins < 0 || award.ItemRank is < 1 or > 9) throw new InvalidDataException("Canonical loot award is invalid.");
                if (award.ItemDefinitionId is not null) RequireId(award.ItemDefinitionId, "loot.itemDefinitionId");
            }
        }
        if (payload.UniquePowers is { } uniques)
        {
            RequireCollection(uniques.Powers, "uniquePowers.powers"); ValidateUnique(uniques.Powers.Select(item => item?.PowerId ?? throw new InvalidDataException("uniquePowers contains a null record.")), "unique power");
            foreach (var power in uniques.Powers)
            {
                RequireRecord(power, "uniquePower"); RequireId(power.PowerId, "uniquePower.powerId"); RequireId(power.ReceiptId, "uniquePower.receiptId"); RequireId(power.HostBossId, "uniquePower.hostBossId");
                if (power.UnlockedTick < 0 || power.PowerRank is < 1 or > 9) throw new InvalidDataException("Canonical unique power state is invalid.");
            }
        }
        if (payload.WorldLifecycle is { } world)
        {
            if (world.WorldCycleId < 1) throw new InvalidDataException("Canonical world cycle is invalid.");
            RequireCollection(world.DefeatedEncounterIds, "world.defeatedEncounterIds"); RequireCollection(world.ClearedGroupIds, "world.clearedGroupIds");
            RequireCollection(world.DiscoveredLandmarkIds, "world.discoveredLandmarkIds"); RequireCollection(world.OpenedChestIds, "world.openedChestIds");
            ValidateUnique(world.DefeatedEncounterIds, "world defeated encounter"); ValidateUnique(world.ClearedGroupIds, "world cleared group");
            ValidateUnique(world.DiscoveredLandmarkIds, "world landmark"); ValidateUnique(world.OpenedChestIds, "world chest");
            foreach (var id in world.DefeatedEncounterIds.Concat(world.ClearedGroupIds).Concat(world.DiscoveredLandmarkIds).Concat(world.OpenedChestIds)) RequireId(id, "world ledger id");
        }
        var factIds = facts.Select(fact => fact.FactId).ToHashSet(StringComparer.Ordinal);
        if (!factIds.SetEquals(payload.CommittedFactIds))
            throw new InvalidDataException("CommittedFactIds must exactly match the typed fact ledger.");
        foreach (var species in payload.Sync) { RequireRecord(species, "sync"); RequireCollection(species.Awards, "sync.awards"); RequireCollection(species.UnlockedMilestoneIds, "sync.unlockedMilestoneIds"); RequireCollection(species.LegacyDedupKeys, "sync.legacyDedupKeys"); RequireId(species.SpeciesId, "sync.speciesId"); ValidateUnique(species.Awards.Select(item => item?.AwardId ?? throw new InvalidDataException("sync.awards contains a null record.")), "sync awards"); ValidateUnique(species.Awards.Select(item => item?.SourceKey ?? throw new InvalidDataException("sync.awards contains a null record.")), "sync source keys"); ValidateUnique(species.UnlockedMilestoneIds, "sync milestone IDs"); foreach (var award in species.Awards) { RequireRecord(award, "sync.award"); RequireId(award.AwardId, "sync.awardId"); RequireId(award.SourceKey, "sync.sourceKey"); RequireId(award.SourceVersion, "sync.sourceVersion"); ValidateNonNegative(award.AwardedMicroPoints, "sync.awardedMicroPoints"); if (award.CommitSequence < 0) throw new InvalidDataException("Sync commit sequence cannot be negative."); } foreach (var milestone in species.UnlockedMilestoneIds) RequireId(milestone, "sync.unlockedMilestoneId"); foreach (var key in species.LegacyDedupKeys) RequireId(key, "sync.legacyDedupKey"); ValidateUnique(species.LegacyDedupKeys, "sync legacy dedup keys"); }
        foreach (var species in payload.Sync)
        {
            var progress = species.Progress ?? Array.Empty<V25SyncSourceProgressSaveState>();
            ValidateUnique(progress.Select(item => item?.SourceId ?? throw new InvalidDataException("sync.progress contains a null record.")), "sync source progress");
            foreach (var row in progress)
            {
                RequireRecord(row, "sync.progress"); RequireId(row.SourceId, "sync.progress.sourceId"); RequireCollection(row.EventIds, "sync.progress.eventIds");
                if (row.Count < 0 || row.EventIds.Count > row.Count) throw new InvalidDataException("Sync source progress count is invalid.");
                ValidateUnique(row.EventIds, "sync progress event"); foreach (var eventId in row.EventIds) RequireId(eventId, "sync.progress.eventId");
            }
        }
        ValidateUnique(payload.Receipts.Select(item => item?.ReceiptId ?? throw new InvalidDataException("receipts contains a null record.")), "receipts");
        foreach (var receipt in payload.Receipts) { RequireRecord(receipt, "receipt"); RequireId(receipt.ReceiptId, "receiptId"); RequireId(receipt.Kind, "receipt.kind"); if (receipt.CommitSequence < 0) throw new InvalidDataException("Receipt commit sequence cannot be negative."); }
        foreach (var fact in payload.CommittedFactIds) RequireId(fact, "committedFactId");
        if (payload.Runtime is not null) ValidateRuntime(payload.Runtime);
        if (registry is not null) ValidateRegistryReferences(normalized: envelope, registry);
    }

    private static void ValidateRegistryReferences(V25SaveEnvelope normalized, CanonicalContentRegistry registry)
    {
        var profile = registry.Profile(normalized.Payload.ProfileId);
        if (!registry.RegionsForProfile(profile.Id).Any(region => region.Id == normalized.Payload.CurrentRegionId)) throw new InvalidDataException($"Save region '{normalized.Payload.CurrentRegionId}' is unavailable in profile '{profile.Id}'.");
        if (normalized.Payload.Player.Level > profile.MaxPlayerLevel) throw new InvalidDataException($"Player level exceeds profile cap {profile.MaxPlayerLevel}.");
        if (normalized.Payload.BannerRank > profile.MaxBannerRank) throw new InvalidDataException($"Banner rank exceeds profile cap {profile.MaxBannerRank}.");
        var speciesById = registry.SpeciesForProfile(profile.Id).ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var owned in normalized.Payload.OwnedSpecies)
        {
            if (!speciesById.TryGetValue(owned.SpeciesId, out var definition)) throw new InvalidDataException($"Save references species '{owned.SpeciesId}' unavailable in profile '{profile.Id}'.");
            if (owned.PowerTier != definition.PowerTier) throw new InvalidDataException($"Save power tier for '{owned.SpeciesId}' does not match canonical registry.");
        }
        var ownedBySpecies = normalized.Payload.OwnedSpecies.ToDictionary(item => item.SpeciesId, StringComparer.Ordinal);
        foreach (var cooldown in normalized.Payload.PossessionCooldowns ?? Array.Empty<V25PossessionCooldownSaveState>())
            if (!speciesById.ContainsKey(cooldown.SpeciesId)) throw new InvalidDataException($"Save possession cooldown references species '{cooldown.SpeciesId}' unavailable in profile '{profile.Id}'.");
        if (normalized.Payload.Possession is { } possession)
        {
            if (!speciesById.TryGetValue(possession.SpeciesId, out var possessionSpecies) || !ownedBySpecies.TryGetValue(possession.SpeciesId, out var owned)) throw new InvalidDataException("Save possession source species is not owned in this profile.");
            if (!string.Equals(possession.SignatureSkillId, possessionSpecies.SignatureSkillId, StringComparison.Ordinal) || !string.Equals(possession.CapabilityId, possessionSpecies.CapabilityId, StringComparison.Ordinal)) throw new InvalidDataException("Save possession grants do not match canonical species definition.");
            if (!string.Equals(owned.Mode, "Possessed", StringComparison.Ordinal) || owned.ActiveAllyUid is not null) throw new InvalidDataException("Active possession must match a Possessed ownership row without an Ally link.");
        }
        else if (normalized.Payload.OwnedSpecies.Any(owned => owned.Mode == "Possessed")) throw new InvalidDataException("Possessed ownership row has no active possession snapshot.");
        foreach (var sync in normalized.Payload.Sync)
            if (!speciesById.ContainsKey(sync.SpeciesId)) throw new InvalidDataException($"Save sync references species '{sync.SpeciesId}' unavailable in profile '{profile.Id}'.");
        foreach (var worldSoul in normalized.Payload.WorldSouls ?? Array.Empty<V25WorldSoulSaveState>())
        {
            if (!speciesById.ContainsKey(worldSoul.SpeciesId)) throw new InvalidDataException($"Save world Soul '{worldSoul.PickupId}' references species '{worldSoul.SpeciesId}' unavailable in profile '{profile.Id}'.");
        }
        foreach (var pity in normalized.Payload.SoulPity ?? Array.Empty<V25SoulPitySaveState>())
            if (!speciesById.ContainsKey(pity.SpeciesId)) throw new InvalidDataException($"Save Soul pity references species '{pity.SpeciesId}' unavailable in profile '{profile.Id}'.");
        if (normalized.Payload.Mastery is { } mastery)
        {
            foreach (var state in mastery.Skills)
            {
                var skill = registry.Content.Skills.FirstOrDefault(item => item.Id == state.SkillId)
                    ?? throw new InvalidDataException($"Save mastery references unknown skill '{state.SkillId}'.");
                if (skill.DefaultSourceKind != "PermanentLearned" || skill.MasteryMetric == "None" || skill.MasteryMetric != state.Metric)
                    throw new InvalidDataException($"Save mastery skill '{state.SkillId}' is not an eligible canonical learned metric.");
            }
        }
        if (normalized.Payload.Quests is { } quests)
        {
            var authored = registry.Content.Quests.Where(quest => profile.RegionIds.Contains(quest.RegionId, StringComparer.Ordinal)).ToDictionary(quest => quest.Id, StringComparer.Ordinal);
            foreach (var quest in quests.Quests)
            {
                if (!authored.TryGetValue(quest.QuestId, out var definition) || definition.Objective.Count != quest.ObjectiveRequired)
                    throw new InvalidDataException($"Save quest '{quest.QuestId}' is not authored for profile '{profile.Id}'.");
                if (quest.RewardReceiptId is not null && quest.RewardReceiptId != $"quest.reward.{quest.QuestId}") throw new InvalidDataException("Quest reward receipt does not match its quest identity.");
            }
            if (!authored.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(quests.Quests.Select(quest => quest.QuestId))) throw new InvalidDataException("Save quest state must cover the complete authored beta quest set.");
        }
        if (normalized.Payload.Loot is { } loot)
        {
            var equipment = registry.EquipmentForProfile(profile.Id).ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var award in loot.Awards)
            {
                if (award.SourceVersion != registry.Content.ContentVersion) throw new InvalidDataException("Save loot source version is not the pinned content version.");
                if (award.ItemDefinitionId is { } itemId && !equipment.ContainsKey(itemId)) throw new InvalidDataException($"Save loot references equipment '{itemId}' unavailable in profile '{profile.Id}'.");
            }
        }
        if (normalized.Payload.UniquePowers is { } uniques)
        {
            var unique = registry.Content.UniquePowers.Where(power => power.Profiles.Contains(profile.Id, StringComparer.Ordinal)).ToDictionary(power => power.Id, StringComparer.Ordinal);
            foreach (var power in uniques.Powers)
            {
                if (!unique.TryGetValue(power.PowerId, out var definition) || power.ReceiptId != definition.ReceiptId || power.HostBossId != definition.HostBossId || power.PowerRank != definition.PowerRank)
                    throw new InvalidDataException($"Save unique power '{power.PowerId}' is unavailable or mismatched for profile '{profile.Id}'.");
            }
        }

        if (normalized.Payload.Runtime is { } runtime)
        {
            var actors = runtime.Actors.ToDictionary(actor => actor.Uid, StringComparer.Ordinal);
            var profileSpecies = speciesById;
            foreach (var actor in runtime.Actors)
            {
                if (actor.Kind == V25EntityKind.Player)
                {
                    if (actor.Uid != normalized.Payload.Player.Uid || actor.DefinitionId != "player" || actor.Level != normalized.Payload.Player.Level || actor.Rank != V25ProgressionRules.RankFromLevel(actor.Level)) throw new InvalidDataException("Runtime player identity/progression does not match save player.");
                }
                else if (!profileSpecies.TryGetValue(actor.SpeciesId, out var species))
                    throw new InvalidDataException($"Runtime actor species '{actor.SpeciesId}' is unavailable in profile '{profile.Id}'.");
                else
                {
                    if (actor.PowerTier != species.PowerTier || actor.CombatStyleId != species.CombatStyleId || actor.SignatureSkillId != species.SignatureSkillId)
                        throw new InvalidDataException($"Runtime actor '{actor.Uid}' does not match canonical species '{actor.SpeciesId}'.");
                }
                if (actor.SignatureSkillId is not null && !registry.Content.Skills.Any(skill => skill.Id == actor.SignatureSkillId))
                    throw new InvalidDataException($"Runtime actor '{actor.Uid}' references unknown signature skill '{actor.SignatureSkillId}'.");
            }
            foreach (var cast in runtime.Casts)
            {
                if (!registry.Content.Skills.Any(skill => skill.Id == cast.SkillId)) throw new InvalidDataException($"Runtime cast references unknown skill '{cast.SkillId}'.");
                if (!actors.TryGetValue(cast.CasterUid, out var source) || source.Kind != cast.SourceKind) throw new InvalidDataException($"Runtime cast '{cast.CastId}' source is missing or has the wrong kind.");
                if (cast.TargetUid is not null && !actors.ContainsKey(cast.TargetUid)) throw new InvalidDataException($"Runtime cast '{cast.CastId}' target is missing.");
            }
            foreach (var projectile in runtime.Projectiles)
            {
                if (!registry.Content.Skills.Any(skill => skill.Id == projectile.SkillId)) throw new InvalidDataException($"Runtime projectile references unknown skill '{projectile.SkillId}'.");
                if (actors.TryGetValue(projectile.CasterUid, out var source))
                {
                    if (source.Kind != projectile.SourceKind) throw new InvalidDataException($"Runtime projectile '{projectile.CastId}' source has the wrong kind.");
                }
                else if (projectile.SourceAttack <= 0 || projectile.SourceRank is < 1 or > 9 || string.IsNullOrWhiteSpace(projectile.SourceCombatStyleId) || !registry.Content.CombatStyles.Any(style => style.Id == projectile.SourceCombatStyleId))
                    throw new InvalidDataException($"Runtime projectile '{projectile.CastId}' has no complete released offense snapshot for its despawned source.");
            }
            foreach (var cooldown in runtime.Cooldowns)
                if ((!actors.ContainsKey(cooldown.SourceUid) && !(normalized.Payload.WorldLifecycle?.DormantRegions?.Values.Any(rows => rows.Any(row => row.Uid == cooldown.SourceUid)) ?? false)) || !registry.Content.Skills.Any(skill => skill.Id == cooldown.SkillId)) throw new InvalidDataException("Runtime cooldown references an unknown actor or skill.");
            foreach (var hitKey in runtime.HitKeys)
                if (!actors.ContainsKey(hitKey.TargetLifeUid)) throw new InvalidDataException($"Runtime hit key target '{hitKey.TargetLifeUid}' is missing.");
            foreach (var knockback in runtime.Knockbacks ?? Array.Empty<V25KnockbackSaveState>())
                if (!actors.TryGetValue(knockback.TargetUid, out var target) || !target.Alive || target.Kind == V25EntityKind.Monster && target.EncounterType == V25EncounterType.Boss)
                    throw new InvalidDataException($"Runtime knockback target '{knockback.TargetUid}' is missing, dead, or boss-immune.");
        }
    }

    private static void ValidatePlayer(V25PlayerSaveState player)
    {
        if (player.Level is < 1 or > 90) throw new InvalidDataException("Player level is outside [1..90].");
        ValidateNonNegative(player.Xp, "player.xp"); ValidateNonNegative(player.CurrentHpMilli, "player.currentHpMilli"); ValidateNonNegative(player.MaxHpMilli, "player.maxHpMilli"); ValidateNonNegative(player.CurrentSpiritMilli, "player.currentSpiritMilli"); ValidateNonNegative(player.MaxSpiritMilli, "player.maxSpiritMilli");
        if (player.CurrentHpMilli > player.MaxHpMilli || player.CurrentSpiritMilli > player.MaxSpiritMilli) throw new InvalidDataException("Player current resource exceeds maximum.");
        RequireId(player.RegionId, "player.regionId"); RequireId(player.CheckpointId, "player.checkpointId"); RequireId(player.Uid, "player.uid");
    }

    private static void ValidateRuntime(V25RuntimeSaveState runtime)
    {
        if (runtime.BufferedSkillTicks is < 0 or > 6 || runtime.BufferedSkillId is null && runtime.BufferedSkillTicks != 0 || runtime.BufferedSkillId is not null && runtime.BufferedSkillTicks == 0)
            throw new InvalidDataException("Invalid saved combat intent buffer.");
        if (runtime.RespawnTicks is < 0 or > 120)
            throw new InvalidDataException("Runtime respawn timer is outside [0..120].");
        if (runtime.Actors is null) throw new InvalidDataException("Runtime actors are missing.");
        var savedPlayer = runtime.Actors.FirstOrDefault(actor => actor is not null && actor.Kind == V25EntityKind.Player);
        if (savedPlayer is not null && runtime.RespawnTicks is { } respawn && savedPlayer.Alive != (respawn == 0))
            throw new InvalidDataException("Runtime respawn timer disagrees with player life state.");
        if (runtime.SchemaVersion != 1) throw new InvalidDataException($"Unsupported V2.5 runtime schema '{runtime.SchemaVersion}'.");
        if (runtime.SimulationTick < 0) throw new InvalidDataException("Runtime simulationTick cannot be negative.");
        RequireCollection(runtime.Actors, "runtime.actors"); RequireCollection(runtime.Casts, "runtime.casts"); RequireCollection(runtime.Projectiles, "runtime.projectiles");
        RequireCollection(runtime.Cooldowns, "runtime.cooldowns"); RequireCollection(runtime.HitKeys, "runtime.hitKeys");
        if (runtime.Actors.Count(actor => actor?.Kind == V25EntityKind.Player) != 1) throw new InvalidDataException("Runtime must contain exactly one player actor.");
        ValidateUnique(runtime.Actors.Select(actor => actor?.Uid ?? throw new InvalidDataException("runtime.actors contains a null record.")), "runtime actor");
        var actorIds = runtime.Actors.Select(actor => actor.Uid).ToHashSet(StringComparer.Ordinal);
        foreach (var actor in runtime.Actors)
        {
            RequireRecord(actor, "runtime.actor"); RequireId(actor.Uid, "runtime.actor.uid"); RequireId(actor.DefinitionId, "runtime.actor.definitionId"); RequireId(actor.SpeciesId, "runtime.actor.speciesId");
            RequireText(actor.Archetype, "runtime.actor.archetype"); RequireId(actor.CombatStyleId, "runtime.actor.combatStyleId"); RequireText(actor.AiState, "runtime.actor.aiState");
            if (!Enum.IsDefined(actor.Kind) || !Enum.IsDefined(actor.EncounterType)) throw new InvalidDataException($"Runtime actor '{actor.Uid}' has an unknown enum value.");
            if (actor.Level is < 1 or > 90 || actor.Rank is < 1 or > 9 || actor.PowerTier is < 1 or > 9) throw new InvalidDataException($"Runtime actor '{actor.Uid}' progression is invalid.");
            ValidateNonNegative(actor.CurrentHpMilli, "runtime.actor.currentHpMilli"); ValidateNonNegative(actor.MaxHpMilli, "runtime.actor.maxHpMilli");
            ValidateNonNegative(actor.CurrentSpiritMilli, "runtime.actor.currentSpiritMilli"); ValidateNonNegative(actor.MaxSpiritMilli, "runtime.actor.maxSpiritMilli");
            if (actor.MaxHpMilli <= 0 || actor.CurrentHpMilli > actor.MaxHpMilli || actor.CurrentSpiritMilli > actor.MaxSpiritMilli || actor.Alive != (actor.CurrentHpMilli > 0)) throw new InvalidDataException($"Runtime actor '{actor.Uid}' resource state is invalid.");
            ValidateNonNegative(actor.AttackCooldownMilli, "runtime.actor.attackCooldownMilli"); ValidateNonNegative(actor.DodgeCooldownTicks, "runtime.actor.dodgeCooldownTicks"); ValidateNonNegative(actor.DodgeRemainingTicks, "runtime.actor.dodgeRemainingTicks"); ValidateNonNegative(actor.DodgeInvulnerabilityTicks, "runtime.actor.dodgeInvulnerabilityTicks"); ValidateNonNegative(actor.DodgeDistanceRemainingMilli, "runtime.actor.dodgeDistanceRemainingMilli"); ValidateNonNegative(actor.DodgeStepDistanceMilli, "runtime.actor.dodgeStepDistanceMilli"); ValidateNonNegative(actor.VitalityMicro, "runtime.actor.vitalityMicro"); ValidateNonNegative(actor.RecoveryTicks, "runtime.actor.recoveryTicks");
            if (actor.VitalityMicro > V25FixedPoint.DensitySyncScale || actor.DodgeRemainingTicks == 0 && actor.DodgeDistanceRemainingMilli != 0 || actor.StaggerPoints is < 0 or > 100 || actor.StaggerImmuneTicks < 0 || actor.StaggerRecoveryTicks < 0 || actor.Kind != V25EntityKind.Monster && (actor.StaggerPoints != 0 || actor.StaggerImmuneTicks != 0 || actor.StaggerRecoveryTicks != 0) || actor.EncounterType != V25EncounterType.Boss && (actor.StaggerPoints != 0 || actor.StaggerImmuneTicks != 0 || actor.StaggerRecoveryTicks != 0)) throw new InvalidDataException($"Runtime actor '{actor.Uid}' dodge/stagger/vitality state is invalid.");
            if (actor.BossPatternIndex is < 0 or > 2 || actor.EncounterType == V25EncounterType.Boss && string.IsNullOrWhiteSpace(actor.BossStoryInstanceId) || actor.EncounterType != V25EncounterType.Boss && (actor.BossPatternIndex != 0 || actor.BossStoryInstanceId is not null)) throw new InvalidDataException($"Runtime actor '{actor.Uid}' boss cycle state is invalid.");
            if (actor.BossStoryInstanceId is not null) RequireId(actor.BossStoryInstanceId, "actor.bossStoryInstanceId");
            if (!Enum.TryParse<AllyAiMode>(actor.AiMode, ignoreCase: false, out var aiMode) || !Enum.IsDefined(aiMode) || actor.ThinkTicks < 0 || actor.ThinkTicks > 6 || actor.FocusRemainingTicks < 0 || actor.FocusRemainingTicks > 480 || actor.PathFailTicks < 0 || actor.PathFailTicks > 120 || actor.RecentAttackerAgeTicks < 0 || actor.RecentAttackerAgeTicks > 120 || actor.Kind != V25EntityKind.Ally && (aiMode != AllyAiMode.Guard || actor.ThinkTicks != 0 || actor.FocusTargetUid is not null || actor.FocusRemainingTicks != 0 || actor.PathFailTicks != 0 || actor.RecentAttackerUid is not null || actor.RecentAttackerAgeTicks != 0))
                throw new InvalidDataException($"Runtime actor '{actor.Uid}' ally AI state is invalid.");
            if (!double.IsFinite(actor.PositionX) || !double.IsFinite(actor.PositionY) || !double.IsFinite(actor.DodgeDirectionX) || !double.IsFinite(actor.DodgeDirectionY) || !double.IsFinite(actor.FacingX) || !double.IsFinite(actor.FacingY)) throw new InvalidDataException($"Runtime actor '{actor.Uid}' position/direction is not finite.");
            if (actor.Kind == V25EntityKind.Player && new Core.Math.Vec2(actor.FacingX, actor.FacingY) == Core.Math.Vec2.Zero) throw new InvalidDataException("Runtime player facing direction cannot be zero.");
            RequireOptionalId(actor.TargetUid, "runtime.actor.targetUid"); RequireOptionalId(actor.EncounterId, "runtime.actor.encounterId"); RequireOptionalId(actor.SignatureSkillId, "runtime.actor.signatureSkillId"); RequireOptionalId(actor.SourceSoulId, "runtime.actor.sourceSoulId");
            RequireOptionalId(actor.FocusTargetUid, "runtime.actor.focusTargetUid"); RequireOptionalId(actor.RecentAttackerUid, "runtime.actor.recentAttackerUid");
            RequireCollection(actor.Statuses, "runtime.actor.statuses"); RequireCollection(actor.Shields, "runtime.actor.shields");
            ValidateUnique(actor.Statuses.Select(status => status is null ? throw new InvalidDataException("runtime.statuses contains a null record.") : $"{status.EffectId}|{status.SourceId}"), "runtime status");
            ValidateUnique(actor.Shields.Select(shield => shield?.SourceId ?? throw new InvalidDataException("runtime.shields contains a null record.")), "runtime shield source");
            foreach (var status in actor.Statuses)
            {
                RequireRecord(status, "runtime.status"); RequireId(status.SourceId, "runtime.status.sourceId"); RequireId(status.TargetUid, "runtime.status.targetUid"); RequireId(status.EffectId, "runtime.status.effectId");
                if (!actorIds.Contains(status.TargetUid) || status.TargetUid != actor.Uid || status.ExpireTick < runtime.SimulationTick || status.NextDotTick < 0 || status.Stacks is < 1 or > 3 || !double.IsFinite(status.Potency) || status.Potency < 0 || !double.IsFinite(status.SnapshotAttack) || status.SnapshotAttack < 0 || status.EffectId is not ("burn" or "poison" or "chill" or "stun" or "guard" or "ward" or "haste" or "veil" or "taunt") || actor.Kind == V25EntityKind.Monster && actor.EncounterType == V25EncounterType.Boss && status.EffectId == "stun") throw new InvalidDataException($"Runtime status on '{actor.Uid}' is invalid.");
            }
            var shieldTotal = 0.0;
            foreach (var shield in actor.Shields)
            {
                RequireRecord(shield, "runtime.shield"); RequireId(shield.SourceId, "runtime.shield.sourceId"); RequireId(shield.TargetUid, "runtime.shield.targetUid");
                if (shield.TargetUid != actor.Uid || shield.ExpireTick < runtime.SimulationTick || !double.IsFinite(shield.Amount) || shield.Amount <= 0) throw new InvalidDataException($"Runtime shield on '{actor.Uid}' is invalid.");
                shieldTotal += shield.Amount;
            }
            if (!double.IsFinite(shieldTotal) || shieldTotal > V25FixedPoint.FromMilli(actor.MaxHpMilli) * 0.5 + 0.001) throw new InvalidDataException($"Runtime shields on '{actor.Uid}' exceed the canonical 50% maximum.");
        }
        ValidateUnique(runtime.Casts.Select(cast => cast?.CastId ?? throw new InvalidDataException("runtime.casts contains a null record.")), "runtime cast");
        foreach (var cast in runtime.Casts)
        {
            RequireRecord(cast, "runtime.cast"); RequireId(cast.CastId, "runtime.cast.castId"); RequireId(cast.CasterUid, "runtime.cast.casterUid"); RequireId(cast.SkillId, "runtime.cast.skillId");
            if (!actorIds.Contains(cast.CasterUid) || !Enum.IsDefined(cast.SourceKind) || !Enum.IsDefined(cast.Phase) || cast.Phase is V25CastPhase.Finished or V25CastPhase.Canceled || cast.AcceptedTick < 0 || cast.ReleaseTick < cast.AcceptedTick || cast.EndTick < cast.ReleaseTick || cast.ElapsedTicks < 0 || cast.TargetUid == cast.CasterUid && cast.TargetUid is not null) throw new InvalidDataException($"Runtime cast '{cast.CastId}' timing/identity is invalid.");
            if (!double.IsFinite(cast.AimX) || !double.IsFinite(cast.AimY) || (cast.GroundX is { } gx && !double.IsFinite(gx)) || (cast.GroundY is { } gy && !double.IsFinite(gy))) throw new InvalidDataException($"Runtime cast '{cast.CastId}' coordinates are invalid.");
            RequireOptionalId(cast.TargetUid, "runtime.cast.targetUid");
            if ((cast.GroundX is null) != (cast.GroundY is null)) throw new InvalidDataException($"Runtime cast '{cast.CastId}' ground point is incomplete.");
        }
        ValidateUnique(runtime.Projectiles.Select(projectile => projectile?.CastId ?? throw new InvalidDataException("runtime.projectiles contains a null record.")), "runtime projectile");
        foreach (var projectile in runtime.Projectiles)
        {
            RequireRecord(projectile, "runtime.projectile"); RequireId(projectile.CastId, "runtime.projectile.castId"); RequireId(projectile.CasterUid, "runtime.projectile.casterUid"); RequireId(projectile.SkillId, "runtime.projectile.skillId");
            if (!Enum.IsDefined(projectile.SourceKind) || projectile.RemainingTicks < 0 || projectile.ReleasedTick < 0 || projectile.HitIndex < 0 || projectile.TravelledMilli < 0 || !double.IsFinite(projectile.PositionX) || !double.IsFinite(projectile.PositionY) || !double.IsFinite(projectile.DirectionX) || !double.IsFinite(projectile.DirectionY) || new Core.Math.Vec2(projectile.DirectionX, projectile.DirectionY) == Core.Math.Vec2.Zero || !double.IsFinite(projectile.SourceAttack) || !double.IsFinite(projectile.SourceDefense) || !double.IsFinite(projectile.SourceEncounterAttackFactor) || projectile.SourceAttack < 0 || projectile.SourceDefense < 0 || projectile.SourceEncounterAttackFactor < 0 || projectile.SourceRank is < 1 or > 9) throw new InvalidDataException($"Runtime projectile '{projectile.CastId}' is invalid.");
        }
        ValidateUnique(runtime.Cooldowns.Select(cooldown => $"{cooldown?.SourceUid}|{cooldown?.SkillId}"), "runtime cooldown");
        foreach (var cooldown in runtime.Cooldowns) { RequireRecord(cooldown, "runtime.cooldown"); RequireId(cooldown.SourceUid, "runtime.cooldown.sourceUid"); RequireId(cooldown.SkillId, "runtime.cooldown.skillId"); if (cooldown.RemainingTicks <= 0) throw new InvalidDataException("Runtime cooldown must be positive."); }
        ValidateUnique(runtime.HitKeys.Select(key => $"{key?.CastId}|{key?.TargetLifeUid}|{key?.HitIndex}"), "runtime hit key");
        foreach (var key in runtime.HitKeys) { RequireRecord(key, "runtime.hitKey"); RequireId(key.CastId, "runtime.hitKey.castId"); RequireId(key.TargetLifeUid, "runtime.hitKey.targetLifeUid"); if (key.HitIndex < 0) throw new InvalidDataException("Runtime hit index cannot be negative."); }
        var knockbacks = runtime.Knockbacks ?? Array.Empty<V25KnockbackSaveState>();
        ValidateUnique(knockbacks.Select(knockback => knockback?.TargetUid ?? throw new InvalidDataException("runtime.knockbacks contains a null record.")), "runtime knockback target");
        foreach (var knockback in knockbacks)
        {
            RequireRecord(knockback, "runtime.knockback"); RequireId(knockback.TargetUid, "runtime.knockback.targetUid");
            if (knockback.RemainingTicks <= 0 || knockback.RemainingDistanceMilli <= 0 || !double.IsFinite(knockback.DirectionX) || !double.IsFinite(knockback.DirectionY) || new Core.Math.Vec2(knockback.DirectionX, knockback.DirectionY) == Core.Math.Vec2.Zero)
                throw new InvalidDataException($"Runtime knockback '{knockback.TargetUid}' is invalid.");
        }
    }

    private static void RequireMode(string value) { if (value is not ("Ready" or "Summoned" or "Dispersed" or "Possessed")) throw new InvalidDataException($"Unknown Soul mode '{value}'."); }
    private static void ValidateNonNegative(long value, string label) { if (value < 0) throw new InvalidDataException($"{label} cannot be negative."); }
    private static void RequireId(string value, string label) { if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.:-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidDataException($"Invalid {label} '{value}'."); }
    private static void RequireOptionalId(string? value, string label) { if (value is not null) RequireId(value, label); }
    private static void RequireText(string value, string label) { if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException($"Invalid {label}."); }
    private static void RequireRecord(object? value, string label) { if (value is null) throw new InvalidDataException($"{label} contains a null record."); }
    private static void RequireCollection<T>(IReadOnlyCollection<T>? value, string label) { if (value is null) throw new InvalidDataException($"{label} collection is missing."); }
    private static void ValidateUnique(IEnumerable<string> values, string label) { var duplicate = values.GroupBy(value => value, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1); if (duplicate is not null) throw new InvalidDataException($"Duplicate {label} ID '{duplicate.Key}'."); }

    private static void ValidateInventoryItems(IReadOnlyList<V25ItemInstanceSaveState> items, HashSet<string> instanceIds, string label)
    {
        foreach (var item in items)
        {
            RequireRecord(item, label); RequireId(item.InstanceUid, $"{label}.instanceUid"); RequireId(item.DefinitionId, $"{label}.definitionId");
            if (!instanceIds.Add(item.InstanceUid) || item.Count <= 0 || item.Count > 99) throw new InvalidDataException($"Invalid or duplicate {label} item.");
        }
    }
}
