using System.Globalization;

namespace SoloVsMortal.Core.Rng;

/// <summary>PCG32 XSH-RR stream required by the canonical V2.5 deterministic ownership contract.</summary>
public sealed class Pcg32
{
    public const ulong Multiplier = 6364136223846793005UL;

    public Pcg32(ulong state, ulong increment)
    {
        if ((increment & 1UL) == 0) throw new ArgumentException("PCG32 increment must be odd.", nameof(increment));
        State = state;
        Increment = increment;
    }

    public ulong State { get; private set; }
    public ulong Increment { get; }

    public static Pcg32 FromSeed(ulong seed, uint streamId)
    {
        var stream = new Pcg32(0, ((ulong)streamId << 1) | 1UL);
        _ = stream.NextUInt32();
        stream.State = unchecked(stream.State + seed);
        _ = stream.NextUInt32();
        return stream;
    }

    public uint NextUInt32()
    {
        var oldState = State;
        State = unchecked(oldState * Multiplier + Increment);
        var xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        var rotation = (int)(oldState >> 59);
        return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
    }

    public double Next() => NextUInt32() / 4294967296.0;

    public bool Chance(double probability)
    {
        if (!double.IsFinite(probability) || probability < 0 || probability > 1)
            throw new ArgumentOutOfRangeException(nameof(probability));
        return Next() < probability;
    }

    public int Int(int minimum, int maximum)
    {
        if (maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
        var range = (ulong)((long)maximum - minimum + 1);
        // Rejection uses the full 2^32 output domain. Using uint.MaxValue as
        // the limit leaves one extra value in the remainder and biases ranges
        // such as Int(0, 1).
        const ulong outputDomain = 1UL << 32;
        var limit = outputDomain - (outputDomain % range);
        uint value;
        do value = NextUInt32(); while (value >= limit);
        return checked(minimum + (int)(value % range));
    }

    public Pcg32State Snapshot() => new(State.ToString(CultureInfo.InvariantCulture), Increment.ToString(CultureInfo.InvariantCulture));

    public void Restore(Pcg32State snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ulong.TryParse(snapshot.State, NumberStyles.None, CultureInfo.InvariantCulture, out var state)) throw new InvalidDataException("PCG32 state is not a decimal uint64.");
        if (!ulong.TryParse(snapshot.Increment, NumberStyles.None, CultureInfo.InvariantCulture, out var increment) || increment != Increment) throw new InvalidDataException("PCG32 increment does not match stream identity.");
        State = state;
    }
}

public sealed record Pcg32State(string State, string Increment);

public enum Pcg32StreamId : uint
{
    CombatCrit = 1,
    SoulDrop = 2,
    ItemDrop = 3,
    ItemFamily = 4,
}

/// <summary>Named streams keep an invalid action from consuming combat, drop, or item RNG state.</summary>
public sealed class Pcg32Streams
{
    public Pcg32Streams(ulong seed)
    {
        Seed = seed;
        CombatCrit = Pcg32.FromSeed(seed, (uint)Pcg32StreamId.CombatCrit);
        SoulDrop = Pcg32.FromSeed(seed, (uint)Pcg32StreamId.SoulDrop);
        ItemDrop = Pcg32.FromSeed(seed, (uint)Pcg32StreamId.ItemDrop);
        ItemFamily = Pcg32.FromSeed(seed, (uint)Pcg32StreamId.ItemFamily);
    }

    public ulong Seed { get; }
    public Pcg32 CombatCrit { get; }
    public Pcg32 SoulDrop { get; }
    public Pcg32 ItemDrop { get; }
    public Pcg32 ItemFamily { get; }

    public IReadOnlyDictionary<string, Pcg32State> Snapshot() => new Dictionary<string, Pcg32State>(StringComparer.Ordinal)
    {
        [nameof(CombatCrit)] = CombatCrit.Snapshot(),
        [nameof(SoulDrop)] = SoulDrop.Snapshot(),
        [nameof(ItemDrop)] = ItemDrop.Snapshot(),
        [nameof(ItemFamily)] = ItemFamily.Snapshot(),
    };

    /// <summary>Restores all named streams only after every identity/state has validated.</summary>
    public void Restore(IReadOnlyDictionary<string, Pcg32State> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var expected = new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [nameof(CombatCrit)] = ((ulong)Pcg32StreamId.CombatCrit << 1) | 1UL,
            [nameof(SoulDrop)] = ((ulong)Pcg32StreamId.SoulDrop << 1) | 1UL,
            [nameof(ItemDrop)] = ((ulong)Pcg32StreamId.ItemDrop << 1) | 1UL,
            [nameof(ItemFamily)] = ((ulong)Pcg32StreamId.ItemFamily << 1) | 1UL,
        };
        if (snapshot.Count != expected.Count || snapshot.Keys.Any(key => !expected.ContainsKey(key)))
            throw new InvalidDataException("PCG32 snapshot must contain exactly the four named streams.");
        foreach (var entry in snapshot)
        {
            if (entry.Value is null || !ulong.TryParse(entry.Value.State, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                || !ulong.TryParse(entry.Value.Increment, NumberStyles.None, CultureInfo.InvariantCulture, out var increment)
                || increment != expected[entry.Key])
                throw new InvalidDataException($"PCG32 snapshot stream '{entry.Key}' is invalid.");
        }
        CombatCrit.Restore(snapshot[nameof(CombatCrit)]);
        SoulDrop.Restore(snapshot[nameof(SoulDrop)]);
        ItemDrop.Restore(snapshot[nameof(ItemDrop)]);
        ItemFamily.Restore(snapshot[nameof(ItemFamily)]);
    }
}
