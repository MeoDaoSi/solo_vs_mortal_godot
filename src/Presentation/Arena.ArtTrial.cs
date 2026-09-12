using System.Text.Json;
using Godot;

namespace SoloVsMortal.Presentation;

public partial class Arena
{
    private bool IsSelectedArtTrial(string assetId)
    {
#if DEBUG
        return assetId == _selectedArtTrialAssetId;
#else
        return false;
#endif
    }

#if DEBUG
    private static (string? Selected, IReadOnlyDictionary<string, string> Held) LoadArtTrial(CanonicalAssetCatalog catalog)
    {
        var held = new Dictionary<string, string>(StringComparer.Ordinal);
        var path = ProjectSettings.GlobalizePath("res://data/v2.5/asset-trial.json");
        if (!File.Exists(path)) return (null, held);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var value = document.RootElement;
        var id = value.GetProperty("assetId").GetString();
        if (id is null || !catalog.TryGet(id, out var entry) || entry.Sha256 != value.GetProperty("sha256").GetString())
            throw new InvalidDataException("Art trial must point to the current imported asset hash.");
        foreach (var item in value.GetProperty("staticActors").EnumerateArray())
        {
            var heldId = item.GetProperty("assetId").GetString();
            if (heldId is null || !catalog.TryGet(heldId, out var pose) || pose.Sha256 != item.GetProperty("sha256").GetString()
                || pose.Role != "actor" || pose.Frames.Count != 1 || pose.Direction is not ("s" or "n" or "w" or "e"))
                throw new InvalidDataException("A held art pose requires one current actor frame and a real direction.");
            var parts = heldId.Split('.');
            held.Add(string.Join('.', parts.Take(parts.Length - 2)), heldId);
        }
        return (id, held);
    }

    // User-driven reload only. It changes visual resources, never Simulation or saves.
    private void ReloadArtTrial()
    {
        try
        {
            var catalog = CanonicalAssetCatalog.Load(ProjectSettings.GlobalizePath("res://"), ProjectSettings.GlobalizePath("res://data/v2.5/asset-catalog.v2.5.json"));
            var metrics = PresentationVisualMetrics.Load(ProjectSettings.GlobalizePath("res://data/v2.5/presentation-visual-metrics.v2.5.json"), ProjectSettings.GlobalizePath("res://data/v2.5/world-scale-policy.v2.5.json"), catalog);
            var trial = LoadArtTrial(catalog);
            // Decode all replacement images before touching the currently displayed scene.
            foreach (var entry in catalog.Assets.Values) _ = catalog.Texture(entry);
            _assetCatalog = catalog;
            _visualMetrics = metrics;
            _selectedArtTrialAssetId = trial.Selected;
            _staticArtTrialAssets = trial.Held;
            var oldPlayer = _playerSprite;
            _playerSprite = BuildPlayerSprite(_visualRank);
            _playerSprite.Position = SnapToPixel(_playerPresentationPosition);
            AddChild(_playerSprite);
            oldPlayer.QueueFree();
            foreach (var sprite in _monsterSprites.Values) sprite.QueueFree();
            foreach (var sprite in _allySprites.Values) sprite.QueueFree();
            _monsterSprites.Clear();
            _allySprites.Clear();
            _dyingMonsters.Clear();
            _mapTextures.Clear();
            _teleportCandidates.Clear();
            _teleportCycleIndex = 0;
            _ground = LoadCanonicalAssetTexture(_application.CurrentMapBackgroundAssetId() ?? "tiles.arena.ground");
            RebuildMapTextures();
            _ashGravesTerrain.ClearCache();
            RebuildStaticTerrain();
            RefreshSnapshot();
            Toast("Đã nạp lại hình ảnh — chờ bạn xem.");
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
            GD.PushWarning($"Art reload failed: {exception.Message}");
            Toast("Chưa nạp được ảnh mới. Kiểm tra PNG và metadata.");
        }
    }
#endif
}
