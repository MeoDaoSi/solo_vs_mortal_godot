namespace SoloVsMortal.Simulation.State.V25;

public sealed record V25SyncAwardState(
    string AwardId,
    string SourceKey,
    string SourceVersion,
    long AwardedMicroPoints,
    long CommitSequence);

public sealed record V25SyncSourceProgressState(
    string SourceId,
    int Count,
    IReadOnlyList<string> EventIds);

public sealed record V25SpeciesSyncSnapshot(
    string SpeciesId,
    long AwardedMicroPoints,
    IReadOnlyList<V25SyncAwardState> Awards,
    IReadOnlyList<string> UnlockedMilestoneIds,
    IReadOnlyList<string> LegacyDedupKeys,
    IReadOnlyList<V25SyncSourceProgressState> Progress);
