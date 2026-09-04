using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Simulation.Rules;

public static class SpriteStageRules
{
    public static int GetSpriteStage(string speciesId, int rank)
    {
        var species = BalanceDefinition.SpeciesById(speciesId);
        var range = BalanceDefinition.TierRange(species.PowerTier);
        if (rank < range.MinimumRank || rank > range.MaximumRank)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank,
                $"Rank is outside {species.PowerTier} range [{range.MinimumRank}..{range.MaximumRank}] for species '{speciesId}'.");
        }

        return rank - range.MinimumRank + 1;
    }

    public static string SpeciesAssetKey(string speciesId, int rank) =>
        $"{speciesId}_STAGE_{GetSpriteStage(speciesId, rank)}";
}
