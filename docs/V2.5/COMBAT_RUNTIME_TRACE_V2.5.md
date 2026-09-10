# V2.5 combat runtime trace and manual acceptance map

**Static audit date:** 2026-09-10  
**Boundary:** This is a source trace. It establishes code order and manual actions; it does not replace user gameplay acceptance.

## Authoritative fixed-tick order

The authority requires: expiry/due DoT → AI/intents/movement/cast accept → release/projectile/damage → death/reward facts → recall/summon/possession/world/equipment/quest → resource integration → derived state → publish/save boundary.

The current domain sequence is:

1. `Arena._PhysicsProcess` supplies the most recent input to `GameApplication.SetInput`; `SimulationClock` advances complete 60 Hz ticks only.
2. `GameSession.FixedStep` invokes `V25CombatCoordinator.PrepareFixedTick`: status/shield expiry and due DoT are processed before new damage.
3. Previous dodge, knockback, Player movement, Monster AI movement and Ally movement advance. Their collision path is swept; actor radii are sourced by V2.5 role.
4. `V25CombatCoordinator.Tick` decrements active cooldowns, accepts Player and actor intents, then advances casts and projectiles. It consumes Spirit/cooldown only after accept validation.
5. Hit resolution validates target/faction/invulnerability before it creates a hit key or consumes a crit roll. It applies defensive state at hit, emits death events, then effect state.
6. The remaining fixed systems handle eligible pickup, Spirit net flow, summon recovery, progression, possession, world/hazard/traversal and inventory timers. `GameApplication` owns durable capture/commit outside an incomplete simulation tick.

## Deterministic execution repair

Dictionary iteration must not determine a canonical combat outcome. The static audit found unstated iteration order in simultaneous combat paths. They now have explicit order:

| Path | Stable order |
|---|---|
| due status/DoT | target UID → effect ID → source ID |
| shield consumption | expiry tick → source ID |
| knockback | target UID |
| cooldown decrement | source UID → skill ID |
| casts releasing in one tick | accepted tick → cast ID |
| projectiles advancing in one tick | released tick → cast ID |

Released projectile state retains `ActorView` offense data. Death, stun, return, or possession revoke cancels only an unreleased cast from that source. Player death additionally clears the active battlefield runtime, while ally recall is invoked through the Player death event after lethal damage resolves.

## Static requirement-to-symbol map

| Requirement | Implementation boundary | User manual confirmation still required |
|---|---|---|
| 60 Hz tick, timer rounding and input buffer | `SimulationClock`, `V25CombatRules.MillisecondsToTicksCeil`, `V25CombatCoordinator.RequestPlayerSkill` | Frame feel, buffer timing and no double input in real Godot. |
| Accept/cost/cooldown/release | `V25CombatCoordinator.AcceptCast`, `AdvanceCasts`, `ReleaseCast` | Wrong weapon/rank/LOS/resource commands visibly fail without a resource change. |
| Damage, shield, crit and immunity | `V25DamageResolver.Resolve`, `ApplyDirect`, `V25ShieldStore.Consume` | Cases 7–9 in the authority manual checklist. |
| Dodge and collision | `PlayerSystem.TryStartDodge/TickDodge/SweptPosition`, `V25ActorBodyRadii` | 72 unit travel, 200 ms hostile invulnerability and wall/gate/hazard behavior. |
| Target/release/projectile behavior | `ValidTargetAtAccept`, `ReleaseCast`, `AdvanceProjectiles` | Targeted LOS revalidation, no homing/pierce, projectile wall collision. |
| Cast/death/stagger/revoke cancellation | `CancelUnreleasedCasts`, `CancelRuntimeForDeath`, `MonsterSystem.ApplyCanonicalStagger` | Same-tick boss/player/death and recall situations. |
| Runtime persistence | `GameApplication.CanonicalRuntimeSnapshot`, `V25SaveData`, `V25SaveStore` | Suspend/load in real Godot while casts/projectiles/DoT/AI/cooldowns are live. |

## Open user-owned acceptance

The user must exercise the 18 cases in `game_spec/acceptance-cases.v2.5.json`. Combat-critical cases are 7–9 and the relevant cross-system cases are 10, 15–18. Record pass/fail against the real build; do not use a command runner, reference-vector runner, or headless scene as a substitute.
