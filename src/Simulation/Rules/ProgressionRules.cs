using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.Rules;

public readonly record struct XpState(int Level, int Xp);
public sealed record XpGainResult(int Level, int Xp, IReadOnlyList<int> LevelsGained, bool BlockedByBreakthrough);

public static class ProgressionRules
{
    public static int XpRequired(int level)
    {
        var config = BalanceDefinition.Progression;
        var n = System.Math.Max(0, level - 1);
        return config.XpBase + config.XpLinear * n + config.XpQuadratic * n * n;
    }

    public static XpGainResult AddXp(XpState state, int amount, int maximumLevel, int? levelCap = null)
    {
        var level = System.Math.Max(1, System.Math.Min(maximumLevel, state.Level));
        var xp = System.Math.Max(0, state.Xp) + System.Math.Max(0, amount);
        var gained = new List<int>();
        var cap = System.Math.Min(maximumLevel, System.Math.Max(level, levelCap ?? maximumLevel));
        while (level < cap)
        {
            var required = XpRequired(level);
            if (xp < required) break;
            xp -= required;
            gained.Add(++level);
        }

        if ((level == cap && cap < maximumLevel) || level >= maximumLevel) xp = 0;
        return new XpGainResult(level, xp, gained.AsReadOnly(), level == cap && cap < maximumLevel);
    }

    public static int PlayerRankCap(int rank) => CombatPowerRules.RankEndLevel(rank);
    public static int BreakthroughFailureLevel(int rank) => CombatPowerRules.RankStartLevel(rank);
}
