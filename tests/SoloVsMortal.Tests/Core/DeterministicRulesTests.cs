using SoloVsMortal.Core.Rng;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Tests.Core;

public sealed class DeterministicRulesTests
{
    [Fact]
    public void SeededRngKeepsItsLockedSequence()
    {
        var expected = new[]
        {
            0.6011037519201636,
            0.44829055899754167,
            0.8524657934904099,
            0.6697340414393693,
            0.17481389874592423,
            0.5265925421845168,
            0.2732279943302274,
            0.6247446539346129,
        };
        var rng = new SeededRng(42);

        foreach (var value in expected) TestGame.Close(value, rng.Next(), 1e-15);
    }

    [Theory]
    [InlineData(EntityType.Player, 1, 1, 100)]
    [InlineData(EntityType.Monster, 1, 1, 118)]
    [InlineData(EntityType.Soul, 1, 1, 84)]
    [InlineData(EntityType.Player, 11, 2, 126.7596)]
    [InlineData(EntityType.Monster, 50, 5, 341.82057936384)]
    [InlineData(EntityType.Soul, 90, 9, 703.0095005700584)]
    public void CombatPowerUsesLockedRepresentativeValues(EntityType entityType, int level, int rank, double expected)
    {
        TestGame.Close(expected, CombatPowerRules.GetCp(entityType, level, rank), 1e-12);
    }

    [Fact]
    public void RankBoundariesAndProgressionCapRemainConsistent()
    {
        for (var rank = 1; rank <= BalanceDefinition.MaximumRank; rank++)
        {
            Assert.Equal(rank, CombatPowerRules.GlobalLevelToRank(CombatPowerRules.RankStartLevel(rank)));
            Assert.Equal(rank, CombatPowerRules.GlobalLevelToRank(CombatPowerRules.RankEndLevel(rank)));
        }

        var capped = ProgressionRules.AddXp(new XpState(1, 0), int.MaxValue, BalanceDefinition.MaximumLevel, 10);
        Assert.Equal(10, capped.Level);
        Assert.Equal(0, capped.Xp);
        Assert.True(capped.BlockedByBreakthrough);
    }

    [Theory]
    [InlineData("SKELETON", 1, 1)]
    [InlineData("SKELETON", 3, 3)]
    [InlineData("GOLEM", 4, 1)]
    [InlineData("DRAGON", 9, 3)]
    public void SpriteStageTracksSpeciesAndRank(string speciesId, int rank, int expectedStage)
    {
        Assert.Equal(expectedStage, SpriteStageRules.GetSpriteStage(speciesId, rank));
    }
}
