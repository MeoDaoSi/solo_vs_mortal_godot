using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Simulation.Systems;

public enum WorldInteractionFailure { NoInteractionNearby, CapabilityRequired }
public sealed record WorldInteractionResult(bool Success, string? ObjectId = null, string? InteractionId = null, WorldInteractionFailure? Failure = null, string? RequiredCapabilityId = null, string? ObjectDisplayName = null);
public sealed class WorldInteractionSystem
{
    private readonly HashSet<string> _destroyed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _destroyedByMap = new(StringComparer.Ordinal);
    private readonly EventBus _events; private MapDefinition _map; private readonly CapabilitySystem _capabilities;
    private V25TraversalSystem? _canonicalTraversal;
    public WorldInteractionSystem(EventBus events, MapDefinition map, CapabilitySystem capabilities) { _events = events; _map = map; _capabilities = capabilities; }
    public MapDefinition CurrentMap => _map;
    public void ConfigureCanonicalTraversal(V25TraversalSystem traversal) => _canonicalTraversal = traversal ?? throw new ArgumentNullException(nameof(traversal));

    public void SetMap(MapDefinition map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _destroyedByMap[_map.Id] = new HashSet<string>(_destroyed, StringComparer.Ordinal);
        _map = map;
        _destroyed.Clear();
        if (_destroyedByMap.TryGetValue(_map.Id, out var restored)) foreach (var id in restored) _destroyed.Add(id);
    }
    public bool IsDestroyed(string objectId) => _destroyed.Contains(objectId);
    public IReadOnlyList<string> DestroyedObjectIds() => _destroyed.Order(StringComparer.Ordinal).ToArray();
    public void Restore(IEnumerable<string>? objectIds) { _destroyed.Clear(); _destroyedByMap.Clear(); if (objectIds is not null) foreach (var id in objectIds.Where(_map.Objects.ContainsKey)) _destroyed.Add(id); }
    public IReadOnlyList<Rect> BlockingRects() => _map.Objects.Values.Where(item => item.Blocking && !IsDestroyed(item.Id) && item.Collision is not null).Select(item => new Rect(item.Position.X + item.Collision!.OffsetX, item.Position.Y + item.Collision.OffsetY, item.Collision.Width, item.Collision.Height)).ToArray();
    public WorldInteractionResult TryInteract(Vec2 position)
    {
        var item = _map.Objects.Values.Where(item => item.Interaction is not null && !IsDestroyed(item.Id) && position.DistanceTo(item.Position) <= item.Interaction.Radius).OrderBy(item => position.DistanceTo(item.Position)).FirstOrDefault();
        if (item?.Interaction is null) return new(false, Failure: WorldInteractionFailure.NoInteractionNearby);
        if (!_capabilities.Has(item.Interaction.RequiredCapabilityId)) return new(false, Failure: WorldInteractionFailure.CapabilityRequired, RequiredCapabilityId: item.Interaction.RequiredCapabilityId, ObjectDisplayName: item.Interaction.DisplayName);
        _destroyed.Add(item.Id); _events.Publish(new WorldObjectDestroyedEvent(item.Id, item.Interaction.Id, item.Interaction.DisplayName)); return new(true, item.Id, item.Interaction.Id);
    }

    public bool CanTraverseCanonical(V25TerrainTag terrain, double widthUnits, bool gateOpen = true, bool isCombatAction = false) =>
        _canonicalTraversal?.CanTraverse(terrain, widthUnits, gateOpen, isCombatAction) ?? false;

    public V25TraversalResult TryTraverseCanonical(V25TraversalRequest request, long tick) =>
        _canonicalTraversal?.TryTraverse(request, tick) ?? new(false, Vec2.Zero, V25TraversalFailure.UnknownTerrainProducer);
}
