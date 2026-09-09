namespace SoloVsMortal.Simulation.State.V25;

public enum V25SoulRuntimeMode
{
    Ready,
    Summoned,
    Dispersed,
    Possessed,
}

public sealed record V25SummonStateSnapshot(
    string SpeciesId,
    V25SoulRuntimeMode Mode,
    string? ActiveAllyUid,
    long VitalityMicro,
    int RecoveryTicks,
    double HpRatio,
    double AttackCooldown);
