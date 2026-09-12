# P3.0 Native Pixel Readability Root-Cause Audit

2026-09-12. Status: blocking; user visual acceptance pending. P3.1+ and full Phase 4 are paused. This is an asset/source audit, not gameplay acceptance. No automated gameplay tests were run.

## Diagnosis recorded before implementation

| Asset / system | Native asset readable? | Godot degradation? | Root cause | Owner | Required systemic fix |
|---|---|---|---|---|---|
| Player | FAIL: highlights fragment head/torso/limbs; no weapon in source | Fractional scale confirmed; severity in Arena pending | BOTH: detailed 1254px source reduced to a 20×37px body; per-direction scaling | Art skill + Godot presentation | Final-pixel brief, common body/anchor contract, semantic QA; author at intended raster height |
| Skeleton static enemy/ally | PASS for skeleton identity; thin limb/rib debt | 1.25× repeats rows unevenly | GODOT_SCALE_DEGRADATION (sampling risk; raw identity survives) | Godot + art handoff | Preserve thin forms in final raster; don't use static approval as animation approval |
| Goblin | Not present in runtime catalog; source production candidate exists | Not measurable in Arena for absent entry | Missing runtime representation | Art production | Review source candidate separately; no invented runtime measurement |
| Pillar | Pillar silhouette PASS, material detail noisy | 1.964286× | GODOT_SCALE_DEGRADATION; cluster quality debt | Art workflow + Godot | Larger native architectural contract; broad shaft/base/capital masses |
| Chest | FAIL at 1×: box-like texture; lid/clasp weak | 1.080357× | BOTH | Art workflow + Godot | Explicit lid/body/lock masses at ~30px visible height |
| Shrine | FAIL: jagged structure, ambiguous ritual identity | 2.941860× | BOTH | Art workflow + Godot | Semantic altar/platform contract at intended height |
| Portal | Arch/gate identity PASS | 3.75× uneven 3/4px clusters | GODOT_SCALE_DEGRADATION; detail debt | Art workflow + Godot | Purpose-built major gate canvas; preserve opening |
| Terrain | FAIL: path/ruin differ mostly by scattered pixels | 32→32, no fractional scale | ASSET_READABILITY_FAIL + composition | Art kit + Phase 4 | Connected material families and transitions; assembled preview before integration |
| Display/import | Integer viewport stretch; nearest inherited | No smoothing identified in source path | Not primary cause | Godot | Preserve settings; verify real Arena capture |

Classifications are agent audit observations, not user approvals. See `before/page-*.png` at actual dimensions and `before/measurements.json` for all-frame bounds. Offline scaled previews are explicitly not engine captures.

## Rendering trace

`project.godot`: viewport 640×360; override 1280×720; fullscreen mode; viewport stretch with integer scale; aspect unspecified (engine default keep). `scenes/Arena.tscn`: root texture_filter=1 (nearest), no scaled parent; Camera2D has no zoom override and smoothing disabled. Arena snaps player, enemy/ally presentation positions and camera to pixels. No camera zoom assignments found.

`CanonicalAssetCatalog.Texture()` loads the original PNG using Image.LoadFromFile and ImageTexture.CreateFromImage. It bypasses imported compressed textures. No mipmap generation or lossy compression call exists on this path. SpriteFrames use explicit integer atlas regions with FilterClip; speed 1000 with millisecond frame durations. Sprites are Centered=false, Offset=-pivot, and their scale comes from VisualScaleFor. Actor parents do not add a scale. World objects use DrawTextureRect at origin - pivot*scale, which can be fractional even when origin is integer.

`VisualScaleFor` divides policy baseline×ratio by stored opaque height. `ResolveWorldScale` then multiplies a clamped 0.8–1.2 instance override. Thus integer display scaling does not fix nonuniform source-texel replication inside the 640×360 render. Scaling above 1 repeats some source rows/columns more often; below 1 removes some. Half-step scaling is not automatically safe either. The exact dropped/repeated features depend on raster phase; offline evidence cannot certify Godot sampling.

World Scale policy currently says **proposed**, with baseline 55px; ratios remain untouched. Actor clip entries without metrics fall back to 1 (Player 1.35). In particular static Skeleton is 1.25 while animated Skeleton entries generally fall back to 1. This means ratio declarations alone do not guarantee consistent body size.

| Representative | Canvas | Opaque height | Target | Scale | Ideal rendered height |
|---|---:|---:|---:|---:|---:|
| Player idle S | 64×64 | 37 | 55 | 1.486486 | 55 |
| Player idle N/E | 64×64 | 33 | 55 | 1.666667 | 55 |
| Player idle W | 64×64 | 28 | 55 | 1.964286 | 55 |
| Skeleton enemy/ally static S | 64×64 | 44 | 55 | 1.25 | 55 |
| Goblin | absent | — | 49.5 policy | — | — |
| Pillar | 64×64 | 49 | 96.25 | 1.964286 | 96.25 |
| Chest | 64×64 | 28 | 30.25 | 1.080357 | 30.25 |
| Shrine | 64×64 | 43 | 126.5 | 2.941860 | 126.5 |
| Portal | 64×64 | 44 | 165 | 3.75 | 165 |
| Ground/path/ruin mask15 | 32×32 | 32 | 32 | 1 | 32 |
| Skeleton pickup | 24×24 | 18 | 17.6 | 0.977778 | 17.6 |

