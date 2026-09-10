using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.Systems.V25;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Application.Persistence.V25;
using SimVec2 = SoloVsMortal.Core.Math.Vec2;

namespace SoloVsMortal.Presentation;

/// <summary>Thin Godot adapter: input in, snapshots out. Simulation remains the gameplay owner.</summary>
public partial class Arena : Node2D
{
    private const string SavePath = "user://solo_vs_mortal_save_v25.json";
    private const string LegacySavePath = "user://solo_vs_mortal_save_v6.json";
    private static readonly bool ShowMapCollisionDebug = false;
    private GameApplication _application = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _currency = null!;
    private Label _toast = null!;
    private VBoxContainer _soulList = null!;
    private VBoxContainer _featureList = null!;
    private TabContainer _screens = null!;
    private WorldMapUI _worldMap = null!;
    private HudMinimap _minimap = null!;
    private double _autosaveRemaining = 10;
    private double _toastRemaining;
    private bool _saveWritesBlocked;
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
    private readonly Dictionary<string, double> _dyingMonsters = new(StringComparer.Ordinal);
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
        _camera = GetNode<Camera2D>("Camera2D"); _status = GetNode<Label>("Hud/Panel/Status"); _currency = GetNode<Label>("Hud/CurrencyPanel/Currency"); _toast = GetNode<Label>("Hud/Toast"); _soulList = GetNode<VBoxContainer>("Hud/SoulPanel/List"); _featureList = GetNode<VBoxContainer>("Hud/FeaturePanel/Content"); _screens = GetNode<TabContainer>("Hud/Screens");
        ApplyHudVisualDesign();
        _application = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs"));
        _assetCatalog = CanonicalAssetCatalog.Load(ProjectSettings.GlobalizePath("res://"), ProjectSettings.GlobalizePath("res://data/v2.5/asset-catalog.v2.5.json"));
        _v25Store = CreateSaveStore(_selectedSaveSlot);
        _application.Start();
        _worldMap = new WorldMapUI(); AddChild(_worldMap); _worldMap.Initialize(_application, TravelToRegion);
        _minimap = new HudMinimap { Position = new Vector2(1060, 24), Size = new Vector2(184, 164) }; GetNode<CanvasLayer>("Hud").AddChild(_minimap);
        _ground = LoadCanonicalAssetTexture(_application.CurrentMapBackgroundAssetId() ?? "tiles.arena.ground"); RebuildMapTextures();
        _visualRank = 1; _playerSprite = BuildPlayerSprite(_visualRank); AddChild(_playerSprite);
        _bossTelegraphs = new BossTelegraphLayer { ZIndex = 15 }; AddChild(_bossTelegraphs);
        var restoredSave = TryLoad(showMessage: false);
        if (!restoredSave && !_saveWritesBlocked && _application.Snapshot().Monsters.Count == 0) _application.SpawnMonster("mon_skeleton", 1, new SimVec2(650, 280));
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
        foreach (var road in _application.CanonicalRoads()) DrawLine(ToGodot(road.Start), ToGodot(road.End), new Color("#756755"), (float)road.Width);
        foreach (var area in _application.CanonicalTerrain()) DrawRect(new Rect2((float)area.Bounds.X, (float)area.Bounds.Y, (float)area.Bounds.Width, (float)area.Bounds.Height), area.Terrain switch { V25TerrainTag.FireField => new Color("#9d472b"), V25TerrainTag.ToxicPool => new Color("#526a39"), V25TerrainTag.FrostFloor => new Color("#819ca6"), V25TerrainTag.Gap => new Color("#201d26"), _ => new Color("#4b5363") });
        foreach (var obj in _application.WorldObjects().Where(item => !item.Destroyed).OrderBy(item => item.ZIndex).ThenBy(item => item.Position.Y))
        {
            var p = ToGodot(obj.Position);
            if (obj.Type == "wall") continue;
            if (obj.Type is "npc" or "shrine" or "chest" or "landmark" or "portal" or "secret") DrawString(ThemeDB.FallbackFont, p + new Vector2(-24, -20), obj.Id, fontSize: 12);
            var scale = (float)System.Math.Clamp(obj.PresentationScale, 0.1, 3); if (_mapTextures.TryGetValue(obj.AssetId, out var texture)) { var half = 42 * scale; DrawTextureRect(texture, new Rect2(p.X - half, p.Y - half, half * 2, half * 2), false, new Color(1, 1, 1, 0.92f)); } else DrawMissingAssetMarker(p, obj.AssetId, (obj.Type == "tree" ? 18 : 12) * scale);
        }
        if (ShowMapCollisionDebug)
            foreach (var rect in _snapshot.World.BlockingRects) DrawRect(new Rect2((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height), new Color(0.25f, 0.16f, 0.08f, 0.32f), false, 2);
        foreach (var soul in _snapshot.WorldSouls)
        {
            var p = ToGodot(soul.Position); var assetId = SoulPickupAssetId(soul.OriginSpeciesId, soul.OriginRank);
            if (_assetCatalog.TryGet(assetId, out var asset)) DrawCanonicalFrame(asset, p);
            else DrawMissingAssetMarker(p, assetId, 10);
        }
        if (!HasCanonicalPlayerVisual()) DrawMissingAssetMarker(ToGodot(_snapshot.Player.Position), "player.base.idle.s", 12);
        foreach (var monster in _snapshot.Monsters)
        {
            var p = ToGodot(monster.Position);
            if (!HasCanonicalActorVisual(monster.SpeciesId, monster.Rank, "enemy")) { DrawMissingAssetMarker(p, ActorAssetId(monster.SpeciesId, monster.Rank, "enemy"), 18); DrawString(ThemeDB.FallbackFont, p + new Vector2(-24, -36), _application.SpeciesDisplayName(monster.SpeciesId), fontSize: 12); }
            DrawRect(new Rect2(p.X - 22, p.Y - 31, 44, 5), new Color("#3f0d0d"));
            DrawRect(new Rect2(p.X - 22, p.Y - 31, (float)(44 * monster.CurrentHp / monster.MaximumHp), 5), new Color("#22c55e"));
        }
        foreach (var ally in _snapshot.Allies)
        {
            var p = ToGodot(ally.Position);
            if (!HasCanonicalActorVisual(ally.SpeciesId, ally.Rank, "ally")) { DrawMissingAssetMarker(p, ActorAssetId(ally.SpeciesId, ally.Rank, "ally"), 18); DrawString(ThemeDB.FallbackFont, p + new Vector2(-24, -36), _application.SpeciesDisplayName(ally.SpeciesId), fontSize: 12); }
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
        _status.Text = $"Khu vực: {regionName}\nSinh lực: {System.Math.Ceiling(player.CurrentHp)}/{player.MaximumHp}  •  Linh lực: {System.Math.Ceiling(player.CurrentSpirit)}/{player.MaximumSpirit}\nCấp: {player.Level}  •  Cảnh giới: {player.Rank}  •  Công: {player.Attack}  •  Thủ: {player.Defense}\nQuái: {_snapshot.Monsters.Count}  •  Hồn gần bản đồ: {_snapshot.WorldSouls.Count}  •  Hồn sở hữu: {_snapshot.OwnedSouls.Count}";
        _currency.Text = _application.CanonicalInventory() is { } canonicalInventory
            ? $"Coin {canonicalInventory.Coins}     ◆ XP {player.Xp}     ◈ Soul {_snapshot.OwnedSouls.Count}"
            : $"✦ {_snapshot.Inventory.Sum(item => item.Count)}     ◆ {player.Xp}     ◈ {_snapshot.OwnedSouls.Count}";
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
                    _application.RestoreSaveJson(legacyJson);
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
        var inventory = GetNode<VBoxContainer>("Hud/Screens/Inventory"); var progression = GetNode<VBoxContainer>("Hud/Screens/Progression"); var bannerPage = GetNode<VBoxContainer>("Hud/Screens/Banner");
        foreach (var page in new[] { inventory, progression, bannerPage }) foreach (var child in page.GetChildren()) child.QueueFree();

        inventory.AddChild(new Label { Text = "Túi Đồ", ThemeTypeVariation = "HeaderMedium" });
        var items = _application.Inventory();
        if (_application.CanonicalContent is not null && _application.CanonicalInventory() is { } canonicalInventory)
        {
            inventory.AddChild(new Label { Text = $"Coin: {canonicalInventory.Coins} · Mang theo {canonicalInventory.Items.Count}/60 · Stash {canonicalInventory.Overflow.Count}" });
            inventory.AddChild(new Label { Text = canonicalInventory.Equipped.Count == 0 ? "Trang bị: trống" : "Trang bị: " + string.Join(", ", canonicalInventory.Equipped.Select(item => $"{item.Slot}={item.DefinitionId}")) });
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
            var slot = new Button { Text = item is null ? "" : $"{item.Value.StableId}\n×{item.Value.Count}", TooltipText = item is null ? "Ô trống" : item.Value.StableId, CustomMinimumSize = new Vector2(94, 48) };
            FancyUi.ApplyItemSlot(slot); grid.AddChild(slot);
        }
        var inventoryFooter = new HBoxContainer(); inventoryFooter.AddChild(new Label { Text = _application.CanonicalContent is null ? $"Số ô: {items.Count}/100" : $"Tổng stack hiển thị: {items.Count}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        inventory.AddChild(inventoryFooter);

        progression.AddChild(new Label { Text = "Tiến Hóa Nhân Vật", ThemeTypeVariation = "HeaderMedium" });
        progression.AddChild(new Label { Text = $"Cấp {snapshot.Player.Level}  •  XP {snapshot.Player.Xp}\nCảnh giới {snapshot.Player.Rank}\nCông {snapshot.Player.Attack}  •  Thủ {snapshot.Player.Defense}", SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var progressionActions = new HBoxContainer();
        if (_application.CanonicalContent?.ActiveProfileId == "beta_01")
        {
            var upgrade = new Button { Text = "Mở Full 01 (tại shrine)" }; StyleActionButton(upgrade);
            upgrade.Pressed += () => { if (GameplayCommandsBlocked || !Save(showMessage: false)) return; if (!_application.UpgradeCanonicalProfile()) { Toast("Cần ở shrine, ngoài giao tranh."); return; } if (Save(showMessage: false)) { Toast("Đã nâng profile Full 01, giữ tiến trình cũ."); RefreshSnapshot(); } };
            progressionActions.AddChild(upgrade);
        }
        var breakthrough = new Button { Text = "Thử phá cảnh", CustomMinimumSize = new Vector2(180, 32) }; StyleActionButton(breakthrough); breakthrough.Pressed += () => { if (GameplayCommandsBlocked) return; var succeeded = _application.AttemptPlayerBreakthrough(); var durable = !succeeded || Save(showMessage: false); Toast(succeeded && durable ? "Phá cảnh thành công." : succeeded ? "Phá cảnh đang chờ lưu bền vững." : "Chưa đủ điều kiện phá cảnh."); if (durable) RefreshSnapshot(); }; progressionActions.AddChild(breakthrough); progression.AddChild(progressionActions);

        var banner = snapshot.SoulBanners.FirstOrDefault();
        if (_application.CanonicalContent is not null)
        {
            bannerPage.AddChild(new Label { Text = $"Hồn Phiên canonical · Rank {_application.CanonicalBannerRank}\nMỗi Species tối đa một Soul; không dùng bind/slot prototype.", ThemeTypeVariation = "HeaderMedium" });
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

    private static string inventorySignature(IReadOnlyList<InventoryItem> items) => string.Join(';', items.Select(item => $"{item.StableId}:{item.Count}"));

    private void RebuildFeaturePanel(GameSnapshot snapshot)
    {
        foreach (var child in _featureList.GetChildren()) child.QueueFree();
        if (_application.CanonicalContent is not null)
        {
            _featureList.AddChild(new Label { Text = $"Hồn Phiên canonical · Rank {_application.CanonicalBannerRank}\nSpecies Soul: {snapshot.OwnedSouls.Count} · Mỗi species một Ally", ThemeTypeVariation = "HeaderMedium" });
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
            var link = _application.SoulLinks().First(item => item.SoulId == soul.Id); var runtime = _application.SoulRuntime(soul.Id); var row = new HBoxContainer(); row.AddChild(new Label { Text = $"{soul.DisplayName}: {link.State} · Tải {link.SoulCost} · Ổn định {link.Stability:P0}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var summon = new Button { Text = runtime.Status == SoulRuntimeStatus.Summoned ? "Thu hồi" : "Triệu hồi", Disabled = runtime.Status is SoulRuntimeStatus.Dispersed or SoulRuntimeStatus.Possessed };
            StyleActionButton(summon);
            summon.Pressed += () => { if (GameplayCommandsBlocked) return; if (runtime.Status == SoulRuntimeStatus.Summoned) _application.UnsummonSoul(soul.Id); else _application.SummonSoul(soul.Id, banner!.Id, new SimVec2(_snapshot!.Player.Position.X + 36, _snapshot.Player.Position.Y)); RefreshSnapshot(); }; row.AddChild(summon);
            var possess = new Button { Text = "Phụ hồn", Disabled = runtime.Status != SoulRuntimeStatus.Ready || _application.ActivePossessionSoulId is not null }; StyleActionButton(possess); possess.Pressed += () => { if (GameplayCommandsBlocked) return; var result = _application.StartPossession(soul.Id); Toast(result.Success ? "Đã bắt đầu phụ hồn." : "Không thể phụ hồn Soul này."); RefreshSnapshot(); }; row.AddChild(possess); _featureList.AddChild(row);
        }
    }
    private void ApplyHudVisualDesign()
    {
        GetNode<Window>("/root").Theme = FancyUi.BuildTooltipTheme();

        FancyUi.ApplyPanel(GetNode<Panel>("Hud/Panel"), main: true);
        FancyUi.ApplyPanel(GetNode<Panel>("Hud/CurrencyPanel"), main: false);
        FancyUi.ApplyPanel(GetNode<Panel>("Hud/SoulPanel"), main: false);
        FancyUi.ApplyPanel(GetNode<Panel>("Hud/FeaturePanel"), main: true);

        FancyUi.ApplyTabs(_screens);
        _screens.SetTabTitle(0, "Túi Đồ");
        _screens.SetTabTitle(1, "Tiến Hóa");
        _screens.SetTabTitle(2, "Hồn Phiên");

        foreach (var labelPath in new[] { "Hud/Panel/Status", "Hud/CurrencyPanel/Currency", "Hud/MapCaption", "Hud/SoulPanel/Title", "Hud/Help", "Hud/Toast" })
            GetNode<Label>(labelPath).AddThemeColorOverride("font_color", FancyUi.TextBrush);
        GetNode<Label>("Hud/SoulPanel/Title").AddThemeFontSizeOverride("font_size", 18);
        GetNode<Label>("Hud/CurrencyPanel/Currency").AddThemeFontSizeOverride("font_size", 17);

    }

    private static void StyleActionButton(Button button, bool destructive = false)
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
        _monsterSprites.Clear(); _allySprites.Clear(); _dyingMonsters.Clear();
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
        return BuildMissingActorSprite(20);
    }

    private void SyncMonsters(IReadOnlyList<MonsterSnapshot> monsters)
    {
        var alive = monsters.Select(item => item.Uid).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _monsterSprites.Keys.Where(id => !alive.Contains(id) && !_dyingMonsters.ContainsKey(id)).ToArray()) { _monsterSprites[stale].QueueFree(); _monsterSprites.Remove(stale); }
        foreach (var monster in monsters)
        {
            if (!_monsterSprites.TryGetValue(monster.Uid, out var sprite)) { sprite = BuildMonsterSprite(monster); _monsterSprites.Add(monster.Uid, sprite); AddChild(sprite); }
            sprite.Position = ToGodot(monster.Position);
            var monsterAction = monster.AiState switch { Simulation.State.MonsterAiState.Chase => "walk", Simulation.State.MonsterAiState.Attack => "attack", Simulation.State.MonsterAiState.Hit => "hit", _ => "idle" };
            PlayActorAnimation(sprite, monsterAction);
        }
        var move = Input.GetVector("move_left", "move_right", "move_up", "move_down"); if (move != Vector2.Zero) _facing = System.Math.Abs(move.X) > System.Math.Abs(move.Y) ? move.X < 0 ? "left" : "right" : move.Y < 0 ? "back" : "front";
        var action = Input.IsActionPressed("attack") ? "attack" : move != Vector2.Zero ? "walk" : "idle"; var desired = $"{action}_{_facing}";
        PlayActorAnimation(_playerSprite, desired);
    }

    private void SyncAllies(IReadOnlyList<AllySnapshot> allies)
    {
        var alive = allies.Select(item => item.Uid).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _allySprites.Keys.Where(id => !alive.Contains(id)).ToArray()) { _allySprites[stale].QueueFree(); _allySprites.Remove(stale); }
        foreach (var ally in allies)
        {
            if (!_allySprites.TryGetValue(ally.Uid, out var sprite))
            {
                sprite = BuildMonsterSprite(new MonsterSnapshot(ally.Uid, ally.DefinitionId, ally.SpeciesId, ally.Position, ally.CurrentHp, ally.MaximumHp, true, MonsterAiState.Idle, ally.Level, ally.Rank), "ally");
                sprite.Modulate = new Color("#86efac"); sprite.ZIndex = 11; _allySprites.Add(ally.Uid, sprite); AddChild(sprite);
            }
            sprite.Position = ToGodot(ally.Position);
            var action = ally.AiState switch { AllyAiState.Chase => "walk", AllyAiState.Attack => "attack", _ => "idle" };
            PlayActorAnimation(sprite, action);
        }
    }

    private AnimatedSprite2D BuildMonsterSprite(MonsterSnapshot monster, string representation = "enemy")
    {
        return _assetCatalog.TryGet(ActorAssetId(monster.SpeciesId, monster.Rank, representation), out var asset)
            ? BuildCanonicalStaticSprite(asset)
            : BuildMissingActorSprite(representation == "ally" ? 11 : 10);
    }

    private void BeginMonsterDeaths()
    {
        foreach (var defeated in _application.DrainDefeatedMonsterVisuals()) if (_monsterSprites.TryGetValue(defeated.Uid, out var sprite)) { sprite.Position = ToGodot(defeated.Position); PlayActorAnimation(sprite, "death"); _dyingMonsters[defeated.Uid] = defeated.DurationSeconds; }
    }

    private void UpdateMonsterDeaths(double delta)
    {
        foreach (var entry in _dyingMonsters.ToArray())
        {
            var remaining = entry.Value - delta; if (remaining > 0) { _dyingMonsters[entry.Key] = remaining; continue; }
            if (_monsterSprites.Remove(entry.Key, out var sprite)) sprite.QueueFree(); _dyingMonsters.Remove(entry.Key);
        }
    }

    private static string ActorAssetId(string speciesId, int rank, string representation) => $"soul.{speciesId}.rank{rank:D2}.{representation}.south";
    private static string SoulPickupAssetId(string speciesId, int rank) => $"soul.{speciesId}.rank{rank:D2}.pickup";
    private bool HasCanonicalPlayerVisual() => _assetCatalog.TryGet("player.base.idle.s", out _);
    private bool HasCanonicalActorVisual(string speciesId, int rank, string representation) => _assetCatalog.TryGet(ActorAssetId(speciesId, rank, representation), out _);

    private AnimatedSprite2D BuildCanonicalStaticSprite(CanonicalAssetEntry asset)
    {
        var sprite = new AnimatedSprite2D { SpriteFrames = _assetCatalog.BuildFrames(asset), Centered = false, Offset = -asset.Pivot, ZIndex = asset.Layering.ZIndex, YSortEnabled = asset.Layering.YSortEnabled };
        sprite.Play("static");
        return sprite;
    }

    private static AnimatedSprite2D BuildMissingActorSprite(int zIndex)
    {
        var frames = new SpriteFrames(); frames.RemoveAnimation("default"); frames.AddAnimation("missing");
        return new AnimatedSprite2D { SpriteFrames = frames, ZIndex = zIndex, YSortEnabled = true };
    }

    private static void PlayActorAnimation(AnimatedSprite2D sprite, string requested)
    {
        var frames = sprite.SpriteFrames;
        var resolved = frames.HasAnimation("static") ? "static" : frames.HasAnimation(requested) ? requested : "missing";
        if (frames.HasAnimation(resolved) && (sprite.Animation != resolved || !sprite.IsPlaying())) sprite.Play(resolved);
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
        DrawString(ThemeDB.FallbackFont, origin + new Vector2(-radius, -radius - 3), $"MISSING: {assetId}", fontSize: 9, modulate: new Color("#ffd4ff"));
    }
}
