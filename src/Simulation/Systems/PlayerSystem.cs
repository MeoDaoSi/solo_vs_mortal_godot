using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems;

public readonly record struct PlayerBounds(double Width, double Height);

public sealed class PlayerSystem
{
    public const double BodyRadius = 12;
    private readonly EventBus _events;
    private readonly PlayerBounds _bounds;
    private IReadOnlyList<Rect> _colliders;

    public PlayerSystem(EventBus events, UidGenerator uids, PlayerDefinition definition, Vec2 start, PlayerBounds bounds, IReadOnlyList<Rect>? colliders = null)
    {
        _events = events;
        _bounds = bounds;
        _colliders = colliders ?? Array.Empty<Rect>();
        var stats = CombatPowerRules.GetStatBlock(new EntityStatContext(EntityType.Player, 1, 1, Archetype: StatArchetypeId.Balanced));
        State = new PlayerState(uids.Create("player"), start, stats, definition.AttackRange);
    }

    public PlayerState State { get; }
    public void SetColliders(IReadOnlyList<Rect> colliders) => _colliders = colliders ?? throw new ArgumentNullException(nameof(colliders));

    public void Update(double deltaSeconds, Vec2 input)
    {
        if (!State.Alive || input == Vec2.Zero) return;
        var direction = input.Normalized();
        var distance = CombatPowerRules.GetMovementSpeed(EntityType.Player, State.Stats.SpeedRaw) * deltaSeconds;
        var nextX = Vec2.Clamp(State.Position.X + direction.X * distance, 0, _bounds.Width);
        var nextY = Vec2.Clamp(State.Position.Y + direction.Y * distance, 0, _bounds.Height);
        if (CanStand(nextX, State.Position.Y)) State.Position = State.Position with { X = nextX };
        if (CanStand(State.Position.X, nextY)) State.Position = State.Position with { Y = nextY };
    }

    public void SetProgression(int level, int rank)
    {
        BalanceDefinition.RequireValidLevel(level);
        BalanceDefinition.RequireValidRank(rank);
        if (CombatPowerRules.GlobalLevelToRank(level) != rank) throw new InvalidOperationException($"Player level {level} does not belong to rank {rank}.");
        State.Level = level;
        State.Rank = rank;
        RecomputeStats();
    }

    public void Restore(int level, int rank, int xp, double currentHp, string? titleDisplayName)
    {
        if (level < 1 || level > BalanceDefinition.MaximumLevel) level = 1;
        var expectedRank = CombatPowerRules.GlobalLevelToRank(level);
        rank = rank >= 1 && rank <= BalanceDefinition.MaximumRank && rank == expectedRank ? rank : expectedRank;
        State.Level = level; State.Rank = rank; State.Xp = System.Math.Max(0, xp);
        RecomputeStats();
        if (double.IsFinite(currentHp) && currentHp > 0) State.CurrentHp = System.Math.Min(System.Math.Truncate(currentHp), State.MaxHp);
        if (!string.IsNullOrWhiteSpace(titleDisplayName)) State.TitleDisplayName = titleDisplayName.Trim()[..System.Math.Min(120, titleDisplayName.Trim().Length)];
    }

    public void RecomputeStats(IReadOnlyList<StatModifiers>? modifiers = null)
    {
        var ratio = State.MaxHp > 0 ? State.CurrentHp / State.MaxHp : 1;
        State.Stats = CombatPowerRules.GetStatBlock(new EntityStatContext(EntityType.Player, State.Level, State.Rank, Archetype: StatArchetypeId.Balanced), modifiers);
        State.MaxHp = State.Stats.Hp;
        State.CurrentHp = System.Math.Min(State.MaxHp, System.Math.Max(0, State.MaxHp * ratio));
    }

    public void TakeDamage(double amount)
    {
        if (amount <= 0 || !State.Alive) return;
        State.CurrentHp = System.Math.Max(0, State.CurrentHp - amount);
        if (!State.Alive) _events.Publish(new PlayerDefeatedEvent(State.CurrentHp));
        else _events.Publish(new PlayerDamagedEvent(amount, State.CurrentHp, State.MaxHp));
    }

    private bool CanStand(double x, double y) => !_colliders.Any(rect => rect.OverlapsCircle(x, y, BodyRadius));
}
