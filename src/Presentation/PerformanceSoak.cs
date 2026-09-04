using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Core.Math;
using System.Diagnostics;

namespace SoloVsMortal.Presentation;

/// <summary>Permanent performance regression probe for a representative 20-monster simulation load.</summary>
public partial class PerformanceSoak : Node
{
    public override void _Ready()
    {
        try
        {
            var app = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs"));
            app.Start();
            for (var i = 0; i < 20; i++) app.SpawnMonster(i % 2 == 0 ? "mon_skeleton" : "mon_goblin", 1, new Vec2(560 + i % 5 * 90, 180 + i / 5 * 90));
            var watch = Stopwatch.StartNew();
            const int frames = 54_000; // 15 minutes of deterministic 60 Hz representative load.
            for (var i = 0; i < frames; i++) { app.SetInput(Vec2.Zero, false); app.Tick(1.0 / 60.0); }
            watch.Stop();
            var averageMs = watch.Elapsed.TotalMilliseconds / frames;
            var snapshot = app.Snapshot();
            if (snapshot.World.Width <= 0 || snapshot.Monsters.Count > 20) throw new InvalidOperationException("Soak state invariant failed.");
            GD.Print($"COMBAT_CROWD_SOAK_PASS frames={frames} monsters={snapshot.Monsters.Count} avg_tick_ms={averageMs:0.000}");
        }
        catch (Exception exception) { GD.PushError($"COMBAT_CROWD_SOAK_FAIL: {exception.Message}"); }
        ProcessMode = ProcessModeEnum.Disabled;
    }
}
