# Asset Integration Trial — 2026-09-10

## Scope and authority

This is a presentation-only, user visual-review trial. It is not ISSUE-05, Part 6 closure, W09 acceptance, final asset approval, or gameplay acceptance. Gameplay authority remains the hash-pinned `C:/ws/asset-production-system/game_spec` bundle; neither that bundle nor `data/v2.5/spec-lock.json` changed.

- Godot starting commit: `081840139b2d76a0505ac7d9095cf2116c9fa212` (`main`).
- Asset source commit: `e70a8a65abef24225d9a0bcf10c859e3fe09c980` (`master`).
- Runtime-trial authorization cutoff: `2026-09-10T11:33:56.999777+00:00`.
- Locked logical scope: 161 unique AssetIds. `slice-parts.json` supplies 158 production-work IDs; the catalog adds the recorded Player idle-S candidate and two explicit Skeleton static compatibility IDs.
- Eligibility follows `runtime-trial.json`: `generated`, `needs_rework`, or `user_approved`, source technical QA true, valid file/SHA/manifest, and no output after the cutoff. It grants only `integration_trial_authorized` use.

## Accounting

| Accounting bucket | Count | Notes |
|---|---:|---|
| Expected logical IDs | 161 | Scope lock and provenance matrix both have 161 unique IDs. |
| `ELIGIBLE_TRIAL` | 145 | Generated/needs-rework assets authorized for this trial only. |
| `REUSE_AUTHORIZED` | 4 | Skeleton pickup, banner icon, enemy south static, ally south static; prior reuse semantics retained. |
| Copied / cataloged | 149 | Hash-verified project-local PNGs in `asset-integration-trial-v001`. |
| Runtime-wired | 111 | A Presentation route exists; this does not mean every state appears in one session. |
| Viewer-only | 38 | Safely inspectable in the Trial viewer, without invented Simulation events or weapon offsets. |
| Missing / rejected | 12 | Source state `integration_ready`, which is outside the authorization base states. |

The last three rows reconcile the 149 copied assets; 149 + 12 = 161.

### Exact unavailable IDs

- `soul.skeleton.rank01.enemy.idle.s`
- `soul.skeleton.rank01.enemy.idle.w`
- `soul.skeleton.rank01.enemy.idle.e`
- `soul.skeleton.rank01.enemy.idle.n`
- `soul.skeleton.rank01.enemy.move.s`
- `soul.skeleton.rank01.enemy.move.w`
- `soul.skeleton.rank01.enemy.move.e`
- `soul.skeleton.rank01.enemy.move.n`
- `soul.skeleton.rank01.enemy.attack.w`
- `soul.skeleton.rank01.enemy.attack.e`
- `soul.skeleton.rank01.enemy.attack.n`
- `soul.skeleton.rank01.enemy.hit.n`

No unavailable ID is substituted by another species, rank, direction, prototype, or vendor asset. The live presentation uses `MISSING:<AssetId>` for a requested unavailable actor clip.

## Package and provenance

`assets/v2.5/asset-integration-trial-v001/provenance.json` is the reproducibility record. It keeps, per copied asset, the source output path/SHA, copied SHA, source manifest/SHA, status/review state, cutoff result, dimensions, explicit frame rectangles/durations, pivot, role/representation, direction/clip, layering, QA, approval status, and source contract. Its 161-row scope matrix records the twelve unavailable IDs rather than silently omitting them.

`data/v2.5/asset-catalog.v2.5.json` remains `complete: false`. It contains only project-relative paths and the existing strict canonical loader validates every runtime PNG SHA before use. The four reuse assets retain `user_reuse_authorized`; other Trial assets use `integration_trial_authorized`. Source `qa.visual`, `qa.motion`, and `qa.inEngine` were preserved; this task set none of them true.

Static/provenance validation passed:

- 161 expected and unique scope IDs; 149 copied and unique catalog IDs.
- All 149 source PNG SHA-256 values, copied SHA-256 values, and 23 source manifest SHA-256 values matched.
- Every copied file stays under the project root; no source absolute path is a runtime dependency.
- All frame rectangles were inside their PNGs; frame counts and millisecond durations matched source metadata.
- All 149 source-manifest entries matched AssetId, resolved output path, SHA, frame count, and duration array.
- No included output was after the authorization cutoff; no technical-QA, manifest, metadata, file, or hash failure occurred.
- The generated catalog has only the strict loader's schema/entry/frame fields and no duplicate IDs.

## Presentation wiring

| Area | Status | Trial behavior |
|---|---|---|
| Player | `WIRED` | Exact idle/move/attack clip and direction mapping: front/s, back/n, left/w, right/e. Hit/death have no fabricated caller and remain viewer-only. |
| Skeleton Enemy R01 | `PARTIAL` | Eligible attack/hit/death clips use movement-delta/last visual facing only. The twelve unavailable idle/move/direction clips stay explicit missing. The south static compatibility asset is retained in catalog/viewer, not used as an animation substitute. |
| Skeleton Ally R01 | `PARTIAL` | Eligible idle/move/attack clips use existing AI state plus visual facing. The old green `Modulate` was removed because the canonical Ally art supplies its own identity. Hit/disperse/summon/recall are viewer-only until an existing lifecycle signal safely drives them. |
| Soul pickup | `WIRED` | Existing canonical Soul pickup entity now resolves the exact Skeleton pickup asset. |
| Banner icon | `WIRED` | Appears in the existing Hồn Phiên tab when a banner exists. |
| Sword R01 | `PARTIAL` | The equipped MainHand R01 icon appears in the existing inventory tab; all nine sword assets are in the viewer. Actor overlay is intentionally disabled. |
| Ash Graves props | `PARTIAL` | Exact emitted `shrine`, `chest`, `portal`, `landmark`, `pillar`, `rock`, `tree_or_spire`, and `debris` object IDs render canonical props. `seal`, `banner`, `bridge_segment`, and `torch` have no matching emitted object and are viewer-only. |
| Ash Graves masks | `WIRED` | Presentation derives the documented NESW Wang bits from canonical roads, Ruins chunk, and outer wall in a camera-local tile neighborhood. It does not modify geometry, collision, navigation, hazards, encounters, or saves. |
| Trial viewer | `WIRED` | Feature-panel button opens a development-only viewer for every one of the 149 copied assets at authoritative frame timing, including world variants and a Player/Enemy/Ally scale comparison. |

