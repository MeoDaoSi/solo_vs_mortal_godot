using System.Collections.ObjectModel;
using System.Text.Json;
using Godot;

namespace SoloVsMortal.Presentation;

/// <summary>
/// Presentation-only physical measurements. Canvas dimensions are deliberately
/// separate from the intended on-map footprint: transparent padding is not a
/// statement about the size of a grave, shrine, or character.
/// </summary>
public sealed record PresentationVisualMetric(
    string AssetId,
    Vector2I CanvasSize,
    Rect2I OpaqueBounds,
    Vector2 GroundPivot,
    Vector2 IntendedFootprint,
    Rect2? CollisionFootprint)
{
    public float UniformScale => Mathf.Min(IntendedFootprint.X / OpaqueBounds.Size.X, IntendedFootprint.Y / OpaqueBounds.Size.Y);
}

public sealed class PresentationVisualMetrics
{
    private readonly IReadOnlyDictionary<string, PresentationVisualMetric> _metrics;

    private PresentationVisualMetrics(int baseTileSize, IReadOnlyDictionary<string, PresentationVisualMetric> metrics)
    {
        BaseTileSize = baseTileSize;
        _metrics = metrics;
    }

    public int BaseTileSize { get; }
    public bool TryGet(string assetId, out PresentationVisualMetric metric) => _metrics.TryGetValue(assetId, out metric!);
    public float ScaleFor(string assetId, float fallback = 1f) => TryGet(assetId, out var metric) ? metric.UniformScale : fallback;

    public static PresentationVisualMetrics Load(string manifestPath, CanonicalAssetCatalog catalog)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        Require(root, "schemaVersion", JsonValueKind.Number).GetInt32().ThrowUnless(1, "Unsupported presentation visual metrics schema.");
        var baseTileSize = Require(root, "baseTileSize", JsonValueKind.Number).GetInt32();
        if (baseTileSize != 32) throw new InvalidDataException("Presentation visual metrics require BASE_TILE=32.");
        var values = Require(root, "assets", JsonValueKind.Array);
        var metrics = new Dictionary<string, PresentationVisualMetric>(StringComparer.Ordinal);
        foreach (var item in values.EnumerateArray())
        {
            var assetId = Require(item, "assetId", JsonValueKind.String).GetString() ?? throw new InvalidDataException("Metric assetId cannot be empty.");
            if (!catalog.TryGet(assetId, out var asset)) throw new InvalidDataException($"Presentation metric references absent canonical asset '{assetId}'.");
            var canvas = Pair(Require(item, "canvasSize", JsonValueKind.Array), "canvasSize", 1);
            if (canvas != asset.FrameSize) throw new InvalidDataException($"Metric canvas for '{assetId}' differs from its canonical frame size.");
            var opaque = Quad(Require(item, "opaqueBounds", JsonValueKind.Array), "opaqueBounds");
            if (opaque.Position.X < 0 || opaque.Position.Y < 0 || opaque.End.X > canvas.X || opaque.End.Y > canvas.Y)
                throw new InvalidDataException($"Metric opaque bounds for '{assetId}' are outside its canvas.");
            var pivot = Pair(Require(item, "groundPivot", JsonValueKind.Array), "groundPivot", 0);
            if (new Vector2(pivot.X, pivot.Y) != asset.Pivot) throw new InvalidDataException($"Metric pivot for '{assetId}' differs from canonical provenance.");
            var footprint = Pair(Require(item, "intendedFootprint", JsonValueKind.Array), "intendedFootprint", 1);
            Rect2? collision = null;
            if (item.TryGetProperty("collisionFootprint", out var collisionValue) && collisionValue.ValueKind != JsonValueKind.Null)
            {
                var rectangle = Quad(collisionValue, "collisionFootprint");
                collision = new Rect2(rectangle.Position, rectangle.Size);
            }
            var metric = new PresentationVisualMetric(assetId, canvas, opaque, new Vector2(pivot.X, pivot.Y), new Vector2(footprint.X, footprint.Y), collision);
            if (!metrics.TryAdd(assetId, metric)) throw new InvalidDataException($"Duplicate presentation metric '{assetId}'.");
        }
        return new PresentationVisualMetrics(baseTileSize, new ReadOnlyDictionary<string, PresentationVisualMetric>(metrics));
    }

    private static JsonElement Require(JsonElement item, string name, JsonValueKind kind) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : throw new InvalidDataException($"Presentation visual metrics requires '{name}'.");

    private static Vector2I Pair(JsonElement values, string name, int minimum)
    {
        if (values.GetArrayLength() != 2 || !values[0].TryGetInt32(out var x) || !values[1].TryGetInt32(out var y) || x < minimum || y < minimum)
            throw new InvalidDataException($"Presentation visual metrics '{name}' must be an integer pair >= {minimum}.");
        return new Vector2I(x, y);
    }

    private static Rect2I Quad(JsonElement values, string name)
    {
        if (values.GetArrayLength() != 4 || !values[0].TryGetInt32(out var x) || !values[1].TryGetInt32(out var y) || !values[2].TryGetInt32(out var width) || !values[3].TryGetInt32(out var height) || width <= 0 || height <= 0)
            throw new InvalidDataException($"Presentation visual metrics '{name}' must be [x, y, width, height].");
        return new Rect2I(x, y, width, height);
    }
}

internal static class PresentationVisualMetricExtensions
{
    internal static void ThrowUnless(this int value, int expected, string message)
    {
        if (value != expected) throw new InvalidDataException(message);
    }
}
