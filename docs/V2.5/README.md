# Solo vs Mortal — V2.5 implementation records

The closed gameplay authority is outside this checkout at `C:/ws/asset-production-system/Solo_vs_Mortal_Gameplay_System_V2.5.md`, revision `2026-09-08.closed-1`. The runtime accepts only the hash-pinned data in `data/v2.5/`; these records never override that authority.

## Current records

- Art workflow (2026-09-13): [one asset → import → user review](../../../asset-production-system/solo-vs-mortal-art/SKILL.md). The production repository's old batches, raw images, masters, dashboards and QA files have been deleted at the user's request. The game now imports `assets/art/player.base.idle.s.png` as a single static Debug trial; F7 reloads art in Arena, F8 shows optional frame/scale metadata. Other live legacy images are retained pending separate cleanup approval, following an automatic review rejection of a destructive live-catalog reset. Older asset reports below are historical, not the active production workflow.
- [Implementation progress](IMPLEMENTATION_PROGRESS_V2.5_2026-09-09.md) is append-only implementation evidence. Its original plan is preserved and later work is recorded under the append-only journal.
- [Current implementation status](CURRENT_IMPLEMENTATION_STATUS.md) is the one-place current-state index. It points to evidence without rewriting historical plans or audit records.
- [Asset dependency inventory](ASSET_DEPENDENCY_INVENTORY_V2.5.md) records protected groups, canonical runtime mappings, and the deletion gates for part 6.
- [Asset Integration Trial](ASSET_INTEGRATION_TRIAL_2026-09-10.md) records the 161-ID, presentation-only Trial package, provenance checks, runtime/viewer boundaries, and user visual-review route.
- [Slice scope reconciliation](SLICE_SCOPE_RECONCILIATION_V2.5.md) locks the 161 Slice 01 logical IDs, explains the former 158/161 mismatch, and separates workflow state from user approval.
- [Slice output audit](SLICE_OUTPUT_AUDIT_V2.5.json) records the hash/manifest verification result for each locked ID at the audit snapshot.
- [Slice dispatch plan](SLICE_DISPATCH_PLAN_V2.5.md) orders only the remaining independent production batches and records the Player identity approval gate.
- [Combat runtime trace](COMBAT_RUNTIME_TRACE_V2.5.md) records the fixed-tick order and the user-owned combat acceptance boundary.
- [Source audit](source-audit.md) maps the authority to source, data, assets, and the user-owned manual acceptance cases.
- [Work items](work-items.json) and [tasks 4–8](implementation-tasks-4-8.json) are machine-readable execution records. They are planning records, not gameplay configuration.

## Documentation boundary

`../ARCHITECTURE.md`, `../MAP_SYSTEM.md`, `../GAME_TERMINOLOGY.md`, `../GAME_TERMINOLOGY_VN.md`, and `../PERFORMANCE_BUDGET.md` are retained historical prototype references. Their headers identify that status. They must not be used to change V2.5 rules, balancing, assets, or save contracts.

Vendor readmes and license files stay with their corresponding dependency. They are not gameplay authority.

## Validation boundary

The official V2.5 implementation baseline is Godot .NET 4.7.2, established by the user on 2026-09-10. `project.godot` currently advertises Godot 4.7 C# Forward Plus, while `export_presets.cfg` exports Windows with `gl_compatibility`; that configuration difference is documented, not changed, by ISSUE-01. Agents may run a 4.7.2 compile/export as technical evidence. The user owns real-Godot gameplay acceptance. Do not add automated gameplay runners or test harnesses.
