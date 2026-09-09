extends Node

## Central reusable Fancy StyleBoxes UI style set for Solo vs Mortal.
## Builds all UI styles from the installed StyleboxFancy plugin and exposes them
## to both GDScript and C# so Inventory, Character, Progression, Soul Banner
## (Hồn Phiên), World Map and future Skill UI share one consistent visual language.
##
## Visual language: pixel-art xianxia/cultivation (tu tiên) + dark dungeon.
## Dark stone / black iron fills, aged bronze & gold borders, subtle jade accents.
##
## Access from C#:
##   var styles = GetNode<Node>("/root/FancyUiStyles")
##   ((StyleBox)styles.Get("main_panel"))
##   styles.ApplyButton(myButton)

const StyleBoxFancyScript = preload("res://addons/StyleboxFancy/StyleBoxFancy.gd")
const StyleBorderScript = preload("res://addons/StyleboxFancy/StyleBorder.gd")

# ---- Palette (dark iron, aged bronze, gold, jade) ----
const IRON_DEEP := Color(0.086, 0.075, 0.047)      # Main panel fill
const IRON_SECONDARY := Color(0.075, 0.066, 0.042) # Secondary panel fill
const IRON_DUSK := Color(0.13, 0.11, 0.07)         # Button rest fill
const IRON_HOVER := Color(0.2, 0.17, 0.1)          # Button hover fill
const IRON_PRESSED := Color(0.08, 0.068, 0.042)    # Button pressed fill
const IRON_DISABLED := Color(0.09, 0.088, 0.082)   # Button disabled fill
const BRONZE := Color(0.43, 0.34, 0.2)             # Outer border rest
const BRONZE_DIM := Color(0.37, 0.3, 0.18)         # Outer border secondary
const GOLD_DARK := Color(0.62, 0.48, 0.24)         # Bright border / selected
const GOLD := Color(0.79, 0.66, 0.42)              # Highlight border
const JADE := Color(0.25, 0.45, 0.39)              # Spiritual accent border

# ---- Shared content margins (symbols reused by several styles) ----
const PANEL_MARGIN := Vector4(12, 10, 12, 10)
const SECONDARY_MARGIN := Vector4(10, 8, 10, 8)
const POPUP_MARGIN := Vector4(14, 12, 14, 12)
const TOOLTIP_MARGIN := Vector4(8, 6, 8, 6)
const SLOT_MARGIN := Vector4(4, 3, 4, 3)
const BUTTON_MARGIN := Vector4(10, 5, 10, 5)
const TAB_MARGIN := Vector4(12, 4, 12, 4)
const HEADER_MARGIN := Vector4(14, 8, 14, 8)

var main_panel: StyleBox
var secondary_panel: StyleBox
var popup: StyleBox
var tooltip: StyleBox
var item_slot: StyleBox
var item_slot_selected: StyleBox
var button_normal: StyleBox
var button_hover: StyleBox
var button_pressed: StyleBox
var button_disabled: StyleBox
var tab_normal: StyleBox
var tab_selected: StyleBox
var header: StyleBox

func _ready() -> void:
	main_panel = _panel_style(
		IRON_DEEP, BRONZE, 2, 5, 2.0,
		GOLD_DARK, 2, true, PANEL_MARGIN
	)
	secondary_panel = _panel_style(
		IRON_SECONDARY, BRONZE_DIM, 1, 4, 2.0,
		Color.TRANSPARENT, 0, true, SECONDARY_MARGIN
	)
	popup = _panel_style(
		Color(0.09, 0.078, 0.05, 0.98), GOLD_DARK, 2, 4, 2.0,
		Color(0.3, 0.24, 0.13, 0.9), 2, true, POPUP_MARGIN
	)
	tooltip = _panel_style(
		Color(0.055, 0.06, 0.05, 0.96), JADE, 1, 3, 2.0,
		Color.TRANSPARENT, 0, false, TOOLTIP_MARGIN
	)
	item_slot = _panel_style(
		Color(0.06, 0.053, 0.035), Color(0.3, 0.25, 0.15), 1, 3, 2.0,
		Color.TRANSPARENT, 0, false, SLOT_MARGIN
	)
	item_slot_selected = _panel_style(
		Color(0.08, 0.075, 0.05), GOLD, 2, 3, 2.0,
		JADE, 2, false, SLOT_MARGIN
	)
	button_normal = _button_style(IRON_DUSK, BRONZE, false, BUTTON_MARGIN)
	button_hover = _button_style(IRON_HOVER, GOLD, false, BUTTON_MARGIN)
	button_pressed = _button_style(IRON_PRESSED, Color(0.55, 0.44, 0.22), true, BUTTON_MARGIN)
	button_disabled = _button_style(IRON_DISABLED, Color(0.22, 0.2, 0.16), false, BUTTON_MARGIN)
	tab_normal = _panel_style(
		Color(0.07, 0.062, 0.04), Color(0.3, 0.25, 0.15), 1, 4, 2.0,
		Color.TRANSPARENT, 0, false, TAB_MARGIN
	)
	tab_selected = _panel_style(
		Color(0.12, 0.1, 0.06), GOLD_DARK, 1, 4, 2.0,
		Color(0.79, 0.65, 0.32, 0.6), 1, false, TAB_MARGIN
	)
	header = _panel_style(
		Color(0.11, 0.09, 0.055), GOLD_DARK, 2, 4, 2.0,
		JADE, 2, true, HEADER_MARGIN
	)

