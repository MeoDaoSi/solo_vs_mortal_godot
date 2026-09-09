using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Rules;
using SimVec2 = SoloVsMortal.Core.Math.Vec2;

namespace SoloVsMortal.Presentation;

/// <summary>Draws enemy intent from the committed simulation cast snapshot. It never drives hits.</summary>
public partial class BossTelegraphLayer : Node2D
{
    private IReadOnlyList<V25CastView> _casts = Array.Empty<V25CastView>();
    private IReadOnlyDictionary<string, V25ActorRuntimeSnapshot> _bosses = new Dictionary<string, V25ActorRuntimeSnapshot>();

    public void Refresh(IReadOnlyList<V25CastView> casts, IReadOnlyList<V25ActorRuntimeSnapshot> actors)
    {
        _casts = casts;
        _bosses = actors.Where(item => item.Kind == V25EntityKind.Monster && item.EncounterType == V25EncounterType.Boss)
            .ToDictionary(item => item.Uid, StringComparer.Ordinal);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var cast in _casts.Where(item => item.SourceKind == V25EntityKind.Monster && item.Phase == V25CastPhase.Windup))
        {
            if (!_bosses.TryGetValue(cast.CasterUid, out var boss)) continue;
            var origin = ToGodot(boss.Position);
            var windupTicks = checked((int)(cast.ReleaseTick - cast.AcceptedTick));
            var remaining = Math.Clamp((double)(cast.ReleaseTick - cast.AcceptedTick - cast.ElapsedTicks) / Math.Max(1, windupTicks), 0, 1);
            var pulse = (float)(0.38 + 0.22 * (1 - remaining));
            var fill = new Color(0.94f, 0.20f, 0.12f, pulse);
            var edge = new Color(1.0f, 0.78f, 0.22f, 0.95f);

            if (windupTicks == 42) DrawCone(origin, ToGodot(cast.Aim), 80, 90, fill, edge); // Pattern A: 700 ms.
            else if (windupTicks == 60 && cast.GroundPoint is { } ground)
            {
                var radius = boss.EncounterId == "boss.r09" ? 96f : 64f;
                DrawCircle(ToGodot(ground), radius, fill);
                DrawArc(ToGodot(ground), radius, 0, Mathf.Tau, 64, edge, 3);
            }
            else if (windupTicks == 54) // Pattern C: 900 ms summon warning.
            {
                DrawCircle(origin, 54, new Color(fill, 0.22f));
                DrawArc(origin, 54, 0, Mathf.Tau, 48, edge, 3);
                DrawArc(origin, 34, 0, Mathf.Tau, 40, edge, 2);
            }
        }
    }

    private void DrawCone(Vector2 origin, Vector2 aim, float range, float arcDegrees, Color fill, Color edge)
    {
        var direction = aim.Normalized();
        if (direction == Vector2.Zero) direction = Vector2.Right;
        var centerAngle = direction.Angle();
        var halfArc = Mathf.DegToRad(arcDegrees * 0.5f);
        const int segments = 24;
        var points = new Vector2[segments + 2];
        points[0] = origin;
        for (var index = 0; index <= segments; index++)
        {
            var angle = Mathf.Lerp(centerAngle - halfArc, centerAngle + halfArc, index / (float)segments);
            points[index + 1] = origin + Vector2.FromAngle(angle) * range;
        }
        DrawColoredPolygon(points, fill);
        DrawPolyline(points.Concat(new[] { origin }).ToArray(), edge, 3, true);
    }

    private static Vector2 ToGodot(SimVec2 value) => new((float)value.X, (float)value.Y);
}
