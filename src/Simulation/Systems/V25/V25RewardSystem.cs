using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.Systems.V25;

public sealed record V25LootAward(
    string AwardId,
    string TargetLifeUid,
    string EncounterId,
    V25EncounterType EncounterType,
    int Rank,
    long Coins,
    string? ItemDefinitionId,
    int? ItemRank,
    string SourceVersion);

public sealed record V25LootSnapshot(IReadOnlyList<V25LootAward> Awards);

/// <summary>Canonical one roll combat loot owner. The event identity is the target life, so a
/// save retry or a duplicate defeat notification cannot spend the item stream twice.</summary>
public sealed class V25LootSystem : IDisposable
{
    private readonly CanonicalContentRegistry _canonical;
    private readonly V25InventorySystem _inventory;
    private readonly EventBus _events;
    private readonly Pcg32 _itemDrop;
    private readonly Pcg32 _itemFamily;
    private readonly Dictionary<string, V25LootAward> _awards = new(StringComparer.Ordinal);
    private readonly IDisposable _defeatSubscription;

    public V25LootSystem(CanonicalContentRegistry canonical, EventBus events, V25InventorySystem inventory,
        Pcg32 itemDrop, Pcg32 itemFamily)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical)); _events = events ?? throw new ArgumentNullException(nameof(events));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory)); _itemDrop = itemDrop ?? throw new ArgumentNullException(nameof(itemDrop)); _itemFamily = itemFamily ?? throw new ArgumentNullException(nameof(itemFamily));
        _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnMonsterDefeated);
    }

    public IReadOnlyList<V25LootAward> Awards => _awards.Values.OrderBy(item => item.AwardId, StringComparer.Ordinal).ToArray();

    private void OnMonsterDefeated(MonsterDefeatedEvent defeated)
    {
        if (!defeated.RewardEligible || defeated.EncounterType is V25EncounterType.Arena or V25EncounterType.Debug) return;
        var targetLife = defeated.TargetLifeUid ?? defeated.Uid;
        var awardId = $"loot.{defeated.EncounterId ?? defeated.Uid}.{targetLife}";
        if (_awards.ContainsKey(awardId)) return;
        var factor = V25EncounterFactors.For(defeated.EncounterType);
        var coins = checked(3L * defeated.Rank * checked((long)factor.Coins));
        var item = RollItem(defeated.Rank, defeated.EncounterType);
        // Inventory overflow is canonical stash behavior, so an eligible roll always has a
        // deterministic destination and is never rerolled because the 60 carried slots are full.
        if (coins > 0 && !_inventory.AddCoins(coins)) throw new InvalidDataException("Canonical coin reward overflowed int64.");
        if (item is not null && !_inventory.AddItem(item.Id, 1, allowOverflow: true).Success)
            throw new InvalidDataException($"Canonical loot item '{item.Id}' could not be placed in inventory/stash.");
        var award = new V25LootAward(awardId, targetLife, defeated.EncounterId ?? defeated.Uid, defeated.EncounterType, defeated.Rank, coins, item?.Id, item?.Rank, _canonical.Content.ContentVersion);
        _awards.Add(awardId, award);
    }

    private CanonicalEquipmentDefinition? RollItem(int rank, V25EncounterType encounterType)
    {
        var chance = encounterType switch { V25EncounterType.Normal => 0.10, V25EncounterType.Elite => 0.30, V25EncounterType.Boss => 1.0, _ => 0 };
        if (chance <= 0 || chance < 1 && !_itemDrop.Chance(chance)) return null;
        var profile = _canonical.Profile(_canonical.ActiveProfileId);
        var itemRank = Math.Min(rank, profile.MaxEquipmentRank);
        var options = _canonical.EquipmentForProfile(profile.Id).Where(item => item.Rank == itemRank).GroupBy(item => item.Family, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        if (options.Length == 0) return null;
        var family = options[_itemFamily.Int(0, options.Length - 1)];
        return family.OrderBy(item => item.Id, StringComparer.Ordinal).First();
    }

    public V25LootSnapshot Snapshot() => new(Awards);

    public void Restore(V25LootSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var staged = new Dictionary<string, V25LootAward>(StringComparer.Ordinal);
        foreach (var award in snapshot.Awards ?? throw new InvalidDataException("Canonical loot ledger is missing."))
        {
            if (award is null || !staged.TryAdd(award.AwardId, award) || string.IsNullOrWhiteSpace(award.TargetLifeUid) || string.IsNullOrWhiteSpace(award.EncounterId) || !Enum.IsDefined(award.EncounterType) || award.Rank is < 1 or > 9 || award.Coins < 0 || award.ItemRank is < 1 or > 9 || award.ItemDefinitionId is not null && !_canonical.Content.Equipment.Any(item => item.Id == award.ItemDefinitionId) || award.SourceVersion != _canonical.Content.ContentVersion)
                throw new InvalidDataException("Canonical loot ledger row is invalid or duplicated.");
        }
        _awards.Clear(); foreach (var award in staged) _awards.Add(award.Key, award.Value);
    }

    public void Dispose() => _defeatSubscription.Dispose();
}

