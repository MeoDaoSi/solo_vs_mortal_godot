using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State.V25;

namespace SoloVsMortal.Simulation.Systems;

public readonly record struct InventoryItem(string StableId, int Count);
public sealed record TimedPlayerBuff(PillId PillId, double RemainingSeconds, StatModifiers Modifiers);
public sealed record TimedPlayerBuffRestore(PillId PillId, double RemainingSeconds);

public sealed class ProgressionSystem : IDisposable
{
    private readonly Dictionary<string, int> _inventory = new(StringComparer.Ordinal);
    // Part 3 reward identity guard. Part 5 persists these identities in the canonical receipt ledger.
    private readonly HashSet<string> _canonicalRewardReceipts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _canonicalBreakthroughReceipts = new(StringComparer.Ordinal);
    private readonly List<TimedPlayerBuff> _playerBuffs = [];
    private readonly EventBus _events; private readonly SeededRng _rng; private readonly PlayerSystem _player; private readonly PlayerModifierSystem _modifiers; private readonly CanonicalContentRegistry? _canonical; private readonly MonsterSystem? _monsters; private readonly Func<string>? _canonicalRegionId; private readonly Func<bool>? _atCanonicalShrine; private readonly Func<long>? _canonicalTick; private readonly V25FactLedger? _canonicalFacts; private readonly IDisposable _defeatSubscription;

