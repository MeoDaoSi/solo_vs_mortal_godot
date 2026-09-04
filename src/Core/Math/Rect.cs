namespace SoloVsMortal.Core.Math;

public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) =>
        x >= X && y >= Y && x <= X + Width && y <= Y + Height;

    public bool Overlaps(Rect other) =>
        X < other.X + other.Width && X + Width > other.X &&
        Y < other.Y + other.Height && Y + Height > other.Y;

    public bool OverlapsCircle(double x, double y, double radius)
    {
        var closestX = System.Math.Max(X, System.Math.Min(x, X + Width));
        var closestY = System.Math.Max(Y, System.Math.Min(y, Y + Height));
        var deltaX = x - closestX;
        var deltaY = y - closestY;
        return System.Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < radius;
    }
}
