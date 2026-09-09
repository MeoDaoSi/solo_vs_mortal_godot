using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Simulation.Systems.V25;

public enum V25QuestStatus { Locked, Available, Active, Completed, Rewarded }

public sealed record V25QuestState(
    string QuestId,
    V25QuestStatus Status,
    int ObjectiveProgress,
    int ObjectiveRequired,
    bool HintShown,
    string? RewardReceiptId);

public sealed record V25QuestSnapshot(
    IReadOnlyList<V25QuestState> Quests,
    IReadOnlyList<string> ObjectiveEventIds,
    IReadOnlyList<string> RewardReceipts,
    IReadOnlyList<string> HintedNpcIds);

public sealed record V25QuestInteractionResult(
    bool Success,
    string? QuestId = null,
    string? Message = null,
    bool HintShown = false);

public sealed record V25QuestRewardResult(
    bool Success,
    bool AlreadyClaimed = false,
    string? QuestId = null,
    string? ReceiptId = null,
    string? Failure = null);

/// <summary>
/// Canonical quest and NPC producer owner. Objective progress is event-ID keyed, and rewards are
/// receipt keyed. A quest definition never becomes completed merely because it is present in the
/// pinned content; only a matching producer event can advance it.
/// </summary>
public sealed class V25QuestSystem : IDisposable
{
    private readonly CanonicalContentRegistry _canonical;
    private readonly EventBus _events;
    private readonly SoulSystem _souls;
    private readonly ProgressionSystem _progression;
    private readonly V25InventorySystem _inventory;
    private readonly V25SkillGrantSystem _skills;
    private readonly Func<string> _regionId;
    private readonly Func<bool> _atShrine;
    private readonly Func<string, bool> _hasFact;
    private readonly Func<long> _tick;
    private readonly Dictionary<string, QuestRow> _quests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _objectiveEventIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _rewardReceipts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hintedNpcIds = new(StringComparer.Ordinal);
    private readonly IDisposable _defeatSubscription;
    private readonly IDisposable _soulAcquiredSubscription;
    private readonly IDisposable _summonSubscription;
    private readonly IDisposable _possessionSubscription;
    private readonly IDisposable _factSubscription;

