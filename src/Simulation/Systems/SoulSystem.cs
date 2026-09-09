using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.State.V25;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Simulation.Systems;

public sealed class SoulSystem : IDisposable
{
    private readonly Dictionary<string, WorldSoulState> _worldSouls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedSoulState> _owned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, V25WorldSoulPickup> _canonicalPickups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _canonicalPity = new(StringComparer.Ordinal);
    private readonly HashSet<string> _canonicalConsumedPickupIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _canonicalTutorialReceipts = new(StringComparer.Ordinal);
    private readonly List<string> _canonicalAcquisitionEventsPendingCommit = new();
    private readonly EventBus _events; private readonly UidGenerator _uids; private readonly SeededRng _rng; private readonly GameDefinitions _definitions; private readonly CanonicalContentRegistry? _canonical; private readonly Pcg32? _canonicalDropRng; private readonly IDisposable _defeatSubscription;
    private V25DensityEngine? _density;
    private int _canonicalBannerRank = 1;
    public SoulSystem(EventBus events, UidGenerator uids, SeededRng rng, GameDefinitions definitions, CanonicalContentRegistry? canonical = null, Pcg32? canonicalDropRng = null) { _events = events; _uids = uids; _rng = rng; _definitions = definitions; _canonical = canonical; _canonicalDropRng = canonicalDropRng; if (canonical is not null) _density = new V25DensityEngine(canonical); _defeatSubscription = events.Subscribe<MonsterDefeatedEvent>(OnMonsterDefeated); }

