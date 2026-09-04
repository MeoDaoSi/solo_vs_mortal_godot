using SoloVsMortal.Simulation;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Core.Math;
using SoloVsMortal.Application.Persistence;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.Events;

namespace SoloVsMortal.Application;

/// <summary>Command/query boundary exposed to Presentation.</summary>
public sealed class GameApplication
{
    private readonly GameSession _session;
    private readonly Queue<DefeatedMonsterVisualSnapshot> _defeatedVisuals = new();

    public GameApplication(GameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _session.Events.Subscribe<MonsterDefeatedEvent>(defeated =>
        {
            var action = _session.Definitions.CharacterAnimations.MonsterActions["death"];
            _defeatedVisuals.Enqueue(new(defeated.Uid, defeated.SpeciesId, defeated.Rank, defeated.Position, action.FrameCount / action.FrameRate));
        });
    }

    public static GameApplication CreateFromDefinitionsDirectory(string directory) =>
        new(new GameSession(GameDefinitionLoader.LoadFromDirectory(directory)));

    public void Start() => _session.Start();

    public void Tick(double deltaSeconds) => _session.Tick(deltaSeconds);
    public void SetInput(Vec2 move, bool attackPressed) => _session.SetInput(move, attackPressed);
    public string SpawnMonster(string definitionId, int? level = null, Vec2? position = null) => _session.SpawnMonster(definitionId, level, position).Uid;
    public void AddPlayerXp(double amount) => _session.Progression.AddPlayerXp(amount);
    public bool AttemptPlayerBreakthrough(PillId? pillId = null) => _session.Progression.AttemptPlayerBreakthrough(pillId);
    public bool Craft(PillId pillId, int amount = 1) => _session.Progression.Craft(pillId, amount);
    public bool UsePlayerStatPill(PillId pillId) => _session.Progression.UsePlayerStatPill(pillId);
    public IReadOnlyList<string> AcquireNearbySouls() => _session.Souls.AcquireNear(_session.Player.State.Position, _session.Definitions.Soul.PickupRadius).Select(soul => soul.Id).ToArray();
    public BindSoulResult BindSoul(string soulId, string bannerId) => _session.SoulBanners.Bind(soulId, bannerId);
    public UnbindSoulResult UnbindSoul(string soulId, string bannerId) => _session.SoulBanners.Unbind(soulId, bannerId);
    public int AddEssence(string profileId, double amount) => _session.Essence.Add(profileId, amount);
    public int AddBloodline(string profileId, double amount) => _session.Bloodline.Add(profileId, amount);
    public StartPossessionResult StartPossession(string soulId) => _session.Possession.Start(soulId);
    public bool EndPossession() => _session.Possession.End();
    public WorldInteractionResult Interact() => _session.World.TryInteract(_session.Player.State.Position);
    public IReadOnlyList<DevourPreview> DevourPreviews(string soulId) => _session.Devouring.Previews(soulId);
    public DevourResult DevourSoul(string soulId, DevourMode mode) => _session.Devouring.Execute(soulId, mode);
    public SummonResult SummonSoul(string soulId, string bannerId, Vec2 position) => _session.Summons.Summon(soulId, bannerId, position);
    public bool UnsummonSoul(string soulId) => _session.Summons.UnsummonSoul(soulId);
    public SoulRuntimeView SoulRuntime(string soulId) => _session.Summons.Runtime(soulId);
    public string? ActivePossessionSoulId => _session.Possession.ActiveSoulId;
    public double PossessionRemainingSeconds() => _session.Possession.RemainingSeconds;
    public IReadOnlyList<InventoryItem> Inventory() => _session.Progression.InventorySnapshot();
    public IReadOnlyList<WorldObjectSnapshot> WorldObjects() => _session.Definitions.DefaultMap.Objects.Values.Select(item => new WorldObjectSnapshot(item.Id, item.Type, item.AssetId, item.Position, item.Blocking, _session.World.IsDestroyed(item.Id))).ToArray();
    public AssetSnapshot Asset(string logicalId) { var asset = _session.Definitions.Assets.Get(logicalId); return new(asset.Id, asset.File, asset.FrameWidth, asset.FrameHeight); }
    public AnimationClipSnapshot MonsterAnimation(string speciesId, int rank, string actionId)
    {
        var descriptor = _session.Definitions.CharacterAnimations.MonstersBySpecies[speciesId]; var action = _session.Definitions.CharacterAnimations.MonsterActions[actionId];
        var form = descriptor.Forms[SpriteStageRules.GetSpriteStage(speciesId, rank) - 1];
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
        new(_session.World.DestroyedObjectIds()));

