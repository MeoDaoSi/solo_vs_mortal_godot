# Feature parity matrix

This is the acceptance matrix for Phaser-to-Godot behavior. A feature is complete only when its definitions, rules, mutable ownership, application boundary, presentation, persistence impact, and parity evidence are accounted for.

| Feature | Definition owner | Runtime owner | Godot presentation | Save impact | Status |
| --- | --- | --- | --- | --- | --- |
| Player movement/collision | Player/map definitions | Player system | Arena adapter | Position is currently not persisted | GODOT SLICE COMPLETE |
| Combat and damage | Balance definitions | Combat and actor systems | Arena animation adapter | HP | GODOT SLICE COMPLETE |
| Monster spawn/AI/death | Monster definitions | Monster system | Monster view nodes + death linger | None currently | GODOT SLICE COMPLETE |
| Player rank/progression | Balance definitions | Progression system | HUD/actions | Save v2+ | SIMULATION COMPLETE |
| Drops, materials, pills | Item/balance definitions | Progression system | Inventory/crafting UI | Save v2+ | SIMULATION COMPLETE |
| Soul acquisition/progression | Soul Nature definitions | Soul system | Orb + pickup input | Save v1+ | GODOT SLICE COMPLETE |
| Soul Banner/loadout | Banner definitions | Soul Banner system | Bind, slot/capacity summary, per-Soul loadout and runtime controls | Save v1+, legacy aliases | UI IN PROGRESS |
| Summon/recovery | Soul/monster definitions | Summon system | Summon/unsummon controls and runtime status | Save v3+ | UI IN PROGRESS |
| Devouring and Essence | Soul Nature definitions | Devour transaction + Essence milestones | ConfirmationDialog, reward preview and result toast | Save v4+ | UI IN PROGRESS |
| Bloodline | Soul Nature definitions | Milestone state and modifier ownership ported with parity checks | Progress UI/VFX | Save v5 imported; v6 round-trip covered | IN PROGRESS |
| Possession | Soul Nature definitions | Possession lifecycle, temporary modifiers and capability ownership ported with parity checks | Initial per-Soul Possession control and active timer | Save v5 imported; v6 round-trip covered | UI IN PROGRESS |
| Capabilities/world objects | Map definitions | Capability/World systems | Definition-driven object proxies and collision footprints | Save v6 | UI IN PROGRESS |
| Save/load migration | Versioned save contracts | Application persistence boundary | `user://` adapter | Versions 1–5 imported; v6 native | GODOT SLICE COMPLETE |

For every row, parity evidence must cover deterministic results and at least one representative behavior/playtest scenario. Missing source assets may use clearly isolated placeholders; asset fidelity is tracked separately and is not a behavior-parity blocker. New content is not a substitute for closing existing parity gaps.
