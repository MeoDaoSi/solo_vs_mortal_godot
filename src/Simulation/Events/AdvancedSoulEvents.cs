namespace SoloVsMortal.Simulation.Events;

public sealed record EssenceGainedEvent(string ProfileId, string DisplayName, int Amount, int Total);
public sealed record EssenceMilestoneReachedEvent(string ProfileId, string DisplayName, int RequiredPoints);
public sealed record BloodlineGainedEvent(string ProfileId, string DisplayName, int Amount, int Total);
public sealed record BloodlineMilestoneReachedEvent(string ProfileId, string DisplayName, int RequiredPoints);
public sealed record PossessionStartedEvent(string SoulId, string ProfileId, string DisplayName, double DurationSeconds);
public sealed record PossessionEndedEvent(string SoulId, string ProfileId, double CooldownSeconds);
public sealed record WorldObjectDestroyedEvent(string ObjectId, string InteractionId, string DisplayName);
