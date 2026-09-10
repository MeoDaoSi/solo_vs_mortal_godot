# Repository Agent Rules

## V2.5 testing and authority policy

These rules are normative for the V2.5 migration.

1. The user is the sole gameplay tester. Validate behavior through the real Godot scene and the manual checklist in `docs/V2.5/source-audit.md`, which mirrors the 18 cases in the closed canonical acceptance JSON. A reference-vector check is not an engine test result.

2. Do not run `dotnet test`, custom runners, parity checks, soak tests, or other automated test executables. Do not create replacement automated tests or harnesses. The former `tests/` tree and `src/Presentation/PerformanceSoak.cs` probe have been removed under the user's cleanup authorization.

3. A `dotnet build` or Godot export is allowed for compile/package validation. It does not claim gameplay acceptance or substitute for user manual playtesting.

4. The gameplay authority is the closed bundle at `C:/ws/asset-production-system/game_spec`, revision `2026-09-08.closed-1`, with the hashes in `data/v2.5/spec-lock.json`. The source audit and work-items are planning/implementation records and must not redefine gameplay numbers.

5. Reuse the existing typed C# Core/Data/Simulation/Application/Presentation layers. The official V2.5 implementation baseline is Godot .NET 4.7.2. Isolate engine calls; a successful 4.7.2 compile or export is valid technical evidence, but never gameplay acceptance. Do not change rendering mode merely because of this baseline decision.
