using System.Collections.Generic;
using Godot;

namespace SoloVsMortal.Presentation;

public partial class VisualScaleLab : Node2D
{
    private static readonly (string Label, string AssetId)[] Representatives =
    {
        ("Player", "player.base.idle.s"),
        ("Goblin (missing)", "soul.goblin.rank01.enemy.idle.s"),
        ("Skeleton", "soul.skeleton.rank01.enemy.south"),
        ("Soul pickup", "soul.skeleton.rank01.pickup"),
        ("Chest", "world.ash_graves.chest"),
        ("Grave marker", "world.ash_graves.rock"),
        ("Pillar", "world.ash_graves.pillar"),
        ("Shrine", "world.ash_graves.shrine"),
        ("Portal", "world.ash_graves.portal"),
    };

    private const float GroundY = 210f;
    private const float StartX = 16f;
    private const float Spacing = 66f;

    private CanonicalAssetCatalog _assetCatalog = null!;
    private PresentationVisualMetrics _visualMetrics = null!;
    private readonly Dictionary<string, Texture2D> _textureCache = new();

    public override void _Ready()
    {
        base._Ready();
        _assetCatalog = CanonicalAssetCatalog.Load(ProjectSettings.GlobalizePath("res://"), ProjectSettings.GlobalizePath("res://data/v2.5/asset-catalog.v2.5.json"));
        _visualMetrics = PresentationVisualMetrics.Load(ProjectSettings.GlobalizePath("res://data/v2.5/presentation-visual-metrics.v2.5.json"), ProjectSettings.GlobalizePath("res://data/v2.5/world-scale-policy.v2.5.json"), _assetCatalog);
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 640, 360), new Color("#141419"));
        var baseline = _visualMetrics.Policy.PlayerBaselineVisibleHeightPx;
        DrawLine(new Vector2(0, GroundY), new Vector2(640, GroundY), new Color("#8a7f6a"), 1);
        DrawLine(new Vector2(0, GroundY - baseline), new Vector2(640, GroundY - baseline), new Color("#8a7f6a", 0.5f), 1);
        DrawString(ThemeDB.FallbackFont, new Vector2(8, GroundY - baseline - 4), $"VisualScaleLab · Player baseline {baseline}px · policy={_visualMetrics.Policy.Status}", fontSize: 9, modulate: new Color("#e8d8a8"));
        for (var i = 0; i < Representatives.Length; i++)
            DrawRepresentative(Representatives[i].Label, Representatives[i].AssetId, new Vector2(StartX + i * Spacing, GroundY), baseline);
    }

    private void DrawRepresentative(string label, string assetId, Vector2 ground, int baselineHeight)
    {
        var targetHeight = _visualMetrics.VisibleHeightFor(assetId, fallback: 0f);
        var targetScale = _visualMetrics.VisualScaleFor(assetId, fallback: 0f);
        var ratio = targetHeight > 0f ? targetHeight / baselineHeight : 0f;
        var currentHeight = _visualMetrics.TryGet(assetId, out var metric) ? metric.IntendedFootprint.Y : 0f;

        if (_assetCatalog.TryGet(assetId, out var asset))
        {
            if (!_textureCache.TryGetValue(assetId, out var texture))
            {
                texture = _assetCatalog.Texture(asset);
                _textureCache.Add(assetId, texture);
            }
            var frame = asset.Frames[0];
            var atlas = new AtlasTexture { Atlas = texture, Region = frame.Region, FilterClip = true };
            var size = new Vector2(asset.FrameSize.X * targetScale, asset.FrameSize.Y * targetScale);
            var origin = ground - asset.Pivot * targetScale;
            DrawGroundShadow(ground, size.X);
            DrawTextureRect(atlas, new Rect2(origin, size), false);
        }
        else
        {
            DrawMissingMarker(ground, targetHeight > 0f ? targetHeight : baselineHeight);
        }

        var accent = targetHeight > 0f ? new Color("#e8d8a8") : new Color("#d03a3a");
        DrawString(ThemeDB.FallbackFont, ground + new Vector2(-34, 24), label, fontSize: 9, modulate: accent);
        DrawString(ThemeDB.FallbackFont, ground + new Vector2(-34, 36), ratio > 0f ? $"ratio {ratio:0.00}" : "ratio --", fontSize: 9, modulate: accent);
        DrawString(ThemeDB.FallbackFont, ground + new Vector2(-34, 48), targetHeight > 0f ? $"target {targetHeight:0.#}px (cur {currentHeight:0.#}px)" : $"cur {currentHeight:0.#}px", fontSize: 9, modulate: accent);
    }

    private void DrawGroundShadow(Vector2 origin, float visualWidth)
    {
        var radius = Mathf.Max(5, visualWidth * 0.28f);
        DrawCircle(origin + new Vector2(0, 1), radius, new Color(0.035f, 0.028f, 0.045f, 0.34f));
    }

    private void DrawMissingMarker(Vector2 origin, float height)
    {
        var half = Mathf.Max(18, height * 0.5f);
        var box = new Rect2(origin.X - half * 0.5f, origin.Y - height, half, height);
        DrawRect(box, new Color(0.16f, 0.10f, 0.10f, 0.85f), false, 1);
        DrawLine(box.Position, box.End, new Color("#d03a3a"), 1);
        DrawLine(new Vector2(box.End.X, box.Position.Y), new Vector2(box.Position.X, box.End.Y), new Color("#d03a3a"), 1);
    }
}