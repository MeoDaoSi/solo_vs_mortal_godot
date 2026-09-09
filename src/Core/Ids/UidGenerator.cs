using System.Globalization;
using System.Threading;

namespace SoloVsMortal.Core.Ids;

public sealed class UidGenerator
{
    private long _counter;

    public UidGenerator(long initialValue = 0)
    {
        _counter = initialValue;
    }

    public string Create(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        var value = Interlocked.Increment(ref _counter);
        if (value <= 0) throw new OverflowException("Runtime UID allocator exhausted int64.");
        return string.Concat(prefix, "_", value.ToString(CultureInfo.InvariantCulture));
    }

    public void Observe(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return;
        var separator = uid.LastIndexOf('_');
        if (separator < 0 || !long.TryParse(uid.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return;
        long current;
        do { current = Interlocked.Read(ref _counter); if (value <= current) return; }
        while (Interlocked.CompareExchange(ref _counter, value, current) != current);
    }

    public long NextValue => Interlocked.Read(ref _counter);

    public void RestoreNext(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        Interlocked.Exchange(ref _counter, value);
    }
}
