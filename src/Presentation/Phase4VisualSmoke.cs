using Godot;
using SoloVsMortal.Application;

namespace SoloVsMortal.Presentation;

/// <summary>Representative visual parity probe: resolves authored map/object and character clip paths without owning runtime state.</summary>
public partial class Phase4VisualSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var app = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs")); app.Start();
            var missing = new List<string>();
            foreach (var obj in app.WorldObjects().Select(item => item.AssetId).Distinct(StringComparer.Ordinal)) { var path = ProjectSettings.GlobalizePath($"res://assets/{app.Asset(obj).File}"); if (!System.IO.File.Exists(path)) missing.Add(obj); }
            foreach (var (species, rank) in new[] { ("SKELETON", 1), ("GOLEM", 4), ("GOBLIN", 1) }) { var clip = app.MonsterAnimation(species, rank, "idle"); if (clip.Files.Count == 0 || !System.IO.File.Exists(ProjectSettings.GlobalizePath($"res://assets/{clip.Files[0]}"))) missing.Add($"{species}:idle"); }
            var player = app.PlayerAnimation("idle", "front", 1); if (!System.IO.File.Exists(ProjectSettings.GlobalizePath($"res://assets/{player.Asset.File}"))) missing.Add("PLAYER:idle");
            if (missing.Count > 0) throw new InvalidOperationException("Missing representative visual assets: " + string.Join(", ", missing));
            // Exercise the presentation boundary for the full summon/ally flow as well as clip resolution.
            var spawn = app.Snapshot().Player.Position; app.SpawnMonster("mon_skeleton", 1, spawn);
            for (var i = 0; i < 30 && app.Snapshot().WorldSouls.Count == 0; i++) { app.SetInput(SoloVsMortal.Core.Math.Vec2.Zero, true); app.Tick(0.2); }
            var acquired = app.AcquireNearbySouls().FirstOrDefault(); var banner = app.Snapshot().SoulBanners.FirstOrDefault();
            if (acquired is null || banner is null || !app.BindSoul(acquired, banner.Id).Success) throw new InvalidOperationException("Summon fixture setup failed.");
            if (!app.SummonSoul(acquired, banner.Id, spawn).Success || app.Snapshot().Allies.Count != 1) throw new InvalidOperationException("Ally snapshot parity failed.");
            if (!app.UnsummonSoul(acquired) || app.Snapshot().Allies.Count != 0) throw new InvalidOperationException("Unsummon parity failed.");
            GD.Print("PHASE4_VISUAL_SMOKE_PASS"); ProcessMode = ProcessModeEnum.Disabled;
        }
        catch (Exception exception) { GD.PushError($"PHASE4_VISUAL_SMOKE_FAIL: {exception.Message}"); ProcessMode = ProcessModeEnum.Disabled; }
    }
}
