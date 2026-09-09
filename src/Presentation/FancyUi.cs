using Godot;

namespace SoloVsMortal.Presentation;

/// <summary>Central holder for the reusable Fancy StyleBoxes UI style set.
/// All Inventory, Character, Progression, Soul Banner (Hồn Phiên), World Map and
/// Skill UI should load styles from the FancyUiStyles autoload so the visual
/// language stays consistent. The style resources themselves are built from the
/// installed StyleboxFancy plugin (see src/Presentation/fancy_ui_styles.gd).</summary>
public static class FancyUi
{
    public static readonly Color TextBrush = new("#f2d795");
    public static readonly Color TextDim = new("#bfa877");
    public static readonly Color TextBright = new("#ffe9b3");

    public static StyleBox MainPanelBox() => GetStyle("main_panel");
    public static StyleBox SecondaryPanelBox() => GetStyle("secondary_panel");
    public static StyleBox PopupBox() => GetStyle("popup");
    public static StyleBox TooltipBox() => GetStyle("tooltip");
    public static StyleBox ItemSlotBox() => GetStyle("item_slot");
    public static StyleBox ItemSlotSelectedBox() => GetStyle("item_slot_selected");
    public static StyleBox HeaderBox() => GetStyle("header");

    /// <summary>Applies the panel style set to a Panel/PanelContainer node.</summary>
    public static void ApplyPanel(Control node, bool main = true) =>
        Styles.Call("ApplyPanel", node, main);

    /// <summary>Applies the full button state set to a Button.</summary>
    public static void ApplyButton(Button button) => Styles.Call("ApplyButton", button);

    /// <summary>Applies the item slot styles to an inventory slot button.</summary>
    public static void ApplyItemSlot(Button slot, bool selected = false) =>
        Styles.Call("ApplyItemSlot", slot, selected);

    /// <summary>Applies the tab styles to a TabContainer.</summary>
    public static void ApplyTabs(TabContainer tabs) => Styles.Call("ApplyTabs", tabs);

    /// <summary>Applies the popup style set to an AcceptDialog/ConfirmationDialog.</summary>
    public static void ApplyDialogWindow(AcceptDialog dialog)
    {
        dialog.AddThemeStyleboxOverride("panel", PopupBox());
        dialog.AddThemeColorOverride("title_color", TextBright);
        dialog.AddThemeFontSizeOverride("title_font_size", 15);
        ApplyButton(dialog.GetOkButton());
        if (dialog is ConfirmationDialog confirm)
            ApplyButton(confirm.GetCancelButton());
    }

    /// <summary>Applies the header/title box style to a Panel used as a headers strip.</summary>
    public static void ApplyHeader(Panel header) => header.AddThemeStyleboxOverride("panel", HeaderBox());

    /// <summary>Builds a shared Theme that routes the game's tooltips through the
    /// Fancy StyleBox set without touching Godot's fallback theme.</summary>
    public static Theme BuildTooltipTheme()
    {
        var theme = new Theme();
        theme.SetStylebox("panel", "TooltipPanel", TooltipBox());
        theme.SetColor("font_color", "TooltipLabel", TextBrush);
        theme.SetColor("font_shadow_color", "TooltipLabel", Colors.Black);
        return theme;
    }

    private static Node Styles
    {
        get
        {
            var root = (Engine.GetMainLoop() as SceneTree)?.Root;
            return root?.GetNode("FancyUiStyles")
                   ?? throw new InvalidOperationException("FancyUiStyles autoload is not available. Was it registered in project.godot?");
        }
    }

    private static StyleBox GetStyle(string name) => (StyleBox)Styles.Get(name);
}