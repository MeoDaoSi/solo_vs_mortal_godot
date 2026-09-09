using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;

namespace SoloVsMortal.Simulation.Systems.V25;

public sealed record V25TerrainArea(string Id, Rect Bounds, V25TerrainTag Terrain, double WidthUnits = 0);
public sealed record V25RoadSegment(Vec2 Start, Vec2 End, double Width);

/// <summary>Deterministic spatial producers from the locked blueprint, independent of artwork.</summary>
public static class V25WorldLayout
{
    public static Vec2 At(CanonicalLayoutBlueprintDefinition l, IReadOnlyList<int> tile, string? chunk = null)
    {
        var offset = chunk is null ? Vec2.Zero : new Vec2(l.ChunkGrid[chunk][0] * l.ChunkTiles[0], l.ChunkGrid[chunk][1] * l.ChunkTiles[1]);
        return new Vec2((tile[0] + offset.X) * l.TileSize, (tile[1] + offset.Y) * l.TileSize);
    }
    public static bool Contains(Rect r, Vec2 p) => p.X >= r.X && p.Y >= r.Y && p.X < r.X + r.Width && p.Y < r.Y + r.Height;
    public static IReadOnlyList<V25RoadSegment> Roads(CanonicalLayoutBlueprintDefinition l)
    {
        var result = new List<V25RoadSegment>();
        foreach (var road in l.Roads.Append(l.ExitRoad))
            for (var i = 1; i < road.Count; i++) result.Add(new(At(l, road[i - 1]), At(l, road[i]), l.RoadWidthTiles * l.TileSize));
        foreach (var pocket in l.SecretPockets)
        {
            var nodes = new[] { pocket.StartTile }.Concat(pocket.DetourTiles).Append(pocket.EndTile).Append(pocket.InteractTile).ToArray();
            for (var i = 1; i < nodes.Length; i++) result.Add(new(At(l, nodes[i - 1], pocket.Chunk), At(l, nodes[i], pocket.Chunk), l.TileSize));
        }
        return result;
    }
    public static IReadOnlyList<V25TerrainArea> Terrain(CanonicalRegionDefinition r, CanonicalContentRegistry c)
    {
        var l = c.Content.LayoutBlueprint; var result = new List<V25TerrainArea>();
        if (r.HazardArea is { } h && Enum.TryParse<V25TerrainTag>(r.HazardType, out var tag))
        {
            var p = At(l, h.RectTiles, h.Chunk);
            result.Add(new($"hazard.{r.Id}", new Rect(p.X, p.Y, (h.RectTiles[2] - h.RectTiles[0]) * l.TileSize, (h.RectTiles[3] - h.RectTiles[1]) * l.TileSize), tag));
        }
        foreach (var pocket in l.SecretPockets.Where(p => p.SpeciesIndex < r.HomeSpecies.Count))
        {
            var species = c.Content.Species.First(s => s.Id == r.HomeSpecies[pocket.SpeciesIndex]);
            var p = At(l, pocket.StartTile, pocket.Chunk);
            var capability = c.Content.SyncSources.First(s => s.SpeciesId == species.Id && s.Event == "SpeciesSecretInteract").RequiredCapability;
            var terrain = capability switch { "UnderwaterBreathing" => V25TerrainTag.ShallowWater, "PhaseStep" => V25TerrainTag.PhasePassable, "Flight" => V25TerrainTag.Gap, "FireWard" => V25TerrainTag.FireField, "ToxicWard" => V25TerrainTag.ToxicPool, "StoneStep" => V25TerrainTag.CrumblingFloor, _ => V25TerrainTag.Ground };
            var width = terrain == V25TerrainTag.PhasePassable ? l.TileSize : pocket.ShortcutSpanTiles * l.TileSize;
            if (terrain != V25TerrainTag.Ground) result.Add(new($"shortcut.{species.Id}", new Rect(p.X - l.TileSize / 2.0, p.Y + l.TileSize, l.TileSize, width), terrain, width));
        }
        return result;
    }
    public static MapDefinition Populate(MapDefinition map, CanonicalRegionDefinition r, CanonicalContentRegistry c)
    {
        var l = c.Content.LayoutBlueprint; var objects = new Dictionary<string, MapObjectDefinition>(StringComparer.Ordinal);
        var layers = new Dictionary<string, MapLayerDefinition>(StringComparer.Ordinal) { ["objects"] = new("objects", "objects", 1, Array.Empty<MapTileDefinition>(), null) };
        void Add(string id, string type, Vec2 p, CollisionFootprintDefinition? collision = null) => objects.Add(id, new(id, $"world.{r.Id}.{type}", type, p, "objects", collision is not null, collision, null));
        Add($"shrine.{r.Id}", "shrine", At(l, l.ShrineTile));
        foreach (var npc in l.NpcTiles) Add($"npc.{npc.Key}", "npc", At(l, npc.Value));
        Add($"landmark.{r.Id}", "landmark", At(l, l.LandmarkTile, "Field"), new(-16, -16, 32, 32));
        foreach (var chest in l.ChestTiles) Add($"chest.{r.Id}.{chest.Key.ToLowerInvariant()}", "chest", At(l, chest.Value, chest.Key));
        Add($"portal.{r.Id}", "portal", At(l, l.ExitTile));
        Add($"entry.{r.Id}", "portal", At(l, l.EntryTile));
        Add($"arena.{r.Id}", "arena", At(l, l.Arena.EntryTile, l.Arena.Chunk));
        foreach (var pocket in l.SecretPockets.Where(p => p.SpeciesIndex < r.HomeSpecies.Count)) Add($"secret.{r.HomeSpecies[pocket.SpeciesIndex]}", "secret", At(l, pocket.InteractTile, pocket.Chunk));
        var t = l.OuterWallThicknessTiles * l.TileSize;
        Add("wall.n", "wall", Vec2.Zero, new(0, 0, map.Width, t));
        Add("wall.s", "wall", new(0, map.Height - t), new(0, 0, map.Width, t));
        Add("wall.w", "wall", new(0, t), new(0, 0, t, map.Height - 2*t));
        Add("wall.e", "wall", new(map.Width - t, t), new(0, 0, t, map.Height - 2*t));
        var roads = Roads(l); var terrain = Terrain(r, c); var index = 0;
        foreach (var chunk in l.ChunkGrid.Keys)
        for (var x = 0; x < 5; x++) for (var y = 0; y < 5; y++)
        {
            if ((x + 2*y) % 3 != 0) continue;
            var p = At(l, new[] {16 + 24*x, 16 + 24*y}, chunk);
            if (p.DistanceTo(At(l, l.ShrineTile)) <= l.SafeCampRadiusTiles*l.TileSize ||
                l.EncounterCenters.Any(e => p.DistanceTo(At(l, e, chunk)) <= 6*l.TileSize) ||
                roads.Any(road => DistanceToSegment(p, road.Start, road.End) <= 4*l.TileSize) ||
                terrain.Any(area => Contains(area.Bounds, p)) || objects.Values.Any(o => p.DistanceTo(o.Position) < 64)) continue;
            var type = new[] {"pillar", "rock", "tree_or_spire", "debris"}[index % 4];
            Add($"decor.{r.Id}.{index++}", type, p, type == "debris" ? null : new(-10, -10, 20, 20));
        }
        return map with { Objects = objects, Layers = layers, ObjectAssetIds = objects.Values.Select(o => o.AssetId).Distinct().ToArray() };
    }
    private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y; var length = dx*dx + dy*dy;
        var t = length == 0 ? 0 : Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy)/length, 0, 1);
        return p.DistanceTo(new Vec2(a.X+t*dx, a.Y+t*dy));
    }
}
