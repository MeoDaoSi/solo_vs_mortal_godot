using System.Text.Json;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Application;
using SoloVsMortal.Application.Persistence;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
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
CheckSoulAndBannerParity();
CheckAdvancedSoulAndWorldParity();
CheckSaveMigrationAndReplayParity();

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

Console.WriteLine("Migration parity checks passed.");
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

    session.Progression.RestoreInventory([new InventoryItem("SPIRIT_CRYSTAL", 3), new InventoryItem("BEAST_CORE", 1)]);
    session.Progression.RestorePlayerBuffs([new TimedPlayerBuffRestore(PillId.Guard, 12)]);
    var application = new GameApplication(session);
    var saved = application.CaptureProgressionSave();
    using var restoredSession = new GameSession(definitions, 2);
    restoredSession.Start();
    var restoredApplication = new GameApplication(restoredSession);
    restoredApplication.RestoreProgressionSave(saved);
    Equal(restoredSession.Progression.Count("SPIRIT_CRYSTAL"), 3, "Restore material inventory");
    Equal(restoredSession.Progression.Count("BEAST_CORE"), 1, "Restore core inventory");
    Equal(restoredSession.Progression.PlayerBuffSnapshot().Count, 1, "Restore Player buffs");
    Equal(restoredSession.Progression.PlayerBuffSnapshot()[0].PillId, PillId.Guard, "Restored buff Pill ID");
    Near(restoredSession.Progression.PlayerBuffSnapshot()[0].RemainingSeconds, 12, 1e-10, "Restored buff duration");
}

void CheckSoulAndBannerParity()
{
    using var session = new GameSession(definitions, 7);
    session.Start();
    var monster = session.SpawnMonster("mon_skeleton", 1, new Vec2(12, 34));
    session.Monsters.TakeDamage(monster.Uid, 10_000_000);
    Equal(session.Souls.WorldSouls().Count, 1, "Soul generated on Monster defeat");
    var worldSoul = session.Souls.WorldSouls()[0];
    Equal(worldSoul.Origin.MonsterUid, monster.Uid, "Soul origin Monster UID");
    Equal(worldSoul.Origin.SpeciesId, "SKELETON", "Soul origin species");
    Equal(worldSoul.Origin.Rank, 1, "Soul origin rank");
    Equal(worldSoul.Position, new Vec2(12, 34), "Soul drop position");

    var owned = session.Souls.Acquire(worldSoul.Id);
    if (owned is null) { failures.Add("Soul acquisition returned null."); return; }
    Equal(session.Souls.WorldSouls().Count, 0, "World Soul removed on acquisition");
    Equal(session.Souls.OwnedSouls().Count, 1, "Owned Soul created");
    Equal(owned.Level, 1, "Owned Soul starts at level 1");
    Equal(owned.Origin.Rank, 1, "Owned Soul preserves origin rank");

    var banner = session.SoulBanners.Starter();
    if (banner is null) { failures.Add("Starter Soul Banner was not created."); return; }
    Equal(banner.Tier, SoulBannerTier.NhapMon, "Starter Soul Banner tier");
    Equal(banner.Computed.SlotLimit, 3, "Starter slot limit");
    Equal(banner.Computed.CapacityLimit, 3, "Starter capacity limit");
    var binding = session.SoulBanners.Bind(owned.Id, banner.Id);
    Equal(binding.Success, true, "Bind owned Soul");
    Equal(binding.SlotIndex, 0, "First Soul Banner slot");
    Equal(session.SoulBanners.UsedCapacity(banner), 1, "Soul Banner used capacity");
    Equal(session.SoulBanners.Bind(owned.Id, banner.Id).Failure, BindSoulFailure.SoulAlreadyBound, "Reject duplicate binding");

    var summoned = session.Summons.Summon(owned.Id, banner.Id, new Vec2(20, 30));
    Equal(summoned.Success, true, "Summon bound Soul");
    Equal(session.Summons.ActiveCount, 1, "Active summon count");
    Equal(session.Summons.Runtime(owned.Id).Status, SoulRuntimeStatus.Summoned, "Summoned runtime state");
    session.Allies.TakeDamage(summoned.SummonUid!, 10_000);
    Equal(session.Summons.Runtime(owned.Id).Status, SoulRuntimeStatus.Dispersed, "Defeated summon disperses Soul");
    Near(session.Summons.Runtime(owned.Id).RecoverySeconds, 10, 1e-10, "Soul recovery duration");
    Equal(session.Summons.Summon(owned.Id, banner.Id, Vec2.Zero).Failure, SummonFailure.Dispersed, "Cannot summon while dispersed");
    session.Summons.Update(10);
    Equal(session.Summons.Runtime(owned.Id).Status, SoulRuntimeStatus.Ready, "Soul recovers to Ready");
    var resummoned = session.Summons.Summon(owned.Id, banner.Id, Vec2.Zero);
    Equal(resummoned.Success, true, "Summon recovered Soul");
    Equal(session.Summons.UnsummonSoul(owned.Id), true, "Unsummon Soul");
    Equal(session.Summons.ActiveCount, 0, "No active summon after unsummon");
    Equal(session.SoulBanners.Unbind(owned.Id, banner.Id).Success, true, "Unbind Soul");

    session.Souls.AddXp(owned.Id, ProgressionRules.XpRequired(1));
    Equal(owned.Level, 2, "Owned Soul XP level-up");
}

