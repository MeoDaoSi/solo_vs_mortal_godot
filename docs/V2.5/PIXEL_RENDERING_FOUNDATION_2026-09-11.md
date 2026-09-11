# Pixel Rendering Foundation — 2026-09-11

## Scope and rendering contract

This Presentation-only foundation changes the real Godot project's display and normal HUD for a pixel-scale review. It does not regenerate, alter, or copy source art, and it does not modify `C:/ws/asset-production-system`.

- Virtual viewport: **640×360**.
- Initial window override: **1280×720** (an exact 2× integer presentation of the virtual viewport).
- Stretch: `viewport`, aspect `keep`, integer scale mode.
- Canvas default filtering and the Arena root both use `nearest`.
- Only 2D transform pixel snapping is enabled. Camera smoothing is disabled; vertex snapping remains off so the two snap modes are not combined.
- `BASE_TILE = 32×32`. `Arena` refuses to draw Trial terrain tiles when the locked layout tile size or the ground-frame size is not 32×32. Simulation geometry, collision, navigation and sprite scale are unchanged.

## Normal gameplay HUD

`CanvasLayer/HudRoot` is a full-rect `Control`; the normal HUD uses top/bottom anchored `MarginContainer` rows, `HBoxContainer`, and `PanelContainer` rather than 1280-specific coordinates.

- Logical outer margin: 6 px; row separation: 6 px; panel content margin: 5–6 px.
- Top row: 176×46 HP/SP panel, 142×28 compact resource panel, flexible spacer, 70×70 minimap.
- Bottom row: one 22 px-high essential combat/Soul label, map hint and 72×22 details button.
- Font sizes: normal 9–10 px; toast/important vitality 10 px; detail/map titles 12–13 px; map legend 8 px.

The permanently visible HUD now contains only region + HP/SP, Coin/XP/Soul counters, minimap, a compact combat/Soul state, toast, map hint, and **I: Chi tiết**. Tutorial key lists, detailed character stats, inventory, Hồn Phiên, integration controls and development information are not in the normal HUD.

`I` opens an overlay anchored to all viewport edges with 12 px outer and 8 px inner logical margins. It contains the existing inventory, progression, Soul Banner, owned Soul list, and action/integration/debug controls. `Esc` or **Đóng** closes it. The Asset Integration Trial button therefore remains available in the **Hành động** tab without occupying the normal gameplay view; F10 remains its development-only direct route.

The world map overlay was also reduced to fit entirely in the 640×360 virtual viewport.

## Current visual footprints (logical / 1280×720 physical)

| Entity | Source/rendered envelope | Footprint |
| --- | --- | --- |
| Player R01 | 64×64 frame, pivot (32,56), no sprite scale | 64×64 / 128×128 px; 2×2 base tiles |
| Skeleton Enemy R01 | 64×64 frame, pivot (32,56), no sprite scale | 64×64 / 128×128 px; 2×2 base tiles. Missing idle/move renders a 36×36 logical marker instead. |
| Skeleton Ally R01 | 64×64 frame, pivot (32,56), no sprite scale | 64×64 / 128×128 px; 2×2 base tiles |
| Skeleton Soul pickup | 24×24 frame, pivot (12,20), no scale | 24×24 / 48×48 px; 0.75×0.75 base tile |
| NPC/world object | Existing Presentation renderer envelope if a matching asset exists | 84×84 / 168×168 px at `PresentationScale=1`; current NPC asset is missing and thus uses a 24×24 logical marker |

The 2× physical values are viewport presentation, not an asset-scale edit.

## Explicit missing mappings — intentionally not substituted

These currently draw magenta missing markers whenever their runtime object/state is visible:

- `world.ash_graves.npc` (all canonical NPCs)
- `world.ash_graves.arena`
- `world.ash_graves.secret`
- `soul.skeleton.rank01.enemy.idle.s`, `.idle.w`, `.idle.e`, `.idle.n`
- `soul.skeleton.rank01.enemy.move.s`, `.move.w`, `.move.e`, `.move.n`
- `soul.skeleton.rank01.enemy.attack.w`, `.attack.e`, `.attack.n`
- `soul.skeleton.rank01.enemy.hit.n`

`tiles.arena.ground` is also absent from the Trial catalog. It produces the pre-existing neutral fallback background rather than a magenta marker because the canonical generated world has no background asset ID. No unrelated asset is substituted.

Asset keys, NPC IDs, integration labels and the textual `MISSING:<AssetId>` label are now behind `ShowPresentationDebug=false`. The visible magenta marker is preserved as the normal-production error signal; no actor, prop or prototype is used as a fallback.

## NPC mapping root cause

1. Locked canonical content declares NPC IDs `an`, `kha`, and `linh`.
2. `V25WorldLayout.Populate` emits each as type `npc`, whose exact presentation key is `world.ash_graves.npc`.
3. `CanonicalAssetCatalog` contains **0** records for that key; Trial `provenance.json` contains **0** records and the copied Trial package contains **0** matching PNG files. Read-only source-art audit also found no `world.ash_graves.npc` production record/output.
4. `Arena.RebuildMapTextures` therefore skips the key. `_mapTextures` has no Godot `AtlasTexture`/`Texture2D` entry, so `_Draw` takes its explicit missing-marker branch.

The root cause is an absent authorized NPC AssetId/output, not a Godot import failure. No substitute was selected or copied.

## Technical evidence

- `dotnet build solo_vs_mortal_godot.csproj --no-restore -v:minimal` — compile/package validation only.
- Godot `4.7.2.stable.mono.official.ed1daf0bf` Arena startup smoke run — technical initialization only, not gameplay acceptance.
- A normal Godot DEBUG run exited `0` and logged `PIXEL_RENDERING_FOUNDATION viewport=(640, 360); window=(1280, 720); baseTile=32`. Native screenshot capture was unavailable from the host connector, so this record preserves direct runtime proof instead of fabricating an image.

No automated gameplay, parity, soak, or acceptance harness was run. User visual/gameplay review remains required.
