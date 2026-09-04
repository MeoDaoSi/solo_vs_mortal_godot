using Godot;
using SoloVsMortal.Application;

namespace SoloVsMortal.Presentation;

public partial class Phase3Smoke : Node
{
    private const string SmokePath = "user://phase3_smoke_save.json";
    public override void _Ready()
    {
        try
        {
            var app = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs")); app.Start(); app.AddPlayerXp(40);
            var expected = app.Snapshot(); GodotSaveStore.Write(SmokePath, app.CaptureSaveJson()); app.AddPlayerXp(80); app.RestoreSaveJson(GodotSaveStore.Read(SmokePath) ?? throw new InvalidDataException("Smoke save was not written.")); var restored = app.Snapshot();
            if (restored.Player.Level != expected.Player.Level || restored.Player.Xp != expected.Player.Xp || restored.Player.CurrentHp != expected.Player.CurrentHp) throw new InvalidOperationException("Save/load did not restore Player state.");
            GD.Print("PHASE3_SMOKE_PASS"); ProcessMode = ProcessModeEnum.Disabled;
        }
        catch (Exception exception) { GD.PushError($"PHASE3_SMOKE_FAIL: {exception}"); ProcessMode = ProcessModeEnum.Disabled; }
    }
}
