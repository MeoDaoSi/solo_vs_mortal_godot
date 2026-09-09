using SoloVsMortal.Data.Definitions.V25;

namespace SoloVsMortal.Simulation.State.V25;

/// <summary>Durable fact evidence. A fact is a claim made by one explicit producer and source version.</summary>
public sealed record V25FactEvidence(
    string FactId,
    string ProducerId,
    string SourceId,
    string SourceVersion,
    long ProducedTick);

public sealed record V25FactLedgerSnapshot(IReadOnlyList<V25FactEvidence> Facts);

public readonly record struct V25FactIngestResult(bool Accepted, bool AlreadyCommitted, V25FactEvidence Evidence);

/// <summary>
/// Typed, idempotent fact ledger used by progression and later quest/world producers. The combat
/// producer is deliberately strict: only an authored, reward-eligible Boss encounter can produce
/// a boss-clear fact. A normal Skeleton death therefore cannot satisfy breakthrough.
/// </summary>
public sealed class V25FactLedger
{
    private readonly CanonicalContentRegistry _canonical;
    private readonly string _profileId;
    private readonly Dictionary<string, V25FactEvidence> _facts = new(StringComparer.Ordinal);

    public V25FactLedger(CanonicalContentRegistry canonical, string? profileId = null)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _profileId = profileId ??= canonical.ActiveProfileId;
        _ = _canonical.Profile(profileId);
    }

    public IReadOnlyList<V25FactEvidence> Snapshot() => _facts.Values
        .OrderBy(fact => fact.FactId, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<string> FactIds() => _facts.Keys.Order(StringComparer.Ordinal).ToArray();

    public bool Contains(string factId) => !string.IsNullOrWhiteSpace(factId) && _facts.ContainsKey(factId);

    public V25FactEvidence? Get(string factId) => _facts.GetValueOrDefault(factId);

    /// <summary>Ingests evidence from a legitimate producer, preserving the first receipt forever.</summary>
    public V25FactIngestResult Ingest(V25FactEvidence evidence)
    {
        ValidateEvidence(evidence);
        if (_facts.TryGetValue(evidence.FactId, out var previous))
        {
            if (previous != evidence)
                throw new InvalidDataException($"Fact '{evidence.FactId}' conflicts with its committed provenance.");
            return new(false, true, previous);
        }
        _facts.Add(evidence.FactId, evidence);
        return new(true, false, evidence);
    }

    /// <summary>
    /// Converts a defeated actor into a fact only when its encounter identity matches an authored
    /// canonical Boss record. The caller supplies the runtime actor UID as source identity.
    /// </summary>
    public V25FactIngestResult IngestBossClear(string encounterId, string sourceUid, long tick)
    {
        RequireId(encounterId, nameof(encounterId));
        RequireId(sourceUid, nameof(sourceUid));
        if (tick < 0) throw new InvalidDataException("Fact production tick cannot be negative.");
        var encounter = _canonical.Content.Encounters.FirstOrDefault(item => item.Id == encounterId)
            ?? throw new InvalidDataException($"Encounter '{encounterId}' is not authored by the canonical bundle.");
        if (!string.Equals(encounter.EncounterType, "Boss", StringComparison.Ordinal) || !encounter.RewardEligible)
            throw new InvalidDataException($"Encounter '{encounterId}' is not a reward-eligible canonical Boss.");
        var profile = _canonical.Profile(_profileId);
        if (!profile.RegionIds.Contains(encounter.RegionId, StringComparer.Ordinal))
            throw new InvalidDataException($"Boss encounter '{encounterId}' is outside profile '{_profileId}'.");
        return Ingest(new V25FactEvidence($"{encounter.Id}.clear", "combat.boss-clear", sourceUid,
            _canonical.Content.ContentVersion, tick));
    }

    /// <summary>Restores facts through a temporary map and swaps only after every row validates.</summary>
    public void Restore(V25FactLedgerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var staged = new Dictionary<string, V25FactEvidence>(StringComparer.Ordinal);
        foreach (var fact in snapshot.Facts ?? throw new InvalidDataException("Fact snapshot is missing."))
        {
            if (fact is null) throw new InvalidDataException("Fact snapshot contains a null row.");
            ValidateEvidence(fact);
            if (!staged.TryAdd(fact.FactId, fact))
                throw new InvalidDataException($"Duplicate fact '{fact.FactId}'.");
        }
        _facts.Clear();
        foreach (var fact in staged) _facts.Add(fact.Key, fact.Value);
    }

    private void ValidateEvidence(V25FactEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        RequireId(evidence.FactId, "factId");
        RequireId(evidence.ProducerId, "producerId");
        RequireId(evidence.SourceId, "sourceId");
        RequireId(evidence.SourceVersion, "sourceVersion");
        if (evidence.ProducedTick < 0) throw new InvalidDataException("Fact production tick cannot be negative.");
        if (evidence.ProducerId == "combat.boss-clear")
        {
            var expected = _canonical.Content.Encounters.FirstOrDefault(item => item.Id == evidence.FactId[..^6])
                ?? throw new InvalidDataException($"Boss fact '{evidence.FactId}' has no authored encounter.");
            if (!string.Equals(evidence.FactId, $"{expected.Id}.clear", StringComparison.Ordinal) ||
                !string.Equals(expected.EncounterType, "Boss", StringComparison.Ordinal) || !expected.RewardEligible)
                throw new InvalidDataException($"Boss fact '{evidence.FactId}' does not match a reward-eligible Boss encounter.");
        }
    }

    private static void RequireId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.:-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Invalid {label} '{value}'.");
    }
}
