using SoloVsMortal.Application;
using SoloVsMortal.Application.Persistence;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation;

namespace SoloVsMortal.Tests.Application;

public sealed class SaveLoadTests
{
    [Fact]
    public void LegacyVersionOneSaveRemainsCompatible()
    {
        const string legacyV1 = """
        {"version":1,"player":{"currentHp":777,"maxHp":1000,"title":"Lữ Khách"},"souls":[{"id":"soul_90","level":2,"xp":17,"origin":{"monsterUid":"monster_89","configId":"mon_skeleton","species":"SKELETON","displayName":"Skeleton Chiến Binh","rank":1,"rankKey":"1","rankDisplay":"Luyện Khí"}}],"honPhien":[{"id":"legacy_banner","tier":"NHAP_MON","level":1,"boundSouls":["soul_90"]}]}
        """;
        using var session = new GameSession(TestGame.Definitions, 1);
        session.Start();

        new GameApplication(session).RestoreSaveJson(legacyV1);

        Assert.Equal("Lữ Khách", session.Player.State.TitleDisplayName);
        Assert.Equal("UNDEAD_WARRIOR", session.Souls.OwnedSoul("soul_90")?.SoulNatureId);
        Assert.Equal("soul_90", session.SoulBanners.Starter()?.BoundSoulIds.Single());
        Assert.Equal(Simulation.Systems.SoulRuntimeStatus.Ready, session.Summons.Runtime("soul_90").Status);
    }

    [Fact]
    public void CurrentSaveRoundTripPreservesCriticalGameplayOwners()
    {
        using var source = new GameSession(TestGame.Definitions, 77);
        source.Start();
        var skeleton = TestGame.AcquireSoul(source, "mon_skeleton", 1, new Vec2(100, 100));
        var golem = TestGame.AcquireSoul(source, "mon_golem", 31, new Vec2(790, 620));
        var banner = source.SoulBanners.Starter()!;
        source.SoulBanners.Bind(skeleton.Id, banner.Id);
        source.SoulBanners.Bind(golem.Id, banner.Id);
        var summoned = source.Summons.Summon(skeleton.Id, banner.Id, Vec2.Zero);
        source.Allies.TakeDamage(summoned.SummonUid!, 10_000);
        source.Essence.Add("STONE_ESSENCE", 10);
        source.Bloodline.Add("STONE_SPIRIT_BLOODLINE", 3);
        source.Possession.Start(golem.Id);
        source.Possession.Update(5);
        source.World.TryInteract(new Vec2(790, 620));
        var json = new GameApplication(source).CaptureSaveJson();

        using var restored = new GameSession(TestGame.Definitions, 999);
        restored.Start();
        new GameApplication(restored).RestoreSaveJson(json);

        Assert.Equal(2, restored.Souls.OwnedSouls().Count);
        Assert.Equal(2, restored.SoulBanners.Starter()?.BoundSoulIds.Count);
        TestGame.Close(10, restored.Summons.Runtime(skeleton.Id).RecoverySeconds);
        Assert.Equal(10, restored.Essence.Count("STONE_ESSENCE"));
        Assert.Equal(3, restored.Bloodline.Count("STONE_SPIRIT_BLOODLINE"));
        TestGame.Close(25, restored.Possession.RemainingSeconds);
        Assert.True(restored.World.IsDestroyed("obj_cracked_wall_01"));
        Assert.Equal(GameSaveCodec.CurrentVersion, GameSaveCodec.Deserialize(json).Version);
    }

    [Fact]
    public void SaveRestoresCurrentRegionAndActiveMapContent()
    {
        using var source = new GameSession(TestGame.Definitions, 100);
        source.Start();
        var sourceApplication = new GameApplication(source);
        Assert.True(sourceApplication.TravelToRegion("volcano").Success);
        var json = sourceApplication.CaptureSaveJson();

        using var restored = new GameSession(TestGame.Definitions, 101);
        restored.Start();
        new GameApplication(restored).RestoreSaveJson(json);

        Assert.Equal("volcano", restored.WorldMap.CurrentRegionId);
        Assert.Equal("volcano", restored.World.CurrentMap.Id);
    }

    [Fact]
    public void IdenticalSeedAndCommandsProduceIdenticalSaveProjection()
    {
        Assert.Equal(Replay(12345), Replay(12345));
    }

    private static string Replay(uint seed)
    {
        using var session = new GameSession(TestGame.Definitions, seed);
        session.Start();
        session.SetInput(new Vec2(1, 1), false);
        session.Tick(0.25);
        session.SetInput(Vec2.Zero, false);
        var monster = session.SpawnMonster("mon_skeleton", 1, session.Player.State.Position with { X = session.Player.State.Position.X + 20 });
        session.SetInput(Vec2.Zero, true);
        for (var index = 0; index < 12 && monster.Alive; index++) session.Tick(0.5);
        session.Essence.Add("STONE_ESSENCE", 3);
        session.Bloodline.Add("STONE_SPIRIT_BLOODLINE", 3);
        return new GameApplication(session).CaptureSaveJson();
    }
}
