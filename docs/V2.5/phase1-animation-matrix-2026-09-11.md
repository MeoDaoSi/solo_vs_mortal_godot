# Phase 1 animation matrix — Skeleton Rank 1

Date: 2026-09-11  
Source: `data/v2.5/asset-catalog.v2.5.json` (static catalog inspection only; not a Godot or gameplay test).

Classification rules:

- `missing_source`: no matching logical AssetId exists in the integration catalog.
- `integration_trial`: catalog entry exists with `approvalStatus: integration_trial_authorized`; its QA data still has `visual: false` and `inEngine: false`.
- `integration_ready`: technically recorded batch available for integration, but not yet release-approved.
- `release_ready`: visual and in-engine evidence approved. None of the entries below have this state.

## Enemy — `soul.skeleton.rank01.enemy`

| Clip | S | W | E | N | Result |
|---|---|---|---|---|---|
| idle | missing_source | missing_source | missing_source | missing_source | Missing 4/4 |
| move | missing_source | missing_source | missing_source | missing_source | Missing 4/4 |
| attack | integration_trial (6 frames) | missing_source | missing_source | missing_source | Missing 3/4 |
| hit | integration_trial (2 frames) | integration_trial (2 frames) | integration_trial (2 frames) | missing_source | Missing 1/4 |
| death | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | Present 4/4 |

Enemy Rank 1 therefore does not meet the minimum integration set. It needs source/rework for idle ×4, move ×4, attack W/E/N, and hit N. Existing entries remain trial-only rather than visually or in-engine approved.

## Ally — `soul.skeleton.rank01.ally`

| Clip | S | W | E | N | Result |
|---|---|---|---|---|---|
| idle | integration_trial (4 frames) | integration_trial (4 frames) | integration_trial (4 frames) | integration_trial (4 frames) | Present 4/4 |
| move | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | Present 4/4 |
| attack | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | Present 4/4 |
| hit | integration_trial (2 frames) | integration_trial (2 frames) | integration_trial (2 frames) | integration_trial (2 frames) | Present 4/4 |
| disperse | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | integration_trial (6 frames) | Present 4/4; substitutes for ally death |

Ally Rank 1 has complete direction coverage for its required clips, but every animated entry remains integration-trial only. Its static `ally.south` compatibility asset is deliberately excluded from animation coverage.

## Runtime fallback boundary

`Arena.PlayActorAnimation` receives the exact requested AssetId and animation name. When the requested named clip is absent, it selects the explicit `missing` animation and records the required AssetId for the magenta missing-asset marker. It does not substitute another direction, another clip, or a static Skeleton asset. This is the required no-silent-fallback behavior.

## Follow-up queue

1. Produce Enemy Rank 1 idle/move directions before presenting enemy locomotion as complete.
2. Produce Enemy Rank 1 attack W/E/N and hit N, with fixed pivot and per-frame motion evidence.
3. User reviews all trial animations at native in-game scale before their state may become release-ready.