    public V25QuestSystem(CanonicalContentRegistry canonical, EventBus events, SoulSystem souls, ProgressionSystem progression,
        V25InventorySystem inventory, V25SkillGrantSystem skills, Func<string> regionId, Func<bool> atShrine,
        Func<string, bool> hasFact, Func<long> tick)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical)); _events = events ?? throw new ArgumentNullException(nameof(events));
        _souls = souls ?? throw new ArgumentNullException(nameof(souls)); _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory)); _skills = skills ?? throw new ArgumentNullException(nameof(skills));
        _regionId = regionId ?? throw new ArgumentNullException(nameof(regionId)); _atShrine = atShrine ?? throw new ArgumentNullException(nameof(atShrine));
        _hasFact = hasFact ?? throw new ArgumentNullException(nameof(hasFact)); _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        InitializeNewGame();
        _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnMonsterDefeated);
        _soulAcquiredSubscription = events.Subscribe<SoulAcquiredEvent>(OnSoulAcquired);
        _summonSubscription = events.Subscribe<SoulSummonedEvent>(OnSoulSummoned);
        _possessionSubscription = events.Subscribe<PossessionStartedEvent>(OnPossessionStarted);
        _factSubscription = events.Subscribe<V25FactCommittedEvent>(_ => RefreshAvailability());
    }

    public IReadOnlyList<V25QuestState> Quests => _quests.Values.OrderBy(row => row.Definition.Id, StringComparer.Ordinal).Select(row => row.Snapshot()).ToArray();
    public IReadOnlyList<V25QuestState> ActiveQuests => Quests.Where(row => row.Status == V25QuestStatus.Active).ToArray();
    public IReadOnlySet<string> RewardReceipts => _rewardReceipts;
    public bool IsRewarded(string questId) => _rewardReceipts.Contains(RewardReceipt(questId));

    /// <summary>Stages ownership objectives before the Soul capture WAL payload is built.</summary>
    public void PrepareOwnershipCommit()
    {
        foreach (var soul in _souls.OwnedSouls().OrderBy(item => item.Origin.SpeciesId, StringComparer.Ordinal))
            RecordObjective("OwnSpecies", soul.Origin.SpeciesId, soul.Id);
    }

    public void InitializeNewGame()
    {
        _quests.Clear(); _objectiveEventIds.Clear(); _rewardReceipts.Clear(); _hintedNpcIds.Clear();
        var allowedRegions = _canonical.RegionsForProfile(_canonical.ActiveProfileId).Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in _canonical.Content.Quests.Where(quest => allowedRegions.Contains(quest.RegionId)).OrderBy(quest => quest.Id, StringComparer.Ordinal))
            _quests.Add(definition.Id, new QuestRow(definition, definition.Prerequisites.Count == 0 ? V25QuestStatus.Available : V25QuestStatus.Locked));
        RefreshAvailability();
    }

    /// <summary>NPC interaction is the only way to create the authored tutorial interaction
    /// event. It returns the first available objective by An, Kha, Linh priority and emits a
    /// one-time hint without completing any unrelated quest.</summary>
    public V25QuestInteractionResult InteractNpc(string npcId)
    {
        if (string.IsNullOrWhiteSpace(npcId) || !_canonical.Content.NpcIds.Contains(npcId, StringComparer.Ordinal))
            return new(false, Message: "Unknown canonical NPC.");
        if (npcId is not ("an" or "kha" or "linh")) return new(false, Message: "NPC is not in the beta interaction contract.");
        var region = _regionId();
        var candidates = _quests.Values.Where(row => row.Definition.RegionId == region && row.Definition.Objective.Event == "InteractNpc" && row.Definition.Objective.Target == npcId && row.Status is V25QuestStatus.Available or V25QuestStatus.Active)
            .OrderBy(row => NpcPriority(npcId)).ThenBy(row => row.Definition.Id, StringComparer.Ordinal).ToArray();
        var hint = _hintedNpcIds.Add(npcId);
        var completed = candidates.FirstOrDefault(row => RecordObjectiveInternal(row.Definition.Objective.Event, row.Definition.Objective.Target, $"npc.{npcId}.{_tick()}", false));
        RefreshAvailability();
        return completed is null
            ? new(false, Message: hint ? "NPC hint recorded." : "No active authored objective for this NPC.", HintShown: hint)
            : new(true, completed.Definition.Id, completed.Definition.Name, hint);
    }

    /// <summary>Producer seam for landmarks, group clears, chests and future world events. The
    /// caller supplies a stable event identity; retries with the same identity are no-ops.</summary>
    public bool RecordObjective(string eventName, string target, string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(eventId)) return false;
        if (!_objectiveEventIds.Add($"{eventName}|{target}|{eventId}")) return false;
        var changed = RecordObjectiveInternal(eventName, target, eventId, true);
        RefreshAvailability();
        return changed;
    }

    /// <summary>Claims a completed non-auto quest at its authored region shrine. Auto quests are
    /// committed at completion; this same method remains idempotent for a replayed receipt.</summary>
    public V25QuestRewardResult ClaimReward(string questId)
    {
        if (!_quests.TryGetValue(questId, out var row)) return new(false, QuestId: questId, Failure: "UnknownQuest");
        var receipt = RewardReceipt(questId);
        if (_rewardReceipts.Contains(receipt) || row.Status == V25QuestStatus.Rewarded)
            return new(true, true, questId, receipt);
        if (row.Status != V25QuestStatus.Completed) return new(false, QuestId: questId, Failure: "ObjectiveIncomplete");
        if (!row.Definition.AutoReward && (_regionId() != row.Definition.RegionId || !_atShrine())) return new(false, QuestId: questId, Failure: "NotAtShrine");
        return ApplyReward(row, receipt);
    }

    public V25QuestSnapshot Snapshot() => new(
        Quests,
        _objectiveEventIds.Order(StringComparer.Ordinal).ToArray(),
        _rewardReceipts.Order(StringComparer.Ordinal).ToArray(),
        _hintedNpcIds.Order(StringComparer.Ordinal).ToArray());

    public void Restore(V25QuestSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var staged = new Dictionary<string, QuestRow>(StringComparer.Ordinal);
        foreach (var state in snapshot.Quests ?? throw new InvalidDataException("Canonical quest snapshot is missing."))
        {
            if (state is null || !_quests.TryGetValue(state.QuestId, out var existing) || !staged.TryAdd(state.QuestId, QuestRow.FromSnapshot(existing.Definition, state)))
                throw new InvalidDataException("Canonical quest snapshot contains an unknown or duplicate row.");
        }
        if (!staged.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(_quests.Keys)) throw new InvalidDataException("Canonical quest snapshot must cover the beta quest set.");
        var events = snapshot.ObjectiveEventIds?.ToArray() ?? throw new InvalidDataException("Canonical quest event ledger is missing.");
        if (events.Any(string.IsNullOrWhiteSpace) || events.Distinct(StringComparer.Ordinal).Count() != events.Length) throw new InvalidDataException("Canonical quest event ledger is invalid.");
        var receipts = snapshot.RewardReceipts?.ToArray() ?? throw new InvalidDataException("Canonical quest reward ledger is missing.");
        if (receipts.Any(id => !id.StartsWith("quest.reward.", StringComparison.Ordinal)) || receipts.Distinct(StringComparer.Ordinal).Count() != receipts.Length) throw new InvalidDataException("Canonical quest reward receipt ledger is invalid.");
        foreach (var receipt in receipts)
        {
            var id = receipt["quest.reward.".Length..];
            if (!staged.TryGetValue(id, out var row) || row.Status != V25QuestStatus.Rewarded) throw new InvalidDataException("Canonical quest receipt does not match rewarded state.");
        }
        var hints = snapshot.HintedNpcIds?.ToArray() ?? throw new InvalidDataException("Canonical NPC hint ledger is missing.");
        if (hints.Any(id => id is not ("an" or "kha" or "linh")) || hints.Distinct(StringComparer.Ordinal).Count() != hints.Length) throw new InvalidDataException("Canonical NPC hint ledger is invalid.");
        _quests.Clear(); foreach (var row in staged) _quests.Add(row.Key, row.Value);
        _objectiveEventIds.Clear(); foreach (var id in events) _objectiveEventIds.Add(id);
        _rewardReceipts.Clear(); foreach (var id in receipts) _rewardReceipts.Add(id);
        _hintedNpcIds.Clear(); foreach (var id in hints) _hintedNpcIds.Add(id);
        RefreshAvailability();
    }

    private void OnMonsterDefeated(MonsterDefeatedEvent defeated)
    {
        if (!defeated.RewardEligible || defeated.EncounterType is V25EncounterType.Arena or V25EncounterType.Debug) return;
        var id = defeated.TargetLifeUid ?? defeated.Uid;
        RecordObjective("DefeatActor", defeated.EncounterId ?? defeated.Uid, id);
        if (defeated.EncounterId is { } encounter && encounter.Contains("tutorial.skeletons", StringComparison.Ordinal)) RecordObjective("DefeatGroup", "tutorial.skeletons", id);
    }

    private void OnSoulAcquired(SoulAcquiredEvent acquired)
    {
        var soul = _souls.OwnedSoul(acquired.SoulId);
        if (soul is not null) RecordObjective("OwnSpecies", soul.Origin.SpeciesId, acquired.SoulId);
    }

    private void OnSoulSummoned(SoulSummonedEvent summoned)
    {
        var soul = _souls.OwnedSoul(summoned.SoulId);
        if (soul is not null) RecordObjective("SummonSpecies", soul.Origin.SpeciesId, summoned.SummonUid);
    }

    private void OnPossessionStarted(PossessionStartedEvent started)
    {
        var soul = _souls.OwnedSoul(started.SoulId);
        if (soul is not null) RecordObjective("PossessSpecies", soul.Origin.SpeciesId, started.ProfileId);
    }

    private bool RecordObjectiveInternal(string eventName, string target, string eventId, bool requireActive)
    {
        var changed = false;
        foreach (var row in _quests.Values.OrderBy(row => row.Definition.Id, StringComparer.Ordinal))
        {
            var objective = row.Definition.Objective;
            if (row.Definition.RegionId != _regionId() || objective.Event != eventName || objective.Target != target || row.Status is V25QuestStatus.Completed or V25QuestStatus.Rewarded) continue;
            // Authored facts may arrive before prerequisites make the quest active. Keep their
            // stable event IDs and counters; availability decides when completion can commit.
            if (requireActive && row.Status == V25QuestStatus.Rewarded) continue;
            if (row.Status == V25QuestStatus.Available) row.Status = V25QuestStatus.Active;
            if (row.ObjectiveProgress >= objective.Count) continue;
            row.ObjectiveProgress++;
            changed = true;
            if (row.ObjectiveProgress >= objective.Count && row.Status != V25QuestStatus.Locked)
            {
                row.Status = V25QuestStatus.Completed;
                if (row.Definition.AutoReward) _ = ApplyReward(row, RewardReceipt(row.Definition.Id));
            }
        }
        return changed;
    }

    private V25QuestRewardResult ApplyReward(QuestRow row, string receipt)
    {
        if (_rewardReceipts.Contains(receipt)) { row.Status = V25QuestStatus.Rewarded; row.RewardReceiptId = receipt; return new(true, true, row.Definition.Id, receipt); }
        var rewards = row.Definition.Rewards;
        var targetSpecies = ResolveDensityTarget(rewards.DensityTarget);
        // Validate every target and authored prerequisite before applying any reward. The reward
        // itself has no random component, and each mutation below is deterministic and bounded.
        if (rewards.Density > 0 && targetSpecies is null) return new(false, QuestId: row.Definition.Id, Failure: "NoOwnedDensityTarget");
        if (rewards.Coin > 0 && !_inventory.CanAddCoins(rewards.Coin)) return new(false, QuestId: row.Definition.Id, Failure: "CoinOverflow");
        if (rewards.WorldSoul is { } soulReward && !_canonical.SpeciesForProfile(_canonical.ActiveProfileId).Any(species => species.Id == soulReward.SpeciesId)) return new(false, QuestId: row.Definition.Id, Failure: "UnknownWorldSoulSpecies");
        if (rewards.RegionSeal is { } seal && (seal < 1 || !_canonical.RegionsForProfile(_canonical.ActiveProfileId).Any(region => region.Id == row.Definition.RegionId))) return new(false, QuestId: row.Definition.Id, Failure: "InvalidRegionSeal");

        if (rewards.Density > 0)
        {
            try { _souls.ApplyCanonicalDensityReward($"tx.quest.{row.Definition.Id}", $"award.quest.{row.Definition.Id}", targetSpecies!, (decimal)rewards.Density, $"quest.{row.Definition.Id}"); }
            catch (Exception exception) when (exception is InvalidDataException or OverflowException) { return new(false, QuestId: row.Definition.Id, Failure: "DensityCommitFailed"); }
        }
        if (rewards.Xp > 0) _progression.AddPlayerXp(rewards.Xp);
        if (rewards.Coin > 0 && !_inventory.AddCoins(rewards.Coin)) throw new InvalidOperationException("Prevalidated quest coin award failed.");
        if (rewards.WorldSoul is { } worldSoul)
        {
            var position = new Vec2(128 + row.Definition.RegionId.Length * 3, 128);
            _souls.SpawnCanonicalTutorialSoul(receipt, worldSoul.SpeciesId, worldSoul.SourceLevel, position);
        }
        if (rewards.RegionSeal is { } regionSeal)
        {
            var factId = $"region.{row.Definition.RegionId}.seal";
            if (!_hasFact(factId)) _progression.IngestCanonicalFact(factId, "quest.region-seal", row.Definition.Id, _tick());
            _ = regionSeal;
        }
        _rewardReceipts.Add(receipt); row.Status = V25QuestStatus.Rewarded; row.RewardReceiptId = receipt;
        return new(true, QuestId: row.Definition.Id, ReceiptId: receipt);
    }

    private string? ResolveDensityTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        if (target != "selectedOwnedElseLowestSpeciesId") return null;
        return _souls.CanonicalDensity?.Ownership.Values.Where(state => state.IsOwned).OrderBy(state => state.SpeciesId, StringComparer.Ordinal).Select(state => state.SpeciesId).FirstOrDefault();
    }

    private void RefreshAvailability()
    {
        foreach (var row in _quests.Values)
        {
            if (row.Status is V25QuestStatus.Completed or V25QuestStatus.Rewarded) continue;
            var prerequisites = row.Definition.Prerequisites;
            var ready = prerequisites.All(id => _quests.TryGetValue(id, out var prerequisite) && prerequisite.Status == V25QuestStatus.Rewarded);
            if (ready && row.Status == V25QuestStatus.Locked)
            {
                row.Status = row.ObjectiveProgress >= row.Definition.Objective.Count ? V25QuestStatus.Completed : V25QuestStatus.Available;
                if (row.Status == V25QuestStatus.Completed && row.Definition.AutoReward) _ = ApplyReward(row, RewardReceipt(row.Definition.Id));
            }
            if (!ready && row.Status == V25QuestStatus.Available) row.Status = V25QuestStatus.Locked;
        }
    }

    private static int NpcPriority(string npcId) => npcId switch { "an" => 0, "kha" => 1, "linh" => 2, _ => 99 };
    private static string RewardReceipt(string questId) => $"quest.reward.{questId}";

    public void Dispose() { _defeatSubscription.Dispose(); _soulAcquiredSubscription.Dispose(); _summonSubscription.Dispose(); _possessionSubscription.Dispose(); _factSubscription.Dispose(); }

    private sealed class QuestRow
    {
        public QuestRow(CanonicalQuestDefinition definition, V25QuestStatus status) { Definition = definition; Status = status; }
        public CanonicalQuestDefinition Definition { get; }
        public V25QuestStatus Status { get; set; }
        public int ObjectiveProgress { get; set; }
        public string? RewardReceiptId { get; set; }
        public V25QuestState Snapshot() => new(Definition.Id, Status, ObjectiveProgress, Definition.Objective.Count, false, RewardReceiptId);
        public static QuestRow FromSnapshot(CanonicalQuestDefinition definition, V25QuestState state)
        {
            if (state.ObjectiveRequired != definition.Objective.Count || state.ObjectiveProgress < 0 || state.ObjectiveProgress > definition.Objective.Count || !Enum.IsDefined(state.Status) || state.Status == V25QuestStatus.Rewarded && string.IsNullOrWhiteSpace(state.RewardReceiptId)) throw new InvalidDataException($"Quest '{definition.Id}' snapshot is invalid.");
            return new(definition, state.Status) { ObjectiveProgress = state.ObjectiveProgress, RewardReceiptId = state.RewardReceiptId };
        }
    }
}
