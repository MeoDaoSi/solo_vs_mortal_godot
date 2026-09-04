using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation;

namespace SoloVsMortal.Presentation;

public partial class Main : Node2D
{
    private GameApplication _application = null!;

    public override void _Ready()
    {
        var definitionsDirectory = ProjectSettings.GlobalizePath("res://data/configs");
        _application = GameApplication.CreateFromDefinitionsDirectory(definitionsDirectory);
        _application.Start();
        var snapshot = _application.Snapshot();
        GD.Print($"SOLO VS MORTAL initialized with {snapshot.MonsterDefinitionCount} monsters and {snapshot.SoulBannerDefinitionCount} Soul Banners.");
    }

    public override void _PhysicsProcess(double delta)
    {
        _application.Tick(delta);
    }
}
