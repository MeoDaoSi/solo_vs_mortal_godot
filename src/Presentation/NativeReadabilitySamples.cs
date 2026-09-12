#if DEBUG
using System.Text.Json;
using Godot;

namespace SoloVsMortal.Presentation;

/// <summary>Opt-in static art review in the real Arena; no simulation or acceptance logic.</summary>
public partial class NativeReadabilitySamples : Node2D
{
    private readonly List<(Texture2D Texture, Vector2 Pivot, string Label)> _samples = new();

    public void LoadSamples()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        ZIndex = 100;
        var root = System.IO.Path.GetFullPath(ProjectSettings.GlobalizePath("res://assets/v2.5/readability-samples-v001"));
        var manifest = System.IO.Path.Combine(root, "samples.json");
        if (!System.IO.File.Exists(manifest)) throw new InvalidDataException($"Missing readability manifest: {manifest}");
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(manifest));
        foreach (var entry in doc.RootElement.GetProperty("samples").EnumerateArray())
        {
            var file = entry.GetProperty("file").GetString()!;
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, file));
            if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Readability sample path escapes its package.");
            var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(path))).ToLowerInvariant();
            if (actualHash != entry.GetProperty("sha256").GetString())
                throw new InvalidDataException("Readability sample hash differs from its manifest.");
            var image = Image.LoadFromFile(path);
            var pivot = entry.GetProperty("pivot");
            _samples.Add((ImageTexture.CreateFromImage(image), new Vector2(pivot[0].GetSingle(), pivot[1].GetSingle()), entry.GetProperty("label").GetString()!));
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawString(ThemeDB.FallbackFont, new Vector2(-260, -60), "P3.0 STATIC SAMPLES / 1:1 raster / user review pending", fontSize: 10);
        for (var i = 0; i < _samples.Count; i++)
        {
            var sample = _samples[i];
            var origin = new Vector2(-240 + i * 110, 120);
            DrawTexture(sample.Texture, origin - sample.Pivot);
            DrawString(ThemeDB.FallbackFont, origin + new Vector2(-30, 14), sample.Label, fontSize: 9);
        }
    }
}
#endif