    private Func<string>? _currentRegion;
    private readonly Dictionary<string, string> _pickupRegions = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> PickupRegions => _pickupRegions;
    public void ConfigureRegion(Func<string> region) => _currentRegion = region;
    private bool PickupInCurrentRegion(string id) => _currentRegion is null || _pickupRegions.GetValueOrDefault(id, _currentRegion()) == _currentRegion();
    public void RestorePickupRegions(IReadOnlyDictionary<string, string>? regions)
    {
        _pickupRegions.Clear();
        foreach (var pickup in _canonicalPickups.Values)
        {
            var region = regions?.GetValueOrDefault(pickup.PickupId) ?? _currentRegion!();
            if (!_canonical!.RegionsForProfile(_canonical.ActiveProfileId).Any(r => r.Id == region)) throw new InvalidDataException("Unknown pickup region.");
            _pickupRegions.Add(pickup.PickupId, region);
        }
        if (regions is not null && regions.Keys.Any(id => !_canonicalPickups.ContainsKey(id))) throw new InvalidDataException("Pickup region references absent pickup.");
    }
    public IReadOnlyList<WorldSoulState> WorldSouls() => _worldSouls.Values.Where(soul => PickupInCurrentRegion(soul.Id)).ToArray();
    public IReadOnlyList<OwnedSoulState> OwnedSouls() => _owned.Values.ToArray();
    public WorldSoulState? WorldSoul(string id) => _worldSouls.GetValueOrDefault(id);
    public OwnedSoulState? OwnedSoul(string id) => _owned.GetValueOrDefault(id);
    public OwnedSoulState? CanonicalOwnedSpecies(string speciesId) => _owned.Values.FirstOrDefault(item => item.Origin.SpeciesId.Equals(speciesId, StringComparison.OrdinalIgnoreCase));
    public bool CanonicalMode => _canonical is not null;
    public V25DensityEngine? CanonicalDensity => _density;
    public int CanonicalBannerRank => _canonicalBannerRank;
    public IReadOnlyList<V25WorldSoulPickup> CanonicalPickups => _canonicalPickups.Values.OrderBy(item => item.PickupId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25SoulPityState> CanonicalPity => _canonicalPity.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new V25SoulPityState(item.Key, item.Value)).ToArray();
    public IReadOnlyList<string> CanonicalConsumedPickupIds => _canonicalConsumedPickupIds.Order(StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> CanonicalTutorialReceipts => _canonicalTutorialReceipts.Order(StringComparer.Ordinal).ToArray();
    public bool HasCanonicalAcquisitionsPendingCommit => _canonicalAcquisitionEventsPendingCommit.Count > 0;
    public V25DensityEngineSnapshot CanonicalDensitySnapshot() => _density?.Snapshot()
        ?? throw new InvalidOperationException("Canonical Density is not enabled.");

    /// <summary>Applies an authored quest/unique Density reward to an already owned species.
    /// The Density engine validates and commits the immutable transaction before the legacy Soul
    /// mirror is updated; a Locked species can therefore never receive an invented reward.</summary>
    public V25DensityApplyResult ApplyCanonicalDensityReward(string transactionId, string awardId, string speciesId, decimal points, string sourceId)
    {
        if (_canonical is null || _density is null) throw new InvalidOperationException("Canonical Density is not enabled.");
        if (points <= 0 || string.IsNullOrWhiteSpace(sourceId)) throw new InvalidDataException("Canonical Density reward is invalid.");
        var ownership = _density.GetOwnership(speciesId);
        if (!ownership.IsOwned) throw new InvalidDataException($"Canonical Density reward target '{speciesId}' is not owned.");
        var request = new V25DensityTransactionRequest(transactionId, awardId, speciesId, sourceId, _canonical.Content.ContentVersion,
            V25DensityMath.ToMicroPoints(points), Array.Empty<V25DensityProof>());
        var result = _density.Apply(request);
        var owned = CanonicalOwnedSpecies(speciesId);
        if (owned is not null)
        {
            owned.Level = result.Ownership.SoulLevel(_density.DensityDefinition);
            owned.Xp = 0;
        }
        return result;
    }

    public void SetCanonicalBannerRank(int rank)
    {
        if (_canonical is null) return;
        var max = _canonical.Profile(_canonical.ActiveProfileId).MaxBannerRank;
        if (rank is < 1 || rank > max) throw new InvalidDataException($"Canonical banner rank {rank} is outside profile cap {max}.");
        _canonicalBannerRank = rank;
    }

    /// <summary>Creates the one tutorial pickup from an explicit quest receipt; no quest completion is fabricated.</summary>
    public WorldSoulState? SpawnCanonicalTutorialSoul(string questReceiptId, string speciesId, int level, Vec2 position)
    {
        if (_canonical is null) throw new InvalidOperationException("Canonical content is not enabled.");
        if (string.IsNullOrWhiteSpace(questReceiptId) || !questReceiptId.StartsWith("quest.", StringComparison.Ordinal))
            throw new InvalidDataException("Tutorial Soul spawn requires a stable quest receipt ID.");
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || level is < 1 or > 90)
            throw new InvalidDataException("Tutorial Soul spawn position or level is invalid.");
        var species = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == speciesId)
            ?? throw new InvalidDataException($"Tutorial Soul species '{speciesId}' is unavailable in beta_01.");
        var pickupId = $"tutorial.{questReceiptId[6..]}";
        if (_canonicalTutorialReceipts.Contains(questReceiptId)) return _worldSouls.GetValueOrDefault(pickupId);
        var monster = _definitions.Monsters.FirstOrDefault(item => item.SpeciesId.Equals(species.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"No monster definition can provide tutorial species '{species.Id}'.");
        var rank = V25DensityMath.SourceRankFromLevel(level);
        var pickup = new V25WorldSoulPickup(pickupId, $"tutorial.source.{questReceiptId[6..]}", monster.Id, species.Id, monster.DisplayName,
            rank, CombatPowerRules.RankKey(rank), CombatPowerRules.RankDisplayName(rank), level, rank, position, monster.SoulNatureId, true);
        _canonicalTutorialReceipts.Add(questReceiptId);
        _canonicalPickups.Add(pickup.PickupId, pickup);
        if (_currentRegion is not null) _pickupRegions[pickup.PickupId] = _currentRegion();
        var world = new WorldSoulState(pickup.PickupId, pickup.SoulNatureId,
            new SoulOrigin(pickup.MonsterUid, pickup.MonsterDefinitionId, pickup.SpeciesId, pickup.DisplayName, pickup.Rank, pickup.RankKey, pickup.RankDisplayName), pickup.Position);
        _worldSouls.Add(pickup.PickupId, world);
        _events.Publish(new SoulGeneratedEvent(pickup.MonsterUid, pickup.PickupId, pickup.RankKey, pickup.Position));
        return world;
    }

