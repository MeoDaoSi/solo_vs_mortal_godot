using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.State.V25;

namespace SoloVsMortal.Simulation.Systems.V25;

/// <summary>
/// Pure V2.5 ownership/Density transaction core. It is intentionally not connected to pickup,
/// UI, or save commands yet; 4.02 must wrap this staged operation in the durable pickup/save
/// transaction. Every request is calculated against a copied state and committed only after all
/// identity, proof, arithmetic, and resulting-state checks pass.
/// </summary>
public sealed class V25DensityEngine
{
    private readonly CanonicalContentRegistry _canonical;
    private readonly string _profileId;
    private readonly CanonicalDensityDefinition _density;
    private readonly Dictionary<string, V25SpeciesOwnershipSnapshot> _ownership = new(StringComparer.Ordinal);
    private readonly Dictionary<string, V25DensityLedgerEntry> _ledgerByTransaction = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _transactionByAward = new(StringComparer.Ordinal);

    public V25DensityEngine(CanonicalContentRegistry canonical, string? profileId = null)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _profileId = profileId ??= canonical.ActiveProfileId;
        _ = _canonical.Profile(profileId);
        _density = _canonical.Balance.Density;
        V25DensityMath.ValidateDensityDefinition(_density);
        foreach (var species in _canonical.SpeciesForProfile(profileId))
            _ownership.Add(species.Id, V25SpeciesOwnershipSnapshot.Locked(species.Id, species.PowerTier));
    }

    public string ProfileId => _profileId;
    public CanonicalDensityDefinition DensityDefinition => _density;
    public IReadOnlyDictionary<string, V25SpeciesOwnershipSnapshot> Ownership =>
        _ownership.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    public IReadOnlyList<V25DensityLedgerEntry> Ledger =>
        _ledgerByTransaction.Values.OrderBy(entry => entry.TransactionId, StringComparer.Ordinal).Select(entry => entry.WithCopiedProofs()).ToArray();

    public V25SpeciesOwnershipSnapshot GetOwnership(string speciesId)
    {
        RequireId(speciesId, nameof(speciesId));
        return _ownership.TryGetValue(speciesId, out var state)
            ? state
            : throw new InvalidDataException($"Species '{speciesId}' is unavailable in profile '{_profileId}'.");
    }

    /// <summary>Pure claim primitive for the later capture transaction; it never grants initial Density.</summary>
    public V25SpeciesOwnershipSnapshot ClaimSpecies(string speciesId)
    {
        var current = GetOwnership(speciesId);
        if (current.IsOwned) return current;
        var definition = SpeciesDefinition(speciesId);
        var claimed = V25SpeciesOwnershipSnapshot.OwnedAtZero(definition.Id, definition.PowerTier);
        claimed.Validate(definition, _density);
        _ownership[speciesId] = claimed;
        return claimed;
    }

    /// <param name="allowFirstOwnership">Only the capture transaction may set this; it stages
    /// Locked-&gt;Owned D0 together with the award and commits both at the same mutation point.</param>
    public V25DensityApplyResult Apply(V25DensityTransactionRequest request, bool allowFirstOwnership = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestIdentity(request);

        // Replay is checked before formula evaluation. The amount and source version in the old
        // receipt are authoritative even if the current canonical balance has since changed.
        if (_ledgerByTransaction.TryGetValue(request.TransactionId, out var previous))
        {
            if (!IdentityMatches(previous, request))
                throw new InvalidDataException($"Density transaction '{request.TransactionId}' conflicts with its committed payload.");
            var current = GetOwnership(previous.SpeciesId);
            return new(ToReceipt(previous), current, true);
        }

        if (_transactionByAward.TryGetValue(request.AwardId, out var existingTransaction))
            throw new InvalidDataException($"Density award '{request.AwardId}' was already committed by transaction '{existingTransaction}'.");

        var before = GetOwnership(request.SpeciesId);
        if (before.IsLocked && !allowFirstOwnership)
            throw new InvalidDataException($"Species '{request.SpeciesId}' is Locked; claim ownership before absorbing Density.");
        var definition = SpeciesDefinition(request.SpeciesId);
        if (before.IsLocked) before = V25SpeciesOwnershipSnapshot.OwnedAtZero(definition.Id, definition.PowerTier);
        before.Validate(definition, _density);
        ValidateSourceProvenance(request, before);

        var staged = StageTransition(before, request);
        staged.State.Validate(definition, _density);
        ValidateAccounting(request, staged);
        var entry = new V25DensityLedgerEntry(
            request.TransactionId,
            request.AwardId,
            request.SpeciesId,
            request.SourceId,
            request.SourceVersion,
            request.GrantedMicro,
            staged.AppliedCommitted,
            staged.AddedPending,
            staged.Discarded,
            request.Proofs.Select(proof => proof.GateIndex).OrderBy(key => key).ToArray(),
            staged.State.CommittedDensityMicro,
            staged.State.PendingDensityMicro,
            staged.State.PassedGateIndex,
            request.SourceLevel,
            request.PreTransactionSoulRank);

        // This is the sole mutation point. All calculations above used local values and the
        // immutable snapshot, so every rejected request leaves both state maps untouched.
        _ownership[request.SpeciesId] = staged.State;
        _ledgerByTransaction.Add(entry.TransactionId, entry);
        _transactionByAward.Add(entry.AwardId, entry.TransactionId);
        return new(ToReceipt(entry), staged.State, false);
    }

    public V25DensityEngineSnapshot Snapshot() => new(
        _ownership.Values.OrderBy(state => state.SpeciesId, StringComparer.Ordinal).ToArray(),
        _ledgerByTransaction.Values.OrderBy(entry => entry.TransactionId, StringComparer.Ordinal).Select(entry => entry.WithCopiedProofs()).ToArray());

    /// <summary>
    /// Restore validates all ownership and ledger rows into temporary maps before swapping them
    /// into the engine. It never recomputes a historical award from current balance definitions.
    /// </summary>
    public void Restore(V25DensityEngineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var stagedOwnership = new Dictionary<string, V25SpeciesOwnershipSnapshot>(StringComparer.Ordinal);
        foreach (var state in snapshot.Ownership ?? throw new InvalidDataException("Density ownership snapshot is missing."))
        {
            if (state is null) throw new InvalidDataException("Density ownership snapshot contains a null state.");
            if (!stagedOwnership.TryAdd(state.SpeciesId, state))
                throw new InvalidDataException($"Duplicate Density ownership for species '{state.SpeciesId}'.");
            state.Validate(SpeciesDefinition(state.SpeciesId), _density);
        }
        var expectedSpecies = _ownership.Keys.ToHashSet(StringComparer.Ordinal);
        if (!expectedSpecies.SetEquals(stagedOwnership.Keys))
            throw new InvalidDataException("Density ownership snapshot must contain exactly the profile species set.");

        var stagedLedger = new Dictionary<string, V25DensityLedgerEntry>(StringComparer.Ordinal);
        var stagedAwards = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Ledger ?? throw new InvalidDataException("Density ledger snapshot is missing."))
        {
            if (entry is null) throw new InvalidDataException("Density ledger snapshot contains a null entry.");
            ValidateLedgerEntry(entry, stagedOwnership, stagedLedger, stagedAwards);
            if (!stagedLedger.TryAdd(entry.TransactionId, entry.WithCopiedProofs()))
                throw new InvalidDataException($"Duplicate Density transaction '{entry.TransactionId}'.");
            if (!stagedAwards.TryAdd(entry.AwardId, entry.TransactionId))
                throw new InvalidDataException($"Duplicate Density award '{entry.AwardId}'.");
        }

        _ownership.Clear();
        foreach (var pair in stagedOwnership) _ownership.Add(pair.Key, pair.Value);
        _ledgerByTransaction.Clear();
        foreach (var pair in stagedLedger) _ledgerByTransaction.Add(pair.Key, pair.Value);
        _transactionByAward.Clear();
        foreach (var pair in stagedAwards) _transactionByAward.Add(pair.Key, pair.Value);
    }

    public static V25DensityEngine RestoreSnapshot(CanonicalContentRegistry canonical, V25DensityEngineSnapshot snapshot, string? profileId = null)
    {
        var engine = new V25DensityEngine(canonical, profileId);
        engine.Restore(snapshot);
        return engine;
    }

    private StagedTransition StageTransition(V25SpeciesOwnershipSnapshot before, V25DensityTransactionRequest request)
    {
        var proofKeys = before.ProofKeys.ToHashSet();
        foreach (var proof in request.Proofs) proofKeys.Add(proof.GateIndex);

        var committed = before.CommittedDensityMicro;
        var oldPending = before.PendingDensityMicro;
        var newGain = request.GrantedMicro;
        long appliedCommitted = 0;
        var passed = before.PassedGateIndex;
        long pendingAfter = 0;
        long addedPending = 0;
        long discarded = 0;

        for (var gateIndex = passed + 1; gateIndex <= _density.Gates.Count; gateIndex++)
        {
            var threshold = V25DensityMath.GateMicro(_density, gateIndex);
            var hasProof = proofKeys.Contains(gateIndex);
            if (hasProof && committed >= threshold)
            {
                passed = gateIndex;
                continue;
            }

            var required = committed < threshold ? V25DensityMath.CheckedSubtract(threshold, committed) : 0;
            var fromOld = Math.Min(oldPending, required);
            oldPending = V25DensityMath.CheckedSubtract(oldPending, fromOld);
            committed = V25DensityMath.CheckedAdd(committed, fromOld);
            required = V25DensityMath.CheckedSubtract(required, fromOld);

            var fromNew = Math.Min(newGain, required);
            newGain = V25DensityMath.CheckedSubtract(newGain, fromNew);
            committed = V25DensityMath.CheckedAdd(committed, fromNew);
            appliedCommitted = V25DensityMath.CheckedAdd(appliedCommitted, fromNew);

            // A missing proof stops at the first unpassed gate. Existing pending has priority
            // over the new award; only the new award contributes to AddedPending/Discarded.
            if (!hasProof || committed < threshold)
            {
                var pendingCap = V25DensityMath.ToMicroPoints(_density.MaxPending);
                pendingAfter = Math.Min(pendingCap, V25DensityMath.CheckedAdd(oldPending, newGain));
                var roomAfterOld = Math.Max(0, pendingCap - Math.Min(pendingCap, oldPending));
                addedPending = Math.Min(newGain, roomAfterOld);
                discarded = V25DensityMath.CheckedSubtract(newGain, addedPending);
                break;
            }

            passed = gateIndex;
        }

        if (passed == _density.Gates.Count)
        {
            committed = V25DensityMath.CheckedAdd(committed, V25DensityMath.CheckedAdd(oldPending, newGain));
            appliedCommitted = V25DensityMath.CheckedAdd(appliedCommitted, newGain);
            pendingAfter = 0;
            addedPending = 0;
            discarded = 0;
        }

        var state = new V25SpeciesOwnershipSnapshot(
            before.SpeciesId,
            before.PowerTier,
            V25SpeciesOwnershipStatus.Owned,
            committed,
            pendingAfter,
            passed,
            proofKeys.OrderBy(key => key).ToArray());
        return new(state, appliedCommitted, addedPending, discarded);
    }

    private void ValidateRequestIdentity(V25DensityTransactionRequest request)
    {
        RequireId(request.TransactionId, nameof(request.TransactionId));
        RequireId(request.AwardId, nameof(request.AwardId));
        RequireId(request.SpeciesId, nameof(request.SpeciesId));
        RequireId(request.SourceId, nameof(request.SourceId));
        RequireId(request.SourceVersion, nameof(request.SourceVersion));
        if (request.GrantedMicro < 0) throw new InvalidDataException("Density transaction gain cannot be negative.");
        if (request.GrantedMicro == 0 && (request.Proofs is null || request.Proofs.Count == 0))
            throw new InvalidDataException("A Density transaction must contain a positive gain or a proof.");
        if (request.Proofs is null) throw new InvalidDataException("Density transaction proofs are missing.");
        var duplicate = request.Proofs.GroupBy(proof => proof?.GateIndex).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException($"Density transaction contains duplicate proof gate '{duplicate.Key}'.");
        foreach (var proof in request.Proofs)
        {
            if (proof is null) throw new InvalidDataException("Density transaction contains a null proof.");
            if (proof.GateIndex is < 1 or > 8) throw new InvalidDataException("Density proof gate must be in [1..8].");
            if (proof.ArenaSeal)
            {
                if (proof.SourceRank != 0) throw new InvalidDataException("ArenaSeal proof must not carry a capture source rank.");
            }
            else if (proof.SourceRank is < 1 or > 9 || proof.SourceRank < V25DensityMath.MinimumProofRank(proof.GateIndex))
                throw new InvalidDataException($"Capture proof for gate {proof.GateIndex} does not meet its canonical source-rank requirement.");
        }
        _ = SpeciesDefinition(request.SpeciesId);
    }

    private void ValidateLedgerEntry(
        V25DensityLedgerEntry entry,
        IReadOnlyDictionary<string, V25SpeciesOwnershipSnapshot> ownership,
        IReadOnlyDictionary<string, V25DensityLedgerEntry> ledger,
        IReadOnlyDictionary<string, string> awards)
    {
        RequireId(entry.TransactionId, "density ledger transactionId");
        RequireId(entry.AwardId, "density ledger awardId");
        RequireId(entry.SpeciesId, "density ledger speciesId");
        RequireId(entry.SourceId, "density ledger sourceId");
        RequireId(entry.SourceVersion, "density ledger sourceVersion");
        if (entry.GrantedMicro < 0 || entry.AppliedCommitted < 0 || entry.AddedPending < 0 || entry.Discarded < 0)
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' has negative amounts.");
        var total = checked(entry.AppliedCommitted + entry.AddedPending + entry.Discarded);
        if (total != entry.GrantedMicro)
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' does not conserve its immutable grant amount.");
        if (!ownership.TryGetValue(entry.SpeciesId, out var state) || state.IsLocked)
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' references a species that is not owned.");
        var keys = entry.ProofKeys ?? throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' proofs are missing.");
        if (keys.Distinct().Count() != keys.Count || keys.Any(key => key is < 1 or > 8))
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' proof keys are invalid.");
        if (keys.Any(key => !state.ProofKeys.Contains(key)))
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' has a proof absent from the restored ownership state.");
        if (entry.ResultCommittedDensityMicro < 0 || entry.ResultPendingDensityMicro < 0 || entry.ResultPassedGateIndex is < 0 or > 8)
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' resulting state is invalid.");
        if (entry.ResultCommittedDensityMicro > state.CommittedDensityMicro || entry.ResultPassedGateIndex > state.PassedGateIndex)
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' is ahead of the restored ownership state.");
        var expected = new V25SpeciesOwnershipSnapshot(
            state.SpeciesId,
            state.PowerTier,
            state.Status,
            entry.ResultCommittedDensityMicro,
            entry.ResultPendingDensityMicro,
            entry.ResultPassedGateIndex,
            state.ProofKeys.Union(keys).OrderBy(key => key).ToArray());
        expected.Validate(SpeciesDefinition(entry.SpeciesId), _density);
        if (entry.SourceLevel is { } sourceLevel && sourceLevel is < 1 or > 90)
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' source level is invalid.");
        if (entry.SourceLevel is null != (entry.PreTransactionSoulRank is null))
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' source provenance is incomplete.");
        if (entry.PreTransactionSoulRank is { } preRank && preRank is < 1 or > 9)
            throw new InvalidDataException($"Density ledger entry '{entry.TransactionId}' pre-transaction rank is invalid.");
        if (ledger.ContainsKey(entry.TransactionId)) throw new InvalidDataException($"Duplicate Density transaction '{entry.TransactionId}'.");
        if (awards.ContainsKey(entry.AwardId)) throw new InvalidDataException($"Duplicate Density award '{entry.AwardId}'.");
    }

    private void ValidateAccounting(V25DensityTransactionRequest request, StagedTransition staged)
    {
        var total = checked(staged.AppliedCommitted + staged.AddedPending + staged.Discarded);
        if (total != request.GrantedMicro)
            throw new InvalidDataException("Density transaction accounting does not conserve the immutable grant amount.");
        if (staged.State.PendingDensityMicro > V25DensityMath.ToMicroPoints(_density.MaxPending))
            throw new InvalidDataException("Density pending state exceeds the canonical cap.");
    }

    private bool IdentityMatches(V25DensityLedgerEntry previous, V25DensityTransactionRequest request) =>
        string.Equals(previous.TransactionId, request.TransactionId, StringComparison.Ordinal) &&
        string.Equals(previous.AwardId, request.AwardId, StringComparison.Ordinal) &&
        string.Equals(previous.SpeciesId, request.SpeciesId, StringComparison.Ordinal) &&
        string.Equals(previous.SourceId, request.SourceId, StringComparison.Ordinal) &&
        string.Equals(previous.SourceVersion, request.SourceVersion, StringComparison.Ordinal) &&
        previous.GrantedMicro == request.GrantedMicro &&
        previous.SourceLevel == request.SourceLevel &&
        previous.PreTransactionSoulRank == request.PreTransactionSoulRank &&
        previous.ProofKeys.SequenceEqual(request.Proofs.Select(proof => proof.GateIndex).OrderBy(key => key));

    private void ValidateSourceProvenance(V25DensityTransactionRequest request, V25SpeciesOwnershipSnapshot before)
    {
        if (request.SourceLevel is null && request.PreTransactionSoulRank is null) return;
        if (request.SourceLevel is null || request.PreTransactionSoulRank is null)
            throw new InvalidDataException("Density source provenance must include both source level and pre-transaction SoulRank.");
        var sourceRank = V25DensityMath.SourceRankFromLevel(request.SourceLevel.Value);
        var actualSoulRank = before.SoulRank(_density);
        if (request.PreTransactionSoulRank.Value != actualSoulRank)
            throw new InvalidDataException($"Density relevance must use the pre-transaction SoulRank {actualSoulRank}, got {request.PreTransactionSoulRank.Value}.");
        var expectedGain = V25DensityMath.ComputeGainMicro(request.SourceLevel.Value, request.PreTransactionSoulRank.Value, _density);
        if (request.GrantedMicro != expectedGain)
            throw new InvalidDataException($"Density gain {request.GrantedMicro} does not match the canonical source formula result {expectedGain}.");
        _ = sourceRank;
    }

    private CanonicalSpeciesDefinition SpeciesDefinition(string speciesId) =>
        _canonical.SpeciesForProfile(_profileId).FirstOrDefault(species => string.Equals(species.Id, speciesId, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"Species '{speciesId}' is unavailable in profile '{_profileId}'.");

    private static V25DensityTransactionReceipt ToReceipt(V25DensityLedgerEntry entry) => new(
        entry.TransactionId,
        entry.AwardId,
        entry.SpeciesId,
        entry.GrantedMicro,
        entry.AppliedCommitted,
        entry.AddedPending,
        entry.Discarded,
        entry.ProofKeys.ToArray(),
        entry.ResultCommittedDensityMicro,
        entry.ResultPendingDensityMicro,
        entry.ResultPassedGateIndex);

    private static void RequireId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9_.:-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidDataException($"Invalid Density {label} '{value}'.");
    }

    private readonly record struct StagedTransition(
        V25SpeciesOwnershipSnapshot State,
        long AppliedCommitted,
        long AddedPending,
        long Discarded);
}
