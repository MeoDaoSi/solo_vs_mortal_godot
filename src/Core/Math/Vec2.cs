namespace SoloVsMortal.Core.Math;

public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public double Length => System.Math.Sqrt(X * X + Y * Y);

    public Vec2 Normalized()
    {
        var length = Length;
        return length == 0 ? Zero : new Vec2(X / length, Y / length);
    }

    public double DistanceTo(Vec2 other)
    {
        var deltaX = X - other.X;
        var deltaY = Y - other.Y;
        return System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    public Vec2 MoveTowards(Vec2 target, double maxStep)
    {
        var distance = DistanceTo(target);
        if (distance <= maxStep)
        {
            return target;
        }

        var direction = new Vec2(target.X - X, target.Y - Y).Normalized();
        return new Vec2(X + direction.X * maxStep, Y + direction.Y * maxStep);
    }

    public static double Clamp(double value, double min, double max) =>
        System.Math.Min(max, System.Math.Max(min, value));
}
