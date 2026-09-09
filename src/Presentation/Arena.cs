using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Simulation.Systems.V25;
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
    private Texture2D? _ground;
    private Texture2D? _soulTexture;
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

    private string SelectedSaveId => _selectedSaveSlot == 1 ? "slot.primary" : $"slot.{_selectedSaveSlot}";

    public override void _Ready()
    {
        _camera = GetNode<Camera2D>("Camera2D"); _status = GetNode<Label>("Hud/Panel/Status"); _currency = GetNode<Label>("Hud/CurrencyPanel/Currency"); _toast = GetNode<Label>("Hud/Toast"); _soulList = GetNode<VBoxContainer>("Hud/SoulPanel/List"); _featureList = GetNode<VBoxContainer>("Hud/FeaturePanel/Content"); _screens = GetNode<TabContainer>("Hud/Screens");
        ApplyHudVisualDesign();
        _application = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs"));
        _v25Store = CreateSaveStore(_selectedSaveSlot);
        _application.Start();
        _worldMap = new WorldMapUI(); AddChild(_worldMap); _worldMap.Initialize(_application, TravelToRegion);
        _minimap = new HudMinimap { Position = new Vector2(1060, 24), Size = new Vector2(184, 164) }; GetNode<CanvasLayer>("Hud").AddChild(_minimap);
        _ground = LoadAssetTexture(_application.CurrentMapBackgroundAssetId() ?? "tiles.arena.ground"); _soulTexture = LoadAssetTexture("soul.orb.no_boc"); RebuildMapTextures();
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
        _application.SetInput(new SimVec2(move.X, move.Y), Input.IsActionPressed("attack"), new SimVec2(mouse.X, mouse.Y), Input.IsActionJustPressed("dodge"));
        _application.Tick(delta);
        // Auto-collect is a gameplay transaction too. Do not let it wait for the ten-second
        // autosave window: commit its exact post-capture payload before any reward event/UI.
        if (_application.HasCanonicalDurableChanges && !Save(showMessage: false)) return;
        BeginMonsterDeaths(); UpdateMonsterDeaths(delta);
        if (Input.IsActionJustPressed("acquire_soul"))
        {
            var interaction = _application.InteractNearestCanonical();
            if (interaction is not null)
            {
                var committed = !_application.HasCanonicalDurableChanges || Save(showMessage: false);
                Toast(committed ? interaction : "Tương tác đang chờ lưu bền vững.");
                if (committed) { RebuildMapTextures(); RefreshSnapshot(); }
            }
            var count = interaction is null ? _application.AcquireNearbySouls().Count : 0;
            // Canonical pickup ownership, Density, Sync and consumed-pickup identity are one
            // transaction. Commit before acknowledging the pickup so a disk failure cannot be
            // reported as a completed reward; Save retains the exact staged payload for retry.
            var durable = count == 0 || _application.CanonicalContent is null || Save(showMessage: false);
            if (interaction is null) Toast(count == 0 ? "Không có Hồn ở gần." : durable ? $"Đã thu {count} Hồn." : "Thu Hồn đang chờ lưu bền vững; hãy thử lưu lại.");
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
            var scale = (float)System.Math.Clamp(obj.PresentationScale, 0.1, 3); if (_mapTextures.TryGetValue(obj.AssetId, out var texture)) { var half = 42 * scale; DrawTextureRect(texture, new Rect2(p.X - half, p.Y - half, half * 2, half * 2), false, new Color(1, 1, 1, 0.92f)); } else { var color = obj.Type switch { "tree" => new Color("#166534"), "building" => new Color("#713f12"), "fragileWall" => new Color("#64748b"), "portal" => new Color("#38bdf8"), _ => new Color("#854d0e") }; DrawCircle(p, (obj.Type == "tree" ? 18 : 12) * scale, color); }
        }
        if (ShowMapCollisionDebug)
            foreach (var rect in _snapshot.World.BlockingRects) DrawRect(new Rect2((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height), new Color(0.25f, 0.16f, 0.08f, 0.32f), false, 2);
        foreach (var soul in _snapshot.WorldSouls) { var p = ToGodot(soul.Position); if (_soulTexture is not null) DrawTextureRect(_soulTexture, new Rect2(p.X - 16, p.Y - 16, 32, 32), false); else DrawCircle(p, 10, new Color("#67e8f9")); }
        foreach (var monster in _snapshot.Monsters)
        {
            var p = ToGodot(monster.Position);
            DrawRect(new Rect2(p.X - 22, p.Y - 31, 44, 5), new Color("#3f0d0d"));
            DrawRect(new Rect2(p.X - 22, p.Y - 31, (float)(44 * monster.CurrentHp / monster.MaximumHp), 5), new Color("#22c55e"));
        }
        foreach (var ally in _snapshot.Allies)
        {
            var p = ToGodot(ally.Position);
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
        _minimap.Refresh(_snapshot, _application.WorldObjects());
        var runtime = _application.CanonicalRuntimeSnapshot();
        _bossTelegraphs.Refresh(runtime.Casts, runtime.Actors ?? Array.Empty<V25ActorRuntimeSnapshot>());
        var signature = string.Join('|', _snapshot.OwnedSouls.Select(soul => $"{soul.Id}:{soul.Level}:{soul.Xp}")); if (signature != _lastSoulSignature) { RebuildSoulPanel(_snapshot); _lastSoulSignature = signature; }
        var questSignature = string.Join(';', _application.CanonicalQuests().Select(item => $"{item.QuestId}:{item.Status}:{item.ObjectiveProgress}"));
        var featureSignature = $"{_snapshot.OwnedSouls.Count}:{_snapshot.SoulBanners.Sum(item => item.BoundSoulIds.Count)}:{_application.Inventory().Count}:{_application.SoulRuntime(_snapshot.OwnedSouls.FirstOrDefault()?.Id ?? "").Status}:{_application.PossessionRemainingSeconds()}:{questSignature}"; if (featureSignature != _lastFeatureSignature) { RebuildFeaturePanel(_snapshot); _lastFeatureSignature = featureSignature; }
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

    private Texture2D? LoadAssetTexture(string logicalId)
    {
        var asset = _application.Asset(logicalId); return LoadTextureFile(asset.File);
    }

    private void RebuildMapTextures()
    {
        _mapTextures.Clear();
        foreach (var assetId in _application.WorldObjects().Select(item => item.AssetId).Distinct(StringComparer.Ordinal))
        {
            if (!_application.HasAsset(assetId)) continue;
            var texture = LoadMapTexture(_application.Asset(assetId));
            if (texture is not null) _mapTextures[assetId] = texture;
        }
    }

    private static Texture2D? LoadMapTexture(AssetSnapshot asset)
    {
        var texture = LoadTextureFile(asset.File);
        return texture is null || asset.FrameWidth is not { } frameWidth || asset.FrameHeight is not { } frameHeight
            ? texture
            : new AtlasTexture { Atlas = texture, Region = new Rect2(0, 0, frameWidth, frameHeight) };
    }

    private RegionTravelResult TravelToRegion(string regionId)
    {
        if (GameplayCommandsBlocked) return new RegionTravelResult(false, Failure: RegionTravelFailure.TravelConditionFailed, FailedConditionId: "save_recovery_required");
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

        foreach (var sprite in _monsterSprites.Values) sprite.QueueFree();
        foreach (var sprite in _allySprites.Values) sprite.QueueFree();
        _monsterSprites.Clear(); _allySprites.Clear(); _dyingMonsters.Clear();
        RebuildMapTextures();
        _ground = LoadAssetTexture(_application.CurrentMapBackgroundAssetId() ?? "tiles.arena.ground");
        _lastSoulSignature = ""; _lastFeatureSignature = ""; _lastScreenSignature = "";
        if (_application.CanonicalContent is not null && !Save(showMessage: false))
            return result with { Success = false, Failure = RegionTravelFailure.TravelConditionFailed, FailedConditionId = "durable_commit_pending" };
        Toast($"Đã đến { _application.RegionDetails(regionId)?.DisplayName ?? regionId }.");
        RefreshSnapshot();
        return result;
    }

    private static Texture2D? LoadTextureFile(string relativeFile)
    {
        var absolute = ProjectSettings.GlobalizePath($"res://assets/{relativeFile}"); if (!System.IO.File.Exists(absolute)) { GD.PushWarning($"Missing slice asset: {relativeFile}"); return null; }
        var image = Image.LoadFromFile(absolute); return image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
    }

    private AnimatedSprite2D BuildPlayerSprite(int rank)
    {
        var frames = new SpriteFrames(); frames.RemoveAnimation("default");
        foreach (var action in new[] { "idle", "walk", "attack" }) foreach (var direction in new[] { "front", "left", "right", "back" })
        {
            var clip = _application.PlayerAnimation(action, direction, rank); var texture = LoadTextureFile(clip.Asset.File); var name = new StringName(clip.Id); frames.AddAnimation(name); if (texture is null) continue;
            var columns = texture.GetWidth() / clip.Asset.FrameWidth!.Value;
            for (var index = 0; index < clip.FrameCount; index++) frames.AddFrame(name, new AtlasTexture { Atlas = texture, Region = new Rect2(index % columns * clip.Asset.FrameWidth.Value, clip.DirectionRow * clip.Asset.FrameHeight!.Value, clip.Asset.FrameWidth.Value, clip.Asset.FrameHeight.Value) });
            frames.SetAnimationSpeed(name, clip.FrameRate); frames.SetAnimationLoopMode(name, clip.Repeat != 0 ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
        }
        var sprite = new AnimatedSprite2D { SpriteFrames = frames, Scale = new Vector2(2.5f, 2.5f), ZIndex = 20 }; sprite.Play("idle_front"); return sprite;
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
            if (sprite.Animation != monsterAction || !sprite.IsPlaying()) sprite.Play(monsterAction);
        }
        var move = Input.GetVector("move_left", "move_right", "move_up", "move_down"); if (move != Vector2.Zero) _facing = System.Math.Abs(move.X) > System.Math.Abs(move.Y) ? move.X < 0 ? "left" : "right" : move.Y < 0 ? "back" : "front";
        var action = Input.IsActionPressed("attack") ? "attack" : move != Vector2.Zero ? "walk" : "idle"; var desired = $"{action}_{_facing}";
        if (_playerSprite.Animation != desired || (!_playerSprite.IsPlaying() && action == "attack")) _playerSprite.Play(desired);
    }

    private void SyncAllies(IReadOnlyList<AllySnapshot> allies)
    {
        var alive = allies.Select(item => item.Uid).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _allySprites.Keys.Where(id => !alive.Contains(id)).ToArray()) { _allySprites[stale].QueueFree(); _allySprites.Remove(stale); }
        foreach (var ally in allies)
        {
            if (!_allySprites.TryGetValue(ally.Uid, out var sprite))
            {
                sprite = BuildMonsterSprite(new MonsterSnapshot(ally.Uid, ally.DefinitionId, ally.SpeciesId, ally.Position, ally.CurrentHp, ally.MaximumHp, true, MonsterAiState.Idle, ally.Level, ally.Rank));
                sprite.Modulate = new Color("#86efac"); sprite.ZIndex = 11; _allySprites.Add(ally.Uid, sprite); AddChild(sprite);
            }
            sprite.Position = ToGodot(ally.Position);
            var action = ally.AiState switch { AllyAiState.Chase => "walk", AllyAiState.Attack => "attack", _ => "idle" };
            if (sprite.Animation != action || !sprite.IsPlaying()) sprite.Play(action);
        }
    }

    private AnimatedSprite2D BuildMonsterSprite(MonsterSnapshot monster)
    {
        var frames = new SpriteFrames(); frames.RemoveAnimation("default");
        foreach (var action in new[] { "idle", "walk", "attack", "hit", "death" })
        {
            var clip = _application.MonsterAnimation(monster.SpeciesId, monster.Rank, action); var name = new StringName(action); frames.AddAnimation(name);
            foreach (var file in clip.Files) { var texture = LoadTextureFile(file); if (texture is not null) frames.AddFrame(name, texture); }
            frames.SetAnimationSpeed(name, clip.FrameRate); frames.SetAnimationLoopMode(name, clip.Repeat != 0 ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
        }
        var sprite = new AnimatedSprite2D { SpriteFrames = frames, Scale = new Vector2(0.1f, 0.1f), ZIndex = 10 }; sprite.Play("idle"); return sprite;
    }

    private void BeginMonsterDeaths()
    {
        foreach (var defeated in _application.DrainDefeatedMonsterVisuals()) if (_monsterSprites.TryGetValue(defeated.Uid, out var sprite)) { sprite.Position = ToGodot(defeated.Position); sprite.Play("death"); _dyingMonsters[defeated.Uid] = defeated.DurationSeconds; }
    }

    private void UpdateMonsterDeaths(double delta)
    {
        foreach (var entry in _dyingMonsters.ToArray())
        {
            var remaining = entry.Value - delta; if (remaining > 0) { _dyingMonsters[entry.Key] = remaining; continue; }
            if (_monsterSprites.Remove(entry.Key, out var sprite)) sprite.QueueFree(); _dyingMonsters.Remove(entry.Key);
        }
    }
}
