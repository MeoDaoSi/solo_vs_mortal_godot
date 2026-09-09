using SoloVsMortal.Simulation.Systems.V25;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Core.Math;
using System.Collections.ObjectModel;

namespace SoloVsMortal.Simulation.Systems;

public enum RegionTravelFailure { RegionNotFound, RegionUnavailable, MapContentUnavailable, TravelConditionFailed }
public sealed record RegionTravelResult(
    bool Success,
    string? RegionId = null,
    string? MapContentId = null,
    string? ScenePath = null,
    MapSpawnDefinition? EntrySpawn = null,
    RegionTravelFailure? Failure = null,
    string? FailedConditionId = null);

/// <summary>Extension point for future quest/level/item/discovery travel gates.</summary>
public interface IRegionTravelConditionEvaluator
{
    bool CanTravel(RegionDefinition region, out string? failedConditionId);
}

/// <summary>V1 travel policy: authored availability is the only gate.</summary>
public sealed class PermissiveRegionTravelConditionEvaluator : IRegionTravelConditionEvaluator
{
    public bool CanTravel(RegionDefinition region, out string? failedConditionId)
    {
        failedConditionId = null;
        return true;
    }
}

/// <summary>Owns the active region and validates map transitions without depending on Godot.</summary>
public sealed class WorldMapSystem
{
    private readonly GameDefinitions _definitions;
    private readonly IRegionTravelConditionEvaluator _conditions;
    private readonly IReadOnlyDictionary<string, RegionDefinition> _regions;
    private readonly IReadOnlyDictionary<string, MapDefinition> _maps;
    private string _currentRegionId;

    public WorldMapSystem(GameDefinitions definitions, IRegionTravelConditionEvaluator? conditions = null, CanonicalContentRegistry? canonical = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _conditions = conditions ?? new PermissiveRegionTravelConditionEvaluator();
        if (canonical is null)
        {
            _regions = definitions.WorldMap.Regions;
            _maps = definitions.Maps;
            _currentRegionId = definitions.WorldMap.StarterRegionId;
        }
        else
        {
            var regions = canonical.RegionsForProfile(canonical.ActiveProfileId).ToDictionary(item => item.Id, ToRuntimeRegion, StringComparer.Ordinal);
            var maps = canonical.RegionsForProfile(canonical.ActiveProfileId).ToDictionary(item => item.Id, item => V25WorldLayout.Populate(BuildCanonicalMap(item, canonical.Content.LayoutBlueprint), item, canonical), StringComparer.Ordinal);
            _regions = new ReadOnlyDictionary<string, RegionDefinition>(regions);
            _maps = new ReadOnlyDictionary<string, MapDefinition>(maps);
            _currentRegionId = canonical.Content.NewGame.PositionRegion;
        }
    }

    public string CurrentRegionId => _currentRegionId;
    public RegionDefinition CurrentRegion => _regions.TryGetValue(_currentRegionId, out var region) ? region : throw new KeyNotFoundException($"Unknown region '{_currentRegionId}'.");
    public MapDefinition CurrentMap => _maps.TryGetValue(CurrentRegion.MapContentId, out var map) ? map : throw new KeyNotFoundException($"Unknown map '{CurrentRegion.MapContentId}'.");
    public IReadOnlyList<RegionDefinition> Regions => _regions.Values.ToArray();

    public RegionTravelResult PreviewTravel(string regionId)
    {
        if (!_regions.TryGetValue(regionId, out var region)) return new(false, Failure: RegionTravelFailure.RegionNotFound);
        if (!region.Available) return new(false, RegionId: region.Id, MapContentId: region.MapContentId, ScenePath: region.ScenePath, Failure: RegionTravelFailure.RegionUnavailable);
        if (!_conditions.CanTravel(region, out var failedConditionId)) return new(false, RegionId: region.Id, MapContentId: region.MapContentId, ScenePath: region.ScenePath, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: failedConditionId);
        if (!_maps.TryGetValue(region.MapContentId, out var map)) return new(false, RegionId: region.Id, MapContentId: region.MapContentId, ScenePath: region.ScenePath, Failure: RegionTravelFailure.MapContentUnavailable);
        MapSpawnDefinition entry;
        try { entry = map.Spawn(region.DefaultSpawnId); }
        catch (KeyNotFoundException) { return new(false, RegionId: region.Id, MapContentId: region.MapContentId, ScenePath: region.ScenePath, Failure: RegionTravelFailure.MapContentUnavailable); }
        return new(true, region.Id, region.MapContentId, region.ScenePath, entry);
    }

    public RegionTravelResult TravelTo(string regionId)
    {
        var result = PreviewTravel(regionId);
        if (result.Success && result.RegionId is { } resolvedRegionId) _currentRegionId = resolvedRegionId;
        return result;
    }

    private static RegionDefinition ToRuntimeRegion(CanonicalRegionDefinition region) => new(
        region.Id, region.Name, $"Rank {region.Rank}: {region.Name}", region.BossName, region.Id, null, null,
        new Vec2(((region.Rank - 1) % 3 - 1) * 0.6, ((region.Rank - 1) / 3 - 1) * 0.6), 0.12, region.DominantPaletteRamp,
        region.Rank == 1, true, new LevelRangeDefinition(region.Levels[0], region.Levels[1]), "shrine",
        region.PortalRequirements, new[] { "canonical", region.HazardType });

    private static MapDefinition BuildCanonicalMap(CanonicalRegionDefinition region, CanonicalLayoutBlueprintDefinition layout)
    {
        var width = checked(layout.ChunkTiles[0] * 2 * layout.TileSize);
        var height = checked(layout.ChunkTiles[1] * 2 * layout.TileSize);
        Vec2 At(IReadOnlyList<int> tile) => new(tile[0] * layout.TileSize, tile[1] * layout.TileSize);
        var spawns = new[]
        {
            new MapSpawnDefinition("shrine", At(layout.ShrineTile), "south"),
            new MapSpawnDefinition("entry", At(layout.EntryTile), "east"),
            new MapSpawnDefinition("portal", At(layout.ExitTile), "east"),
        };
        var zones = layout.ChunkGrid.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair =>
            new MapZoneDefinition(pair.Key.ToLowerInvariant(), pair.Key, new Rect(pair.Value[0] * layout.ChunkTiles[0] * layout.TileSize, pair.Value[1] * layout.ChunkTiles[1] * layout.TileSize, layout.ChunkTiles[0] * layout.TileSize, layout.ChunkTiles[1] * layout.TileSize), Array.Empty<string>()), StringComparer.Ordinal);
        return new MapDefinition(region.Id, region.Name, "canonical-region", width, height, layout.TileSize,
            new MapCameraDefinition(new Rect(0, 0, width, height), 1, null), null, Array.Empty<string>(), Array.Empty<string>(),
            new ReadOnlyDictionary<string, MapLayerDefinition>(new Dictionary<string, MapLayerDefinition>(StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, MapObjectDefinition>(new Dictionary<string, MapObjectDefinition>(StringComparer.Ordinal)),
            Array.Empty<string>(), spawns, Array.Empty<MapExitDefinition>(), new ReadOnlyDictionary<string, MapZoneDefinition>(zones),
            new ReadOnlyDictionary<string, MapPlacementRuleDefinition>(new Dictionary<string, MapPlacementRuleDefinition>(StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, double>(new Dictionary<string, double>(StringComparer.Ordinal)), 48);
    }
}
