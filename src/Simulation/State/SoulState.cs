using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Simulation.State;

public sealed record SoulOrigin(string MonsterUid, string MonsterDefinitionId, string SpeciesId, string DisplayName, int Rank, string RankKey, string RankDisplayName);
public sealed record WorldSoulState(string Id, string SoulNatureId, SoulOrigin Origin, Vec2 Position);

public sealed class OwnedSoulState
{
    internal OwnedSoulState(string id, string soulNatureId, SoulOrigin origin, int level = 1, int xp = 0) { Id = id; SoulNatureId = soulNatureId; Origin = origin; Level = level; Xp = xp; }
    public string Id { get; }
    public string SoulNatureId { get; }
    public SoulOrigin Origin { get; }
    public int Level { get; internal set; }
    public int Xp { get; internal set; }
}
