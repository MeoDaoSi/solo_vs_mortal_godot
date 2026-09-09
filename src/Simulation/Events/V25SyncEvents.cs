namespace SoloVsMortal.Simulation.Events;

public sealed record V25SyncAwardCommittedEvent(string AwardId, string SpeciesId, string SourceKey, long AwardedMicroPoints);
