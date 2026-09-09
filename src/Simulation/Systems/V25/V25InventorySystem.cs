using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems.V25;

public enum V25EquipmentSlot { MainHand, OffHand, Head, Chest, Hands, Feet, Accessory1, Accessory2 }
public enum V25InventoryFailure { UnknownItem, InvalidAmount, InventoryFull, InsufficientCoins, RankTooLow, InCombat, HazardActive, Transition, AccessoryFamilyConflict, Equipped, NotFound, ResourceFull, Cooldown, InvalidState }
public sealed record V25ItemInstance(string InstanceUid, string DefinitionId, int Count = 1);
public sealed record V25EquippedItem(V25EquipmentSlot Slot, string InstanceUid, string DefinitionId);
public sealed record V25InventorySnapshot(long Coins, IReadOnlyList<V25ItemInstance> Items, IReadOnlyList<V25ItemInstance> Overflow, IReadOnlyList<V25EquippedItem> Equipped, int SharedPotionCooldownTicks, int NextInstance = 0);
public sealed record V25InventoryResult(bool Success, V25InventoryFailure? Failure = null, string? InstanceUid = null);

/// <summary>Canonical 60-slot inventory and eight-slot loadout owner.</summary>
public sealed class V25InventorySystem
{
    public const int InventorySlotCapacity = 60;
    public const int ConsumableStackMaximum = 99;
    private readonly CanonicalContentRegistry _canonical;
    private readonly PlayerSystem _player;
    private readonly PlayerModifierSystem _modifiers;
    private readonly EventBus _events;
    private readonly Func<bool> _combatActive;
    private readonly Func<bool> _hazardActive;
    private readonly Func<bool> _transitionLocked;
    private readonly Dictionary<string, V25ItemInstance> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, V25ItemInstance> _overflow = new(StringComparer.Ordinal);
    private readonly Dictionary<V25EquipmentSlot, V25EquippedItem> _equipped = new();
    private int _nextInstance;
    private long _coins;
    private int _potionCooldownTicks;

