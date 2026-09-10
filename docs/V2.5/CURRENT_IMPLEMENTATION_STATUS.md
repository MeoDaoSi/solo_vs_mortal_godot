# Solo vs Mortal — current V2.5 implementation status

**Updated:** 2026-09-10  
**Status source:** this file is the current-state index only. It does not rewrite the historical plans in `work-items.json`, `implementation-tasks-4-8.json`, or `source-audit.md`. The append-only evidence remains in [implementation progress](IMPLEMENTATION_PROGRESS_V2.5_2026-09-09.md).

## Authority and validation boundary

Gameplay authority is `C:/ws/asset-production-system/Solo_vs_Mortal_Gameplay_System_V2.5.md`, revision `2026-09-08.closed-1`, with its hash-pinned `game_spec` bundle. The user is the sole gameplay tester. The technical results below are not gameplay acceptance.

| Scope | Current status | Evidence / remaining boundary |
|---|---|---|
| Part 1 — authority, loader, save foundation | `complete` | Closed authority loader, schema-1 canonical save and durable WAL boundary are implemented. See progress journal entries through 2026-09-09. |
| Part 2 — documentation and technical packaging | `in_progress` | V2.5 document entry point and historical-document boundary are complete. ISSUE-01 is complete: Godot .NET 4.7.2 is the official implementation baseline. Other documentation/package work remains. |
| Part 3 — combat and control | `in_progress` | Static implementation is present and compile-valid. User manual acceptance remains open. |
| Part 4 — gameplay/content systems | `ISSUE-04 CODE_COMPLETE — READY_FOR_USER_ACCEPTANCE` | Soul drop/capture/Density/Sync/Banner/Summon/Spirit/Ally/Possession persistence and real canonical callers are closed by static/runtime-path audit. The 18 real-scene manual cases remain solely user-owned. See `ISSUE-04_SOUL_LOOP_CLOSURE_MATRIX.md`. |
| Part 5 — presentation/runtime validation | `in_progress` | World/persistence items P01–P10 and W01–W08 are complete by static audit; W09 awaits canonical asset mapping. |
| Part 6 — asset adapter and cleanup | `in_progress` | Inventory/contract work has started. No old asset is removed before a canonical manifest maps every live reference. |
| Part 7 — Art production | `not_complete` | User approval and an approved versioned asset export are required before this part can close. |
| Part 8 — integration/package/manual acceptance | `not_complete` | Depends on the part 6/7 delivery and user-owned manual acceptance. |

## Current technical validation

| Check | Result | Scope limit |
|---|---|---|
| `dotnet build solo_vs_mortal_godot.csproj --no-restore` | `complete` — 0 errors on 2026-09-10 | Latest ISSUE-04 build has one `NU1900` warning because NuGet vulnerability metadata cannot be reached; compilation only, never gameplay acceptance. |
| Godot .NET 4.7.2 baseline build/export | `complete` — `dotnet build solo_vs_mortal_godot.csproj --no-restore` passed with 0 warnings/0 errors; Windows release export exited 0 and produced `build/issue-01-4.7.2/SoloVsMortal.exe` and `.pck` on 2026-09-10 | This is valid baseline compile/package evidence only; it does not prove gameplay acceptance. Godot reported non-blocking root-certificate/editor-settings warnings. |
| ISSUE-04 Godot export retry | `attempted` — Godot 4.7.2 Windows Desktop export reached packing but its internal .NET publish reported failure; direct `dotnet publish ... --no-restore --configuration Release --runtime win-x64 --self-contained false` succeeded | Packaging/toolchain follow-up only; it does not indicate a Part4 gameplay or source-code defect and does not replace user manual acceptance. |
| Rendering configuration review | `recorded` | `project.godot` advertises C# Forward Plus, while the Windows export preset selects `gl_compatibility`. ISSUE-01 does not change either rendering setting. |
| Manual acceptance cases 1–18 | `not_complete` | Must be exercised by the user in the real game under `AGENTS.md`; no agent gameplay runner is permitted. |

## Current asset dependency decision

The accepted reusable export `C:/ws/asset-production-system/art/exports/skeleton-static-integration-v001/` remains an incremental source package. Its four Skeleton rank-01 static IDs may be integrated only through a versioned canonical manifest. They do not close the beta asset requirement and no `needs_rework` animation becomes approved by default.

The next active implementation step is Part 6.01: record each V2.5 `AssetId`, its production/export state, runtime consumer, and any fallback. Part 6.02 then introduces the adapter contract; only after its mapping validates may obsolete presentation paths and legacy assets be cleaned. The next engine maintenance task is to keep the local Godot .NET 4.7.2 executable/templates available and repeat baseline build/export validation when toolchain or package configuration changes.
