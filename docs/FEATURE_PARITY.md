# Feature parity matrix

This is the acceptance matrix for Phaser-to-Godot behavior. A feature is complete only when its definitions, rules, mutable ownership, application boundary, presentation, persistence impact, and parity evidence are accounted for.

| Feature | Definition owner | Runtime owner | Godot presentation | Save impact | Status |
| --- | --- | --- | --- | --- | --- |
| Player movement/collision | Player/map definitions | Player system | Character body adapter | Position is currently not persisted | SIMULATION IN PROGRESS |
| Combat and damage | Balance definitions | Combat and actor systems | Animation/VFX adapter | HP | SIMULATION IN PROGRESS |
| Monster spawn/AI/death | Monster definitions | Monster system | Monster view nodes | None currently | SIMULATION IN PROGRESS |
| Player rank/progression | Balance definitions | Progression system | HUD/actions | Save v2+ | SIMULATION IN PROGRESS |
| Drops, materials, pills | Item/balance definitions | Progression system | Inventory/crafting UI | Save v2+ | SIMULATION IN PROGRESS |
| Soul acquisition/progression | Soul Nature definitions | Soul system | Orb/inventory views | Save v1+ | SIMULATION PARITY IN PROGRESS |
| Soul Banner/loadout | Banner definitions | Soul Banner system | Banner panel | Save v1+, legacy aliases | SIMULATION PARITY IN PROGRESS |
| Summon/recovery | Soul/monster definitions | Summon system | Ally view nodes | Save v3+ | SIMULATION PARITY IN PROGRESS |
| Devouring and Essence | Soul Nature definitions | Essence milestone state/modifier ownership ported; devour transaction pending | Confirmation/results UI | Save v4+ pending | IN PROGRESS |
| Bloodline | Soul Nature definitions | Milestone state and modifier ownership ported with parity checks | Progress UI/VFX | Save v5 imported; v6 round-trip covered | IN PROGRESS |
| Possession | Soul Nature definitions | Possession lifecycle, temporary modifiers and capability ownership ported with parity checks | Form/animation adapter | Save v5 imported; v6 round-trip covered | IN PROGRESS |
| Capabilities/world objects | Map definitions | Capability/World systems | Collision/visual proxy | Review required | NOT STARTED |
| Save/load migration | Versioned save contracts | Application persistence boundary | Platform storage adapter | Versions 1–5 | NOT STARTED |

For every row, parity evidence must cover deterministic results and at least one representative visual/playtest scenario. New content is not a substitute for closing existing parity gaps.