    public string CaptureSaveJson() => GameSaveCodec.Serialize(CaptureSave());
    public void RestoreSaveJson(string json) => RestoreSave(GameSaveCodec.Deserialize(json));
    public void RestoreSave(GameSaveData save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var level = save.Player.Level ?? 1; var rank = save.Player.Rank ?? 1;
        _session.Player.Restore(level, rank, save.Player.Xp ?? 0, save.Player.CurrentHp, save.Player.TitleDisplayName ?? save.Player.Title);
        _session.SoulBanners.Clear();
        var restoredSouls = new List<Simulation.State.OwnedSoulState>();
        foreach (var soul in save.Souls)
        {
            MonsterDefinition monster; try { monster = _session.Definitions.Monster(soul.Origin.ConfigId); } catch (KeyNotFoundException) { continue; }
            var natureId = soul.SoulNatureId ?? monster.SoulNatureId; if (!_session.Definitions.SoulNatures.Natures.ContainsKey(natureId)) continue;
            var rankKey = string.IsNullOrEmpty(soul.Origin.RankKey) ? soul.Origin.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture) : soul.Origin.RankKey;
            var rankName = string.IsNullOrEmpty(soul.Origin.RankDisplayName) ? CombatPowerRules.RankDisplayName(soul.Origin.Rank) : soul.Origin.RankDisplayName;
            restoredSouls.Add(new Simulation.State.OwnedSoulState(soul.Id, natureId, new Simulation.State.SoulOrigin(soul.Origin.MonsterUid, soul.Origin.ConfigId, string.IsNullOrEmpty(soul.Origin.Species) ? monster.SpeciesId : soul.Origin.Species, soul.Origin.DisplayName, soul.Origin.Rank, rankKey, rankName), soul.Level, soul.Xp));
        }
        _session.Souls.RestoreOwned(restoredSouls);
        RestoreProgressionSave(save.Progression);
        _session.Essence.Restore(save.Essence?.Points); _session.Summons.RestoreDispersed(save.SoulRuntime?.Select(item => new DispersedSoulSaveData(item.SoulId, item.RecoverySeconds, item.RecoveryDurationSeconds))); _session.Bloodline.Restore(save.Bloodline?.Points);
        foreach (var savedBanner in save.SoulBanner)
        {
            if (!TryTier(savedBanner.Tier, out var tier)) continue;
            var banner = _session.SoulBanners.Create(_session.Definitions.SoulBanner(tier), savedBanner.Level); _session.SoulBanners.RestoreBindings(banner.Id, savedBanner.BoundSouls);
        }
        if (_session.SoulBanners.Banners().Count == 0) _session.SoulBanners.CreateStarter();
        _session.Possession.Restore(save.Possession is null ? null : new PossessionSaveData(save.Possession.SoulId, save.Possession.ProfileId, save.Possession.RemainingSeconds));
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
        new WorldSnapshot(_session.Definitions.DefaultMap.Width, _session.Definitions.DefaultMap.Height, _session.World.BlockingRects()),
        new PlayerSnapshot(_session.Player.State.Uid, _session.Player.State.Position, _session.Player.State.CurrentHp, _session.Player.State.MaxHp, _session.Player.State.Alive, _session.Player.State.Level, _session.Player.State.Xp, _session.Player.State.Rank, _session.Player.State.Stats.Atk, _session.Player.State.Stats.Def, _session.Player.State.Stats.Speed),
        _session.Monsters.AliveMonsters().Select(monster => new MonsterSnapshot(monster.Uid, monster.DefinitionId, monster.SpeciesId, monster.Position, monster.CurrentHp, monster.MaxHp, monster.Alive, monster.AiState, monster.Level, monster.Rank)).ToArray(),
        _session.Allies.AliveAllies().Select(ally => new AllySnapshot(ally.Uid, ally.DefinitionId, ally.SpeciesId, ally.DisplayName, ally.SourceSoulId, ally.Position, ally.CurrentHp, ally.MaxHp, ally.AiState, ally.Level, ally.Rank)).ToArray(),
        _session.Progression.InventorySnapshot().Select(item => new InventoryItemSnapshot(item.StableId, item.Count)).ToArray(),
        _session.Souls.WorldSouls().Select(soul => new WorldSoulSnapshot(soul.Id, soul.SoulNatureId, soul.Position, soul.Origin.Rank, soul.Origin.SpeciesId)).ToArray(),
        _session.Souls.OwnedSouls().Select(soul => new OwnedSoulSnapshot(soul.Id, soul.SoulNatureId, _session.Definitions.SoulNatures.Natures[soul.SoulNatureId].DisplayName, soul.Level, soul.Xp, soul.Origin.Rank, soul.Origin.SpeciesId)).ToArray(),
        _session.SoulBanners.Banners().Select(banner => new SoulBannerSnapshot(banner.Id, banner.Tier, banner.Level, banner.BoundSoulIds.ToArray(), _session.SoulBanners.UsedCapacity(banner), banner.Computed.SlotLimit, banner.Computed.CapacityLimit, banner.Computed.ActiveLimit)).ToArray());
}
