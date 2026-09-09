namespace SoloVsMortal.Simulation.State.V25;

public enum V25BannerUpgradeFailure
{
    AtProfileCap,
    PlayerRankTooLow,
    NotAtShrine,
    BossProofMissing,
    NoEligibleOwnedSpecies,
    InvalidRegion,
}

public sealed record V25BannerUpgradeResult(
    bool Success,
    int BannerRank,
    int? NextRank,
    string? GateId = null,
    string? ReceiptId = null,
    V25BannerUpgradeFailure? Failure = null,
    bool AlreadyApplied = false);

public sealed record V25BannerGateReceipt(
    string GateId,
    string GateVersion,
    string ReceiptId,
    int FromRank,
    int ToRank);
