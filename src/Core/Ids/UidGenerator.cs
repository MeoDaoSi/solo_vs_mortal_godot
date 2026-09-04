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
        return string.Concat(prefix, "_", value.ToString(CultureInfo.InvariantCulture));
    }
}
