using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.Systems.V25;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Application.Persistence.V25;
using SimVec2 = SoloVsMortal.Core.Math.Vec2;

namespace SoloVsMortal.Presentation;

/// <summary>Thin Godot adapter: input in, snapshots out. Simulation remains the gameplay owner.</summary>
public partial class Arena : Node2D
{
    // Locked presentation grid for the Pixel Rendering Foundation. World layout
    // and collision remain simulation-owned; this only guards trial tile drawing.
    private const int BaseTileSize = 32;
    private const string SavePath = "user://solo_vs_mortal_save_v25.json";
    private const string LegacySavePath = "user://solo_vs_mortal_save_v6.json";
    private static readonly bool ShowMapCollisionDebug = false;
    // Opt-in developer diagnostics only. Normal gameplay never renders IDs,
    // asset keys, or integration/missing text over the world.
    private static readonly bool ShowPresentationDebug = false;
    private GameApplication _application = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _currency = null!;
    private Label _combatSoulState = null!;
    private Label _toast = null!;
    private VBoxContainer _soulList = null!;
    private VBoxContainer _featureList = null!;
    private TabContainer _screens = null!;
    private PanelContainer _detailsOverlay = null!;
    private WorldMapUI _worldMap = null!;
    private HudMinimap _minimap = null!;
    private double _autosaveRemaining = 10;
    private double _toastRemaining;
    private bool _saveWritesBlocked;
    // A V6 save cannot be restored into the hash-pinned V2.5 payload without a
    // lossless migration.  Keep it untouched, but do not let its presence make
    // a fresh V2.5 session unplayable.
    private bool _legacySavePreserved;
    private V25SaveStore? _v25Store;
    private int _selectedSaveSlot = 1;
    private V25SaveEnvelope? _pendingSave;
    private bool _saveCommitFailed;
    private bool _suspended;
    private bool GameplayCommandsBlocked => _suspended || _saveCommitFailed || _saveWritesBlocked;
    private GameSnapshot? _snapshot;
    private CanonicalAssetCatalog _assetCatalog = null!;
    private Texture2D? _ground;
    private AnimatedSprite2D _playerSprite = null!;
    private readonly Dictionary<string, AnimatedSprite2D> _monsterSprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AnimatedSprite2D> _allySprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _mapTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _trialTileTextures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _dyingMonsters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector2> _actorVisualPositions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _actorVisualDirections = new(StringComparer.Ordinal);
    private string _facing = "front";
    private string _lastSoulSignature = "";
    private string _lastFeatureSignature = "";
    private string _lastScreenSignature = "";
    private int _visualRank;
    private BossTelegraphLayer _bossTelegraphs = null!;
    private string? _activeRitualPowerId;
    private string? _activeSyncRitualSpeciesId;

    private string SelectedSaveId => _selectedSaveSlot == 1 ? "slot.primary" : $"slot.{_selectedSaveSlot}";

    public override void _Ready()
    {
#if DEBUG
        // Technical startup proof for the pixel-foundation configuration. This is
        // intentionally log-only, never part of the normal gameplay HUD.
        GD.Print($"PIXEL_RENDERING_FOUNDATION viewport={GetViewport().GetVisibleRect().Size}; window={DisplayServer.WindowGetSize()}; baseTile={BaseTileSize}");
#endif
        _camera = GetNode<Camera2D>("Camera2D"); _status = GetNode<Label>("Hud/HudRoot/TopMargin/TopRow/StatusPanel/StatusMargin/Status"); _currency = GetNode<Label>("Hud/HudRoot/TopMargin/TopRow/CurrencyPanel/CurrencyMargin/Currency"); _combatSoulState = GetNode<Label>("Hud/HudRoot/BottomMargin/BottomRow/CombatSoulState"); _toast = GetNode<Label>("Hud/HudRoot/Toast");
        _soulList = GetNode<VBoxContainer>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Screens/Souls/List"); _featureList = GetNode<VBoxContainer>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Screens/Actions/Content"); _screens = GetNode<TabContainer>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Screens"); _detailsOverlay = GetNode<PanelContainer>("Hud/HudRoot/DetailsOverlay");
        GetNode<Button>("Hud/HudRoot/BottomMargin/BottomRow/DetailsToggle").Pressed += ToggleDetails;
        GetNode<Button>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Header/CloseDetails").Pressed += ToggleDetails;
        ApplyHudVisualDesign();
        _application = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs"));
        _assetCatalog = CanonicalAssetCatalog.Load(ProjectSettings.GlobalizePath("res://"), ProjectSettings.GlobalizePath("res://data/v2.5/asset-catalog.v2.5.json"));
        _v25Store = CreateSaveStore(_selectedSaveSlot);
        _application.Start();
        _worldMap = new WorldMapUI(); AddChild(_worldMap); _worldMap.Initialize(_application, TravelToRegion);
        _minimap = new HudMinimap(); _minimap.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); GetNode<Control>("Hud/HudRoot/TopMargin/TopRow/MinimapSlot").AddChild(_minimap);
        _ground = LoadCanonicalAssetTexture(_application.CurrentMapBackgroundAssetId() ?? "tiles.arena.ground"); RebuildMapTextures();
        _visualRank = 1; _playerSprite = BuildPlayerSprite(_visualRank); AddChild(_playerSprite);
        _bossTelegraphs = new BossTelegraphLayer { ZIndex = 15 }; AddChild(_bossTelegraphs);
        var restoredSave = TryLoad(showMessage: false);
        if (!restoredSave && !_saveWritesBlocked && _application.Snapshot().Monsters.Count == 0) _application.SpawnMonster("mon_skeleton", 1, new SimVec2(650, 280));
#if DEBUG
        if (_legacySavePreserved)
            GD.Print("MOVEMENT_RECOVERY legacy_v6_preserved=true; fresh_v25_session=true; gameplay_input_blocked=false");
