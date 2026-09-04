using SoloVsMortal.Application;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Tests.Simulation;

public sealed class WorldMapSystemTests
{
    [Fact]
    public void QueryReportsCurrentRegionAndUnavailableTravelIsRejectedWithoutMutation()
    {
        using var session = new GameSession(TestGame.Definitions, 99);
        session.Start();
        var application = new GameApplication(session);

        Assert.Equal("desert", session.WorldMap.CurrentRegionId);
        Assert.Contains(application.WorldMapRegions(), region => region.Id == "desert" && region.IsCurrent && !string.IsNullOrWhiteSpace(region.Story));
        Assert.Contains(application.WorldMapRegions(), region => region.Id == "forest" && !region.IsAvailable && !region.CanTravel);
        Assert.Equal(RegionTravelFailure.RegionUnavailable, application.TravelToRegion("forest").Failure);
        Assert.Equal(RegionTravelFailure.RegionNotFound, application.TravelToRegion("missing-region").Failure);
        Assert.Equal("desert", session.WorldMap.CurrentRegionId);
    }

    [Fact]
    public void TravelChangesAuthoritativeMapAndUpdatesMapSizedRuntimeOwners()
    {
        using var session = new GameSession(TestGame.Definitions, 99);
        session.Start();
        var application = new GameApplication(session);
        session.SpawnMonster("mon_skeleton", 1, new Vec2(700, 240));

        var travel = application.TravelToRegion("volcano");

        Assert.True(travel.Success);
        Assert.Equal("volcano", session.WorldMap.CurrentRegionId);
        Assert.Equal("volcano", session.World.CurrentMap.Id);
        Assert.Empty(session.Monsters.AliveMonsters());
        Assert.Equal(new Vec2(384, 256), session.Player.State.Position);
        var spawned = session.SpawnMonster("mon_skeleton", 1);
        Assert.InRange(spawned.Position.X, 0, session.World.CurrentMap.Width);
        Assert.InRange(spawned.Position.Y, 0, session.World.CurrentMap.Height);
        session.SetInput(new Vec2(1, 0), false);
        session.Tick(10);
        Assert.InRange(session.Player.State.Position.X, 0, session.World.CurrentMap.Width);
        Assert.InRange(session.Player.State.Position.Y, 0, session.World.CurrentMap.Height);
    }

    [Fact]
    public void AuthoredMapPointsAndBlockingCollisionsRespectSafetyInvariants()
    {
        var desert = TestGame.Definitions.Map("desert");

        foreach (var spawn in desert.SpawnPoints)
        {
            Assert.InRange(spawn.Position.X, 0, desert.Width);
            Assert.InRange(spawn.Position.Y, 0, desert.Height);
            Assert.DoesNotContain(desert.BlockedObjectIds, id =>
                desert.Objects[id].Collision is { } collision &&
                new Rect(desert.Objects[id].Position.X + collision.OffsetX, desert.Objects[id].Position.Y + collision.OffsetY, collision.Width, collision.Height)
                    .OverlapsCircle(spawn.Position.X, spawn.Position.Y, desert.SpawnSafetyRadius));
        }

        foreach (var exit in desert.Exits)
        {
            Assert.True(exit.TriggerArea.X >= 0 && exit.TriggerArea.Y >= 0);
            Assert.True(exit.TriggerArea.X + exit.TriggerArea.Width <= desert.Width);
            Assert.True(exit.TriggerArea.Y + exit.TriggerArea.Height <= desert.Height);
        }
    }
}
