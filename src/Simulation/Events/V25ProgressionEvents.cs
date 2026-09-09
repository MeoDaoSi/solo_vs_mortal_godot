namespace SoloVsMortal.Simulation.Events;

public sealed record V25BannerUpgradeCommittedEvent(string GateId, string ReceiptId, int FromRank, int ToRank);
public sealed record V25FactCommittedEvent(string FactId, string ProducerId, string SourceId);
public sealed record V25UniquePowerUnlockedEvent(string PowerId, string ReceiptId, string HostBossId, int PowerRank);
