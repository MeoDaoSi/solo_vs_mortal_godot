using SoloVsMortal.Simulation.State;
using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Application;

public sealed record GameSnapshot(
    GameStage Stage,
    double ElapsedSeconds,
    int MonsterDefinitionCount,
    int SoulBannerDefinitionCount,
    int SoulNatureDefinitionCount,
    int CapabilityDefinitionCount,
    PlayerSnapshot Player,
    IReadOnlyList<MonsterSnapshot> Monsters);

public sealed record PlayerSnapshot(string Uid, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, int Level, int Rank);
public sealed record MonsterSnapshot(string Uid, string DefinitionId, string SpeciesId, Vec2 Position, double CurrentHp, double MaximumHp, bool Alive, MonsterAiState AiState, int Level, int Rank);
