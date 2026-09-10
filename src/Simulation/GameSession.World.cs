using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.Systems.V25;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation;

public sealed partial class GameSession
{
    private readonly Dictionary<string, HashSet<int>> _visitedTiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<V25TerrainArea>> _terrainCache = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, IReadOnlyList<int>> VisitedTiles => _visitedTiles.ToDictionary(p => p.Key, p => (IReadOnlyList<int>)p.Value.Order().ToArray());
    public bool WasVisited(Vec2 position) => _visitedTiles.TryGetValue(CanonicalRegionId, out var tiles) && tiles.Contains((int)(position.Y / 32) * 256 + (int)(position.X / 32));
    public void RestoreVisitedTiles(IReadOnlyDictionary<string, IReadOnlyList<int>>? regions)
    {
        _visitedTiles.Clear();
        foreach (var pair in regions ?? new Dictionary<string, IReadOnlyList<int>>())
        {
            if (!CanonicalContent!.RegionsForProfile(CanonicalContent.ActiveProfileId).Any(r => r.Id == pair.Key) || pair.Value.Any(tile => tile is < 0 or >= 65536) || pair.Value.Distinct().Count() != pair.Value.Count) throw new InvalidDataException("Invalid visited tile map.");
            _visitedTiles.Add(pair.Key, pair.Value.ToHashSet());
        }
    }
    private void RevealCanonicalFog()
    {
        if (!_visitedTiles.TryGetValue(CanonicalRegionId, out var tiles)) _visitedTiles.Add(CanonicalRegionId, tiles = new());
        var x = (int)(Player.State.Position.X / 32); var y = (int)(Player.State.Position.Y / 32);
        for (var dy = -12; dy <= 12; dy++) for (var dx = -12; dx <= 12; dx++)
            if (dx*dx + dy*dy <= 144 && x+dx is >= 0 and < 256 && y+dy is >= 0 and < 256) tiles.Add((y+dy)*256+x+dx);
    }
    private readonly Dictionary<string, int> _hazardTicks = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> HazardTicks => _hazardTicks;
    public IReadOnlyList<V25TerrainArea> CanonicalTerrain
    {
        get
        {
            if (CanonicalContent is null) return Array.Empty<V25TerrainArea>();
            if (!_terrainCache.TryGetValue(CanonicalRegionId, out var areas)) _terrainCache.Add(CanonicalRegionId, areas = V25WorldLayout.Terrain(CanonicalContent.Content.Regions.First(r => r.Id == CanonicalRegionId), CanonicalContent));
            return areas;
        }
    }
    public bool IsNearCanonicalObject(string id, double radius = 48) => Player.State.Alive &&
        WorldMap.CurrentMap.Objects.TryGetValue(id, out var item) && Player.State.Position.DistanceTo(item.Position) <= radius;
    public bool CanUseCanonicalNpc(string id) => IsNearCanonicalObject($"npc.{id}") && !IsCanonicalCombatActive() && Traversal?.IsHazardActive != true;
    public void RestoreHazardTicks(IReadOnlyDictionary<string, int>? values)
    {
        _hazardTicks.Clear();
        foreach (var pair in values ?? new Dictionary<string, int>())
        {
            if (pair.Value is < 0 or >= 30 || pair.Key != Player.State.Uid && !Allies.AliveAllies().Any(a => a.Uid == pair.Key)) throw new InvalidDataException("Invalid environmental damage clock.");
            _hazardTicks.Add(pair.Key, pair.Value);
        }
    }
    private bool CanEnterCanonicalTerrain(Vec2 position) => CanonicalTerrain.Where(a => V25WorldLayout.Contains(a.Bounds, position))
        .All(a => Traversal?.CanTraverse(a.Terrain, a.WidthUnits, isCombatAction: Player.State.IsDodging) == true);
    private void TickCanonicalWorld()
    {
        if (CanonicalContent is null || Traversal is null) return;
        RevealCanonicalFog();
        var areas = CanonicalTerrain;
        var area = areas.FirstOrDefault(a => V25WorldLayout.Contains(a.Bounds, Player.State.Position));
        Traversal.ObserveTerrain(area?.Terrain ?? V25TerrainTag.Ground, area?.WidthUnits ?? 0, CanonicalRegionId, SimulationTick);
        Traversal.ObserveSafeAnchor(Player.State.Position, SimulationTick, area is null, area is not null, CanonicalRegionId);
        Player.EnvironmentMoveMultiplier = area?.Terrain == V25TerrainTag.FrostFloor && !Capabilities.Has("FrostStep") ? 0.75 : 1;
        bool Pulse(string uid, Vec2 position, bool player)
        {
            var hazard = areas.FirstOrDefault(a => (a.Terrain is V25TerrainTag.FireField or V25TerrainTag.ToxicPool) && V25WorldLayout.Contains(a.Bounds, position));
            var immune = hazard is not null && player && Capabilities.Has(hazard.Terrain == V25TerrainTag.FireField ? "FireWard" : "ToxicWard");
            if (hazard is null || immune) { _hazardTicks.Remove(uid); return false; }
            var count = _hazardTicks.GetValueOrDefault(uid) + 1;
            _hazardTicks[uid] = count % 30;
            return count >= 30;
        }
        if (Player.State.Alive && Pulse(Player.State.Uid, Player.State.Position, true)) Player.ApplyCanonicalDamage(0, Player.State.MaxHp * 0.03);
        foreach (var ally in Allies.AliveAllies().Where(a => a.Alive).ToArray())
            if (Pulse(ally.Uid, ally.Position, false)) Allies.ApplyEnvironmentalDamage(ally.Uid, ally.MaxHp * 0.03);
        foreach (var key in _hazardTicks.Keys.Where(k => k != Player.State.Uid && !Allies.AliveAllies().Any(a => a.Uid == k)).ToArray()) _hazardTicks.Remove(key);
    }
    public bool TryDiscoverCanonicalLandmark(string id)
    {
        if (!IsNearCanonicalObject(id) || !id.StartsWith("landmark.", StringComparison.Ordinal) || WorldLifecycleV25?.DiscoverLandmark(id) != true) return false;
        Sync?.RecordEvent(id, "DiscoverLandmark", id);
        RequireCanonicalDurableCommit(); return true;
    }
    public bool TryOpenCanonicalChest(string id)
    {
        if (!IsNearCanonicalObject(id) || !id.StartsWith("chest.", StringComparison.Ordinal) || InventoryV25 is null || WorldLifecycleV25 is null || WorldLifecycleV25.Snapshot().OpenedChestIds.Contains(id)) return false;
        var rank = CanonicalContent!.Content.Regions.First(r => r.Id == CanonicalRegionId).Rank;
        var items = CanonicalContent.EquipmentForProfile(CanonicalContent.ActiveProfileId).Where(i => i.Rank == rank).OrderBy(i => i.Id, StringComparer.Ordinal).ToArray();
        if (items.Length == 0 || InventoryV25.Coins > long.MaxValue - 20L * rank) return false;
        // Deterministic authored-region reward: opening never rerolls after reload.
        var chestIndex = id.EndsWith("camp", StringComparison.Ordinal) ? 0 : id.EndsWith("field", StringComparison.Ordinal) ? 1 : 2;
        if (!InventoryV25.AddItem(items[chestIndex % items.Length].Id, allowOverflow: true).Success) return false;
        InventoryV25.AddCoins(20L * rank);
        WorldLifecycleV25.OpenChest(id); RequireCanonicalDurableCommit(); return true;
    }
    public bool TryInteractCanonicalSecret(string id)
    {
        if (!IsNearCanonicalObject(id) || !id.StartsWith("secret.", StringComparison.Ordinal) || Sync is null || Possession.CanonicalSnapshot is not { } possessed) return false;
        var species = id["secret.".Length..];
        var source = CanonicalContent!.Content.SyncSources.FirstOrDefault(s => s.SpeciesId == species && s.Event == "SpeciesSecretInteract");
        if (source is null || possessed.SpeciesId != species || Sync.TotalMicro(species) < V25FixedPoint.RoundMicro(40) || source.RequiredCapability is { } cap && !Capabilities.Has(cap) || Sync.IsAwarded(species, source.SourceKey)) return false;
        Sync.RecordEvent(id, "SpeciesSecretInteract", id, species, currentSpeciesPossession: true);
        RequireCanonicalDurableCommit(); return true;
    }
}