void CheckAdvancedSoulAndWorldParity()
{
    using var session = new GameSession(definitions, 7);
    session.Start();
    var baseDefense = session.Player.State.Stats.DefRaw;
    var baseHp = session.Player.State.MaxHp;
    Equal(session.Essence.Add("STONE_ESSENCE", 3.9), 3, "Essence truncates contribution");
    Near(session.Player.State.Stats.DefRaw, baseDefense * 1.02, 1e-10, "Essence first milestone modifier");
    Equal(session.Essence.Add("STONE_ESSENCE", 100), 25, "Essence caps at final milestone");
    Near(session.Player.State.Stats.DefRaw, baseDefense * 1.06, 1e-10, "Essence highest milestone only");
    session.Essence.Restore(new Dictionary<string, int> { ["STONE_ESSENCE"] = 10 });
    Near(session.Player.State.Stats.DefRaw, baseDefense * 1.04, 1e-10, "Essence restore modifier ownership");

    Equal(session.Bloodline.Add("STONE_SPIRIT_BLOODLINE", 10), 10, "Bloodline progress");
    Near(session.Player.State.MaxHp, baseHp * 1.02, 1e-10, "Bloodline milestone modifier");
    session.Bloodline.Restore(new Dictionary<string, int> { ["STONE_SPIRIT_BLOODLINE"] = 3 });
    Near(session.Player.State.MaxHp, baseHp * 1.01, 1e-10, "Bloodline restore modifier ownership");

    OwnedSoulState? golemSoul = null;
    for (var attempt = 0; attempt < 200 && golemSoul is null; attempt++)
    {
        var monster = session.SpawnMonster("mon_golem", 31, new Vec2(790, 620));
        session.Monsters.TakeDamage(monster.Uid, 10_000_000);
        var drop = session.Souls.WorldSouls().FirstOrDefault();
        if (drop is not null) golemSoul = session.Souls.Acquire(drop.Id);
    }
    if (golemSoul is null) { failures.Add("Could not deterministically generate a Golem Soul."); return; }
    var banner = session.SoulBanners.Starter();
    if (banner is null || !session.SoulBanners.Bind(golemSoul.Id, banner.Id).Success) { failures.Add("Could not bind Golem Soul for possession."); return; }

    var blockedBefore = session.World.BlockingRects().Count;
    var blocked = session.World.TryInteract(new Vec2(790, 620));
    Equal(blocked.Failure, WorldInteractionFailure.CapabilityRequired, "Fragile wall requires capability");
    var defenseBeforePossession = session.Player.State.Stats.DefRaw;
    var possession = session.Possession.Start(golemSoul.Id);
    Equal(possession.Success, true, "Start bound Golem possession");
    Equal(session.Summons.Runtime(golemSoul.Id).Status, SoulRuntimeStatus.Possessed, "Possessed runtime state");
    Equal(session.Capabilities.Has("BREAK_FRAGILE_WALL"), true, "Possession grants capability");
    if (session.Player.State.Stats.DefRaw <= defenseBeforePossession) failures.Add("Possession did not apply its defense modifier.");
    Equal(session.Summons.Summon(golemSoul.Id, banner.Id, Vec2.Zero).Failure, SummonFailure.Possessed, "Cannot summon possessed Soul");
    var interaction = session.World.TryInteract(new Vec2(790, 620));
    Equal(interaction.Success, true, "Possession breaks fragile wall");
    Equal(interaction.ObjectId, "obj_cracked_wall_01", "Destroyed world object ID");
    Equal(session.World.BlockingRects().Count, blockedBefore - 1, "Destroyed wall removes blocking collider");
    session.Possession.Update(30);
    Equal(session.Possession.ActiveSoulId, null, "Possession expires");
    Equal(session.Capabilities.Has("BREAK_FRAGILE_WALL"), false, "Capability removed after possession");
    Equal(session.Summons.Runtime(golemSoul.Id).Status, SoulRuntimeStatus.Dispersed, "Possession cooldown disperses Soul");
    Near(session.Summons.Runtime(golemSoul.Id).RecoverySeconds, 15, 1e-10, "Possession cooldown duration");
    session.Summons.Update(15);
    Equal(session.Summons.Runtime(golemSoul.Id).Status, SoulRuntimeStatus.Ready, "Soul ready after possession cooldown");
}