public sealed record V25UniquePowerState(string PowerId, string ReceiptId, long UnlockedTick, string HostBossId, int PowerRank);
public sealed record V25UniquePowerSnapshot(IReadOnlyList<V25UniquePowerState> Powers);
public sealed record V25RitualProgress(bool Started, bool Completed, string? PowerId, int RemainingTicks, string? Failure = null);

/// <summary>Owns unique Fragment/Entity collection independently from Species Soul ownership.
/// Only authored facts unlock a power; there is no capture roll and no implicit Sync requirement.
/// Beta exposes Chaos only while preserving full profile rows when the content profile expands.</summary>
public sealed class V25UniquePowerSystem : IDisposable
{
    private readonly CanonicalContentRegistry _canonical;
    private readonly EventBus _events;
    private readonly Func<string, bool> _hasFact;
    private readonly Func<bool> _atShrine;
    private readonly Func<Vec2> _playerPosition;
    private readonly Func<long> _tick;
    private readonly Dictionary<string, V25UniquePowerState> _owned = new(StringComparer.Ordinal);
    private readonly IDisposable _factSubscription;
    private readonly IDisposable _defeatSubscription;
    private readonly IDisposable _playerDamagedSubscription;
    private readonly IDisposable _playerDefeatedSubscription;
    private ActiveRitual? _activeRitual;

