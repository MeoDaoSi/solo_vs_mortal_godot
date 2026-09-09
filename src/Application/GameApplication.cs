using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Application.Persistence;
using SoloVsMortal.Application.Persistence.V25;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Core.Rng;
using SoloVsMortal.Simulation.State.V25;
using SoloVsMortal.Simulation.Systems.V25;

namespace SoloVsMortal.Application;

/// <summary>Command/query boundary exposed to Presentation.</summary>
public sealed class GameApplication
{
    private GameSession _session;
    private IDisposable? _defeatSubscription;
    private readonly Queue<DefeatedMonsterVisualSnapshot> _defeatedVisuals = new();
    private long _v25CommitSequence;

    public GameApplication(GameSession session, CanonicalContentRegistry? canonicalContent = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        CanonicalContent = canonicalContent;
        SubscribeToSession();
    }

    private void SubscribeToSession()
    {
        _defeatSubscription = _session.Events.Subscribe<MonsterDefeatedEvent>(defeated =>
        {
            var action = _session.Definitions.CharacterAnimations.MonsterActions["death"];
            _defeatedVisuals.Enqueue(new(defeated.Uid, defeated.SpeciesId, defeated.Rank, defeated.Position, action.FrameCount / action.FrameRate));
        });
    }

    public static GameApplication CreateFromDefinitionsDirectory(string directory, string profileId = "beta_01")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var canonicalDirectory = Path.GetFullPath(Path.Combine(directory, "..", "v2.5"));
        var canonical = CanonicalV25Loader.LoadFromDirectory(canonicalDirectory, profileId, "2026-09-08.closed-1");
        canonical.ActiveProfileId = profileId;
        return new(new GameSession(GameDefinitionLoader.LoadFromDirectory(directory).WithCanonicalRoster(canonical), canonical: canonical), canonical);
    }

    /// <summary>Validated, hash-pinned V2.5 content exposed to the application boundary for staged systems.</summary>
    public CanonicalContentRegistry? CanonicalContent { get; private set; }

    public void Start() => _session.Start();

    public void Tick(double deltaSeconds) => _session.Tick(deltaSeconds);
    public void SetInput(Vec2 move, bool attackPressed) => _session.SetInput(move, attackPressed);
    public void SetInput(Vec2 move, bool attackPressed, Vec2 aim, bool dodgePressed) => _session.SetInput(move, attackPressed, aim, dodgePressed);
    public void RequestPlayerSkill(string skillId) => _session.Combat.RequestPlayerSkill(skillId);
    public void RequestCanonicalLearnedSkill(int slot) => RequestCanonicalResolvedSkill(_session.SkillGrantsV25?.LearnedButtonSkill(slot));
    public void RequestCanonicalMainHandSkill() => RequestCanonicalResolvedSkill(_session.SkillGrantsV25?.MainHandButtonSkill);
    public void RequestCanonicalOffHandSkill() => RequestCanonicalResolvedSkill(_session.SkillGrantsV25?.OffHandButtonSkill);
    public void RequestCanonicalPossessionSkill() => RequestCanonicalResolvedSkill(_session.SkillGrantsV25?.PossessionButtonSkill);
    public void RequestCanonicalUniqueSkill() => RequestCanonicalResolvedSkill(_session.SkillGrantsV25?.UniqueButtonSkill);
    public string SpawnMonster(string definitionId, int? level = null, Vec2? position = null) => _session.SpawnMonster(definitionId, level, position).Uid;
    public void AddPlayerXp(double amount) => _session.Progression.AddPlayerXp(amount);
    public bool AttemptPlayerBreakthrough(PillId? pillId = null) => _session.Progression.AttemptPlayerBreakthrough(CanonicalContent is null ? pillId : null);
    public V25BannerUpgradeResult AttemptCanonicalBannerUpgrade() { var result = _session.SoulBanners.UpgradeCanonical(); if (result.Success && !result.AlreadyApplied) _session.RequireCanonicalDurableCommit(); return result; }
    public int CanonicalBannerRank => _session.SoulBanners.CanonicalBannerRank;
    public void RecordCanonicalSyncSource(string sourceId, string eventId, bool atShrine = false, bool currentSpeciesPossession = false) { (_session.Sync ?? throw new InvalidOperationException("Canonical Sync is not enabled.")).RecordSourceEvent(sourceId, eventId, atShrine, currentSpeciesPossession); _session.RequireCanonicalDurableCommit(); }
    public WorldSoulState? SpawnCanonicalTutorialSoul(string questReceiptId, string speciesId, int level, Vec2 position) => _session.Souls.SpawnCanonicalTutorialSoul(questReceiptId, speciesId, level, position);
    public IReadOnlyList<string> AcquireNearbySouls() => _session.Souls.AcquireNear(_session.Player.State.Position, _session.CanonicalContent is null ? _session.Definitions.Soul.PickupRadius : 48).Select(soul => soul.Id).ToArray();
    public bool HasCanonicalDurableChanges => _session.CanonicalDurableCommitRequired;
    public void PrepareCanonicalRewardCommit()
    {
        if (!_session.Souls.HasCanonicalAcquisitionsPendingCommit) return;
        _session.Sync?.PrepareOwnershipCommit();
        _session.QuestsV25?.PrepareOwnershipCommit();
    }
    public void CommitCanonicalAcquisitionEvents()
    {
        _session.CompleteCanonicalDurableCommit();
        _session.Souls.CommitCanonicalAcquisitionEvents();
        _session.Sync?.CommitPreparedEvents();
    }
    public BindSoulResult BindSoul(string soulId, string bannerId) => CanonicalContent is null ? _session.SoulBanners.Bind(soulId, bannerId) : new(false, BindSoulFailure.SoulNotOwned);
    public UnbindSoulResult UnbindSoul(string soulId, string bannerId)
    {
        var runtime = _session.Summons.Runtime(soulId);
        if (runtime.Status is SoulRuntimeStatus.Summoned or SoulRuntimeStatus.Possessed) return new(false, UnbindSoulFailure.SoulActive);
        return CanonicalContent is null ? _session.SoulBanners.Unbind(soulId, bannerId) : new(false, UnbindSoulFailure.SoulNotBound);
    }
    public StartPossessionResult StartPossession(string soulId) => _session.Possession.Start(soulId);
    public bool EndPossession() => _session.Possession.End();
    public bool RestAtCanonicalShrine(double seconds = 1) => _session.TryRestAtCanonicalShrine(seconds);
    public bool RestResetCanonicalEncounters() => _session.TryCanonicalRestReset();
    public bool DiscoverCanonicalLandmark(string landmarkId) { var result = _session.TryDiscoverCanonicalLandmark(landmarkId); if (result) _session.RequireCanonicalDurableCommit(); return result; }
    public bool OpenCanonicalChest(string chestId) { var result = _session.TryOpenCanonicalChest(chestId); if (result) _session.RequireCanonicalDurableCommit(); return result; }
    public bool ObserveCanonicalSafeAnchor(Vec2 position, bool safeWalkmesh = true, bool inHazard = false, string gateStateId = "open") =>
        _session.Traversal?.ObserveSafeAnchor(position, _session.SimulationTick, safeWalkmesh, inHazard, gateStateId) ?? false;
    public V25TraversalResult TraverseCanonical(V25TraversalRequest request) =>
        _session.Traversal?.TryTraverse(request, _session.SimulationTick) ?? new(false, _session.Player.State.Position, V25TraversalFailure.UnknownTerrainProducer);
    public bool ReturnToCanonicalSafeAnchor() => _session.Traversal?.TryReturnToSafeAnchor() ?? false;
    public bool SetAllyMode(AllyAiMode mode, string? soulId = null) => _session.Allies.SetCanonicalMode(mode, soulId);
    public bool FocusAlly(string allyUid, string targetUid) => _session.Allies.FocusCanonical(allyUid, targetUid, _session.Monsters, _session.SimulationTick);
    public bool WasCanonicalTileVisited(Vec2 position) => CanonicalContent is null || _session.WasVisited(position);
    public IReadOnlyList<V25TerrainArea> CanonicalTerrain() => _session.CanonicalTerrain;
    public IReadOnlyList<V25RoadSegment> CanonicalRoads() => CanonicalContent is null ? Array.Empty<V25RoadSegment>() : V25WorldLayout.Roads(CanonicalContent.Content.LayoutBlueprint);
    public bool HasSpeciesAnimation(string speciesId) => _session.Definitions.CharacterAnimations.MonstersBySpecies.Keys.Any(id => id.Equals(speciesId, StringComparison.OrdinalIgnoreCase));
    public string SpeciesDisplayName(string speciesId) => CanonicalContent?.Content.Species.FirstOrDefault(s => s.Id == speciesId)?.Name ?? speciesId;
    public bool HasAsset(string id) => _session.Definitions.Assets.Assets.ContainsKey(id);
    public bool UpgradeCanonicalProfile()
    {
        if (CanonicalContent?.ActiveProfileId != "beta_01" || !_session.IsAtCanonicalShrine() || _session.IsCanonicalCombatActive()) return false;
        RestoreCanonicalSave(CaptureCanonicalSave(), upgradeToFull: true);
        _session.RequireCanonicalDurableCommit(); return true;
    }
    public string? InteractNearestCanonical()
    {
        if (CanonicalContent is null) return null;
        var item = _session.World.CurrentMap.Objects.Values.Where(o => o.Type is "npc" or "landmark" or "secret" or "chest" or "portal" or "shrine")
            .Where(o => _session.IsNearCanonicalObject(o.Id)).OrderBy(o => o.Position.DistanceTo(_session.Player.State.Position)).FirstOrDefault();
        if (item is null) return null;
        switch (item.Type)
        {
            case "npc": return InteractCanonicalNpc(item.Id[4..]).Message ?? "Đã tương tác NPC.";
            case "landmark": return DiscoverCanonicalLandmark(item.Id) ? "Đã khám phá địa danh." : "Địa danh đã được ghi nhận.";
            case "chest": return OpenCanonicalChest(item.Id) ? "Đã nhận vật phẩm và coin từ rương." : "Rương đã mở hoặc chưa thể nhận thưởng.";
            case "secret": return _session.TryInteractCanonicalSecret(item.Id) ? "Đã nhận Sync bí mật." : "Cần phụ hồn đúng loài, Sync 40 và capability tương ứng; hoặc đã nhận thưởng.";
            case "shrine": return "Shrine: dùng Nghỉ / Nhận thưởng / Nghi thức trong bảng chức năng.";
            case "portal":
                var current = CanonicalContent.Content.Regions.First(r => r.Id == _session.CanonicalRegionId);
                var target = item.Id.StartsWith("entry.", StringComparison.Ordinal) ? CanonicalContent.Content.Regions.FirstOrDefault(r => r.NextRegionId == current.Id)?.Id : current.NextRegionId;
                return target is not null && _session.TravelToCanonicalRegion(target).Success ? "Đã chuyển vùng." : "Cổng chưa mở hoặc đã tới giới hạn profile.";
        }
        return null;
    }

    public WorldInteractionResult Interact() => _session.World.TryInteract(_session.Player.State.Position);
    public SummonResult SummonSoul(string soulId, string bannerId, Vec2 position) => _session.Summons.Summon(soulId, bannerId, position);
    public bool UnsummonSoul(string soulId) => _session.Summons.UnsummonSoul(soulId);
    public SoulRuntimeView SoulRuntime(string soulId) => _session.Summons.Runtime(soulId);
    public string? ActivePossessionSoulId => _session.Possession.ActiveSoulId;
    public double PossessionRemainingSeconds() => _session.Possession.RemainingSeconds;
    public IReadOnlyList<InventoryItem> Inventory() => CanonicalContent is not null
        ? (_session.InventoryV25?.Items.Concat(_session.InventoryV25.Overflow).Select(item => new InventoryItem(item.DefinitionId, item.Count)).ToArray() ?? Array.Empty<InventoryItem>())
        : _session.Progression.InventorySnapshot();
    public V25InventorySnapshot? CanonicalInventory() => _session.InventoryV25?.Snapshot();
    public V25InventoryResult AddCanonicalItem(string definitionId, int amount = 1) => MarkIfSuccessful(_session.InventoryV25?.AddItem(definitionId, amount) ?? new(false, V25InventoryFailure.InvalidState));
    public V25InventoryResult BuyCanonicalItem(string definitionId, int amount = 1) => !_session.CanUseCanonicalNpc("kha") ? new(false, V25InventoryFailure.InvalidState) : MarkIfSuccessful(_session.InventoryV25?.Buy(definitionId, amount) ?? new(false, V25InventoryFailure.InvalidState));
    public V25InventoryResult SellCanonicalItem(string instanceUid) => !_session.CanUseCanonicalNpc("kha") ? new(false, V25InventoryFailure.InvalidState) : MarkIfSuccessful(_session.InventoryV25?.Sell(instanceUid) ?? new(false, V25InventoryFailure.InvalidState));
    public V25InventoryResult WithdrawCanonicalOverflow(string instanceUid) => !_session.IsAtCanonicalShrine() || _session.IsCanonicalCombatActive()
        ? new(false, V25InventoryFailure.InvalidState) : MarkIfSuccessful(_session.InventoryV25?.WithdrawOverflow(instanceUid) ?? new(false, V25InventoryFailure.InvalidState));
    public bool ClearCanonicalSkillSlot(int slot, bool passive) => MarkIfTrue(_session.SkillGrantsV25?.ClearSlot(slot, passive) == true);
    public V25InventoryResult EquipCanonicalItem(string instanceUid) => MarkIfSuccessful(_session.InventoryV25?.Equip(instanceUid) ?? new(false, V25InventoryFailure.InvalidState));
    public V25InventoryResult UnequipCanonicalItem(V25EquipmentSlot slot) => MarkIfSuccessful(_session.InventoryV25?.Unequip(slot) ?? new(false, V25InventoryFailure.InvalidState));
    public V25InventoryResult UseCanonicalPotion(string definitionId) => MarkIfSuccessful(_session.InventoryV25?.UsePotion(definitionId) ?? new(false, V25InventoryFailure.InvalidState));
    public V25SkillGrantSnapshot? CanonicalSkillGrants() => _session.SkillGrantsV25?.Snapshot();
    public bool LearnCanonicalSkill(string skillId)
    {
        if (!_session.CanUseCanonicalNpc("linh") || _session.SkillGrantsV25?.Learn(skillId) != true) return false;
        _session.MasteryV25?.EnsureLearnedTrack(skillId);
        _session.RequireCanonicalDurableCommit();
        return true;
    }
    public bool AssignCanonicalActiveSkill(int slot, string skillId) => MarkIfTrue(_session.SkillGrantsV25?.AssignActive(slot, skillId) == true);
    public bool AssignCanonicalPassiveSkill(int slot, string skillId) => MarkIfTrue(_session.SkillGrantsV25?.AssignPassive(slot, skillId) == true);
    public bool PromoteCanonicalSkill(string skillId, int rank)
    {
        var mastery = _session.MasteryV25?.Skill(skillId);
        if (mastery is null || rank != mastery.PromotedRank + 1 || _session.MasteryV25!.TryPromote(skillId) != true) return false;
        if (_session.SkillGrantsV25?.Promote(skillId, rank) != true) throw new InvalidOperationException("Mastery and skill-grant promotion state diverged.");
        _session.RequireCanonicalDurableCommit();
        return true;
    }

    private V25InventoryResult MarkIfSuccessful(V25InventoryResult result) { if (result.Success) _session.RequireCanonicalDurableCommit(); return result; }
    private bool MarkIfTrue(bool result) { if (result) _session.RequireCanonicalDurableCommit(); return result; }

    private void RequestCanonicalResolvedSkill(string? skillId)
    {
        if (CanonicalContent is null || string.IsNullOrWhiteSpace(skillId)) return;
        _session.Combat.RequestPlayerSkill(skillId);
    }
    public V25MasterySnapshot? CanonicalMastery() => _session.MasteryV25?.Snapshot();
    public bool PromoteCanonicalMastery(string skillId) => _session.MasteryV25?.TryPromote(skillId) == true;
    public IReadOnlyList<V25QuestState> CanonicalQuests() => _session.QuestsV25?.Quests ?? Array.Empty<V25QuestState>();
    public V25QuestInteractionResult InteractCanonicalNpc(string npcId) { var result = !_session.CanUseCanonicalNpc(npcId) ? new V25QuestInteractionResult(false, Message: "Hãy đến gần NPC (48 units), ngoài giao tranh.") : _session.QuestsV25?.InteractNpc(npcId) ?? new(false, Message: "Canonical quests are not enabled."); if (result.Success || result.HintShown) _session.RequireCanonicalDurableCommit(); return result; }
    public bool RecordCanonicalQuestObjective(string eventName, string target, string eventId) => MarkIfTrue(_session.QuestsV25?.RecordObjective(eventName, target, eventId) == true);
    public V25QuestRewardResult ClaimCanonicalQuestReward(string questId) { var result = _session.QuestsV25?.ClaimReward(questId) ?? new(false, QuestId: questId, Failure: "Canonical quests are not enabled."); if (result.Success && !result.AlreadyClaimed) _session.RequireCanonicalDurableCommit(); return result; }
    public IReadOnlyList<V25LootAward> CanonicalLootAwards() => _session.LootV25?.Awards ?? Array.Empty<V25LootAward>();
    public IReadOnlyList<V25UniquePowerState> CanonicalUniquePowers() => _session.UniquePowersV25?.Powers ?? Array.Empty<V25UniquePowerState>();
    public bool ClaimCanonicalUniquePower(string powerId) => MarkIfTrue(_session.UniquePowersV25?.Claim(powerId) == true);
    public string? CurrentMapBackgroundAssetId() => _session.World.CurrentMap.BackgroundAssetId;
    public IReadOnlyList<WorldMapRegionSnapshot> WorldMapRegions() => _session.WorldMap.Regions.Select(BuildRegionSnapshot).ToArray();
    public WorldMapRegionSnapshot? RegionDetails(string regionId) => _session.WorldMap.Regions.FirstOrDefault(region => region.Id == regionId) is { } region ? BuildRegionSnapshot(region) : null;
    public RegionTravelResult TravelToRegion(string regionId) => CanonicalContent is null ? _session.TravelToRegion(regionId) : _session.TravelToCanonicalRegion(regionId);
    public IReadOnlyList<SoulLinkView> SoulLinks()
    {
        var banner = _session.SoulBanners.Starter();
        if (CanonicalContent is not null)
        {
            var canonicalRank = _session.SoulBanners.CanonicalBannerRank;
            return _session.Souls.OwnedSouls().Select(soul =>
            {
                var runtime = _session.Summons.Runtime(soul.Id);
                var state = runtime.Status switch
                {
                    SoulRuntimeStatus.Summoned => SoulLinkState.Manifested,
                    SoulRuntimeStatus.Dispersed => SoulLinkState.Dispersed,
                    SoulRuntimeStatus.Possessed => SoulLinkState.Possessed,
                    _ => SoulLinkState.Dormant,
                };
                var species = CanonicalContent.SpeciesForProfile(CanonicalContent.ActiveProfileId).FirstOrDefault(item => item.Id == soul.Origin.SpeciesId);
                var eligible = species is not null && species.PowerTier <= canonicalRank;
                return new SoulLinkView(soul.Id, soul.Origin.DisplayName, "canonical.banner", state, 0, runtime.Stability,
                    eligible && runtime.Status == SoulRuntimeStatus.Ready && _session.Player.State.CurrentSpirit > 0,
                    eligible && runtime.Status == SoulRuntimeStatus.Ready && _session.Possession.ActiveSoulId is null,
                    false);
            }).ToArray();
        }
        var bound = banner?.BoundSoulIds.ToHashSet(StringComparer.Ordinal) ?? [];
        var activeCount = _session.Summons.ActiveCount;
        return _session.Souls.OwnedSouls().Select(soul =>
        {
            var runtime = _session.Summons.Runtime(soul.Id);
            var state = runtime.Status switch
            {
                SoulRuntimeStatus.Summoned => SoulLinkState.Manifested,
                SoulRuntimeStatus.Dispersed => SoulLinkState.Dispersed,
                SoulRuntimeStatus.Possessed => SoulLinkState.Possessed,
                _ => SoulLinkState.Dormant,
            };
            var linked = banner is not null && bound.Contains(soul.Id);
            var cost = _session.Souls.Cost(soul.Id) ?? 0;
            var canPossess = linked && runtime.Status == SoulRuntimeStatus.Ready && _session.Definitions.SoulNatures.Natures.TryGetValue(soul.SoulNatureId, out var nature) && nature.PossessionProfileId is not null;
            var canDevour = !linked && runtime.Status == SoulRuntimeStatus.Ready && _session.Devouring.Previews(soul.Id).Count > 0;
            var canSummon = linked && runtime.Status == SoulRuntimeStatus.Ready && banner is not null && activeCount < banner.Computed.ActiveLimit;
            return new SoulLinkView(soul.Id, soul.Origin.DisplayName, linked ? banner!.Id : null, state, cost, runtime.Stability, canSummon, canPossess, canDevour);
        }).ToArray();
    }
    public IReadOnlyList<WorldObjectSnapshot> WorldObjects() => _session.World.CurrentMap.Objects.Values
        .Select(item => new WorldObjectSnapshot(item.Id, item.Type, item.AssetId, item.Position, item.Blocking, _session.World.IsDestroyed(item.Id), item.ZoneId, item.PresentationScale, _session.World.CurrentMap.Layers[item.LayerId].ZIndex))
        .ToArray();
    public AssetSnapshot Asset(string logicalId) { var asset = _session.Definitions.Assets.Get(logicalId); return new(asset.Id, asset.File, asset.FrameWidth, asset.FrameHeight); }
    public AnimationClipSnapshot MonsterAnimation(string speciesId, int rank, string actionId)
    {
        var animations = _session.Definitions.CharacterAnimations.MonstersBySpecies;
        var descriptor = animations.GetValueOrDefault(speciesId) ?? animations.GetValueOrDefault(speciesId.ToUpperInvariant());
        var action = _session.Definitions.CharacterAnimations.MonsterActions[actionId];
        // Canonical IDs are lower-case while the legacy animation manifest uses Pascal/upper-case
        // species keys. Missing art is a presentation warning; it must not abort the simulation boot.
        if (descriptor is null) return new(action.Id, action.FrameRate, action.Repeat, Array.Empty<string>());
        var form = descriptor.Forms[SpriteStageRules.GetSpriteStage(speciesId.ToUpperInvariant(), rank) - 1];
        var files = Enumerable.Range(0, action.FrameCount).Select(frame => descriptor.FramePathPattern.Replace("{rootDir}", descriptor.RootDirectory, StringComparison.Ordinal).Replace("{formDirectory}", form.Directory, StringComparison.Ordinal).Replace("{actionDirectory}", action.Directory, StringComparison.Ordinal).Replace("{filePrefix}", form.FilePrefix, StringComparison.Ordinal).Replace("{frame}", frame.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("assets/", "", StringComparison.Ordinal)).ToArray();
        return new(action.Id, action.FrameRate, action.Repeat, files);
    }
    public PlayerAnimationClipSnapshot PlayerAnimation(string actionId, string directionId, int rank)
    {
        var descriptor = _session.Definitions.CharacterAnimations.Player; var action = descriptor.Actions[actionId]; var form = System.Math.Clamp((rank - 1) / descriptor.RanksPerForm + 1, 1, descriptor.FormCount);
        var asset = Asset(descriptor.AssetIdPattern.Replace("{form}", form.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{action}", action.AssetAction, StringComparison.Ordinal));
        var count = action.FramesByDirection.GetValueOrDefault(directionId, action.Columns);
        return new($"{actionId}_{directionId}", asset, descriptor.Directions[directionId], count, action.FrameRate, action.Repeat, descriptor.Scale);
    }
    public IReadOnlyList<DefeatedMonsterVisualSnapshot> DrainDefeatedMonsterVisuals() { var result = _defeatedVisuals.ToArray(); _defeatedVisuals.Clear(); return result; }

    public GameSaveData CaptureSave() => new(
        GameSaveCodec.CurrentVersion,
        new(_session.Player.State.CurrentHp, _session.Player.State.MaxHp, _session.Player.State.Level, _session.Player.State.Rank, _session.Player.State.TitleDisplayName, null, _session.Player.State.Xp),
        _session.Souls.OwnedSouls().Select(soul => new OwnedSoulSaveData(soul.Id, soul.SoulNatureId, soul.Level, soul.Xp, new SoulOriginSaveData(soul.Origin.MonsterUid, soul.Origin.MonsterDefinitionId, soul.Origin.SpeciesId, soul.Origin.DisplayName, soul.Origin.Rank, soul.Origin.RankKey, soul.Origin.RankDisplayName))).ToArray(),
        _session.SoulBanners.Banners().Select(banner => new SoulBannerSaveData(banner.Id, TierId(banner.Tier), banner.Level, banner.BoundSoulIds.ToArray())).ToArray(),
        CaptureProgressionSave(),
        _session.Summons.DispersedSnapshot().Select(item => new SoulRuntimeSaveData(item.SoulId, item.RecoverySeconds, item.RecoveryDurationSeconds)).ToArray(),
        new(_session.Essence.Snapshot()), new(_session.Bloodline.Snapshot()),
        _session.Possession.Snapshot() is { } possession ? new(possession.SoulId, possession.ProfileId, possession.RemainingSeconds) : null,
        new(_session.World.DestroyedObjectIds(), _session.WorldMap.CurrentRegionId));

    public string CaptureSaveJson() => CanonicalContent is null ? GameSaveCodec.Serialize(CaptureSave()) : V25SaveCodec.Serialize(CaptureCanonicalSave());
    public void RestoreSaveJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (CanonicalContent is null)
        {
            RestoreSave(LegacySaveAdapter.DeserializeForRuntime(json, _session.Definitions));
            return;
        }
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("format", out var format) && format.ValueKind == System.Text.Json.JsonValueKind.String && string.Equals(format.GetString(), V25SaveFormat.FormatId, StringComparison.Ordinal))
        {
            RestoreCanonicalSave(V25SaveCodec.Deserialize(json, CanonicalContent));
            return;
        }
        // Legacy v1-v6 remains recognized only through its own namespace. There is no lossless
        // V2.5 mapping for its Soul/Essence/Bloodline payload yet, so fail closed before any state mutates.
        var recognition = LegacySaveAdapter.Recognize(json);
        throw new InvalidDataException(recognition.IsRecognized
            ? $"Legacy save v{recognition.Version} is recognized but has no lossless V2.5 mapping; the original file must be preserved."
            : recognition.Error ?? "Unsupported save format; the original file must be preserved.");
    }

    public bool CanManualCanonicalSave => CanonicalContent is null ||
        (!_session.IsCanonicalCombatActive() && _session.Traversal?.IsHazardActive != true && !_session.Possession.CanonicalTransitionLocked);

    public V25SaveEnvelope CaptureCanonicalSave(long minimumSequence = 0, string saveId = "slot.primary")
    {
        if (CanonicalContent is null) throw new InvalidOperationException("Canonical content is not enabled.");
        if (saveId is not ("slot.primary" or "slot.2" or "slot.3")) throw new ArgumentOutOfRangeException(nameof(saveId), "Canonical saves support exactly the primary, second and third slots.");
        var runtime = CanonicalRuntimeSnapshot();
        var sequence = checked(Math.Max(minimumSequence, Math.Max(_v25CommitSequence + 1, runtime.SimulationTick)));
        _v25CommitSequence = sequence;
        var profile = CanonicalContent.Profile(CanonicalContent.ActiveProfileId);
        var densitySnapshot = _session.Souls.CanonicalDensitySnapshot();
        var summonStates = _session.Summons.CanonicalSnapshot().ToDictionary(item => item.SpeciesId, StringComparer.Ordinal);
        var ownedSpecies = densitySnapshot.Ownership.Where(state => state.IsOwned).OrderBy(state => state.SpeciesId, StringComparer.Ordinal).Select(state =>
        {
            var definition = CanonicalContent.Content.Species.First(item => item.Id == state.SpeciesId);
            var summon = summonStates.GetValueOrDefault(state.SpeciesId) ?? new V25SummonStateSnapshot(state.SpeciesId, V25SoulRuntimeMode.Ready, null, V25FixedPoint.DensitySyncScale, 0, 1, 0);
            return new V25OwnedSpeciesSaveState(state.SpeciesId, definition.PowerTier, state.CommittedDensityMicro, state.PendingDensityMicro, 0, summon.VitalityMicro,
                summon.Mode.ToString(), summon.ActiveAllyUid, state.PassedGateIndex, state.ProofKeys.ToArray());
        }).ToArray();
        var densityAwards = densitySnapshot.Ledger.Select(entry => new V25DensityAwardSaveRecord(entry.AwardId, entry.SourceId, entry.SourceVersion, entry.GrantedMicro, entry.AppliedCommitted, entry.AddedPending, entry.Discarded, entry.ProofKeys.ToArray(), entry.TransactionId, entry.SpeciesId, entry.ResultCommittedDensityMicro, entry.ResultPendingDensityMicro, entry.ResultPassedGateIndex, entry.SourceLevel, entry.PreTransactionSoulRank)).ToArray();
        var worldSouls = _session.Souls.CanonicalPickups.Select(pickup => new V25WorldSoulSaveState(pickup.PickupId, pickup.MonsterUid, pickup.MonsterDefinitionId, pickup.SpeciesId, pickup.DisplayName, pickup.Rank, pickup.RankKey, pickup.RankDisplayName, pickup.SourceLevel, pickup.SourceRank, pickup.Position.X, pickup.Position.Y, pickup.SoulNatureId, pickup.RewardEligible)).ToArray();
        var pity = _session.Souls.CanonicalPity.Select(item => new V25SoulPitySaveState(item.SpeciesId, item.Counter)).ToArray();
        var sync = (_session.Sync?.Snapshot() ?? Array.Empty<V25SpeciesSyncSnapshot>()).Select(state => new V25SpeciesSyncSaveState(
            state.SpeciesId,
            state.Awards.Select(award => new V25SyncAwardSaveRecord(award.AwardId, award.SourceKey, award.SourceVersion, award.AwardedMicroPoints, award.CommitSequence)).ToArray(),
            state.UnlockedMilestoneIds,
            state.LegacyDedupKeys,
            state.Progress.Select(progress => new V25SyncSourceProgressSaveState(progress.SourceId, progress.Count, progress.EventIds)).ToArray())).ToArray();
        var payload = new V25SaveDocument(
            profile.Id,
            _session.UidNext.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _session.CanonicalRegionId,
            new V25PlayerSaveState(_session.Player.State.Level, _session.Player.State.Xp, V25FixedPoint.RoundMilli(_session.Player.State.CurrentHp), V25FixedPoint.RoundMilli(_session.Player.State.MaxHp), V25FixedPoint.RoundMilli(_session.Player.State.CurrentSpirit), V25FixedPoint.RoundMilli(_session.Player.State.MaxSpirit), _session.CanonicalRegionId, CanonicalContent.Content.NewGame.CheckpointId, _session.Player.State.Uid),
            ownedSpecies,
            densityAwards,
            sync,
            _session.Progression.CanonicalRewardReceiptSnapshot().Select(receipt => new V25ReceiptSaveRecord(receipt, "combatReward", sequence))
                .Concat(_session.Progression.CanonicalBreakthroughReceiptSnapshot().Select(receipt => new V25ReceiptSaveRecord(receipt, "breakthrough", sequence)))
                .Concat(sync.SelectMany(state => state.Awards.Select(award => new V25ReceiptSaveRecord(award.AwardId, "sync", sequence)))).ToArray(),
            _session.Progression.CanonicalFactSnapshot().Select(fact => fact.FactId).ToArray(),
            ToV25RuntimeSave(runtime) with { RespawnTicks = _session.RespawnTicks, BufferedSkillId = _session.Combat.BufferedSkillId, BufferedSkillTicks = _session.Combat.BufferedSkillTicks, MonsterNavigation = _session.Monsters.NavigationSnapshot() },
            worldSouls,
            pity,
            _session.Souls.CanonicalConsumedPickupIds,
            _session.Souls.CanonicalTutorialReceipts,
            _session.Progression.CanonicalFactSnapshot().Select(fact => new V25FactSaveRecord(fact.FactId, fact.ProducerId, fact.SourceId, fact.SourceVersion, fact.ProducedTick)).ToArray(),
            _session.SoulBanners.CanonicalBannerRank,
            _session.SoulBanners.CanonicalUpgradeReceipts.Select(receipt => new V25BannerGateSaveRecord(receipt.GateId, receipt.GateVersion, receipt.ReceiptId, receipt.FromRank, receipt.ToRank)).ToArray(),
            summonStates.Values.Select(state => new V25SummonSaveState(state.SpeciesId, state.RecoveryTicks, state.HpRatio, state.AttackCooldown)).ToArray(),
            _session.Spirit?.RemainderMicro ?? 0,
            _session.Spirit?.RateRemainderMicro ?? 0,
            _session.Possession.CanonicalSnapshot is { } possession ? new V25PossessionSaveState(possession.SourceInstanceId, possession.SoulId, possession.SpeciesId, possession.SoulLevel, possession.SoulRank, possession.SyncMicro,
                possession.MilestoneIds, possession.SoulBaseHp, possession.SoulBaseAttack, possession.SoulBaseDefense, possession.SoulBaseMoveSpeed, possession.TransferHp, possession.TransferAttack, possession.TransferDefense, possession.TransferMoveSpeed,
                possession.SignatureSkillId, possession.CapabilityId, possession.DurationSeconds, possession.RemainingSeconds, possession.CooldownSeconds, possession.StartedTick) : null,
            _session.Possession.CanonicalCooldowns.Select(cooldown => new V25PossessionCooldownSaveState(cooldown.SpeciesId, cooldown.RemainingSeconds)).ToArray(),
            _session.Traversal?.Snapshot() is { } traversal ? new V25TraversalSaveState(
                traversal.SafeAnchor is { } anchor ? new V25SafeAnchorSaveState(anchor.PositionX, anchor.PositionY, anchor.ConfirmedTick, anchor.GateStateId, anchor.SafeWalkmesh) : null,
                traversal.AnchorCandidateTicks, traversal.CandidatePositionX, traversal.CandidatePositionY, traversal.CandidateGateStateId,
                traversal.FlightGraceTicks, traversal.BreathingGraceTicks, traversal.ActiveTerrain?.ToString(), traversal.ActiveWidthUnits,
                traversal.ActiveGateStateId, traversal.ActiveStartedTick, traversal.CrumblingTicks) : null,
            _session.InventoryV25?.Snapshot() is { } inventory ? new V25InventorySaveState(
                inventory.Coins,
                inventory.Items.Select(item => new V25ItemInstanceSaveState(item.InstanceUid, item.DefinitionId, item.Count)).ToArray(),
                inventory.Overflow.Select(item => new V25ItemInstanceSaveState(item.InstanceUid, item.DefinitionId, item.Count)).ToArray(),
                inventory.Equipped.Select(item => new V25EquippedItemSaveState(item.Slot.ToString(), item.InstanceUid, item.DefinitionId)).ToArray(),
                inventory.SharedPotionCooldownTicks, inventory.NextInstance) : null,
            _session.SkillGrantsV25?.Snapshot() is { } skills ? new V25SkillGrantSaveState(
                skills.LearnedSkillIds.ToArray(), skills.ActiveSkillIds.ToArray(), skills.PassiveSkillIds.ToArray(),
                skills.PromotedRanks.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)) : null,
            _session.MasteryV25?.Snapshot() is { } mastery ? new V25MasterySaveState(
                mastery.Skills.Select(skill => new V25MasterySkillSaveState(skill.SkillId, skill.Metric, skill.PromotedRank, skill.ProgressMicro,
                    skill.Credits.Select(credit => new V25MasteryCreditSaveState(credit.AwardId, credit.SkillId, credit.Metric, credit.CastId, credit.TargetLifeUid, credit.AmountMicro, credit.Tick, credit.SourceVersion)).ToArray())).ToArray(),
                mastery.TargetBudgets.Select(budget => new V25MasteryTargetBudgetSaveState(budget.TargetLifeUid, budget.EncounterId, budget.SpawnMaxHpMicro, budget.RemainingMicro, budget.RewardEligible, budget.EncounterType)).ToArray(),
                mastery.HealingDebts.Select(debt => new V25MasteryHealingDebtSaveState(debt.DebtId, debt.AmountMicro, debt.CreatedTick, debt.ExpireTick)).ToArray()) : null,
            _session.QuestsV25?.Snapshot() is { } quests ? new V25QuestSaveData(
                quests.Quests.Select(quest => new V25QuestSaveState(quest.QuestId, quest.Status, quest.ObjectiveProgress, quest.ObjectiveRequired, quest.HintShown, quest.RewardReceiptId)).ToArray(),
                quests.ObjectiveEventIds.ToArray(), quests.RewardReceipts.ToArray(), quests.HintedNpcIds.ToArray()) : null,
            _session.LootV25?.Snapshot() is { } loot ? new V25LootSaveData(loot.Awards.Select(award => new V25LootAwardSaveState(award.AwardId, award.TargetLifeUid, award.EncounterId, award.EncounterType, award.Rank, award.Coins, award.ItemDefinitionId, award.ItemRank, award.SourceVersion)).ToArray()) : null,
            _session.UniquePowersV25?.Snapshot() is { } uniques ? new V25UniquePowerSaveData(uniques.Powers.Select(power => new V25UniquePowerSaveState(power.PowerId, power.ReceiptId, power.UnlockedTick, power.HostBossId, power.PowerRank)).ToArray()) : null,
            _session.WorldLifecycleV25?.Snapshot() is { } world ? new V25WorldLifecycleSaveData(world.WorldCycleId, world.DefeatedEncounterIds, world.ClearedGroupIds, world.DiscoveredLandmarkIds, world.OpenedChestIds, _session.HazardTicks, _session.Monsters.DormantRegions, _session.Souls.PickupRegions, _session.VisitedTiles) : null);
        return V25SaveCodec.Normalize(new V25SaveEnvelope(V25SaveFormat.FormatId, V25SaveFormat.SchemaVersion, V25SaveFormat.SpecRevision, V25SaveFormat.ContentVersion, V25SaveFormat.BalanceVersion, sequence,
            $"tx_{saveId}_{sequence}_{_session.UidNext}", payload, "", saveId, runtime.SimulationTick, runtime.RngStreams));
    }

    public void RestoreCanonicalSave(V25SaveEnvelope save, bool upgradeToFull = false)
    {
        if (CanonicalContent is null) throw new InvalidOperationException("Canonical content is not enabled.");
        // Every restore operation runs against an isolated session. A late failure must not
        // clear live actors, consume UIDs, publish events, or change the current region.
        var targetProfile = upgradeToFull ? "full_01" : save.Payload.ProfileId;
        if (upgradeToFull && save.Payload.ProfileId is not ("beta_01" or "full_01")) throw new InvalidDataException("Unsupported profile upgrade.");
        var targetContent = CanonicalContent.WithProfile(targetProfile);
        var staged = new GameApplication(new GameSession(_session.Definitions, canonical: targetContent), targetContent);
        try
        {
            staged.Start();
            staged.RestoreCanonicalSaveInPlace(save);
        }
        catch
        {
            staged._defeatSubscription?.Dispose();
            staged._session.Dispose();
            throw;
        }
        var previous = _session;
        _defeatSubscription?.Dispose();
        staged._defeatSubscription?.Dispose();
        _session = staged._session;
        CanonicalContent = targetContent;
        _v25CommitSequence = staged._v25CommitSequence;
        _defeatedVisuals.Clear();
        SubscribeToSession();
        previous.Dispose();
    }

    private void RestoreCanonicalSaveInPlace(V25SaveEnvelope save)
    {
        if (CanonicalContent is null) throw new InvalidOperationException("Canonical content is not enabled.");
        ArgumentNullException.ThrowIfNull(save);
        V25SaveCodec.Validate(save, CanonicalContent);
        ValidateImplementedCanonicalPayload(save);
        var runtime = save.Payload.Runtime ?? throw new InvalidDataException("V2.5 save has no live runtime state; it is preserved and cannot be applied to this build.");
        if (runtime.SimulationTick != save.SimulationTick) throw new InvalidDataException("V2.5 envelope/runtime simulation ticks differ.");
        ValidateCanonicalRuntimeBeforeSwap(save, runtime);
        if (!ulong.TryParse(save.Payload.UidNext, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var uidNext) || uidNext > long.MaxValue)
            throw new InvalidDataException("V2.5 UID allocator value cannot be represented by the runtime.");
        var player = runtime.Actors.Single(actor => actor.Kind == V25EntityKind.Player);
        var casts = runtime.Casts.Select(cast => new V25CastView(cast.CastId, cast.CasterUid, cast.SkillId, cast.AcceptedTick, cast.ReleaseTick, cast.EndTick, cast.Phase,
            new Vec2(cast.AimX, cast.AimY), cast.TargetUid, cast.SourceKind,
            cast.GroundX is { } gx ? new Vec2(gx, cast.GroundY!.Value) : null, cast.Released, cast.ElapsedTicks)).ToArray();
        var projectiles = runtime.Projectiles.Select(projectile => new V25ProjectileView(projectile.CastId, projectile.CasterUid,
            new Vec2(projectile.PositionX, projectile.PositionY), V25FixedPoint.FromMilli(projectile.TravelledMilli), projectile.RemainingTicks, projectile.ReleasedTick, projectile.HitIndex,
            projectile.SkillId, projectile.SourceKind, new Vec2(projectile.DirectionX, projectile.DirectionY), projectile.SourceAttack, projectile.SourceDefense,
            projectile.SourceRank, projectile.SourceEncounterAttackFactor, projectile.SourceCombatStyleId)).ToArray();
        var cooldowns = runtime.Cooldowns.Select(item => new V25CooldownView(item.SourceUid, item.SkillId, item.RemainingTicks)).ToArray();
        var hitKeys = runtime.HitKeys.Select(item => new V25HitKeyView(item.CastId, item.TargetLifeUid, item.HitIndex)).ToArray();
        var densitySnapshot = BuildCanonicalDensitySnapshot(save.Payload);
        var worldSouls = (save.Payload.WorldSouls ?? Array.Empty<V25WorldSoulSaveState>()).Select(item =>
            new V25WorldSoulPickup(item.PickupId, item.MonsterUid, item.MonsterDefinitionId, item.SpeciesId, item.DisplayName,
                item.Rank, item.RankKey, item.RankDisplayName, item.SourceLevel, item.SourceRank,
                new Vec2(item.PositionX, item.PositionY), item.SoulNatureId, item.RewardEligible)).ToArray();
        var pity = (save.Payload.SoulPity ?? Array.Empty<V25SoulPitySaveState>()).Select(item => new V25SoulPityState(item.SpeciesId, item.Counter)).ToArray();
        if (!string.Equals(_session.WorldMap.CurrentRegionId, save.Payload.CurrentRegionId, StringComparison.Ordinal))
        {
            var travel = _session.TravelToRegion(save.Payload.CurrentRegionId);
            if (!travel.Success) throw new InvalidDataException($"V2.5 save region '{save.Payload.CurrentRegionId}' cannot be entered.");
        }
        _session.Combat.ClearCanonicalRuntime();
        _session.Summons.ClearActiveForMapChange();
        _session.Monsters.Clear(); _session.Allies.Clear();
        _session.Monsters.RestoreDormantRegions(save.Payload.WorldLifecycle?.DormantRegions, save.Payload.CurrentRegionId, runtime.SimulationTick);
        _session.Player.RestoreCanonicalRuntime(player.Uid, save.Payload.Player.Level, player.Rank, save.Payload.Player.Xp,
            V25FixedPoint.FromMilli(player.CurrentHpMilli), V25FixedPoint.FromMilli(player.MaxHpMilli), V25FixedPoint.FromMilli(player.CurrentSpiritMilli), V25FixedPoint.FromMilli(player.MaxSpiritMilli), player.CombatStyleId,
            V25FixedPoint.FromMilli(player.AttackCooldownMilli), player.BreakthroughReady, player.DodgeCooldownTicks, player.DodgeRemainingTicks, player.DodgeInvulnerabilityTicks,
            new Vec2(player.DodgeDirectionX, player.DodgeDirectionY), V25FixedPoint.FromMilli(player.DodgeDistanceRemainingMilli), V25FixedPoint.FromMilli(player.DodgeStepDistanceMilli), player.Statuses, player.Shields, runtime.SimulationTick,
            new Vec2(player.FacingX, player.FacingY));
        _session.Player.SetPosition(new Vec2(player.PositionX, player.PositionY));
        _session.Spirit?.RestoreRemainder(save.Payload.SpiritRemainderMicro);
        // Before world clocks were serialized, this carry used denominator 60; retain its fractional value.
        _session.Spirit?.RestoreRateRemainder(save.Payload.WorldLifecycle?.HazardTicks is null
            ? checked(save.Payload.SpiritRateRemainderMicro * 60) : save.Payload.SpiritRateRemainderMicro);
        _session.RestoreRespawnTicks(runtime.RespawnTicks ?? (player.Alive ? 0 : throw new InvalidDataException("Older dead-player save lacks its respawn timer; preserved without guessing.")));
        foreach (var actor in runtime.Actors.Where(actor => actor.Kind == V25EntityKind.Monster))
            _session.Monsters.RestoreCanonicalRuntime(actor.Uid, actor.DefinitionId, actor.SpeciesId, actor.Level, actor.Rank, actor.PowerTier, actor.Archetype, actor.CombatStyleId, actor.SignatureSkillId,
                actor.PositionX, actor.PositionY, V25FixedPoint.FromMilli(actor.CurrentHpMilli), V25FixedPoint.FromMilli(actor.MaxHpMilli), actor.Alive, actor.AiState,
                V25FixedPoint.FromMilli(actor.AttackCooldownMilli), actor.EncounterId, actor.EncounterType, actor.RewardEligible, actor.Statuses, actor.Shields, runtime.SimulationTick,
                actor.StaggerPoints, actor.StaggerImmuneTicks, actor.StaggerRecoveryTicks, actor.BossPatternIndex, actor.BossStoryInstanceId);
        foreach (var actor in runtime.Actors.Where(actor => actor.Kind == V25EntityKind.Ally))
        {
            _session.Allies.RestoreCanonicalRuntime(actor.Uid, actor.DefinitionId, actor.SpeciesId, actor.DisplayName!, actor.SourceSoulId!, actor.Level, actor.Rank, actor.PowerTier, actor.Archetype,
                actor.CombatStyleId, actor.SignatureSkillId, actor.PositionX, actor.PositionY, V25FixedPoint.FromMilli(actor.CurrentHpMilli), V25FixedPoint.FromMilli(actor.MaxHpMilli), actor.Alive, actor.AiState,
                V25FixedPoint.FromMilli(actor.AttackCooldownMilli), actor.TargetUid, V25FixedPoint.FromMicro(actor.VitalityMicro), actor.RecoveryTicks, actor.Statuses, actor.Shields, runtime.SimulationTick,
                Enum.Parse<AllyAiMode>(actor.AiMode, ignoreCase: false), actor.ThinkTicks, actor.FocusTargetUid, actor.FocusRemainingTicks, actor.PathFailTicks, actor.RecentAttackerUid, actor.RecentAttackerAgeTicks);
            _session.Summons.RestoreCanonicalActiveLink(actor.SourceSoulId!, actor.Uid);
        }
        var navigation = runtime.MonsterNavigation ?? throw new InvalidDataException("Save lacks monster home/target state; original preserved rather than guessing leash origins.");
        var actorIds = runtime.Actors.Select(actor => actor.Uid).ToHashSet(StringComparer.Ordinal);
        if (navigation.Any(state => state.TargetUid is not null && !actorIds.Contains(state.TargetUid)))
            throw new InvalidDataException("Monster target refers to an absent actor.");
        _session.Monsters.RestoreNavigation(navigation);
        _session.Player.SetColliders(_session.World.BlockingRects());
        _session.RandomStreams.Restore(save.RngStreams!);
        var knockbacks = (runtime.Knockbacks ?? Array.Empty<V25KnockbackSaveState>()).Select(item => new V25KnockbackView(item.TargetUid, new Vec2(item.DirectionX, item.DirectionY), V25FixedPoint.FromMilli(item.RemainingDistanceMilli), item.RemainingTicks)).ToArray();
        _session.Combat.RestoreCanonicalRuntime(casts, projectiles, cooldowns, hitKeys, knockbacks);
        _session.Combat.RestoreInputBuffer(runtime.BufferedSkillId, runtime.BufferedSkillTicks);
        _session.Souls.RestoreCanonicalState(densitySnapshot, worldSouls, pity,
            save.Payload.ConsumedPickupIds ?? Array.Empty<string>(), save.Payload.TutorialReceipts ?? Array.Empty<string>());
        var bannerReceipts = (save.Payload.BannerReceipts ?? Array.Empty<V25BannerGateSaveRecord>()).Select(receipt => new V25BannerGateReceipt(receipt.GateId, receipt.GateVersion, receipt.ReceiptId, receipt.FromRank, receipt.ToRank));
        _session.SoulBanners.RestoreCanonicalState(save.Payload.BannerRank, bannerReceipts);
        var summonRows = save.Payload.SummonStates ?? Array.Empty<V25SummonSaveState>();
        var summonBySpecies = summonRows.ToDictionary(item => item.SpeciesId, StringComparer.Ordinal);
        var summonSnapshots = save.Payload.OwnedSpecies.Select(owned =>
        {
            var mode = Enum.Parse<V25SoulRuntimeMode>(owned.Mode, ignoreCase: false);
            var detail = summonBySpecies.GetValueOrDefault(owned.SpeciesId);
            return new V25SummonStateSnapshot(owned.SpeciesId, mode, owned.ActiveAllyUid, owned.VitalityMicro,
                detail?.RecoveryTicks ?? 0, detail?.HpRatio ?? 1, detail?.AttackCooldown ?? 0);
        }).ToArray();
        _session.Summons.RestoreCanonicalState(summonSnapshots);
        _session.Progression.RestoreCanonicalRewardReceipts(save.Payload.Receipts.Where(receipt => receipt.Kind == "combatReward").Select(receipt => receipt.ReceiptId));
        var factRows = (save.Payload.Facts ?? Array.Empty<V25FactSaveRecord>()).Select(fact => new V25FactEvidence(fact.FactId, fact.ProducerId, fact.SourceId, fact.SourceVersion, fact.ProducedTick));
        _session.Progression.RestoreCanonicalFactState(factRows, save.Payload.Receipts.Where(receipt => receipt.Kind == "breakthrough").Select(receipt => receipt.ReceiptId));
        if (_session.Sync is not null)
        {
            var syncSnapshots = save.Payload.Sync.Count == 0
                ? _session.Sync.Snapshot()
                : save.Payload.Sync.Select(state => new V25SpeciesSyncSnapshot(
                    state.SpeciesId,
                    state.Awards.Sum(award => award.AwardedMicroPoints),
                    state.Awards.Select(award => new V25SyncAwardState(award.AwardId, award.SourceKey, award.SourceVersion, award.AwardedMicroPoints, award.CommitSequence)).ToArray(),
                    state.UnlockedMilestoneIds,
                    state.LegacyDedupKeys,
                    (state.Progress ?? Array.Empty<V25SyncSourceProgressSaveState>()).Select(progress => new V25SyncSourceProgressState(progress.SourceId, progress.Count, progress.EventIds)).ToArray())).ToArray();
            if (save.Payload.ProfileId != CanonicalContent.ActiveProfileId)
                syncSnapshots = syncSnapshots.Concat(_session.Sync.Snapshot().Where(row => !syncSnapshots.Any(saved => saved.SpeciesId == row.SpeciesId))).ToArray();
            _session.Sync.Restore(syncSnapshots);
        }
        var possession = save.Payload.Possession is { } savedPossession ? new V25PossessionSnapshot(savedPossession.SourceInstanceId, savedPossession.SoulId, savedPossession.SpeciesId,
            savedPossession.SoulLevel, savedPossession.SoulRank, savedPossession.SyncMicro, savedPossession.MilestoneIds, savedPossession.SoulBaseHp, savedPossession.SoulBaseAttack,
            savedPossession.SoulBaseDefense, savedPossession.SoulBaseMoveSpeed, savedPossession.TransferHp, savedPossession.TransferAttack, savedPossession.TransferDefense,
            savedPossession.TransferMoveSpeed, savedPossession.SignatureSkillId, savedPossession.CapabilityId, savedPossession.DurationSeconds, savedPossession.RemainingSeconds,
            savedPossession.CooldownSeconds, savedPossession.StartedTick) : null;
        _session.Possession.RestoreCanonical(possession, (save.Payload.PossessionCooldowns ?? Array.Empty<V25PossessionCooldownSaveState>()).Select(cooldown => new V25PossessionCooldownState(cooldown.SpeciesId, cooldown.RemainingSeconds)));
        if (_session.Traversal is { } traversalSystem)
        {
            if (save.Payload.Traversal is { } traversal)
            {
                var anchor = traversal.SafeAnchor is { } savedAnchor
                    ? new V25SafeAnchorSnapshot(savedAnchor.PositionX, savedAnchor.PositionY, savedAnchor.ConfirmedTick, savedAnchor.GateStateId, savedAnchor.SafeWalkmesh)
                    : null;
                var activeTerrain = traversal.ActiveTerrain is { } active ? (V25TerrainTag?)Enum.Parse<V25TerrainTag>(active, ignoreCase: false) : null;
                traversalSystem.Restore(new V25TraversalSnapshot(anchor, traversal.AnchorCandidateTicks, traversal.CandidatePositionX, traversal.CandidatePositionY,
                    traversal.CandidateGateStateId, traversal.FlightGraceTicks, traversal.BreathingGraceTicks, activeTerrain, traversal.ActiveWidthUnits,
                    traversal.ActiveGateStateId, traversal.ActiveStartedTick, traversal.CrumblingTicks), runtime.SimulationTick);
            }
            else traversalSystem.ResetForRegion();
        }
        if (_session.InventoryV25 is { } inventorySystem)
        {
            var savedInventory = save.Payload.Inventory ?? throw new InvalidDataException("V2.5 save lacks canonical inventory state; original preserved rather than resetting equipment.");
            inventorySystem.Restore(new V25InventorySnapshot(savedInventory.Coins,
                savedInventory.Items.Select(item => new V25ItemInstance(item.InstanceUid, item.DefinitionId, item.Count)).ToArray(),
                savedInventory.Overflow.Select(item => new V25ItemInstance(item.InstanceUid, item.DefinitionId, item.Count)).ToArray(),
                savedInventory.Equipped.Select(item => new V25EquippedItem(Enum.Parse<V25EquipmentSlot>(item.Slot, ignoreCase: false), item.InstanceUid, item.DefinitionId)).ToArray(),
                savedInventory.SharedPotionCooldownTicks, savedInventory.NextInstance));
        }
        if (_session.SkillGrantsV25 is { } skillSystem)
        {
            var savedSkills = save.Payload.SkillGrants ?? throw new InvalidDataException("V2.5 save lacks canonical skill grants; original preserved rather than resetting learned progress.");
            skillSystem.Restore(new V25SkillGrantSnapshot(savedSkills.LearnedSkillIds, savedSkills.ActiveSkillIds, savedSkills.PassiveSkillIds, savedSkills.PromotedRanks));
        }
        if (_session.MasteryV25 is { } masterySystem)
        {
            var savedMastery = save.Payload.Mastery ?? throw new InvalidDataException("V2.5 save lacks canonical mastery state; original preserved rather than resetting progression.");
            masterySystem.Restore(new V25MasterySnapshot(
                savedMastery.Skills.Select(skill => new V25MasterySkillState(skill.SkillId, skill.Metric, skill.PromotedRank, skill.ProgressMicro,
                    skill.Credits.Select(credit => new V25MasteryCredit(credit.AwardId, credit.SkillId, credit.Metric, credit.CastId, credit.TargetLifeUid, credit.AmountMicro, credit.Tick, credit.SourceVersion)).ToArray())).ToArray(),
                savedMastery.TargetBudgets.Select(budget => new V25MasteryTargetBudget(budget.TargetLifeUid, budget.EncounterId, budget.SpawnMaxHpMicro, budget.RemainingMicro, budget.RewardEligible, budget.EncounterType)).ToArray(),
                savedMastery.HealingDebts.Select(debt => new V25MasteryHealingDebt(debt.DebtId, debt.AmountMicro, debt.CreatedTick, debt.ExpireTick)).ToArray()));
            var grantRanks = _session.SkillGrantsV25!.Snapshot().PromotedRanks;
            foreach (var skill in masterySystem.Skills)
                if (grantRanks.GetValueOrDefault(skill.SkillId, Math.Max(1, CanonicalContent.Content.Skills.First(item => item.Id == skill.SkillId).BaseRank)) != skill.PromotedRank)
                    throw new InvalidDataException($"Mastery and grant rank disagree for '{skill.SkillId}'.");
        }
        if (_session.QuestsV25 is { } questSystem)
        {
            var savedQuests = save.Payload.Quests ?? throw new InvalidDataException("V2.5 save lacks canonical quest state; original preserved rather than resetting objectives.");
            questSystem.Restore(new V25QuestSnapshot(
                savedQuests.Quests.Select(quest => new V25QuestState(quest.QuestId, quest.Status, quest.ObjectiveProgress, quest.ObjectiveRequired, quest.HintShown, quest.RewardReceiptId))
                    .Concat(save.Payload.ProfileId != CanonicalContent.ActiveProfileId ? questSystem.Snapshot().Quests.Where(row => !savedQuests.Quests.Any(saved => saved.QuestId == row.QuestId)) : Array.Empty<V25QuestState>()).ToArray(),
                savedQuests.ObjectiveEventIds, savedQuests.RewardReceipts, savedQuests.HintedNpcIds));
        }
        if (_session.LootV25 is { } lootSystem)
        {
            var savedLoot = save.Payload.Loot ?? throw new InvalidDataException("V2.5 save lacks canonical loot ledger; original preserved rather than rerolling rewards.");
            lootSystem.Restore(new V25LootSnapshot(savedLoot.Awards.Select(award => new V25LootAward(award.AwardId, award.TargetLifeUid, award.EncounterId, award.EncounterType, award.Rank, award.Coins, award.ItemDefinitionId, award.ItemRank, award.SourceVersion)).ToArray()));
        }
        if (_session.UniquePowersV25 is { } uniqueSystem)
        {
            var savedUniques = save.Payload.UniquePowers ?? throw new InvalidDataException("V2.5 save lacks canonical unique power state; original preserved rather than resetting unlocks.");
            uniqueSystem.Restore(new V25UniquePowerSnapshot(savedUniques.Powers.Select(power => new V25UniquePowerState(power.PowerId, power.ReceiptId, power.UnlockedTick, power.HostBossId, power.PowerRank)).ToArray()));
        }
        if (_session.WorldLifecycleV25 is { } worldSystem)
        {
            var savedWorld = save.Payload.WorldLifecycle ?? throw new InvalidDataException("V2.5 save lacks canonical world lifecycle state; original preserved rather than respawning encounters.");
            worldSystem.Restore(new V25WorldLifecycleSnapshot(savedWorld.WorldCycleId, savedWorld.DefeatedEncounterIds, savedWorld.ClearedGroupIds, savedWorld.DiscoveredLandmarkIds, savedWorld.OpenedChestIds));
        }
        _session.Souls.RestorePickupRegions(save.Payload.WorldLifecycle?.PickupRegions);
        _session.RestoreVisitedTiles(save.Payload.WorldLifecycle?.VisitedTiles);
        _session.RestoreHazardTicks(save.Payload.WorldLifecycle?.HazardTicks);
        _session.Player.RecomputeStats();
        // Keep absolute resource amounts; intermediate modifier application may temporarily clamp them.
        // Historical balance versions may legitimately have different derived maxima.
        if (save.BalanceVersion == V25SaveFormat.BalanceVersion &&
            (V25FixedPoint.RoundMilli(_session.Player.State.MaxHp) != player.MaxHpMilli || V25FixedPoint.RoundMilli(_session.Player.State.MaxSpirit) != player.MaxSpiritMilli
                && !(save.Payload.WorldLifecycle?.HazardTicks is null && player.MaxSpiritMilli == V25FixedPoint.RoundMilli(V25PlayerSpirit(player.Level, player.Rank)))))
            throw new InvalidDataException("Saved maxima disagree with restored equipment, passives and possession.");
        _session.Player.State.CurrentHp = Math.Min(_session.Player.State.MaxHp, V25FixedPoint.FromMilli(player.CurrentHpMilli));
        _session.Player.State.CurrentSpirit = Math.Min(_session.Player.State.MaxSpirit, V25FixedPoint.FromMilli(player.CurrentSpiritMilli));
        _session.RestoreCanonicalClock(runtime.SimulationTick, checked((long)uidNext));
        _v25CommitSequence = Math.Max(_v25CommitSequence, save.CommitSequence);
    }
    public void RestoreSave(GameSaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);
        LegacySaveValidator.Validate(save, _session.Definitions);
        var level = save.Player.Level ?? 1; var rank = save.Player.Rank ?? 1;
        _session.Player.Restore(level, rank, save.Player.Xp ?? 0, save.Player.CurrentHp, save.Player.TitleDisplayName ?? save.Player.Title);
        _session.SoulBanners.Clear();
        var restoredSouls = new List<Simulation.State.OwnedSoulState>();
        foreach (var soul in save.Souls)
        {
            var monster = _session.Definitions.Monster(soul.Origin.ConfigId);
            var natureId = soul.SoulNatureId!;
            var rankKey = string.IsNullOrEmpty(soul.Origin.RankKey) ? soul.Origin.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) : soul.Origin.RankKey;
            var rankName = string.IsNullOrEmpty(soul.Origin.RankDisplayName) ? CombatPowerRules.RankDisplayName(soul.Origin.Rank) : soul.Origin.RankDisplayName;
            restoredSouls.Add(new Simulation.State.OwnedSoulState(soul.Id, natureId, new Simulation.State.SoulOrigin(soul.Origin.MonsterUid, soul.Origin.ConfigId, string.IsNullOrEmpty(soul.Origin.Species) ? monster.SpeciesId : soul.Origin.Species, soul.Origin.DisplayName, soul.Origin.Rank, rankKey, rankName), soul.Level, soul.Xp));
        }
        _session.Souls.RestoreOwned(restoredSouls);
        RestoreProgressionSave(save.Progression);
        _session.Essence.Restore(save.Essence?.Points); _session.Summons.RestoreDispersed(save.SoulRuntime?.Select(item => new DispersedSoulSaveData(item.SoulId, item.RecoverySeconds, item.RecoveryDurationSeconds))); _session.Bloodline.Restore(save.Bloodline?.Points);
        foreach (var savedBanner in save.SoulBanner)
        {
            if (!TryTier(savedBanner.Tier, out var tier)) throw new InvalidDataException($"Unsupported Soul Banner tier '{savedBanner.Tier}'.");
            var banner = _session.SoulBanners.Create(_session.Definitions.SoulBanner(tier), savedBanner.Level); _session.SoulBanners.RestoreBindings(banner.Id, savedBanner.BoundSouls);
        }
        if (_session.SoulBanners.Banners().Count == 0) _session.SoulBanners.CreateStarter();
        _session.Possession.Restore(save.Possession is null ? null : new PossessionSaveData(save.Possession.SoulId, save.Possession.ProfileId, save.Possession.RemainingSeconds));
        if (save.World?.CurrentRegionId is { } savedRegionId && !string.Equals(savedRegionId, _session.WorldMap.CurrentRegionId, StringComparison.Ordinal))
            _ = _session.TravelToRegion(savedRegionId);
        _session.World.Restore(save.World?.DestroyedObjectIds); _session.Player.SetColliders(_session.World.BlockingRects());
    }

    private static string TierId(SoulBannerTier tier) => tier switch { SoulBannerTier.NhapMon => "NHAP_MON", SoulBannerTier.LinhNgoc => "LINH_NGOC", SoulBannerTier.ThienLinh => "THIEN_LINH", _ => throw new ArgumentOutOfRangeException(nameof(tier)) };
    private static bool TryTier(string value, out SoulBannerTier tier) { tier = value switch { "NHAP_MON" => SoulBannerTier.NhapMon, "LINH_NGOC" => SoulBannerTier.LinhNgoc, "THIEN_LINH" => SoulBannerTier.ThienLinh, _ => default }; return value is "NHAP_MON" or "LINH_NGOC" or "THIEN_LINH"; }

    public ProgressionSaveData CaptureProgressionSave() => new(
        _session.Progression.InventorySnapshot().ToDictionary(item => item.StableId, item => item.Count, StringComparer.Ordinal),
        _session.Progression.PlayerBuffSnapshot().Select(buff => new TimedBuffSaveData(BalanceDefinition.Pills[buff.PillId].StableId, buff.RemainingSeconds)).ToArray());

    public void RestoreProgressionSave(ProgressionSaveData? save)
    {
        if (save is null)
        {
            _session.Progression.RestoreInventory(Array.Empty<InventoryItem>());
            _session.Progression.RestorePlayerBuffs(Array.Empty<TimedPlayerBuffRestore>());
            return;
        }
        _session.Progression.RestoreInventory(save.Inventory.Select(item => new InventoryItem(item.Key, item.Value)));
        var buffs = new List<TimedPlayerBuffRestore>();
        foreach (var saved in save.PlayerBuffs)
        {
            try { buffs.Add(new TimedPlayerBuffRestore(BalanceDefinition.PillByStableId(saved.PillId).Id, saved.RemainingSeconds)); }
            catch (KeyNotFoundException) { }
        }
        _session.Progression.RestorePlayerBuffs(buffs);
    }

    public GameSnapshot Snapshot() => new(
        _session.State.Stage,
        _session.ElapsedSeconds,
        _session.Definitions.Monsters.Count(),
        _session.Definitions.SoulBanners.Count(),
        _session.Definitions.SoulNatures.Natures.Count,
        _session.Definitions.SoulNatures.Capabilities.Count,
        new WorldSnapshot(_session.World.CurrentMap.Width, _session.World.CurrentMap.Height, _session.World.BlockingRects()),
        new PlayerSnapshot(_session.Player.State.Uid, _session.Player.State.Position, _session.Player.State.CurrentHp, _session.Player.State.MaxHp, _session.Player.State.Alive, _session.Player.State.Level, _session.Player.State.Xp, _session.Player.State.Rank, _session.Player.State.Stats.Atk, _session.Player.State.Stats.Def, _session.Player.State.Stats.Speed, _session.Player.State.CurrentSpirit, _session.Player.State.MaxSpirit, _session.Player.State.Shield, _session.Player.State.IsDodging, _session.Player.IsInvulnerable, _session.Player.State.BreakthroughReady),
        _session.Monsters.AliveMonsters().Select(monster => new MonsterSnapshot(monster.Uid, monster.DefinitionId, monster.SpeciesId, monster.Position, monster.CurrentHp, monster.MaxHp, monster.Alive, monster.AiState, monster.Level, monster.Rank, monster.Shield)).ToArray(),
        _session.Allies.AliveAllies().Select(ally => new AllySnapshot(ally.Uid, ally.DefinitionId, ally.SpeciesId, ally.DisplayName, ally.SourceSoulId, ally.Position, ally.CurrentHp, ally.MaxHp, ally.AiState, ally.Level, ally.Rank, ally.Shield, ally.Vitality)).ToArray(),
        _session.Progression.InventorySnapshot().Select(item => new InventoryItemSnapshot(item.StableId, item.Count)).ToArray(),
        _session.Souls.WorldSouls().Select(soul => new WorldSoulSnapshot(soul.Id, soul.SoulNatureId, soul.Position, soul.Origin.Rank, soul.Origin.SpeciesId)).ToArray(),
        _session.Souls.OwnedSouls().Select(soul => new OwnedSoulSnapshot(soul.Id, soul.SoulNatureId, _session.Definitions.SoulNatures.Natures[soul.SoulNatureId].DisplayName, soul.Level, soul.Xp, soul.Origin.Rank, soul.Origin.SpeciesId)).ToArray(),
        _session.SoulBanners.Banners().Select(banner => new SoulBannerSnapshot(banner.Id, banner.Tier, banner.Level, banner.BoundSoulIds.ToArray(), _session.SoulBanners.UsedCapacity(banner), banner.Computed.SlotLimit, banner.Computed.CapacityLimit, banner.Computed.ActiveLimit)).ToArray(),
        _session.WorldMap.CurrentRegionId);

    /// <summary>Typed live combat/timing state reserved for the V2.5 save writer; no legacy v6 wrapper is emitted.</summary>
    public V25RuntimeSnapshot CanonicalRuntimeSnapshot()
    {
        var actors = new List<V25ActorRuntimeSnapshot>
        {
            new(_session.Player.State.Uid, V25EntityKind.Player, "player", "player", _session.Player.State.Position,
                _session.Player.State.CurrentHp, _session.Player.State.MaxHp, _session.Player.State.Shield, _session.Player.State.Alive,
                _session.Player.State.Level, _session.Player.State.Rank, null, null, _session.Player.State.CombatStyleId,
                _session.Player.State.Statuses.Snapshot(_session.Player.State.Uid), _session.Player.State.Shields.Snapshot(_session.Player.State.Uid), _session.Player.State.DodgeCooldownTicks,
                _session.Player.State.DodgeRemainingTicks, _session.Player.State.DodgeInvulnerabilityTicks, 1, "Balanced",
                _session.Player.State.CurrentSpirit, _session.Player.State.MaxSpirit,
                DodgeDirection: _session.Player.State.DodgeDirection, DodgeDistanceRemaining: _session.Player.State.DodgeDistanceRemaining,
                DodgeStepDistance: _session.Player.State.DodgeStepDistance, AttackCooldown: _session.Player.State.AttackCooldown,
                AiState: _session.Player.State.Alive ? "Idle" : "Dead", BreakthroughReady: _session.Player.State.BreakthroughReady, Facing: _session.Player.State.Facing)
        };
        actors.AddRange(_session.Monsters.AllMonsters().Select(monster => new V25ActorRuntimeSnapshot(
            monster.Uid, V25EntityKind.Monster, monster.DefinitionId, monster.SpeciesId, monster.Position, monster.CurrentHp, monster.MaxHp,
            monster.Shield, monster.Alive, monster.Level, monster.Rank, null, monster.EncounterId, monster.CombatStyleId,
            monster.Statuses.Snapshot(monster.Uid), monster.Shields.Snapshot(monster.Uid), PowerTier: monster.PowerTier, Archetype: monster.Archetype,
            SignatureSkillId: monster.SignatureSkillId, EncounterType: monster.EncounterType, RewardEligible: monster.RewardEligible,
            AttackCooldown: monster.AttackCooldown, AiState: monster.AiState.ToString(), StaggerPoints: monster.StaggerPoints,
            StaggerImmuneTicks: monster.StaggerImmuneTicks, StaggerRecoveryTicks: monster.StaggerRecoveryTicks,
            BossPatternIndex: monster.BossPatternIndex, BossStoryInstanceId: monster.BossStoryInstanceId)));
        actors.AddRange(_session.Allies.AliveAllies().Select(ally => new V25ActorRuntimeSnapshot(
            ally.Uid, V25EntityKind.Ally, ally.DefinitionId, ally.SpeciesId, ally.Position, ally.CurrentHp, ally.MaxHp, ally.Shield,
            ally.Alive, ally.Level, ally.Rank, ally.TargetUid, null, ally.CombatStyleId, ally.Statuses.Snapshot(ally.Uid), ally.Shields.Snapshot(ally.Uid),
            PowerTier: ally.PowerTier, Archetype: ally.Archetype, Vitality: ally.Vitality, RecoveryTicks: ally.RecoveryTicks,
            SignatureSkillId: ally.SignatureSkillId, SourceSoulId: ally.SourceSoulId, DisplayName: ally.DisplayName,
            AttackCooldown: ally.AttackCooldown, AiState: ally.AiState.ToString(), AiMode: ally.AiMode,
            ThinkTicks: ally.ThinkTicks, FocusTargetUid: ally.FocusTargetUid, FocusRemainingTicks: ally.FocusRemainingTicks,
            PathFailTicks: ally.PathFailTicks, RecentAttackerUid: ally.RecentAttackerUid,
            RecentAttackerAgeTicks: ally.RecentAttackerTick < 0 ? 0 : checked((int)Math.Max(0, _session.SimulationTick - ally.RecentAttackerTick)))));
        return new(_session.SimulationTick, _session.Combat.ActiveCasts, _session.Combat.ActiveProjectiles, _session.RandomStreams.Snapshot(), actors, _session.Combat.Cooldowns.Where(cd => actors.Any(a => a.Uid == cd.SourceUid) || _session.Monsters.HasDormantActor(cd.SourceUid)).ToArray(), _session.Combat.HitKeys, _session.Combat.Knockbacks);
    }

    private V25RuntimeSaveState ToV25RuntimeSave(V25RuntimeSnapshot runtime) => new(
        1,
        runtime.SimulationTick,
        (runtime.Actors ?? Array.Empty<V25ActorRuntimeSnapshot>()).Select(actor => new V25RuntimeActorSaveState(
            actor.Uid, actor.Kind, actor.DefinitionId, actor.SpeciesId, actor.Position.X, actor.Position.Y,
            V25FixedPoint.RoundMilli(actor.CurrentHp), V25FixedPoint.RoundMilli(actor.MaximumHp), actor.Alive, actor.BreakthroughReady,
            actor.Level, actor.Rank, actor.PowerTier, actor.Archetype, actor.CombatStyleId, actor.TargetUid, actor.EncounterId, actor.SignatureSkillId,
            actor.AiState, V25FixedPoint.RoundMilli(actor.AttackCooldown), V25FixedPoint.RoundMilli(actor.CurrentSpirit), V25FixedPoint.RoundMilli(actor.MaximumSpirit),
            actor.DodgeCooldownTicks, actor.DodgeRemainingTicks, actor.DodgeInvulnerabilityTicks, actor.DodgeDirection.X, actor.DodgeDirection.Y,
            V25FixedPoint.RoundMilli(actor.DodgeDistanceRemaining), V25FixedPoint.RoundMilli(actor.DodgeStepDistance), V25FixedPoint.RoundMicro(actor.Vitality), actor.RecoveryTicks,
            actor.EncounterType, actor.RewardEligible, actor.SourceSoulId, actor.DisplayName, actor.Statuses, actor.Shields, actor.StaggerPoints,
            actor.StaggerImmuneTicks, actor.StaggerRecoveryTicks, actor.Facing.X, actor.Facing.Y,
            actor.AiMode.ToString(), actor.ThinkTicks, actor.FocusTargetUid, actor.FocusRemainingTicks, actor.PathFailTicks,
            actor.RecentAttackerUid, actor.RecentAttackerAgeTicks, actor.BossPatternIndex, actor.BossStoryInstanceId)).ToArray(),
        runtime.Casts.Select(cast => new V25CastSaveState(cast.CastId, cast.CasterUid, cast.SkillId, cast.AcceptedTick, cast.ReleaseTick, cast.EndTick, cast.Phase,
            cast.Aim.X, cast.Aim.Y, cast.TargetUid, cast.SourceKind, cast.GroundPoint?.X, cast.GroundPoint?.Y, cast.Released, cast.ElapsedTicks)).ToArray(),
        runtime.Projectiles.Select(projectile => new V25ProjectileSaveState(projectile.CastId, projectile.CasterUid, projectile.SkillId, projectile.SourceKind,
            projectile.Position.X, projectile.Position.Y, projectile.Direction.X, projectile.Direction.Y, V25FixedPoint.RoundMilli(projectile.Travelled), projectile.RemainingTicks,
            projectile.ReleasedTick, projectile.HitIndex, projectile.SourceAttack, projectile.SourceDefense, projectile.SourceRank,
            projectile.SourceEncounterAttackFactor, projectile.SourceCombatStyleId)).ToArray(),
        (runtime.Cooldowns ?? Array.Empty<V25CooldownView>()).ToArray().Select(item => new V25CooldownSaveState(item.SourceUid, item.SkillId, item.RemainingTicks)).ToArray(),
        (runtime.HitKeys ?? Array.Empty<V25HitKeyView>()).ToArray().Select(item => new V25HitKeySaveState(item.CastId, item.TargetLifeUid, item.HitIndex)).ToArray(),
        (runtime.Knockbacks ?? Array.Empty<V25KnockbackView>()).Select(item => new V25KnockbackSaveState(item.TargetUid, item.Direction.X, item.Direction.Y, V25FixedPoint.RoundMilli(item.RemainingDistance), item.RemainingTicks)).ToArray());

    private void ValidateCanonicalRuntimeBeforeSwap(V25SaveEnvelope save, V25RuntimeSaveState runtime)
    {
        var actors = runtime.Actors.ToDictionary(actor => actor.Uid, StringComparer.Ordinal);
        var player = runtime.Actors.Single(actor => actor.Kind == V25EntityKind.Player);
        var expectedPlayerRank = V25ProgressionRules.RankFromLevel(save.Payload.Player.Level);
        if (player.Uid != save.Payload.Player.Uid || player.DefinitionId != "player" || player.Level != save.Payload.Player.Level || player.Rank != expectedPlayerRank || player.CurrentHpMilli != save.Payload.Player.CurrentHpMilli || player.MaxHpMilli != save.Payload.Player.MaxHpMilli || player.CurrentSpiritMilli != save.Payload.Player.CurrentSpiritMilli || player.MaxSpiritMilli != save.Payload.Player.MaxSpiritMilli)
            throw new InvalidDataException("V2.5 player payload and live runtime actor disagree.");
        var playerStyle = CanonicalContent!.Content.CombatStyles.Any(style => style.Id == player.CombatStyleId);
        if (!playerStyle) throw new InvalidDataException($"Unknown canonical player combat style '{player.CombatStyleId}'.");
        var playerStats = V25CombatRules.ComputeStats(V25EntityKind.Player, 1, "Balanced", player.Rank, player.Level, balance: CanonicalContent.Balance);
        var possessionHpTransfer = save.Payload.Possession?.TransferHp ?? 0;
        // Derived maxima are checked after restoring equipment, passive and possession sources.
        if (player.MaxHpMilli <= 0 || player.MaxSpiritMilli <= 0) throw new InvalidDataException("Invalid player resource maxima.");
        if (player.Archetype != "Balanced" || new Vec2(player.DodgeDirectionX, player.DodgeDirectionY) == Vec2.Zero)
            throw new InvalidDataException("V2.5 player combat archetype or dodge direction is invalid.");
        foreach (var actor in runtime.Actors.Where(actor => actor.Kind == V25EntityKind.Monster))
        {
            var definition = _session.Definitions.Monster(actor.DefinitionId);
            var species = CanonicalContent.SpeciesForProfile(save.Payload.ProfileId).FirstOrDefault(item => item.Id == actor.SpeciesId) ?? throw new InvalidDataException($"Unknown canonical monster species '{actor.SpeciesId}'.");
            if (definition.SpeciesId.ToLowerInvariant() != actor.SpeciesId || species.PowerTier != actor.PowerTier || species.Archetype != actor.Archetype || species.CombatStyleId != actor.CombatStyleId || species.SignatureSkillId != actor.SignatureSkillId || string.IsNullOrWhiteSpace(actor.EncounterId) || !Enum.TryParse<MonsterAiState>(actor.AiState, ignoreCase: false, out var monsterAi) || actor.Alive != (actor.CurrentHpMilli > 0 && monsterAi != MonsterAiState.Dead))
                throw new InvalidDataException($"Monster actor '{actor.Uid}' definition/species/AI state mismatch.");
            var stats = V25CombatRules.ComputeStats(V25EntityKind.Monster, actor.PowerTier, actor.Archetype, actor.Rank, actor.Level, actor.EncounterType, CanonicalContent.Balance);
            if (V25FixedPoint.RoundMilli(stats.MaxHp) != actor.MaxHpMilli) throw new InvalidDataException($"Monster actor '{actor.Uid}' maximum HP mismatch.");
        }
        foreach (var actor in runtime.Actors.Where(actor => actor.Kind == V25EntityKind.Ally))
        {
            var definition = _session.Definitions.Monster(actor.DefinitionId);
            var species = CanonicalContent.SpeciesForProfile(save.Payload.ProfileId).FirstOrDefault(item => item.Id == actor.SpeciesId) ?? throw new InvalidDataException($"Unknown canonical ally species '{actor.SpeciesId}'.");
            if (definition.SpeciesId.ToLowerInvariant() != actor.SpeciesId || species.PowerTier != actor.PowerTier || species.Archetype != actor.Archetype || species.CombatStyleId != actor.CombatStyleId || species.SignatureSkillId != actor.SignatureSkillId || string.IsNullOrWhiteSpace(actor.SourceSoulId) || string.IsNullOrWhiteSpace(actor.DisplayName) || !Enum.TryParse<AllyAiState>(actor.AiState, ignoreCase: false, out var allyAi) || actor.Alive != (actor.CurrentHpMilli > 0 && allyAi != AllyAiState.Dead))
                throw new InvalidDataException($"Ally actor '{actor.Uid}' definition/species/AI/source state mismatch.");
            var stats = V25CombatRules.ComputeStats(V25EntityKind.Ally, actor.PowerTier, actor.Archetype, actor.Rank, actor.Level, balance: CanonicalContent.Balance);
            if (V25FixedPoint.RoundMilli(stats.MaxHp) != actor.MaxHpMilli) throw new InvalidDataException($"Ally actor '{actor.Uid}' maximum HP mismatch.");
        }
        foreach (var cast in runtime.Casts)
        {
            var source = actors[cast.CasterUid];
            var skill = CanonicalContent.Content.Skills.FirstOrDefault(item => item.Id == cast.SkillId) ?? throw new InvalidDataException($"Unknown canonical cast skill '{cast.SkillId}'.");
            var timing = skill.Id == "player_basic_attack"
                ? CanonicalContent.Content.CombatStyles.FirstOrDefault(item => item.Id == source.CombatStyleId) ?? CanonicalContent.Content.CombatStyles.First(item => item.Id == "fist")
                : null;
            var windup = V25CombatRules.MillisecondsToTicksCeil(timing?.WindupMs ?? skill.WindupMs);
            var active = V25CombatRules.MillisecondsToTicksCeil(timing?.ActiveMs ?? skill.ActiveMs);
            var recovery = V25CombatRules.MillisecondsToTicksCeil(timing?.RecoveryMs ?? skill.RecoveryMs);
            var expectedPhase = !cast.Released ? V25CastPhase.Windup : cast.ElapsedTicks < windup + active ? V25CastPhase.Active : V25CastPhase.Recovery;
            var timingMatches = cast.ReleaseTick == checked(cast.AcceptedTick + windup) && cast.EndTick == checked(cast.ReleaseTick + active + recovery);
            if (!timingMatches && source.Kind == V25EntityKind.Monster && source.EncounterType == V25EncounterType.Boss)
            {
                var recoveryScale = source.CurrentHpMilli <= source.MaxHpMilli / 2 ? 0.8 : 1.0;
                timingMatches = new[] { (700, 100, (int)Math.Round(800 * recoveryScale)), (1000, 100, (int)Math.Round(900 * recoveryScale)), (900, 100, (int)Math.Round(1000 * recoveryScale)) }
                    .Any(timing => cast.ReleaseTick == cast.AcceptedTick + V25CombatRules.MillisecondsToTicksCeil(timing.Item1) && cast.EndTick == cast.ReleaseTick + V25CombatRules.MillisecondsToTicksCeil(timing.Item2) + V25CombatRules.MillisecondsToTicksCeil(timing.Item3));
                if (timingMatches) { windup = checked((int)(cast.ReleaseTick - cast.AcceptedTick)); active = V25CombatRules.MillisecondsToTicksCeil(100); recovery = checked((int)(cast.EndTick - cast.ReleaseTick - active)); expectedPhase = !cast.Released ? V25CastPhase.Windup : cast.ElapsedTicks < windup + active ? V25CastPhase.Active : V25CastPhase.Recovery; }
            }
            if (!timingMatches || cast.ElapsedTicks >= windup + active + recovery || cast.Phase != expectedPhase || new Vec2(cast.AimX, cast.AimY) == Vec2.Zero || !cast.Released && cast.ElapsedTicks >= windup || cast.Released && cast.ElapsedTicks < windup)
                throw new InvalidDataException($"Canonical cast '{cast.CastId}' phase/timing does not match its authored skill.");
        }
        foreach (var projectile in runtime.Projectiles)
        {
            var skill = CanonicalContent.Content.Skills.FirstOrDefault(item => item.Id == projectile.SkillId) ?? throw new InvalidDataException($"Unknown canonical projectile skill '{projectile.SkillId}'.");
            if (!string.Equals(skill.Shape, "Projectile", StringComparison.OrdinalIgnoreCase) || projectile.RemainingTicks <= 0 || projectile.TravelledMilli >= V25FixedPoint.RoundMilli(skill.RangeUnits))
                throw new InvalidDataException($"Canonical projectile '{projectile.CastId}' is outside its authored lifetime.");
            if (!actors.TryGetValue(projectile.CasterUid, out var source)) continue;
            var expectedSource = V25CombatRules.ComputeStats(source.Kind, source.PowerTier, source.Archetype, source.Rank, source.Level, source.EncounterType, CanonicalContent.Balance);
            var expectedEncounterFactor = source.Kind == V25EntityKind.Monster ? V25EncounterFactors.For(source.EncounterType).Attack : 1;
            if (source.Kind != projectile.SourceKind || source.Rank != projectile.SourceRank || source.CombatStyleId != projectile.SourceCombatStyleId || source.Kind != V25EntityKind.Player && (Math.Abs(expectedSource.Attack - projectile.SourceAttack) > 0.001 || Math.Abs(expectedSource.Defense - projectile.SourceDefense) > 0.001) || Math.Abs(expectedEncounterFactor - projectile.SourceEncounterAttackFactor) > 0.001)
                throw new InvalidDataException($"Canonical projectile '{projectile.CastId}' offense snapshot disagrees with its live source.");
        }
        foreach (var knockback in runtime.Knockbacks ?? Array.Empty<V25KnockbackSaveState>())
            if (knockback.RemainingDistanceMilli > V25FixedPoint.RoundMilli(48)) throw new InvalidDataException($"Canonical knockback '{knockback.TargetUid}' exceeds its authored distance.");
        foreach (var actor in runtime.Actors)
        {
            if (actor.TargetUid is not null && !actors.ContainsKey(actor.TargetUid)) throw new InvalidDataException($"Actor '{actor.Uid}' target '{actor.TargetUid}' is missing.");
            if (actor.Kind == V25EntityKind.Player && actor.BreakthroughReady && actor.Level % 10 != 0) throw new InvalidDataException("Breakthrough can only be pending at a rank boundary.");
        }
        var allySources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ally in runtime.Actors.Where(actor => actor.Kind == V25EntityKind.Ally))
            if (ally.SourceSoulId is null || !allySources.Add(ally.SourceSoulId)) throw new InvalidDataException("V2.5 runtime contains duplicate or missing ally Soul source identity.");
    }

    private V25DensityEngineSnapshot BuildCanonicalDensitySnapshot(V25SaveDocument payload)
    {
        var initial = _session.Souls.CanonicalDensitySnapshot();
        var savedOwnership = payload.OwnedSpecies.ToDictionary(item => item.SpeciesId, StringComparer.Ordinal);
        var ownership = initial.Ownership.Select(state =>
        {
            if (!savedOwnership.TryGetValue(state.SpeciesId, out var saved)) return state;
            if (saved.SyncMicro != 0)
                throw new InvalidDataException($"V2.5 Soul state for '{saved.SpeciesId}' contains Sync data outside its canonical ledger.");
            return new V25SpeciesOwnershipSnapshot(saved.SpeciesId, saved.PowerTier, V25SpeciesOwnershipStatus.Owned,
                saved.CommittedDensity, saved.PendingDensity, saved.PassedGateIndex, saved.ProofKeys ?? Array.Empty<int>());
        }).ToArray();
        var ledger = payload.DensityAwards.Select(award => new V25DensityLedgerEntry(
            award.TransactionId, award.AwardId, award.SpeciesId, award.SourceId, award.SourceVersion,
            award.GrantedMicro, award.AppliedCommitted, award.AddedPending, award.Discarded,
            award.ProofKeys.ToArray(), award.ResultCommittedDensityMicro, award.ResultPendingDensityMicro,
            award.ResultPassedGateIndex, award.SourceLevel, award.PreTransactionSoulRank)).ToArray();
        return new V25DensityEngineSnapshot(ownership, ledger);
    }

    private static void ValidateImplementedCanonicalPayload(V25SaveEnvelope save)
    {
        var payload = save.Payload;
        if (payload.ProfileId is not ("beta_01" or "full_01")) throw new InvalidDataException("Unsupported runtime profile.");
        if (payload.OwnedSpecies.Any(species => species.SyncMicro != 0))
            throw new InvalidDataException("V2.5 save contains Sync state in an ownership row; the original must be preserved.");
        var syncAwardIds = payload.Sync.SelectMany(sync => sync.Awards.Select(award => award.AwardId)).ToHashSet(StringComparer.Ordinal);
        var syncReceiptIds = payload.Receipts.Where(receipt => receipt.Kind == "sync").Select(receipt => receipt.ReceiptId).ToHashSet(StringComparer.Ordinal);
        if (!syncAwardIds.SetEquals(syncReceiptIds))
            throw new InvalidDataException("V2.5 Sync awards and durable receipts disagree; the original must be preserved.");
        if (payload.Receipts.Any(receipt => receipt.Kind is not ("combatReward" or "breakthrough" or "sync")))
            throw new InvalidDataException("V2.5 save contains a receipt kind outside the implemented combat reward ledger; the original must be preserved.");
        if (payload.Player.RegionId != payload.CurrentRegionId)
            throw new InvalidDataException("V2.5 player region and save region disagree.");
    }

    private double V25PlayerSpirit(int level, int rank) => System.Math.Round((120 + 12 * (level - 1)) * (1 + 0.08 * (rank - 1)), MidpointRounding.AwayFromZero);

    private WorldMapRegionSnapshot BuildRegionSnapshot(RegionDefinition region)
    {
        var preview = CanonicalContent is null ? _session.WorldMap.PreviewTravel(region.Id) : _session.PreviewCanonicalRegion(region.Id);
        return new WorldMapRegionSnapshot(
            region.Id,
            region.DisplayName,
            region.ShortDescription,
            region.Story,
            region.MapContentId,
            region.ScenePath,
            region.WorldMapPosition,
            region.WorldMapRadius,
            region.Biome,
            region.StarterCandidate,
            string.Equals(region.Id, _session.WorldMap.CurrentRegionId, StringComparison.Ordinal),
            region.Available,
            CanonicalContent is not null || preview.Success,
            preview.Success,
            region.RecommendedLevelRange?.Minimum,
            region.RecommendedLevelRange?.Maximum,
            region.TravelConditionIds,
            region.Tags);
    }
}
