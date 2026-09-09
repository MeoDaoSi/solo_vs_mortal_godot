using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Data.Definitions.V25;

namespace SoloVsMortal.Simulation.Systems.V25;

public sealed record V25SkillGrantSnapshot(
    IReadOnlyList<string> LearnedSkillIds,
    IReadOnlyList<string?> ActiveSkillIds,
    IReadOnlyList<string?> PassiveSkillIds,
    IReadOnlyDictionary<string, int> PromotedRanks);

/// <summary>Resolves canonical skill ownership, exposure policy, weapon grants and effective power rank.</summary>
public sealed class V25SkillGrantSystem
{
    private const int ActiveSlotCount = 6;
    private const int PassiveSlotCount = 3;
    private readonly CanonicalContentRegistry _canonical;
    private readonly PlayerSystem _player;
    private readonly V25InventorySystem _inventory;
    private readonly Func<string, bool> _possessionGrant;
    private readonly Func<string, int> _possessionRank;
    private readonly Func<string, bool> _uniqueGrant;
    private readonly Func<string, int> _uniqueRank;
    private readonly Func<bool> _canChangeLoadout;
    public event Action? LoadoutChanged;
    public IReadOnlyList<StatModifiers> PassiveModifiers => _passive.Take(PassiveSlotCapacity).Where(id => id is not null).Distinct(StringComparer.Ordinal).Select(id => Find(id!)!.PassiveModifiers).OfType<IReadOnlyDictionary<string, double>>().Select(values => new StatModifiers(HpPercent: values!.GetValueOrDefault("HPPercent"), AtkPercent: values.GetValueOrDefault("ATKPercent"), DefPercent: values.GetValueOrDefault("DEFPercent"), SpeedPercent: values.GetValueOrDefault("MoveSpeedPercent"), SpiritCapacityPercent: values.GetValueOrDefault("SpiritCapacityPercent"), SpiritCapacityFlat: values.GetValueOrDefault("SpiritCapacityFlat"), SpiritRegenPercent: values.GetValueOrDefault("SpiritRegenPercent"), SpiritRegenFlat: values.GetValueOrDefault("SpiritRegenFlat"))).ToArray();
    private readonly HashSet<string> _learned = new(StringComparer.Ordinal);
    private readonly List<string?> _active = Enumerable.Repeat<string?>(null, ActiveSlotCount).ToList();
    private readonly List<string?> _passive = Enumerable.Repeat<string?>(null, PassiveSlotCount).ToList();
    private readonly Dictionary<string, int> _promotedRanks = new(StringComparer.Ordinal);

