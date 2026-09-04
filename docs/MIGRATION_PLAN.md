# Phaser 3 to Godot 4 C# migration plan

## Goal and constraints

Migrate the existing playable prototype from TypeScript + Phaser 3 to Godot 4.7 C# while preserving gameplay behavior, deterministic simulation, stable IDs, save compatibility, Vietnamese player-facing names, and the existing art catalogue. Treat the Phaser repository as the behavioral reference until each vertical slice reaches parity.

The target dependency direction remains:

```text
Presentation -> Application -> Simulation -> Systems / Config / Data / Core
```

Godot nodes must stay in Presentation. Engine-independent gameplay code should remain plain C# so it can be tested without constructing a scene tree.

## Proposed target structure

```text
assets/                  Imported source art and audio
data/configs/            JSON content copied with stable IDs
scenes/                  Godot scenes and reusable scene components
src/Core/                IDs, math, events, RNG, simulation clock
src/Data/                DTOs, validation, registries, save migrations
src/Systems/             Shared formulas such as CP and sprite stages
src/Simulation/          Player, monsters, combat, Souls, progression
src/Application/         Commands, queries, facades, save boundary
src/Presentation/        Godot nodes, input, camera, UI, animation, audio
tests/                   Engine-independent C# parity and regression tests
```

## Engine mapping

| Phaser 3 | Godot 4 C# |
| --- | --- |
| `BootScene` / `PreloadScene` | Project startup and Godot resource import; optional loading scene for progress UI |
| `ArenaScene` | `Arena.tscn` with a thin coordinating C# node |
| Arcade sprites/bodies | `CharacterBody2D`, `Area2D`, `CollisionShape2D` |
| Spritesheets/anims | `Sprite2D` or `AnimatedSprite2D` with `SpriteFrames` resources |
| Camera | `Camera2D` |
| Keyboard/pointer input | Input Map actions consumed by presentation controllers |
| Phaser events | Typed C# domain event bus; Godot signals only at presentation boundaries |
| JSON preload/cache | Typed JSON loader and validated registries, later convertible to `Resource` assets |
| Browser local storage | Versioned `user://` save file with migration from Phaser JSON |
| Phaser update loop | Fixed-step simulation driven by `_PhysicsProcess`; render interpolation if needed |

## Migration phases

### 0. Baseline and parity contract

- Freeze a known-good Phaser revision and record its save schema, inputs, viewport behavior, and deterministic seeds.
- Inventory configs and manifest paths; classify assets as direct-copy, spritesheet slicing, sequence import, or replacement-needed.
- Capture parity fixtures for CP, XP, combat, Souls, banners, summons, possession, capabilities, and world interactions.

Exit: a versioned parity checklist and representative input/output fixtures exist.

### 1. Foundation port

- Port `core`, shared `systems`, canonical balance config, data types, validators, and registries to engine-independent C#.
- Preserve JSON keys, enum wire values, IDs, rounding order, seeded RNG behavior, and immutable query results.
- Mirror the current Vitest coverage, beginning with RNG, rank, CP, clock, and validation.

Exit: foundation fixtures match TypeScript results exactly.

### 2. Simulation and application port

- Port `GameState`, Player, Monster, Combat, Progression, Soul, Soul Banner, Summon, Essence, Bloodline, Possession, Capability, and World Interaction systems.
- Port application facades/use cases only after their dependent systems reach parity.
- Implement the versioned save boundary and a one-time importer for existing Phaser saves where feasible.

Exit: a headless C# simulation replays deterministic scenarios with matching results.

### 3. First playable Godot vertical slice

- Build the arena, player movement/collision, camera, one monster, combat, Soul acquisition, minimal HUD, and save/load.
- Copy only assets required by this slice and centralize every resource path in the asset registry.
- Keep Godot node scripts as adapters; gameplay formulas do not belong in nodes.

Exit: launch, move, fight, acquire a Soul, save, reload, and reproduce the same state.

### 4. Full feature parity

- Add progression, pills/material drops, Soul Banner loadout, summon lifecycle, reinforcement, devouring, Essence, Bloodline, Possession, and the fragile-wall capability slice.
- Rebuild animation resolution from `characterAnimations.json`; validate every referenced frame source.
- Recreate UI flows with Vietnamese `displayName` values rather than internal IDs.

Exit: every implemented Phaser roadmap item has a Godot parity check.

### 5. Godot-native hardening

- Profile physics, draw calls, texture memory, import settings, and scene loading.
- Add audio/VFX and new content only after parity; keep redesign separate from migration.
- Establish desktop exports first, then add other targets and platform-specific save/input handling.
- Retire Phaser only after save migration, smoke checks, and playtest sign-off.

Exit: reproducible export, acceptable performance, and no unresolved P0/P1 parity gaps.

## Recommended execution order

1. Seeded RNG and math primitives.
2. Config DTOs, validation, and asset registries.
3. CP, rank, sprite-stage, and progression formulas.
4. State, player, monster, and combat.
5. Souls, banners, summons, then advanced Soul systems.
6. Application commands/queries and save migrations.
7. Arena presentation and UI.
8. Remaining content, polish, and exports.

## Main risks

- JavaScript number semantics versus C# numeric types can change rounding, overflow, and seeded RNG output. Lock these with fixtures before refactoring.
- Phaser and Godot collision resolution differ. Compare intended gameplay outcomes, not frame-level engine internals, and keep collision dimensions data-driven.
- Godot imports and renames resources. Preserve a stable logical asset ID layer rather than persisting `res://` paths in saves.
- Sprite sheet margins, frame ordering, pivots, and animation timing can silently drift. Validate representative animations visually and through registry checks.
- Porting and redesigning simultaneously makes regressions hard to isolate. Reach parity first; optimize architecture afterward.

## Immediate next milestone

Port `Vec2`, `Rect`, `Uid`, `SeededRng`, and `SimulationClock` plus parity fixtures from their current Vitest cases. Then port rank and Combat Power as the first end-to-end deterministic subsystem.
