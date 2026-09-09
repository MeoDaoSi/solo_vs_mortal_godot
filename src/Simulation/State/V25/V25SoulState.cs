using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Simulation.State.V25;

/// <summary>Persistent canonical world pickup metadata. Pickups have no timeout.</summary>
public sealed record V25WorldSoulPickup(
    string PickupId,
    string MonsterUid,
    string MonsterDefinitionId,
    string SpeciesId,
    string DisplayName,
    int Rank,
    string RankKey,
    string RankDisplayName,
    int SourceLevel,
    int SourceRank,
    Vec2 Position,
    string SoulNatureId,
    bool RewardEligible = true);

public sealed record V25SoulPityState(string SpeciesId, int Counter);

