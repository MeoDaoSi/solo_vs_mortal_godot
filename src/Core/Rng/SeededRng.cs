namespace SoloVsMortal.Core.Rng;

/// <summary>Mulberry32 RNG with explicit uint32 behavior for deterministic simulation.</summary>
public sealed class SeededRng
{
    private uint _state;

    public SeededRng(uint seed)
    {
        _state = seed == 0 ? 1u : seed;
    }

    public static SeededRng FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SeededRng(HashString(text));
    }

    public double Next()
    {
        unchecked
        {
            var t = _state += 0x6d2b79f5u;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public double Range(double min, double max) => min + Next() * (max - min);

    public int Int(int min, int max)
    {
        if (max < min)
        {
            throw new ArgumentOutOfRangeException(nameof(max), "Maximum must be at least minimum.");
        }

        return (int)System.Math.Floor(Range(min, (double)max + 1));
    }

    public bool Chance(double probability) => Next() < probability;

    public T Pick<T>(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("Cannot pick from an empty collection.", nameof(items));
        }

        return items[Int(0, items.Count - 1)];
    }

    private static uint HashString(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var codeUnit in text)
            {
                hash ^= codeUnit;
                hash *= 16777619u;
            }

            return hash;
        }
    }
}
