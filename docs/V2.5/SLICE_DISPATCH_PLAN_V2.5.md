# Slice 01 dispatch plan — V2.5

**Status:** ready to dispatch only independent work. This document is a production queue, not an approval record and not a claim that an image exists.

## Inputs and non-negotiable rules

- Exact scope: `data/v2.5/slice-01.scope-lock.v2.5.json`, `scopeMembershipSha256 = 994ebfd3d946d506a085452da40523ce141cd5b22ab020bdd95d301f15143df6`.
- Runtime/source audit: `SLICE_OUTPUT_AUDIT_V2.5.json`.
- Style/reference authority: `C:/ws/asset-production-system/solo-vs-mortal-art/references/style-lock.json`, `art-direction.md`, and `approval-policy.md`.
- Each raster request uses the accepted style reference as a real input and produces a separate source candidate. A raw ImageGen board is never an atlas or runtime texture.
- Pillow may normalize only after the source is saved; it must keep source, recipe, frame/pivot, palette, alpha, crop and hash evidence.
- A fresh file begins as `generated` / `pending_user_review`. Neither technical pass nor an agent's view can promote it. The user records `approved`, `needs_rework`, or `pending` against its file and manifest hashes in `user-reviews.json`.

## Verified starting state

| State | Count | Dispatch action |
|---|---:|---|
| Registered output, user decision still pending | 101 | Do not regenerate. Keep ready for the user's review queue. |
| `needs_rework` | 4 | Replace Player idle S/W/E/N as one identity batch; do not reuse as a motion master. |
| No registered output | 56 | 40 independent world assets can be generated now. 16 Player animation assets wait for Player idle identity approval. |
| User-authorized static reuse | 4 | Preserve hash-pinned Skeleton static images. They remain static fallbacks and still require later runtime QA. |

All 105 registered outputs, their hashes, and their manifests exist and match. The 18 source manifests pass the technical validator. That only verifies files and metadata. No Slice asset has in-engine QA, so the number eligible for an integration export is zero.

The registered Player W/E/N files in `beta-slice-v003` are already the three tracked rework outputs; Player South is the tracked `beta-slice-v002` output. They are retained for history and must not be overwritten or silently recorded as a new version.

## Queue A — independent Ash Graves generation

These seven parts have no dependency on a new Player identity and may run while Player review is pending. Each part is one discrete batch, so an ImageGen quota/tool failure leaves a precise resumable boundary. A batch saves every raw source and normalized output before moving to the next part.

| Order | Part | IDs | Count | Required delivery before next part |
|---:|---|---|---:|---|
| A1 | `ART-43` | `world.ash_graves.ruin.mask08` through `mask15` | 8 | Eight separate 32×32 tile runtime files; exact file/hash/manifest; technical check; pending user review. |
| A2 | `ART-44` | `world.ash_graves.wall.mask00` through `mask07` | 8 | Same, with coherent wall edge/corner contract; no raw contact sheet as atlas. |
| A3 | `ART-45` | `world.ash_graves.wall.mask08` through `mask15` | 8 | Same. |
| A4 | `ART-46` | `world.ash_graves.wall.cap.n`, `.e`, `.s`, `.w` | 4 | Four 32×32 tile runtime files and a tile mask/edge explanation. |
| A5 | `ART-47` | `world.ash_graves.shrine`, `.chest`, `.portal`, `.seal` | 4 | Four independent prop files using the prop contract, grounded pivot metadata and no gameplay behavior embedded in art. |
| A6 | `ART-48` | `world.ash_graves.landmark`, `.pillar`, `.rock`, `.tree_or_spire` | 4 | Same. |
| A7 | `ART-49` | `world.ash_graves.banner`, `.bridge_segment`, `.torch`, `.debris` | 4 | Same. |

For every Queue A batch: record the actual ImageGen output/tool fields if returned, reference file hashes, normalization recipe, exact dimensions, color/alpha check, pivot (props) or tile metadata (tiles), and manifest hash. Run the technical validator. Update the production catalog/dashboard only after the record is valid. If an image cannot satisfy its contract after two targeted attempts, mark only that AssetId blocked/rework and continue the independent IDs.

## Queue B — Player identity gate

| Order | Work | IDs | State gate |
|---:|---|---|---|
| B1 | `ART-01` replacement identity idle | `player.base.idle.s`, `.w`, `.e`, `.n` | All four existing revisions are `needs_rework` from user feedback about inconsistent size/clothing. Create four new directional idle clips with shared identity anchors and identical frame canvas/pivot contract. Each output returns to `pending_user_review`. |
| B2 | `ART-02`–`ART-09` dependent clips | 16 Move/Attack/Hit/Death direction IDs | **Blocked until the user explicitly approves the new B1 Player identity for this version/hash.** Then dispatch each listed part in its own two-ID batch and keep the approved identity anchors fixed. |

This gate is deliberate: creating movement/attack from the rejected Player idle candidate would spread the same inconsistency through 16 more assets. It does not block Queue A.

## Existing Skeleton, equipment, and early world output

The remaining 101 generated outputs are not a generation queue. Their technical manifests are valid, but their visual/frame-isolation/motion/in-engine review remains incomplete. The dashboard must present them as `Đã tạo · chờ bạn duyệt`; it must not infer approval from `generated`, old `qa.visual`, or static-reuse permission.

The Skeleton preview uses timestamp-based elapsed milliseconds and the manifest duration arrays (Idle 4×200 ms; Move 6×100 ms in the current preview data). Its current technical data has no detected 2-pixel edge-risk frame, but this does not prove motion quality or clear it for runtime use.

## Assembly gate for 7.02

Do not create a complete export until every locked ID has a hash-verified output, manifest and user decision; animation also needs frame isolation/motion evidence, and runtime release needs in-engine evidence. A partial kit is allowed only with an explicit `excluded` list and `complete: false`. It may include the four static Skeleton reuse files only as declared fallbacks, never as replacements for missing animated Player/world assets.
