using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Application;

public sealed record GameSnapshot(
    GameStage Stage,
    double ElapsedSeconds,
    int MonsterDefinitionCount,
    int SoulBannerDefinitionCount,
    int SoulNatureDefinitionCount,
    int CapabilityDefinitionCount);
