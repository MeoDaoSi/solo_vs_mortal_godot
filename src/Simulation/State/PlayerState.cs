using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.State;

public sealed class PlayerState
{
    internal PlayerState(string uid, Vec2 position, StatBlock stats, double attackRange)
    {
        Uid = uid;
        Position = position;
        Stats = stats;
        MaxHp = stats.Hp;
        CurrentHp = MaxHp;
        AttackRange = attackRange;
    }

    public string Uid { get; private set; }
    public CombatantKind Kind => CombatantKind.Player;
    public Faction Faction => Faction.Player;
    public int Level { get; internal set; } = 1;
    public int Xp { get; internal set; }
    public bool BreakthroughReady { get; internal set; }
    public int Rank { get; internal set; } = 1;
    public string TitleDisplayName { get; internal set; } = "Kẻ chiến đấu dưới nước";
    public StatBlock Stats { get; internal set; }
    /// <summary>Canonical MainHand combat style. Part 5 loadout replaces this with the equipped style; empty hand uses fist.</summary>
    public string CombatStyleId { get; internal set; } = "fist";
    public double MaxHp { get; internal set; }
    public double CurrentHp { get; internal set; }
    public double MaxSpirit { get; internal set; }
    public double CurrentSpirit { get; internal set; }
    public V25ShieldStore Shields { get; } = new();
    public double Shield { get => Shields.Total(Uid); internal set => Shields.SetLegacy(Uid, value); }
    /// <summary>Canonical status ownership. Status timers are simulation ticks, never render callbacks.</summary>
    public V25StatusStore Statuses { get; } = new();
    public Vec2 Position { get; internal set; }
    public double AttackRange { get; }
    public double AttackCooldown { get; internal set; }
    public int DodgeCooldownTicks { get; internal set; }
    public int DodgeRemainingTicks { get; internal set; }
    public int DodgeInvulnerabilityTicks { get; internal set; }
    /// <summary>Direction and distance state for the distributed canonical dodge.</summary>
    public Vec2 DodgeDirection { get; internal set; } = new(0, 1);
    public double DodgeDistanceRemaining { get; internal set; }
    public double DodgeStepDistance { get; internal set; }
    public bool IsDodging => DodgeRemainingTicks > 0;
    public Vec2 Facing { get; internal set; } = new(0, 1);
    public bool Alive => CurrentHp > 0;

    internal void RestoreIdentity(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) throw new ArgumentException("Player UID is required.", nameof(uid));
        Uid = uid;
    }
}
