# Solo vs Mortal — current V2.5 implementation status

**Updated:** 2026-09-10  
**Status source:** this file is the current-state index only. It does not rewrite the historical plans in `work-items.json`, `implementation-tasks-4-8.json`, or `source-audit.md`. The append-only evidence remains in [implementation progress](IMPLEMENTATION_PROGRESS_V2.5_2026-09-09.md).

## Authority and validation boundary

Gameplay authority is `C:/ws/asset-production_system/Solo_vs_Mortal_Gameplay_System_V2.5.md`, revision `2026-09-08.closed-1`, with its hash-pinned `game_spec` bundle. The user is the sole gameplay tester. The technical results below are not gameplay acceptance.

| Scope | Current status | Evidence / remaining boundary |
|---|---|---|
| Part 1 — authority, loader, save foundation | `complete` | Closed authority loader, schema-1 canonical save and durable WAL boundary are implemented. See progress journal entries through 2026-09-09. |
| Part 2 — documentation and technical packaging | `in_progress` | V2.5 document entry point and historical-document boundary are complete. 4.7.2 compile/package are complete. The required 4.5.2 Compatibility baseline is unavailable locally and therefore unverified. |
| Part 3 — combat and control | `in_progress` | Static implementation is present and compile-valid. User manual acceptance remains open. |
| Part 4 — gameplay/content systems | `in_progress` | Implemented work is tracked in the progress journal; dependency and user acceptance work remain. |
| Part 5 — presentation/runtime validation | `in_progress` | World/persistence items P01–P10 and W01–W08 are complete by static audit; W09 awaits canonical asset mapping. |
| Part 6 — asset adapter and cleanup | `in_progress` | Inventory/contract work has started. No old asset is removed before a canonical manifest maps every live reference. |
| Part 7 — Art production | `not_complete` | User approval and an approved versioned asset export are required before this part can close. |
| Part 8 — integration/package/manual acceptance | `not_complete` | Depends on the part 6/7 delivery and user-owned manual acceptance. |

## Current technical validation

| Check | Result | Scope limit |
|---|---|---|
| `dotnet build solo_vs_mortal_godot.csproj --no-restore` | `complete` — 0 warnings, 0 errors on 2026-09-10 | Compilation only; it does not run gameplay. |
| Godot 4.7.2 debug export | `complete` — `build/v25-4.7.2/SoloVsMortal.exe`, `.console.exe`, `.pck` were produced on 2026-09-10 | The project uses Godot .NET 4.7.2. This does not verify the canonical Godot 4.5.2 Compatibility baseline. |
| Godot 4.5.2 Compatibility compile/export | `not_complete` | No Godot 4.5.2 .NET executable/templates exist in the local environment. Do not infer compatibility from 4.7.2. |
| Manual acceptance cases 1–18 | `not_complete` | Must be exercised by the user in the real game under `AGENTS.md`; no agent gameplay runner is permitted. |

## Current asset dependency decision

The accepted reusable export `C:/ws/asset-production_system/art/exports/skeleton-static-integration-v001/` remains an incremental source package. Its four Skeleton rank-01 static IDs may be integrated only through a versioned canonical manifest. They do not close the beta asset requirement and no `needs_rework` animation becomes approved by default.

The next active implementation step is Part 6.01: record each V2.5 `AssetId`, its production/export state, runtime consumer, and any fallback. Part 6.02 then introduces the adapter contract; only after its mapping validates may obsolete presentation paths and legacy assets be cleaned.