    public void RestoreCanonicalState(
        V25DensityEngineSnapshot densitySnapshot,
        IEnumerable<V25WorldSoulPickup> pickups,
        IEnumerable<V25SoulPityState> pity,
        IEnumerable<string> consumedPickupIds,
        IEnumerable<string> tutorialReceipts,
        int bannerRank = 1)
    {
        if (_canonical is null) throw new InvalidOperationException("Canonical content is not enabled.");
        ArgumentNullException.ThrowIfNull(densitySnapshot); ArgumentNullException.ThrowIfNull(pickups); ArgumentNullException.ThrowIfNull(pity);
        ArgumentNullException.ThrowIfNull(consumedPickupIds); ArgumentNullException.ThrowIfNull(tutorialReceipts);
        var restoredDensity = V25DensityEngine.RestoreSnapshot(_canonical, densitySnapshot, _canonical.ActiveProfileId);
        var restoredPickups = pickups.ToArray(); var restoredPity = pity.ToArray(); var restoredConsumed = consumedPickupIds.ToArray(); var restoredTutorial = tutorialReceipts.ToArray();
        if (restoredPickups.Select(item => item.PickupId).Distinct(StringComparer.Ordinal).Count() != restoredPickups.Length || restoredConsumed.Distinct(StringComparer.Ordinal).Count() != restoredConsumed.Length || restoredTutorial.Distinct(StringComparer.Ordinal).Count() != restoredTutorial.Length)
            throw new InvalidDataException("Canonical Soul restore contains duplicate pickup or receipt identities.");
        if (restoredPickups.Any(item => string.IsNullOrWhiteSpace(item.PickupId) || string.IsNullOrWhiteSpace(item.SpeciesId) || item.SourceLevel is < 1 or > 90 || item.SourceRank != V25DensityMath.SourceRankFromLevel(item.SourceLevel) || !double.IsFinite(item.Position.X) || !double.IsFinite(item.Position.Y) || restoredConsumed.Contains(item.PickupId, StringComparer.Ordinal)))
            throw new InvalidDataException("Canonical world Soul restore contains invalid or already-consumed pickup state.");
        foreach (var pickup in restoredPickups)
        {
            _ = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(species => species.Id == pickup.SpeciesId)
                ?? throw new InvalidDataException($"Canonical world Soul restore references unknown species '{pickup.SpeciesId}'.");
            _ = _definitions.Monsters.FirstOrDefault(monster => monster.Id == pickup.MonsterDefinitionId)
                ?? throw new InvalidDataException($"Canonical world Soul restore references unknown monster '{pickup.MonsterDefinitionId}'.");
        }
        if (restoredPity.Select(item => item.SpeciesId).Distinct(StringComparer.Ordinal).Count() != restoredPity.Length || restoredPity.Any(item => item.Counter is < 0 or > 10 || !_canonical.SpeciesForProfile(_canonical.ActiveProfileId).Any(species => species.Id == item.SpeciesId)))
            throw new InvalidDataException("Canonical Soul pity restore is invalid.");
        if (restoredConsumed.Any(string.IsNullOrWhiteSpace) || restoredTutorial.Any(receipt => string.IsNullOrWhiteSpace(receipt) || !receipt.StartsWith("quest.", StringComparison.Ordinal)))
            throw new InvalidDataException("Canonical consumed pickup or tutorial receipt identity is invalid.");
        if (bannerRank < 1 || bannerRank > _canonical.Profile(_canonical.ActiveProfileId).MaxBannerRank) throw new InvalidDataException("Canonical banner rank restore is invalid.");

        // No live maps are touched until density, pickup, pity, and identity validation above succeeds.
        _density = restoredDensity; _canonicalBannerRank = bannerRank;
        _worldSouls.Clear(); _canonicalPickups.Clear(); _pickupRegions.Clear(); _canonicalPity.Clear(); _canonicalConsumedPickupIds.Clear(); _canonicalTutorialReceipts.Clear(); _owned.Clear();
        _canonicalAcquisitionEventsPendingCommit.Clear();
        foreach (var pickup in restoredPickups)
        {
            _canonicalPickups.Add(pickup.PickupId, pickup);
        if (_currentRegion is not null) _pickupRegions[pickup.PickupId] = _currentRegion();
            _worldSouls.Add(pickup.PickupId, new WorldSoulState(pickup.PickupId, pickup.SoulNatureId,
                new SoulOrigin(pickup.MonsterUid, pickup.MonsterDefinitionId, pickup.SpeciesId, pickup.DisplayName, pickup.Rank, pickup.RankKey, pickup.RankDisplayName), pickup.Position));
        }
        foreach (var item in restoredPity) _canonicalPity.Add(item.SpeciesId, item.Counter);
        foreach (var pickupId in restoredConsumed) _canonicalConsumedPickupIds.Add(pickupId);
        foreach (var receipt in restoredTutorial) _canonicalTutorialReceipts.Add(receipt);
        foreach (var state in restoredDensity.Ownership.Values.Where(state => state.IsOwned))
        {
            var monster = _definitions.Monsters.FirstOrDefault(item => item.SpeciesId.Equals(state.SpeciesId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"No legacy mirror definition exists for owned species '{state.SpeciesId}'.");
            var level = state.SoulLevel(_density.DensityDefinition); var rank = state.SoulRank(_density.DensityDefinition);
            var id = $"owned.{state.SpeciesId}";
            _owned[id] = new OwnedSoulState(id, monster.SoulNatureId,
                new SoulOrigin($"owned.source.{state.SpeciesId}", monster.Id, state.SpeciesId, monster.DisplayName, rank, CombatPowerRules.RankKey(rank), CombatPowerRules.RankDisplayName(rank)), level, 0);
        }
    }

    public OwnedSoulState? Acquire(string soulId)
    {
        if (_canonical is not null) return AcquireCanonical(soulId);
        if (!_worldSouls.Remove(soulId, out var soul)) return null;
        var owned = new OwnedSoulState(soul.Id, soul.SoulNatureId, soul.Origin);
        _owned.Add(owned.Id, owned); _events.Publish(new SoulAcquiredEvent(owned.Id)); return owned;
    }

    public IReadOnlyList<OwnedSoulState> AcquireNear(Vec2 position, double radius)
    {
        if (_canonical is not null)
        {
            var allowedRadius = Math.Min(48, Math.Max(0, radius));
            return _canonicalPickups.Values
                .Where(pickup => PickupInCurrentRegion(pickup.PickupId) && pickup.RewardEligible && pickup.Position.DistanceTo(position) <= allowedRadius)
                .OrderBy(pickup => pickup.Position.DistanceTo(position)).ThenBy(pickup => pickup.PickupId, StringComparer.Ordinal)
                .Select(pickup => AcquireCanonical(pickup.PickupId))
                .Where(soul => soul is not null).Cast<OwnedSoulState>().ToArray();
        }
        var ids = _worldSouls.Values.Where(soul => soul.Position.DistanceTo(position) <= radius).Select(soul => soul.Id).ToArray();
        return ids.Select(Acquire).Where(soul => soul is not null).Cast<OwnedSoulState>().ToArray();
    }

    /// <summary>Canonical auto-collect is deliberately a query plus the same atomic capture path as E.</summary>
    public IReadOnlyList<OwnedSoulState> AutoCollect(Vec2 position, bool playerAlive)
    {
        if (_canonical is null || !playerAlive) return Array.Empty<OwnedSoulState>();
        return AcquireNear(position, 48);
    }

    private OwnedSoulState? AcquireCanonical(string pickupId)
    {
        if (_canonical is null || _density is null || !_canonicalPickups.TryGetValue(pickupId, out var pickup) || _canonicalConsumedPickupIds.Contains(pickupId) || !pickup.RewardEligible || !PickupInCurrentRegion(pickupId))
            return null;
        if (_owned.ContainsKey(pickupId)) return _owned[pickupId];
        var ownership = _density.GetOwnership(pickup.SpeciesId);
        var preRank = ownership.IsOwned ? ownership.SoulRank(_density.DensityDefinition) : 1;
        var request = V25DensityTransactionRequest.FromSource(
            $"tx.capture.{pickup.PickupId}",
            $"award.density.{pickup.PickupId}",
            pickup.SpeciesId,
            $"source.kill.{pickup.MonsterUid}",
            _canonical.Content.ContentVersion,
            pickup.SourceLevel,
            preRank,
            _density.DensityDefinition);

        // The engine stages Locked->Owned D0 and the award together. Nothing is removed from the
        // world or marked consumed until that validation/commit has succeeded.
        var applied = _density.Apply(request, allowFirstOwnership: true);
        var origin = new SoulOrigin(pickup.MonsterUid, pickup.MonsterDefinitionId, pickup.SpeciesId, pickup.DisplayName, pickup.Rank, pickup.RankKey, pickup.RankDisplayName);
        // Canonical ownership is keyed by SpeciesId: a later pickup adds Density to the
        // existing species Soul and may never create a second summon/possession identity.
        var owned = CanonicalOwnedSpecies(pickup.SpeciesId);
        if (owned is null)
        {
            owned = new OwnedSoulState($"owned.{pickup.SpeciesId}", pickup.SoulNatureId, origin,
                applied.Ownership.SoulLevel(_density.DensityDefinition), 0);
            _owned.Add(owned.Id, owned);
        }
        else
        {
            owned.Level = applied.Ownership.SoulLevel(_density.DensityDefinition);
            owned.Xp = 0;
        }
        _worldSouls.Remove(pickup.PickupId);
        _canonicalPickups.Remove(pickup.PickupId); _pickupRegions.Remove(pickup.PickupId);
        _canonicalConsumedPickupIds.Add(pickup.PickupId);
        // Capture mutates the in-memory transaction so the save payload contains ownership,
        // Density, pickup consumption and receipts together. The public reward event is held
        // until the WAL/current-file commit succeeds; failure pauses the caller and retries the
        // exact payload without exposing an uncommitted reward to Sync or presentation.
        _canonicalAcquisitionEventsPendingCommit.Add(owned.Id);
        return owned;
    }

    public void CommitCanonicalAcquisitionEvents()
    {
        if (_canonical is null || _canonicalAcquisitionEventsPendingCommit.Count == 0) return;
        var committed = _canonicalAcquisitionEventsPendingCommit.ToArray();
        _canonicalAcquisitionEventsPendingCommit.Clear();
        foreach (var soulId in committed) _events.Publish(new SoulAcquiredEvent(soulId));
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
        if (_canonical is not null) return null;
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
        if (_canonical is not null)
        {
            SpawnCanonicalDrop(defeated);
            return;
        }
        if (!defeated.RewardEligible) return;
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

    private void SpawnCanonicalDrop(MonsterDefeatedEvent defeated)
    {
        if (_canonical is null || _density is null || _canonicalDropRng is null || !defeated.RewardEligible || defeated.EncounterType is V25EncounterType.Arena or V25EncounterType.Debug)
            return;
        var species = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(item => item.Id == defeated.SpeciesId.ToLowerInvariant());
        if (species is null || species.PowerTier > _canonicalBannerRank) return; // ineligible kills do not roll or advance pity.

        var ownership = _density.GetOwnership(species.Id);
        var guaranteedAt = ownership.IsOwned ? 10 : 5;
        var currentPity = _canonicalPity.GetValueOrDefault(species.Id);
        var nextPity = checked(currentPity + 1);
        var guaranteed = nextPity >= guaranteedAt;
        var chance = CanonicalSoulDropChance(defeated.Rank, species.PowerTier, defeated.EncounterType);
        var dropped = guaranteed || _canonicalDropRng.Chance(chance);
        _canonicalPity[species.Id] = dropped ? 0 : nextPity;
        if (!dropped) return;

        var monster = _definitions.Monsters.FirstOrDefault(item => item.Id == defeated.DefinitionId)
            ?? throw new InvalidDataException($"Canonical defeated monster definition '{defeated.DefinitionId}' is missing.");
        var pickup = new V25WorldSoulPickup(
            _uids.Create("worldSoul"), defeated.Uid, defeated.DefinitionId, species.Id, monster.DisplayName,
            defeated.Rank, defeated.RankKey, defeated.RankDisplayName, defeated.Level,
            V25DensityMath.SourceRankFromLevel(defeated.Level), defeated.Position, monster.SoulNatureId, true);
        _canonicalPickups.Add(pickup.PickupId, pickup);
        if (_currentRegion is not null) _pickupRegions[pickup.PickupId] = _currentRegion();
        _worldSouls.Add(pickup.PickupId, new WorldSoulState(pickup.PickupId, pickup.SoulNatureId,
            new SoulOrigin(pickup.MonsterUid, pickup.MonsterDefinitionId, pickup.SpeciesId, pickup.DisplayName, pickup.Rank, pickup.RankKey, pickup.RankDisplayName), pickup.Position));
        _events.Publish(new SoulGeneratedEvent(defeated.Uid, pickup.PickupId, defeated.RankKey, defeated.Position));
    }

    private static double CanonicalSoulDropChance(int rank, int powerTier, V25EncounterType encounterType)
    {
        var encounterMultiplier = encounterType switch
        {
            V25EncounterType.Elite => 1.25,
            V25EncounterType.Boss => 1.60,
            _ => 1.0,
        };
        var chance = 0.38 * Math.Pow(0.82, rank - 1) * Math.Pow(0.94, powerTier - 1) * encounterMultiplier;
        return Math.Clamp(chance, 0.03, 0.60);
    }

    public void Dispose() => _defeatSubscription.Dispose();
}
