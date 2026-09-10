using SoloVsMortal.Core.Loop;
using SoloVsMortal.Core.Events;
using SoloVsMortal.Core.Ids;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.Systems.V25;
using SoloVsMortal.Simulation.Events;

namespace SoloVsMortal.Simulation;

/// <summary>Headless owner and tick coordinator for a single gameplay session.</summary>
public sealed partial class GameSession : IDisposable
{
    private readonly SimulationClock _clock = new();
    private readonly UidGenerator _uids;
    private Vec2 _moveInput;
    private Vec2 _aimInput;
    private bool _attackPressed;
    private bool _dodgePressed;
    private int _respawnTicks;
    private readonly IDisposable _deathSubscription;
    private readonly List<IDisposable> _canonicalDurabilitySubscriptions = [];
    private bool _canonicalDurableCommitRequired;

    public GameSession(GameDefinitions definitions, uint seed = 1, CanonicalContentRegistry? canonical = null)
    {
        Definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        CanonicalContent = canonical;
        Events = new EventBus();
        var uids = _uids = new UidGenerator();
        var rng = new SeededRng(seed);
        RandomStreams = new Pcg32Streams(seed);
        WorldMap = new WorldMapSystem(definitions, canonical: canonical);
        var map = WorldMap.CurrentMap; var playerSpawn = map.Spawn(WorldMap.CurrentRegion.DefaultSpawnId).Position;
        Player = new PlayerSystem(Events, uids, definitions.Player, playerSpawn, new PlayerBounds(map.Width, map.Height), canonical: canonical);
        Monsters = new MonsterSystem(Events, uids, rng, definitions, new SpawnArea(0, 0, map.Width, map.Height), canonical);
        Souls = new SoulSystem(Events, uids, rng, definitions, canonical, RandomStreams.SoulDrop);
        if (canonical is not null) Souls.ConfigureRegion(() => CanonicalRegionId);
        SoulBanners = new SoulBannerSystem(Events, uids, definitions, Souls);
        SoulBanners.CreateStarter();
        Allies = new AllySystem(Events, uids, definitions, canonical);
        Combat = new CombatSystem(Events, Player, Monsters, uids, canonical, Allies, RandomStreams);
        Summons = new SummonSystem(Events, definitions, Souls, SoulBanners, Allies);
        PlayerModifiers = new PlayerModifierSystem(Player);
        Progression = new ProgressionSystem(Events, rng, Player, PlayerModifiers, canonical, Monsters, () => CanonicalRegionId, IsAtCanonicalShrine, () => SimulationTick);
        if (canonical is not null)
            SoulBanners.ConfigureCanonical(canonical, () => Player.State.Rank, () => CanonicalRegionId, IsAtCanonicalShrine, Progression.HasCanonicalFact);
        Essence = canonical is null ? new EssenceSystem(Events, definitions.SoulNatures, PlayerModifiers) : null;
        Bloodline = canonical is null ? new BloodlineSystem(Events, definitions.SoulNatures, PlayerModifiers) : null;
        Capabilities = new CapabilitySystem();
        if (canonical is not null) Capabilities.ConfigureCanonical(canonical.Content.Capabilities);
        Possession = new PossessionSystem(Events, definitions.SoulNatures, Souls, SoulBanners, Summons, PlayerModifiers, Capabilities, Player);
        if (canonical is not null) Summons.ConfigureCanonical(canonical, Player, () => Possession.ActiveSoulId is not null,
            () => IsWithinCanonicalShrine(96), IsCanonicalCombatActive);
        if (canonical is not null) Allies.ConfigureCanonical(Player, (start, goal, radius, distance) => Player.FindReachableNextStep(start, goal, radius, distance),
            (origin, radius, maxRadius) => Player.FindNearestFree(origin, radius, maxRadius), IsCanonicalCombatActive);
        World = new WorldInteractionSystem(Events, map, Capabilities);
        Devouring = canonical is null ? new DevourSystem(Events, definitions, Souls, SoulBanners, Summons, Essence!, Bloodline!, Progression.AddPlayerXp) : null;
        Sync = canonical is null ? null : new V25SyncSystem(Events, canonical, Souls, () => SimulationTick, IsAtCanonicalShrine, Capabilities.Has, speciesId => Possession.CanonicalSnapshot?.SpeciesId == speciesId);
        if (canonical is not null) Possession.ConfigureCanonical(canonical,
            speciesId => Sync?.TotalMicro(speciesId) ?? 0,
            speciesId => Sync?.Milestones(speciesId) ?? new HashSet<string>(StringComparer.Ordinal),
            () => Combat.IsActionLocked(Player.State.Uid), () => SimulationTick,
            sourceUid => Combat.CancelUnreleasedCanonicalCasts(sourceUid),
            capabilityId => Traversal?.NotifyCapabilityRevoked(capabilityId));
        if (canonical is not null) Combat.ConfigureCanonicalPlayerSkillGrant(Possession.IsSkillGranted);
        Spirit = canonical is null ? null : new V25SpiritSystem(canonical, Player, Souls, Summons, IsAtCanonicalShrine, IsCanonicalCombatActive);
        Traversal = canonical is null ? null : new V25TraversalSystem(canonical, Capabilities, Player, IsCanonicalCombatActive, sourceUid => Combat.CancelUnreleasedCanonicalCasts(sourceUid));
        InventoryV25 = canonical is null ? null : new V25InventorySystem(canonical, Player, PlayerModifiers, Events, IsCanonicalCombatActive, () => Traversal?.IsHazardActive == true, () => Possession.CanonicalTransitionLocked);
        if (InventoryV25 is not null)
        {
            InventoryV25.InitializeNewGame();
            Player.State.CurrentHp = Player.State.MaxHp;
            Player.State.CurrentSpirit = Player.State.MaxSpirit;
        }
        UniquePowersV25 = canonical is null || InventoryV25 is null ? null
            : new V25UniquePowerSystem(canonical, Events, Progression.HasCanonicalFact, IsAtCanonicalShrine, () => Player.State.Position, () => SimulationTick);
        LootV25 = canonical is null || InventoryV25 is null ? null
            : new V25LootSystem(canonical, Events, InventoryV25, RandomStreams.ItemDrop, RandomStreams.ItemFamily);
        SkillGrantsV25 = canonical is null || InventoryV25 is null ? null
            : new V25SkillGrantSystem(canonical, Player, InventoryV25, Possession.IsSkillGranted, Possession.EffectiveSignatureRank,
                UniquePowersV25 is null ? null : (Func<string, bool>)UniquePowersV25.IsSkillGranted,
                UniquePowersV25 is null ? null : (Func<string, int>)UniquePowersV25.PowerRank,
                () => !IsCanonicalCombatActive() && Traversal?.IsHazardActive != true && !Possession.CanonicalTransitionLocked);
        if (SkillGrantsV25 is not null)
        {
            SkillGrantsV25.LoadoutChanged += () => PlayerModifiers.SetSource(PlayerModifierSource.PassiveSkills, SkillGrantsV25.PassiveModifiers);
            PlayerModifiers.SetSource(PlayerModifierSource.PassiveSkills, SkillGrantsV25.PassiveModifiers);
            Combat.ConfigureCanonicalPlayerSkillGrant(skillId => Player.TerrainCombatAllowed?.Invoke() != false && SkillGrantsV25.IsExposed(skillId));
            Combat.ConfigureCanonicalPlayerSkillRank(SkillGrantsV25.EffectivePlayerRank);
        }
        MasteryV25 = canonical is null || SkillGrantsV25 is null
            ? null
            : new V25MasterySystem(canonical, Events, Player, Monsters,
                skillId => SkillGrantsV25.LearnedSkillIds.Contains(skillId) && SkillGrantsV25.IsExposed(skillId),
                SkillGrantsV25.EffectivePlayerRank,
                () => SimulationTick);
        QuestsV25 = canonical is null || InventoryV25 is null || SkillGrantsV25 is null
            ? null
            : new V25QuestSystem(canonical, Events, Souls, Progression, InventoryV25, SkillGrantsV25,
                () => CanonicalRegionId, IsAtCanonicalShrine, Progression.HasCanonicalFact, () => SimulationTick);
        WorldLifecycleV25 = canonical is null || QuestsV25 is null ? null
            : new V25WorldLifecycleSystem(canonical, Events, QuestsV25, () => CanonicalRegionId, ResetCanonicalEncounter);
        if (canonical is not null)
        {
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<MonsterDefeatedEvent>(defeated => { if (defeated.RewardEligible && defeated.EncounterType is not (V25EncounterType.Arena or V25EncounterType.Debug)) _canonicalDurableCommitRequired = true; }));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<V25FactCommittedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<V25BannerUpgradeCommittedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<V25UniquePowerUnlockedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<SoulSummonedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<SoulUnsummonedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<SoulDispersedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<SoulRecoveredEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<PossessionStartedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<PossessionEndedEvent>(_ => _canonicalDurableCommitRequired = true));
            _canonicalDurabilitySubscriptions.Add(Events.Subscribe<PlayerDefeatedEvent>(_ => _canonicalDurableCommitRequired = true));
        }
        Player.SetColliders(World.BlockingRects());
        if (Traversal is not null) World.ConfigureCanonicalTraversal(Traversal);
        if (canonical is not null)
        {
            var canonicalContent = CanonicalContent!;
            Player.TerrainEntryAllowed = CanEnterCanonicalTerrain;
            // Only Player traversal is capability-gated. AI actors use their own body radius and
            // never borrow a possessed Player's mobility; crumbling surfaces are excluded because
            // they have no non-player rescue contract.
            Player.NonPlayerTerrainBarriers = () => CanonicalTerrain.Where(a => a.Terrain is V25TerrainTag.Gap or V25TerrainTag.ShallowWater or V25TerrainTag.PhasePassable or V25TerrainTag.CrumblingFloor).Select(a => a.Bounds).ToArray();
            Player.TerrainCombatAllowed = () => !CanonicalTerrain.Any(a => a.Terrain == V25TerrainTag.ShallowWater && V25WorldLayout.Contains(a.Bounds, Player.State.Position));
            Allies.EnvironmentSpeedMultiplier = ally =>
            {
                var onFrost = CanonicalTerrain.Any(area => area.Terrain == V25TerrainTag.FrostFloor && V25WorldLayout.Contains(area.Bounds, ally.Position));
                var species = canonicalContent.SpeciesForProfile(canonicalContent.ActiveProfileId).First(item => item.Id == ally.SpeciesId);
                return onFrost && species.CapabilityId != "FrostStep" ? 0.75 : 1;
            };
            // NewGame starts full after equipment/passives. Restore later overwrites these constructor values.
            Player.State.CurrentHp = Player.State.MaxHp;
            Player.State.CurrentSpirit = Player.State.MaxSpirit;
        }
        _deathSubscription = Events.Subscribe<Simulation.Events.PlayerDefeatedEvent>(_ =>
        {
            if (CanonicalContent is null || _respawnTicks > 0) return;
            _respawnTicks = 120;
            Possession.EndCanonicalForDeath();
            Combat.ClearCanonicalRuntime();
            Summons.RecallAllLivingForDeath();
            Player.State.Statuses.Clear();
            Player.State.Shields.Clear();
            Player.State.DodgeRemainingTicks = 0;
            Player.State.DodgeDistanceRemaining = 0;
            Player.State.DodgeInvulnerabilityTicks = 0;
            _moveInput = Vec2.Zero; _attackPressed = false; _dodgePressed = false;
        });
        _worldSubscription = Events.Subscribe<Simulation.Events.WorldObjectDestroyedEvent>(_ => Player.SetColliders(World.BlockingRects()));
    }

    public GameSessionState State { get; } = new();
    public GameDefinitions Definitions { get; }
    public CanonicalContentRegistry? CanonicalContent { get; }
    public Pcg32Streams RandomStreams { get; }
    public WorldMapSystem WorldMap { get; }
    public EventBus Events { get; }
    public PlayerSystem Player { get; }
    public MonsterSystem Monsters { get; }
    public CombatSystem Combat { get; }
    public SoulSystem Souls { get; }
    public SoulBannerSystem SoulBanners { get; }
    public AllySystem Allies { get; }
    public SummonSystem Summons { get; }
    public PlayerModifierSystem PlayerModifiers { get; }
    public ProgressionSystem Progression { get; }
    /// <summary>Legacy-save compatibility only; canonical V2.5 has no Essence mechanic.</summary>
    public EssenceSystem? Essence { get; }
    /// <summary>Legacy-save compatibility only; canonical V2.5 has no Bloodline mechanic.</summary>
    public BloodlineSystem? Bloodline { get; }
    public CapabilitySystem Capabilities { get; }
    public PossessionSystem Possession { get; }
    public WorldInteractionSystem World { get; }
    /// <summary>Legacy-save compatibility only; canonical V2.5 has no Devour mechanic.</summary>
    public DevourSystem? Devouring { get; }
    public V25SyncSystem? Sync { get; }
    public V25SpiritSystem? Spirit { get; }
    public V25TraversalSystem? Traversal { get; }
    public V25InventorySystem? InventoryV25 { get; }
    public V25SkillGrantSystem? SkillGrantsV25 { get; }
    public V25UniquePowerSystem? UniquePowersV25 { get; }
    public V25LootSystem? LootV25 { get; }
    public V25MasterySystem? MasteryV25 { get; }
    public V25QuestSystem? QuestsV25 { get; }
    public V25WorldLifecycleSystem? WorldLifecycleV25 { get; }
    private readonly IDisposable _worldSubscription;

    public double ElapsedSeconds => _clock.Time;
    public long SimulationTick => _clock.TickCount;
    public long UidNext => _uids.NextValue;
    public bool CanonicalDurableCommitRequired => _canonicalDurableCommitRequired || Souls.HasCanonicalAcquisitionsPendingCommit;
    public void RequireCanonicalDurableCommit() { if (CanonicalContent is not null) _canonicalDurableCommitRequired = true; }
    public void CompleteCanonicalDurableCommit() => _canonicalDurableCommitRequired = false;
    public string CanonicalRegionId => WorldMap.CurrentRegionId;

    public RegionTravelResult PreviewCanonicalRegion(string regionId)
    {
        if (CanonicalContent is null) return new(false, Failure: RegionTravelFailure.RegionNotFound);
        var region = CanonicalContent.RegionsForProfile(CanonicalContent.ActiveProfileId).FirstOrDefault(item => item.Id == regionId);
        if (region is null) return new(false, Failure: RegionTravelFailure.RegionNotFound);
        if (IsCanonicalCombatActive()) return new(false, RegionId: regionId, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: "outside-combat");
        if (Traversal?.IsHazardActive == true || Possession.CanonicalTransitionLocked) return new(false, RegionId: regionId, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: "safe-transition");
        var current = CanonicalContent.Content.Regions.First(item => item.Id == CanonicalRegionId);
        var backwards = region.NextRegionId == current.Id;
        if (current.NextRegionId != regionId && !backwards || !IsNearCanonicalObject(backwards ? $"entry.{current.Id}" : $"portal.{current.Id}"))
            return new(false, RegionId: regionId, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: "at-adjacent-portal");
        var gateRegion = backwards ? null : current;
        var requirements = gateRegion?.PortalRequirements ?? Array.Empty<string>();
        var failed = requirements.FirstOrDefault(requirement => requirement.StartsWith("player.rank.", StringComparison.Ordinal)
            ? Player.State.Rank < int.Parse(requirement["player.rank.".Length..], System.Globalization.CultureInfo.InvariantCulture)
            : !Progression.HasCanonicalFact(requirement));
        if (failed is not null) return new(false, RegionId: regionId, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: failed);
        return WorldMap.PreviewTravel(regionId);
    }

    public RegionTravelResult TravelToCanonicalRegion(string regionId)
    {
        var preview = PreviewCanonicalRegion(regionId);
        if (!preview.Success) return preview;
        var backwards = CanonicalContent!.Content.Regions.First(r => r.Id == regionId).NextRegionId == CanonicalRegionId;
        Possession.End();
        var result = TravelToRegion(regionId);
        if (result.Success)
        {
            Player.SetPosition(WorldMap.CurrentMap.Spawn(backwards ? "portal" : "entry").Position);
            RequireCanonicalDurableCommit();
        }
        return result;
    }

    /// <summary>The authored default checkpoint is the shrine anchor in the current playable map.</summary>
    public bool IsAtCanonicalShrine() => IsWithinCanonicalShrine(48);

    public bool IsWithinCanonicalShrine(double radius)
    {
        if (CanonicalContent is null || !double.IsFinite(radius) || radius < 0) return false;
        var region = CanonicalContent.RegionsForProfile(CanonicalContent.ActiveProfileId).FirstOrDefault(item => item.Id == CanonicalRegionId);
        if (region is null) return false;
        try
        {
            var checkpoint = WorldMap.CurrentMap.Spawn(WorldMap.CurrentRegion.DefaultSpawnId).Position;
            return Player.State.Position.DistanceTo(checkpoint) <= radius;
        }
        catch (KeyNotFoundException) { return false; }
    }

    /// <summary>Conservative simulation gate for shrine Rest and Ready Soul vitality regen.</summary>
    public bool IsCanonicalCombatActive()
    {
        if (CanonicalContent is null) return false;
        if (Combat.ActiveCasts.Count > 0 || Combat.ActiveProjectiles.Count > 0) return true;
        return Monsters.AliveMonsters().Any(monster => monster.TargetUid is not null || monster.AiState is MonsterAiState.Chase or MonsterAiState.Attack || monster.Position.DistanceTo(Player.State.Position) <= 160);
    }

    public bool TryRestAtCanonicalShrine(double seconds) => Spirit?.TryRest(seconds) ?? false;
    public bool TryCanonicalRestReset()
    {
        var allowed = CanonicalContent is not null && IsAtCanonicalShrine() && !IsCanonicalCombatActive() && Traversal?.IsHazardActive != true && !Possession.CanonicalTransitionLocked;
        var result = WorldLifecycleV25?.RestReset(allowed) == true;
        if (result) RequireCanonicalDurableCommit();
        return result;
    }
    public int RespawnTicks => _respawnTicks;
    public void RestoreRespawnTicks(int ticks)
    {
        if (ticks is < 0 or > 120 || Player.State.Alive && ticks != 0 || !Player.State.Alive && ticks == 0)
            throw new InvalidDataException("Respawn timer and player life disagree.");
        _respawnTicks = ticks;
    }

    public void RestoreCanonicalClock(long simulationTick, long uidNext)
    {
        if (CanonicalContent is null) throw new InvalidOperationException("Canonical content is not enabled.");
        if (simulationTick < 0 || uidNext < 0) throw new InvalidDataException("Canonical clock restore is invalid.");
        _clock.Restore(simulationTick);
        _uids.RestoreNext(uidNext);
    }

    public void Start()
    {
        State.TransitionTo(GameStage.Loading);
        State.TransitionTo(GameStage.Playing);
        if (CanonicalContent is not null && Monsters.AllMonsters().Count == 0) SpawnCanonicalRegionEncounters();
    }

    public void Tick(double deltaSeconds)
    {
        if (State.Stage == GameStage.Playing) _clock.Advance(deltaSeconds, FixedStep);
    }

    public void SetInput(Vec2 move, bool attackPressed) { _moveInput = move; _attackPressed = attackPressed; }
    public void SetInput(Vec2 move, bool attackPressed, Vec2 aim, bool dodgePressed)
    {
        _moveInput = move; _attackPressed = attackPressed; _aimInput = aim; _dodgePressed |= dodgePressed;
    }

    private void FixedStep(double deltaSeconds)
    {
        var waitingForRespawn = _respawnTicks > 0;
        if (CanonicalContent is not null) Combat.PrepareCanonicalFixedTick(_clock.TickCount);
        var dodgeOccupiedTick = CanonicalContent is not null && Player.State.DodgeRemainingTicks > 0;
        if (CanonicalContent is not null) Player.TickDodge();
        if (CanonicalContent is not null) Combat.AdvanceCanonicalMovement(_clock.TickCount);
        var startedDodge = CanonicalContent is not null && _dodgePressed && Combat.CanStartCanonicalDodge && Player.TryStartDodge(_moveInput, _aimInput, V25CombatRules.MillisecondsToTicksCeil(CanonicalContent.Balance.Combat.DodgeCooldownMs), V25CombatRules.MillisecondsToTicksCeil(CanonicalContent.Balance.Combat.DodgeMs), V25CombatRules.MillisecondsToTicksCeil(CanonicalContent.Balance.Combat.DodgeInvulnerableMs), CanonicalContent.Balance.Combat.DodgeDistanceUnits);
        if (!startedDodge && !dodgeOccupiedTick) Player.Update(deltaSeconds, _moveInput);
        Monsters.Update(deltaSeconds, Player.State, Allies, Player, Combat.IsActionLocked);
        Allies.Update(deltaSeconds, Monsters, Player.State.Position, _clock.TickCount);
        if (CanonicalContent is not null) Combat.UpdateCanonical(_clock.TickCount, _moveInput, _aimInput, _attackPressed, startedDodge);
        else Combat.Update(deltaSeconds, _attackPressed);
        if (CanonicalContent is not null) Souls.AutoCollect(Player.State.Position, Player.State.Alive);
        Spirit?.Tick(_clock.TickCount);
        Summons.Update(deltaSeconds);
        Progression.Update(deltaSeconds);
        Possession.Update(deltaSeconds);
        TickCanonicalWorld();
        Traversal?.Tick(_clock.TickCount);
        InventoryV25?.Tick(deltaSeconds);
        _dodgePressed = false;
        if (waitingForRespawn && --_respawnTicks == 0)
        {
            var checkpoint = WorldMap.CurrentMap.Spawn(WorldMap.CurrentRegion.DefaultSpawnId).Position;
            Player.SetPosition(checkpoint);
            Player.State.CurrentHp = V25FixedPoint.QuantizeMilli(Player.State.MaxHp * (CanonicalContent?.Balance.Save.RespawnHpFraction ?? 0.5));
            Player.State.CurrentSpirit = V25FixedPoint.QuantizeMilli(Player.State.MaxSpirit * (CanonicalContent?.Balance.Save.RespawnSpiritFraction ?? 0.5));
            // Living encounters reset health without recreating life/reward identities.
            foreach (var monster in Monsters.AliveMonsters())
            {
                monster.CurrentHp = monster.MaxHp;
                monster.Statuses.Clear(); monster.Shields.Clear();
            }
        }
    }
    public MonsterState SpawnMonster(string definitionId, int? level = null, Vec2? position = null) => Monsters.Spawn(definitionId, new MonsterSpawnOptions(Level: level, Position: position));
    public MonsterState SpawnCanonicalEncounter(string encounterId)
    {
        if (CanonicalContent is null) throw new InvalidOperationException("Canonical content is not enabled.");
        var encounter = CanonicalContent.Content.Encounters.FirstOrDefault(item => item.Id == encounterId)
            ?? throw new KeyNotFoundException($"Unknown canonical encounter '{encounterId}'.");
        if (!Enum.TryParse<V25EncounterType>(encounter.EncounterType, ignoreCase: false, out var encounterType))
            throw new InvalidDataException($"Canonical encounter '{encounterId}' has an unknown type '{encounter.EncounterType}'.");
        var definition = Definitions.Monsters.FirstOrDefault(item => item.SpeciesId.Equals(encounter.SpeciesId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"No runtime monster definition exists for canonical species '{encounter.SpeciesId}'.");
        var position = CanonicalEncounterPosition(encounter);
        if (position.X < 0 || position.Y < 0 || position.X > WorldMap.CurrentMap.Width || position.Y > WorldMap.CurrentMap.Height)
            throw new InvalidDataException($"Canonical encounter '{encounter.Id}' is outside the active map; authored data was not clamped.");
        return Monsters.Spawn(definition.Id, new MonsterSpawnOptions(Level: encounter.Level, Position: position,
            EncounterType: encounterType, RewardEligible: encounter.RewardEligible, EncounterId: encounter.Id));
    }

    private MonsterState ResetCanonicalEncounter(string encounterId)
    {
        // Rest reset is the sole producer of a new authored normal/elite life. Remove its
        // previously defeated runtime row first so one encounter never accumulates a second
        // life identity (and therefore a second save/runtime entry) in the same world cycle.
        if (!Monsters.RemoveDefeatedEncounter(encounterId))
            throw new InvalidOperationException($"Canonical Rest reset cannot find one retired encounter life for '{encounterId}'.");
        return SpawnCanonicalEncounter(encounterId);
    }
    public RegionTravelResult TravelToRegion(string regionId)
    {
        var previousRegionId = WorldMap.CurrentRegionId;
        var result = WorldMap.TravelTo(regionId);
        if (!result.Success || result.EntrySpawn is null) return result;
        if (!string.Equals(previousRegionId, WorldMap.CurrentRegionId, StringComparison.Ordinal))
        {
            if (CanonicalContent is not null) Monsters.ParkRegion(previousRegionId);
            World.SetMap(WorldMap.CurrentMap);
            Monsters.Clear();
            Monsters.SetSpawnArea(new SpawnArea(0, 0, WorldMap.CurrentMap.Width, WorldMap.CurrentMap.Height));
            Summons.ClearActiveForMapChange();
            Player.SetBounds(new PlayerBounds(WorldMap.CurrentMap.Width, WorldMap.CurrentMap.Height));
            Player.SetColliders(World.BlockingRects());
            Traversal?.ResetForRegion();
            _hazardTicks.Clear();
            if (CanonicalContent is not null && !Monsters.ResumeRegion(WorldMap.CurrentRegionId, SimulationTick)) SpawnCanonicalRegionEncounters();
        }
        Player.SetPosition(result.EntrySpawn.Position);
        return result;
    }

    private void SpawnCanonicalRegionEncounters()
    {
        if (CanonicalContent is null) return;
        foreach (var encounter in CanonicalContent.Content.Encounters.Where(item => item.RegionId == CanonicalRegionId).OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (WorldLifecycleV25?.ShouldSpawn(encounter.Id) != false) SpawnCanonicalEncounter(encounter.Id);
        }
    }
    private Vec2 CanonicalEncounterPosition(CanonicalEncounterDefinition encounter)
    {
        var layout = CanonicalContent!.Content.LayoutBlueprint; var tile = layout.TileSize;
        var cell = layout.ChunkGrid.TryGetValue(encounter.Chunk, out var chunk) ? chunk : throw new InvalidDataException($"Unknown canonical chunk '{encounter.Chunk}'.");
        IReadOnlyList<int> local = encounter.CenterTile is { Count: >= 2 } center ? center
            : encounter.CenterIndex is { } index && index >= 0 && index < layout.EncounterCenters.Count
                ? new[] { layout.EncounterCenters[index][0] + (encounter.OffsetTiles?[0] ?? 0), layout.EncounterCenters[index][1] + (encounter.OffsetTiles?[1] ?? 0) }
                : throw new InvalidDataException($"Canonical encounter '{encounter.Id}' has no valid authored position.");
        return new Vec2((cell[0] * layout.ChunkTiles[0] + local[0]) * tile, (cell[1] * layout.ChunkTiles[1] + local[1]) * tile);
    }
    public void Dispose() { _deathSubscription.Dispose(); _worldSubscription.Dispose(); foreach (var subscription in _canonicalDurabilitySubscriptions) subscription.Dispose(); WorldLifecycleV25?.Dispose(); QuestsV25?.Dispose(); MasteryV25?.Dispose(); LootV25?.Dispose(); UniquePowersV25?.Dispose(); Sync?.Dispose(); Summons.Dispose(); Progression.Dispose(); Souls.Dispose(); Combat.Dispose(); }
}
