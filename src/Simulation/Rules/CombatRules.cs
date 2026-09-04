using SoloVsMortal.Simulation.Events;

namespace SoloVsMortal.Simulation.Rules;

public readonly record struct DamageResult(double RawDamage, double FinalDamage, double DefenseMultiplier);

public static class CombatRules
{
    public const double BasicAttackMultiplier = 1;

    public static bool CanTarget(Faction attacker, Faction defender) =>
        attacker == Faction.Enemy ? defender != Faction.Enemy : defender == Faction.Enemy;

    public static DamageResult CalculateDamage(double attackerAttack, double targetDefense, double skillMultiplier)
    {
        var rawDamage = attackerAttack * skillMultiplier;
        var effectiveDefense = System.Math.Max(0, targetDefense);
        var defenseMultiplier = 100 / (100 + effectiveDefense);
        return new DamageResult(rawDamage, rawDamage * defenseMultiplier, defenseMultiplier);
    }
}
