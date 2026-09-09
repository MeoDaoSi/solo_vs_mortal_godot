using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems.V25;

public sealed record V25WorldLifecycleSnapshot(long WorldCycleId, IReadOnlyList<string> DefeatedEncounterIds,
    IReadOnlyList<string> ClearedGroupIds, IReadOnlyList<string> DiscoveredLandmarkIds, IReadOnlyList<string> OpenedChestIds);

/// <summary>Persistent authored encounter/discovery owner. Region streaming never recreates a
/// defeated life; only an explicit RestReset creates new normal/elite lives.</summary>
public sealed class V25WorldLifecycleSystem : IDisposable
{
    private readonly CanonicalContentRegistry _canonical;
    private readonly V25QuestSystem _quests;
    private readonly Func<string> _regionId;
    private readonly Func<string, MonsterState> _spawn;
    private readonly HashSet<string> _defeated = new(StringComparer.Ordinal);
    private readonly HashSet<string> _groups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _landmarks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _chests = new(StringComparer.Ordinal);
    private readonly IDisposable _defeatSubscription;
    private long _worldCycleId = 1;

    public V25WorldLifecycleSystem(CanonicalContentRegistry canonical, EventBus events, V25QuestSystem quests,
        Func<string> regionId, Func<string, MonsterState> spawn)
    {
        _canonical = canonical; _quests = quests; _regionId = regionId; _spawn = spawn;
        _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnDefeated);
    }

    public long WorldCycleId => _worldCycleId;
    public bool ShouldSpawn(string encounterId) => !_defeated.Contains(encounterId);

    public bool DiscoverLandmark(string landmarkId)
    {
        if (string.IsNullOrWhiteSpace(landmarkId) || !_landmarks.Add(landmarkId)) return false;
        _quests.RecordObjective("DiscoverLandmark", landmarkId, $"landmark.{landmarkId}");
        return true;
    }

    public bool OpenChest(string chestId)
    {
        if (string.IsNullOrWhiteSpace(chestId) || !_chests.Add(chestId)) return false;
        _quests.RecordObjective("OpenChest", chestId, $"chest.{chestId}");
        return true;
    }

    public bool RestReset(bool allowed)
    {
        if (!allowed) return false;
        var respawn = _defeated.Select(id => _canonical.Content.Encounters.First(item => item.Id == id))
            .Where(item => item.RegionId == _regionId() && item.EncounterType is "Normal" or "Elite")
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
        _worldCycleId = checked(_worldCycleId + 1);
        foreach (var encounter in respawn) _defeated.Remove(encounter.Id);
        foreach (var encounter in respawn) _spawn(encounter.Id);
        return true;
    }

    public V25WorldLifecycleSnapshot Snapshot() => new(_worldCycleId, _defeated.Order(StringComparer.Ordinal).ToArray(),
        _groups.Order(StringComparer.Ordinal).ToArray(), _landmarks.Order(StringComparer.Ordinal).ToArray(), _chests.Order(StringComparer.Ordinal).ToArray());

    public void Restore(V25WorldLifecycleSnapshot snapshot)
    {
        if (snapshot.WorldCycleId < 1) throw new InvalidDataException("World cycle must be positive.");
        var authored = _canonical.Content.Encounters.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var groupIds = _canonical.Content.Encounters.Select(item => item.GroupId).ToHashSet(StringComparer.Ordinal);
        Validate(snapshot.DefeatedEncounterIds, authored, "defeated encounter");
        Validate(snapshot.ClearedGroupIds, groupIds, "cleared group");
        var regions = _canonical.RegionsForProfile(_canonical.ActiveProfileId);
        Validate(snapshot.DiscoveredLandmarkIds, regions.Select(r => $"landmark.{r.Id}").ToHashSet(StringComparer.Ordinal), "landmark");
        Validate(snapshot.OpenedChestIds, regions.SelectMany(r => new[] { "camp", "field", "ruins" }.Select(chunk => $"chest.{r.Id}.{chunk}")).ToHashSet(StringComparer.Ordinal), "chest");
        _worldCycleId = snapshot.WorldCycleId;
        Replace(_defeated, snapshot.DefeatedEncounterIds); Replace(_groups, snapshot.ClearedGroupIds);
        Replace(_landmarks, snapshot.DiscoveredLandmarkIds); Replace(_chests, snapshot.OpenedChestIds);
    }

    private void OnDefeated(MonsterDefeatedEvent defeated)
    {
        if (!defeated.RewardEligible || defeated.EncounterType is V25EncounterType.Arena or V25EncounterType.Debug || defeated.EncounterId is null) return;
        var encounter = _canonical.Content.Encounters.FirstOrDefault(item => item.Id == defeated.EncounterId);
        if (encounter is null || !_defeated.Add(encounter.Id)) return;
        var group = _canonical.Content.Encounters.Where(item => item.GroupId == encounter.GroupId).ToArray();
        if (group.Length > 0 && group.All(item => _defeated.Contains(item.Id)) && _groups.Add(encounter.GroupId))
            _quests.RecordObjective("DefeatGroup", encounter.GroupId, $"group.{encounter.GroupId}");
    }

    private static void Validate(IReadOnlyList<string> values, IReadOnlySet<string> authored, string label)
    { if (values is null || values.Distinct(StringComparer.Ordinal).Count() != values.Count || values.Any(id => !authored.Contains(id))) throw new InvalidDataException($"Invalid {label} ledger."); }
    private static void ValidateFree(IReadOnlyList<string> values, string label)
    { if (values is null || values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Count) throw new InvalidDataException($"Invalid {label} ledger."); }
    private static void Replace(HashSet<string> target, IEnumerable<string> values) { target.Clear(); foreach (var value in values) target.Add(value); }
    public void Dispose() => _defeatSubscription.Dispose();
}
