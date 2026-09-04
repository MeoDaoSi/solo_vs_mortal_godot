using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems;

public readonly record struct InventoryItem(string StableId, int Count);
public sealed record TimedPlayerBuff(PillId PillId, double RemainingSeconds, StatModifiers Modifiers);
public sealed record TimedPlayerBuffRestore(PillId PillId, double RemainingSeconds);

public sealed class ProgressionSystem : IDisposable
{
    private readonly Dictionary<string, int> _inventory = new(StringComparer.Ordinal);
    private readonly List<TimedPlayerBuff> _playerBuffs = [];
    private readonly EventBus _events; private readonly SeededRng _rng; private readonly PlayerSystem _player; private readonly PlayerModifierSystem _modifiers; private readonly IDisposable _defeatSubscription;

    public ProgressionSystem(EventBus events, SeededRng rng, PlayerSystem player, PlayerModifierSystem modifiers)
    {
        _events = events; _rng = rng; _player = player; _modifiers = modifiers;
        _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnMonsterDefeated);
    }

    public void AddPlayerXp(double amount)
    {
        if (!double.IsFinite(amount) || amount <= 0) return;
        var player = _player.State;
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
        var count = System.Math.Max(1, amount); var pill = BalanceDefinition.Pills[pillId];
        if (pill.Recipe.Any(entry => Count(MaterialStableId(entry.Key)) < entry.Value * count)) return false;
        foreach (var entry in pill.Recipe) Consume(MaterialStableId(entry.Key), entry.Value * count);
        Gain(pill.StableId, count); _events.Publish(new PillCraftedEvent(pillId, count)); return true;
    }

    public bool UsePlayerStatPill(PillId pillId)
    {
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
