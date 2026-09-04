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
    private GameApplication _application = null!;
    private Camera2D _camera = null!;
    private Label _status = null!;
    private Label _toast = null!;
    private VBoxContainer _soulList = null!;
    private VBoxContainer _featureList = null!;
    private TabContainer _screens = null!;
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
        _camera = GetNode<Camera2D>("Camera2D"); _status = GetNode<Label>("Hud/Panel/Status"); _toast = GetNode<Label>("Hud/Toast"); _soulList = GetNode<VBoxContainer>("Hud/SoulPanel/List"); _featureList = GetNode<VBoxContainer>("Hud/FeaturePanel/Content"); _screens = GetNode<TabContainer>("Hud/Screens");
        _application = GameApplication.CreateFromDefinitionsDirectory(ProjectSettings.GlobalizePath("res://data/configs"));
        _application.Start();
        _ground = LoadAssetTexture("tiles.arena.ground"); _soulTexture = LoadAssetTexture("soul.orb.no_boc"); foreach (var obj in _application.WorldObjects().Select(item => item.AssetId).Distinct(StringComparer.Ordinal)) { var texture = LoadAssetTexture(obj); if (texture is not null) _mapTextures[obj] = texture; }
        GetNode<ConfirmationDialog>("Hud/DevourConfirm").Confirmed += ConfirmDevour;
        _visualRank = 1; _playerSprite = BuildPlayerSprite(_visualRank); AddChild(_playerSprite);
        TryLoad(showMessage: false);
        if (_application.Snapshot().Monsters.Count == 0) _application.SpawnMonster("mon_skeleton", 1, new SimVec2(650, 280));
        RefreshSnapshot();
    }

    public override void _PhysicsProcess(double delta)
    {
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

    public override void _ExitTree() { if (_application is not null) Save(showMessage: false); }

    public override void _Draw()
    {
        var world = _snapshot?.World; var width = (float)(world?.Width ?? 1536); var height = (float)(world?.Height ?? 768);
        DrawRect(new Rect2(0, 0, width, height), new Color("#c99b5b")); if (_ground is not null) DrawTextureRect(_ground, new Rect2(0, 0, width, height), true, new Color(1, 1, 1, 0.45f));
        if (_snapshot is null) return;
        foreach (var rect in _snapshot.World.BlockingRects) DrawRect(new Rect2((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height), new Color(0.25f, 0.16f, 0.08f, 0.32f), false, 2);
        foreach (var obj in _application.WorldObjects().Where(item => !item.Destroyed))
        {
            var p = ToGodot(obj.Position); if (_mapTextures.TryGetValue(obj.AssetId, out var texture)) DrawTextureRect(texture, new Rect2(p.X - 42, p.Y - 42, 84, 84), false, new Color(1, 1, 1, 0.92f)); else { var color = obj.Type switch { "tree" => new Color("#166534"), "building" => new Color("#713f12"), "fragileWall" => new Color("#64748b"), _ => new Color("#854d0e") }; DrawCircle(p, obj.Type == "tree" ? 18 : 12, color); } DrawString(ThemeDB.FallbackFont, p + new Vector2(-24, 52), obj.Type, HorizontalAlignment.Left, -1, 10, new Color(0.1f, 0.08f, 0.04f, 0.7f));
        }
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
        _status.Text = $"Sinh lực: {System.Math.Ceiling(player.CurrentHp)}/{player.MaximumHp}\nCấp: {player.Level}  •  Cảnh giới: {player.Rank}  •  Công: {player.Attack}  •  Thủ: {player.Defense}\nQuái: {_snapshot.Monsters.Count}  •  Hồn gần bản đồ: {_snapshot.WorldSouls.Count}  •  Hồn sở hữu: {_snapshot.OwnedSouls.Count}";
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
            bind.Pressed += () => { if (banner is null) return; var ok = bound ? _application.UnbindSoul(soul.Id, banner.Id).Success : _application.BindSoul(soul.Id, banner.Id).Success; if (ok) { Toast(bound ? "Đã gỡ bind Soul." : "Đã bind Soul."); RefreshSnapshot(); } }; row.AddChild(bind);
            foreach (var preview in _application.DevourPreviews(soul.Id)) { var devour = new Button { Text = $"Devour {preview.DisplayName} (+{preview.Reward:0})", Disabled = bound }; devour.Pressed += () => ShowDevourConfirmation(soul.Id, preview.Mode); row.AddChild(devour); }
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
        inventory.AddChild(new Label { Text = "Túi vật phẩm", ThemeTypeVariation = "HeaderMedium" }); var items = _application.Inventory(); if (items.Count == 0) inventory.AddChild(new Label { Text = "Chưa có vật phẩm." }); else foreach (var item in items) inventory.AddChild(new Label { Text = $"{item.StableId}: {item.Count}" });
        var craft = new Button { Text = "Chế Cường Công Đan" }; craft.Pressed += () => { Toast(_application.Craft(PillId.Power) ? "Đã chế Cường Công Đan." : "Thiếu Tinh Thạch/Beast Core."); RefreshSnapshot(); }; inventory.AddChild(craft);
        progression.AddChild(new Label { Text = $"Tiến trình Player\nCấp {snapshot.Player.Level} • XP {snapshot.Player.Xp}\nCông {snapshot.Player.Attack} • Thủ {snapshot.Player.Defense}", ThemeTypeVariation = "HeaderMedium" }); var gain = new Button { Text = "Nhận 20 XP" }; gain.Pressed += () => { _application.AddPlayerXp(20); RefreshSnapshot(); }; progression.AddChild(gain);
        var breakthrough = new Button { Text = "Thử phá cảnh" }; breakthrough.Pressed += () => { Toast(_application.AttemptPlayerBreakthrough() ? "Phá cảnh thành công." : "Phá cảnh thất bại hoặc chưa đủ điều kiện."); RefreshSnapshot(); }; progression.AddChild(breakthrough);
        var banner = snapshot.SoulBanners.FirstOrDefault(); bannerPage.AddChild(new Label { Text = banner is null ? "Chưa có Hồn Phiên" : $"{banner.Tier}\nCấp {banner.Level}\nÔ: {banner.BoundSoulIds.Count}/{banner.SlotLimit}\nDung lượng: {banner.UsedCapacity}/{banner.CapacityLimit}", ThemeTypeVariation = "HeaderMedium" }); if (banner is not null) foreach (var soul in snapshot.OwnedSouls.Where(item => banner.BoundSoulIds.Contains(item.Id))) bannerPage.AddChild(new Label { Text = $"• {soul.DisplayName} — Lv.{soul.Level}" });
    }

    private static string inventorySignature(IReadOnlyList<InventoryItem> items) => string.Join(';', items.Select(item => $"{item.StableId}:{item.Count}"));

    private void RebuildFeaturePanel(GameSnapshot snapshot)
    {
        foreach (var child in _featureList.GetChildren()) child.QueueFree();
        var banner = snapshot.SoulBanners.FirstOrDefault();
        _featureList.AddChild(new Label { Text = banner is null ? "Hồn Phiên: chưa tạo" : $"Hồn Phiên {banner.Tier}  Lv.{banner.Level}\nÔ: {banner.UsedCapacity}/{banner.CapacityLimit}  •  Ô: {banner.BoundSoulIds.Count}/{banner.SlotLimit}" });
        var inventory = _application.Inventory(); _featureList.AddChild(new Label { Text = inventory.Count == 0 ? "Vật phẩm: trống" : "Vật phẩm: " + string.Join(", ", inventory.Select(item => $"{item.StableId}×{item.Count}")) });
        var progression = new HBoxContainer(); progression.AddChild(new Label { Text = "Tiến độ", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); var xp = new Button { Text = "+20 XP" }; xp.Pressed += () => { _application.AddPlayerXp(20); Toast("Đã nhận 20 XP."); RefreshSnapshot(); }; progression.AddChild(xp); var craft = new Button { Text = "Chế Power" }; craft.Pressed += () => { Toast(_application.Craft(PillId.Power) ? "Đã chế Power Pill." : "Thiếu nguyên liệu."); RefreshSnapshot(); }; progression.AddChild(craft); _featureList.AddChild(progression);
        var possession = new Label { Text = _application.ActivePossessionSoulId is { } active ? $"Đảo chiều Hồn Liên: {active} ({Math.Ceiling(_application.PossessionRemainingSeconds())}s)" : "Đảo chiều Hồn Liên: không hoạt động" }; _featureList.AddChild(possession);
        foreach (var soul in snapshot.OwnedSouls.Where(soul => banner?.BoundSoulIds.Contains(soul.Id) == true))
        {
            var link = _application.SoulLinks().First(item => item.SoulId == soul.Id); var runtime = _application.SoulRuntime(soul.Id); var row = new HBoxContainer(); row.AddChild(new Label { Text = $"{soul.DisplayName}: {link.State} · Tải {link.SoulCost} · Ổn định {link.Stability:P0}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var summon = new Button { Text = runtime.Status == SoulRuntimeStatus.Summoned ? "Thu hồi" : "Triệu hồi", Disabled = runtime.Status is SoulRuntimeStatus.Dispersed or SoulRuntimeStatus.Possessed };
            summon.Pressed += () => { if (runtime.Status == SoulRuntimeStatus.Summoned) _application.UnsummonSoul(soul.Id); else _application.SummonSoul(soul.Id, banner!.Id, new SimVec2(_snapshot!.Player.Position.X + 36, _snapshot.Player.Position.Y)); RefreshSnapshot(); }; row.AddChild(summon);
            var possess = new Button { Text = "Phụ hồn", Disabled = runtime.Status != SoulRuntimeStatus.Ready || _application.ActivePossessionSoulId is not null }; possess.Pressed += () => { var result = _application.StartPossession(soul.Id); Toast(result.Success ? "Đã bắt đầu phụ hồn." : "Không thể phụ hồn Soul này."); RefreshSnapshot(); }; row.AddChild(possess); _featureList.AddChild(row);
        }
    }
    private static Vector2 ToGodot(SimVec2 value) => new((float)value.X, (float)value.Y);

    private Texture2D? LoadAssetTexture(string logicalId)
    {
        var asset = _application.Asset(logicalId); return LoadTextureFile(asset.File);
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
