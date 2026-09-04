using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation;

namespace SoloVsMortal.Presentation;

public partial class Main : Node2D
{
    private readonly GameApplication _application = new(new GameSession());

    public override void _Ready()
    {
        _application.Start();
        GD.Print("SOLO VS MORTAL Godot C# project initialized.");
    }

    public override void _PhysicsProcess(double delta)
    {
        _application.Tick(delta);
    }
}
