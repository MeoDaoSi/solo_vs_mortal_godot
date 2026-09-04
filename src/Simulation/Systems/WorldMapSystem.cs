using SoloVsMortal.Data.Definitions;

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
    private string _currentRegionId;

    public WorldMapSystem(GameDefinitions definitions, IRegionTravelConditionEvaluator? conditions = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _conditions = conditions ?? new PermissiveRegionTravelConditionEvaluator();
        _currentRegionId = definitions.WorldMap.StarterRegionId;
    }

    public string CurrentRegionId => _currentRegionId;
    public RegionDefinition CurrentRegion => _definitions.WorldMap.Region(_currentRegionId);
    public MapDefinition CurrentMap => _definitions.Map(CurrentRegion.MapContentId);
    public IReadOnlyList<RegionDefinition> Regions => _definitions.WorldMap.Regions.Values.ToArray();

    public RegionTravelResult PreviewTravel(string regionId)
    {
        if (!_definitions.WorldMap.Regions.TryGetValue(regionId, out var region)) return new(false, Failure: RegionTravelFailure.RegionNotFound);
        if (!region.Available) return new(false, RegionId: region.Id, MapContentId: region.MapContentId, ScenePath: region.ScenePath, Failure: RegionTravelFailure.RegionUnavailable);
        if (!_conditions.CanTravel(region, out var failedConditionId)) return new(false, RegionId: region.Id, MapContentId: region.MapContentId, ScenePath: region.ScenePath, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: failedConditionId);
        if (!_definitions.Maps.TryGetValue(region.MapContentId, out var map)) return new(false, RegionId: region.Id, MapContentId: region.MapContentId, ScenePath: region.ScenePath, Failure: RegionTravelFailure.MapContentUnavailable);
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
}
