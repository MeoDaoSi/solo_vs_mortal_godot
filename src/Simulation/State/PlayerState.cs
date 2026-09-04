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

    public string Uid { get; }
    public CombatantKind Kind => CombatantKind.Player;
    public Faction Faction => Faction.Player;
    public int Level { get; internal set; } = 1;
    public int Xp { get; internal set; }
    public int Rank { get; internal set; } = 1;
    public string TitleDisplayName { get; internal set; } = "Kẻ chiến đấu dưới nước";
    public StatBlock Stats { get; internal set; }
    public double MaxHp { get; internal set; }
    public double CurrentHp { get; internal set; }
    public Vec2 Position { get; internal set; }
    public double AttackRange { get; }
    public double AttackCooldown { get; internal set; }
    public bool Alive => CurrentHp > 0;
}
