# Migration status

Status values are `NOT STARTED`, `IN PROGRESS`, `BLOCKED`, and `COMPLETE`. A phase is complete only when its exit criteria in `MIGRATION_PLAN.md` are satisfied.

| Phase | Status | Current evidence | Next gate |
| --- | --- | --- | --- |
| 0 — Baseline and parity contract | IN PROGRESS | Source revision and inventory recorded in `PARITY_BASELINE.md`; canonical configs copied | Generate representative golden fixtures and complete asset classification |
| 1 — Foundation port | COMPLETE | Foundation rules and all canonical Definition registries are ported; the source-revision golden harness passes RNG, CP, rank, sprite-stage, progression and registry-loading checks | Full source asset-file existence verification was explicitly excluded; proceed to Phase 2 systems |
| 2 — Simulation and application | COMPLETE | Headless parity passes for all scoped systems; save schema v6 round-trips all mutable state, imports Phaser v1–v5 (including legacy aliases), and identical seeded command streams produce identical final saves | Proceed with the playable Godot slice while keeping new gameplay work behind parity checks |
| 3 — First playable slice | COMPLETE | Arena launches and supports map-sized movement/collision, camera, definition-driven Player/Skeleton combat animation including event-backed death linger, Soul acquisition, Vietnamese HUD, and `user://` save/load; automated Godot smoke prints `PHASE3_SMOKE_PASS`, and Arena starts with zero runtime errors | Proceed to Phase 4 feature/UI parity; replace remaining geometric map presentation as each content slice is integrated |
| 4 — Full feature parity | COMPLETE | Application/Simulation parity covers progression, pills/materials, Soul Banner binding, summon/unsummon/recovery, devour, Essence, Bloodline, Possession and fragile-wall interaction. Arena exposes Inventory/Progression/Banner screens, Devour confirmation, summon/Possession controls, asset-backed map/character rendering and Ally/Summon animation/HP visuals. `Migration parity checks passed` and `PHASE4_VISUAL_SMOKE_PASS` (including summon/Ally snapshot lifecycle) are green; missing source audio/VFX use deterministic fallbacks | Proceed to Phase 5 hardening; add content/redesign only after parity |
| 5 — Godot-native hardening | IN PROGRESS | Initial profiling/export gates recorded in `PERFORMANCE_BUDGET.md` | Capture metrics when representative playable content exists |

## Working rule

Phases overlap only where dependencies are real. Starting a later phase means preparing its acceptance criteria or infrastructure; it does not permit bypassing parity gates or moving business state into Presentation.
