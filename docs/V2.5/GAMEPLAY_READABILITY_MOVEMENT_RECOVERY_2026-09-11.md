# Gameplay Readability + Movement Recovery — 2026-09-11

## Scope

This is a Presentation/save-flow recovery pass for the real 640×360 Arena. It preserves the existing virtual viewport, integer scale, filtering, fullscreen/window behaviour, HUD migration, canonical world geometry, collisions, save schemas and source-art files. No source art was generated or changed.

## Movement recovery

### Root cause

The InputMap remains valid: **WASD** and arrow keys feed `move_left`, `move_right`, `move_up`, and `move_down`; **E** feeds `acquire_soul`. `Arena._PhysicsProcess` obtains that vector, calls `GameApplication.SetInput`, advances `GameApplication.Tick`, and the canonical `PlayerSystem` applies swept movement against the canonical collision layout.

The actual regression happened before that path. If slot 1 had no V2.5 envelope but the preserved `user://solo_vs_mortal_save_v6.json` existed, `Arena.TryLoad` attempted to restore that incompatible legacy JSON into the canonical V2.5 session. The lossless restore rejected it, then `_saveWritesBlocked` made `_PhysicsProcess` feed zero input and return on every tick. This was save-safety code accidentally becoming an input lock.

The V6 JSON is now left completely untouched. In that case the already-started fresh V2.5 session remains playable and writes only to `user://solo_vs_mortal_save_v25.json` when the normal save flow occurs. A bad **V2.5** envelope still blocks writes/input deliberately so an invalid current save cannot be silently overwritten. Debug startup logs the explicit `MOVEMENT_RECOVERY` state when the V6 preservation path is used.

## Ash Graves presentation pass

- Ground, path, ruin and outer-wall mask families remain the canonical 32×32 Ash Graves tiles. The existing deterministic NESW mask renderer remains driven by the locked road/layout blueprint, never collision or gameplay state.
- Low-contrast, Presentation-only focal clearings now frame the canonical Shrine, Field landmark and exit portal. They make camp, landmark and exit visually distinct without adding a fake route, obstacle or interaction.
- Four exact Trial props (pillar, rock, debris, pillar) frame the starting Shrine off the authored road. They are deliberately pass-through dressing: the composition pass does not change navigation or collision ownership.
- Runtime objects now render from their own canonical frame size and pivot rather than the previous universal 84×84 center-anchored box. A restrained feet shadow provides value separation for props, Player, enemies, Allies and Soul pickups.

## Logical footprints at BASE_TILE = 32×32

| Subject | Logical envelope | Hierarchy / status |
| --- | ---: | --- |
| Debris | 32×32 | small prop (1.0 tile) |
| Rock | ~36×36 | small prop (1.1 tiles) |
| Chest | ~40×40 | small prop (1.25 tiles) |
| Pillar/grave marker | ~50×50 | grave (1.56 tiles) |
| Player | 64×64 | character (2×2 tiles), unchanged source scale |
| Skeleton enemy / Ally | 64×64 | character (2×2 tiles), unchanged source scale |
| Soul pickup | 24×24 | readable collectable (0.75 tile), unchanged source scale |
| NPC expected canvas | ~56×56 | between grave and Player, but exact asset is missing |
| Shrine | ~76×76 | tall structure (2.38 tiles) |
| Landmark | 80×80 | tall structure (2.5 tiles) |
| Tree/spire | ~82×82 | tall structure (2.56 tiles) |
| Portal/gate | ~92×92 | major structure (2.88 tiles) |
| Arena expected canvas | 96×96 | major structure target, but exact asset is missing |

Physical output is exactly 2× these numbers at the existing 1280×720 window override. The hierarchy is `small prop < grave < character < tall structure < gate/major structure`; no Player/monster/Soul source sprite was resized.

## Explicit remaining asset gaps

These retain magenta `MISSING:<AssetId>` presentation rather than substitute unrelated art:

- `world.ash_graves.npc`: blocks readable NPC identity near the starting Shrine. The canonical world definition emits the exact key, but the Trial catalog/provenance/copied package has no record or PNG. This is an asset-pipeline/source-output gap, not a Godot importer or renderer bug.
- `world.ash_graves.arena` and `world.ash_graves.secret`: exact world-object source outputs are absent. This is an asset-pipeline/source-output gap.
- Twelve exact Skeleton Enemy clips remain unavailable under the authorised Trial set: idle all four directions; move all four; attack W/E/N; hit N. This is a Trial authorization/source-state gap, not a fallback candidate. The existing static compatibility asset and eligible clips are not used to impersonate those exact requested clips.
- `tiles.arena.ground` remains absent from the Trial catalog. Ash Graves uses its available ground masks, so this does not block the current area; the neutral fallback remains explicit elsewhere.

## Files and technical evidence

- `src/Presentation/Arena.cs` — V6-safe movement recovery, pivot-aware hierarchy, shadows and Ash Graves composition pass.
- `docs/V2.5/GAMEPLAY_READABILITY_MOVEMENT_RECOVERY_2026-09-11.md` — this record.
- `dotnet build solo_vs_mortal_godot.csproj --no-restore -v:minimal` — succeeded, 0 warnings and 0 errors. This is compile evidence only, not gameplay acceptance.

No automated gameplay, parity, soak or acceptance runner was created or executed. No commit or push was performed.

## Manual visual/gameplay check

1. Open the project in Godot 4.7.2 and run the real `Arena` scene.
2. With the Arena focused, move using **WASD** or arrow keys. The initial V6-preservation toast may appear, but movement must remain enabled. A current invalid V2.5 save is intentionally still protected instead of overwritten.
3. Walk the road around the Shrine. Confirm the road reads as a corridor through ash ground, the shrine clearing is distinguishable, and the off-path marker group reads as grave dressing without blocking travel.
4. Compare the Player to a pillar, shrine, landmark/tree-spire and portal: Player should be larger than a grave marker and distinctly smaller than tall/gate structures.
5. Judge contrast, palette, path coherence, pivot/shadow readability and the explicit magenta markers. Do not treat build success as visual or gameplay acceptance.