    public ProgressionSystem(EventBus events, SeededRng rng, PlayerSystem player, PlayerModifierSystem modifiers, CanonicalContentRegistry? canonical = null, MonsterSystem? monsters = null, Func<string>? canonicalRegionId = null, Func<bool>? atCanonicalShrine = null, Func<long>? canonicalTick = null)
    {
        _events = events; _rng = rng; _player = player; _modifiers = modifiers; _canonical = canonical; _monsters = monsters; _canonicalRegionId = canonicalRegionId; _atCanonicalShrine = atCanonicalShrine; _canonicalTick = canonicalTick;
        if (canonical is not null) _canonicalFacts = new V25FactLedger(canonical);
        _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnMonsterDefeated);
    }

    public void AddPlayerXp(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) return;
        var player = _player.State;
        if (_canonical is not null)
        {
            var requested = checked((int)System.Math.Truncate(amount));
            if (requested <= 0) return;
            var canonicalResult = V25ProgressionRules.AddXp(player.Level, player.Xp, requested, _canonical.Profile(_canonical.ActiveProfileId).MaxPlayerLevel, player.BreakthroughReady, _canonical.Balance.Xp);
            if (canonicalResult.Level != player.Level) _player.SetProgression(canonicalResult.Level, V25ProgressionRules.RankFromLevel(canonicalResult.Level));
            player.Xp = canonicalResult.Xp;
            player.BreakthroughReady = canonicalResult.BreakthroughReady;
            var xpToNext = player.Level >= _canonical.Profile(_canonical.ActiveProfileId).MaxPlayerLevel ? 0 : checked((int)V25ProgressionRules.XpRequirement(player.Level, _canonical.Balance.Xp));
            _events.Publish(new PlayerXpGainedEvent(canonicalResult.AcceptedXp, player.Level, player.Xp, xpToNext));
            foreach (var level in canonicalResult.LevelsGained) _events.Publish(new PlayerLevelUpEvent(level, V25ProgressionRules.RankFromLevel(level)));
            return;
        }
        var result = ProgressionRules.AddXp(new XpState(player.Level, player.Xp), (int)System.Math.Truncate(amount), BalanceDefinition.MaximumLevel, ProgressionRules.PlayerRankCap(player.Rank));
        player.Xp = result.Xp;
        if (result.Level != player.Level)
        {
            _player.SetProgression(result.Level, player.Rank);
            _modifiers.Recompute();
        }
        _events.Publish(new PlayerXpGainedEvent((int)System.Math.Truncate(amount), player.Level, player.Xp, player.Level >= BalanceDefinition.MaximumLevel ? 0 : ProgressionRules.XpRequired(player.Level)));
        foreach (var level in result.LevelsGained) _events.Publish(new PlayerLevelUpEvent(level, player.Rank));
    }

    public bool AttemptPlayerBreakthrough(PillId? pillId = null)
    {
        if (_canonical is not null)
        {
            var canonicalPlayer = _player.State;
            var profile = _canonical.Profile(_canonical.ActiveProfileId);
            if (canonicalPlayer.Level >= profile.MaxPlayerLevel || canonicalPlayer.Level % 10 != 0 || !canonicalPlayer.BreakthroughReady) return false;
            var threshold = V25ProgressionRules.XpRequirement(canonicalPlayer.Level, _canonical.Balance.Xp);
            if (canonicalPlayer.Xp < threshold || _atCanonicalShrine?.Invoke() != true) return false;
            var regionId = _canonicalRegionId?.Invoke();
            if (string.IsNullOrWhiteSpace(regionId)) return false;
            var boss = _canonical.Content.Regions.FirstOrDefault(region => region.Id == regionId)?.BossId;
            if (string.IsNullOrWhiteSpace(boss) || _canonicalFacts is null || !_canonicalFacts.Contains($"{boss}.clear")) return false;
            var receiptId = $"breakthrough.r{canonicalPlayer.Rank}.{regionId}";
            if (!_canonicalBreakthroughReceipts.Add(receiptId)) return true;
            var canonicalPreviousRank = canonicalPlayer.Rank;
            var nextLevel = checked(canonicalPlayer.Level + 1);
            _player.SetProgression(nextLevel, V25ProgressionRules.RankFromLevel(nextLevel));
            canonicalPlayer.Xp = 0;
            canonicalPlayer.BreakthroughReady = false;
            _events.Publish(new BreakthroughResultEvent(true, 1, canonicalPreviousRank, canonicalPlayer.Rank, canonicalPlayer.Level, null));
            _events.Publish(new PlayerLevelUpEvent(canonicalPlayer.Level, canonicalPlayer.Rank));
            return true;
        }
        var player = _player.State; var rank = player.Rank;
        if (rank >= BalanceDefinition.MaximumRank || player.Level != ProgressionRules.PlayerRankCap(rank)) return false;
        var bonus = 0.0;
        if (pillId is { } selected)
        {
            var pill = BalanceDefinition.Pills[selected];
            if (pill.BreakthroughChance is null || !Consume(pill.StableId, 1)) return false;
            bonus = pill.BreakthroughChance.Value;
        }
        var chance = System.Math.Min(1, BalanceDefinition.Progression.BreakthroughBaseChance + bonus);
        var success = _rng.Chance(chance); var previousRank = rank;
        player.Xp = 0;
        _player.SetProgression(success ? System.Math.Min(BalanceDefinition.MaximumLevel, ProgressionRules.PlayerRankCap(rank) + 1) : ProgressionRules.BreakthroughFailureLevel(rank), success ? rank + 1 : rank);
        _modifiers.Recompute();
        _events.Publish(new BreakthroughResultEvent(success, chance, previousRank, player.Rank, player.Level, pillId));
        return success;
    }

    public bool Craft(PillId pillId, int amount = 1)
    {
        if (_canonical is not null) return false;
        var count = System.Math.Max(1, amount); var pill = BalanceDefinition.Pills[pillId];
        if (pill.Recipe.Any(entry => Count(MaterialStableId(entry.Key)) < entry.Value * count)) return false;
        foreach (var entry in pill.Recipe) Consume(MaterialStableId(entry.Key), entry.Value * count);
        Gain(pill.StableId, count); _events.Publish(new PillCraftedEvent(pillId, count)); return true;
    }

    public bool UsePlayerStatPill(PillId pillId)
    {
        if (_canonical is not null) return false;
        var pill = BalanceDefinition.Pills[pillId];
        if (pill.Modifiers is null || pill.DurationSeconds is null || !Consume(pill.StableId, 1)) return false;
        var index = _playerBuffs.FindIndex(buff => buff.PillId == pillId);
        var buff = new TimedPlayerBuff(pillId, pill.DurationSeconds.Value, ModifierRules.FromDefinition(pill.Modifiers));
        if (index >= 0) _playerBuffs[index] = buff; else _playerBuffs.Add(buff);
        RecomputeBuffs(); _events.Publish(new TemporaryPlayerBuffChangedEvent(pillId, true, pill.DurationSeconds.Value)); return true;
    }

    public void Update(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        var changed = false;
        for (var index = _playerBuffs.Count - 1; index >= 0; index--)
        {
            var buff = _playerBuffs[index]; var remaining = System.Math.Max(0, buff.RemainingSeconds - deltaSeconds);
            if (remaining == 0) { _playerBuffs.RemoveAt(index); changed = true; _events.Publish(new TemporaryPlayerBuffChangedEvent(buff.PillId, false, 0)); }
            else _playerBuffs[index] = buff with { RemainingSeconds = remaining };
        }
        if (changed) RecomputeBuffs();
    }

    public int Count(string stableItemId) => _inventory.GetValueOrDefault(stableItemId);
    public IReadOnlyList<InventoryItem> InventorySnapshot() => _inventory.Select(entry => new InventoryItem(entry.Key, entry.Value)).OrderBy(entry => entry.StableId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<TimedPlayerBuff> PlayerBuffSnapshot() => _playerBuffs.ToArray();
    public IReadOnlyList<string> CanonicalRewardReceiptSnapshot() => _canonicalRewardReceipts.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25FactEvidence> CanonicalFactSnapshot() => _canonicalFacts?.Snapshot() ?? Array.Empty<V25FactEvidence>();
    public IReadOnlyList<string> CanonicalBreakthroughReceiptSnapshot() => _canonicalBreakthroughReceipts.Order(StringComparer.Ordinal).ToArray();
    public bool HasCanonicalFact(string factId) => _canonicalFacts?.Contains(factId) == true;
    /// <summary>Legitimate quest/world producer seam. The fact ledger validates provenance and
    /// idempotence before this boundary publishes a user-visible commitment.</summary>
    public V25FactIngestResult IngestCanonicalFact(string factId, string producerId, string sourceId, long? producedTick = null)
    {
        if (_canonical is null || _canonicalFacts is null) throw new InvalidOperationException("Canonical content is not enabled.");
        var result = _canonicalFacts.Ingest(new V25FactEvidence(factId, producerId, sourceId, _canonical.Content.ContentVersion, producedTick ?? _canonicalTick?.Invoke() ?? 0));
        if (result.Accepted) _events.Publish(new V25FactCommittedEvent(result.Evidence.FactId, result.Evidence.ProducerId, result.Evidence.SourceId));
        return result;
    }
    public void RestoreCanonicalRewardReceipts(IEnumerable<string> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        var staged = receipts.ToArray();
        if (staged.Any(string.IsNullOrWhiteSpace) || staged.Distinct(StringComparer.Ordinal).Count() != staged.Length) throw new InvalidDataException("Canonical reward receipt ledger is invalid.");
        _canonicalRewardReceipts.Clear(); foreach (var receipt in staged) _canonicalRewardReceipts.Add(receipt);
    }

    public void RestoreCanonicalFactState(IEnumerable<V25FactEvidence> facts, IEnumerable<string> breakthroughReceipts)
    {
        if (_canonical is null || _canonicalFacts is null) throw new InvalidOperationException("Canonical content is not enabled.");
        ArgumentNullException.ThrowIfNull(facts); ArgumentNullException.ThrowIfNull(breakthroughReceipts);
        var factRows = facts.ToArray();
        var receipts = breakthroughReceipts.ToArray();
        if (receipts.Any(string.IsNullOrWhiteSpace) || receipts.Distinct(StringComparer.Ordinal).Count() != receipts.Length)
            throw new InvalidDataException("Canonical breakthrough receipt ledger is invalid.");
        _canonicalFacts.Restore(new V25FactLedgerSnapshot(factRows));
        _canonicalBreakthroughReceipts.Clear();
        foreach (var receipt in receipts) _canonicalBreakthroughReceipts.Add(receipt);
    }

    public void RestoreInventory(IEnumerable<InventoryItem> items)
    {
        _inventory.Clear();
        foreach (var item in items) if (item.Count > 0 && IsKnownItem(item.StableId)) _inventory[item.StableId] = item.Count;
    }

    public void RestorePlayerBuffs(IEnumerable<TimedPlayerBuffRestore> buffs)
    {
        ArgumentNullException.ThrowIfNull(buffs);
        _playerBuffs.Clear();
        foreach (var saved in buffs)
        {
            var pill = BalanceDefinition.Pills[saved.PillId];
            if (pill.Modifiers is null || pill.DurationSeconds is null || !double.IsFinite(saved.RemainingSeconds) || saved.RemainingSeconds <= 0) continue;
            var remaining = System.Math.Min(pill.DurationSeconds.Value, saved.RemainingSeconds);
            var runtimeModifiers = ModifierRules.FromDefinition(pill.Modifiers);
            var index = _playerBuffs.FindIndex(buff => buff.PillId == saved.PillId);
            var restored = new TimedPlayerBuff(saved.PillId, remaining, runtimeModifiers);
            if (index >= 0) _playerBuffs[index] = restored; else _playerBuffs.Add(restored);
        }
        RecomputeBuffs();
    }

    private void OnMonsterDefeated(MonsterDefeatedEvent defeated)
    {
        if (_canonical is not null)
        {
            var rewardEligible = defeated.RewardEligible && _monsters?.Get(defeated.Uid) is { RewardEligible: true };
            var identity = $"{defeated.EncounterId ?? defeated.Uid}:{defeated.TargetLifeUid ?? defeated.Uid}";
            if (rewardEligible && _canonicalRewardReceipts.Add(identity) && _monsters?.Get(defeated.Uid) is { } monster)
            {
                var xp = V25ProgressionRules.KillXp(_player.State.Level, monster.Level, monster.EncounterType, _canonical.Profile(_canonical.ActiveProfileId).MaxPlayerLevel, _canonical.Balance.Xp);
                if (xp > 0) AddPlayerXp(xp);
            }
            if (defeated.EncounterType == V25EncounterType.Boss && defeated.RewardEligible && _canonicalFacts is not null && defeated.EncounterId is { } encounterId && _monsters?.Get(defeated.Uid) is { } boss)
            {
                // Only the authored encounter ID may produce a boss fact. Invalid/manual IDs are
                // ignored as an ineligible producer; no generic monster death is promoted.
                var authored = _canonical.Content.Encounters.FirstOrDefault(encounter => encounter.Id == encounterId);
                if (authored is not null && authored.EncounterType == "Boss" && authored.RewardEligible && authored.SpeciesId == boss.SpeciesId)
                {
                    try
                    {
                        var factResult = _canonicalFacts.IngestBossClear(encounterId, defeated.Uid, _canonicalTick?.Invoke() ?? 0);
                        if (factResult.Accepted)
                            _events.Publish(new V25FactCommittedEvent(factResult.Evidence.FactId, factResult.Evidence.ProducerId, factResult.Evidence.SourceId));
                    }
                    catch (InvalidDataException) { /* preserve the no-fabrication boundary */ }
                }
            }
            return;
        }
        var config = BalanceDefinition.Progression;
        AddPlayerXp(config.MonsterXpBase + config.MonsterXpPerLevel * defeated.Level);
        if (_rng.Chance(config.CrystalDropChance)) Gain(MaterialStableId(MaterialId.SpiritCrystal), System.Math.Max(1, defeated.Rank));
        if (_rng.Chance(config.BeastCoreDropChance)) Gain(MaterialStableId(MaterialId.BeastCore), 1);
    }

    private void Gain(string id, int amount) { var total = Count(id) + amount; _inventory[id] = total; _events.Publish(new ItemGainedEvent(id, amount, total)); }
    private bool Consume(string id, int amount) { if (Count(id) < amount) return false; var left = Count(id) - amount; if (left > 0) _inventory[id] = left; else _inventory.Remove(id); return true; }
    private void RecomputeBuffs() => _modifiers.SetSource(PlayerModifierSource.TemporaryPills, _playerBuffs.Select(buff => buff.Modifiers).ToArray());
    private static string MaterialStableId(MaterialId id) => id switch { MaterialId.SpiritCrystal => "SPIRIT_CRYSTAL", MaterialId.BeastCore => "BEAST_CORE", _ => throw new ArgumentOutOfRangeException(nameof(id)) };
    private static bool IsKnownItem(string id) => id is "SPIRIT_CRYSTAL" or "BEAST_CORE" || BalanceDefinition.Pills.Values.Any(pill => pill.StableId == id);
    public void Dispose() => _defeatSubscription.Dispose();
}
