# Migration status

Status values are `NOT STARTED`, `IN PROGRESS`, `BLOCKED`, and `COMPLETE`. A phase is complete only when its exit criteria in `MIGRATION_PLAN.md` are satisfied.

| Phase | Status | Current evidence | Next gate |
| --- | --- | --- | --- |
| 0 — Baseline and parity contract | IN PROGRESS | Source revision and inventory recorded in `PARITY_BASELINE.md`; canonical configs copied | Generate representative golden fixtures and complete asset classification |
| 1 — Foundation port | IN PROGRESS | Math, UID, Mulberry32 RNG, clock, canonical balance Definitions, Rank/CP, sprite-stage, and shared progression rules ported; C# build is clean | Add JSON loaders/validation and parity fixtures/tests before declaring formula parity |
| 2 — Simulation and application | IN PROGRESS | Headless `GameSession`, owned stage state, and Application command/query boundary created | Add systems only after their Phase 1 rules and definitions have fixtures |
| 3 — First playable slice | IN PROGRESS | Godot `Main` is a thin adapter that starts/ticks Application | Build Arena after Player/Monster/Combat foundation is usable |
| 4 — Full feature parity | IN PROGRESS | Feature ownership and acceptance matrix created in `FEATURE_PARITY.md` | Complete vertical slices against the matrix |
| 5 — Godot-native hardening | IN PROGRESS | Initial profiling/export gates recorded in `PERFORMANCE_BUDGET.md` | Capture metrics when representative playable content exists |

## Working rule

Phases overlap only where dependencies are real. Starting a later phase means preparing its acceptance criteria or infrastructure; it does not permit bypassing parity gates or moving business state into Presentation.
