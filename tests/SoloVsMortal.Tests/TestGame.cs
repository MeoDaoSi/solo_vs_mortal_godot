using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Tests;

internal static class TestGame
{
    public static GameDefinitions Definitions { get; } = GameDefinitionLoader.LoadFromDirectory(Path.Combine(ProjectRoot, "data", "configs"));

    public static OwnedSoulState AcquireSoul(GameSession session, string monsterId, int level, Vec2 position)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            var monster = session.SpawnMonster(monsterId, level, position);
            session.Monsters.TakeDamage(monster.Uid, 10_000_000);
            var drop = session.Souls.WorldSouls().FirstOrDefault();
            if (drop is not null && session.Souls.Acquire(drop.Id) is { } soul) return soul;
        }
        throw new InvalidOperationException($"Could not generate an owned Soul from '{monsterId}'.");
    }

    public static void Close(double expected, double actual, double tolerance = 1e-10) =>
        Assert.InRange(System.Math.Abs(actual - expected), 0, tolerance);

    private static string ProjectRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "project.godot"))) return directory.FullName;
            throw new DirectoryNotFoundException("Could not locate project.godot.");
        }
    }
}
