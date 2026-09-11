# Presentation Motion and Semantic Map Audit — 2026-09-11

## Acceptance status

The preceding Gameplay Readability pass **failed manual visual acceptance**. A successful build is not visual acceptance. This pass preserves the accepted movement recovery and V6/V2.5 save separation, but requires a new user movement and visual review.

## Animation evidence and classification

The four resolved Player movement asset routes are correct:

| Input direction | AssetId | SpriteFrames animation | Frames | Source timing |
| --- | --- | --- | ---: | --- |
| down/front | `player.base.move.s` | `move_s` | 6 | 100 ms × 6 |
| left | `player.base.move.w` | `move_w` | 6 | 100 ms × 6 |
| right | `player.base.move.e` | `move_e` | 6 | 100 ms × 6 |
| up/back | `player.base.move.n` | `move_n` | 6 | 100 ms × 6 |

`CanonicalAssetCatalog.BuildFrames` configures each animation at speed `1000` with the six source duration values; this represents 100 ms per frame (10 fps), not a one-frame Godot timeline.

However, direct RGBA SHA-256 comparison of every 64×64 atlas rectangle found **one unique frame hash per six-frame movement clip**:

| AssetId | Unique pixel frames / declared frames |
| --- | ---: |
| `player.base.move.s` | 1 / 6 |
| `player.base.move.w` | 1 / 6 |
| `player.base.move.e` | 1 / 6 |
| `player.base.move.n` | 1 / 6 |

This is classification **C — source frames are visually insufficient**. It is not a missing AssetId or a six-frame-count integration failure. No source image was regenerated or altered.

For manual runtime confirmation, DEBUG builds now emit `PRESENTATION_ANIMATION_PROBE` every 200 ms while a movement key is held. The line records resolved AssetId, animation name, frame count, speed, per-frame duration, `currentFrame`, and `isPlaying`. The native Godot window was not exposed by the host capture connector in this task, so no live `currentFrame` sequence or screenshot is fabricated here.

## Motion architecture

- `AshGravesTerrainLayer` now owns 16×16-tile cached terrain chunks (256 chunks for the current 256×256 logical world). A chunk records ground/path/ruin/wall draw commands only after initial region/layout construction.
- `Arena._Process` no longer rebuilds static terrain. It interpolates presentation targets for Player, enemies, Allies and Camera2D at render cadence; only the final transform is rounded to logical pixel coordinates.
- Simulation positions, collision, input and fixed-step movement remain untouched. The camera and actor sprites do not receive direct snapshot assignment in `RefreshSnapshot`.
- Dynamic actor grounding/health overlays still redraw at presentation cadence; this does not invalidate terrain chunks.

## Visual metrics: actual silhouette, not canvas

`data/v2.5/presentation-visual-metrics.v2.5.json` is a validated Presentation-only contract. Every entry records canonical canvas size, measured opaque bounds, canonical ground pivot, intended visual footprint and optional collision field. It is checked against the hash-validated asset catalog on startup.

| Visual subject | Measured opaque bounds | Intended logical footprint |
| --- | ---: | ---: |
| Player move south | 22×41 | ~30×55 |
| Debris / rock / chest | 41×27 / 33×26 / 29×28 | 24×16 / 20×16 / 24×23 |
| Pillar grave | 29×49 | 32×54 |
| Shrine | 36×43 | 88×105 |
| Landmark / tree-spire | 22×41 / 21×50 | 32×60 / 32×76 |
| Portal gate | 43×44 | ~125×128 |
| Soul pickup | 12×18 | 14×21 |

At the retained 2× output, a 125×128 logical gate presents around 250×256 physical px. The visual hierarchy is now based on actual opaque silhouettes: pickup/debris < small prop < grave < person < tall marker < shrine/gate. It does not change PNG data, collision or canonical positions.

## Ash Graves semantic composition

- The terrain cache uses dark negative space with sparse ash material patches instead of a full screen of the same 32×32 ground tile.
- The canonical four-tile road topology remains the navigable main corridor; it uses the existing path Wang masks and visually separates from the quiet ash field.
- Shrine, landmark and exit receive quiet clearings. The Ruins chunk is represented by three broken-foundation areas, leaving negative space and roads rather than filling an entire chunk with a uniform matrix.
- Existing authorised but previously unused Trial props now dress the Shrine: `seal`, two `torch`, two `banner`, plus off-path `pillar`/`rock`/`debris` grave clusters. They are Presentation-only scenery, never simulated collision, interaction or save objects.

## Environment audit

`seal`, `banner`, `bridge_segment` and `torch` were already copied and authorized in the Trial catalog, but were not emitted by `V25WorldLayout`; this pass uses context-appropriate shrine dressing for `seal`, `banner` and `torch`. `bridge_segment` remains cataloged but unused because no canonical water crossing topology authorizes its placement.

The source workspace also has 16 generated, technically-QA water mask outputs (`world.ash_graves.water.mask00`–`mask15`). They are `slice01:false`, outside the locked 161 logical Trial scope and not present in the Godot Trial catalog. They were not copied or imported without a scope/authorization change.

The exact missing `world.ash_graves.npc`, `world.ash_graves.arena`, and `world.ash_graves.secret` remain source-pipeline gaps and still use explicit magenta missing markers. The 12 unavailable Skeleton Enemy clips remain an authorization/source-state gap.

## Evidence and limits

- `dotnet build solo_vs_mortal_godot.csproj --no-restore -v:minimal` succeeded with 0 warnings and 0 errors.
- Presentation metrics static audit: 23 records, 0 duplicate AssetIds, 0 absent catalog references, 0 canvas mismatches, `BASE_TILE=32`.
- No automated gameplay/parity/soak/acceptance harness was run.
- The computer-use connector exposes no native Godot surface for this task; requested before/after screenshots therefore remain unavailable rather than fabricated. User manual screenshots and visual acceptance are still required.

No gameplay rule, collision, save schema, canonical progression, source art, asset-production workspace, renderer, 640×360 viewport or HUD migration was modified.
