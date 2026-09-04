# Feature parity matrix

This is the acceptance matrix for Phaser-to-Godot behavior. A feature is complete only when its definitions, rules, mutable ownership, application boundary, presentation, persistence impact, and parity evidence are accounted for.

| Feature | Definition owner | Runtime owner | Godot presentation | Save impact | Status |
| --- | --- | --- | --- | --- | --- |
| Player movement/collision | Player/map definitions | Player system | Character body adapter | Position is currently not persisted | SIMULATION IN PROGRESS |
| Combat and damage | Balance definitions | Combat and actor systems | Animation/VFX adapter | HP | SIMULATION IN PROGRESS |
| Monster spawn/AI/death | Monster definitions | Monster system | Monster view nodes | None currently | SIMULATION IN PROGRESS |
| Player rank/progression | Balance definitions | Progression system | HUD/actions | Save v2+ | NOT STARTED |
| Drops, materials, pills | Item/balance definitions | Progression system | Inventory/crafting UI | Save v2+ | NOT STARTED |
| Soul acquisition/progression | Soul Nature definitions | Soul system | Orb/inventory views | Save v1+ | NOT STARTED |
| Soul Banner/loadout | Banner definitions | Soul Banner system | Banner panel | Save v1+, legacy aliases | NOT STARTED |
| Summon/recovery | Soul/monster definitions | Summon system | Ally view nodes | Save v3+ | NOT STARTED |
| Devouring and Essence | Soul Nature definitions | Soul/Essence systems | Confirmation/results UI | Save v4+ | NOT STARTED |
| Bloodline | Soul Nature definitions | Bloodline system | Progress UI/VFX | Save v5 | NOT STARTED |
| Possession | Soul Nature definitions | Possession system | Form/animation adapter | Save v5 | NOT STARTED |
| Capabilities/world objects | Map definitions | Capability/World systems | Collision/visual proxy | Review required | NOT STARTED |
| Save/load migration | Versioned save contracts | Application persistence boundary | Platform storage adapter | Versions 1–5 | NOT STARTED |

For every row, parity evidence must cover deterministic results and at least one representative visual/playtest scenario. New content is not a substitute for closing existing parity gaps.