    public V25InventorySystem(CanonicalContentRegistry canonical, PlayerSystem player, PlayerModifierSystem modifiers, EventBus events,
        Func<bool> combatActive, Func<bool> hazardActive, Func<bool> transitionLocked)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical)); _player = player ?? throw new ArgumentNullException(nameof(player));
        _modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers)); _events = events ?? throw new ArgumentNullException(nameof(events));
        _combatActive = combatActive ?? throw new ArgumentNullException(nameof(combatActive)); _hazardActive = hazardActive ?? throw new ArgumentNullException(nameof(hazardActive)); _transitionLocked = transitionLocked ?? throw new ArgumentNullException(nameof(transitionLocked));
    }

    public long Coins => _coins;
    public IReadOnlyList<V25ItemInstance> Items => _items.Values.OrderBy(item => item.DefinitionId, StringComparer.Ordinal).ThenBy(item => item.InstanceUid, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25ItemInstance> Overflow => _overflow.Values.OrderBy(item => item.DefinitionId, StringComparer.Ordinal).ThenBy(item => item.InstanceUid, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25EquippedItem> Equipped => _equipped.OrderBy(item => item.Key).Select(item => item.Value).ToArray();
    public int SharedPotionCooldownTicks => _potionCooldownTicks;
    public string? MainHandFamily => EquippedDefinition(V25EquipmentSlot.MainHand)?.Family;
    public string? OffHandFamily => EquippedDefinition(V25EquipmentSlot.OffHand)?.Family;
    public IReadOnlyDictionary<string, int> EquipmentSkillRanks => _equipped.Values
        .Select(item => _canonical.Content.Equipment.First(definition => definition.Id == item.DefinitionId))
        .Where(item => item.GrantedSkillId is not null)
        .GroupBy(item => item.GrantedSkillId!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Max(item => item.Rank), StringComparer.Ordinal);

    public void InitializeNewGame()
    {
        if (_coins != 0 || _items.Count != 0 || _overflow.Count != 0 || _equipped.Count != 0) throw new InvalidOperationException("Canonical inventory has already been initialized.");
        _coins = 100;
        AddItemInternal("hp_potion", 3, true); AddItemInternal("spirit_potion", 3, true);
        foreach (var slot in new[] { V25EquipmentSlot.MainHand, V25EquipmentSlot.Head, V25EquipmentSlot.Chest, V25EquipmentSlot.Hands, V25EquipmentSlot.Feet })
        {
            var definition = EquipmentForSlot(slot).FirstOrDefault(item => item.Rank == 1)
                ?? throw new InvalidDataException($"Canonical beta profile has no Rank1 equipment for slot '{slot}'.");
            var item = NewInstance(definition.Id);
            _equipped[slot] = new V25EquippedItem(slot, item.InstanceUid, definition.Id);
        }
        ApplyEquipmentModifiers();
    }

    public void Tick(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return;
        _potionCooldownTicks = Math.Max(0, _potionCooldownTicks - Math.Max(1, (int)Math.Round(deltaSeconds * 60, MidpointRounding.AwayFromZero)));
    }

    public V25InventoryResult AddItem(string definitionId, int amount = 1, bool allowOverflow = true)
    {
        if (amount <= 0) return new(false, V25InventoryFailure.InvalidAmount);
        if (!KnownItem(definitionId)) return new(false, V25InventoryFailure.UnknownItem);
        return AddItemInternal(definitionId, amount, allowOverflow);
    }

    /// <summary>Adds canonical coins after a validated reward receipt. Coins are int64 and are
    /// intentionally separate from the legacy material inventory.</summary>
    public bool AddCoins(long amount)
    {
        if (amount <= 0) return false;
        _coins = checked(_coins + amount);
        return true;
    }
    public bool CanAddCoins(long amount) => amount > 0 && _coins <= long.MaxValue - amount;

    public V25InventoryResult Buy(string definitionId, int amount = 1)
    {
        var item = _canonical.Content.Equipment.FirstOrDefault(value => value.Id == definitionId);
        var consumable = _canonical.Content.Consumables.FirstOrDefault(value => value.Id == definitionId);
        if (item is not null && (!_canonical.EquipmentForProfile(_canonical.ActiveProfileId).Any(e => e.Id == item.Id) || item.Rank > _player.State.Rank)) return new(false, V25InventoryFailure.RankTooLow);
        if (item is null && consumable is null) return new(false, V25InventoryFailure.UnknownItem);
        if (amount <= 0) return new(false, V25InventoryFailure.InvalidAmount);
        var price = checked((long)(item?.BuyPrice ?? consumable!.Price)) * amount;
        if (_coins < price) return new(false, V25InventoryFailure.InsufficientCoins);
        var result = AddItemInternal(definitionId, amount, true);
        if (!result.Success) return result;
        _coins -= price;
        return result;
    }

    public V25InventoryResult Sell(string instanceUid)
    {
        var found = FindCarried(instanceUid);
        if (found is null) return new(false, V25InventoryFailure.NotFound);
        if (_equipped.Values.Any(item => item.InstanceUid == instanceUid)) return new(false, V25InventoryFailure.Equipped);
        var definition = _canonical.Content.Equipment.FirstOrDefault(item => item.Id == found.DefinitionId);
        if (definition is null) return new(false, V25InventoryFailure.UnknownItem);
        if (!_items.Remove(instanceUid) && !_overflow.Remove(instanceUid)) return new(false, V25InventoryFailure.NotFound);
        _coins = checked(_coins + (long)Math.Floor(definition.BuyPrice * 0.25) * found.Count);
        return new(true, InstanceUid: instanceUid);
    }

    public V25InventoryResult Equip(string instanceUid)
    {
        if (!CanChangeLoadout(out var failure)) return new(false, failure);
        var carried = FindCarried(instanceUid);
        if (carried is null) return new(false, V25InventoryFailure.NotFound);
        var definition = _canonical.Content.Equipment.FirstOrDefault(item => item.Id == carried.DefinitionId);
        if (definition is null) return new(false, V25InventoryFailure.UnknownItem);
        var profile = _canonical.Profile(_canonical.ActiveProfileId);
        if (definition.Rank > Math.Min(_player.State.Rank, profile.MaxEquipmentRank)) return new(false, V25InventoryFailure.RankTooLow);
        var slot = ParseSlot(definition.Slot, instanceUid);
        if (slot is V25EquipmentSlot.Accessory1 or V25EquipmentSlot.Accessory2 && _equipped.Values.Any(item => item.DefinitionId == definition.Id && item.InstanceUid != instanceUid))
            return new(false, V25InventoryFailure.AccessoryFamilyConflict);
        if (slot is V25EquipmentSlot.Accessory1 or V25EquipmentSlot.Accessory2)
        {
            var family = definition.Family;
            if (_equipped.Any(pair => pair.Key is V25EquipmentSlot.Accessory1 or V25EquipmentSlot.Accessory2 && pair.Value.InstanceUid != instanceUid && _canonical.Content.Equipment.First(item => item.Id == pair.Value.DefinitionId).Family == family))
                return new(false, V25InventoryFailure.AccessoryFamilyConflict);
        }
        var displaced = new List<V25EquippedItem>();
        if (_equipped.TryGetValue(slot, out var current) && current.InstanceUid != instanceUid) displaced.Add(current);
        if (definition.TwoHanded && slot == V25EquipmentSlot.MainHand && _equipped.TryGetValue(V25EquipmentSlot.OffHand, out var offhand)) displaced.Add(offhand);
        // Validate every destination before mutating, then move the whole loadout atomically.
        var removed = RemoveCarried(instanceUid); if (!removed) return new(false, V25InventoryFailure.NotFound);
        foreach (var old in displaced) _equipped.Remove(old.Slot);
        foreach (var old in displaced) AddItemInternal(old.DefinitionId, 1, true, old.InstanceUid);
        _equipped[slot] = new V25EquippedItem(slot, carried.InstanceUid, carried.DefinitionId);
        ApplyEquipmentModifiers();
        return new(true, InstanceUid: instanceUid);
    }

    public V25InventoryResult Unequip(V25EquipmentSlot slot)
    {
        if (!CanChangeLoadout(out var failure)) return new(false, failure);
        if (!_equipped.TryGetValue(slot, out var current)) return new(false, V25InventoryFailure.NotFound);
        var added = AddItemInternal(current.DefinitionId, 1, true, current.InstanceUid);
        if (!added.Success) return added;
        _equipped.Remove(slot); ApplyEquipmentModifiers(); return new(true, InstanceUid: current.InstanceUid);
    }

    public V25InventoryResult UsePotion(string definitionId)
    {
        if (_potionCooldownTicks > 0) return new(false, V25InventoryFailure.Cooldown);
        var potion = _canonical.Content.Consumables.FirstOrDefault(item => item.Id == definitionId);
        if (potion is null) return new(false, V25InventoryFailure.UnknownItem);
        if (potion.Resource == "HP" && _player.State.CurrentHp >= _player.State.MaxHp) return new(false, V25InventoryFailure.ResourceFull);
        if (potion.Resource == "Spirit" && _player.State.CurrentSpirit >= _player.State.MaxSpirit) return new(false, V25InventoryFailure.ResourceFull);
        var carried = _items.Values.FirstOrDefault(item => item.DefinitionId == definitionId && item.Count > 0);
        if (carried is null) return new(false, V25InventoryFailure.NotFound);
        if (!ConsumeCarried(carried.InstanceUid, 1)) return new(false, V25InventoryFailure.NotFound);
        if (potion.Resource == "HP") _player.State.CurrentHp = V25FixedPoint.QuantizeMilli(Math.Min(_player.State.MaxHp, _player.State.CurrentHp + _player.State.MaxHp * potion.MaxFraction));
        else _player.State.CurrentSpirit = V25FixedPoint.QuantizeMilli(Math.Min(_player.State.MaxSpirit, _player.State.CurrentSpirit + _player.State.MaxSpirit * potion.MaxFraction));
        _potionCooldownTicks = 600; return new(true, InstanceUid: carried.InstanceUid);
    }

    public V25InventoryResult WithdrawOverflow(string instanceUid)
    {
        if (!_overflow.TryGetValue(instanceUid, out var item)) return new(false, V25InventoryFailure.NotFound);
        if (_items.Count >= InventorySlotCapacity) return new(false, V25InventoryFailure.InventoryFull);
        _items.Add(instanceUid, item); _overflow.Remove(instanceUid);
        return new(true, InstanceUid: instanceUid);
    }

    public V25InventorySnapshot Snapshot() => new(_coins, Items, Overflow, Equipped, _potionCooldownTicks, _nextInstance);

    public void Restore(V25InventorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var itemIds = new HashSet<string>(StringComparer.Ordinal); var instanceIds = new HashSet<string>(StringComparer.Ordinal);
        var items = ValidateItems(snapshot.Items, itemIds, instanceIds); var overflow = ValidateItems(snapshot.Overflow, itemIds, instanceIds, carried: false);
        if (snapshot.NextInstance < 0 || snapshot.Coins < 0 || snapshot.SharedPotionCooldownTicks is < 0 or > 600) throw new InvalidDataException("Canonical inventory coins/cooldown are invalid.");
        var slots = new Dictionary<V25EquipmentSlot, V25EquippedItem>();
        foreach (var item in snapshot.Equipped)
        {
            if (!Enum.IsDefined(item.Slot) || !slots.TryAdd(item.Slot, item) || !instanceIds.Add(item.InstanceUid) || !KnownEquipment(item.DefinitionId)) throw new InvalidDataException("Canonical equipped item is invalid or duplicated.");
            var definition = _canonical.Content.Equipment.First(value => value.Id == item.DefinitionId);
            if (definition.Slot == "Accessory" && item.Slot is not (V25EquipmentSlot.Accessory1 or V25EquipmentSlot.Accessory2)) throw new InvalidDataException("Accessory is in a non-accessory slot.");
            if (definition.Slot != "Accessory" && ParseSlot(definition.Slot, item.InstanceUid) != item.Slot) throw new InvalidDataException("Equipped item slot does not match definition.");
        }
        if (slots.Values.Select(item => _canonical.Content.Equipment.First(definition => definition.Id == item.DefinitionId).Family).Where((_, i) => slots.Keys.ElementAt(i) is V25EquipmentSlot.Accessory1 or V25EquipmentSlot.Accessory2).GroupBy(value => value, StringComparer.Ordinal).Any(group => group.Count() > 1)) throw new InvalidDataException("Duplicate accessory family in loadout.");
        _items.Clear(); foreach (var item in items) _items.Add(item.InstanceUid, item);
        _overflow.Clear(); foreach (var item in overflow) _overflow.Add(item.InstanceUid, item);
        _equipped.Clear(); foreach (var item in slots) _equipped.Add(item.Key, item.Value);
        _coins = snapshot.Coins; _potionCooldownTicks = snapshot.SharedPotionCooldownTicks; _nextInstance = Math.Max(snapshot.NextInstance, instanceIds.Select(id => id.StartsWith("item.", StringComparison.Ordinal) && int.TryParse(id[5..], out var number) ? number : 0).DefaultIfEmpty().Max()); ApplyEquipmentModifiers();
    }

    public IReadOnlyList<StatModifiers> EquipmentModifiers() => _equipped.Values.Select(item => _canonical.Content.Equipment.First(definition => definition.Id == item.DefinitionId)).Select(ToModifiers).ToArray();

    private V25InventoryResult AddItemInternal(string definitionId, int amount, bool allowOverflow, string? forcedInstanceUid = null)
    {
        var consumable = _canonical.Content.Consumables.FirstOrDefault(item => item.Id == definitionId);
        if (consumable is not null)
        {
            var remaining = amount;
            foreach (var entry in _items.Values.Concat(_overflow.Values).Where(item => item.DefinitionId == definitionId && item.Count < ConsumableStackMaximum).ToArray())
            {
                var add = Math.Min(remaining, ConsumableStackMaximum - entry.Count); var next = entry with { Count = entry.Count + add };
                if (_items.ContainsKey(entry.InstanceUid)) _items[entry.InstanceUid] = next; else _overflow[entry.InstanceUid] = next;
                remaining -= add; if (remaining == 0) return new(true, InstanceUid: entry.InstanceUid);
            }
            while (remaining > 0)
            {
                var add = Math.Min(remaining, ConsumableStackMaximum); var instance = NewInstance(definitionId, forcedInstanceUid, add); forcedInstanceUid = null;
                if (_items.Count < InventorySlotCapacity) _items.Add(instance.InstanceUid, instance); else if (allowOverflow) _overflow.Add(instance.InstanceUid, instance); else return new(false, V25InventoryFailure.InventoryFull);
                remaining -= add;
            }
            return new(true);
        }
        if (!KnownEquipment(definitionId) || amount != 1) return new(false, V25InventoryFailure.InvalidAmount);
        var equipment = NewInstance(definitionId, forcedInstanceUid);
        if (_items.Count < InventorySlotCapacity) _items.Add(equipment.InstanceUid, equipment); else if (allowOverflow) _overflow.Add(equipment.InstanceUid, equipment); else return new(false, V25InventoryFailure.InventoryFull);
        return new(true, InstanceUid: equipment.InstanceUid);
    }

    private bool KnownItem(string definitionId) => KnownEquipment(definitionId) || _canonical.Content.Consumables.Any(item => item.Id == definitionId);
    private bool KnownEquipment(string definitionId) => _canonical.Content.Equipment.Any(item => item.Id == definitionId);
    private V25ItemInstance NewInstance(string definitionId, string? forced = null, int count = 1) => new(forced ?? $"item.{++_nextInstance}", definitionId, count);
    private V25ItemInstance? FindCarried(string uid) => _items.GetValueOrDefault(uid);
    private bool RemoveCarried(string uid) => _items.Remove(uid) || _overflow.Remove(uid);
    private bool ConsumeCarried(string uid, int amount)
    {
        if (amount <= 0) return false;
        if (_items.TryGetValue(uid, out var item)) { if (item.Count < amount) return false; if (item.Count == amount) _items.Remove(uid); else _items[uid] = item with { Count = item.Count - amount }; return true; }
        if (_overflow.TryGetValue(uid, out item)) { if (item.Count < amount) return false; if (item.Count == amount) _overflow.Remove(uid); else _overflow[uid] = item with { Count = item.Count - amount }; return true; }
        return false;
    }
    private bool CanChangeLoadout(out V25InventoryFailure? failure)
    {
        if (_combatActive()) { failure = V25InventoryFailure.InCombat; return false; }
        if (_hazardActive()) { failure = V25InventoryFailure.HazardActive; return false; }
        if (_transitionLocked()) { failure = V25InventoryFailure.Transition; return false; }
        failure = null; return true;
    }
    private IEnumerable<CanonicalEquipmentDefinition> EquipmentForSlot(V25EquipmentSlot slot) => _canonical.EquipmentForProfile(_canonical.ActiveProfileId).Where(item => slot is V25EquipmentSlot.Accessory1 or V25EquipmentSlot.Accessory2 ? item.Slot == "Accessory" : item.Slot == slot.ToString());
    private V25EquipmentSlot ParseSlot(string slot, string uid) => slot switch { "MainHand" => V25EquipmentSlot.MainHand, "OffHand" => V25EquipmentSlot.OffHand, "Head" => V25EquipmentSlot.Head, "Chest" => V25EquipmentSlot.Chest, "Hands" => V25EquipmentSlot.Hands, "Feet" => V25EquipmentSlot.Feet, "Accessory" => _equipped.ContainsKey(V25EquipmentSlot.Accessory1) ? V25EquipmentSlot.Accessory2 : V25EquipmentSlot.Accessory1, _ => throw new InvalidDataException($"Unknown equipment slot '{slot}' for '{uid}'.") };
    private List<V25ItemInstance> ValidateItems(IReadOnlyList<V25ItemInstance> source, HashSet<string> itemIds, HashSet<string> instanceIds, bool carried = true)
    {
        if (source is null) throw new InvalidDataException("Canonical inventory collection is missing.");
        var result = new List<V25ItemInstance>();
        foreach (var item in source)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.InstanceUid) || !instanceIds.Add(item.InstanceUid) || !KnownItem(item.DefinitionId) || item.Count <= 0 || item.Count > ConsumableStackMaximum || !itemIds.Add(item.InstanceUid)) throw new InvalidDataException("Canonical inventory item is invalid or duplicated.");
            if (KnownEquipment(item.DefinitionId) && item.Count != 1) throw new InvalidDataException("Equipment instances cannot stack.");
            result.Add(item);
        }
        if (carried && result.Count > InventorySlotCapacity) throw new InvalidDataException("Canonical inventory exceeds 60 carried slots.");
        return result;
    }
    private void ApplyEquipmentModifiers() => _modifiers.SetSource(PlayerModifierSource.Equipment, EquipmentModifiers());
    private CanonicalEquipmentDefinition? EquippedDefinition(V25EquipmentSlot slot) => _equipped.TryGetValue(slot, out var item) ? _canonical.Content.Equipment.FirstOrDefault(definition => definition.Id == item.DefinitionId) : null;
    private static StatModifiers ToModifiers(CanonicalEquipmentDefinition definition)
    {
        var values = definition.Modifiers;
        return new(values.GetValueOrDefault("HPPercent"), values.GetValueOrDefault("ATKPercent"), values.GetValueOrDefault("DEFPercent"), values.GetValueOrDefault("MoveSpeedPercent"), SpiritCapacityPercent: values.GetValueOrDefault("SpiritCapacityPercent"), SpiritCapacityFlat: values.GetValueOrDefault("SpiritCapacityFlat"), SpiritRegenPercent: values.GetValueOrDefault("SpiritRegenPercent"), SpiritRegenFlat: values.GetValueOrDefault("SpiritRegenFlat"));
    }
}
