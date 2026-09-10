# V2.5 asset dependency inventory

**Snapshot:** 2026-09-10  
**Purpose:** Part 6.01 evidence before any physical deletion. This record lists ownership and replacement conditions; it is not an instruction to delete a directory by pattern.

## Canonical requirement coverage

`data/v2.5/asset-requirements.v2.5.json` contains **10,700** declared V2.5 AssetIds, all currently marked `not_generated`. The beta profile (`beta_01`) requires **1,271** IDs:

| Group | Beta requirements |
|---|---:|
| audio | 56 |
| equipment | 178 |
| items | 2 |
| monsters | 241 |
| player | 61 |
| skills | 37 |
| souls | 456 |
| ui | 31 |
| unique | 1 |
| world | 208 |

An AssetId requirement is a production target, not proof that an image exists. `asset-catalog.v2.5.json` is the only runtime mapping catalogue; it carries only physically imported, hash-verified files.

## Protected and imported source package

| Group / file | Owner | Reference / replacement state | Action |
|---|---|---|---|
| `assets/v2.5/skeleton-static-integration-v001/*.png` | Art export `skeleton-static-integration-v001` | Four user-authorized, technically and visually QA-passed static Skeleton rank-01 files. Hashes and source path are pinned in `data/v2.5/asset-catalog.v2.5.json`. `inEngine` remains false until manual user review. | retain |
| `C:/ws/asset-production_system/art/exports/skeleton-static-integration-v001/` | Art production repository | Provenance package and the source `asset-map.json`; it is not a project runtime dependency after the four PNGs are copied. | retain outside game project |
| Raw/master/normalized/preview files in `C:/ws/asset-production_system/art/` | Art production repository | Production provenance and user review evidence. | retain outside game project |
| `LICENSE*`, vendor readmes and third-party legal files | Respective vendor | License/legal dependency. | retain |

The imported Skeleton art is deliberately static. It may render only `soul.skeleton.rank01.pickup`, `.banner.icon`, `.enemy.south`, and `.ally.south`. It does not stand in for any other species, direction, rank, or animation.

## Current runtime consumers and migration state

| Runtime site | Previous dependency | Current V2.5 state | Next condition |
|---|---|---|---|
| `Arena` map background/world props | `data/asset-manifest.json` + old files | Queries canonical catalog only. Unmapped world AssetIds render an explicit `MISSING: <AssetId>` marker. | Map only when approved canonical world/tile export exists. |
| `Arena` Soul pickup | generic `soul.orb.no_boc` | Resolves species/rank pickup ID from canonical catalog. Skeleton rank-01 uses its imported 24×24 frame/pivot. | Add an approved pickup entry per species/rank. |
| `Arena` Monster/Ally | generated old animation paths and hard-coded scales | Skeleton rank-01 uses its explicit static actor asset with origin/pivot/layering. All other actors render explicit missing markers; no Skeleton reuse. | Add approved animation/static entries for each requested actor. |
| `Arena` Player | old sprite-sheet path and fixed `2.5` scale | Explicit `player.base.idle.s` missing marker; the obsolete raw-sheet path is no longer called by canonical presentation. | Add a user-approved player package with clips/directions. |
| `HudMinimap` ring | hard-coded Kenney image path | Drawn with primitives while canonical marker assets are absent. | Replace only after `ui.minimap_marker` and `ui.quest_marker` are in a canonical export. |

## Filesystem groups retained pending replacement

| Project group | Snapshot size | Why retained | Delete gate |
|---|---:|---|---|
| `assets/` | 5,018 files / 209,846,810 bytes | Contains old runtime art, third-party content, and the new `v2.5` package. | Every live caller moved; legal ownership resolved; each file group has an explicit replacement ID or retain reason. |
| `spritesheets/` | 2,562 files / 52,103,059 bytes | Old player/monster animation generation source may still be needed by non-canonical compatibility paths. | Compatibility path removed or given a named retained boundary. |
| `spriteframes/` | 1,275 files / 13,479,675 bytes | Generated legacy animation resources. | No scene/export/importer references remain after caller scan. |
| `build/` | 212 files / 928,356,309 bytes | Includes current 4.7.2 technical export and generated build material. | Separate generated-output cleanup after user confirms artifact retention. |

## Part 6 decision log

1. No old image, sprite sheet, resource, import file, sample root, or license is deleted in this phase.
2. Missing V2.5 art is observable in the running presentation instead of silently substituted with prototype art or a Skeleton from another role.
3. The next step is to extend `asset-catalog.v2.5.json` from an approved Art export and verify every new file hash, frame rect, duration, pivot, socket and layering value at load time. Then migrate each current missing marker one ID at a time.