#endif
        RefreshSnapshot();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_suspended)
        {
            _application.SetInput(SimVec2.Zero, false, SimVec2.Zero, false);
            if (Input.IsActionJustPressed("suspend_game")) { _suspended = false; Toast("Đã tiếp tục."); }
            return;
        }
        if (_saveCommitFailed || _saveWritesBlocked)
        {
            _application.SetInput(SimVec2.Zero, false, SimVec2.Zero, false);
            if (Input.IsActionJustPressed("save_game") && !_saveWritesBlocked) Save(showMessage: true);
            if (Input.IsActionJustPressed("load_game") && _pendingSave is null) TryLoad(showMessage: true);
            return;
        }
        if (_worldMap.IsOpen)
        {
            _application.SetInput(SimVec2.Zero, false, SimVec2.Zero, false);
            return;
        }
        for (var slot = 1; slot <= 3; slot++) if (Input.IsActionJustPressed($"save_slot_{slot}")) SelectSaveSlot(slot);
        if (Input.IsActionJustPressed("suspend_game")) { Suspend(); return; }
        var move = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        var mouse = GetGlobalMousePosition();
        if (Input.IsActionJustPressed("main_hand_skill")) _application.RequestCanonicalMainHandSkill();
        if (Input.IsActionJustPressed("offhand_skill")) _application.RequestCanonicalOffHandSkill();
        if (Input.IsActionJustPressed("possession_skill")) _application.RequestCanonicalPossessionSkill();
        if (Input.IsActionJustPressed("unique_skill")) _application.RequestCanonicalUniqueSkill();
        for (var slot = 0; slot < 6; slot++) if (Input.IsActionJustPressed($"learned_skill_{slot + 1}")) _application.RequestCanonicalLearnedSkill(slot);
        if (_application.CanonicalContent is not null && Input.IsActionJustPressed("summon_all"))
        {
            var results = _application.SummonAllSouls().Results;
            var summoned = results.Count(result => result.Value.Success);
            var durable = summoned == 0 || Save(showMessage: false);
            Toast(summoned == 0 ? "Không có Soul sẵn sàng để Triệu hồi toàn bộ." : durable ? $"Đã triệu hồi {summoned}/{results.Count} Soul." : "Triệu hồi đang chờ lưu bền vững.");
            RefreshSnapshot();
        }
        if (_application.CanonicalContent is not null && Input.IsActionJustPressed("recall_all"))
        {
            var recalled = _application.RecallAllSouls();
            var durable = recalled == 0 || Save(showMessage: false);
            Toast(recalled == 0 ? "Không có Ally để Thu hồi." : durable ? $"Đã thu hồi {recalled} Ally." : "Thu hồi đang chờ lưu bền vững.");
            RefreshSnapshot();
        }
        if (_application.CanonicalContent is not null && Input.IsActionJustPressed("focus_allies"))
        {
            var focused = _application.FocusAlliesAt(new SimVec2(mouse.X, mouse.Y));
            var durable = focused == 0 || Save(showMessage: false);
            Toast(focused == 0 ? "Không có mục tiêu Focus hợp lệ dưới con trỏ." : durable ? $"Focus: {focused} Ally." : "Focus đang chờ lưu bền vững.");
            RefreshSnapshot();
        }
        if (_application.CanonicalContent is not null && Input.IsActionJustPressed("toggle_ally_mode"))
        {
            var changed = _application.ToggleCanonicalAllyMode();
            var durable = !changed || Save(showMessage: false);
            Toast(changed && durable ? "Đã đổi Guard/Assault và regroup đội hình." : changed ? "Đổi mode đang chờ lưu bền vững." : "Không có Ally để đổi mode.");
            RefreshSnapshot();
        }
        _application.SetInput(new SimVec2(move.X, move.Y), Input.IsActionPressed("attack"), new SimVec2(mouse.X, mouse.Y), Input.IsActionJustPressed("dodge"));
        _application.Tick(delta);
        if (_activeSyncRitualSpeciesId is not null)
        {
            var ritual = _application.AdvanceCanonicalSyncRitual(Input.IsActionPressed("acquire_soul"));
            if (ritual.Completed)
            {
                _activeSyncRitualSpeciesId = null;
                var durable = Save(showMessage: false);
                Toast(durable ? "Nghi thức Sync hoàn tất." : "Nghi thức Sync hoàn tất nhưng đang chờ lưu bền vững.");
                RefreshSnapshot();
            }
            else if (!ritual.Started)
            {
                _activeSyncRitualSpeciesId = null;
                Toast(ritual.Failure switch { "Released" => "Nghi thức Sync đã hủy vì nhả E.", "MovedOrLeftShrine" => "Nghi thức Sync đã hủy vì di chuyển hoặc rời Shrine.", "Canceled" => "Nghi thức Sync đã hủy vì nhận sát thương.", _ => "Nghi thức Sync đã hủy vì điều kiện thay đổi." });
            }
        }
        else if (_activeRitualPowerId is not null)
        {
            var ritual = _application.AdvanceCanonicalUniqueRitual(Input.IsActionPressed("acquire_soul"));
            if (ritual.Completed)
            {
                _activeRitualPowerId = null;
                var durable = Save(showMessage: false);
                Toast(durable ? "Nghi thức hoàn tất; Unique Power đã được nhận." : "Nghi thức hoàn tất nhưng đang chờ lưu bền vững.");
                RefreshSnapshot();
            }
            else if (!ritual.Started)
            {
                _activeRitualPowerId = null;
                Toast(ritual.Failure switch { "Released" => "Nghi thức đã hủy vì nhả E.", "MovedOrLeftShrine" => "Nghi thức đã hủy vì di chuyển hoặc rời Shrine.", "Canceled" => "Nghi thức đã hủy vì nhận sát thương.", _ => "Nghi thức đã hủy vì điều kiện thay đổi." });
            }
        }
        // Auto-collect is a gameplay transaction too. Do not let it wait for the ten-second
        // autosave window: commit its exact post-capture payload before any reward event/UI.
        if (_application.HasCanonicalDurableChanges && !Save(showMessage: false)) return;
        BeginMonsterDeaths(); UpdateMonsterDeaths(delta);
        if (Input.IsActionJustPressed("acquire_soul"))
        {
            // Locked input order: quest → pickup → shrine → NPC, then the remaining nearby
            // world object. Each branch owns its mutation and commits before acknowledgement.
            var questNpc = _application.NearbyCanonicalNpcId(requirePendingQuest: true);
            if (questNpc is not null)
            {
                var result = _application.InteractCanonicalNpc(questNpc);
                var durable = !(result.Success || result.HintShown) || Save(showMessage: false);
                Toast(durable ? result.Message ?? "Đã cập nhật nhiệm vụ." : "Tương tác nhiệm vụ đang chờ lưu bền vững.");
                RefreshSnapshot();
            }
            else if (_application.HasNearbyCanonicalPickup())
            {
                var count = _application.AcquireNearbySouls().Count;
                var durable = count == 0 || _application.CanonicalContent is null || Save(showMessage: false);
                Toast(count == 0 ? "Không có Hồn ở gần." : durable ? $"Đã thu {count} Hồn." : "Thu Hồn đang chờ lưu bền vững; hãy thử lưu lại.");
            }
            else if (_application.IsAtCanonicalShrine())
            {
                var syncRitual = _application.BeginCanonicalSyncRitual();
                if (syncRitual.Started)
                {
                    _activeSyncRitualSpeciesId = syncRitual.SpeciesId;
                    Toast($"Đang thực hiện nghi thức Sync {syncRitual.SpeciesId}: giữ E trong {Math.Ceiling(syncRitual.RemainingTicks / 60.0)} giây.");
                }
                else
                {
                    var ritual = _application.BeginCanonicalUniqueRitual();
                    if (ritual.Started)
                    {
                        _activeRitualPowerId = ritual.PowerId;
                        Toast($"Đang thực hiện nghi thức {ritual.PowerId}: giữ E trong {Math.Ceiling(ritual.RemainingTicks / 60.0)} giây.");
                    }
                    else Toast("Shrine: dùng Nghỉ, nhận thưởng hoặc nghi thức khi đủ điều kiện.");
                }
            }
            else
            {
                var npcId = _application.NearbyCanonicalNpcId(requirePendingQuest: false);
                if (npcId is not null)
                {
                    var result = _application.InteractCanonicalNpc(npcId);
                    var durable = !(result.Success || result.HintShown) || Save(showMessage: false);
                    Toast(durable ? result.Message ?? "Đã tương tác NPC." : "Tương tác đang chờ lưu bền vững.");
                    RefreshSnapshot();
                }
                else
                {
                    // All portal use goes through this durable transaction. GameApplication deliberately
                    // exposes the reachable target only; it never mutates a region from a generic E action.
                    var portalTarget = _application.NearbyCanonicalPortalTarget();
                    if (portalTarget is not null) _ = TravelToRegion(portalTarget);
                    else
                    {
                        var interaction = _application.InteractNearestCanonical();
                        if (interaction is not null)
                        {
                            var committed = !_application.HasCanonicalDurableChanges || Save(showMessage: false);
                            Toast(committed ? interaction : "Tương tác đang chờ lưu bền vững.");
                            if (committed) { RebuildMapTextures(); RefreshSnapshot(); }
                        }
                        else Toast("Không có đối tượng để tương tác.");
                    }
                }
            }
        }
        if (Input.IsActionJustPressed("save_game")) Save(showMessage: true, requireSafeManual: true);
        if (Input.IsActionJustPressed("load_game") && _pendingSave is null) TryLoad(showMessage: true);
        if (!GameplayCommandsBlocked) { _autosaveRemaining -= delta; if (_autosaveRemaining <= 0) { Save(showMessage: false); _autosaveRemaining = 10; } }
        if (_toastRemaining > 0) { _toastRemaining -= delta; if (_toastRemaining <= 0) _toast.Text = ""; }
        RefreshSnapshot();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // This development-only route is deliberately independent of the gameplay UI and
        // remains available while gameplay commands are paused or save writes are blocked.
        if (@event is InputEventKey trialKey && trialKey.Pressed && !trialKey.Echo && trialKey.Keycode == Key.F10 && _assetCatalog.CatalogVersion == "asset-integration-trial-v001")
        {
            OpenAssetTrialViewer();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventKey detailsKey && detailsKey.Pressed && !detailsKey.Echo && (detailsKey.Keycode == Key.I || detailsKey.Keycode == Key.Escape && _detailsOverlay.Visible))
        {
            ToggleDetails();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (GameplayCommandsBlocked) return;
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.M)
        {
            _worldMap.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree() { if (_application is not null && !_saveWritesBlocked) Save(showMessage: false); }

    public override void _Draw()
    {
        var world = _snapshot?.World; var width = (float)(world?.Width ?? 1536); var height = (float)(world?.Height ?? 768);
        DrawRect(new Rect2(0, 0, width, height), new Color("#c99b5b")); if (_ground is not null) DrawTextureRect(_ground, new Rect2(0, 0, width, height), true, new Color(1, 1, 1, 0.45f));
        if (_snapshot is null) return;
        var hasTrialWorldTiles = DrawAshGravesTrialTiles();
        if (hasTrialWorldTiles && _application.CanonicalContent is not null)
            DrawAshGravesVisualComposition(_application.CanonicalContent.Content.LayoutBlueprint);
        if (!hasTrialWorldTiles)
            foreach (var road in _application.CanonicalRoads()) DrawLine(ToGodot(road.Start), ToGodot(road.End), new Color("#756755"), (float)road.Width);
        foreach (var area in _application.CanonicalTerrain()) DrawRect(new Rect2((float)area.Bounds.X, (float)area.Bounds.Y, (float)area.Bounds.Width, (float)area.Bounds.Height), area.Terrain switch { V25TerrainTag.FireField => new Color("#9d472b"), V25TerrainTag.ToxicPool => new Color("#526a39"), V25TerrainTag.FrostFloor => new Color("#819ca6"), V25TerrainTag.Gap => new Color("#201d26"), _ => new Color("#4b5363") });
        foreach (var obj in _application.WorldObjects().Where(item => !item.Destroyed).OrderBy(item => item.ZIndex).ThenBy(item => item.Position.Y))
        {
            var p = ToGodot(obj.Position);
            if (obj.Type == "wall") continue;
            if (ShowPresentationDebug && (obj.Type is "npc" or "shrine" or "chest" or "landmark" or "portal" or "secret")) DrawString(ThemeDB.FallbackFont, p + new Vector2(-24, -20), obj.Id, fontSize: 9);
            var scale = WorldObjectVisualScale(obj.Type) * (float)System.Math.Clamp(obj.PresentationScale, 0.1, 3);
            if (_mapTextures.TryGetValue(obj.AssetId, out var texture) && _assetCatalog.TryGet(obj.AssetId, out var asset))
                DrawWorldAsset(texture, asset, p, scale, new Color(1, 1, 1, 0.96f));
            else
                DrawMissingAssetMarker(p, obj.AssetId, WorldObjectMissingRadius(obj.Type) * scale);
        }
        if (ShowMapCollisionDebug)
            foreach (var rect in _snapshot.World.BlockingRects) DrawRect(new Rect2((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height), new Color(0.25f, 0.16f, 0.08f, 0.32f), false, 2);
        foreach (var soul in _snapshot.WorldSouls)
        {
            var p = ToGodot(soul.Position); var assetId = SoulPickupAssetId(soul.OriginSpeciesId, soul.OriginRank);
            DrawGroundShadow(p, 20, 0.34f);
            if (_assetCatalog.TryGet(assetId, out var asset)) DrawCanonicalFrame(asset, p);
            else DrawMissingAssetMarker(p, assetId, 10);
        }
        var playerPosition = ToGodot(_snapshot.Player.Position);
        DrawGroundShadow(playerPosition, BaseTileSize, 0.42f);
        if (MissingAssetId(_playerSprite) is { } playerMissing) DrawMissingAssetMarker(playerPosition, playerMissing, 12);
        foreach (var monster in _snapshot.Monsters)
        {
            var p = ToGodot(monster.Position);
            DrawGroundShadow(p, BaseTileSize, 0.4f);
            if (_monsterSprites.TryGetValue(monster.Uid, out var sprite) && MissingAssetId(sprite) is { } missing) { DrawMissingAssetMarker(p, missing, 18); if (ShowPresentationDebug) DrawString(ThemeDB.FallbackFont, p + new Vector2(-24, -36), _application.SpeciesDisplayName(monster.SpeciesId), fontSize: 9); }
            DrawRect(new Rect2(p.X - 22, p.Y - 31, 44, 5), new Color("#3f0d0d"));
            DrawRect(new Rect2(p.X - 22, p.Y - 31, (float)(44 * monster.CurrentHp / monster.MaximumHp), 5), new Color("#22c55e"));
        }
        foreach (var ally in _snapshot.Allies)
        {
            var p = ToGodot(ally.Position);
            DrawGroundShadow(p, BaseTileSize, 0.4f);
            if (_allySprites.TryGetValue(ally.Uid, out var sprite) && MissingAssetId(sprite) is { } missing) { DrawMissingAssetMarker(p, missing, 18); if (ShowPresentationDebug) DrawString(ThemeDB.FallbackFont, p + new Vector2(-24, -36), _application.SpeciesDisplayName(ally.SpeciesId), fontSize: 9); }
            DrawRect(new Rect2(p.X - 22, p.Y - 31, 44, 5), new Color("#123b25"));
            DrawRect(new Rect2(p.X - 22, p.Y - 31, (float)(44 * ally.CurrentHp / ally.MaximumHp), 5), new Color("#86efac"));
        }
    }

    private void RefreshSnapshot()
    {
        _snapshot = _application.Snapshot(); var player = _snapshot.Player; _camera.Position = ToGodot(player.Position);
        _playerSprite.Position = ToGodot(player.Position); SyncMonsters(_snapshot.Monsters); SyncAllies(_snapshot.Allies);
        if (player.Rank != _visualRank) { var old = _playerSprite; _visualRank = player.Rank; _playerSprite = BuildPlayerSprite(_visualRank); AddChild(_playerSprite); _playerSprite.Position = old.Position; old.QueueFree(); }
        var regionName = _application.RegionDetails(_snapshot.CurrentRegionId)?.DisplayName ?? _snapshot.CurrentRegionId;
        _status.Text = $"{regionName}\nHP {DisplayWhole(player.CurrentHp)}/{DisplayWhole(player.MaximumHp)} · SP {DisplayWhole(player.CurrentSpirit)}/{DisplayWhole(player.MaximumSpirit)}";
        _currency.Text = _application.CanonicalInventory() is { } canonicalInventory
            ? $"Coin {canonicalInventory.Coins} · XP {player.Xp} · Hồn {_snapshot.OwnedSouls.Count}"
            : $"Vật phẩm {_snapshot.Inventory.Sum(item => item.Count)} · XP {player.Xp} · Hồn {_snapshot.OwnedSouls.Count}";
        var soulState = _application.ActivePossessionSoulId is not null ? "Phụ hồn" : _snapshot.Allies.Count > 0 ? $"Ally {_snapshot.Allies.Count}" : $"Hồn {_snapshot.OwnedSouls.Count}";
        _combatSoulState.Text = _snapshot.Monsters.Count > 0 ? $"Địch {_snapshot.Monsters.Count} · {soulState}" : soulState;
        _minimap.Refresh(_snapshot, _application.WorldObjects(), _application.WasCanonicalTileVisited);
        var runtime = _application.CanonicalRuntimeSnapshot();
        _bossTelegraphs.Refresh(runtime.Casts, runtime.Actors ?? Array.Empty<V25ActorRuntimeSnapshot>());
        var signature = string.Join('|', _snapshot.OwnedSouls.Select(soul => $"{soul.Id}:{soul.Level}:{soul.Xp}")); if (signature != _lastSoulSignature) { RebuildSoulPanel(_snapshot); _lastSoulSignature = signature; }
        var questSignature = string.Join(';', _application.CanonicalQuests().Select(item => $"{item.QuestId}:{item.Status}:{item.ObjectiveProgress}"));
        var soulLoopSignature = string.Join(';', _application.CanonicalSoulLoop().Select(item => $"{item.SpeciesId}:{item.CommittedDensityMicro}:{item.PendingDensityMicro}:{item.PassedGateIndex}:{item.SyncMicro}:{item.RuntimeStatus}:{item.Vitality:0.######}:{item.RecoverySeconds:0.###}"));
        var allySignature = string.Join(';', _snapshot.Allies.OrderBy(item => item.Uid, StringComparer.Ordinal).Select(item => $"{item.Uid}:{item.AiState}:{item.Vitality:0.######}"));
        var featureSignature = $"{_snapshot.OwnedSouls.Count}:{_snapshot.SoulBanners.Sum(item => item.BoundSoulIds.Count)}:{_application.Inventory().Count}:{_application.SoulRuntime(_snapshot.OwnedSouls.FirstOrDefault()?.Id ?? "").Status}:{_application.PossessionRemainingSeconds():0.###}:{questSignature}:{soulLoopSignature}:{allySignature}"; if (featureSignature != _lastFeatureSignature) { RebuildFeaturePanel(_snapshot); _lastFeatureSignature = featureSignature; }
        var banner = _snapshot.SoulBanners.FirstOrDefault(); var screenSignature = $"{player.Level}:{player.Xp}:{inventorySignature(_application.Inventory())}:{banner?.Level}:{banner?.BoundSoulIds.Count}"; if (screenSignature != _lastScreenSignature) { RebuildScreens(_snapshot); _lastScreenSignature = screenSignature; }
        QueueRedraw();
    }

    private static long DisplayWhole(double value) => double.IsFinite(value) ? (long)System.Math.Round(value, MidpointRounding.AwayFromZero) : 0;

    private bool Save(bool showMessage, bool requireSafeManual = false)
    {
        if (_saveWritesBlocked)
        {
            GD.PushError("Save writes are blocked after an invalid load; recover or load a valid save first.");
            if (showMessage) Toast("Đã chặn lưu để bảo toàn bản lưu lỗi.");
            return false;
        }
        if (requireSafeManual && !_application.CanManualCanonicalSave)
        {
            if (showMessage) Toast("Chỉ lưu tay ngoài giao chiến, nguy hiểm và chuyển cảnh. F6 để tạm dừng giữa tick.");
            return false;
        }
        try
        {
            if (_application.CanonicalContent is not null)
            {
                // Retry the exact staged payload and identity after an uncertain disk result.
                if (_pendingSave is null) _application.PrepareCanonicalRewardCommit();
                _pendingSave ??= _application.CaptureCanonicalSave(_v25Store!.NextCommitSequence(SelectedSaveId), SelectedSaveId);
                _v25Store!.Commit(_pendingSave);
                _pendingSave = null;
                _application.CommitCanonicalAcquisitionEvents();
            }
            else GodotSaveStore.Write(SavePath, _application.CaptureSaveJson());
            _saveCommitFailed = false;
            if (showMessage) Toast("Đã lưu tiến trình.");
            return true;
        }
        catch (Exception exception)
        {
            _saveCommitFailed = true;
            GD.PushError($"Save failed; simulation paused and transaction retained: {exception.Message}");
            Toast("Lưu thất bại. Đã tạm dừng; nhấn F5 để thử lại.");
            return false;
        }
    }

    private bool TryLoad(bool showMessage)
    {
        try
        {
            if (_application.CanonicalContent is not null)
            {
                var recovery = _v25Store!.Recover();
                if (recovery.Status == V25RecoveryStatus.RejectedPreserved)
                    throw new System.IO.InvalidDataException(recovery.Error ?? "Save recovery failed; original files preserved.");
                if (recovery.Save is not null)
                {
                    if (!string.Equals(recovery.Save.SaveId, SelectedSaveId, StringComparison.Ordinal))
                        throw new System.IO.InvalidDataException($"Bản lưu thuộc {recovery.Save.SaveId}, không phải {SelectedSaveId}.");
                    _application.RestoreCanonicalSave(recovery.Save);
                }
                else
                {
                    var legacyJson = GodotSaveStore.Read(LegacySavePath);
                    if (legacyJson is null) { if (showMessage) Toast("Chưa có bản lưu."); return false; }
                    // The V6 schema predates the canonical V2.5 save envelope.  It
                    // is deliberately not parsed or overwritten here: doing either
                    // would risk data loss.  The already-started canonical session
                    // is valid to play and saves exclusively to the V2.5 store.
                    _legacySavePreserved = true;
                    _saveWritesBlocked = false;
                    _saveCommitFailed = false;
                    _suspended = false;
                    _pendingSave = null;
                    Toast("Đã giữ nguyên bản lưu V6; bắt đầu phiên V2.5 mới.");
                    return false;
                }
            }
            else
            {
                var json = GodotSaveStore.Read(SavePath);
                if (json is null) { if (showMessage) Toast("Chưa có bản lưu."); return false; }
                _application.RestoreSaveJson(json);
            }
            _saveWritesBlocked = false;
            _saveCommitFailed = false;
            _suspended = false;
            _pendingSave = null;
            if (showMessage) Toast("Đã tải tiến trình.");
            return true;
        }
        catch (Exception exception)
        {
            _saveWritesBlocked = true;
            GD.PushError($"Load failed; original save preserved and future autosave/exit writes blocked: {exception.Message}");
            if (showMessage) Toast("Bản lưu không hợp lệ; đã giữ nguyên file.");
            return false;
        }
    }

    private V25SaveStore CreateSaveStore(int slot)
    {
        if (slot is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(slot));
        var path = slot == 1 ? SavePath : $"user://solo_vs_mortal_save_v25_slot{slot}.json";
        return new V25SaveStore(ProjectSettings.GlobalizePath(path), _application.CanonicalContent);
    }

    private void Suspend()
    {
        if (!Save(showMessage: false)) return;
        _suspended = true;
        Toast($"Đã tạm dừng tại ranh giới tick trong ô {_selectedSaveSlot}. Nhấn F6 để tiếp tục.");
    }

    private void SelectSaveSlot(int slot)
    {
        if (slot == _selectedSaveSlot) { Toast($"Đang dùng ô lưu {slot}."); return; }
        if (_pendingSave is not null || _application.HasCanonicalDurableChanges)
        {
            if (!Save(showMessage: false)) { Toast("Chưa thể đổi ô khi giao dịch lưu đang chờ."); return; }
        }
        _selectedSaveSlot = slot;
        _v25Store = CreateSaveStore(slot);
        _saveWritesBlocked = false;
        Toast($"Đã chọn ô lưu {slot}. F5 lưu, F9 tải ô này.");
        RefreshSnapshot();
    }

    private void Toast(string text) { _toast.Text = text; _toastRemaining = 2.5; }
    private void RebuildSoulPanel(GameSnapshot snapshot)
    {
        foreach (var child in _soulList.GetChildren()) child.QueueFree();
        if (_application.CanonicalContent is not null)
        {
            var bannerRank = _application.CanonicalContent.Profile(_application.CanonicalContent.ActiveProfileId).MaxBannerRank;
            _soulList.AddChild(new Label { Text = $"Hồn sở hữu theo Species · Hồn Phiên tối đa {bannerRank}", ThemeTypeVariation = "HeaderSmall" });
            foreach (var soul in snapshot.OwnedSouls)
            {
                var row = new HBoxContainer();
                var species = _application.CanonicalContent.SpeciesForProfile(_application.CanonicalContent.ActiveProfileId).FirstOrDefault(item => item.Id == soul.OriginSpeciesId);
                var runtime = _application.SoulRuntime(soul.Id);
                row.AddChild(new Label { Text = $"{soul.DisplayName}  Lv.{soul.Level}  PT.{species?.PowerTier ?? 0}  · {runtime.Status}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                var summon = new Button { Text = runtime.Status == SoulRuntimeStatus.Summoned ? "Thu hồi" : "Triệu hồi", Disabled = runtime.Status is SoulRuntimeStatus.Dispersed or SoulRuntimeStatus.Possessed };
                StyleActionButton(summon);
                summon.Pressed += () => { if (GameplayCommandsBlocked) return; if (runtime.Status == SoulRuntimeStatus.Summoned) _application.UnsummonSoul(soul.Id); else _application.SummonSoul(soul.Id, "canonical.banner", new SimVec2(_snapshot!.Player.Position.X + 36, _snapshot.Player.Position.Y)); RefreshSnapshot(); };
                row.AddChild(summon);
                var possess = new Button { Text = "Phụ hồn", Disabled = runtime.Status != SoulRuntimeStatus.Ready || _application.ActivePossessionSoulId is not null };
                StyleActionButton(possess);
                possess.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.StartPossession(soul.Id); var durable = !result.Success || Save(showMessage: false); Toast(result.Success && durable ? "Đã bắt đầu phụ hồn." : result.Success ? "Phụ hồn đang chờ lưu bền vững." : "Không thể phụ hồn Soul này."); RefreshSnapshot(); };
                row.AddChild(possess);
                _soulList.AddChild(row);
            }
            if (snapshot.OwnedSouls.Count == 0) _soulList.AddChild(new Label { Text = "Chưa sở hữu Soul. Nhấn E gần orb." });
            return;
        }
        foreach (var soul in snapshot.OwnedSouls)
        {
            var row = new HBoxContainer(); var label = new Label { Text = $"{soul.DisplayName}  Lv.{soul.Level}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = soul.SoulNatureId }; row.AddChild(label);
            var banner = snapshot.SoulBanners.FirstOrDefault(); var bound = banner?.BoundSoulIds.Contains(soul.Id) == true;
            var link = _application.SoulLinks().FirstOrDefault(item => item.SoulId == soul.Id); var runtime = _application.SoulRuntime(soul.Id);
            var bind = new Button { Text = bound ? "Gỡ Hồn Liên" : "Thiết lập Hồn Liên", Disabled = banner is null || (bound && !string.IsNullOrEmpty(link?.BannerId) && runtime.Status is SoulRuntimeStatus.Summoned or SoulRuntimeStatus.Possessed) };
            StyleActionButton(bind);
            bind.Pressed += () => { if (GameplayCommandsBlocked) return; if (banner is null) return; var ok = bound ? _application.UnbindSoul(soul.Id, banner.Id).Success : _application.BindSoul(soul.Id, banner.Id).Success; if (ok) { Toast(bound ? "Đã gỡ bind Soul." : "Đã bind Soul."); RefreshSnapshot(); } }; row.AddChild(bind);
            _soulList.AddChild(row);
        }
        if (snapshot.OwnedSouls.Count == 0) _soulList.AddChild(new Label { Text = "Chưa sở hữu Soul. Nhấn E gần orb." });
    }
    private void RebuildScreens(GameSnapshot snapshot)
    {
        var inventory = GetNode<VBoxContainer>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Screens/Inventory"); var progression = GetNode<VBoxContainer>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Screens/Progression"); var bannerPage = GetNode<VBoxContainer>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Screens/Banner");
        foreach (var page in new[] { inventory, progression, bannerPage }) foreach (var child in page.GetChildren()) child.QueueFree();

        inventory.AddChild(new Label { Text = "Túi Đồ", ThemeTypeVariation = "HeaderMedium" });
        var items = _application.Inventory();
        if (_application.CanonicalContent is not null && _application.CanonicalInventory() is { } canonicalInventory)
        {
            inventory.AddChild(new Label { Text = $"Coin: {canonicalInventory.Coins} · Mang theo {canonicalInventory.Items.Count}/60 · Stash {canonicalInventory.Overflow.Count}" });
            inventory.AddChild(new Label { Text = canonicalInventory.Equipped.Count == 0 ? "Trang bị: trống" : "Trang bị: " + string.Join(", ", canonicalInventory.Equipped.Select(item => $"{item.Slot}={item.DefinitionId}")) });
            if (canonicalInventory.Equipped.Any(item => item.DefinitionId == "equipment.sword.rank01") && TryBuildCanonicalIcon("equipment.sword.rank01.icon", 32) is { } swordIcon)
            {
                var swordRow = new HBoxContainer(); swordRow.AddChild(swordIcon); swordRow.AddChild(new Label { Text = "Kiếm Rank 01 — icon Trial", VerticalAlignment = VerticalAlignment.Center }); inventory.AddChild(swordRow);
            }
            var actions = new HBoxContainer();
            foreach (var potionId in new[] { "hp_potion", "spirit_potion" })
            {
                var potion = new Button { Text = potionId == "hp_potion" ? "Dùng HP Potion" : "Dùng Spirit Potion" };
                StyleActionButton(potion); potion.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.UseCanonicalPotion(potionId); var durable = !result.Success || Save(showMessage: false); Toast(result.Success && durable ? "Đã dùng potion." : result.Success ? "Potion đang chờ lưu bền vững." : $"Không thể dùng: {result.Failure}"); RefreshSnapshot(); }; actions.AddChild(potion);
            }
            var manage = new Button { Text = "Trang bị / Cửa hàng / Kỹ năng" };
            manage.Pressed += () => { if (!GameplayCommandsBlocked) OpenManagementWindow(); };
            actions.AddChild(manage);
            inventory.AddChild(actions);
        }
        var grid = new GridContainer { Columns = 7, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; inventory.AddChild(grid);
        for (var index = 0; index < Math.Max(14, items.Count); index++)
        {
            InventoryItem? item = index < items.Count ? items[index] : null;
            var slot = new Button { Text = item is null ? "" : $"{item.Value.StableId}\n×{item.Value.Count}", TooltipText = item is null ? "Ô trống" : item.Value.StableId, CustomMinimumSize = new Vector2(74, 36) };
            FancyUi.ApplyItemSlot(slot); grid.AddChild(slot);
        }
        var inventoryFooter = new HBoxContainer(); inventoryFooter.AddChild(new Label { Text = _application.CanonicalContent is null ? $"Số ô: {items.Count}/100" : $"Tổng stack hiển thị: {items.Count}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        inventory.AddChild(inventoryFooter);

        progression.AddChild(new Label { Text = "Tiến Hóa Nhân Vật", ThemeTypeVariation = "HeaderMedium" });
        progression.AddChild(new Label { Text = $"Cấp {snapshot.Player.Level}  •  XP {snapshot.Player.Xp}\nCảnh giới {snapshot.Player.Rank}\nCông {DisplayWhole(snapshot.Player.Attack)}  •  Thủ {DisplayWhole(snapshot.Player.Defense)}", SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var progressionActions = new HBoxContainer();
        if (_application.CanonicalContent?.ActiveProfileId == "beta_01")
        {
            var upgrade = new Button { Text = "Mở Full 01 (tại shrine)" }; StyleActionButton(upgrade);
            upgrade.Pressed += () => { if (GameplayCommandsBlocked || !Save(showMessage: false)) return; if (!_application.UpgradeCanonicalProfile()) { Toast("Cần ở shrine, ngoài giao tranh."); return; } if (Save(showMessage: false)) { Toast("Đã nâng profile Full 01, giữ tiến trình cũ."); RefreshSnapshot(); } };
            progressionActions.AddChild(upgrade);
        }
        var breakthrough = new Button { Text = "Thử phá cảnh", CustomMinimumSize = new Vector2(132, 24) }; StyleActionButton(breakthrough); breakthrough.Pressed += () => { if (GameplayCommandsBlocked) return; var succeeded = _application.AttemptPlayerBreakthrough(); var durable = !succeeded || Save(showMessage: false); Toast(succeeded && durable ? "Phá cảnh thành công." : succeeded ? "Phá cảnh đang chờ lưu bền vững." : "Chưa đủ điều kiện phá cảnh."); if (durable) RefreshSnapshot(); }; progressionActions.AddChild(breakthrough); progression.AddChild(progressionActions);

        var banner = snapshot.SoulBanners.FirstOrDefault();
        if (_application.CanonicalContent is not null)
        {
            bannerPage.AddChild(new Label { Text = $"Hồn Phiên canonical · Rank {_application.CanonicalBannerRank}\nMỗi Species tối đa một Soul; không dùng bind/slot prototype.", ThemeTypeVariation = "HeaderMedium" });
            if (banner is not null && TryBuildCanonicalIcon("soul.skeleton.rank01.banner.icon", 32) is { } bannerIcon)
            {
                var bannerIconRow = new HBoxContainer(); bannerIconRow.AddChild(bannerIcon); bannerIconRow.AddChild(new Label { Text = "Biểu tượng Skeleton Rank 01", VerticalAlignment = VerticalAlignment.Center }); bannerPage.AddChild(bannerIconRow);
            }
            bannerPage.AddChild(new Label { Text = snapshot.OwnedSouls.Count == 0 ? "Chưa có Species Soul." : string.Join('\n', snapshot.OwnedSouls.Select(soul => $"• {soul.DisplayName} — Lv.{soul.Level}")), SizeFlagsVertical = Control.SizeFlags.ExpandFill });
            var upgrade = new Button { Text = "Nâng Hồn Phiên tại shrine" }; StyleActionButton(upgrade);
            upgrade.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.AttemptCanonicalBannerUpgrade(); var durable = !result.Success || result.AlreadyApplied || Save(showMessage: false); Toast(result.Success && durable ? $"Hồn Phiên Rank {result.BannerRank}." : result.Success ? "Nâng Hồn Phiên đang chờ lưu bền vững." : $"Không thể nâng: {result.Failure}"); RefreshSnapshot(); }; bannerPage.AddChild(upgrade);
            return;
        }
        bannerPage.AddChild(new Label { Text = banner is null ? "Chưa có Hồn Phiên" : $"Hồn Phiên {banner.Tier}\nCấp {banner.Level}  •  Ô {banner.BoundSoulIds.Count}/{banner.SlotLimit}\nDung lượng {banner.UsedCapacity}/{banner.CapacityLimit}", ThemeTypeVariation = "HeaderMedium" });
        var boundSouls = banner is null ? Array.Empty<OwnedSoulSnapshot>() : snapshot.OwnedSouls.Where(item => banner.BoundSoulIds.Contains(item.Id)).ToArray();
        bannerPage.AddChild(new Label { Text = boundSouls.Length == 0 ? "Chưa có linh hồn liên kết." : string.Join('\n', boundSouls.Select(soul => $"• {soul.DisplayName} — Lv.{soul.Level}")), SizeFlagsVertical = Control.SizeFlags.ExpandFill });
    }

    private void OpenManagementWindow()
    {
        var window = new Window { Title = "Trang bị và kỹ năng", Size = new Vector2I(820, 650) };
        AddChild(window); window.CloseRequested += () => window.QueueFree();
        var scroll = new ScrollContainer(); scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); window.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(list);
        void Rebuild()
        {
            foreach (var child in list.GetChildren()) { list.RemoveChild(child); child.QueueFree(); }
            var inventory = _application.CanonicalInventory(); var canonical = _application.CanonicalContent;
            if (inventory is null || canonical is null) return;
            void ActionButton(string text, Func<bool> command)
            {
                var button = new Button { Text = text }; list.AddChild(button);
                button.Pressed += () =>
                {
                    if (GameplayCommandsBlocked) return;
                    var ok = command(); var durable = !ok || Save(showMessage: false);
                    Toast(ok && durable ? "Đã cập nhật." : ok ? "Đang chờ lưu; nhấn F5 để thử lại." : "Chưa đủ điều kiện thực hiện.");
                    if (durable) { _lastScreenSignature = ""; RefreshSnapshot(); Rebuild(); }
                };
            }
            list.AddChild(new Label { Text = $"Coin {inventory.Coins} · Túi {inventory.Items.Count}/60 · Kho {inventory.Overflow.Count}" });
            foreach (var item in inventory.Overflow)
                ActionButton($"Nhận từ kho tại shrine: {item.DefinitionId} ×{item.Count}", () => _application.WithdrawCanonicalOverflow(item.InstanceUid).Success);
            foreach (var equipped in inventory.Equipped)
                ActionButton($"Tháo {equipped.Slot}: {equipped.DefinitionId}", () => _application.UnequipCanonicalItem(equipped.Slot).Success);
            foreach (var item in inventory.Items)
            {
                list.AddChild(new Label { Text = $"{item.DefinitionId} ×{item.Count}" });
                if (canonical.Content.Equipment.Any(definition => definition.Id == item.DefinitionId))
                    ActionButton("Trang bị " + item.DefinitionId, () => _application.EquipCanonicalItem(item.InstanceUid).Success);
                ActionButton("Bán " + item.DefinitionId, () => _application.SellCanonicalItem(item.InstanceUid).Success);
            }
            list.AddChild(new Label { Text = "Cửa hàng — đến gần Kha để mua/bán" });
            foreach (var item in canonical.EquipmentForProfile(canonical.ActiveProfileId).Where(item => item.Rank <= _application.Snapshot().Player.Rank))
                ActionButton($"Mua {item.Id}", () => _application.BuyCanonicalItem(item.Id).Success);
            foreach (var item in canonical.Content.Consumables)
                ActionButton("Mua " + item.Id, () => _application.BuyCanonicalItem(item.Id).Success);
            var grants = _application.CanonicalSkillGrants();
            if (grants is null) return;
            list.AddChild(new Label { Text = "Kỹ năng — đến gần Linh để học; đổi slot khi an toàn" });
            for (var i = 0; i < grants.ActiveSkillIds.Count; i++)
            {
                var slot = i; if (grants.ActiveSkillIds[i] is not { } id) continue;
                ActionButton($"Gỡ active {i+1}: {id}", () => _application.ClearCanonicalSkillSlot(slot, false));
            }
            for (var i = 0; i < grants.PassiveSkillIds.Count; i++)
            {
                var slot = i; if (grants.PassiveSkillIds[i] is not { } id) continue;
                ActionButton($"Gỡ passive {i+1}: {id}", () => _application.ClearCanonicalSkillSlot(slot, true));
            }
            foreach (var skill in canonical.LearnedSkillsForProfile(canonical.ActiveProfileId))
            {
                if (!grants.LearnedSkillIds.Contains(skill.Id)) { ActionButton("Học " + skill.Id, () => _application.LearnCanonicalSkill(skill.Id)); continue; }
                var passive = skill.FunctionalCategory == "Passive";
                var slots = passive ? Math.Min(3, 1 + (_application.Snapshot().Player.Rank - 1) / 3) : Math.Min(6, 2 + (_application.Snapshot().Player.Rank - 1) / 2);
                for (var index = 0; index < slots; index++)
                {
                    var slot = index;
                    ActionButton($"{skill.Id} → {(passive ? "passive" : "active")} {slot + 1}", () => passive ? _application.AssignCanonicalPassiveSkill(slot, skill.Id) : _application.AssignCanonicalActiveSkill(slot, skill.Id));
                }
                if (!passive) ActionButton("Thăng bậc " + skill.Id, () => _application.PromoteCanonicalSkill(skill.Id, grants.PromotedRanks.GetValueOrDefault(skill.Id, skill.BaseRank) + 1));
            }
        }
        Rebuild(); window.PopupCentered();
    }

    private void ToggleDetails()
    {
        _detailsOverlay.Visible = !_detailsOverlay.Visible;
        GetViewport().SetInputAsHandled();
    }

    // A deliberately narrow, development-only viewer for clips that have no safe gameplay event
    // yet. It reads the catalog only; it never dispatches Simulation commands or touches saves.

    private void OpenAssetTrialViewer()
    {
        var entries = _assetCatalog.Assets.Values.OrderBy(asset => asset.AssetId, StringComparer.Ordinal).ToArray();
        if (entries.Length == 0) return;
        var window = new Window { Title = "Asset Integration Trial v001", Size = new Vector2I(980, 670) };
        AddChild(window); window.CloseRequested += () => window.QueueFree();
        var root = new VBoxContainer(); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.OffsetLeft = 18; root.OffsetTop = 16; root.OffsetRight = -18; root.OffsetBottom = -16; window.AddChild(root);
        root.AddChild(new Label { Text = "149 authorized assets • animation timing is source metadata • no visual, motion, or in-engine approval is implied.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        root.AddChild(new Label { Text = "12 Skeleton Enemy clips remain MISSING because their source state is integration_ready, outside the Trial authorization base states. Sword overlay is intentionally disabled: WEAPON_ALIGNMENT_METADATA_GAP.", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("#f2d795") });
        var selector = new OptionButton { CustomMinimumSize = new Vector2(0, 36) };
        foreach (var entry in entries) selector.AddItem(entry.AssetId);
        root.AddChild(selector);
        var metadata = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 54) }; root.AddChild(metadata);
        var stage = new Control { CustomMinimumSize = new Vector2(0, 440), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; root.AddChild(stage);
        var selected = new AnimatedSprite2D { Position = new Vector2(760, 210), Centered = false, Scale = Vector2.One * 2, ZIndex = 2 }; stage.AddChild(selected);
        var selectedCaption = new Label { Text = "Selected asset • 2× display only", Position = new Vector2(650, 350), Size = new Vector2(280, 30), HorizontalAlignment = HorizontalAlignment.Center }; stage.AddChild(selectedCaption);

        void AddScaleComparison(string assetId, string caption, Vector2 origin)
        {
            if (!_assetCatalog.TryGet(assetId, out var entry)) return;
            var actor = new AnimatedSprite2D { SpriteFrames = _assetCatalog.BuildFrames(entry, "trial"), Position = origin, Centered = false, Offset = -entry.Pivot, Scale = Vector2.One * 2 };
            actor.Play("trial"); stage.AddChild(actor);
            stage.AddChild(new Label { Text = caption, Position = origin + new Vector2(-70, 84), Size = new Vector2(140, 24), HorizontalAlignment = HorizontalAlignment.Center });
        }
        AddScaleComparison("player.base.idle.s", "Player", new Vector2(150, 210));
        AddScaleComparison("soul.skeleton.rank01.enemy.south", "Enemy static compatibility", new Vector2(350, 210));
        AddScaleComparison("soul.skeleton.rank01.ally.idle.s", "Ally", new Vector2(550, 210));

        void ShowEntry(long index)
        {
            var entry = entries[checked((int)index)];
            selected.SpriteFrames = _assetCatalog.BuildFrames(entry, "trial"); selected.Offset = -entry.Pivot; selected.Play("trial");
            metadata.Text = $"{entry.AssetId}\nrole={entry.Role}; representation={entry.Representation}; clip={entry.Clip}; direction={entry.Direction}; frames={entry.Frames.Count}; durations={string.Join(", ", entry.Frames.Select(frame => frame.DurationMs + "ms"))}; pivot=({entry.Pivot.X}, {entry.Pivot.Y}); technical={entry.TechnicalQa}; visual={entry.VisualQa}; inEngine={entry.InEngineQa}; approval={entry.ApprovalStatus}";
        }
        selector.ItemSelected += ShowEntry; selector.Select(0); ShowEntry(0);
        window.PopupCentered();
    }

    private static string inventorySignature(IReadOnlyList<InventoryItem> items) => string.Join(';', items.Select(item => $"{item.StableId}:{item.Count}"));

    private void RebuildFeaturePanel(GameSnapshot snapshot)
    {
        foreach (var child in _featureList.GetChildren()) child.QueueFree();
        if (_application.CanonicalContent is not null)
        {
            _featureList.AddChild(new Label { Text = $"Hồn Phiên canonical · Rank {_application.CanonicalBannerRank}\nSpecies Soul: {snapshot.OwnedSouls.Count} · Mỗi species một Ally", ThemeTypeVariation = "HeaderMedium" });
            if (_assetCatalog.CatalogVersion == "asset-integration-trial-v001")
            {
                var openTrial = new Button { Text = "Mở Asset Integration Trial (149 assets)" }; StyleActionButton(openTrial); openTrial.Pressed += OpenAssetTrialViewer; _featureList.AddChild(openTrial);
            }
            var canonicalPossession = new Label { Text = _application.ActivePossessionSoulId is { } canonicalActiveId ? $"Phụ hồn: {canonicalActiveId} ({Math.Ceiling(_application.PossessionRemainingSeconds())}s)" : "Phụ hồn: không hoạt động" };
            _featureList.AddChild(canonicalPossession);
            if (_application.ActivePossessionSoulId is not null)
            {
                var endPossession = new Button { Text = "Kết thúc Phụ hồn" }; StyleActionButton(endPossession);
                endPossession.Pressed += () => { if (GameplayCommandsBlocked) return; var ended = _application.EndPossession(); var durable = !ended || Save(showMessage: false); Toast(ended && durable ? "Đã kết thúc Phụ hồn; cooldown bắt đầu." : ended ? "Kết thúc Phụ hồn đang chờ lưu bền vững." : "Không có Phụ hồn đang hoạt động."); RefreshSnapshot(); };
                _featureList.AddChild(endPossession);
            }
            var soulLoop = _application.CanonicalSoulLoop();
            if (soulLoop.Count > 0)
                _featureList.AddChild(new Label { Text = string.Join('\n', soulLoop.Select(item => $"{item.DisplayName}: D {V25FixedPoint.FromMicro(item.CommittedDensityMicro):0.###} + pending {V25FixedPoint.FromMicro(item.PendingDensityMicro):0.###}; gate {item.PassedGateIndex}; Sync {V25FixedPoint.FromMicro(item.SyncMicro):0.###}; vitality {item.Vitality:P0}")) });
            var teamActions = new HBoxContainer();
            var summonAll = new Button { Text = "Triệu hồi tất cả (Shift+Q)" }; StyleActionButton(summonAll);
            summonAll.Pressed += () => { if (GameplayCommandsBlocked) return; var results = _application.SummonAllSouls().Results; var count = results.Count(item => item.Value.Success); var durable = count == 0 || Save(showMessage: false); Toast(count == 0 ? "Không có Soul sẵn sàng." : durable ? $"Đã triệu hồi {count}/{results.Count} Soul." : "Triệu hồi đang chờ lưu bền vững."); RefreshSnapshot(); };
            teamActions.AddChild(summonAll);
            var recallAll = new Button { Text = "Thu hồi tất cả (Ctrl+Q)" }; StyleActionButton(recallAll);
            recallAll.Pressed += () => { if (GameplayCommandsBlocked) return; var count = _application.RecallAllSouls(); var durable = count == 0 || Save(showMessage: false); Toast(count == 0 ? "Không có Ally để thu hồi." : durable ? $"Đã thu hồi {count} Ally." : "Thu hồi đang chờ lưu bền vững."); RefreshSnapshot(); };
            teamActions.AddChild(recallAll);
            _featureList.AddChild(teamActions);
            var aiActions = new HBoxContainer();
            var mode = new Button { Text = "Đổi Guard/Assault (H)" }; StyleActionButton(mode);
            mode.Pressed += () => { if (GameplayCommandsBlocked) return; var changed = _application.ToggleCanonicalAllyMode(); var durable = !changed || Save(showMessage: false); Toast(changed && durable ? "Đã đổi mode và regroup." : changed ? "Đổi mode đang chờ lưu bền vững." : "Không có Ally để đổi mode."); RefreshSnapshot(); };
            aiActions.AddChild(mode);
            var focus = new Button { Text = "Focus tại con trỏ (G)" }; StyleActionButton(focus);
            focus.Pressed += () => { if (GameplayCommandsBlocked) return; var mouse = GetGlobalMousePosition(); var count = _application.FocusAlliesAt(new SimVec2(mouse.X, mouse.Y)); var durable = count == 0 || Save(showMessage: false); Toast(count == 0 ? "Không có Focus hợp lệ." : durable ? $"Focus: {count} Ally." : "Focus đang chờ lưu bền vững."); RefreshSnapshot(); };
            aiActions.AddChild(focus);
            _featureList.AddChild(aiActions);
            var syncRitual = new Button { Text = "Nghi thức Sync tại Shrine (giữ E)" }; StyleActionButton(syncRitual);
            syncRitual.Pressed += () => { if (GameplayCommandsBlocked || _activeRitualPowerId is not null) return; var ritual = _application.BeginCanonicalSyncRitual(); if (ritual.Started) { _activeSyncRitualSpeciesId = ritual.SpeciesId; Toast($"Giữ E trong {Math.Ceiling(ritual.RemainingTicks / 60.0)} giây để hoàn tất Sync."); } else Toast("Cần ở Shrine và đủ 7 nguồn Sync khác của một Soul."); };
            _featureList.AddChild(syncRitual);
            if (_application.CanonicalSkillGrants() is { } grants)
            {
                var skillRow = new HBoxContainer();
                for (var slot = 0; slot < grants.ActiveSkillIds.Count; slot++)
                {
                    var skillId = grants.ActiveSkillIds[slot]; if (skillId is null) continue;
                    var button = new Button { Text = $"{slot + 1}: {skillId}" }; StyleActionButton(button);
                    var capturedSlot = slot; button.Pressed += () => { if (!GameplayCommandsBlocked) _application.RequestCanonicalLearnedSkill(capturedSlot); }; skillRow.AddChild(button);
                }
                _featureList.AddChild(skillRow);
            }
            var questSummary = _application.CanonicalQuests().Where(item => item.Status is V25QuestStatus.Available or V25QuestStatus.Active or V25QuestStatus.Completed).Take(3).ToArray();
            _featureList.AddChild(new Label { Text = questSummary.Length == 0 ? "Nhiệm vụ: chưa có mục tiêu khả dụng" : string.Join('\n', questSummary.Select(item => $"{item.QuestId}: {item.ObjectiveProgress}/{item.ObjectiveRequired} · {item.Status}")) });
            var npcRow = new HBoxContainer();
            foreach (var npcId in new[] { "an", "kha", "linh" })
            {
                var npc = new Button { Text = $"E: {npcId}" }; StyleActionButton(npc);
                npc.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.InteractCanonicalNpc(npcId); var durable = !(result.Success || result.HintShown) || Save(showMessage: false); Toast(durable ? result.Message ?? (result.Success ? "Đã cập nhật nhiệm vụ." : "Không có tương tác.") : "Tương tác đang chờ lưu bền vững."); RefreshSnapshot(); }; npcRow.AddChild(npc);
            }
            _featureList.AddChild(npcRow);
            var shrineActions = new HBoxContainer();
            var rest = new Button { Text = "Nghỉ 1 giây tại Shrine" }; StyleActionButton(rest);
            rest.Pressed += () =>
            {
                if (GameplayCommandsBlocked) return;
                var result = _application.RestAtCanonicalShrine();
                var durable = !result || Save(showMessage: false);
                Toast(result && durable ? "Đã nghỉ tại Shrine." : result ? "Nghỉ đang chờ lưu bền vững." : "Cần ở Shrine, ngoài giao tranh và hazard.");
                RefreshSnapshot();
            };
            shrineActions.AddChild(rest);
            var reset = new Button { Text = "RestReset encounters" }; StyleActionButton(reset);
            reset.Pressed += () =>
            {
                if (GameplayCommandsBlocked) return;
                var result = _application.RestResetCanonicalEncounters();
                var durable = !result || Save(showMessage: false);
                Toast(result && durable ? "Đã reset encounter đã bị đánh bại hợp lệ." : result ? "RestReset đang chờ lưu bền vững." : "Không thể RestReset ở trạng thái hiện tại.");
                RefreshSnapshot();
            };
            shrineActions.AddChild(reset);
            _featureList.AddChild(shrineActions);
            foreach (var quest in questSummary.Where(item => item.Status == V25QuestStatus.Completed))
            {
                var claim = new Button { Text = $"Nhận thưởng {quest.QuestId}" }; StyleActionButton(claim);
                claim.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.ClaimCanonicalQuestReward(quest.QuestId); var durable = !result.Success || result.AlreadyClaimed || Save(showMessage: false); Toast(result.Success && durable ? "Đã nhận thưởng nhiệm vụ." : result.Success ? "Thưởng đang chờ lưu bền vững." : $"Không thể nhận: {result.Failure}"); RefreshSnapshot(); }; _featureList.AddChild(claim);
            }
            foreach (var soul in snapshot.OwnedSouls)
            {
                var runtime = _application.SoulRuntime(soul.Id);
                var row = new HBoxContainer(); row.AddChild(new Label { Text = $"{soul.DisplayName} · {runtime.Status}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                var summon = new Button { Text = runtime.Status == SoulRuntimeStatus.Summoned ? "Thu hồi" : "Triệu hồi", Disabled = runtime.Status is SoulRuntimeStatus.Dispersed or SoulRuntimeStatus.Possessed };
                StyleActionButton(summon);
                summon.Pressed += () => { if (GameplayCommandsBlocked) return; if (runtime.Status == SoulRuntimeStatus.Summoned) _application.UnsummonSoul(soul.Id); else _application.SummonSoul(soul.Id, "canonical.banner", new SimVec2(_snapshot!.Player.Position.X + 36, _snapshot.Player.Position.Y)); RefreshSnapshot(); };
                row.AddChild(summon);
                var possess = new Button { Text = "Phụ hồn", Disabled = runtime.Status != SoulRuntimeStatus.Ready || _application.ActivePossessionSoulId is not null };
                StyleActionButton(possess);
                possess.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.StartPossession(soul.Id); var durable = !result.Success || Save(showMessage: false); Toast(result.Success && durable ? "Đã bắt đầu phụ hồn." : result.Success ? "Phụ hồn đang chờ lưu bền vững." : "Không thể phụ hồn Soul này."); RefreshSnapshot(); };
                row.AddChild(possess); _featureList.AddChild(row);
            }
            return;
        }
        var banner = snapshot.SoulBanners.FirstOrDefault();
        _featureList.AddChild(new Label { Text = banner is null ? "Hồn Phiên: chưa tạo" : $"Hồn Phiên {banner.Tier}  Lv.{banner.Level}\nÔ: {banner.UsedCapacity}/{banner.CapacityLimit}  •  Ô: {banner.BoundSoulIds.Count}/{banner.SlotLimit}" });
        var inventory = _application.Inventory(); _featureList.AddChild(new Label { Text = inventory.Count == 0 ? "Vật phẩm: trống" : "Vật phẩm: " + string.Join(", ", inventory.Select(item => $"{item.StableId}×{item.Count}")) });
        var possession = new Label { Text = _application.ActivePossessionSoulId is { } active ? $"Đảo chiều Hồn Liên: {active} ({Math.Ceiling(_application.PossessionRemainingSeconds())}s)" : "Đảo chiều Hồn Liên: không hoạt động" }; _featureList.AddChild(possession);
        foreach (var soul in snapshot.OwnedSouls.Where(soul => banner?.BoundSoulIds.Contains(soul.Id) == true))
        {
            var link = _application.SoulLinks().First(item => item.SoulId == soul.Id); var runtime = _application.SoulRuntime(soul.Id); var row = new HBoxContainer(); row.AddChild(new Label { Text = $"{soul.DisplayName}: {link.State} · Tải {DisplayWhole(link.SoulCost)} · Ổn định {link.Stability:P0}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var summon = new Button { Text = runtime.Status == SoulRuntimeStatus.Summoned ? "Thu hồi" : "Triệu hồi", Disabled = runtime.Status is SoulRuntimeStatus.Dispersed or SoulRuntimeStatus.Possessed };
            StyleActionButton(summon);
            summon.Pressed += () => { if (GameplayCommandsBlocked) return; if (runtime.Status == SoulRuntimeStatus.Summoned) _application.UnsummonSoul(soul.Id); else _application.SummonSoul(soul.Id, banner!.Id, new SimVec2(_snapshot!.Player.Position.X + 36, _snapshot.Player.Position.Y)); RefreshSnapshot(); }; row.AddChild(summon);
            var possess = new Button { Text = "Phụ hồn", Disabled = runtime.Status != SoulRuntimeStatus.Ready || _application.ActivePossessionSoulId is not null }; StyleActionButton(possess); possess.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.StartPossession(soul.Id); Toast(result.Success ? "Đã bắt đầu phụ hồn." : "Không thể phụ hồn Soul này."); RefreshSnapshot(); }; row.AddChild(possess); _featureList.AddChild(row);
        }
    }
    private void ApplyHudVisualDesign()
    {
        GetNode<Window>("/root").Theme = FancyUi.BuildTooltipTheme();

        FancyUi.ApplyPanel(GetNode<PanelContainer>("Hud/HudRoot/TopMargin/TopRow/StatusPanel"), main: true);
        FancyUi.ApplyPanel(GetNode<PanelContainer>("Hud/HudRoot/TopMargin/TopRow/CurrencyPanel"), main: false);
        FancyUi.ApplyPanel(GetNode<PanelContainer>("Hud/HudRoot/DetailsOverlay"), main: true);
        StyleActionButton(GetNode<Button>("Hud/HudRoot/BottomMargin/BottomRow/DetailsToggle"), fontSize: 9);
        StyleActionButton(GetNode<Button>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Header/CloseDetails"), fontSize: 9);

        FancyUi.ApplyTabs(_screens);
        _screens.SetTabTitle(0, "Túi Đồ");
        _screens.SetTabTitle(1, "Tiến Hóa");
        _screens.SetTabTitle(2, "Hồn Phiên");
        _screens.SetTabTitle(3, "Hồn");
        _screens.SetTabTitle(4, "Hành động");

        foreach (var labelPath in new[] { "Hud/HudRoot/TopMargin/TopRow/StatusPanel/StatusMargin/Status", "Hud/HudRoot/TopMargin/TopRow/CurrencyPanel/CurrencyMargin/Currency", "Hud/HudRoot/BottomMargin/BottomRow/CombatSoulState", "Hud/HudRoot/BottomMargin/BottomRow/MapCaption", "Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Header/Title", "Hud/HudRoot/Toast" })
            GetNode<Label>(labelPath).AddThemeColorOverride("font_color", FancyUi.TextBrush);
        GetNode<Label>("Hud/HudRoot/TopMargin/TopRow/StatusPanel/StatusMargin/Status").AddThemeFontSizeOverride("font_size", 10);
        GetNode<Label>("Hud/HudRoot/TopMargin/TopRow/CurrencyPanel/CurrencyMargin/Currency").AddThemeFontSizeOverride("font_size", 9);
        GetNode<Label>("Hud/HudRoot/BottomMargin/BottomRow/CombatSoulState").AddThemeFontSizeOverride("font_size", 9);
        GetNode<Label>("Hud/HudRoot/BottomMargin/BottomRow/MapCaption").AddThemeFontSizeOverride("font_size", 9);
        GetNode<Label>("Hud/HudRoot/Toast").AddThemeFontSizeOverride("font_size", 10);
        GetNode<Label>("Hud/HudRoot/DetailsOverlay/DetailsMargin/DetailsColumn/Header/Title").AddThemeFontSizeOverride("font_size", 13);

    }

    private static void StyleActionButton(Button button, bool destructive = false, int fontSize = 10)
    {
        FancyUi.ApplyButton(button);
        if (destructive)
        {
            button.AddThemeColorOverride("font_color", new Color("#e08a75"));
            button.AddThemeColorOverride("font_hover_color", new Color("#ffb39c"));
        }
        else
        {
            button.AddThemeColorOverride("font_color", FancyUi.TextBrush);
            button.AddThemeColorOverride("font_hover_color", FancyUi.TextBright);
        }
        button.AddThemeColorOverride("font_pressed_color", FancyUi.TextDim);
        button.AddThemeColorOverride("font_disabled_color", FancyUi.TextDim);
        button.AddThemeFontSizeOverride("font_size", fontSize);
    }

    private static Vector2 ToGodot(SimVec2 value) => new((float)value.X, (float)value.Y);

    private Texture2D? LoadCanonicalAssetTexture(string logicalId)
    {
        return _assetCatalog.TryGet(logicalId, out var asset) ? _assetCatalog.Texture(asset) : null;
    }

    private void RebuildMapTextures()
    {
        _mapTextures.Clear();
        foreach (var assetId in _application.WorldObjects().Select(item => item.AssetId).Distinct(StringComparer.Ordinal))
        {
            if (!_assetCatalog.TryGet(assetId, out var asset)) continue;
            var texture = LoadMapTexture(asset);
            if (texture is not null) _mapTextures[assetId] = texture;
        }
    }

    private Texture2D? LoadMapTexture(CanonicalAssetEntry asset)
    {
        var texture = _assetCatalog.Texture(asset);
        return asset.Frames.Count == 1 ? new AtlasTexture { Atlas = texture, Region = asset.Frames[0].Region, FilterClip = true } : texture;
    }

    private TextureRect? TryBuildCanonicalIcon(string assetId, int size)
    {
        if (!_assetCatalog.TryGet(assetId, out var asset)) return null;
        return new TextureRect { Texture = LoadMapTexture(asset), CustomMinimumSize = new Vector2(size, size), TooltipText = assetId };
    }

    private RegionTravelResult TravelToRegion(string regionId)
    {
        if (GameplayCommandsBlocked) return new RegionTravelResult(false, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: "save_recovery_required");

        // Region travel is a durable canonical transition.  Flush any earlier durable
        // mutation first, then retain an in-memory pre-transition envelope while the
        // post-transition state is written.  A failed disk commit must not strand the
        // live session in a region that is not represented by the current slot.
        if (_application.CanonicalContent is not null && _application.HasCanonicalDurableChanges && !Save(showMessage: false))
            return new RegionTravelResult(false, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: "durable_commit_pending");
        var beforeTravel = _application.CanonicalContent is null
            ? null
            : _application.CaptureCanonicalSave(_v25Store!.NextCommitSequence(SelectedSaveId), SelectedSaveId);
        var result = _application.TravelToRegion(regionId);
        if (!result.Success)
        {
            Toast(result.Failure switch
            {
                RegionTravelFailure.RegionUnavailable => "Chưa thể đến khu vực này.",
                RegionTravelFailure.MapContentUnavailable => "Khu vực chưa có map content.",
                RegionTravelFailure.TravelConditionFailed => $"Chưa đủ điều kiện: {result.FailedConditionId}",
                _ => "Không tìm thấy khu vực."
            });
            return result;
        }

        if (_application.CanonicalContent is not null && !Save(showMessage: false))
        {
            try
            {
                _application.RestoreCanonicalSave(beforeTravel!);
            }
            catch (Exception exception)
            {
                _saveWritesBlocked = true;
                GD.PushError($"Region-transition rollback failed; save files were preserved and writes are blocked: {exception.Message}");
                Toast("Chuyển vùng chưa được lưu và không thể khôi phục runtime; hãy tải lại bản lưu hợp lệ.");
                return result with { Success = false, Failure = RegionTravelFailure.TravelConditionFailed, FailedConditionId = "rollback_required" };
            }
            Toast("Chuyển vùng chưa được lưu; runtime đã quay lại vùng trước. Nhấn F5 để thử lưu lại.");
            return result with { Success = false, Failure = RegionTravelFailure.TravelConditionFailed, FailedConditionId = "durable_commit_pending" };
        }

        foreach (var sprite in _monsterSprites.Values) sprite.QueueFree();
        foreach (var sprite in _allySprites.Values) sprite.QueueFree();
        _monsterSprites.Clear(); _allySprites.Clear(); _dyingMonsters.Clear(); _actorVisualPositions.Clear(); _actorVisualDirections.Clear();
        RebuildMapTextures();
        _ground = LoadCanonicalAssetTexture(_application.CurrentMapBackgroundAssetId() ?? "tiles.arena.ground");
        _lastSoulSignature = ""; _lastFeatureSignature = ""; _lastScreenSignature = "";
        Toast($"Đã đến { _application.RegionDetails(regionId)?.DisplayName ?? regionId }.");
        RefreshSnapshot();
        return result;
    }

    private AnimatedSprite2D BuildPlayerSprite(int rank)
    {
        _ = rank;
        var sprite = BuildCanonicalActorSprite("player.base", 20);
        PlayActorAnimation(sprite, PlayerAnimationName("idle", _facing), PlayerAssetId("idle", FacingToDirection(_facing)));
        return sprite;
    }

    private void SyncMonsters(IReadOnlyList<MonsterSnapshot> monsters)
    {
        var alive = monsters.Select(item => item.Uid).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _monsterSprites.Keys.Where(id => !alive.Contains(id) && !_dyingMonsters.ContainsKey(id)).ToArray()) { _monsterSprites[stale].QueueFree(); _monsterSprites.Remove(stale); ForgetActorVisual("enemy", stale); }
        foreach (var monster in monsters)
        {
            if (!_monsterSprites.TryGetValue(monster.Uid, out var sprite)) { sprite = BuildMonsterSprite(monster); _monsterSprites.Add(monster.Uid, sprite); AddChild(sprite); }
            var position = ToGodot(monster.Position); var direction = ResolveVisualDirection("enemy", monster.Uid, position); sprite.Position = position;
            var monsterAction = monster.AiState switch { Simulation.State.MonsterAiState.Chase => "walk", Simulation.State.MonsterAiState.Attack => "attack", Simulation.State.MonsterAiState.Hit => "hit", _ => "idle" };
            var clip = CanonicalClip(monsterAction); PlayActorAnimation(sprite, ActorAnimationName(clip, direction), ActorAssetId(monster.SpeciesId, monster.Rank, "enemy", clip, direction));
        }
        var move = Input.GetVector("move_left", "move_right", "move_up", "move_down"); if (move != Vector2.Zero) _facing = System.Math.Abs(move.X) > System.Math.Abs(move.Y) ? move.X < 0 ? "left" : "right" : move.Y < 0 ? "back" : "front";
        var action = Input.IsActionPressed("attack") ? "attack" : move != Vector2.Zero ? "move" : "idle";
        PlayActorAnimation(_playerSprite, PlayerAnimationName(action, _facing), PlayerAssetId(action, FacingToDirection(_facing)));
    }

    private void SyncAllies(IReadOnlyList<AllySnapshot> allies)
    {
        var alive = allies.Select(item => item.Uid).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _allySprites.Keys.Where(id => !alive.Contains(id)).ToArray()) { _allySprites[stale].QueueFree(); _allySprites.Remove(stale); ForgetActorVisual("ally", stale); }
        foreach (var ally in allies)
        {
            if (!_allySprites.TryGetValue(ally.Uid, out var sprite))
            {
                sprite = BuildMonsterSprite(new MonsterSnapshot(ally.Uid, ally.DefinitionId, ally.SpeciesId, ally.Position, ally.CurrentHp, ally.MaximumHp, true, MonsterAiState.Idle, ally.Level, ally.Rank), "ally");
                sprite.ZIndex = 11; _allySprites.Add(ally.Uid, sprite); AddChild(sprite);
            }
            var position = ToGodot(ally.Position); var direction = ResolveVisualDirection("ally", ally.Uid, position); sprite.Position = position;
            var action = ally.AiState switch { AllyAiState.Chase => "walk", AllyAiState.Attack => "attack", _ => "idle" };
            var clip = CanonicalClip(action); PlayActorAnimation(sprite, ActorAnimationName(clip, direction), ActorAssetId(ally.SpeciesId, ally.Rank, "ally", clip, direction));
        }
    }

    private AnimatedSprite2D BuildMonsterSprite(MonsterSnapshot monster, string representation = "enemy")
    {
        return BuildCanonicalActorSprite($"soul.{monster.SpeciesId}.rank{monster.Rank:D2}.{representation}", representation == "ally" ? 11 : 10);
    }

    private void BeginMonsterDeaths()
    {
        foreach (var defeated in _application.DrainDefeatedMonsterVisuals()) if (_monsterSprites.TryGetValue(defeated.Uid, out var sprite))
        {
            var position = ToGodot(defeated.Position); var direction = ResolveVisualDirection("enemy", defeated.Uid, position); sprite.Position = position;
            PlayActorAnimation(sprite, ActorAnimationName("death", direction), ActorAssetId(defeated.SpeciesId, defeated.Rank, "enemy", "death", direction));
            _dyingMonsters[defeated.Uid] = defeated.DurationSeconds;
        }
    }

    private void UpdateMonsterDeaths(double delta)
    {
        foreach (var entry in _dyingMonsters.ToArray())
        {
            var remaining = entry.Value - delta; if (remaining > 0) { _dyingMonsters[entry.Key] = remaining; continue; }
            if (_monsterSprites.Remove(entry.Key, out var sprite)) sprite.QueueFree(); ForgetActorVisual("enemy", entry.Key); _dyingMonsters.Remove(entry.Key);
        }
    }

    private static string ActorAssetId(string speciesId, int rank, string representation, string clip, string direction) => $"soul.{speciesId}.rank{rank:D2}.{representation}.{clip}.{direction}";
    private static string PlayerAssetId(string clip, string direction) => $"player.base.{clip}.{direction}";
    private static string SoulPickupAssetId(string speciesId, int rank) => $"soul.{speciesId}.rank{rank:D2}.pickup";

    private AnimatedSprite2D BuildCanonicalActorSprite(string assetIdPrefix, int missingZIndex)
    {
        var animations = _assetCatalog.Assets.Values
            .Where(asset => asset.AssetId.StartsWith(assetIdPrefix + ".", StringComparison.Ordinal) && asset.Direction is "s" or "w" or "e" or "n" && asset.Clip is "idle" or "move" or "attack" or "hit" or "death" or "disperse" or "summon" or "recall")
            .ToDictionary(asset => ActorAnimationName(asset.Clip, asset.Direction), asset => asset, StringComparer.Ordinal);
        if (animations.Count == 0) return BuildMissingActorSprite(missingZIndex);
        var anchor = animations.Values.First();
        if (animations.Values.Any(asset => asset.FrameSize != anchor.FrameSize || asset.Pivot != anchor.Pivot)) return BuildMissingActorSprite(missingZIndex);
        var frames = _assetCatalog.BuildFrames(animations);
        frames.AddAnimation("missing");
        return new AnimatedSprite2D { SpriteFrames = frames, Centered = false, Offset = -anchor.Pivot, ZIndex = anchor.Layering.ZIndex, YSortEnabled = anchor.Layering.YSortEnabled };
    }

    private static AnimatedSprite2D BuildMissingActorSprite(int zIndex)
    {
        var frames = new SpriteFrames(); frames.RemoveAnimation("default"); frames.AddAnimation("missing");
        return new AnimatedSprite2D { SpriteFrames = frames, ZIndex = zIndex, YSortEnabled = true };
    }

    private static void PlayActorAnimation(AnimatedSprite2D sprite, string requested, string requiredAssetId)
    {
        var frames = sprite.SpriteFrames;
        var resolved = frames.HasAnimation(requested) ? requested : "missing";
        if (resolved == "missing") sprite.SetMeta("trial_missing_asset_id", requiredAssetId);
        else if (sprite.HasMeta("trial_missing_asset_id")) sprite.RemoveMeta("trial_missing_asset_id");
        if (frames.HasAnimation(resolved) && (sprite.Animation != resolved || !sprite.IsPlaying())) sprite.Play(resolved);
    }

    private static string CanonicalClip(string action) => action == "walk" ? "move" : action;
    private static string ActorAnimationName(string clip, string direction) => $"{clip}_{direction}";
    private static string PlayerAnimationName(string clip, string facing) => ActorAnimationName(clip, FacingToDirection(facing));
    private static string FacingToDirection(string facing) => facing switch { "back" => "n", "left" => "w", "right" => "e", _ => "s" };

    // Facing is deliberately transient Presentation state. It is inferred from the snapshot's
    // observable position delta and is never written into Simulation or a save payload.
    private string ResolveVisualDirection(string representation, string uid, Vector2 position)
    {
        var key = representation + ":" + uid;
        if (_actorVisualPositions.TryGetValue(key, out var previous))
        {
            var delta = position - previous;
            if (delta.LengthSquared() > 0.001f)
                _actorVisualDirections[key] = Mathf.Abs(delta.X) > Mathf.Abs(delta.Y) ? delta.X < 0 ? "w" : "e" : delta.Y < 0 ? "n" : "s";
        }
        _actorVisualPositions[key] = position;
        return _actorVisualDirections.TryGetValue(key, out var direction) ? direction : "s";
    }

    private void ForgetActorVisual(string representation, string uid)
    {
        var key = representation + ":" + uid;
        _actorVisualPositions.Remove(key); _actorVisualDirections.Remove(key);
    }

    private static string? MissingAssetId(AnimatedSprite2D sprite) => sprite.HasMeta("trial_missing_asset_id") ? sprite.GetMeta("trial_missing_asset_id").AsString() : null;

    // These presentation sizes all begin from the source 64×64 object canvas.
    // They establish a legible Ash Graves hierarchy without touching any sprite
    // pixels, gameplay positions, collision footprints, or camera scale.
    private static float WorldObjectVisualScale(string type) => type switch
    {
        "debris" => 0.50f,
        "rock" => 0.56f,
        "chest" => 0.62f,
        "pillar" => 0.78f,
        "npc" => 0.88f,
        "shrine" => 1.19f,
        "landmark" => 1.25f,
        "tree_or_spire" => 1.28f,
        "portal" => 1.44f,
        "arena" => 1.50f,
        "secret" => 1.09f,
        _ => 0.75f
    };

    private static float WorldObjectMissingRadius(string type) => type switch
    {
        "portal" or "arena" => 22,
        "shrine" or "landmark" or "tree_or_spire" => 18,
        "npc" => 14,
        _ => 12
    };

    private void DrawGroundShadow(Vector2 origin, float visualWidth, float alpha)
    {
        var radius = Mathf.Max(5, visualWidth * 0.28f);
        DrawCircle(origin + new Vector2(0, 1), radius, new Color(0.035f, 0.028f, 0.045f, alpha));
    }

    private void DrawWorldAsset(Texture2D texture, CanonicalAssetEntry asset, Vector2 origin, float scale, Color modulate)
    {
        var size = new Vector2(asset.FrameSize.X * scale, asset.FrameSize.Y * scale);
        DrawGroundShadow(origin, size.X, 0.34f);
        DrawTextureRect(texture, new Rect2(origin - asset.Pivot * scale, size), false, modulate);
    }

    /// <summary>
    /// Adds only visual composition around canonical anchors.  The road network,
    /// region positions and collisions remain owned by the V2.5 layout; the four
    /// marker groups deliberately remain pass-through scenery rather than making
    /// a visual request change navigation.
    /// </summary>
    private void DrawAshGravesVisualComposition(CanonicalLayoutBlueprintDefinition layout)
    {
        if (!_assetCatalog.TryGet("world.ash_graves.pillar", out var pillar) ||
            !_assetCatalog.TryGet("world.ash_graves.rock", out var rock) ||
            !_assetCatalog.TryGet("world.ash_graves.debris", out var debris)) return;

        var shrine = ToGodot(V25WorldLayout.At(layout, layout.ShrineTile));
        var landmark = ToGodot(V25WorldLayout.At(layout, layout.LandmarkTile, "Field"));
        var exit = ToGodot(V25WorldLayout.At(layout, layout.ExitTile));
        DrawAshGravesFocalClearing(shrine, new Vector2(160, 128));
        DrawAshGravesFocalClearing(landmark, new Vector2(192, 160));
        DrawAshGravesFocalClearing(exit, new Vector2(176, 144));

        // These off-path positions frame the initial Shrine rather than replacing
        // any missing NPC or gate. They use the exact trial assets and are not
        // interaction targets or collision producers.
        var campDressing = new (CanonicalAssetEntry Asset, Vector2 Offset, float Scale)[]
        {
            (pillar, new Vector2(128, -160), 0.78f),
            (rock, new Vector2(256, -160), 0.56f),
            (debris, new Vector2(160, 160), 0.50f),
            (pillar, new Vector2(288, 160), 0.78f),
        };
        foreach (var dressing in campDressing)
            DrawWorldAsset(MapTexture(dressing.Asset), dressing.Asset, shrine + dressing.Offset, dressing.Scale, new Color(0.92f, 0.92f, 0.98f, 0.9f));
    }

    private void DrawAshGravesFocalClearing(Vector2 center, Vector2 size)
    {
        var bounds = new Rect2(center - size * 0.5f, size);
        DrawRect(bounds, new Color(0.06f, 0.045f, 0.065f, 0.18f));
        DrawRect(bounds, new Color(0.45f, 0.36f, 0.42f, 0.16f), false, 2);
    }

    private Texture2D MapTexture(CanonicalAssetEntry asset)
    {
        if (_mapTextures.TryGetValue(asset.AssetId, out var texture)) return texture;
        texture = LoadMapTexture(asset) ?? throw new InvalidOperationException($"Cannot load map texture '{asset.AssetId}'.");
        _mapTextures.Add(asset.AssetId, texture);
        return texture;
    }

    /// <summary>
    /// The source package defines mask00..mask15 as NESW Wang bits. This renderer derives those
    /// bits only from the already-authoritative visual layout (roads, Ruins chunk, outer wall),
    /// never from collision or gameplay state. It draws only the camera-local tile neighborhood.
    /// </summary>
    private bool DrawAshGravesTrialTiles()
    {
        if (_snapshot?.CurrentRegionId != "ash_graves" || _application.CanonicalContent is null || !_assetCatalog.TryGet("world.ash_graves.ground.mask15", out var ground)) return false;
        var layout = _application.CanonicalContent.Content.LayoutBlueprint;
        var tileSize = layout.TileSize;
        if (tileSize != BaseTileSize || tileSize != ground.FrameSize.X || tileSize != ground.FrameSize.Y) return false;
        var player = ToGodot(_snapshot.Player.Position);
        var maxTileX = Math.Max(0, (int)Math.Ceiling(_snapshot.World.Width / tileSize));
        var maxTileY = Math.Max(0, (int)Math.Ceiling(_snapshot.World.Height / tileSize));
        var minX = Math.Max(0, (int)MathF.Floor((player.X - 720) / tileSize)); var maxX = Math.Min(maxTileX - 1, (int)MathF.Ceiling((player.X + 720) / tileSize));
        var minY = Math.Max(0, (int)MathF.Floor((player.Y - 480) / tileSize)); var maxY = Math.Min(maxTileY - 1, (int)MathF.Ceiling((player.Y + 480) / tileSize));
        if (maxX < minX || maxY < minY) return false;

        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
            DrawTrialTile(ground, x, y, tileSize);

        bool Road(int x, int y) => IsRoadTile(x, y, tileSize);
        bool Ruin(int x, int y) => IsRuinTile(layout, x, y);
        bool Wall(int x, int y) => x < layout.OuterWallThicknessTiles || y < layout.OuterWallThicknessTiles || x >= maxTileX - layout.OuterWallThicknessTiles || y >= maxTileY - layout.OuterWallThicknessTiles;
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            if (Ruin(x, y) && TryGetMaskAsset("ruin", WangMask(Ruin, x, y), out var ruin)) DrawTrialTile(ruin, x, y, tileSize);
            if (Road(x, y) && TryGetMaskAsset("path", WangMask(Road, x, y), out var path)) DrawTrialTile(path, x, y, tileSize);
            if (Wall(x, y) && TryGetMaskAsset("wall", WangMask(Wall, x, y), out var wall)) DrawTrialTile(wall, x, y, tileSize);
        }
        DrawTrialWallCap("nw", 0, 0, minX, maxX, minY, maxY, tileSize);
        DrawTrialWallCap("ne", maxTileX - 1, 0, minX, maxX, minY, maxY, tileSize);
        DrawTrialWallCap("se", maxTileX - 1, maxTileY - 1, minX, maxX, minY, maxY, tileSize);
        DrawTrialWallCap("sw", 0, maxTileY - 1, minX, maxX, minY, maxY, tileSize);
        return true;
    }

    private bool TryGetMaskAsset(string family, int mask, out CanonicalAssetEntry asset) => _assetCatalog.TryGet($"world.ash_graves.{family}.mask{mask:D2}", out asset!);
    private void DrawTrialWallCap(string direction, int x, int y, int minX, int maxX, int minY, int maxY, int tileSize)
    {
        if (x >= minX && x <= maxX && y >= minY && y <= maxY && _assetCatalog.TryGet($"world.ash_graves.wall.cap.{direction}", out var cap)) DrawTrialTile(cap, x, y, tileSize);
    }
    private void DrawTrialTile(CanonicalAssetEntry asset, int x, int y, int tileSize)
    {
        if (!_trialTileTextures.TryGetValue(asset.AssetId, out var texture)) { texture = LoadMapTexture(asset)!; _trialTileTextures.Add(asset.AssetId, texture); }
        DrawTextureRect(texture, new Rect2(x * tileSize, y * tileSize, tileSize, tileSize), false);
    }
    private bool IsRoadTile(int tileX, int tileY, int tileSize)
    {
        var center = new Vector2((tileX + 0.5f) * tileSize, (tileY + 0.5f) * tileSize);
        return _application.CanonicalRoads().Any(road => DistanceToSegment(center, ToGodot(road.Start), ToGodot(road.End)) <= road.Width * 0.5);
    }
    private static bool IsRuinTile(CanonicalLayoutBlueprintDefinition layout, int x, int y)
    {
        if (!layout.ChunkGrid.TryGetValue("Ruins", out var chunk)) return false;
        return x >= chunk[0] * layout.ChunkTiles[0] && x < (chunk[0] + 1) * layout.ChunkTiles[0] && y >= chunk[1] * layout.ChunkTiles[1] && y < (chunk[1] + 1) * layout.ChunkTiles[1];
    }
    private static int WangMask(Func<int, int, bool> occupied, int x, int y) => (occupied(x, y - 1) ? 1 : 0) | (occupied(x + 1, y) ? 2 : 0) | (occupied(x, y + 1) ? 4 : 0) | (occupied(x - 1, y) ? 8 : 0);
    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var delta = end - start; var lengthSquared = delta.LengthSquared();
        return point.DistanceTo(start + delta * (lengthSquared == 0 ? 0 : Mathf.Clamp((point - start).Dot(delta) / lengthSquared, 0, 1)));
    }

    private void DrawCanonicalFrame(CanonicalAssetEntry asset, Vector2 origin)
    {
        var texture = _assetCatalog.Texture(asset); var frame = asset.Frames[0];
        var atlas = new AtlasTexture { Atlas = texture, Region = frame.Region, FilterClip = true };
        DrawTextureRect(atlas, new Rect2(origin - asset.Pivot, asset.FrameSize), false);
    }

    private void DrawMissingAssetMarker(Vector2 origin, string assetId, float radius)
    {
        var rect = new Rect2(origin - new Vector2(radius, radius), new Vector2(radius * 2, radius * 2));
        DrawRect(rect, new Color("#ff00ff"), false, 2); DrawLine(rect.Position, rect.End, new Color("#ff00ff"), 2); DrawLine(new Vector2(rect.End.X, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y), new Color("#ff00ff"), 2);
        if (ShowPresentationDebug) DrawString(ThemeDB.FallbackFont, origin + new Vector2(-radius, -radius - 3), $"MISSING: {assetId}", fontSize: 9, modulate: new Color("#ffd4ff"));
    }
}
