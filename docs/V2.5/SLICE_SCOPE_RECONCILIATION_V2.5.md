# Slice 01 scope reconciliation — V2.5

**Status:** complete for 7.01 scope reconciliation; this is not an art-approval, export, or in-engine acceptance record.

The exact machine-readable membership is locked in `data/v2.5/slice-01.scope-lock.v2.5.json`. It contains all 161 logical IDs, their source production status, and the source-catalog/worklist hashes used for this decision.

## Authority and fixed membership

- Production catalog: `C:/ws/asset-production_system/art/production/catalog.json`, scope version `2.5.1-closed`.
- Production worklist: `C:/ws/asset-production_system/art/production/slice-parts.json`.
- Reconciled scope version: `slice-01.reconciled.2026-09-10`.
- Slice membership: every catalog entry whose `slice01` field is `true` at the hashes recorded in the lock file.
- Stable membership digest: `scopeMembershipSha256` in the lock is calculated from the sorted ID list, so an automatically refreshed catalog timestamp cannot invalidate the scope decision.
- Result: **161 unique logical AssetId**. No duplicate ID exists in the 161 entries.

The lock prevents an output from being counted merely because it happens to be in a directory. Adding or removing a Slice asset requires a new lock with a stated reason and new source hashes.

## Why 158 and 161 differed

`slice-parts.json` is a **production worklist**, not the complete logical scope. Its 49 parts contain 158 unique AssetId; all 158 are present in the 161 catalog scope. The catalog has three additional logical IDs deliberately omitted from that generation worklist:

| Catalog-only ID | Reason | Current handling |
|---|---|---|
| `player.base.idle.s` | Existing `beta-slice-v002` candidate is marked `needs_rework`; it must not be counted as an approved player direction. | Make a replacement only after the next generation batch is dispatched; its result returns to pending user review. |
| `soul.skeleton.rank01.enemy.south` | User authorized reuse of the existing static south-facing Skeleton visual. It is a runtime static fallback, not an animation-generation task. | Imported with hash verification into the V2.5 project catalog; visual/in-engine acceptance remains open. |
| `soul.skeleton.rank01.ally.south` | Same as enemy south: user-authorized static reuse, not an animation-generation task. | Imported with hash verification into the V2.5 project catalog; visual/in-engine acceptance remains open. |

The other two user-authorized Skeleton static files (`pickup` and `banner.icon`) are already represented by worklist part `ART-34`; that is why they do not add to the numerical difference.

## Current production state — not approval

| State in source catalog | Count | Meaning |
|---|---:|---|
| `generated` | 101 | A normalized output was recorded by the production workflow. This does not mean the user accepted its appearance. |
| `needs_rework` | 4 | Previous output may be retained for history only; it cannot be promoted or packaged as approved art. |
| `not_generated` | 56 | No output exists yet. |

There are 105 entries with an output record (101 generated plus 4 needing rework). Of those, source QA currently records 105 technical checks, 5 visual checks, 5 frame-isolation checks, 1 motion check, and **0 in-engine checks**. Those flags are production evidence; they do not replace the user's artistic decision.

## Status model for all future batches

1. A newly normalized output enters `pending_user_review`, after its technical checks and provenance/hash are recorded. It is not `approved`.
2. The user may mark it `approved`, `needs_rework`, or `rejected`. Only `approved` is eligible for the canonical game export.
3. A rework creates a new version/hash and again enters `pending_user_review`. Older versions remain historical evidence and cannot silently inherit approval.
4. Static-reuse permission means the file may be included as a static fallback. It still needs visual and in-engine review before a completed Slice package can claim acceptance.

This vocabulary is intentionally separate from the older catalog field `status`, which currently reports production generation state and cannot be treated as user approval.

## Dispatch boundary after 7.01

The next automated Art batches must use only the 158 `production_worklist` IDs in the scope lock, grouped by dependency. The three catalog-only entries above must retain their stated paths: one replacement candidate for the player South idle and two existing static Skeleton fallbacks. No raw contact sheet, preview, master, or prototype file can be substituted for a missing locked AssetId.

Before 7.02 assembly, every required ID needs a file, hash, manifest, frame/pivot metadata where applicable, and an explicit user decision. The package must list the static fallbacks and every excluded/missing ID; it must remain `complete: false` while any locked requirement is unresolved.