    public V25UniquePowerSystem(CanonicalContentRegistry canonical, EventBus events, Func<string, bool> hasFact, Func<bool> atShrine, Func<Vec2> playerPosition, Func<long> tick)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical)); _events = events ?? throw new ArgumentNullException(nameof(events)); _hasFact = hasFact ?? throw new ArgumentNullException(nameof(hasFact)); _atShrine = atShrine ?? throw new ArgumentNullException(nameof(atShrine)); _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _playerPosition = playerPosition ?? throw new ArgumentNullException(nameof(playerPosition));
        _factSubscription = events.Subscribe<V25FactCommittedEvent>(_ => EvaluateEligible());
        _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(_ => EvaluateEligible());
        _playerDamagedSubscription = events.Subscribe<PlayerDamagedEvent>(_ => CancelRitual());
        _playerDefeatedSubscription = events.Subscribe<PlayerDefeatedEvent>(_ => CancelRitual());
    }

    public IReadOnlyList<V25UniquePowerState> Powers => _owned.Values.OrderBy(item => item.PowerId, StringComparer.Ordinal).ToArray();
    public V25RitualProgress RitualProgress
    {
        get
        {
            if (_activeRitual is not { } ritual) return new(false, false, null, 0);
            var elapsed = checked((int)Math.Min(int.MaxValue, _tick() - ritual.StartedTick));
            return new(true, false, ritual.PowerId, Math.Max(0, ritual.DurationTicks - elapsed));
        }
    }
    public bool IsOwned(string powerId) => _owned.ContainsKey(powerId);
    public bool IsSkillGranted(string skillId) => _canonical.UniqueSkillsForProfile(_canonical.ActiveProfileId).Any(skill => skill.Id == skillId && _owned.Values.Any(power => power.PowerId == _canonical.Content.UniquePowers.First(item => item.SkillId == skillId).Id));
    public int PowerRank(string skillId) => _owned.Values.Where(power => _canonical.Content.UniquePowers.FirstOrDefault(item => item.Id == power.PowerId)?.SkillId == skillId).Select(power => power.PowerRank).DefaultIfEmpty(1).Max();

    /// <summary>Attempts a shrine claim for a full-profile Entity. Fragment rewards with zero
    /// ritual time are auto-claimable once facts are committed. The beta path therefore unlocks
    /// Chaos after boss.r01 while Despair/Covenant remain unavailable by the profile gate.</summary>
    public bool Claim(string powerId)
    {
        var definition = _canonical.Content.UniquePowers.FirstOrDefault(item => item.Id == powerId);
        if (definition is null || definition.RitualMs > 0 || !definition.Profiles.Contains(_canonical.ActiveProfileId, StringComparer.Ordinal) || _owned.ContainsKey(powerId) || !RequirementsMet(definition)) return false;
        Commit(definition);
        return true;
    }

    /// <summary>Starts the one authored hold ritual. It is transient by design: no award exists until completion.</summary>
    public V25RitualProgress BeginAvailableRitual()
    {
        if (_activeRitual is { } active) return RitualProgress;
        var definition = _canonical.Content.UniquePowers.Where(power => power.RitualMs > 0 && power.Profiles.Contains(_canonical.ActiveProfileId, StringComparer.Ordinal) && !_owned.ContainsKey(power.Id) && RequirementsMet(power))
            .OrderBy(power => power.Id, StringComparer.Ordinal).FirstOrDefault();
        if (definition is null) return new(false, false, null, 0, "NoEligibleRitual");
        if (!_atShrine()) return new(false, false, definition.Id, 0, "NotAtShrine");
        var durationTicks = V25CombatRules.MillisecondsToTicksCeil(definition.RitualMs);
        _activeRitual = new ActiveRitual(definition.Id, _tick(), _playerPosition(), durationTicks);
        return RitualProgress;
    }

    /// <summary>Advances only while E remains held. Moving, damage, failed facts, or leaving the shrine cancels without a receipt.</summary>
    public V25RitualProgress AdvanceRitual(bool held)
    {
        if (_activeRitual is not { } ritual) return new(false, false, null, 0, "NoActiveRitual");
        if (!held) return CancelRitual("Released");
        var definition = _canonical.Content.UniquePowers.FirstOrDefault(power => power.Id == ritual.PowerId);
        if (definition is null || !definition.Profiles.Contains(_canonical.ActiveProfileId, StringComparer.Ordinal) || _owned.ContainsKey(ritual.PowerId) || !RequirementsMet(definition))
            return CancelRitual("RequirementsChanged");
        if (!_atShrine() || _playerPosition().DistanceTo(ritual.StartPosition) > 1) return CancelRitual("MovedOrLeftShrine");
        var elapsed = checked((int)Math.Min(int.MaxValue, _tick() - ritual.StartedTick));
        var remaining = Math.Max(0, ritual.DurationTicks - elapsed);
        if (remaining > 0) return new(true, false, ritual.PowerId, remaining);
        _activeRitual = null;
        Commit(definition);
        return new(false, true, definition.Id, 0);
    }

    public V25RitualProgress CancelRitual(string failure = "Canceled")
    {
        if (_activeRitual is not { } ritual) return new(false, false, null, 0, failure);
        _activeRitual = null;
        return new(false, false, ritual.PowerId, 0, failure);
    }

    public V25UniquePowerSnapshot Snapshot() => new(Powers);

    public void Restore(V25UniquePowerSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var staged = new Dictionary<string, V25UniquePowerState>(StringComparer.Ordinal);
        foreach (var power in snapshot.Powers ?? throw new InvalidDataException("Canonical unique power snapshot is missing."))
        {
            var definition = _canonical.Content.UniquePowers.FirstOrDefault(item => item.Id == power.PowerId)
                ?? throw new InvalidDataException($"Unknown canonical unique power '{power.PowerId}'.");
            if (!definition.Profiles.Contains(_canonical.ActiveProfileId, StringComparer.Ordinal) || power.ReceiptId != definition.ReceiptId || power.HostBossId != definition.HostBossId || power.PowerRank != definition.PowerRank || power.UnlockedTick < 0 || !staged.TryAdd(power.PowerId, power))
                throw new InvalidDataException("Canonical unique power snapshot is invalid or duplicated.");
        }
        _owned.Clear(); foreach (var power in staged) _owned.Add(power.Key, power.Value);
    }

    private void EvaluateEligible()
    {
        foreach (var definition in _canonical.Content.UniquePowers.Where(power => power.Profiles.Contains(_canonical.ActiveProfileId, StringComparer.Ordinal) && !_owned.ContainsKey(power.Id)).OrderBy(power => power.Id, StringComparer.Ordinal))
            if (RequirementsMet(definition) && definition.RitualMs == 0) Commit(definition);
    }

    private bool RequirementsMet(CanonicalUniquePowerDefinition definition) => definition.RequiredFacts.All(fact => fact == "all_region_seals" ? _canonical.RegionsForProfile(_canonical.ActiveProfileId).All(region => _hasFact($"region.{region.Id}.seal")) : _hasFact(fact));

    private void Commit(CanonicalUniquePowerDefinition definition)
    {
        var state = new V25UniquePowerState(definition.Id, definition.ReceiptId, _tick(), definition.HostBossId, definition.PowerRank);
        _owned.Add(definition.Id, state);
        _events.Publish(new V25UniquePowerUnlockedEvent(definition.Id, definition.ReceiptId, definition.HostBossId, definition.PowerRank));
    }

    public void Dispose() { _factSubscription.Dispose(); _defeatSubscription.Dispose(); _playerDamagedSubscription.Dispose(); _playerDefeatedSubscription.Dispose(); }

    private sealed record ActiveRitual(string PowerId, long StartedTick, Vec2 StartPosition, int DurationTicks);
}
