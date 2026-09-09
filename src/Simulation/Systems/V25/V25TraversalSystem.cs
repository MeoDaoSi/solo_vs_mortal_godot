using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions.V25;

namespace SoloVsMortal.Simulation.Systems.V25;

/// <summary>Typed world tags consumed by the V2.5 traversal gate. A missing producer is never treated as passable.</summary>
public enum V25TerrainTag
{
    Ground,
    Gap,
    ShallowWater,
    FireField,
    ToxicPool,
    FrostFloor,
    CrumblingFloor,
    PhasePassable,
    Wall,
    LockedGate,
}

public enum V25TraversalFailure
{
    InvalidRequest,
    UnknownTerrainProducer,
    CapabilityRequired,
    GapTooWide,
    PhaseTooWide,
    LockedGate,
    WallBlocked,
    CombatSwimmingForbidden,
    HazardBlocked,
    NoReachableRoute,
    NoSafeAnchor,
}

public sealed record V25TraversalRequest(
    Vec2 Destination,
    V25TerrainTag Terrain,
    double WidthUnits = 0,
    bool GateOpen = true,
    bool IsCombatAction = false,
    string? GateStateId = null);

public sealed record V25TraversalResult(
    bool Success,
    Vec2 Position,
    V25TraversalFailure? Failure = null,
    string? RequiredCapabilityId = null,
    bool UsedGrace = false,
    bool Rescued = false,
    bool CombatAllowed = true);

public sealed record V25SafeAnchorSnapshot(
    double PositionX,
    double PositionY,
    long ConfirmedTick,
    string GateStateId,
    bool SafeWalkmesh);

public sealed record V25TraversalSnapshot(
    V25SafeAnchorSnapshot? SafeAnchor,
    int AnchorCandidateTicks,
    double CandidatePositionX,
    double CandidatePositionY,
    string? CandidateGateStateId,
    int FlightGraceTicks,
    int BreathingGraceTicks,
    V25TerrainTag? ActiveTerrain,
    double ActiveWidthUnits,
    string? ActiveGateStateId,
    long ActiveStartedTick,
    int CrumblingTicks);

/// <summary>
/// Owns the simulation side of canonical capability traversal. Presentation may draw shortcuts,
/// but only this service can authorize a typed terrain step, anchor, grace timer, or rescue.
/// </summary>
public sealed class V25TraversalSystem
{
    private const int TickRate = 60;
    private const int AnchorTicks = 30; // 500ms
    private const int FlightGraceDurationTicks = 120; // 2s
    private const int BreathingGraceDurationTicks = 180; // 3s
    private const double MaxFlightGap = 96;
    private const double MaxPhaseWidth = 32;
    private readonly CanonicalContentRegistry _canonical;
    private readonly CapabilitySystem _capabilities;
    private readonly PlayerSystem _player;
    private readonly Func<bool> _combatActive;
    private readonly Action<string>? _cancelUnreleasedCasts;
    private V25SafeAnchorSnapshot? _safeAnchor;
    private int _anchorCandidateTicks;
    private Vec2 _candidatePosition;
    private string? _candidateGateStateId;
    private int _flightGraceTicks;
    private int _breathingGraceTicks;
    private V25TerrainTag? _activeTerrain;
    private double _activeWidth;
    private string? _activeGateStateId;
    private long _activeStartedTick;
    private int _crumblingTicks;

