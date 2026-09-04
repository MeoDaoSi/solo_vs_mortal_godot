using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Application;

public enum SoulLinkState { Dormant, Manifested, Dispersed, Possessed }

/// <summary>Immutable presentation/query projection of the Player-Banner-Soul relationship.</summary>
public sealed record SoulLinkView(
    string SoulId,
    string DisplayName,
    string? BannerId,
    SoulLinkState State,
    int SoulCost,
    double Stability,
    bool CanSummon,
    bool CanPossess,
    bool CanDevour);
