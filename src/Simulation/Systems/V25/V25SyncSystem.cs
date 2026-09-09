using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State.V25;

namespace SoloVsMortal.Simulation.Systems.V25;

/// <summary>
/// Historical Sync ledger. Authored source amounts and versions are recorded once; event counts
/// can accrue before ownership and are claimed atomically after the species is captured.
/// </summary>
public sealed class V25SyncSystem : IDisposable
{
    private readonly CanonicalContentRegistry _canonical;
    private readonly SoulSystem _souls;
    private readonly EventBus _events;
    private readonly Dictionary<string, SpeciesState> _states = new(StringComparer.Ordinal);
    private readonly Func<long> _tick;
    private readonly Func<bool>? _atShrine;
    private readonly Func<string, bool>? _hasCapability;
    private readonly Func<string, bool>? _isPossessed;
    private readonly IDisposable _defeatSubscription;
    private readonly IDisposable _soulAcquiredSubscription;
    private readonly List<V25SyncAwardCommittedEvent> _eventsPendingCommit = new();

    public V25SyncSystem(EventBus events, CanonicalContentRegistry canonical, SoulSystem souls, Func<long>? tick = null,
        Func<bool>? atShrine = null, Func<string, bool>? hasCapability = null, Func<string, bool>? isPossessed = null)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _souls = souls ?? throw new ArgumentNullException(nameof(souls));
        _tick = tick ?? (() => 0);
        _atShrine = atShrine;
        _hasCapability = hasCapability;
        _isPossessed = isPossessed;
        foreach (var species in _canonical.SpeciesForProfile(_canonical.ActiveProfileId)) _states.Add(species.Id, new SpeciesState(species.Id));
        _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnMonsterDefeated);
        _soulAcquiredSubscription = events.Subscribe<SoulAcquiredEvent>(_ => EvaluateAll(publishImmediately: true));
    }

    public IReadOnlyList<V25SpeciesSyncSnapshot> Snapshot() => _states.Values.OrderBy(item => item.SpeciesId, StringComparer.Ordinal).Select(item => item.Snapshot()).ToArray();

    public long TotalMicro(string speciesId) => State(speciesId).AwardedMicroPoints;

    public IReadOnlySet<string> Milestones(string speciesId) => State(speciesId).UnlockedMilestones.ToHashSet(StringComparer.Ordinal);

    public bool IsAwarded(string speciesId, string sourceKey) => State(speciesId).Awards.ContainsKey(sourceKey);

    /// <summary>Claims sources made eligible by a pending Soul capture before its durable save is built.</summary>
    public void PrepareOwnershipCommit() => EvaluateAll(publishImmediately: false);

    public void CommitPreparedEvents()
    {
        var committed = _eventsPendingCommit.ToArray();
        _eventsPendingCommit.Clear();
        foreach (var item in committed) _events.Publish(item);
    }

    /// <summary>Records one authored source event with a stable event identity.</summary>
    public void RecordEvent(string eventId, string eventName, string target, string? actorSpeciesId = null, bool currentSpeciesPossession = false, bool atShrine = false)
    {
        RequireId(eventId, nameof(eventId)); RequireId(eventName, nameof(eventName)); RequireId(target, nameof(target));
        var sources = _canonical.Content.SyncSources.Where(source => _states.ContainsKey(source.SpeciesId) && source.Event == eventName && source.Target == target && (actorSpeciesId is null || source.SpeciesId == actorSpeciesId)).OrderBy(source => source.Id, StringComparer.Ordinal).ToArray();
        foreach (var source in sources)
        {
            var state = State(source.SpeciesId);
            var key = $"{source.Id}|{eventId}";
            if (!state.EventIds.Add(key)) continue;
            var progress = state.Progress.GetValueOrDefault(source.Id) ?? new ProgressState(source.Id);
            progress.Count = checked(progress.Count + 1);
            progress.EventIds.Add(eventId);
            state.Progress[source.Id] = progress;
            TryClaim(source, state, atShrine, currentSpeciesPossession);
        }
    }

    /// <summary>Explicit producer API for landmarks, ally kills, secrets and quest facts.</summary>
    public void RecordSourceEvent(string sourceId, string eventId, bool atShrine = false, bool currentSpeciesPossession = false)
    {
        RequireId(sourceId, nameof(sourceId)); RequireId(eventId, nameof(eventId));
        var source = _canonical.Content.SyncSources.FirstOrDefault(item => item.Id == sourceId)
            ?? throw new InvalidDataException($"Unknown canonical Sync source '{sourceId}'.");
        RecordEvent(eventId, source.Event, source.Target, source.SpeciesId, currentSpeciesPossession, atShrine);
    }

    public void Restore(IEnumerable<V25SpeciesSyncSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var staged = new Dictionary<string, SpeciesState>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || !staged.TryAdd(snapshot.SpeciesId, SpeciesState.FromSnapshot(snapshot, _canonical)))
                throw new InvalidDataException("Canonical Sync snapshot has duplicate or null species state.");
        }
        var expected = _states.Keys.ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(staged.Keys)) throw new InvalidDataException("Canonical Sync snapshot must cover all profile species.");
        // Every row was validated into a detached state, so replacing this map is the commit point.
        _states.Clear(); foreach (var pair in staged) _states.Add(pair.Key, pair.Value);
        _eventsPendingCommit.Clear();
    }

    private void OnMonsterDefeated(MonsterDefeatedEvent defeated)
    {
        if (!defeated.RewardEligible || defeated.EncounterType is V25EncounterType.Arena or V25EncounterType.Debug) return;
        var eventId = $"kill.{defeated.TargetLifeUid ?? defeated.Uid}";
        RecordEvent(eventId, "EligibleSpeciesKills", defeated.SpeciesId, defeated.SpeciesId);
        if (defeated.EncounterType is V25EncounterType.Boss or V25EncounterType.Elite)
            RecordEvent(eventId, "DefeatActor", defeated.EncounterId ?? defeated.Uid, defeated.SpeciesId);
    }

    private void EvaluateAll(bool publishImmediately)
    {
        foreach (var source in _canonical.Content.SyncSources.Where(item => _states.ContainsKey(item.SpeciesId)).OrderBy(item => item.Id, StringComparer.Ordinal))
            TryClaim(source, State(source.SpeciesId), atShrine: _atShrine?.Invoke() == true, currentSpeciesPossession: _isPossessed?.Invoke(source.SpeciesId) == true, publishImmediately);
    }

    private void TryClaim(CanonicalSyncSourceDefinition source, SpeciesState state, bool atShrine, bool currentSpeciesPossession, bool publishImmediately = true)
    {
        var progress = state.Progress.GetValueOrDefault(source.Id);
        if (progress is null || progress.Count < source.RequiredCount || state.Awards.ContainsKey(source.SourceKey)) return;
        var owned = _souls.CanonicalDensity?.GetOwnership(source.SpeciesId).IsOwned == true;
        if (source.RequiresOwned && !owned) return;
        if (state.AwardedMicroPoints < ToMicro(source.RequiresSyncAtLeast)) return;
        if (source.RequiredCapability is { } required && (_hasCapability?.Invoke(required) != true)) return;
        if (source.ClaimAt == "shrine" && !atShrine) return;
        if (source.RequiresCurrentSpeciesPossession && !currentSpeciesPossession) return;
        var amount = ToMicro(source.Gain);
        if (amount <= 0) throw new InvalidDataException($"Sync source '{source.Id}' has a non-positive authored gain.");
        // Award identity is stable across definition revisions. SourceVersion and the exact
        // amount live in the immutable row, so a later rebalance cannot award the same source
        // again or rewrite the historical amount.
        var awardId = $"sync.award.{source.SpeciesId}.{source.SourceKey}";
        var commitSequence = _tick();
        var milestones = state.UnlockedMilestones.ToHashSet(StringComparer.Ordinal);
        var total = checked(state.AwardedMicroPoints + amount);
        foreach (var milestone in _canonical.Balance.Sync.Milestones)
            if (total >= ToMicro(milestone)) milestones.Add($"sync.{source.SpeciesId}.{(int)milestone}");
        // Award, receipt, event dedup and milestone unlock are one state replacement.
        state.Awards.Add(source.SourceKey, new V25SyncAwardState(awardId, source.SourceKey, source.SourceVersion, amount, commitSequence));
        state.AwardedMicroPoints = total;
        state.UnlockedMilestones.Clear(); foreach (var milestone in milestones) state.UnlockedMilestones.Add(milestone);
        var committedEvent = new V25SyncAwardCommittedEvent(awardId, source.SpeciesId, source.SourceKey, amount);
        if (publishImmediately) _events.Publish(committedEvent);
        else _eventsPendingCommit.Add(committedEvent);
    }

    private SpeciesState State(string speciesId) => _states.TryGetValue(speciesId, out var state)
        ? state : throw new InvalidDataException($"Canonical Sync species '{speciesId}' is unavailable.");

    private static long ToMicro(double points)
    {
        if (!double.IsFinite(points) || points < 0) throw new InvalidDataException("Sync amount is invalid.");
        return V25FixedPoint.RoundMicro(points);
    }

    private static void RequireId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.:-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Invalid Sync {label} '{value}'.");
    }

    public void Dispose() { _defeatSubscription.Dispose(); _soulAcquiredSubscription.Dispose(); }

    private sealed class SpeciesState
    {
        public SpeciesState(string speciesId) { SpeciesId = speciesId; }
        public string SpeciesId { get; }
        public long AwardedMicroPoints { get; set; }
        public Dictionary<string, V25SyncAwardState> Awards { get; } = new(StringComparer.Ordinal);
        public HashSet<string> UnlockedMilestones { get; } = new(StringComparer.Ordinal);
        public HashSet<string> EventIds { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ProgressState> Progress { get; } = new(StringComparer.Ordinal);
        public V25SpeciesSyncSnapshot Snapshot() => new(SpeciesId, AwardedMicroPoints, Awards.Values.OrderBy(item => item.SourceKey, StringComparer.Ordinal).ToArray(), UnlockedMilestones.Order(StringComparer.Ordinal).ToArray(), EventIds.Order(StringComparer.Ordinal).ToArray(), Progress.Values.OrderBy(item => item.SourceId, StringComparer.Ordinal).Select(item => item.Snapshot()).ToArray());
        public static SpeciesState FromSnapshot(V25SpeciesSyncSnapshot snapshot, CanonicalContentRegistry canonical)
        {
            var expectedSpecies = canonical.SpeciesForProfile(canonical.ActiveProfileId).FirstOrDefault(item => item.Id == snapshot.SpeciesId)
                ?? throw new InvalidDataException($"Unknown Sync species '{snapshot.SpeciesId}'.");
            if (snapshot.AwardedMicroPoints < 0) throw new InvalidDataException("Sync amount cannot be negative.");
            var state = new SpeciesState(expectedSpecies.Id) { AwardedMicroPoints = snapshot.AwardedMicroPoints };
            foreach (var key in snapshot.LegacyDedupKeys ?? Array.Empty<string>()) { RequireId(key, "legacyDedupKey"); if (!state.EventIds.Add(key)) throw new InvalidDataException("Duplicate Sync dedup key."); }
            foreach (var milestone in snapshot.UnlockedMilestoneIds ?? Array.Empty<string>()) { RequireId(milestone, "milestoneId"); if (!state.UnlockedMilestones.Add(milestone)) throw new InvalidDataException("Duplicate Sync milestone."); }
            foreach (var award in snapshot.Awards ?? Array.Empty<V25SyncAwardState>())
            {
                if (award is null || !state.Awards.TryAdd(award.SourceKey, award) || award.AwardedMicroPoints <= 0 || award.CommitSequence < 0 || string.IsNullOrWhiteSpace(award.SourceVersion) || award.AwardId != $"sync.award.{snapshot.SpeciesId}.{award.SourceKey}")
                    throw new InvalidDataException("Invalid Sync award ledger row.");
                _ = canonical.Content.SyncSources.FirstOrDefault(item => item.SpeciesId == snapshot.SpeciesId && item.SourceKey == award.SourceKey)
                    ?? throw new InvalidDataException($"Sync award source '{award.SourceKey}' is not authored for '{snapshot.SpeciesId}'.");
            }
            foreach (var progress in snapshot.Progress ?? Array.Empty<V25SyncSourceProgressState>())
            {
                if (progress is null || !state.Progress.TryAdd(progress.SourceId, ProgressState.FromSnapshot(progress))) throw new InvalidDataException("Invalid Sync source progress row.");
                foreach (var eventId in progress.EventIds) if (!state.EventIds.Add($"{progress.SourceId}|{eventId}")) throw new InvalidDataException("Duplicate Sync source event identity.");
            }
            foreach (var source in canonical.Content.SyncSources.Where(item => item.SpeciesId == snapshot.SpeciesId))
                if (!state.Progress.ContainsKey(source.Id)) state.Progress.Add(source.Id, new ProgressState(source.Id));
            var sum = checked(state.Awards.Values.Sum(item => item.AwardedMicroPoints));
            if (sum != state.AwardedMicroPoints) throw new InvalidDataException("Sync total does not equal immutable award ledger.");
            return state;
        }
    }

    private sealed class ProgressState
    {
        public ProgressState(string sourceId) { SourceId = sourceId; }
        public string SourceId { get; }
        public int Count { get; set; }
        public HashSet<string> EventIds { get; } = new(StringComparer.Ordinal);
        public V25SyncSourceProgressState Snapshot() => new(SourceId, Count, EventIds.Order(StringComparer.Ordinal).ToArray());
        public static ProgressState FromSnapshot(V25SyncSourceProgressState snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.SourceId) || snapshot.Count < 0 || snapshot.EventIds is null || snapshot.EventIds.Count != snapshot.EventIds.Distinct(StringComparer.Ordinal).Count() || snapshot.EventIds.Count > snapshot.Count)
                throw new InvalidDataException("Sync source progress is invalid.");
            var result = new ProgressState(snapshot.SourceId) { Count = snapshot.Count };
            foreach (var id in snapshot.EventIds) { RequireId(id, "Sync event ID"); result.EventIds.Add(id); }
            return result;
        }
    }
}