    public V25TraversalSystem(CanonicalContentRegistry canonical, CapabilitySystem capabilities, PlayerSystem player,
        Func<bool> combatActive, Action<string>? cancelUnreleasedCasts = null)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _combatActive = combatActive ?? throw new ArgumentNullException(nameof(combatActive));
        _cancelUnreleasedCasts = cancelUnreleasedCasts;
        if (!_canonical.Content.Capabilities.Contains("Flight", StringComparer.Ordinal) ||
            !_canonical.Content.Capabilities.Contains("UnderwaterBreathing", StringComparer.Ordinal))
            throw new InvalidDataException("Canonical capability vocabulary is missing Flight or UnderwaterBreathing.");
    }

    public V25SafeAnchorSnapshot? SafeAnchor => _safeAnchor;
    public int FlightGraceTicks => _flightGraceTicks;
    public int BreathingGraceTicks => _breathingGraceTicks;
    public V25TerrainTag? ActiveTerrain => _activeTerrain;
    public bool IsSwimmingCombatAllowed => false;
    public bool IsHazardActive => _activeTerrain is V25TerrainTag.FireField or V25TerrainTag.ToxicPool or V25TerrainTag.FrostFloor or V25TerrainTag.CrumblingFloor;

    /// <summary>Canonical region importers use these stable producer contracts; presentation
    /// assets and object names never grant traversal implicitly.</summary>
    public IReadOnlyDictionary<V25TerrainTag, string> CanonicalTerrainTagProducers { get; } =
        new Dictionary<V25TerrainTag, string>
        {
            [V25TerrainTag.Gap] = "layout.secretPockets.shortcutSpan",
            [V25TerrainTag.ShallowWater] = "region.hazardArea:ShallowWater",
            [V25TerrainTag.FireField] = "region.hazardArea:FireField",
            [V25TerrainTag.ToxicPool] = "region.hazardArea:ToxicPool",
            [V25TerrainTag.FrostFloor] = "region.hazardArea:FrostFloor",
            [V25TerrainTag.CrumblingFloor] = "region.hazardArea:CrumblingFloor",
            [V25TerrainTag.PhasePassable] = "layout.secretPockets:PhaseStep",
            [V25TerrainTag.LockedGate] = "region.portalRequirements",
        };

    public bool ObserveSafeAnchor(Vec2 position, long tick, bool safeWalkmesh, bool inHazard, string gateStateId)
    {
        if (!IsFinite(position) || tick < 0 || !safeWalkmesh || inHazard || string.IsNullOrWhiteSpace(gateStateId) || !_player.IsPositionFree(position, PlayerSystem.CanonicalBodyRadius))
        {
            _anchorCandidateTicks = 0;
            _candidatePosition = Vec2.Zero;
            _candidateGateStateId = null;
            return false;
        }
        if (_anchorCandidateTicks == 0 || _candidatePosition.DistanceTo(position) > 1 || !string.Equals(_candidateGateStateId, gateStateId, StringComparison.Ordinal))
        {
            _anchorCandidateTicks = 0;
            _candidatePosition = position;
            _candidateGateStateId = gateStateId;
        }
        _anchorCandidateTicks = Math.Min(AnchorTicks, _anchorCandidateTicks + 1);
        if (_anchorCandidateTicks < AnchorTicks) return false;
        _safeAnchor = new V25SafeAnchorSnapshot(position.X, position.Y, tick, gateStateId, true);
        return true;
    }

    public bool CanTraverse(V25TerrainTag terrain, double widthUnits, bool gateOpen = true, bool isCombatAction = false)
    {
        return Evaluate(terrain, widthUnits, gateOpen, isCombatAction).Allowed;
    }

    public V25TraversalResult TryTraverse(V25TraversalRequest request, long tick)
    {
        if (!IsFinite(request.Destination) || !double.IsFinite(request.WidthUnits) || request.WidthUnits < 0 || tick < 0)
            return new(false, _player.State.Position, V25TraversalFailure.InvalidRequest);
        if (request.Terrain == V25TerrainTag.LockedGate || !request.GateOpen)
            return new(false, _player.State.Position, V25TraversalFailure.LockedGate, CombatAllowed: true);
        var evaluation = Evaluate(request.Terrain, request.WidthUnits, request.GateOpen, request.IsCombatAction);
        if (!evaluation.Allowed)
            return new(false, _player.State.Position, evaluation.Failure, evaluation.RequiredCapability, false, false, evaluation.CombatAllowed);
        if (!_player.IsSegmentFree(_player.State.Position, request.Destination, PlayerSystem.CanonicalBodyRadius, 2))
            return new(false, _player.State.Position, V25TraversalFailure.NoReachableRoute, CombatAllowed: evaluation.CombatAllowed);
        _player.SetPosition(request.Destination);
        _activeTerrain = request.Terrain;
        _activeWidth = request.WidthUnits;
        _activeGateStateId = request.GateStateId;
        _activeStartedTick = tick;
        if (request.Terrain != V25TerrainTag.CrumblingFloor) _crumblingTicks = 0;
        return new(true, _player.State.Position, UsedGrace: evaluation.UsedGrace, CombatAllowed: evaluation.CombatAllowed);
    }

    /// <summary>Begins a typed shortcut span; a missing producer or unopened gate fails closed.</summary>
    public bool BeginShortcut(Vec2 destination, V25TerrainTag terrain, double widthUnits, bool gateOpen, string gateStateId, long tick, bool isCombatAction = false)
    {
        var result = TryTraverse(new V25TraversalRequest(destination, terrain, widthUnits, gateOpen, isCombatAction, gateStateId), tick);
        return result.Success;
    }

    public void ObserveTerrain(V25TerrainTag terrain, double width, string gateStateId, long tick)
    {
        if (_activeTerrain != terrain) { _activeStartedTick = tick; _crumblingTicks = 0; }
        _activeTerrain = terrain; _activeWidth = width; _activeGateStateId = gateStateId;
        if (terrain == V25TerrainTag.Ground) { _flightGraceTicks = 0; _breathingGraceTicks = 0; }
    }

    public void Tick(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (_flightGraceTicks > 0 && !_capabilities.Has("Flight"))
        {
            _flightGraceTicks--;
            if (_flightGraceTicks == 0 && _activeTerrain == V25TerrainTag.Gap) HandleCapabilityExpiry("Flight");
        }
        else if (_capabilities.Has("Flight")) _flightGraceTicks = 0;
        if (_breathingGraceTicks > 0 && !_capabilities.Has("UnderwaterBreathing"))
        {
            _breathingGraceTicks--;
            if (_breathingGraceTicks == 0 && _activeTerrain == V25TerrainTag.ShallowWater) HandleCapabilityExpiry("UnderwaterBreathing");
        }
        else if (_capabilities.Has("UnderwaterBreathing")) _breathingGraceTicks = 0;
        if (_activeTerrain == V25TerrainTag.CrumblingFloor && !_capabilities.Has("StoneStep"))
        {
            _crumblingTicks = Math.Min(120, _crumblingTicks + 1);
            if (_crumblingTicks >= 120) { _crumblingTicks = 0; RescueFromHazard(); }
        }
    }

    /// <summary>Call when a capability source is revoked. Grace starts once per active traversal.</summary>
    public void NotifyCapabilityRevoked(string capabilityId)
    {
        if (string.Equals(capabilityId, "Flight", StringComparison.Ordinal) && _activeTerrain == V25TerrainTag.Gap)
        {
            _flightGraceTicks = Math.Max(_flightGraceTicks, FlightGraceDurationTicks);
            _cancelUnreleasedCasts?.Invoke(_player.State.Uid);
        }
        if (string.Equals(capabilityId, "UnderwaterBreathing", StringComparison.Ordinal) && _activeTerrain == V25TerrainTag.ShallowWater)
        {
            _breathingGraceTicks = Math.Max(_breathingGraceTicks, BreathingGraceDurationTicks);
            _cancelUnreleasedCasts?.Invoke(_player.State.Uid);
        }
    }

    public bool TryReturnToSafeAnchor()
    {
        if (_safeAnchor is null) return false;
        if (_activeGateStateId is not null && !string.Equals(_activeGateStateId, _safeAnchor.GateStateId, StringComparison.Ordinal)) return false;
        var anchor = new Vec2(_safeAnchor.PositionX, _safeAnchor.PositionY);
        if (!_player.IsSegmentFree(_player.State.Position, anchor, PlayerSystem.CanonicalBodyRadius, 2)) return false;
        _player.SetPosition(anchor);
        _activeTerrain = null;
        _crumblingTicks = 0;
        return true;
    }

    public bool RescueFromHazard()
    {
        if (TryReturnToSafeAnchor())
        {
            _player.ApplyCanonicalDamage(0, Math.Min(_player.State.MaxHp * 0.10, Math.Max(0, _player.State.CurrentHp - 1)));
            return true;
        }
        if (_safeAnchor is null) return false;
        if (_activeGateStateId is not null && !string.Equals(_activeGateStateId, _safeAnchor.GateStateId, StringComparison.Ordinal)) return false;
        var anchor = new Vec2(_safeAnchor.PositionX, _safeAnchor.PositionY);
        if (!_player.IsPositionFree(anchor, PlayerSystem.CanonicalBodyRadius)) return false;
        _player.SetPosition(anchor);
        var loss = Math.Min(_player.State.MaxHp * 0.10, Math.Max(0, _player.State.CurrentHp - 1));
        _player.ApplyCanonicalDamage(0, loss);
        _activeTerrain = null;
        _crumblingTicks = 0;
        return true;
    }

    /// <summary>Region transition invalidates the old walkmesh anchor and active shortcut state.</summary>
    public void ResetForRegion()
    {
        _safeAnchor = null;
        _anchorCandidateTicks = 0;
        _candidatePosition = Vec2.Zero;
        _candidateGateStateId = null;
        _flightGraceTicks = 0;
        _breathingGraceTicks = 0;
        _activeTerrain = null;
        _activeWidth = 0;
        _activeGateStateId = null;
        _activeStartedTick = 0;
        _crumblingTicks = 0;
    }

    public V25TraversalSnapshot Snapshot() => new(
        _safeAnchor,
        _anchorCandidateTicks,
        _candidatePosition.X,
        _candidatePosition.Y,
        _candidateGateStateId,
        _flightGraceTicks,
        _breathingGraceTicks,
        _activeTerrain,
        _activeWidth,
        _activeGateStateId,
        _activeStartedTick,
        _crumblingTicks);

    public void Restore(V25TraversalSnapshot snapshot, long currentTick)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (currentTick < 0 || snapshot.AnchorCandidateTicks is < 0 or > AnchorTicks || snapshot.FlightGraceTicks is < 0 or > FlightGraceDurationTicks || snapshot.BreathingGraceTicks is < 0 or > BreathingGraceDurationTicks || snapshot.CrumblingTicks is < 0 or > 120 || !double.IsFinite(snapshot.CandidatePositionX) || !double.IsFinite(snapshot.CandidatePositionY) || !double.IsFinite(snapshot.ActiveWidthUnits) || snapshot.ActiveWidthUnits < 0 || snapshot.ActiveStartedTick < 0 || snapshot.ActiveStartedTick > currentTick)
            throw new InvalidDataException("Canonical traversal snapshot is invalid.");
        if (snapshot.SafeAnchor is { } anchor)
        {
            if (!double.IsFinite(anchor.PositionX) || !double.IsFinite(anchor.PositionY) || anchor.ConfirmedTick < 0 || anchor.ConfirmedTick > currentTick || string.IsNullOrWhiteSpace(anchor.GateStateId) || !anchor.SafeWalkmesh || !_player.IsPositionFree(new Vec2(anchor.PositionX, anchor.PositionY), PlayerSystem.CanonicalBodyRadius))
                throw new InvalidDataException("Canonical safe anchor is invalid or blocked.");
        }
        if (snapshot.ActiveTerrain is V25TerrainTag.LockedGate or V25TerrainTag.Wall || snapshot.ActiveTerrain is not null && string.IsNullOrWhiteSpace(snapshot.ActiveGateStateId))
            throw new InvalidDataException("Canonical active traversal tag/state is invalid.");
        _safeAnchor = snapshot.SafeAnchor;
        _anchorCandidateTicks = snapshot.AnchorCandidateTicks;
        _candidatePosition = new Vec2(snapshot.CandidatePositionX, snapshot.CandidatePositionY);
        _candidateGateStateId = snapshot.CandidateGateStateId;
        _flightGraceTicks = snapshot.FlightGraceTicks;
        _breathingGraceTicks = snapshot.BreathingGraceTicks;
        _activeTerrain = snapshot.ActiveTerrain;
        _activeWidth = snapshot.ActiveWidthUnits;
        _activeGateStateId = snapshot.ActiveGateStateId;
        _activeStartedTick = snapshot.ActiveStartedTick;
        _crumblingTicks = snapshot.CrumblingTicks;
    }

    private (bool Allowed, V25TraversalFailure? Failure, string? RequiredCapability, bool UsedGrace, bool CombatAllowed) Evaluate(V25TerrainTag terrain, double widthUnits, bool gateOpen, bool isCombatAction)
    {
        if (!gateOpen || terrain == V25TerrainTag.LockedGate) return (false, V25TraversalFailure.LockedGate, null, false, true);
        if (terrain == V25TerrainTag.Wall) return (false, V25TraversalFailure.WallBlocked, null, false, true);
        if (terrain == V25TerrainTag.Ground) return (true, null, null, false, true);
        if (terrain == V25TerrainTag.Gap)
        {
            if (widthUnits > MaxFlightGap) return (false, V25TraversalFailure.GapTooWide, "Flight", false, true);
            var direct = _capabilities.Has("Flight");
            var grace = !direct && _flightGraceTicks > 0;
            return (direct || grace, direct || grace ? null : V25TraversalFailure.CapabilityRequired, "Flight", grace, true);
        }
        if (terrain == V25TerrainTag.ShallowWater)
        {
            var direct = _capabilities.Has("UnderwaterBreathing");
            var grace = !direct && _breathingGraceTicks > 0;
            if (isCombatAction) return (false, V25TraversalFailure.CombatSwimmingForbidden, "UnderwaterBreathing", grace, false);
            return (direct || grace, direct || grace ? null : V25TraversalFailure.CapabilityRequired, "UnderwaterBreathing", grace, false);
        }
        if (terrain == V25TerrainTag.PhasePassable)
        {
            if (widthUnits > MaxPhaseWidth) return (false, V25TraversalFailure.PhaseTooWide, "PhaseStep", false, true);
            return (_capabilities.Has("PhaseStep"), _capabilities.Has("PhaseStep") ? null : V25TraversalFailure.CapabilityRequired, "PhaseStep", false, true);
        }
        if (terrain == V25TerrainTag.FireField)
            return (true, null, _capabilities.Has("FireWard") ? null : "FireWard", false, true);
        if (terrain == V25TerrainTag.ToxicPool)
            return (true, null, _capabilities.Has("ToxicWard") ? null : "ToxicWard", false, true);
        if (terrain == V25TerrainTag.FrostFloor) return (true, null, null, false, true);
        if (terrain == V25TerrainTag.CrumblingFloor) return (true, null, _capabilities.Has("StoneStep") ? null : "StoneStep", false, true);
        return (false, V25TraversalFailure.UnknownTerrainProducer, null, false, true);
    }

    private void HandleCapabilityExpiry(string capabilityId)
    {
        _cancelUnreleasedCasts?.Invoke(_player.State.Uid);
        if (!TryReturnToSafeAnchor()) RescueFromHazard();
    }

    private static bool IsFinite(Vec2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
