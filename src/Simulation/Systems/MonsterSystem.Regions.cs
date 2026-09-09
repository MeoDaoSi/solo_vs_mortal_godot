using SoloVsMortal.Core.Math;
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
                row.EncounterType, row.RewardEligible, row.Statuses.Where(s => s.ExpireTick >= tick).ToArray(), row.Shields.Where(s => s.ExpireTick >= tick).ToArray(), tick, row.StaggerPoints, row.StaggerImmuneTicks, row.StaggerRecoveryTicks, row.BossPatternIndex, row.BossStoryInstanceId);
            monster.HomePosition = new Vec2(row.HomeX, row.HomeY); monster.IsReturning = row.IsReturning;
        }
        return true;
    }
    public void RestoreDormantRegions(IReadOnlyDictionary<string, IReadOnlyList<V25RegionMonster>>? regions, string activeRegion, long tick)
    {
        if (_monsters.Count != 0) throw new InvalidOperationException("Dormant restore requires the isolated, empty monster store.");
        _dormantRegions.Clear(); var uids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in regions ?? new Dictionary<string, IReadOnlyList<V25RegionMonster>>())
        {
            if (pair.Key == activeRegion || !_canonical!.RegionsForProfile(_canonical.ActiveProfileId).Any(r => r.Id == pair.Key)) throw new InvalidDataException("Invalid dormant region.");
            foreach (var row in pair.Value)
                if (!uids.Add(row.Uid) || !_canonical.Content.Encounters.Any(e => e.Id == row.EncounterId && e.RegionId == pair.Key) || !double.IsFinite(row.HomeX) || !double.IsFinite(row.HomeY)) throw new InvalidDataException("Invalid dormant encounter identity.");
            _dormantRegions.Add(pair.Key, pair.Value);
            ResumeRegion(pair.Key, tick); // Reuse actor/stat/status validation in the isolated restore.
            ParkRegion(pair.Key); _monsters.Clear();
        }
    }
}