## Builds a panel style: fill + outer border + optional inner accent border + shadow.
func _panel_style(
	fill: Color,
	outer_color: Color,
	outer_width: int,
	radius: int,
	curvature: float,
	inner_color: Color,
	inner_inset: int,
	shadow: bool,
	margins: Vector4
) -> StyleBoxFancy:
	var style := StyleBoxFancyScript.new()
	style.color = fill
	style.set_corner_radius_all(radius)
	style.set_corner_curvature_all(curvature)
	style.content_margin_left = margins.x
	style.content_margin_top = margins.y
	style.content_margin_right = margins.z
	style.content_margin_bottom = margins.w

	var outer := StyleBorderScript.new()
	outer.color = outer_color
	outer.set_width_all(outer_width)
	style.borders.append(outer)

	if inner_color.a > 0.0:
		var inner := StyleBorderScript.new()
		inner.color = inner_color
		inner.set_width_all(1)
		inner.inset_left = inner_inset
		inner.inset_top = inner_inset
		inner.inset_right = inner_inset
		inner.inset_bottom = inner_inset
		style.borders.append(inner)

	if shadow:
		style.shadow_enabled = true
		style.shadow_color = Color(0, 0, 0, 0.55)
		style.shadow_blur = 1
		style.shadow_offset = Vector2(0, 2)

	return style

## Builds a button style: dark fill + single border + optional pressed inset shadow.
func _button_style(fill: Color, border: Color, pressed := false, margins := BUTTON_MARGIN) -> StyleBoxFancy:
	var style := _panel_style(fill, border, 1, 4, 2.0, Color.TRANSPARENT, 0, false, margins)
	if pressed:
		style.shadow_enabled = true
		style.shadow_color = Color(0, 0, 0, 0.5)
		style.shadow_blur = 1
		style.shadow_offset = Vector2(0, 1)
		style.shadow_spread = Vector2(0, -1)
	return style

## Applies the full button state set to a Button.
func ApplyButton(button: Button) -> void:
	button.add_theme_stylebox_override("normal", button_normal)
	button.add_theme_stylebox_override("hover", button_hover)
	button.add_theme_stylebox_override("pressed", button_pressed)
	button.add_theme_stylebox_override("disabled", button_disabled)
	button.add_theme_font_size_override("font_size", 13)
	button.add_theme_color_override("font_color", Color(0.95, 0.85, 0.58))
	button.add_theme_color_override("font_hover_color", Color(1.0, 0.91, 0.7))
	button.add_theme_color_override("font_pressed_color", Color(0.75, 0.66, 0.47))
	button.add_theme_color_override("font_disabled_color", Color(0.75, 0.66, 0.47))

## Applies the item slot styles to an inventory slot Button.
func ApplyItemSlot(slot: Button, selected := false) -> void:
	slot.add_theme_stylebox_override("normal", item_slot_selected if selected else item_slot)
	slot.add_theme_stylebox_override("hover", item_slot_selected)
	slot.add_theme_stylebox_override("focus", item_slot_selected if selected else item_slot)
	slot.add_theme_stylebox_override("pressed", item_slot)
	slot.add_theme_font_size_override("font_size", 11)
	slot.add_theme_color_override("font_color", Color(0.95, 0.85, 0.58))
	slot.add_theme_color_override("font_hover_color", Color(1.0, 0.91, 0.7))
	slot.add_theme_color_override("font_pressed_color", Color(0.75, 0.66, 0.47))
	slot.add_theme_color_override("font_disabled_color", Color(0.75, 0.66, 0.47))
	slot.add_theme_color_override("font_focus_color", Color(1.0, 0.91, 0.7))

## Applies the panel style set to a Panel/PanelContainer (main or secondary).
func ApplyPanel(node: Control, main := true) -> void:
	node.add_theme_stylebox_override("panel", main_panel if main else secondary_panel)

## Applies the tab styles to a TabContainer.
func ApplyTabs(tabs: TabContainer) -> void:
	tabs.add_theme_stylebox_override("panel", main_panel)
	tabs.add_theme_stylebox_override("tab_selected", tab_selected)
	tabs.add_theme_stylebox_override("tab_unselected", tab_normal)
	tabs.add_theme_stylebox_override("tab_hovered", button_hover)
	tabs.add_theme_color_override("font_selected_color", Color(1.0, 0.91, 0.7))
	tabs.add_theme_color_override("font_unselected_color", Color(0.75, 0.66, 0.47))
	tabs.add_theme_color_override("font_hover_pressed_color", Color(0.95, 0.85, 0.58))