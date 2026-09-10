using SoloVsMortal.Core.Math;

namespace SoloVsMortal.Simulation.Systems.V25;

public readonly record struct V25WorldBounds(double Width, double Height);
public sealed record V25RouteResult(bool Reachable, IReadOnlyList<Vec2> Points);

/// <summary>Collision radii are gameplay data derived from the locked species role, never from sprite pixels.</summary>
public static class V25ActorBodyRadii
{
    public const double Actor = 10;
    public const double LargeActor = 18;
    public const double HugeActor = 26;

    public static double ForSpeciesRole(string role) => role switch
    {
        "actor" => Actor,
        "large_actor" => LargeActor,
        "huge_actor" => HugeActor,
        _ => throw new InvalidDataException($"Unknown canonical species role '{role}'.")
    };
}

/// <summary>Deterministic collision-aware navigation shared by Player, Monster and Ally actors.</summary>
public static class V25Navigation
{
    private const double Grid = 16;
    private static readonly (int X, int Y)[] Neighbours =
    [
        (0, 1), (-1, 0), (1, 0), (0, -1),
        (-1, 1), (1, 1), (-1, -1), (1, -1),
    ];

    /// <summary>Searches radius 0/16/32/48 with 16 clockwise samples beginning at South.</summary>
    public static Vec2? FindNearestFree(Vec2 origin, V25WorldBounds bounds, IReadOnlyList<Rect> blockers, double bodyRadius, double maxSearchRadius = 48)
    {
        if (!IsFinite(origin) || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) || bounds.Width <= 0 || bounds.Height <= 0 || !double.IsFinite(bodyRadius) || bodyRadius < 0 || !double.IsFinite(maxSearchRadius) || maxSearchRadius < 0)
            return null;
        var maxRadius = Math.Min(48, maxSearchRadius);
        for (var ring = 0; ring <= (int)Math.Floor(maxRadius / Grid); ring++)
        {
            var distance = ring * Grid;
            if (ring == 0)
            {
                if (IsFree(origin, bounds, blockers, bodyRadius)) return origin;
                continue;
            }
            for (var sample = 0; sample < 16; sample++)
            {
                // South is angle +pi/2. Subtracting angle walks clockwise in screen/world XY.
                var angle = Math.PI / 2 - sample * 2 * Math.PI / 16;
                var candidate = new Vec2(origin.X + Math.Cos(angle) * distance, origin.Y + Math.Sin(angle) * distance);
                candidate = new Vec2(Math.Clamp(candidate.X, bodyRadius, Math.Max(bodyRadius, bounds.Width - bodyRadius)), Math.Clamp(candidate.Y, bodyRadius, Math.Max(bodyRadius, bounds.Height - bodyRadius)));
                if (IsFree(candidate, bounds, blockers, bodyRadius)) return candidate;
            }
        }
        return null;
    }

    /// <summary>A* over a 16-unit grid. Every segment is checked against the actual blockers.</summary>
    public static V25RouteResult FindRoute(Vec2 start, Vec2 goal, V25WorldBounds bounds, IReadOnlyList<Rect> blockers, double radius, int maxNodes = 4096)
    {
        if (!IsFinite(start) || !IsFinite(goal) || !double.IsFinite(radius) || radius < 0 || maxNodes < 1)
            return new(false, Array.Empty<Vec2>());
        var clampedStart = ClampToBounds(start, bounds, radius);
        var clampedGoal = ClampToBounds(goal, bounds, radius);
        if (!IsFree(clampedStart, bounds, blockers, radius) || !IsFree(clampedGoal, bounds, blockers, radius))
            return new(false, Array.Empty<Vec2>());
        if (SegmentFree(clampedStart, clampedGoal, bounds, blockers, radius))
            return new(true, new[] { clampedStart, clampedGoal });

        var startNode = ToNode(clampedStart); var goalNode = ToNode(clampedGoal);
        var open = new PriorityQueue<GridNode, (double Cost, int Sequence)>();
        var cameFrom = new Dictionary<GridNode, GridNode>();
        var gScore = new Dictionary<GridNode, double> { [startNode] = 0 };
        var sequence = 0;
        open.Enqueue(startNode, (Heuristic(startNode, goalNode), sequence++));
        var visited = 0;
        while (open.Count > 0 && visited++ < maxNodes)
        {
            var current = open.Dequeue();
            if (current == goalNode)
            {
                var nodes = new List<GridNode> { current };
                while (cameFrom.TryGetValue(nodes[^1], out var parent)) nodes.Add(parent);
                nodes.Reverse();
                var points = new List<Vec2> { clampedStart };
                foreach (var node in nodes.Skip(1)) points.Add(ToPoint(node));
                points[^1] = clampedGoal;
                return new(true, points);
            }
            foreach (var offset in Neighbours)
            {
                var next = new GridNode(current.X + offset.X, current.Y + offset.Y);
                var point = ToPoint(next);
                if (!IsFree(point, bounds, blockers, radius) || !SegmentFree(ToPoint(current), point, bounds, blockers, radius)) continue;
                var stepCost = offset.X != 0 && offset.Y != 0 ? 1.4142135623730951 : 1;
                var tentative = gScore[current] + stepCost;
                if (gScore.TryGetValue(next, out var old) && tentative >= old) continue;
                cameFrom[next] = current; gScore[next] = tentative;
                open.Enqueue(next, (tentative + Heuristic(next, goalNode), sequence++));
            }
        }
        return new(false, Array.Empty<Vec2>());
    }

    public static Vec2 NextStep(Vec2 start, Vec2 goal, double distance, V25WorldBounds bounds, IReadOnlyList<Rect> blockers, double radius)
    {
        if (!double.IsFinite(distance) || distance <= 0) return start;
        var route = FindRoute(start, goal, bounds, blockers, radius);
        if (!route.Reachable || route.Points.Count < 2) return start;
        var waypoint = route.Points[1];
        return start.MoveTowards(waypoint, distance);
    }

    public static bool IsFree(Vec2 point, V25WorldBounds bounds, IReadOnlyList<Rect> blockers, double radius)
    {
        if (!IsFinite(point) || !double.IsFinite(radius) || radius < 0 || point.X < radius || point.Y < radius || point.X > bounds.Width - radius || point.Y > bounds.Height - radius) return false;
        return !(blockers ?? Array.Empty<Rect>()).Any(rect => rect.OverlapsCircle(point.X, point.Y, radius));
    }

    public static bool SegmentFree(Vec2 start, Vec2 end, V25WorldBounds bounds, IReadOnlyList<Rect> blockers, double radius, double step = 4)
    {
        var distance = start.DistanceTo(end); var count = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(1, step)));
        for (var index = 0; index <= count; index++)
        {
            var t = index / (double)count; var point = new Vec2(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            if (!IsFree(point, bounds, blockers, radius)) return false;
        }
        return true;
    }

    private static GridNode ToNode(Vec2 point) => new((int)Math.Round(point.X / Grid, MidpointRounding.AwayFromZero), (int)Math.Round(point.Y / Grid, MidpointRounding.AwayFromZero));
    private static Vec2 ToPoint(GridNode node) => new(node.X * Grid, node.Y * Grid);
    private static double Heuristic(GridNode left, GridNode right) => Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
    private static Vec2 ClampToBounds(Vec2 point, V25WorldBounds bounds, double radius) => new(Math.Clamp(point.X, radius, Math.Max(radius, bounds.Width - radius)), Math.Clamp(point.Y, radius, Math.Max(radius, bounds.Height - radius)));
    private static bool IsFinite(Vec2 value) => double.IsFinite(value.X) && double.IsFinite(value.Y);
    private readonly record struct GridNode(int X, int Y);
}
