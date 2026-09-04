using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;

namespace SoloVsMortal.Simulation.Systems;

public sealed class SoulSystem : IDisposable
{
    private readonly Dictionary<string, WorldSoulState> _worldSouls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedSoulState> _owned = new(StringComparer.Ordinal);
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly SeededRng _rng; private readonly GameDefinitions _definitions; private readonly IDisposable _defeatSubscription;
    public SoulSystem(EventBus events, UidGenerator uids, SeededRng rng, GameDefinitions definitions) { _events = events; _uids = uids; _rng = rng; _definitions = definitions; _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnMonsterDefeated); }

    public IReadOnlyList<WorldSoulState> WorldSouls() => _worldSouls.Values.ToArray();
    public IReadOnlyList<OwnedSoulState> OwnedSouls() => _owned.Values.ToArray();
    public WorldSoulState? WorldSoul(string id) => _worldSouls.GetValueOrDefault(id);
    public OwnedSoulState? OwnedSoul(string id) => _owned.GetValueOrDefault(id);

    public OwnedSoulState? Acquire(string soulId)
    {
        if (!_worldSouls.Remove(soulId, out var soul)) return null;
        var owned = new OwnedSoulState(soul.Id, soul.SoulNatureId, soul.Origin);
        _owned.Add(owned.Id, owned); _events.Publish(new SoulAcquiredEvent(owned.Id)); return owned;
    }

    public IReadOnlyList<OwnedSoulState> AcquireNear(Vec2 position, double radius)
    {
        var ids = _worldSouls.Values.Where(soul => soul.Position.DistanceTo(position) <= radius).Select(soul => soul.Id).ToArray();
        return ids.Select(Acquire).Where(soul => soul is not null).Cast<OwnedSoulState>().ToArray();
    }

    public bool RemoveOwned(string soulId) { if (!_owned.Remove(soulId)) return false; _events.Publish(new SoulLostEvent(soulId)); return true; }

    public void RestoreOwned(IEnumerable<OwnedSoulState> souls)
    {
        _worldSouls.Clear(); _owned.Clear();
        foreach (var soul in souls)
        {
            if (string.IsNullOrWhiteSpace(soul.Id) || _owned.ContainsKey(soul.Id) || !_definitions.SoulNatures.Natures.ContainsKey(soul.SoulNatureId)) continue;
            try { _ = _definitions.Monster(soul.Origin.MonsterDefinitionId); } catch (KeyNotFoundException) { continue; }
            var level = System.Math.Clamp(soul.Level, 1, _definitions.Soul.MaximumLevel);
            var xp = level >= _definitions.Soul.MaximumLevel ? 0 : System.Math.Max(0, soul.Xp);
            _owned[soul.Id] = new OwnedSoulState(soul.Id, soul.SoulNatureId, soul.Origin, level, xp); _uids.Observe(soul.Id);
        }
    }

    public OwnedSoulState? AddXp(string soulId, int amount)
    {
        if (!_owned.TryGetValue(soulId, out var soul) || amount <= 0) return null;
        var result = ProgressionRules.AddXp(new XpState(soul.Level, soul.Xp), amount, _definitions.Soul.MaximumLevel);
        soul.Level = result.Level; soul.Xp = result.Xp;
        _events.Publish(new SoulXpGainedEvent(soulId, amount, soul.Level, soul.Xp, soul.Level < _definitions.Soul.MaximumLevel ? ProgressionRules.XpRequired(soul.Level) : 0));
        foreach (var level in result.LevelsGained) _events.Publish(new SoulLevelUpEvent(soulId, level));
        return soul;
    }

    public int? Cost(string soulId)
    {
        if (!_owned.TryGetValue(soulId, out var soul)) return null;
        var nature = _definitions.SoulNatures.Natures[soul.SoulNatureId];
        return _definitions.SoulNatures.CostProfiles[nature.SoulCostProfileId].Cost;
    }

    private void OnMonsterDefeated(MonsterDefeatedEvent defeated)
    {
        var monster = _definitions.Monster(defeated.DefinitionId);
        var scale = monster.SoulDrop.RankChanceScale.GetValueOrDefault(defeated.RankKey, 1);
        var chance = monster.SoulDrop.Chance * scale;
        if (chance <= 0) return;
        for (var index = 0; index < System.Math.Max(1, monster.SoulDrop.MaximumCount); index++)
        {
            if (chance < 1 && !_rng.Chance(chance)) continue;
            var soul = new WorldSoulState(_uids.Create("soul"), monster.SoulNatureId,
                new SoulOrigin(defeated.Uid, defeated.DefinitionId, defeated.SpeciesId, monster.DisplayName, defeated.Rank, defeated.RankKey, defeated.RankDisplayName), defeated.Position);
            _worldSouls.Add(soul.Id, soul); _events.Publish(new SoulGeneratedEvent(defeated.Uid, soul.Id, defeated.RankKey, defeated.Position));
        }
    }

    public void Dispose() => _defeatSubscription.Dispose();
}
