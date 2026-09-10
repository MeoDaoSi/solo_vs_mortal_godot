# Solo vs Mortal documentation authority

The closed V2.5 gameplay authority lives outside this source checkout at:

`C:/ws/asset-production-system/Solo_vs_Mortal_Gameplay_System_V2.5.md`
Revision: `2026-09-08.closed-1`  
Content: `svm-content-2.5.1`  
Balance: `svm-balance-2.5.1`  
Save schema: `1`

The bundled, hash-pinned machine-readable inputs are under `data/v2.5/` and are validated by `CanonicalV25Loader` during both `Main` and `Arena` application bootstrap. The locked hashes are in `data/v2.5/spec-lock.json`; a source or UI document cannot override that bundle.

## Source records

* `docs/V2.5/README.md` is the entry point for current V2.5 implementation records and the historical-document boundary.
* `docs/V2.5/source-audit.md` records the complete source/data/assets/docs/build comparison and manual acceptance checklist.
* `docs/V2.5/work-items.json` is the machine-readable implementation order, dependencies, deletion risks, and acceptance mapping.

## Historical and supporting documents

`docs/ARCHITECTURE.md`, `docs/MAP_SYSTEM.md`, `docs/GAME_TERMINOLOGY.md`, `docs/GAME_TERMINOLOGY_VN.md`, and `docs/PERFORMANCE_BUDGET.md` describe the pre-V2.5 prototype and are historical until migrated. They do not define current gameplay. `Readme_for_Users.md` and license files are vendor/sample or legal documents and remain outside gameplay authority.

## Engine and testing boundary

The implementation reuses the typed C# project while keeping Godot calls in Presentation. The official V2.5 implementation baseline is Godot .NET 4.7.2, established by the user on 2026-09-10. The editor project advertises Godot 4.7 C# Forward Plus, while the Windows export preset selects `gl_compatibility`; this rendering-configuration difference is recorded separately and is not changed by the engine-baseline decision. A successful 4.7.2 compile/export is technical evidence only. The user performs manual gameplay acceptance; automated tests and test harnesses are not part of the V2.5 source checkout.
