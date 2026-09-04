# SOLO VS MORTAL — Architecture

This document defines the long-term architecture of Solo vs Mortal. It is the normative source for module boundaries, dependency direction, state ownership, and naming responsibilities. Implementation plans may describe how the code reaches this architecture, but they do not redefine it.

The architecture is designed to keep gameplay deterministic, headless-testable, and independent from scene-tree lifetime while using Godot as the presentation and platform runtime.

## Dependency direction

```text
Presentation -> Application -> Simulation -> Data / Core
                              -> Core
```

- `Core` contains game-agnostic primitives and infrastructure contracts: IDs, math, deterministic RNG, clock abstractions, and the domain event mechanism.
- `Data` contains immutable authored definitions, their loaders, validation, and registries. It does not contain live entity instances.
- `Simulation` contains gameplay rules and owns all mutable gameplay runtime state.
- `Application` exposes commands, queries, use cases, save/load orchestration, and immutable view data.
- `Presentation` contains Godot nodes, scenes, resources used only for rendering, input adapters, camera, UI, animation, audio, and VFX.

Lower layers never import Godot. Presentation may observe and command the game through Application, but it must not mutate Simulation storage directly.

## Module organization

There is no top-level `src/Systems` layer. “System” and “rule” describe different responsibilities inside Simulation; neither is an independent architectural layer.

- Pure deterministic gameplay formulas go in `src/Simulation/Rules`, for example `CombatPowerRules`, `ProgressionRules`, and `SpriteStageRules`.
- Stateful gameplay subsystem owners go in feature-oriented folders under `src/Simulation/Systems`, for example `MonsterSystem`, `SoulSystem`, and `CombatSystem`.
- Generic, non-game-specific algorithms go in `src/Core`, not `Simulation/Rules`.
- Authored tuning values go in `src/Data/Definitions` or a validated balance definition; formulas must not own mutable state or hidden configuration.

The suffix is meaningful:

- `*Rules`: stateless and deterministic; output depends only on explicit inputs and immutable definitions.
- `*System`: owns or coordinates mutable runtime state for one gameplay capability.
- `*Registry`: indexes validated immutable definitions; it is not an entity store.
- `*Store` or `*State`: owns live runtime records and is located in Simulation.

## Definition versus runtime state

### Definition

A Definition describes an authored type or reusable template shared by many instances. Definitions are immutable after validation and live under `Data/Definitions`.

Examples: monster definition, player starting definition, Soul Nature definition, banner tier definition, map definition, animation descriptor, balance definition.

A Definition may contain:

- Stable content ID and localized `displayName`.
- Base values, ranges, tags, species, tier, costs, drop tables, AI tuning, asset IDs, and cross-references to other definition IDs.
- Validation metadata needed to reject malformed content.

A Definition must not contain:

- Instance UID.
- Current HP/XP, world position, current target, alive/dead status, cooldown remaining, elapsed timers, acquired/bound/summoned status, or per-session flags.
- References to live Simulation objects or Godot nodes.
- A mutable collection that changes during play.

Definitions answer “what kind of thing is this?” They never answer “what is happening to this particular instance now?”

### Runtime State

Runtime State represents a particular play-session instance and lives only under `Simulation/State` or inside its owning `Simulation/Systems` feature.

Examples: `MonsterState`, `PlayerState`, `OwnedSoulState`, `SoulBannerState`, `SummonState`, `WorldObjectState`, and `GameSessionState`.

Runtime State may contain:

- Instance UID and the stable definition ID it was created from.
- Current/max HP, level, XP, position, AI state, cooldowns, timers, ownership, bindings, and lifecycle status.
- Derived values cached for performance, provided the owning Simulation system invalidates or recomputes them.

Runtime State must not duplicate authored definition fields merely for convenient editing. It references a definition by stable ID and resolves immutable authored values through a registry. A copied value is allowed only when it is intentionally historical state, such as a captured Soul retaining its origin rank; that intent must be named and persisted explicitly.

Runtime State answers “what is happening to this specific instance now?”

### Snapshot, command, event, and save DTO

These boundary objects are not Definitions and do not own live state:

- A query Snapshot/View DTO is an immutable copy projected from Simulation for Application or Presentation. Mutating it cannot affect the game.
- A Command expresses requested intent and is validated before mutation.
- An Event reports a fact that has already occurred and must not be used as mutable storage or a query response.
- A Save DTO is a versioned serialization projection. It may resemble Runtime State but is not the live object graph. Loading validates and reconstructs Simulation state through explicit restore methods.

Place save contracts under `Application/Persistence`, not `Data/Definitions`. Place presentation-only animation and resource bindings under `Presentation`, unless they are engine-neutral authored descriptors referenced by logical asset ID.

## Simulation ownership

Simulation is the single source of truth for mutable gameplay state.

- Only Simulation systems may create, mutate, remove, or restore gameplay instances.
- Each state field has one owning system. Other systems interact through explicit methods, commands, queries, or facts; they do not retain writable aliases to another owner's collections.
- `GameSimulation`/`GameSession` is the composition root and deterministic tick coordinator. It does not move business rules into Godot callbacks.
- Application coordinates multi-system use cases and transaction-like workflows, but mutations occur through Simulation owners.
- Presentation never owns authoritative HP, XP, inventory, Souls, bindings, cooldowns, AI state, world-object state, or save state.
- Godot nodes cache only presentation state: node references, animation playback position, interpolation samples, UI selection, hover/focus, camera shake, particles, and audio handles.
- Input adapters translate Godot input into Application commands. They do not update entity state directly.
- Rendering reads immutable snapshots. A node being freed, hidden, paused, or re-instantiated must not delete or alter its corresponding gameplay entity unless it sends an explicit valid command.
- Physics contacts are observations from Presentation. Simulation decides gameplay consequences such as damage, death, acquisition, destruction, and cooldown changes.
- Godot signals stay inside Presentation or adapt to/from typed domain events at a boundary. Simulation does not depend on Node paths, scene-tree lifetime, signals, or `GodotObject` identity.

## Ownership examples

| Concern | Authoritative owner | Presentation responsibility |
| --- | --- | --- |
| Player HP, XP, rank, modifiers | Player/Progression Simulation systems | Render bars/text; send commands |
| Monster position and AI state | Monster Simulation system | Interpolate and render node transforms |
| Damage and attack cooldown | Combat/actor Simulation systems | Play animation, sound, VFX from facts |
| Owned Souls and progression | Soul Simulation system | Render inventory snapshot |
| Banner bindings and limits | Soul Banner Simulation system | Render slots; request bind/unbind |
| Summon lifecycle | Summon Simulation system | Spawn/despawn visual representatives |
| World object destroyed/blocking | World Interaction Simulation system | Update visuals and collision proxy |
| UI selection, camera, particles | Presentation | Not persisted as business state |

## Architecture review checklist

Before accepting a new or changed type, answer all of the following:

1. Is it immutable authored content, live instance state, a boundary DTO, or presentation state?
2. If mutable gameplay state exists, which single Simulation system owns every field?
3. Can Presentation mutate it without issuing an Application command? If yes, the boundary is wrong.
4. Does Data contain a UID, current value, timer, position, lifecycle flag, or writable instance collection? If yes, move it to Simulation.
5. Does a `*Rules` type retain session state or use Godot APIs? If yes, split or relocate it.
6. Does a `*System` only calculate a pure value? If yes, rename it to `*Rules`.
7. Can a headless test execute the gameplay behavior without a scene tree? If no, engine concerns have leaked inward.
