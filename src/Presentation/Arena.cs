using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation.Systems;
using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.State;
using SimVec2 = SoloVsMortal.Core.Math.Vec2;

namespace SoloVsMortal.Presentation;

/// <summary>Thin Godot adapter: input in, snapshots out. Simulation remains the gameplay owner.</summary>
public partial class Arena : Node2D
{
    private const string SavePath = "user://solo_vs_mortal_save_v6.json";
    private static readonly bool ShowMapCollisionDebug = false;
    private const string UiAssetRoot = "res://assets/third_party/kenney-ui-adventure/PNG/Default/";
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
    private string? _pendingDevourSoulId;
    private DevourMode _pendingDevourMode = DevourMode.Essence;
    private string _lastFeatureSignature = "";
    private string _lastScreenSignature = "";
    private int _visualRank;

    public override void _Ready()
    {
        _camera = GetNode<Camera2D>("Camera2D"); _status = GetNode<Label>("Hud/Panel/Status"); _currency = GetNode<Label>("Hud/CurrencyPanel/Currency"); _toast = GetNode<Label>("Hud/Toast"); _soulList = GetNode<VBoxContainer>("Hud/SoulPanel/List"); _featureList = GetNode<VBoxContainer>("Hud/FeaturePanel/Content"); _screens = GetNode<TabContainer>("Hud/Screens");
        ApplyHudVisualDesign();
        _application = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs"));
        _application.Start();
        _worldMap = new WorldMapUI(); AddChild(_worldMap); _worldMap.Initialize(_application, TravelToRegion);
        _minimap = new HudMinimap { Position = new Vector2(1060, 24), Size = new Vector2(184, 164) }; GetNode<CanvasLayer>("Hud").AddChild(_minimap);
        _ground = LoadAssetTexture(_application.CurrentMapBackgroundAssetId() ?? "tiles.arena.ground"); _soulTexture = LoadAssetTexture("soul.orb.no_boc"); RebuildMapTextures();
        GetNode<ConfirmationDialog>("Hud/DevourConfirm").Confirmed += ConfirmDevour;
        _visualRank = 1; _playerSprite = BuildPlayerSprite(_visualRank); AddChild(_playerSprite);
        TryLoad(showMessage: false);
        if (_application.Snapshot().Monsters.Count == 0) _application.SpawnMonster("mon_skeleton", 1, new SimVec2(650, 280));
        RefreshSnapshot();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_worldMap.IsOpen)
        {
            _application.SetInput(SimVec2.Zero, false);
            return;
        }
        var move = Input.GetVector("move_left", "move_right", "move_up", "move_down");
        _application.SetInput(new SimVec2(move.X, move.Y), Input.IsActionPressed("attack"));
        _application.Tick(delta);
        BeginMonsterDeaths(); UpdateMonsterDeaths(delta);
        if (Input.IsActionJustPressed("acquire_soul")) { var count = _application.AcquireNearbySouls().Count; Toast(count > 0 ? $"Đã thu {count} Hồn." : "Không có Hồn ở gần."); }
        if (Input.IsActionJustPressed("devour_soul")) DevourFirstEssence();
        if (Input.IsActionJustPressed("save_game")) Save(showMessage: true);
        if (Input.IsActionJustPressed("load_game")) TryLoad(showMessage: true);
        _autosaveRemaining -= delta; if (_autosaveRemaining <= 0) { Save(showMessage: false); _autosaveRemaining = 10; }
        if (_toastRemaining > 0) { _toastRemaining -= delta; if (_toastRemaining <= 0) _toast.Text = ""; }
        RefreshSnapshot();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.M)
        {
            _worldMap.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree() { if (_application is not null) Save(showMessage: false); }

    public override void _Draw()
    {
        var world = _snapshot?.World; var width = (float)(world?.Width ?? 1536); var height = (float)(world?.Height ?? 768);
        DrawRect(new Rect2(0, 0, width, height), new Color("#c99b5b")); if (_ground is not null) DrawTextureRect(_ground, new Rect2(0, 0, width, height), true, new Color(1, 1, 1, 0.45f));
        if (_snapshot is null) return;
        foreach (var obj in _application.WorldObjects().Where(item => !item.Destroyed).OrderBy(item => item.ZIndex).ThenBy(item => item.Position.Y))
        {
            var p = ToGodot(obj.Position); var scale = (float)System.Math.Clamp(obj.PresentationScale, 0.1, 3); if (_mapTextures.TryGetValue(obj.AssetId, out var texture)) { var half = 42 * scale; DrawTextureRect(texture, new Rect2(p.X - half, p.Y - half, half * 2, half * 2), false, new Color(1, 1, 1, 0.92f)); } else { var color = obj.Type switch { "tree" => new Color("#166534"), "building" => new Color("#713f12"), "fragileWall" => new Color("#64748b"), "portal" => new Color("#38bdf8"), _ => new Color("#854d0e") }; DrawCircle(p, (obj.Type == "tree" ? 18 : 12) * scale, color); }
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
        _status.Text = $"Khu vực: {regionName}\nSinh lực: {System.Math.Ceiling(player.CurrentHp)}/{player.MaximumHp}\nCấp: {player.Level}  •  Cảnh giới: {player.Rank}  •  Công: {player.Attack}  •  Thủ: {player.Defense}\nQuái: {_snapshot.Monsters.Count}  •  Hồn gần bản đồ: {_snapshot.WorldSouls.Count}  •  Hồn sở hữu: {_snapshot.OwnedSouls.Count}";
        _currency.Text = $"✦ {_snapshot.Inventory.Sum(item => item.Count)}     ◆ {player.Xp}     ◈ {_snapshot.OwnedSouls.Count}";
        _minimap.Refresh(_snapshot, _application.WorldObjects());
        var signature = string.Join('|', _snapshot.OwnedSouls.Select(soul => $"{soul.Id}:{soul.Level}:{soul.Xp}")); if (signature != _lastSoulSignature) { RebuildSoulPanel(_snapshot); _lastSoulSignature = signature; }
        var featureSignature = $"{_snapshot.OwnedSouls.Count}:{_snapshot.SoulBanners.Sum(item => item.BoundSoulIds.Count)}:{_application.Inventory().Count}:{_application.SoulRuntime(_snapshot.OwnedSouls.FirstOrDefault()?.Id ?? "").Status}:{_application.PossessionRemainingSeconds()}"; if (featureSignature != _lastFeatureSignature) { RebuildFeaturePanel(_snapshot); _lastFeatureSignature = featureSignature; }
        var banner = _snapshot.SoulBanners.FirstOrDefault(); var screenSignature = $"{player.Level}:{player.Xp}:{inventorySignature(_application.Inventory())}:{banner?.Level}:{banner?.BoundSoulIds.Count}"; if (screenSignature != _lastScreenSignature) { RebuildScreens(_snapshot); _lastScreenSignature = screenSignature; }
        QueueRedraw();
    }

    private void Save(bool showMessage)
    {
        try { GodotSaveStore.Write(SavePath, _application.CaptureSaveJson()); if (showMessage) Toast("Đã lưu tiến trình."); }
        catch (Exception exception) { GD.PushError($"Save failed: {exception.Message}"); if (showMessage) Toast("Không thể lưu tiến trình."); }
    }

    private bool TryLoad(bool showMessage)
    {
        var json = GodotSaveStore.Read(SavePath); if (json is null) { if (showMessage) Toast("Chưa có bản lưu."); return false; }
        try { _application.RestoreSaveJson(json); if (showMessage) Toast("Đã tải tiến trình."); return true; }
        catch (Exception exception) { GD.PushError($"Load failed: {exception.Message}"); if (showMessage) Toast("Bản lưu không hợp lệ."); return false; }
    }

    private void Toast(string text) { _toast.Text = text; _toastRemaining = 2.5; }
    private void RebuildSoulPanel(GameSnapshot snapshot)
    {
        foreach (var child in _soulList.GetChildren()) child.QueueFree();
        foreach (var soul in snapshot.OwnedSouls)
        {
            var row = new HBoxContainer(); var label = new Label { Text = $"{soul.DisplayName}  Lv.{soul.Level}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = soul.SoulNatureId }; row.AddChild(label);
            var banner = snapshot.SoulBanners.FirstOrDefault(); var bound = banner?.BoundSoulIds.Contains(soul.Id) == true;
            var link = _application.SoulLinks().FirstOrDefault(item => item.SoulId == soul.Id); var runtime = _application.SoulRuntime(soul.Id);
            var bind = new Button { Text = bound ? "Gỡ Hồn Liên" : "Thiết lập Hồn Liên", Disabled = banner is null || (bound && !string.IsNullOrEmpty(link?.BannerId) && runtime.Status is SoulRuntimeStatus.Summoned or SoulRuntimeStatus.Possessed) };
            StyleActionButton(bind);
            bind.Pressed += () => { if (banner is null) return; var ok = bound ? _application.UnbindSoul(soul.Id, banner.Id).Success : _application.BindSoul(soul.Id, banner.Id).Success; if (ok) { Toast(bound ? "Đã gỡ bind Soul." : "Đã bind Soul."); RefreshSnapshot(); } }; row.AddChild(bind);
            foreach (var preview in _application.DevourPreviews(soul.Id)) { var devour = new Button { Text = $"Devour {preview.DisplayName} (+{preview.Reward:0})", Disabled = bound }; StyleActionButton(devour, destructive: true); devour.Pressed += () => ShowDevourConfirmation(soul.Id, preview.Mode); row.AddChild(devour); }
            _soulList.AddChild(row);
        }
        if (snapshot.OwnedSouls.Count == 0) _soulList.AddChild(new Label { Text = "Chưa sở hữu Soul. Nhấn E gần orb." });
    }
    private void ShowDevourConfirmation(string soulId, DevourMode mode) { _pendingDevourSoulId = soulId; _pendingDevourMode = mode; var preview = _application.DevourPreviews(soulId).FirstOrDefault(item => item.Mode == mode); if (preview is null) return; var dialog = GetNode<ConfirmationDialog>("Hud/DevourConfirm"); dialog.DialogText = $"Hấp thụ {preview.DisplayName} (+{preview.Reward:0})?\nThao tác này không thể hoàn tác."; dialog.PopupCentered(); }
    private void ConfirmDevour() { if (_pendingDevourSoulId is null) return; var result = _application.DevourSoul(_pendingDevourSoulId, _pendingDevourMode); _pendingDevourSoulId = null; Toast(result.Success ? $"Đã hấp thụ {result.Reward:0} điểm." : "Không thể hấp thụ Soul."); RefreshSnapshot(); }
    private void DevourFirstEssence()
    {
        var soul = _snapshot?.OwnedSouls.FirstOrDefault(item => !_snapshot.SoulBanners.Any(banner => banner.BoundSoulIds.Contains(item.Id)) && _application.DevourPreviews(item.Id).Any(preview => preview.Mode == DevourMode.Essence));
        if (soul is null) { Toast("Không có Soul phù hợp để hấp thụ."); return; } var result = _application.DevourSoul(soul.Id, DevourMode.Essence); Toast(result.Success ? "Đã hấp thụ Tinh Hoa." : "Không thể hấp thụ Soul."); RefreshSnapshot();
    }

    private void RebuildScreens(GameSnapshot snapshot)
    {
        var inventory = GetNode<VBoxContainer>("Hud/Screens/Inventory"); var progression = GetNode<VBoxContainer>("Hud/Screens/Progression"); var bannerPage = GetNode<VBoxContainer>("Hud/Screens/Banner");
        foreach (var page in new[] { inventory, progression, bannerPage }) foreach (var child in page.GetChildren()) child.QueueFree();

        inventory.AddChild(new Label { Text = "Túi Đồ", ThemeTypeVariation = "HeaderMedium" });
        var items = _application.Inventory(); var grid = new GridContainer { Columns = 7, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; inventory.AddChild(grid);
        for (var index = 0; index < Math.Max(14, items.Count); index++)
        {
            InventoryItem? item = index < items.Count ? items[index] : null;
            var slot = new Button { Text = item is null ? "" : $"{item.Value.StableId}\n×{item.Value.Count}", TooltipText = item is null ? "Ô trống" : item.Value.StableId, CustomMinimumSize = new Vector2(94, 48) };
            StyleActionButton(slot); grid.AddChild(slot);
        }
        var inventoryFooter = new HBoxContainer(); inventoryFooter.AddChild(new Label { Text = $"Số ô: {items.Count}/100", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var craft = new Button { Text = "Chế Cường Công Đan", CustomMinimumSize = new Vector2(180, 30) }; StyleActionButton(craft); craft.Pressed += () => { Toast(_application.Craft(PillId.Power) ? "Đã chế Cường Công Đan." : "Thiếu Tinh Thạch/Beast Core."); RefreshSnapshot(); }; inventoryFooter.AddChild(craft); inventory.AddChild(inventoryFooter);

        progression.AddChild(new Label { Text = "Tiến Hóa Nhân Vật", ThemeTypeVariation = "HeaderMedium" });
        progression.AddChild(new Label { Text = $"Cấp {snapshot.Player.Level}  •  XP {snapshot.Player.Xp}\nCảnh giới {snapshot.Player.Rank}\nCông {snapshot.Player.Attack}  •  Thủ {snapshot.Player.Defense}", SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var progressionActions = new HBoxContainer(); var gain = new Button { Text = "+20 XP", CustomMinimumSize = new Vector2(150, 32) }; StyleActionButton(gain); gain.Pressed += () => { _application.AddPlayerXp(20); RefreshSnapshot(); }; progressionActions.AddChild(gain);
        var breakthrough = new Button { Text = "Thử phá cảnh", CustomMinimumSize = new Vector2(180, 32) }; StyleActionButton(breakthrough); breakthrough.Pressed += () => { Toast(_application.AttemptPlayerBreakthrough() ? "Phá cảnh thành công." : "Phá cảnh thất bại hoặc chưa đủ điều kiện."); RefreshSnapshot(); }; progressionActions.AddChild(breakthrough); progression.AddChild(progressionActions);

        var banner = snapshot.SoulBanners.FirstOrDefault(); bannerPage.AddChild(new Label { Text = banner is null ? "Chưa có Hồn Phiên" : $"Hồn Phiên {banner.Tier}\nCấp {banner.Level}  •  Ô {banner.BoundSoulIds.Count}/{banner.SlotLimit}\nDung lượng {banner.UsedCapacity}/{banner.CapacityLimit}", ThemeTypeVariation = "HeaderMedium" });
        var boundSouls = banner is null ? Array.Empty<OwnedSoulSnapshot>() : snapshot.OwnedSouls.Where(item => banner.BoundSoulIds.Contains(item.Id)).ToArray();
        bannerPage.AddChild(new Label { Text = boundSouls.Length == 0 ? "Chưa có linh hồn liên kết." : string.Join('\n', boundSouls.Select(soul => $"• {soul.DisplayName} — Lv.{soul.Level}")), SizeFlagsVertical = Control.SizeFlags.ExpandFill });
    }

    private static string inventorySignature(IReadOnlyList<InventoryItem> items) => string.Join(';', items.Select(item => $"{item.StableId}:{item.Count}"));

    private void RebuildFeaturePanel(GameSnapshot snapshot)
    {
        foreach (var child in _featureList.GetChildren()) child.QueueFree();
        var banner = snapshot.SoulBanners.FirstOrDefault();
        _featureList.AddChild(new Label { Text = banner is null ? "Hồn Phiên: chưa tạo" : $"Hồn Phiên {banner.Tier}  Lv.{banner.Level}\nÔ: {banner.UsedCapacity}/{banner.CapacityLimit}  •  Ô: {banner.BoundSoulIds.Count}/{banner.SlotLimit}" });
        var inventory = _application.Inventory(); _featureList.AddChild(new Label { Text = inventory.Count == 0 ? "Vật phẩm: trống" : "Vật phẩm: " + string.Join(", ", inventory.Select(item => $"{item.StableId}×{item.Count}")) });
        var progression = new HBoxContainer(); progression.AddChild(new Label { Text = "Tiến độ", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); var xp = new Button { Text = "+20 XP" }; StyleActionButton(xp); xp.Pressed += () => { _application.AddPlayerXp(20); Toast("Đã nhận 20 XP."); RefreshSnapshot(); }; progression.AddChild(xp); var craft = new Button { Text = "Chế Power" }; StyleActionButton(craft); craft.Pressed += () => { Toast(_application.Craft(PillId.Power) ? "Đã chế Power Pill." : "Thiếu nguyên liệu."); RefreshSnapshot(); }; progression.AddChild(craft); _featureList.AddChild(progression);
        var possession = new Label { Text = _application.ActivePossessionSoulId is { } active ? $"Đảo chiều Hồn Liên: {active} ({Math.Ceiling(_application.PossessionRemainingSeconds())}s)" : "Đảo chiều Hồn Liên: không hoạt động" }; _featureList.AddChild(possession);
        foreach (var soul in snapshot.OwnedSouls.Where(soul => banner?.BoundSoulIds.Contains(soul.Id) == true))
        {
            var link = _application.SoulLinks().First(item => item.SoulId == soul.Id); var runtime = _application.SoulRuntime(soul.Id); var row = new HBoxContainer(); row.AddChild(new Label { Text = $"{soul.DisplayName}: {link.State} · Tải {link.SoulCost} · Ổn định {link.Stability:P0}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var summon = new Button { Text = runtime.Status == SoulRuntimeStatus.Summoned ? "Thu hồi" : "Triệu hồi", Disabled = runtime.Status is SoulRuntimeStatus.Dispersed or SoulRuntimeStatus.Possessed };
            StyleActionButton(summon);
            summon.Pressed += () => { if (runtime.Status == SoulRuntimeStatus.Summoned) _application.UnsummonSoul(soul.Id); else _application.SummonSoul(soul.Id, banner!.Id, new SimVec2(_snapshot!.Player.Position.X + 36, _snapshot.Player.Position.Y)); RefreshSnapshot(); }; row.AddChild(summon);
            var possess = new Button { Text = "Phụ hồn", Disabled = runtime.Status != SoulRuntimeStatus.Ready || _application.ActivePossessionSoulId is not null }; StyleActionButton(possess); possess.Pressed += () => { var result = _application.StartPossession(soul.Id); Toast(result.Success ? "Đã bắt đầu phụ hồn." : "Không thể phụ hồn Soul này."); RefreshSnapshot(); }; row.AddChild(possess); _featureList.AddChild(row);
        }
    }
    private void ApplyHudVisualDesign()
    {
        foreach (var panelPath in new[] { "Hud/Panel", "Hud/CurrencyPanel", "Hud/SoulPanel", "Hud/FeaturePanel" })
            GetNode<Panel>(panelPath).AddThemeStyleboxOverride("panel", UiBox("panel_brown_dark.png", 14));

        _screens.AddThemeStyleboxOverride("panel", UiBox("panel_brown_dark.png", 14));
        _screens.AddThemeStyleboxOverride("tab_selected", UiBox("button_brown.png", 10));
        _screens.AddThemeStyleboxOverride("tab_unselected", UiBox("button_grey.png", 10));
        _screens.AddThemeColorOverride("font_selected_color", new Color("#fff0ba"));
        _screens.AddThemeColorOverride("font_unselected_color", new Color("#e5c981"));
        _screens.SetTabTitle(0, "Túi Đồ");
        _screens.SetTabTitle(1, "Tiến Hóa");
        _screens.SetTabTitle(2, "Hồn Phiên");

        foreach (var labelPath in new[] { "Hud/Panel/Status", "Hud/CurrencyPanel/Currency", "Hud/MapCaption", "Hud/SoulPanel/Title", "Hud/Help", "Hud/Toast" })
            GetNode<Label>(labelPath).AddThemeColorOverride("font_color", new Color("#f9e7b0"));
        GetNode<Label>("Hud/SoulPanel/Title").AddThemeFontSizeOverride("font_size", 18);
        GetNode<Label>("Hud/CurrencyPanel/Currency").AddThemeFontSizeOverride("font_size", 17);
    }

    private static StyleBoxTexture UiBox(string fileName, float margin)
    {
        var style = new StyleBoxTexture { Texture = GD.Load<Texture2D>(UiAssetRoot + fileName), DrawCenter = true };
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        {
            style.SetTextureMargin(side, margin);
            style.SetContentMargin(side, margin);
        }
        return style;
    }

    private static void StyleActionButton(Button button, bool destructive = false)
    {
        button.AddThemeStyleboxOverride("normal", UiBox(destructive ? "button_red.png" : "button_brown.png", 10));
        button.AddThemeStyleboxOverride("hover", UiBox(destructive ? "button_red_close.png" : "button_grey.png", 10));
        button.AddThemeStyleboxOverride("pressed", UiBox(destructive ? "button_red_close.png" : "button_brown_close.png", 10));
        button.AddThemeColorOverride("font_color", new Color("#fff0ba"));
        button.AddThemeFontSizeOverride("font_size", 13);
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
