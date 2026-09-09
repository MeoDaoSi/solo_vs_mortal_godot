using System.Collections.ObjectModel;
using System.Globalization;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.State.V25;

/// <summary>
/// The ownership state used by the pure V2.5 Density core. Density and pending values are
/// signed int64 micro-points (1,000,000 micro-points = 1 DensityPoint). A Locked species is
/// deliberately different from an Owned species at D0: Locked has no Soul level or rank and
/// cannot receive an absorb transaction until a later capture transaction claims it.
/// </summary>
public enum V25SpeciesOwnershipStatus
{
    Locked,
    Owned,
}

public sealed record V25SpeciesOwnershipSnapshot
{
    public V25SpeciesOwnershipSnapshot(
        string speciesId,
        int powerTier,
        V25SpeciesOwnershipStatus status,
        long committedDensityMicro,
        long pendingDensityMicro,
        int passedGateIndex,
        IReadOnlyList<int>? proofKeys = null)
    {
        SpeciesId = speciesId;
        PowerTier = powerTier;
        Status = status;
        CommittedDensityMicro = committedDensityMicro;
        PendingDensityMicro = pendingDensityMicro;
        PassedGateIndex = passedGateIndex;
        ProofKeys = new ReadOnlyCollection<int>((proofKeys ?? Array.Empty<int>()).ToArray());
    }

    public string SpeciesId { get; }
    public int PowerTier { get; }
    public V25SpeciesOwnershipStatus Status { get; }
    public long CommittedDensityMicro { get; }
    public long PendingDensityMicro { get; }
    public int PassedGateIndex { get; }
    public IReadOnlyList<int> ProofKeys { get; }
    public bool IsLocked => Status == V25SpeciesOwnershipStatus.Locked;
    public bool IsOwned => Status == V25SpeciesOwnershipStatus.Owned;

    /// <summary>Locked has no derived level; an owned D0 species starts at canonical Level 1.</summary>
    public int SoulLevel(CanonicalDensityDefinition density) =>
        IsLocked ? 0 : V25DensityMath.DeriveSoulLevel(CommittedDensityMicro, density);

    public int SoulRank(CanonicalDensityDefinition density)
    {
        var level = SoulLevel(density);
        return level == 0 ? 0 : V25ProgressionRules.RankFromLevel(level);
    }

    public static V25SpeciesOwnershipSnapshot Locked(string speciesId, int powerTier) =>
        new(speciesId, powerTier, V25SpeciesOwnershipStatus.Locked, 0, 0, 0);

    public static V25SpeciesOwnershipSnapshot OwnedAtZero(string speciesId, int powerTier) =>
        new(speciesId, powerTier, V25SpeciesOwnershipStatus.Owned, 0, 0, 0);

    public void Validate(CanonicalSpeciesDefinition definition, CanonicalDensityDefinition density)
    {
        if (definition is null) throw new InvalidDataException("Canonical species definition is missing.");
        if (!string.Equals(SpeciesId, definition.Id, StringComparison.Ordinal))
            throw new InvalidDataException($"Ownership species '{SpeciesId}' does not match canonical definition '{definition.Id}'.");
        if (PowerTier != definition.PowerTier)
            throw new InvalidDataException($"Ownership power tier for '{SpeciesId}' does not match canonical registry.");
        V25DensityMath.ValidateDensityDefinition(density);
        if (CommittedDensityMicro < 0 || PendingDensityMicro < 0)
            throw new InvalidDataException($"Density values for '{SpeciesId}' cannot be negative.");
        var pendingCapMicro = V25DensityMath.ToMicroPoints(density.MaxPending);
        if (PendingDensityMicro > pendingCapMicro)
            throw new InvalidDataException($"Pending Density for '{SpeciesId}' exceeds the canonical {density.MaxPending} point cap.");
        if (PassedGateIndex is < 0 or > 8 || PassedGateIndex > density.Gates.Count)
            throw new InvalidDataException($"PassedGateIndex for '{SpeciesId}' is outside the canonical gate range.");

        var proofs = ProofKeys ?? throw new InvalidDataException($"Proof keys for '{SpeciesId}' are missing.");
        if (proofs.Distinct().Count() != proofs.Count || proofs.Any(key => key is < 1 or > 8))
            throw new InvalidDataException($"Proof keys for '{SpeciesId}' must be unique gate IDs in [1..8].");
        for (var gate = 1; gate <= PassedGateIndex; gate++)
        {
            if (!proofs.Contains(gate))
                throw new InvalidDataException($"Passed gate {gate} for '{SpeciesId}' has no permanent proof key.");
            if (CommittedDensityMicro < V25DensityMath.GateMicro(density, gate))
                throw new InvalidDataException($"Passed gate {gate} for '{SpeciesId}' is above the committed Density threshold.");
        }

        if (Status == V25SpeciesOwnershipStatus.Locked)
        {
            if (CommittedDensityMicro != 0 || PendingDensityMicro != 0 || PassedGateIndex != 0 || proofs.Count != 0)
                throw new InvalidDataException($"Locked species '{SpeciesId}' must remain at empty D0 state.");
            return;
        }

        if (Status != V25SpeciesOwnershipStatus.Owned)
            throw new InvalidDataException($"Unknown ownership status for '{SpeciesId}'.");
        if (PassedGateIndex < density.Gates.Count &&
            CommittedDensityMicro > V25DensityMath.GateMicro(density, PassedGateIndex + 1))
            throw new InvalidDataException($"Unpassed Density gate {PassedGateIndex + 1} cannot have committed Density above its threshold.");
        _ = SoulLevel(density);
    }
}

