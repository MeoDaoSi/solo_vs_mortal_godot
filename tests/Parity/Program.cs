using System.Text.Json;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.Systems;

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

CheckPlayerMonsterCombatParity();
CheckProgressionAndModifierParity();

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

void Equal<T>(T actual, T expected, string label)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected)) failures.Add($"{label}: expected {expected}, got {actual}.");
}

static string FindProjectRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "project.godot"))) return directory.FullName;
    throw new DirectoryNotFoundException("Could not locate project.godot.");
}

void CheckPlayerMonsterCombatParity()
{
    using var movement = new GameSession(definitions, 1234);
    movement.Start();
    var start = movement.Player.State.Position;
    movement.SetInput(new Vec2(1, 0), false);
    movement.Tick(1);
    Near(movement.Player.State.Position.X, start.X + 140, 1e-10, "Player horizontal movement");
    Near(movement.Player.State.Position.Y, start.Y, 1e-10, "Player horizontal Y");

    using var diagonal = new GameSession(definitions, 1234);
    diagonal.Start();
    var diagonalStart = diagonal.Player.State.Position;
    diagonal.SetInput(new Vec2(1, 1), false);
    diagonal.Tick(1);
    Near(diagonal.Player.State.Position.X, diagonalStart.X + 140 / Math.Sqrt(2), 1e-10, "Player diagonal X");
    Near(diagonal.Player.State.Position.Y, diagonalStart.Y + 140 / Math.Sqrt(2), 1e-10, "Player diagonal Y");

    using var combat = new GameSession(definitions, 1234);
    combat.Start();
    var player = combat.Player.State;
    var monster = combat.SpawnMonster("mon_skeleton", 1, player.Position with { X = player.Position.X + 20 });
    Equal(monster.Rank, 1, "Monster level-to-rank");
    Equal(monster.CurrentHp, monster.MaxHp, "Monster starts at maximum HP");
    var expectedDamage = CombatRules.CalculateDamage(player.Stats.Atk, monster.Stats.DefRaw, 1).FinalDamage;
    var hpBefore = monster.CurrentHp;
    combat.SetInput(Vec2.Zero, true);
    combat.Tick(0.5);
    Near(monster.CurrentHp, hpBefore - expectedDamage, 1e-10, "Player attack damage");
    Equal(monster.AiState, SoloVsMortal.Simulation.State.MonsterAiState.Attack, "Monster attacks in range");
    if (player.CurrentHp >= player.MaxHp) failures.Add("Monster attack did not damage Player.");

    Equal(CombatRules.CanTarget(Faction.Enemy, Faction.Player), true, "Enemy targets Player");
    Equal(CombatRules.CanTarget(Faction.Player, Faction.Enemy), true, "Player targets Enemy");
    Equal(CombatRules.CanTarget(Faction.Player, Faction.Ally), false, "Player cannot target Ally");
}

void CheckProgressionAndModifierParity()
{
    using var session = new GameSession(definitions, 1);
    session.Start();
    session.Progression.AddPlayerXp(ProgressionRules.XpRequired(1));
    Equal(session.Player.State.Level, 2, "Player XP level-up");

    var baseHp = session.Player.State.MaxHp;
    var baseAttack = session.Player.State.Stats.Atk;
    session.PlayerModifiers.SetSource(PlayerModifierSource.Essence, [new StatModifiers(DefPercent: 0.1)]);
    var essenceDefense = session.Player.State.Stats.DefRaw;
    session.PlayerModifiers.SetSource(PlayerModifierSource.DebugTestBoost, [new StatModifiers(HpFlat: 500, AtkFlat: 200)]);
    Near(session.Player.State.MaxHp, baseHp + 500, 1e-10, "Debug HP modifier");
    Equal(session.Player.State.Stats.Atk, baseAttack + 200, "Debug ATK modifier");
    session.PlayerModifiers.SetSource(PlayerModifierSource.DebugTestBoost, Array.Empty<StatModifiers>());
    Near(session.Player.State.MaxHp, baseHp, 1e-10, "Remove isolated HP modifier");
    Near(session.Player.State.Stats.DefRaw, essenceDefense, 1e-10, "Preserve Essence modifier source");

    session.Progression.RestoreInventory([
        new InventoryItem("SPIRIT_CRYSTAL", 5),
        new InventoryItem("BEAST_CORE", 2),
        new InventoryItem("POWER_PILL", 1),
    ]);
    Equal(session.Progression.Craft(PillId.BreakthroughMinor), true, "Craft breakthrough pill");
    Equal(session.Progression.Count("BREAKTHROUGH_MINOR"), 1, "Crafted pill count");
    var attackBeforePill = session.Player.State.Stats.Atk;
    Equal(session.Progression.UsePlayerStatPill(PillId.Power), true, "Use Power Pill");
    if (session.Player.State.Stats.Atk <= attackBeforePill) failures.Add("Power Pill did not increase Player ATK.");
    session.Progression.Update(60);
    Equal(session.Player.State.Stats.Atk, attackBeforePill, "Power Pill expiry");
}
