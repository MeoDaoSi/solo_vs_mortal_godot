# Migration status

Status values are `NOT STARTED`, `IN PROGRESS`, `BLOCKED`, and `COMPLETE`. A phase is complete only when its exit criteria in `MIGRATION_PLAN.md` are satisfied.

| Phase | Status | Current evidence | Next gate |
| --- | --- | --- | --- |
| 0 — Baseline and parity contract | IN PROGRESS | Source revision and inventory recorded in `PARITY_BASELINE.md`; canonical configs copied | Generate representative golden fixtures and complete asset classification |
| 1 — Foundation port | COMPLETE | Foundation rules and all canonical Definition registries are ported; the source-revision golden harness passes RNG, CP, rank, sprite-stage, progression and registry-loading checks | Full source asset-file existence verification was explicitly excluded; proceed to Phase 2 systems |
| 2 — Simulation and application | COMPLETE | Headless parity passes for all scoped systems; save schema v6 round-trips all mutable state, imports Phaser v1–v5 (including legacy aliases), and identical seeded command streams produce identical final saves | Proceed with the playable Godot slice while keeping new gameplay work behind parity checks |
| 3 — First playable slice | IN PROGRESS | Godot `Main` is a thin adapter that starts/ticks Application | Build Arena after Player/Monster/Combat foundation is usable |
| 4 — Full feature parity | IN PROGRESS | Feature ownership and acceptance matrix created in `FEATURE_PARITY.md` | Complete vertical slices against the matrix |
| 5 — Godot-native hardening | IN PROGRESS | Initial profiling/export gates recorded in `PERFORMANCE_BUDGET.md` | Capture metrics when representative playable content exists |

## Working rule

Phases overlap only where dependencies are real. Starting a later phase means preparing its acceptance criteria or infrastructure; it does not permit bypassing parity gates or moving business state into Presentation.