/// <summary>Canonical capture proof evidence. ArenaSeal proofs use SourceRank 0.</summary>
public sealed record V25DensityProof(int GateIndex, int SourceRank, bool ArenaSeal = false)
{
    public static V25DensityProof Capture(int gateIndex, int sourceRank) => new(gateIndex, sourceRank, false);
    public static V25DensityProof ForArenaSeal(int gateIndex) => new(gateIndex, 0, true);
}

/// <summary>
/// A fully valued Density transaction. Callers that have source metadata should use FromSource
/// so the amount is calculated once and then held immutable in the ledger. Later balance changes
/// never recalculate this request or an existing ledger entry.
/// </summary>
public sealed record V25DensityTransactionRequest(
    string TransactionId,
    string AwardId,
    string SpeciesId,
    string SourceId,
    string SourceVersion,
    long GrantedMicro,
    IReadOnlyList<V25DensityProof> Proofs,
    int? SourceLevel = null,
    int? PreTransactionSoulRank = null)
{
    public static V25DensityTransactionRequest FromSource(
        string transactionId,
        string awardId,
        string speciesId,
        string sourceId,
        string sourceVersion,
        int sourceLevel,
        int preTransactionSoulRank,
        CanonicalDensityDefinition density,
        IReadOnlyList<V25DensityProof>? proofs = null) =>
        new(transactionId, awardId, speciesId, sourceId, sourceVersion,
            V25DensityMath.ComputeGainMicro(sourceLevel, preTransactionSoulRank, density),
            proofs ?? Array.Empty<V25DensityProof>(), sourceLevel, preTransactionSoulRank);
}

/// <summary>Immutable award ledger entry. All amount fields are int64 micro-points.</summary>
public sealed record V25DensityLedgerEntry(
    string TransactionId,
    string AwardId,
    string SpeciesId,
    string SourceId,
    string SourceVersion,
    long GrantedMicro,
    long AppliedCommitted,
    long AddedPending,
    long Discarded,
    IReadOnlyList<int> ProofKeys,
    long ResultCommittedDensityMicro,
    long ResultPendingDensityMicro,
    int ResultPassedGateIndex,
    int? SourceLevel = null,
    int? PreTransactionSoulRank = null)
{
    public V25DensityLedgerEntry WithCopiedProofs() => this with { ProofKeys = new ReadOnlyCollection<int>(ProofKeys.ToArray()) };
}

public sealed record V25DensityTransactionReceipt(
    string TransactionId,
    string AwardId,
    string SpeciesId,
    long GrantedMicro,
    long AppliedCommitted,
    long AddedPending,
    long Discarded,
    IReadOnlyList<int> ProofKeys,
    long CommittedDensityMicro,
    long PendingDensityMicro,
    int PassedGateIndex)
{
    public V25DensityTransactionReceipt WithCopiedProofs() => this with { ProofKeys = new ReadOnlyCollection<int>(ProofKeys.ToArray()) };
}

public sealed record V25DensityApplyResult(V25DensityTransactionReceipt Receipt, V25SpeciesOwnershipSnapshot Ownership, bool AlreadyApplied);

/// <summary>Typed state boundary for staged restore. The lists are copied by the engine.</summary>
public sealed record V25DensityEngineSnapshot(
    IReadOnlyList<V25SpeciesOwnershipSnapshot> Ownership,
    IReadOnlyList<V25DensityLedgerEntry> Ledger);

/// <summary>
/// Integer micro-point arithmetic and the canonical Density projection/gain formulas. Any
/// conversion that could overflow an int64 is rejected before state mutation.
/// </summary>
public static class V25DensityMath
{
    public const long MicroScale = V25FixedPoint.DensitySyncScale;

    public static void ValidateDensityDefinition(CanonicalDensityDefinition density)
    {
        if (density is null) throw new InvalidDataException("Canonical Density definition is missing.");
        if (!double.IsFinite(density.GainBase) || density.GainBase <= 0 ||
            !double.IsFinite(density.RankExponent) || density.RankExponent <= 0 ||
            !double.IsFinite(density.SourceLevelDivisor) || density.SourceLevelDivisor <= 0 ||
            density.MaxPending < 0 || density.Gates.Count != 8 || density.Gates.Any(gate => gate <= 0) ||
            density.Gates.Distinct().Count() != density.Gates.Count ||
            density.Gates.Zip(density.Gates.Skip(1)).Any(pair => pair.First >= pair.Second) ||
            density.ProjectionKnots.Count < 2 || density.Relevance.Count < 4)
            throw new InvalidDataException("Canonical Density definition is invalid.");
        for (var i = 0; i < density.ProjectionKnots.Count; i++)
        {
            var knot = density.ProjectionKnots[i];
            if (knot.Count != 2 || knot.Any(value => !double.IsFinite(value)))
                throw new InvalidDataException("Canonical Density projection knot is invalid.");
            if (i > 0 && (knot[0] <= density.ProjectionKnots[i - 1][0] || knot[1] < density.ProjectionKnots[i - 1][1]))
                throw new InvalidDataException("Canonical Density projection knots must be ordered and non-decreasing.");
        }
        if (density.ProjectionKnots[0][0] != 0 || density.ProjectionKnots[0][1] < 1)
            throw new InvalidDataException("Canonical Density projection must start at D0/Level1.");
    }

