using SoloVsMortal.Application;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Tests.Simulation;

public sealed class CombatAndProgressionTests
{
    [Fact]
    public void MovementIsNormalizedAndCombatRespectsFactionAndDamageRules()
    {
        using var horizontal = new GameSession(TestGame.Definitions, 1234);
        horizontal.Start();
        var horizontalStart = horizontal.Player.State.Position;
        horizontal.SetInput(new Vec2(1, 0), false);
        horizontal.Tick(1);
        TestGame.Close(horizontalStart.X + 140, horizontal.Player.State.Position.X);
        TestGame.Close(horizontalStart.Y, horizontal.Player.State.Position.Y);

        using var diagonal = new GameSession(TestGame.Definitions, 1234);
        diagonal.Start();
        var diagonalStart = diagonal.Player.State.Position;
        diagonal.SetInput(new Vec2(1, 1), false);
        diagonal.Tick(1);
        TestGame.Close(diagonalStart.X + 140 / System.Math.Sqrt(2), diagonal.Player.State.Position.X);
        TestGame.Close(diagonalStart.Y + 140 / System.Math.Sqrt(2), diagonal.Player.State.Position.Y);

        using var combat = new GameSession(TestGame.Definitions, 1234);
        combat.Start();
        var player = combat.Player.State;
        var monster = combat.SpawnMonster("mon_skeleton", 1, player.Position with { X = player.Position.X + 20 });
        var expectedDamage = CombatRules.CalculateDamage(player.Stats.Atk, monster.Stats.DefRaw, 1).FinalDamage;
        var monsterHp = monster.CurrentHp;
        combat.SetInput(Vec2.Zero, true);
        combat.Tick(0.5);

        TestGame.Close(monsterHp - expectedDamage, monster.CurrentHp);
        Assert.Equal(SoloVsMortal.Simulation.State.MonsterAiState.Attack, monster.AiState);
        Assert.True(player.CurrentHp < player.MaxHp);
        Assert.True(CombatRules.CanTarget(Faction.Enemy, Faction.Player));
        Assert.True(CombatRules.CanTarget(Faction.Player, Faction.Enemy));
        Assert.False(CombatRules.CanTarget(Faction.Player, Faction.Ally));
    }

    [Fact]
    public void ProgressionCraftingAndModifierSourcesRemainIsolated()
    {
        using var session = new GameSession(TestGame.Definitions, 1);
        session.Start();
        session.Progression.AddPlayerXp(ProgressionRules.XpRequired(1));
        Assert.Equal(2, session.Player.State.Level);

        var baseHp = session.Player.State.MaxHp;
        var baseAttack = session.Player.State.Stats.Atk;
        session.PlayerModifiers.SetSource(PlayerModifierSource.Essence, [new StatModifiers(DefPercent: 0.1)]);
        var essenceDefense = session.Player.State.Stats.DefRaw;
        session.PlayerModifiers.SetSource(PlayerModifierSource.DebugTestBoost, [new StatModifiers(HpFlat: 500, AtkFlat: 200)]);
        TestGame.Close(baseHp + 500, session.Player.State.MaxHp);
        Assert.Equal(baseAttack + 200, session.Player.State.Stats.Atk);
        session.PlayerModifiers.SetSource(PlayerModifierSource.DebugTestBoost, Array.Empty<StatModifiers>());
        TestGame.Close(baseHp, session.Player.State.MaxHp);
        TestGame.Close(essenceDefense, session.Player.State.Stats.DefRaw);

        session.Progression.RestoreInventory([
            new InventoryItem("SPIRIT_CRYSTAL", 5),
            new InventoryItem("BEAST_CORE", 2),
            new InventoryItem("POWER_PILL", 1),
        ]);
        Assert.True(session.Progression.Craft(PillId.BreakthroughMinor));
        Assert.Equal(1, session.Progression.Count("BREAKTHROUGH_MINOR"));
        var attackBeforePill = session.Player.State.Stats.Atk;
        Assert.True(session.Progression.UsePlayerStatPill(PillId.Power));
        Assert.True(session.Player.State.Stats.Atk > attackBeforePill);
        session.Progression.Update(60);
        Assert.Equal(attackBeforePill, session.Player.State.Stats.Atk);
    }

    [Fact]
    public void ProgressionSaveProjectionRestoresInventoryAndTimedBuffs()
    {
        using var source = new GameSession(TestGame.Definitions, 1);
        source.Start();
        source.Progression.RestoreInventory([new InventoryItem("SPIRIT_CRYSTAL", 3), new InventoryItem("BEAST_CORE", 1)]);
        source.Progression.RestorePlayerBuffs([new TimedPlayerBuffRestore(PillId.Guard, 12)]);
        var saved = new GameApplication(source).CaptureProgressionSave();

        using var restored = new GameSession(TestGame.Definitions, 2);
        restored.Start();
        new GameApplication(restored).RestoreProgressionSave(saved);

        Assert.Equal(3, restored.Progression.Count("SPIRIT_CRYSTAL"));
        Assert.Equal(1, restored.Progression.Count("BEAST_CORE"));
        var buff = Assert.Single(restored.Progression.PlayerBuffSnapshot());
        Assert.Equal(PillId.Guard, buff.PillId);
        TestGame.Close(12, buff.RemainingSeconds);
    }
}