void CheckSaveMigrationAndReplayParity()
{
    const string legacyV1 = """
    {"version":1,"player":{"currentHp":777,"maxHp":1000,"title":"Lữ Khách"},"souls":[{"id":"soul_90","level":2,"xp":17,"origin":{"monsterUid":"monster_89","configId":"mon_skeleton","species":"SKELETON","displayName":"Skeleton Chiến Binh","rank":1,"rankKey":"1","rankDisplay":"Luyện Khí"}}],"honPhien":[{"id":"legacy_banner","tier":"NHAP_MON","level":1,"boundSouls":["soul_90"]}]}
    """;
    using (var legacySession = new GameSession(definitions, 1))
    {
        legacySession.Start(); var legacyApp = new GameApplication(legacySession); legacyApp.RestoreSaveJson(legacyV1);
        Equal(legacySession.Player.State.TitleDisplayName, "Lữ Khách", "Legacy title alias");
        Equal(legacySession.Souls.OwnedSoul("soul_90")?.SoulNatureId, "UNDEAD_WARRIOR", "Legacy Soul nature fallback");
        Equal(legacySession.SoulBanners.Starter()?.BoundSoulIds.SingleOrDefault(), "soul_90", "Legacy honPhien alias");
        Equal(legacySession.Summons.Runtime("soul_90").Status, SoulRuntimeStatus.Ready, "Legacy save defaults Soul runtime to Ready");
    }

    using var source = new GameSession(definitions, 77); source.Start(); var sourceApp = new GameApplication(source);
    var skeleton = AcquireSoul(source, "mon_skeleton", 1, new Vec2(100, 100));
    var golem = AcquireSoul(source, "mon_golem", 31, new Vec2(790, 620));
    if (skeleton is null || golem is null) { failures.Add("Could not prepare Souls for save round-trip."); return; }
    var banner = source.SoulBanners.Starter()!;
    source.SoulBanners.Bind(skeleton.Id, banner.Id); source.SoulBanners.Bind(golem.Id, banner.Id);
    var summoned = source.Summons.Summon(skeleton.Id, banner.Id, Vec2.Zero); source.Allies.TakeDamage(summoned.SummonUid!, 10_000);
    source.Essence.Add("STONE_ESSENCE", 10); source.Bloodline.Add("STONE_SPIRIT_BLOODLINE", 3);
    source.Possession.Start(golem.Id); source.Possession.Update(5); source.World.TryInteract(new Vec2(790, 620));
    var json = sourceApp.CaptureSaveJson();
    using var restored = new GameSession(definitions, 999); restored.Start(); var restoredApp = new GameApplication(restored); restoredApp.RestoreSaveJson(json);
    Equal(restored.Souls.OwnedSouls().Count, 2, "Save restores owned Souls");
    Equal(restored.SoulBanners.Starter()?.BoundSoulIds.Count, 2, "Save restores banner bindings");
    Near(restored.Summons.Runtime(skeleton.Id).RecoverySeconds, 10, 1e-10, "Save restores summon cooldown");
    Equal(restored.Essence.Count("STONE_ESSENCE"), 10, "Save restores Essence");
    Equal(restored.Bloodline.Count("STONE_SPIRIT_BLOODLINE"), 3, "Save restores Bloodline");
    Near(restored.Possession.RemainingSeconds, 25, 1e-10, "Save restores active possession");
    Equal(restored.World.IsDestroyed("obj_cracked_wall_01"), true, "Save restores destroyed world object");
    Equal(GameSaveCodec.Deserialize(json).Version, GameSaveCodec.CurrentVersion, "Current save schema version");

    var replayA = Replay(12345); var replayB = Replay(12345);
    Equal(replayA, replayB, "Deterministic full-session replay save");

    OwnedSoulState? AcquireSoul(GameSession session, string monsterId, int level, Vec2 position)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            var monster = session.SpawnMonster(monsterId, level, position); session.Monsters.TakeDamage(monster.Uid, 10_000_000);
            var drop = session.Souls.WorldSouls().FirstOrDefault(); if (drop is not null) return session.Souls.Acquire(drop.Id);
        }
        return null;
    }

    string Replay(uint seed)
    {
        using var session = new GameSession(definitions, seed); session.Start();
        session.SetInput(new Vec2(1, 1), false); session.Tick(0.25); session.SetInput(Vec2.Zero, false);
        var monster = session.SpawnMonster("mon_skeleton", 1, session.Player.State.Position with { X = session.Player.State.Position.X + 20 });
        session.SetInput(Vec2.Zero, true); for (var index = 0; index < 12 && monster.Alive; index++) session.Tick(0.5);
        session.Essence.Add("STONE_ESSENCE", 3); session.Bloodline.Add("STONE_SPIRIT_BLOODLINE", 3);
        return new GameApplication(session).CaptureSaveJson();
    }
}
