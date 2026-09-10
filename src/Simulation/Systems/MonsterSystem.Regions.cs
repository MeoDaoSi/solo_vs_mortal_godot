using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
namespace SoloVsMortal.Simulation.Systems;

public sealed record V25RegionMonster(string Uid, string DefinitionId, string SpeciesId, int Level, int Rank, int PowerTier,
    string Archetype, string CombatStyleId, string? SignatureSkillId, double X, double Y, double Hp, double MaxHp, bool Alive,
    string AiState, double AttackCooldown, string EncounterId, V25EncounterType EncounterType, bool RewardEligible,
    IReadOnlyList<V25StatusInstance> Statuses, IReadOnlyList<V25ShieldInstance> Shields, int StaggerPoints, int StaggerImmuneTicks,
    int StaggerRecoveryTicks, int BossPatternIndex, string? BossStoryInstanceId, double HomeX, double HomeY, bool IsReturning);

public sealed partial class MonsterSystem
{
    private readonly Dictionary<string, IReadOnlyList<V25RegionMonster>> _dormantRegions = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, IReadOnlyList<V25RegionMonster>> DormantRegions => _dormantRegions;
    public bool HasDormantActor(string uid) => _dormantRegions.Values.Any(rows => rows.Any(row => row.Uid == uid));
    public void ParkRegion(string regionId)
    {
        _dormantRegions[regionId] = _monsters.Values.Select(m => new V25RegionMonster(m.Uid, m.DefinitionId, m.SpeciesId, m.Level, m.Rank,
            m.PowerTier, m.Archetype, m.CombatStyleId, m.SignatureSkillId, m.Position.X, m.Position.Y, m.CurrentHp, m.MaxHp, m.Alive,
            m.AiState.ToString(), m.AttackCooldown, m.EncounterId, m.EncounterType, m.RewardEligible, m.Statuses.Snapshot(m.Uid), m.Shields.Snapshot(m.Uid),
            m.StaggerPoints, m.StaggerImmuneTicks, m.StaggerRecoveryTicks, m.BossPatternIndex, m.BossStoryInstanceId, m.HomePosition.X, m.HomePosition.Y, m.IsReturning)).ToArray();
    }
    public bool ResumeRegion(string regionId, long tick)
    {
        if (!_dormantRegions.Remove(regionId, out var rows)) return false;
        _monsters.Clear();
        foreach (var row in rows)
        {
            var monster = RestoreCanonicalRuntime(row.Uid, row.DefinitionId, row.SpeciesId, row.Level, row.Rank, row.PowerTier, row.Archetype,
                row.CombatStyleId, row.SignatureSkillId, row.X, row.Y, row.Hp, row.MaxHp, row.Alive, row.AiState, row.AttackCooldown, row.EncounterId,
                row.EncounterType, row.RewardEligible, row.Statuses.Where(s => s.ExpireTick > tick).ToArray(), row.Shields.Where(s => s.ExpireTick > tick).ToArray(), tick, row.StaggerPoints, row.StaggerImmuneTicks, row.StaggerRecoveryTicks, row.BossPatternIndex, row.BossStoryInstanceId);
            monster.HomePosition = new Vec2(row.HomeX, row.HomeY); monster.IsReturning = row.IsReturning;
        }
        return true;
    }
    /// <summary>Restores parked authored encounter lives into a staged session. Runtime boss adds
    /// remain active-session actors and are intentionally not accepted as dormant map rows.</summary>
    public void RestoreDormantRegions(IReadOnlyDictionary<string, IReadOnlyList<V25RegionMonster>>? regions, string activeRegion, long tick,
        IReadOnlySet<string>? activeActorIds = null)
    {
        if (_monsters.Count != 0) throw new InvalidOperationException("Dormant restore requires the isolated, empty monster store.");
        _dormantRegions.Clear();
        var uids = activeActorIds is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(activeActorIds, StringComparer.Ordinal);
        var regionsForProfile = _canonical!.RegionsForProfile(_canonical.ActiveProfileId).ToDictionary(region => region.Id, StringComparer.Ordinal);
        var speciesForProfile = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).ToDictionary(species => species.Id, StringComparer.Ordinal);
        var layout = _canonical.Content.LayoutBlueprint;
        var mapWidth = checked(layout.ChunkTiles[0] * 2 * layout.TileSize);
        var mapHeight = checked(layout.ChunkTiles[1] * 2 * layout.TileSize);
        foreach (var pair in regions ?? new Dictionary<string, IReadOnlyList<V25RegionMonster>>())
        {
            if (pair.Key == activeRegion || !regionsForProfile.ContainsKey(pair.Key) || pair.Value is null) throw new InvalidDataException("Invalid dormant region.");
            foreach (var row in pair.Value)
            {
                var encounter = _canonical.Content.Encounters.FirstOrDefault(item => item.Id == row.EncounterId && item.RegionId == pair.Key)
                    ?? throw new InvalidDataException("Dormant monster has no authored encounter in its saved region.");
                var species = speciesForProfile.GetValueOrDefault(row.SpeciesId)
                    ?? throw new InvalidDataException("Dormant monster species is unavailable in the saved profile.");
                if (!uids.Add(row.Uid) || row.DefinitionId != $"mon_{row.SpeciesId}" || encounter.SpeciesId != row.SpeciesId || encounter.Level != row.Level ||
                    !Enum.TryParse<V25EncounterType>(encounter.EncounterType, ignoreCase: false, out var encounterType) || row.EncounterType != encounterType ||
                    row.RewardEligible != encounter.RewardEligible || row.PowerTier != species.PowerTier || row.Archetype != species.Archetype ||
                    row.CombatStyleId != species.CombatStyleId || row.SignatureSkillId != species.SignatureSkillId ||
                    !IsInsideMap(row.X, row.Y, mapWidth, mapHeight) || !IsInsideMap(row.HomeX, row.HomeY, mapWidth, mapHeight))
                    throw new InvalidDataException("Dormant monster identity, canonical metadata, or map bounds are invalid.");
                var home = AuthoredEncounterPosition(encounter, layout);
                if (Math.Abs(row.HomeX - home.X) > 0.001 || Math.Abs(row.HomeY - home.Y) > 0.001)
                    throw new InvalidDataException("Dormant monster home position does not match its authored encounter.");
            }
            _dormantRegions.Add(pair.Key, pair.Value);
            ResumeRegion(pair.Key, tick); // Reuse actor/stat/status validation in the isolated restore.
            ParkRegion(pair.Key); _monsters.Clear();
        }
    }

    private static bool IsInsideMap(double x, double y, int width, int height) =>
        double.IsFinite(x) && double.IsFinite(y) && x >= 0 && y >= 0 && x <= width && y <= height;

    private static Vec2 AuthoredEncounterPosition(CanonicalEncounterDefinition encounter,
        CanonicalLayoutBlueprintDefinition layout)
    {
        if (!layout.ChunkGrid.TryGetValue(encounter.Chunk, out var chunk)) throw new InvalidDataException("Dormant monster encounter references an unknown chunk.");
        IReadOnlyList<int> local = encounter.CenterTile is { Count: >= 2 } center
            ? center
            : encounter.CenterIndex is { } index && index >= 0 && index < layout.EncounterCenters.Count
                ? new[] { layout.EncounterCenters[index][0] + (encounter.OffsetTiles?[0] ?? 0), layout.EncounterCenters[index][1] + (encounter.OffsetTiles?[1] ?? 0) }
                : throw new InvalidDataException("Dormant monster encounter has no authored position.");
        return new Vec2((chunk[0] * layout.ChunkTiles[0] + local[0]) * layout.TileSize,
            (chunk[1] * layout.ChunkTiles[1] + local[1]) * layout.TileSize);
    }
}
