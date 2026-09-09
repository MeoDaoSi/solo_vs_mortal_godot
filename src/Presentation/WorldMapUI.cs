using Godot;
using SoloVsMortal.Application;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Presentation;

/// <summary>Godot-only world map overlay. It renders Application views and sends travel commands back through Application.</summary>
public partial class WorldMapUI : Control
{
    private GameApplication _application = null!;
    private Func<string, RegionTravelResult> _travel = null!;
    private WorldMapCanvas _mapCanvas = null!;
    private Label _selectionTitle = null!;
    private Label _selectionDetails = null!;
    private Button _travelButton = null!;
    private string? _selectedRegionId;

    public bool IsOpen => Visible;

    public void Initialize(GameApplication application, Func<string, RegionTravelResult> travel)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _travel = travel ?? throw new ArgumentNullException(nameof(travel));
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 1000;
        BuildLayout();
        Refresh();
    }

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    public void Open()
    {
        _selectedRegionId = _application.Snapshot().CurrentRegionId;
        Refresh();
        Visible = true;
        GetViewport().SetInputAsHandled();
    }

    public void Close()
    {
        Visible = false;
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
            Close();
    }

    public void Refresh()
    {
        if (!IsInstanceValid(_mapCanvas) || _application is null) return;
        var regions = _application.WorldMapRegions();
        if (_selectedRegionId is null || regions.All(item => item.Id != _selectedRegionId)) _selectedRegionId = regions.FirstOrDefault()?.Id;
        _mapCanvas.Rebuild(regions, _selectedRegionId, SelectRegion);
        RefreshDetails(regions.FirstOrDefault(item => item.Id == _selectedRegionId));
    }

    private void BuildLayout()
    {
        var backdrop = new ColorRect { Color = new Color(0.015f, 0.02f, 0.04f, 0.88f), MouseFilter = Control.MouseFilterEnum.Stop };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(backdrop);

        var panel = new PanelContainer { Position = new Vector2(90, 48), Size = new Vector2(1100, 624), MouseFilter = Control.MouseFilterEnum.Stop };
        FancyUi.ApplyPanel(panel, main: true);
        AddChild(panel);
        var margin = new MarginContainer(); margin.AddThemeConstantOverride("margin_left", 22); margin.AddThemeConstantOverride("margin_top", 18); margin.AddThemeConstantOverride("margin_right", 22); margin.AddThemeConstantOverride("margin_bottom", 18); panel.AddChild(margin);
        var column = new VBoxContainer(); margin.AddChild(column);

        var header = new HBoxContainer(); column.AddChild(header);
        var title = new Label { Text = "BẢN ĐỒ THẾ GIỚI", ThemeTypeVariation = "HeaderLarge", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; title.AddThemeColorOverride("font_color", FancyUi.TextBright); header.AddChild(title);
        var close = new Button { Text = "Đóng  (M / Esc)", CustomMinimumSize = new Vector2(150, 36) }; close.Pressed += Close; FancyUi.ApplyButton(close); header.AddChild(close);
        column.AddChild(new Label { Text = "Chọn một khu vực để xem câu chuyện và điều kiện di chuyển.", Modulate = new Color(0.72f, 0.78f, 0.88f) });

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; column.AddChild(body);
        _mapCanvas = new WorldMapCanvas { CustomMinimumSize = new Vector2(650, 500), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddChild(_mapCanvas);
        var detailsPanel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0) }; FancyUi.ApplyPanel(detailsPanel, main: false); body.AddChild(detailsPanel);
        var detailsMargin = new MarginContainer(); detailsMargin.AddThemeConstantOverride("margin_left", 18); detailsMargin.AddThemeConstantOverride("margin_top", 18); detailsMargin.AddThemeConstantOverride("margin_right", 18); detailsMargin.AddThemeConstantOverride("margin_bottom", 18); detailsPanel.AddChild(detailsMargin);
        var details = new VBoxContainer(); detailsMargin.AddChild(details);
        _selectionTitle = new Label { Text = "Chọn khu vực", ThemeTypeVariation = "HeaderMedium", AutowrapMode = TextServer.AutowrapMode.WordSmart }; details.AddChild(_selectionTitle);
        _selectionDetails = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; details.AddChild(_selectionDetails);
        _travelButton = new Button { Text = "Đi tới khu vực", CustomMinimumSize = new Vector2(0, 42) }; _travelButton.Pressed += TravelSelected; FancyUi.ApplyButton(_travelButton); details.AddChild(_travelButton);
        details.AddChild(new Label { Text = "Màu vàng: hiện tại\nMàu đỏ: chưa thể đến\nMàu xanh: đã sẵn sàng", Modulate = new Color(0.72f, 0.78f, 0.88f) });
    }

    private void SelectRegion(string regionId)
    {
        _selectedRegionId = regionId;
        Refresh();
    }

    private void RefreshDetails(WorldMapRegionSnapshot? region)
    {
        if (region is null)
        {
            _selectionTitle.Text = "Chưa có khu vực";
            _selectionDetails.Text = "World Map chưa có region definition.";
            _travelButton.Disabled = true;
            return;
        }

        _selectionTitle.Text = region.IsCurrent ? $"◆ {region.DisplayName}" : region.DisplayName;
        var level = region.RecommendedLevelMinimum is { } min && region.RecommendedLevelMaximum is { } max ? $"Cấp đề xuất: {min}–{max}\n" : "";
        var state = region.IsCurrent ? "Vị trí hiện tại" : region.CanTravel ? "Sẵn sàng di chuyển" : "Chưa thể đến khu vực này";
        var conditions = region.TravelConditionIds.Count == 0 ? "Không yêu cầu điều kiện đặc biệt" : $"Điều kiện: {string.Join(", ", region.TravelConditionIds)}";
        _selectionDetails.Text = $"{state}\n\n{region.ShortDescription}\n\n{region.Story}\n\nBiome: {region.Biome}\n{level}{conditions}";
        _travelButton.Disabled = !region.CanTravel || region.IsCurrent;
        _travelButton.Text = region.IsCurrent ? "Đang ở khu vực này" : "Đi tới khu vực";
    }

    private void TravelSelected()
    {
        if (_selectedRegionId is null) return;
        var result = _travel(_selectedRegionId);
        if (result.Success) Close();
        else Refresh();
    }

    private sealed partial class WorldMapCanvas : Control
    {
        private readonly List<Button> _buttons = [];
        private IReadOnlyList<WorldMapRegionSnapshot> _regions = Array.Empty<WorldMapRegionSnapshot>();
        private string? _currentSelection;

        public void Rebuild(IReadOnlyList<WorldMapRegionSnapshot> regions, string? selectedRegionId, Action<string> select)
        {
            _regions = regions;
            _currentSelection = selectedRegionId;
            foreach (var button in _buttons) button.QueueFree();
            _buttons.Clear();
            var center = new Vector2(325, 250);
            var radius = 195f;
            foreach (var region in regions)
            {
                var button = new Button { Text = region.IsCurrent ? $"◆ {region.DisplayName}" : region.DisplayName, TooltipText = region.ShortDescription, CustomMinimumSize = new Vector2(138, 54), Size = new Vector2(138, 54) };
                var position = center + new Vector2((float)region.WorldMapPosition.X * radius, (float)region.WorldMapPosition.Y * radius) - button.Size / 2;
                button.Position = position;
                button.AddThemeStyleboxOverride("normal", RegionStyle(region, region.Id == selectedRegionId, false));
                button.AddThemeStyleboxOverride("hover", RegionStyle(region, true, false));
                button.AddThemeStyleboxOverride("pressed", RegionStyle(region, true, true));
                button.Pressed += () => select(region.Id);
                AddChild(button); _buttons.Add(button);
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var center = new Vector2(325, 250);
            DrawCircle(center, 224, new Color(0.035f, 0.08f, 0.13f, 0.96f));
            DrawArc(center, 224, 0, Mathf.Tau, 96, new Color(0.28f, 0.62f, 0.78f, 0.85f), 3);
            DrawArc(center, 190, 0, Mathf.Tau, 96, new Color(0.18f, 0.33f, 0.44f, 0.7f), 1);
            foreach (var region in _regions)
            {
                var position = center + new Vector2((float)region.WorldMapPosition.X * 195f, (float)region.WorldMapPosition.Y * 195f);
                DrawCircle(position, 5, region.IsCurrent ? new Color("#facc15") : region.IsAvailable ? new Color("#4ade80") : new Color("#f87171"));
            }
            DrawString(ThemeDB.FallbackFont, new Vector2(250, 30), "VÒNG THẾ GIỚI", HorizontalAlignment.Left, -1, 16, new Color(0.72f, 0.85f, 0.92f));
        }

        private static StyleBoxFlat RegionStyle(WorldMapRegionSnapshot region, bool selected, bool pressed) => new()
        {
            BgColor = region.IsCurrent ? new Color("#8a6512") : region.IsAvailable ? new Color("#1f5c52") : new Color("#572f3a"),
            BorderColor = selected ? new Color("#f8e16c") : new Color(0.35f, 0.52f, 0.62f, 0.9f),
            BorderWidthLeft = selected ? 3 : 1,
            BorderWidthTop = selected ? 3 : 1,
            BorderWidthRight = selected ? 3 : 1,
            BorderWidthBottom = selected ? 3 : 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ShadowColor = pressed ? new Color(0, 0, 0, 0.35f) : new Color(0, 0, 0, 0.18f),
            ShadowSize = 4,
        };
    }
}
