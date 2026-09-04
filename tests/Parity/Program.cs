using System.Text.Json;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Rules;

var fixturePath = Path.Combine(AppContext.BaseDirectory, "foundation-golden.json");
using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
var root = fixture.RootElement;
var failures = new List<string>();

var rngInput = root.GetProperty("rng");
var rng = new SeededRng(rngInput.GetProperty("seed").GetUInt32());
var rngIndex = 0;
foreach (var expectedElement in rngInput.GetProperty("values").EnumerateArray())
{
    Near(rng.Next(), expectedElement.GetDouble(), 1e-15, $"RNG[{rngIndex++}]");
}

foreach (var row in root.GetProperty("combatPower").EnumerateArray())
{
    var level = row.GetProperty("level").GetInt32();
    var rank = row.GetProperty("rank").GetInt32();
    Near(CombatPowerRules.GetCp(EntityType.Player, level, rank), row.GetProperty("player").GetDouble(), 1e-12, $"Player CP L{level} R{rank}");
    Near(CombatPowerRules.GetCp(EntityType.Monster, level, rank), row.GetProperty("monster").GetDouble(), 1e-12, $"Monster CP L{level} R{rank}");
    Near(CombatPowerRules.GetCp(EntityType.Soul, level, rank), row.GetProperty("soul").GetDouble(), 1e-12, $"Soul CP L{level} R{rank}");
}

for (var rank = 1; rank <= BalanceDefinition.MaximumRank; rank++)
{
    Equal(CombatPowerRules.GlobalLevelToRank(CombatPowerRules.RankStartLevel(rank)), rank, $"Rank start {rank}");
    Equal(CombatPowerRules.GlobalLevelToRank(CombatPowerRules.RankEndLevel(rank)), rank, $"Rank end {rank}");
}

Equal(SpriteStageRules.GetSpriteStage("SKELETON", 1), 1, "Skeleton sprite stage 1");
Equal(SpriteStageRules.GetSpriteStage("SKELETON", 3), 3, "Skeleton sprite stage 3");
Equal(SpriteStageRules.GetSpriteStage("GOLEM", 4), 1, "Golem sprite stage 1");
Equal(SpriteStageRules.GetSpriteStage("DRAGON", 9), 3, "Dragon sprite stage 3");
Equal(ProgressionRules.XpRequired(1), 100, "Level 1 XP");
Equal(ProgressionRules.XpRequired(2), 130, "Level 2 XP");
var capped = ProgressionRules.AddXp(new XpState(1, 0), int.MaxValue, BalanceDefinition.MaximumLevel, 10);
Equal(capped.Level, 10, "Breakthrough level cap");
Equal(capped.Xp, 0, "XP cleared at breakthrough cap");
Equal(capped.BlockedByBreakthrough, true, "Breakthrough block flag");

var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
var definitions = GameDefinitionLoader.LoadFromDirectory(Path.Combine(projectRoot, "data", "configs"));
Equal(definitions.Monsters.Count(), 3, "Monster definition count");
Equal(definitions.SoulBanners.Count(), 3, "Soul Banner definition count");
Equal(definitions.SoulNatures.Natures.Count, 3, "Soul Nature definition count");
Equal(definitions.Maps.Count, 1, "Map definition count");

if (args.Length > 0)
{
    var sourceAssetRoot = Path.GetFullPath(args[0]);
    foreach (var asset in definitions.Assets.Assets.Values)
    {
        var candidate = Path.GetFullPath(Path.Combine(sourceAssetRoot, asset.File.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(sourceAssetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            failures.Add($"Asset '{asset.Id}' is missing at '{candidate}'.");
    }
}

if (failures.Count > 0)
{
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}

Console.WriteLine("Foundation parity checks passed.");
return 0;

void Near(double actual, double expected, double tolerance, string label)
{
    if (double.IsNaN(actual) || Math.Abs(actual - expected) > tolerance)
        failures.Add($"{label}: expected {expected:R}, got {actual:R}.");
}

void Equal<T>(T actual, T expected, string label) where T : IEquatable<T>
{
    if (!actual.Equals(expected)) failures.Add($"{label}: expected {expected}, got {actual}.");
}

static string FindProjectRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "project.godot"))) return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate project.godot.");
}
