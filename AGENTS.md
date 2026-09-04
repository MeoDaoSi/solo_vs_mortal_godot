# Repository Agent Rules

## Testing Policy

These rules are normative for all coding agents working in this repository.

1. Automated tests primarily protect high-value core gameplay behavior.

2. Create automated tests mainly for deterministic gameplay rules, Combat and Combat Power calculations, Progression and rank/level gates, Soul ownership and lifecycle, Soul Banner limits, Summon, Devour, Essence, Bloodline, Possession, Capability rules, critical World/Map state, Save/Load compatibility, critical Application workflows, and regressions likely to recur.

3. Do not create tests merely to mirror every class, constructor, property, DTO, trivial registry getter, enum, helper, Godot scene, or UI element.

4. Prefer a small number of meaningful behavioral tests over many low-value structural tests.

5. Validate Presentation and game feel through Godot Debug/playtesting by default. This includes UI layout, animation, VFX, sound, camera feel, map composition, decorative placement, visual polish, and subjective timing.

6. Do not create standalone test executables, custom harnesses, migration runners, phase validators, or soak executables unless there is a strong permanent technical reason. Use the normal C# test project/framework for automated tests.

7. Do not introduce migration/parity/phase terminology into permanent tests. Names must describe the current behavior being protected. Preserve useful behavior from an old migration test by moving or renaming it as a normal unit, regression, system, or integration test.

8. Performance probes may remain only when they have a documented long-term budget and a purpose-based name. They are profiling tools, not substitutes for gameplay tests.

9. **Do not run automated tests unless the user explicitly requests test execution in the current task.** This includes `dotnet test`, custom runners, test executables, integration runners, parity checks, and soak tests. Agents may inspect and edit tests and report commands for later execution.

10. Build and test are different permissions. A permitted `dotnet build` may be used for compile validation, but “validate” does not authorize running tests.

The permanent test project is `tests/SoloVsMortal.Tests/SoloVsMortal.Tests.csproj`. Keep Core and Simulation coverage behavioral and invariant-focused, keep Application coverage to critical workflows, and normally validate Godot Presentation manually.
