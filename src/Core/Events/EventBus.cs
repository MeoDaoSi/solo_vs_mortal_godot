namespace SoloVsMortal.Core.Events;

public sealed class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var handlers)) _handlers[type] = handlers = [];
        handlers.Add(handler);
        return new Subscription(() => Unsubscribe(handler));
    }

    public void Publish<TEvent>(TEvent message)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;
        foreach (var handler in handlers.ToArray()) ((Action<TEvent>)handler)(message);
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler)
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var handlers)) handlers.Remove(handler);
    }

    public int ListenerCount<TEvent>() => _handlers.TryGetValue(typeof(TEvent), out var handlers) ? handlers.Count : 0;
    public void Clear() => _handlers.Clear();

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
