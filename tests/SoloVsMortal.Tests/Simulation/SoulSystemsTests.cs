using SoloVsMortal.Application;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Tests.Simulation;

public sealed class SoulSystemsTests
{
    [Fact]
    public void CapturedSoulCanBindSummonDisperseRecoverAndUnbind()
    {
        using var session = new GameSession(TestGame.Definitions, 7);
        session.Start();
        var monster = session.SpawnMonster("mon_skeleton", 1, new Vec2(12, 34));
        session.Monsters.TakeDamage(monster.Uid, 10_000_000);
        var worldSoul = Assert.Single(session.Souls.WorldSouls());
        Assert.Equal(monster.Uid, worldSoul.Origin.MonsterUid);
        Assert.Equal("SKELETON", worldSoul.Origin.SpeciesId);
        Assert.Equal(new Vec2(12, 34), worldSoul.Position);

        var owned = Assert.IsType<OwnedSoulState>(session.Souls.Acquire(worldSoul.Id));
        Assert.Empty(session.Souls.WorldSouls());
        Assert.Equal(1, owned.Level);
        Assert.Equal(1, owned.Origin.Rank);

        var banner = Assert.IsType<SoulBannerState>(session.SoulBanners.Starter());
        var binding = session.SoulBanners.Bind(owned.Id, banner.Id);
        Assert.True(binding.Success);
        Assert.Equal(0, binding.SlotIndex);
        Assert.Equal(1, session.SoulBanners.UsedCapacity(banner));
        Assert.Equal(BindSoulFailure.SoulAlreadyBound, session.SoulBanners.Bind(owned.Id, banner.Id).Failure);

        var application = new GameApplication(session);
        var link = Assert.Single(application.SoulLinks());
        Assert.Equal(SoulLinkState.Dormant, link.State);
        Assert.True(link.CanSummon);

        var summoned = session.Summons.Summon(owned.Id, banner.Id, new Vec2(20, 30));
        Assert.True(summoned.Success);
        Assert.Equal(SoulRuntimeStatus.Summoned, session.Summons.Runtime(owned.Id).Status);
        Assert.Equal(UnbindSoulFailure.SoulActive, application.UnbindSoul(owned.Id, banner.Id).Failure);
        session.Allies.TakeDamage(summoned.SummonUid!, 10_000);
        Assert.Equal(SoulRuntimeStatus.Dispersed, session.Summons.Runtime(owned.Id).Status);
        TestGame.Close(10, session.Summons.Runtime(owned.Id).RecoverySeconds);
        Assert.Equal(SummonFailure.Dispersed, session.Summons.Summon(owned.Id, banner.Id, Vec2.Zero).Failure);
        session.Summons.Update(10);
        Assert.Equal(SoulRuntimeStatus.Ready, session.Summons.Runtime(owned.Id).Status);
        Assert.True(session.Summons.Summon(owned.Id, banner.Id, Vec2.Zero).Success);
        Assert.True(session.Summons.UnsummonSoul(owned.Id));
        Assert.True(session.SoulBanners.Unbind(owned.Id, banner.Id).Success);
    }

    [Fact]
    public void EssenceBloodlinePossessionAndCapabilityOwnersRemainIsolated()
    {
        using var session = new GameSession(TestGame.Definitions, 7);
        session.Start();
        var baseDefense = session.Player.State.Stats.DefRaw;
        var baseHp = session.Player.State.MaxHp;

        Assert.Equal(3, session.Essence.Add("STONE_ESSENCE", 3.9));
        TestGame.Close(baseDefense * 1.02, session.Player.State.Stats.DefRaw);
        Assert.Equal(25, session.Essence.Add("STONE_ESSENCE", 100));
        TestGame.Close(baseDefense * 1.06, session.Player.State.Stats.DefRaw);
        session.Essence.Restore(new Dictionary<string, int> { ["STONE_ESSENCE"] = 10 });
        TestGame.Close(baseDefense * 1.04, session.Player.State.Stats.DefRaw);

        Assert.Equal(10, session.Bloodline.Add("STONE_SPIRIT_BLOODLINE", 10));
        TestGame.Close(baseHp * 1.02, session.Player.State.MaxHp);
        session.Bloodline.Restore(new Dictionary<string, int> { ["STONE_SPIRIT_BLOODLINE"] = 3 });
        TestGame.Close(baseHp * 1.01, session.Player.State.MaxHp);

        var soul = TestGame.AcquireSoul(session, "mon_golem", 31, new Vec2(790, 620));
        var banner = Assert.IsType<SoulBannerState>(session.SoulBanners.Starter());
        Assert.True(session.SoulBanners.Bind(soul.Id, banner.Id).Success);
        var blockersBefore = session.World.BlockingRects().Count;
        Assert.Equal(WorldInteractionFailure.CapabilityRequired, session.World.TryInteract(new Vec2(790, 620)).Failure);

        var defenseBeforePossession = session.Player.State.Stats.DefRaw;
        Assert.True(session.Possession.Start(soul.Id).Success);
        Assert.Equal(SoulRuntimeStatus.Possessed, session.Summons.Runtime(soul.Id).Status);
        Assert.True(session.Capabilities.Has("BREAK_FRAGILE_WALL"));
        Assert.True(session.Player.State.Stats.DefRaw > defenseBeforePossession);
        Assert.Equal(SummonFailure.Possessed, session.Summons.Summon(soul.Id, banner.Id, Vec2.Zero).Failure);
        var interaction = session.World.TryInteract(new Vec2(790, 620));
        Assert.True(interaction.Success);
        Assert.Equal("obj_cracked_wall_01", interaction.ObjectId);
        Assert.Equal(blockersBefore - 1, session.World.BlockingRects().Count);

        session.Possession.Update(30);
        Assert.Null(session.Possession.ActiveSoulId);
        Assert.False(session.Capabilities.Has("BREAK_FRAGILE_WALL"));
        Assert.Equal(SoulRuntimeStatus.Dispersed, session.Summons.Runtime(soul.Id).Status);
        TestGame.Close(15, session.Summons.Runtime(soul.Id).RecoverySeconds);
        session.Summons.Update(15);
        Assert.Equal(SoulRuntimeStatus.Ready, session.Summons.Runtime(soul.Id).Status);
    }

    [Fact]
    public void DevourConsumesAnEligibleSoulExactlyOnceAndGrantsItsReward()
    {
        using var session = new GameSession(TestGame.Definitions, 17);
        session.Start();
        var soul = TestGame.AcquireSoul(session, "mon_golem", 31, new Vec2(200, 200));
        Assert.Contains(session.Devouring.Previews(soul.Id), item => item.Mode == DevourMode.Essence);

        var result = session.Devouring.Execute(soul.Id, DevourMode.Essence);

        Assert.True(result.Success);
        Assert.Null(session.Souls.OwnedSoul(soul.Id));
        Assert.Equal(1, session.Essence.Count("STONE_ESSENCE"));
        Assert.Equal(DevourFailure.SoulNotFound, session.Devouring.Execute(soul.Id, DevourMode.Essence).Failure);
    }
}
