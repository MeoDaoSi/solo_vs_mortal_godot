# Solo vs Mortal — current V2.5 implementation status

**Updated:** 2026-09-13 (art workflow; gameplay status unchanged)
**Status source:** this file is the current-state index only. It does not rewrite the historical plans in `work-items.json`, `implementation-tasks-4-8.json`, or `source-audit.md`. The append-only evidence remains in [implementation progress](IMPLEMENTATION_PROGRESS_V2.5_2026-09-09.md).

## Authority and validation boundary

Art now follows the production repository's `solo-vs-mortal-art/SKILL.md`: one final PNG at a time, immediate Arena import, user visual review, then stop. The first replacement is `assets/art/player.base.idle.s.png`, a single south pose on a 64×64 canvas with 55px visible height and pivot (32,60). Debug holds this pose while moving; F7 reloads imported art. It is not completed animation. Production batches/raw/QA have been reset; other live legacy game art remains pending cleanup authorization after the automatic review blocked a whole-catalog deletion. Historical package/approval records below do not govern this new workflow.

Gameplay authority is `C:/ws/asset-production-system/Solo_vs_Mortal_Gameplay_System_V2.5.md`, revision `2026-09-08.closed-1`, with its hash-pinned `game_spec` bundle. The user is the sole gameplay tester. The technical results below are not gameplay acceptance.

| Scope | Current status | Evidence / remaining boundary |
|---|---|---|
| Part 1 — authority, loader, save foundation | `complete` | Closed authority loader, schema-1 canonical save and durable WAL boundary are implemented. See progress journal entries through 2026-09-09. |
| Part 2 — documentation and technical packaging | `in_progress` | V2.5 document entry point and historical-document boundary are complete. ISSUE-01 is complete: Godot .NET 4.7.2 is the official implementation baseline. Other documentation/package work remains. |
| Part 3 — combat and control | `in_progress` | Static implementation is present and compile-valid. User manual acceptance remains open. |
| Part 4 — gameplay/content systems | `ISSUE-04 CODE_COMPLETE — READY_FOR_USER_ACCEPTANCE` | Soul drop/capture/Density/Sync/Banner/Summon/Spirit/Ally/Possession persistence and real canonical callers are closed by static/runtime-path audit. The 18 real-scene manual cases remain solely user-owned. See `ISSUE-04_SOUL_LOOP_CLOSURE_MATRIX.md`. |
| Part 5 — presentation/runtime validation | `in_progress` | World/persistence items P01–P10 and W01–W08 are complete by static audit. The 2026-09-10 Asset Integration Trial adds a presentation-only Ash Graves mapping, but does not close W09 or user visual acceptance. |
| Part 6 — asset adapter and cleanup | `in_progress` | The canonical adapter now maps the Trial package with exact hash/frame metadata and explicit missing IDs. No old asset is removed before every live reference has a finalized replacement and deletion gate. |
| Part 7 — Art production | `not_complete` | User approval and an approved versioned asset export are required before this part can close. |
| Part 8 — integration/package/manual acceptance | `not_complete` | Depends on the part 6/7 delivery and user-owned manual acceptance. |

## Current technical validation

| Check | Result | Scope limit |
|---|---|---|
| `dotnet build solo_vs_mortal_godot.csproj --no-restore` | `complete` — 0 errors on 2026-09-10 | Latest ISSUE-04 build has one `NU1900` warning because NuGet vulnerability metadata cannot be reached; compilation only, never gameplay acceptance. |
| Godot .NET 4.7.2 baseline build/export | `complete` — `dotnet build solo_vs_mortal_godot.csproj --no-restore` passed with 0 warnings/0 errors; Windows release export exited 0 and produced `build/issue-01-4.7.2/SoloVsMortal.exe` and `.pck` on 2026-09-10 | This is valid baseline compile/package evidence only; it does not prove gameplay acceptance. Godot reported non-blocking root-certificate/editor-settings warnings. |
| ISSUE-04 Godot Windows export | `complete` — Godot 4.7.2 ran `ExportRelease`, `win-x64`, and `--self-contained true`; the actual Godot process exited 0 and produced `build/issue-04-export-verified/solo_vs_mortal_godot.exe` and `.pck` on 2026-09-10 | The first reproduction found `MSB1029` only when the sandbox denied Godot's AppData build-log write. The normal toolchain run has no MSBuild errors (only `NU1900` feed-metadata warnings). This is compile/package evidence, never gameplay acceptance. |
| Asset Integration Trial compile | `complete` — `dotnet build solo_vs_mortal_godot.csproj --no-restore -v:minimal` passed with 0 warnings/0 errors after the 149-file Trial import | Hash/provenance/frame validation and compile evidence only. User visual review in the real Arena remains required; 12 source IDs are unavailable and no QA/approval status was elevated. |
| Rendering configuration review | `recorded` | `project.godot` advertises C# Forward Plus, while the Windows export preset selects `gl_compatibility`. ISSUE-01 does not change either rendering setting. |
| Manual acceptance cases 1–18 | `not_complete` | Must be exercised by the user in the real game under `AGENTS.md`; no agent gameplay runner is permitted. |

## Current asset dependency decision

The accepted reusable export `C:/ws/asset-production-system/art/exports/skeleton-static-integration-v001/` remains an incremental source package. Its four Skeleton rank-01 static IDs retain their prior provenance/reuse semantics inside the versioned Trial catalog; they do not substitute for missing animations or close the beta asset requirement.

`assets/v2.5/asset-integration-trial-v001/` is a portable, presentation-only 149-file Trial package sourced from the user-authorized cutoff in `runtime-trial.json`. Its 161-ID matrix, 12 unavailable IDs, source/copied hashes, frame metadata, viewer boundary, and limitations are recorded in `ASSET_INTEGRATION_TRIAL_2026-09-10.md`. It does not grant release readiness, visual/motion approval, official in-engine QA, W09 acceptance, Part 6/7 closure, or gameplay acceptance.

The next active implementation step is Part 6.01: record each V2.5 `AssetId`, its production/export state, runtime consumer, and any fallback. Part 6.02 then introduces the adapter contract; only after its mapping validates may obsolete presentation paths and legacy assets be cleaned. The next engine maintenance task is to keep the local Godot .NET 4.7.2 executable/templates available and repeat baseline build/export validation when toolchain or package configuration changes.