## Intentional limitations and preserved source issues

- `WEAPON_ALIGNMENT_METADATA_GAP`: the sword package says a socket map is required but supplies no measured per-frame socket coordinates. No attachment/weaponpose offset is guessed, and no weapon overlay is presented as final.
- World-mask metadata has no gap: the source contract supplies `NESW=1,2,4,8`; the renderer uses only that declared meaning. This does not close W09 or constitute user acceptance.
- The `needs_rework` source state for `soul.skeleton.rank01.enemy.attack.s` is preserved. Trial visibility does not resolve it.
- Source visual/motion/in-engine review states remain untouched. In particular, the Player idle candidate's recorded repeated-pose/motion-pending note remains a source limitation for user review.

## Technical validation

```powershell
dotnet build solo_vs_mortal_godot.csproj --no-restore -v:minimal
```

Result: succeeded, 0 warnings, 0 errors. This is compile evidence only. The package was not treated as a gameplay or visual-approval result.

Godot package check used the already-validated 4.7.2 toolchain:

```powershell
C:\Users\levan\Downloads\Godot\Godot.exe --headless --path C:\ws\solo_vs_mortal_godot --export-release "Windows Desktop" C:\ws\solo_vs_mortal_godot\build\asset-integration-trial-v001\solo_vs_mortal_godot.exe
```

The captured export log ends with `[ DONE ] savepack`, stderr is empty, and the final package contains the Trial provenance plus imported Player and Ash Graves paths. Artifacts are `build/asset-integration-trial-v001/solo_vs_mortal_godot.exe` (109,513,728 bytes) and `.pck` (279,539,536 bytes). The host wrapper detached from the long-running Godot child before it returned a final exit code, so this is recorded as completed packing/artifact evidence rather than an asserted exit-0 result.

## Manual visual-review route

1. Open `C:/ws/solo_vs_mortal_godot` with Godot .NET `4.7.2.stable.mono.official.ed1daf0bf`, then Run Project (`F6`/the Arena scene or `F5` from the project).
2. Select the HUD button **Mở Asset Integration Trial (F10)** immediately below the status panel, or press **F10**. This route is independent of the fixed-height Feature panel. Choose any AssetId in the selector; it plays at source millisecond timing and includes Player / Skeleton Enemy static / Skeleton Ally native-scale comparison. This is the route for all viewer-only assets, world variants, and sword frames.
3. Move with WASD and hold primary attack to inspect Player idle/move/attack direction mapping. Do not infer gameplay timing from the animation.
4. In a normal current-runtime flow, defeat a Skeleton, press `E` near a Soul pickup, then use **Triệu hồi tất cả (Shift+Q)** or the existing Soul controls after the Soul is available. Observe the Ally's real idle/move/attack presentation; summon/recall/disperse clips remain viewer-only by design.
5. Use the normal Hồn Phiên flow to create/own a banner, then open the Hồn Phiên tab to see the Skeleton banner icon. Soul pickup remains visible at the existing world pickup location before acquisition.
6. To see the sword icon through normal runtime, use **Trang bị / Cửa hàng / Kỹ năng** near Kha, acquire/equip `equipment.sword.rank01`, then open Túi Đồ. The Trial viewer shows attachment/weaponpose art, but no actor overlay is claimed.
7. Walk Ash Graves normally to inspect canonical ground/path/ruin/wall rendering and the mapped props. The viewer exposes every mask and non-emitted prop variant directly.

Judge only scale, palette, directions, extraction, timing, pivot/jitter, weapon/body alignment, pickup/prop readability, and Ash Graves cohesion. Do not use this Trial as the 18-case canonical gameplay acceptance checklist.

### Access correction — 2026-09-11

The original Feature-panel route was not reliably usable because that panel has a fixed-height, non-scrolling content area. The Trial now also creates a standalone HUD button below the status panel and accepts `F10`; both paths only open the existing read-only viewer and do not change Simulation, input commands, or saves.

### Catalog structural correction — 2026-09-11

The first real Arena startup exposed a catalog-serialization defect: 89 static assets had `frames` serialized as an object rather than the strict loader's required one-element array. The correction wraps only those existing frame records as `frames: [ { ... } ]` in the canonical catalog and package provenance; frame rectangles, duration (1000 ms), pivot, source/copied SHA-256, QA, authorization, and approval data are unchanged. Structural revalidation now finds 149 catalog assets and 149 copied provenance entries with array `frames`; a five-frame headless Godot startup completed with exit `0`. This is startup/package evidence only, not gameplay or visual approval.

## Scope safety

- Gameplay, balance, Soul rules, Inventory rules, saves, and gameplay authority are unchanged.
- No ISSUE-05/Part 5 work, 640×360 migration, renderer change, asset regeneration, approval-state mutation, cleanup, commit, or push occurred.
- Part 6/7, W09, final asset approval, official in-engine QA, and gameplay acceptance remain open.