All listed non-unit scales are arbitrary fractions, none are integer or half-step. Ideal height is mathematical, not measured engine output. Stored Player move bounds are stale: S first frame height 42 vs stored 41; N 42 vs 38; W 39 vs 36; E 42 vs 30. Re-normalizing each current frame to its bbox would conceal anatomy inconsistency and make scale depend on the pose.

## Source/workflow trace

Player idle S provenance matches production `art/production/work/player.slice_01/batch-001/recipes/player.base.idle.s.recipe.json`: RGB 1254×1254 source, source bbox [430,326,831,1051], crop 464×821 reduced to 24×42 then placed at [20,14] in 64×64. Visible body ends up 20×37. Roughly 19.5 source pixels become one runtime pixel. Source inspection shows many tiny buckles, seams and armor highlights. `normalize_player_slice01.py` maps a fixed palette then performs nearest reduction, uses foreground bottom as foot, and repeats one generated pose. No weapon was requested in the old prompt; weapon absence is source scope, not a lost Godot edge.

`normalize_monster_batch.py` splits the source into equal-width cells, calculates common scale per strip (not per actor family), fits 46px height / 58px width, maps palette after reduction, and anchors against maximum opaque bottom. This can hide ownership errors and changes apparent size between independently produced clips/directions. Its neutral-background removal also runs on alpha-bearing sources.

`world.slice_01/world_pipeline.py` fits all props into 54px inside a 64px canvas, irrespective of their physical role; tiles are whole-image nearest reduced to 32×32. Its palette[:max_colors] discards later ramps: tile output receives ash/stone/bone, props add cloth but omit vegetation/amber. This contradicts the prompts' terrain grass/amber content. Quantization therefore can collapse material distinction. Do not fix this by increasing brightness.

Existing SKILL/source-to-runtime prose already mentions native review and common transforms, but provides no required evidence record for final gameplay scale, critical feature thickness or true-back identity. The generated preview defaults to 3× or 6×; world previews use 2×/3×. Validator checks file contracts. production.py's integration state checks technical QA and a user decision; the legacy trial snapshot bypasses visual debt for existing outputs. These are systemic enforcement gaps, not absence of every readability sentence.

## Separate debts

**PLAYER_READABILITY_DEBT:** current 1× dark armor reads as disconnected highlights; head/torso/arms lack large internal value masses; legs and hand gaps fragile; no separated weapon in idle source. Common canvas/pivot does not mean common anatomy. Idle S/N/W/E visible heights 37/33/28/33; head size and torso proportions differ. A shared transform across all directions is required.

**PLAYER_DIRECTION_ASSET_DEBT:** N idle still reads as face/front chest rather than back. E/W appear oblique and differ in anatomy; do not repair by relabeling/flipping N. Movement needs all-frame direction and anatomy review separately from static samples. User alone validates motion and gameplay.

**PHASE_4_ASSET_SKILL_BLOCKER:** ground/path/ruin/wall families exist, so this is not simply missing all terrain. AshGravesTerrainLayer draws a solid base, hash-selected isolated 32px ground squares at 0.38 alpha, rectangular clearings and ruin foundations, with Wang-mask path/wall overlays. Sparse random squares and rectangular composition amplify the weak tile family identities. Graveyard floor, shrine floor and portal approach need coordinated semantic kit/transition evidence before Phase 4. No full map redesign in P3.0.

## Acceptance still required

### Implementation and capture update

The skill/workflow refactor and five static samples are now saved. `before/arena-native-640x360.png` is a real Arena viewport capture; `batch/arena-samples-native-640x360.png` is the same Arena with the opt-in F7 static row at integer 1:1 raster scale. F12 saves directly from the viewport. These are before/sample-comparison captures, **not a completed gameplay replacement**. Existing gameplay actors and terrain remain unchanged.

The sample row retains nearest filtering and integer positions/pivots. Player and Skeleton target 55px; pillar uses 96px vs physical target 96.25; chest uses 30px vs30.25. The quarter-pixel difference is an explicit calibration approximation only; no policy ratios were changed. The static Player now has broader armor planes, Skeleton has clearer skull/ribs, pillar has clear large structure. Chest revision1 and terrain remain unsatisfactory candidates; user explicitly rejected chest revision2 as ugly. The rejected revision is excluded from the sample package and reference use. A terrain revision2 call has no completed result available; it is not counted.

The five-sample PNG technical validator passed; C# build passed with one nullable warning at Arena's existing Player sprite position assignment. No automated gameplay acceptance tests were run. Native preview tooling ran on the real sample manifest. The new readiness gate checks current-hash preview evidence and separate semantic/direction/body/motion records, without inferring quality from PNG validity. Legacy normalizers retain executable behavior but the skill directs new production through explicit recipes.

Remaining: all-direction Player/monster samples and motion review, revised chest brief preserving style, complete semantic terrain family/transitions, production-grade raster policy for existing animated actors/props, and the user's visual/gameplay acceptance. Do not broaden regeneration or resume P3.1+.

Before/after native 640×360 Arena captures, equivalent-size raw comparison, user ~1-second identity review, and manual checklist in `docs/V2.5/source-audit.md`. Movement, animation timing, collision, camera, save data, combat balance and hierarchy require user manual review; compile evidence cannot mark these passed. No broad regeneration until the small representative batch and P3.0 are accepted.
