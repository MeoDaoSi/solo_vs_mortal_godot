using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.Rules;

public sealed record SoulBannerComputed(int SlotLimit, int CapacityLimit, int ActiveLimit, double SoulHpPercent, double SoulAtkPercent, double CaptureChance, double SummonModifier);

public static class SoulBannerRules
{
    public static SoulBannerComputed Compute(SoulBannerDefinition definition, int level)
    {
        var safeLevel = System.Math.Clamp(level, 1, definition.MaximumLevel); var steps = safeLevel - 1;
        return new SoulBannerComputed(definition.BaseSlotLimit + definition.SlotLimitPerLevel * steps, definition.BaseCapacity + definition.CapacityPerLevel * steps,
            definition.BaseActiveLimit + definition.ActiveLimitPerLevel * steps, definition.BaseBonuses.SoulHpPercent + definition.BonusPerLevel.SoulHpPercent * steps,
            definition.BaseBonuses.SoulAtkPercent + definition.BonusPerLevel.SoulAtkPercent * steps, definition.CaptureModifier, definition.SummonModifier);
    }
}
