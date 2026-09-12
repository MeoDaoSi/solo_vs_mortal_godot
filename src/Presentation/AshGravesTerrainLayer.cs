using Godot;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Presentation;

/// <summary>
/// Region/layout cache for immutable Ash Graves terrain. Each 16×16-tile child
/// records its draw commands only when this layer is rebuilt, not as actors move.
/// This intentionally has no input, collision, or gameplay ownership.
/// </summary>
public sealed partial class AshGravesTerrainLayer : Node2D
{
    private const int ChunkTiles = 16;
    private const int BaseTileSize = 32;
    private CanonicalAssetCatalog? _catalog;
    private CanonicalLayoutBlueprintDefinition? _layout;
    private IReadOnlyList<V25RoadSegment> _roads = Array.Empty<V25RoadSegment>();
    private int _mapTilesX;
    private int _mapTilesY;
    private string? _signature;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);

    public bool Rebuild(CanonicalAssetCatalog catalog, CanonicalLayoutBlueprintDefinition layout, IReadOnlyList<V25RoadSegment> roads, double worldWidth, double worldHeight)
    {
        if (layout.TileSize != BaseTileSize) throw new InvalidDataException("Ash Graves terrain cache requires BASE_TILE=32.");
        var mapTilesX = checked((int)Math.Ceiling(worldWidth / layout.TileSize));
        var mapTilesY = checked((int)Math.Ceiling(worldHeight / layout.TileSize));
        var roadSignature = string.Join(';', roads.Select(road => $"{road.Start.X},{road.Start.Y},{road.End.X},{road.End.Y},{road.Width}"));
        var signature = $"{layout.TileSize}:{mapTilesX}:{mapTilesY}:{roadSignature}";
        if (_signature == signature && IsVisibleInTree()) return false;

        _catalog = catalog;
        _layout = layout;
        _roads = roads.ToArray();
        _mapTilesX = mapTilesX;
        _mapTilesY = mapTilesY;
        _signature = signature;
        _textures.Clear();
        foreach (var child in GetChildren()) child.QueueFree();
        for (var y = 0; y < mapTilesY; y += ChunkTiles)
        for (var x = 0; x < mapTilesX; x += ChunkTiles)
            AddChild(new AshGravesTerrainChunk(this, x, y, Math.Min(ChunkTiles, mapTilesX - x), Math.Min(ChunkTiles, mapTilesY - y)));
        Visible = true;
        GD.Print($"ASH_GRAVES_STATIC_TERRAIN_CACHE chunks={GetChildCount()}; tileSize={layout.TileSize}; rebuild=region_or_layout_change");
        return true;
    }

    public void ClearCache()
    {
        _signature = null;
        _textures.Clear();
        foreach (var child in GetChildren()) child.QueueFree();
        Visible = false;
    }

    internal void DrawChunk(Node2D canvas, int startX, int startY, int countX, int countY)
    {
        if (_catalog is null || _layout is null) return;
        var chunkBounds = new Rect2(startX * BaseTileSize, startY * BaseTileSize, countX * BaseTileSize, countY * BaseTileSize);
        canvas.DrawRect(chunkBounds, new Color("#20212a"));
        for (var y = startY; y < startY + countY; y++)
        for (var x = startX; x < startX + countX; x++)
        {
            var tileRect = new Rect2(x * BaseTileSize, y * BaseTileSize, BaseTileSize, BaseTileSize);
            if (IsFocalClearing(x, y))
                canvas.DrawRect(tileRect, new Color("#29242d"));
            else if (IsAshTexturePatch(x, y) && TryAsset("ground", 15, out var ground))
                DrawTile(canvas, ground, tileRect, Colors.White);
        }

        bool Road(int x, int y) => IsRoadTile(x, y);
        bool Ruin(int x, int y) => IsRuinPatch(x, y);
        bool Wall(int x, int y) => x < _layout.OuterWallThicknessTiles || y < _layout.OuterWallThicknessTiles || x >= _mapTilesX - _layout.OuterWallThicknessTiles || y >= _mapTilesY - _layout.OuterWallThicknessTiles;
        for (var y = startY; y < startY + countY; y++)
        for (var x = startX; x < startX + countX; x++)
        {
            var tileRect = new Rect2(x * BaseTileSize, y * BaseTileSize, BaseTileSize, BaseTileSize);
            if (Ruin(x, y) && TryAsset("ruin", WangMask(Ruin, x, y), out var ruin)) DrawTile(canvas, ruin, tileRect, Colors.White);
            if (Road(x, y) && TryAsset("path", WangMask(Road, x, y), out var path)) DrawTile(canvas, path, tileRect, Colors.White);
            if (Wall(x, y) && TryAsset("wall", WangMask(Wall, x, y), out var wall)) DrawTile(canvas, wall, tileRect, Colors.White);
        }
        DrawCornerCap(canvas, "nw", 0, 0, startX, startY, countX, countY);
        DrawCornerCap(canvas, "ne", _mapTilesX - 1, 0, startX, startY, countX, countY);
        DrawCornerCap(canvas, "se", _mapTilesX - 1, _mapTilesY - 1, startX, startY, countX, countY);
        DrawCornerCap(canvas, "sw", 0, _mapTilesY - 1, startX, startY, countX, countY);
    }

    private void DrawCornerCap(Node2D canvas, string direction, int x, int y, int startX, int startY, int countX, int countY)
    {
        if (x < startX || x >= startX + countX || y < startY || y >= startY + countY || _catalog is null || !_catalog.TryGet($"world.ash_graves.wall.cap.{direction}", out var cap)) return;
        DrawTile(canvas, cap, new Rect2(x * BaseTileSize, y * BaseTileSize, BaseTileSize, BaseTileSize), Colors.White);
    }

    private bool TryAsset(string family, int mask, out CanonicalAssetEntry asset) => _catalog!.TryGet($"world.ash_graves.{family}.mask{mask:D2}", out asset!);

    private void DrawTile(Node2D canvas, CanonicalAssetEntry asset, Rect2 target, Color modulate)
    {
        if (!_textures.TryGetValue(asset.AssetId, out var texture))
        {
            var source = _catalog!.Texture(asset);
            texture = asset.Frames.Count == 1 ? new AtlasTexture { Atlas = source, Region = asset.Frames[0].Region, FilterClip = true } : source;
            _textures.Add(asset.AssetId, texture);
        }
        canvas.DrawTextureRect(texture, target, false, modulate);
    }

    private bool IsRoadTile(int tileX, int tileY)
    {
        var point = new Vector2((tileX + 0.5f) * BaseTileSize, (tileY + 0.5f) * BaseTileSize);
        return _roads.Any(road => DistanceToSegment(point, ToGodot(road.Start), ToGodot(road.End)) <= road.Width * 0.5);
    }

    private bool IsRuinPatch(int x, int y)
    {
        if (_layout is null || !_layout.ChunkGrid.TryGetValue("Ruins", out var chunk)) return false;
        var originX = chunk[0] * _layout.ChunkTiles[0];
        var originY = chunk[1] * _layout.ChunkTiles[1];
        var localX = x - originX;
        var localY = y - originY;
        if (localX is < 0 or >= 128 || localY is < 0 or >= 128) return false;
        // Three broken foundations create a recognisable ruined district while
        // leaving negative space and canonical paths visible.
        return In(localX, localY, 14, 20, 34, 24) || In(localX, localY, 72, 18, 38, 34) || In(localX, localY, 42, 74, 48, 30);
    }

    private bool IsFocalClearing(int x, int y)
    {
        if (_layout is null) return false;
        var shrine = _layout.ShrineTile;
        if (In(x, y, shrine[0] - 7, shrine[1] - 5, 15, 11)) return true;
        var landmarkChunk = _layout.ChunkGrid["Field"];
        var landmark = _layout.LandmarkTile;
        var landmarkX = landmarkChunk[0] * _layout.ChunkTiles[0] + landmark[0];
        var landmarkY = landmarkChunk[1] * _layout.ChunkTiles[1] + landmark[1];
        if (In(x, y, landmarkX - 8, landmarkY - 6, 17, 13)) return true;
        return In(x, y, _layout.ExitTile[0] - 8, _layout.ExitTile[1] - 6, 17, 13);
    }

    private static bool IsAshTexturePatch(int x, int y)
    {
        // Stable, sparse macro patches reduce the repeated 32px matrix while
        // retaining the generated ash material over the base colour.
        unchecked
        {
            var hash = x * 73856093 ^ y * 19349663;
            return (hash & 7) is 0 or 3;
        }
    }

    private static bool In(int x, int y, int left, int top, int width, int height) => x >= left && x < left + width && y >= top && y < top + height;
    private static int WangMask(Func<int, int, bool> occupied, int x, int y) => (occupied(x, y - 1) ? 1 : 0) | (occupied(x + 1, y) ? 2 : 0) | (occupied(x, y + 1) ? 4 : 0) | (occupied(x - 1, y) ? 8 : 0);
    private static Vector2 ToGodot(SoloVsMortal.Core.Math.Vec2 value) => new((float)value.X, (float)value.Y);
    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var delta = end - start;
        var lengthSquared = delta.LengthSquared();
        return point.DistanceTo(start + delta * (lengthSquared == 0 ? 0 : Mathf.Clamp((point - start).Dot(delta) / lengthSquared, 0, 1)));
    }
}

internal sealed partial class AshGravesTerrainChunk : Node2D
{
    private readonly AshGravesTerrainLayer _owner;
    private readonly int _startX;
    private readonly int _startY;
    private readonly int _countX;
    private readonly int _countY;

    public AshGravesTerrainChunk(AshGravesTerrainLayer owner, int startX, int startY, int countX, int countY)
    {
        _owner = owner;
        _startX = startX;
        _startY = startY;
        _countX = countX;
        _countY = countY;
        Name = $"TerrainChunk_{startX}_{startY}";
    }

    public override void _Draw() => _owner.DrawChunk(this, _startX, _startY, _countX, _countY);
}