    public V25SkillGrantSystem(CanonicalContentRegistry canonical, PlayerSystem player, V25InventorySystem inventory,
        Func<string, bool> possessionGrant, Func<string, int> possessionRank,
        Func<string, bool>? uniqueGrant = null, Func<string, int>? uniqueRank = null, Func<bool>? canChangeLoadout = null)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical)); _player = player ?? throw new ArgumentNullException(nameof(player)); _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _possessionGrant = possessionGrant ?? throw new ArgumentNullException(nameof(possessionGrant)); _possessionRank = possessionRank ?? throw new ArgumentNullException(nameof(possessionRank));
        _uniqueGrant = uniqueGrant ?? (_ => false); _uniqueRank = uniqueRank ?? (_ => 1);
        _canChangeLoadout = canChangeLoadout ?? (() => true);
        InitializeNewGame();
    }

    public IReadOnlySet<string> LearnedSkillIds => _learned;
    /// <summary>Slot arrays retain empty positions; a compact list would silently reorder a loadout.</summary>
    public IReadOnlyList<string?> ActiveSkillIds => _active.ToArray();
    public IReadOnlyList<string?> PassiveSkillIds => _passive.ToArray();
    public int ActiveSlotCapacity => Math.Min(ActiveSlotCount, 2 + (_player.State.Rank - 1) / 2);
    public int PassiveSlotCapacity => Math.Min(PassiveSlotCount, 1 + (_player.State.Rank - 1) / 3);

    public string? LearnedButtonSkill(int slot) => slot >= 0 && slot < ActiveSlotCapacity ? _active[slot] : null;
    public string? MainHandButtonSkill => GrantedEquipmentSkill(V25EquipmentSlot.MainHand);
    public string? OffHandButtonSkill => GrantedEquipmentSkill(V25EquipmentSlot.OffHand);
    public string? PossessionButtonSkill => _canonical.SkillsForProfile(_canonical.ActiveProfileId).Where(skill => _possessionGrant(skill.Id)).OrderBy(skill => skill.Id, StringComparer.Ordinal).Select(skill => skill.Id).FirstOrDefault();
    public string? UniqueButtonSkill => _canonical.UniqueSkillsForProfile(_canonical.ActiveProfileId).Where(skill => _uniqueGrant(skill.Id)).OrderBy(skill => skill.Id, StringComparer.Ordinal).Select(skill => skill.Id).FirstOrDefault();

    public void InitializeNewGame()
    {
        _learned.Clear(); _promotedRanks.Clear(); for (var i = 0; i < _active.Count; i++) _active[i] = null; for (var i = 0; i < _passive.Count; i++) _passive[i] = null;
        foreach (var id in _canonical.Content.NewGame.LearnedSkillIds)
        {
            var skill = Find(id) ?? throw new InvalidDataException($"NewGame learned skill '{id}' is missing from canonical content.");
            if (skill.DefaultSourceKind != "PermanentLearned" || !IsInProfile(skill)) throw new InvalidDataException($"NewGame learned skill '{id}' is not a beta learned skill.");
            _learned.Add(id);
        }
        AssignDefault("player_power_slash", _active, 0); AssignDefault("player_guard", _active, 1); AssignDefault("focus_breath", _passive, 0);
    }

    public bool Learn(string skillId)
    {
        var skill = Find(skillId); if (skill is null || skill.DefaultSourceKind != "PermanentLearned" || !IsInProfile(skill) || skill.UnlockPlayerRank is { } required && _player.State.Rank < required) return false;
        return _learned.Add(skillId);
    }

    public bool AssignActive(int slot, string skillId)
    {
        if (!_canChangeLoadout() || slot < 0 || slot >= ActiveSlotCapacity || _active.Where((_, index) => index != slot).Contains(skillId) || !CanUseLearned(skillId) || !CompatibleWeapon(Find(skillId)!) || Find(skillId)?.FunctionalCategory is "Passive") return false;
        _active[slot] = skillId; LoadoutChanged?.Invoke(); return true;
    }

    public bool AssignPassive(int slot, string skillId)
    {
        if (!_canChangeLoadout() || slot < 0 || slot >= PassiveSlotCapacity || _passive.Where((_, index) => index != slot).Contains(skillId) || !CanUseLearned(skillId) || Find(skillId)?.FunctionalCategory is not "Passive") return false;
        _passive[slot] = skillId; LoadoutChanged?.Invoke(); return true;
    }

    public bool Promote(string skillId, int rank)
    {
        var skill = Find(skillId);
        if (skill is null || !_learned.Contains(skillId) || rank is < 1 or > 6 || rank > skill.MaxMasteryRank || rank < _promotedRanks.GetValueOrDefault(skillId, 1)) return false;
        _promotedRanks[skillId] = rank; return true;
    }

    public bool IsExposed(string skillId)
    {
        var skill = Find(skillId); if (skill is null) return false;
        if (skill.Id == "player_basic_attack" || skill.DefaultSourceKind == "Core") return true;
        if (_possessionGrant(skillId)) return true;
        if (_uniqueGrant(skillId)) return true;
        if (_inventory.EquipmentSkillRanks.ContainsKey(skillId)) return CompatibleWeapon(skill);
        if (skill.DefaultSourceKind != "PermanentLearned" || !_learned.Contains(skillId)) return false;
        return _active.Take(ActiveSlotCapacity).Contains(skillId, StringComparer.Ordinal);
    }

    public int EffectivePlayerRank(string skillId)
    {
        var skill = Find(skillId);
        if (skill is null || skill.Id == "player_basic_attack") return 1;
        var rank = skill.DefaultSourceKind == "PermanentLearned" && _learned.Contains(skillId)
            ? _promotedRanks.GetValueOrDefault(skillId, Math.Max(1, skill.BaseRank))
            : Math.Max(1, skill.BaseRank);
        if (_inventory.EquipmentSkillRanks.TryGetValue(skillId, out var equipmentRank)) rank = Math.Max(rank, equipmentRank);
        rank = Math.Max(rank, _possessionRank(skillId));
        rank = Math.Max(rank, _uniqueRank(skillId));
        return Math.Clamp(rank, 1, 9);
    }

    public V25SkillGrantSnapshot Snapshot() => new(_learned.Order(StringComparer.Ordinal).ToArray(), ActiveSkillIds, PassiveSkillIds, _promotedRanks.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    public void Restore(V25SkillGrantSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var learned = snapshot.LearnedSkillIds?.ToHashSet(StringComparer.Ordinal) ?? throw new InvalidDataException("Canonical learned skill collection is missing.");
        if (learned.Any(id => !CanLearnedDefinition(id))) throw new InvalidDataException("Canonical learned skill is unknown, unavailable, or not permanent.");
        var active = snapshot.ActiveSkillIds?.ToArray() ?? throw new InvalidDataException("Canonical active skill collection is missing.");
        var passive = snapshot.PassiveSkillIds?.ToArray() ?? throw new InvalidDataException("Canonical passive skill collection is missing.");
        if (active.Length > ActiveSlotCount || passive.Length > PassiveSlotCount || active.Where(id => id is not null).Distinct(StringComparer.Ordinal).Count() != active.Count(id => id is not null) || passive.Where(id => id is not null).Distinct(StringComparer.Ordinal).Count() != passive.Count(id => id is not null) || active.Any(id => id is not null && (!learned.Contains(id) || !CanLearnedDefinition(id))) || passive.Any(id => id is not null && (!learned.Contains(id) || !CanLearnedDefinition(id))))
            throw new InvalidDataException("Canonical skill loadout is invalid or duplicates a slot.");
        if (active.Where((id, index) => id is not null && (index >= ActiveSlotCapacity || Find(id!)?.FunctionalCategory == "Passive")).Any()
            || passive.Where((id, index) => id is not null && (index >= PassiveSlotCapacity || Find(id!)?.FunctionalCategory != "Passive")).Any())
            throw new InvalidDataException("Loadout slot category or rank capacity is invalid.");
        var promoted = snapshot.PromotedRanks?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal) ?? throw new InvalidDataException("Canonical promoted skill ranks are missing.");
        foreach (var row in promoted)
        {
            var skill = Find(row.Key); if (skill is null || !learned.Contains(row.Key) || row.Value is < 1 or > 6 || row.Value > skill.MaxMasteryRank) throw new InvalidDataException("Canonical promoted skill rank is invalid.");
        }
        _learned.Clear(); foreach (var id in learned) _learned.Add(id); _promotedRanks.Clear(); foreach (var row in promoted) _promotedRanks.Add(row.Key, row.Value);
        for (var i = 0; i < _active.Count; i++) _active[i] = i < active.Length ? active[i] : null;
        for (var i = 0; i < _passive.Count; i++) _passive[i] = i < passive.Length ? passive[i] : null;
        LoadoutChanged?.Invoke();
    }

    private bool CanUseLearned(string skillId) => Find(skillId) is { } skill && skill.DefaultSourceKind == "PermanentLearned" && _learned.Contains(skillId) && IsInProfile(skill);
    private bool CanLearnedDefinition(string skillId) => Find(skillId) is { } skill && skill.DefaultSourceKind == "PermanentLearned" && IsInProfile(skill);
    private bool CompatibleWeapon(CanonicalSkillDefinition skill) => skill.RequiredWeaponFamilies is null || skill.RequiredWeaponFamilies.Count == 0 || skill.RequiredWeaponFamilies.Any(family => string.Equals(family, _inventory.MainHandFamily, StringComparison.Ordinal) || string.Equals(family, _inventory.OffHandFamily, StringComparison.Ordinal));
    private string? GrantedEquipmentSkill(V25EquipmentSlot slot) => _inventory.Equipped.FirstOrDefault(item => item.Slot == slot) is { } equipped
        ? _canonical.Content.Equipment.First(item => item.Id == equipped.DefinitionId).GrantedSkillId
        : null;
    private CanonicalSkillDefinition? Find(string skillId) => _canonical.Content.Skills.FirstOrDefault(item => item.Id == skillId);
    private bool IsInProfile(CanonicalSkillDefinition skill) => _canonical.SkillsForProfile(_canonical.ActiveProfileId).Any(item => item.Id == skill.Id);
    private void AssignDefault(string skillId, List<string?> slots, int index)
    {
        if (index >= 0 && index < slots.Count && CanLearnedDefinition(skillId)) slots[index] = skillId;
    }
}
