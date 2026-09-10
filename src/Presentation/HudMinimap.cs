using Godot;
using SoloVsMortal.Application;

namespace SoloVsMortal.Presentation;

/// <summary>Presentation-only tactical overview. It reads snapshots and never owns world state.</summary>
public partial class HudMinimap : Control
{
    private Func<SoloVsMortal.Core.Math.Vec2, bool>? _visited;
    private GameSnapshot? _snapshot;
    private IReadOnlyList<WorldObjectSnapshot> _objects = Array.Empty<WorldObjectSnapshot>();

    public HudMinimap()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Refresh(GameSnapshot snapshot, IReadOnlyList<WorldObjectSnapshot> objects, Func<SoloVsMortal.Core.Math.Vec2, bool>? visited = null)
    {
        _visited = visited;
        _snapshot = snapshot;
        _objects = objects;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_snapshot is null) return;
        var center = Size / 2;
        var radius = Mathf.Min(Size.X, Size.Y) * 0.42f;
        DrawCircle(center, radius, new Color("#24170d"));
        DrawCircle(center, radius, new Color("#e9b94a"), false, 2);

        foreach (var item in _objects.Where(item => !item.Destroyed && (_visited?.Invoke(item.Position) ?? true)))
        {
            var p = Project(item.Position, center, radius);
            if (p.DistanceTo(center) > radius - 4) continue;
            var color = item.Type switch
            {
                "lake" => new Color("#36a6d7"),
                "road" => new Color("#d5a04e"),
                "building" or "ruinWall" => new Color("#a87a45"),
                "portal" => new Color("#74ddff"),
                _ => new Color("#6f9f45")
            };
            DrawCircle(p, item.Type == "road" ? 1.5f : 2.5f, color);
        }

        foreach (var soul in _snapshot.WorldSouls.Where(s => _visited?.Invoke(s.Position) ?? true))
        {
            var p = Project(soul.Position, center, radius);
            if (p.DistanceTo(center) <= radius - 4) DrawCircle(p, 2.5f, new Color("#73e7ff"));
        }

        var player = Project(_snapshot.Player.Position, center, radius);
        DrawCircle(player, 5, new Color("#f8dd6d"));
        DrawString(ThemeDB.FallbackFont, new Vector2(center.X - 4, 13), "N", HorizontalAlignment.Left, -1, 13, new Color("#ffe9a3"));
    }

    private Vector2 Project(SoloVsMortal.Core.Math.Vec2 point, Vector2 center, float radius)
    {
        var world = _snapshot!.World;
        return center + new Vector2((float)(point.X / world.Width - 0.5) * radius * 2, (float)(point.Y / world.Height - 0.5) * radius * 2);
    }
}