    public static long ToMicroPoints(long wholePoints)
    {
        if (wholePoints < 0) throw new ArgumentOutOfRangeException(nameof(wholePoints));
        return checked(wholePoints * MicroScale);
    }

    public static long ToMicroPoints(decimal points)
    {
        if (points < 0 || points > long.MaxValue / (decimal)MicroScale)
            throw new OverflowException("Density points do not fit in int64 micro-points.");
        var rounded = decimal.Round(points * MicroScale, 0, MidpointRounding.AwayFromZero);
        if (rounded > long.MaxValue || rounded < long.MinValue) throw new OverflowException("Density micro-points overflowed int64.");
        return checked((long)rounded);
    }

    public static long CheckedAdd(long left, long right) => checked(left + right);
    public static long CheckedSubtract(long left, long right) => checked(left - right);

    public static int DeriveSoulLevel(long committedDensityMicro, CanonicalDensityDefinition density)
    {
        ValidateDensityDefinition(density);
        if (committedDensityMicro < 0) throw new InvalidDataException("Committed Density cannot be negative.");
        var points = committedDensityMicro / (decimal)MicroScale;
        var knots = density.ProjectionKnots;
        decimal projected;
        if (points >= ToDecimal(knots[^1][0]))
        {
            projected = ToDecimal(knots[^1][1]);
        }
        else
        {
            var segment = 0;
            while (segment + 1 < knots.Count && points > ToDecimal(knots[segment + 1][0])) segment++;
            var start = knots[segment];
            var end = knots[segment + 1];
            var startDensity = ToDecimal(start[0]);
            var endDensity = ToDecimal(end[0]);
            var startLevel = ToDecimal(start[1]);
            var endLevel = ToDecimal(end[1]);
            projected = startLevel + (points - startDensity) * (endLevel - startLevel) / (endDensity - startDensity);
        }
        var level = decimal.ToInt32(decimal.Truncate(projected));
        return Math.Clamp(level, 1, 90);
    }

    public static int SourceRankFromLevel(int sourceLevel) => V25ProgressionRules.RankFromLevel(sourceLevel);

    public static int MinimumProofRank(int gateIndex)
    {
        if (gateIndex is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(gateIndex));
        return Math.Min(9, gateIndex + 2);
    }

    public static double RelevanceForRanks(int sourceRank, int preTransactionSoulRank, CanonicalDensityDefinition density)
    {
        ValidateDensityDefinition(density);
        if (sourceRank is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(sourceRank));
        if (preTransactionSoulRank is < 1 or > 9) throw new ArgumentOutOfRangeException(nameof(preTransactionSoulRank));
        var delta = sourceRank - preTransactionSoulRank;
        var index = delta >= 0 ? 0 : delta == -1 ? 1 : delta == -2 ? 2 : 3;
        var relevance = density.Relevance[index];
        if (!double.IsFinite(relevance) || relevance < 0) throw new InvalidDataException("Canonical Density relevance is invalid.");
        return relevance;
    }

    public static long ComputeGainMicro(int sourceLevel, int preTransactionSoulRank, CanonicalDensityDefinition density)
    {
        ValidateDensityDefinition(density);
        var sourceRank = SourceRankFromLevel(sourceLevel);
        var relevance = RelevanceForRanks(sourceRank, preTransactionSoulRank, density);
        var gain = density.GainBase * Math.Pow(sourceRank, density.RankExponent)
            * (1 + (sourceLevel - 1) / density.SourceLevelDivisor) * relevance;
        if (!double.IsFinite(gain) || gain < 0) throw new InvalidDataException("Canonical Density gain is invalid.");
        try
        {
            var scaled = checked((decimal)gain * MicroScale);
            var rounded = decimal.Round(scaled, 0, MidpointRounding.AwayFromZero);
            if (rounded > long.MaxValue) throw new OverflowException("Density gain overflowed int64 micro-points.");
            return checked((long)rounded);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Density gain overflowed int64 micro-points.", exception);
        }
    }

    public static long GateMicro(CanonicalDensityDefinition density, int gateIndex)
    {
        ValidateDensityDefinition(density);
        if (gateIndex is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(gateIndex));
        return ToMicroPoints(density.Gates[gateIndex - 1]);
    }

    private static decimal ToDecimal(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Canonical Density value is not finite.");
        try { return decimal.Parse(value.ToString("G17", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture); }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        { throw new InvalidDataException("Canonical Density value cannot be represented exactly enough for fixed-point projection.", exception); }
    }
}
