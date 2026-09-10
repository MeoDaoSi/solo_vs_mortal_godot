# ISSUE-04 — Soul Loop closure matrix

**Agent-side status:** `ISSUE-04 CODE_COMPLETE — READY_FOR_USER_ACCEPTANCE`  
**Authority:** `C:/ws/asset-production-system/Solo_vs_Mortal_Gameplay_System_V2.5.md`, revision `2026-09-08.closed-1`. On 2026-09-10 all six SHA-256 entries in `data/v2.5/spec-lock.json` matched the active hyphenated authority workspace. This matrix is a source/runtime-path audit, not a gameplay-acceptance report.

## Requirement-to-runtime matrix

| Requirement | Authority | Owner / live producer → caller | Durable boundary | UI / manual case | Status |
|---|---|---|---|---|---|
| Soul drop and pity | §7 | `SoulSystem.SpawnCanonicalDrop` subscribes to eligible `MonsterDefeatedEvent`; banner gate precedes roll/pity | canonical pickup, pity and RNG state are captured by `GameApplication.CaptureCanonicalSave` | world pickup marker; cases 4, 14 | `CORRECT` |
| World pickup and capture | §7 | `SoulSystem.AutoCollect` / `AcquireNear` → `AcquireCanonical` | density transaction, first ownership, consumed pickup ID and deferred event share one WAL payload | E / 48-unit auto pickup; cases 4–6, 14 | `CORRECT` |
| Ownership and Density | §7–8 | `V25DensityEngine.Apply` is the sole ownership/Density writer; `V25DensityTransactionRequest.FromSource` supplies pre-transaction rank | immutable receipt/ledger, strict snapshot validation and staged restore | Soul loop panel; cases 3–6 | `CORRECT` |
| Pending, proofs and gates | §8 | `V25DensityEngine.StageTransition` releases old pending before new gain and preserves proof keys | ownership and ledger conservation checks | Soul loop panel; cases 4–6 | `CORRECT` |
| Sync ledger | §10 | `V25SyncSystem.RecordEvent` / `TryClaim`; award IDs are species/source-key stable | source versions, progress/event dedup, award totals and milestones capture/validate/restore | Soul loop panel; cases 12–14 | `CORRECT` |
| All authored Sync sources | §10; `content.v2.5.json` | 19 species × 8 sources: defeat and hunt from `OnMonsterDefeated`; Ally/possession lethal sources; landmark/secret in `GameSession.World`; ritual in `V25SyncSystem` | progress is durable before the next input and claims are immutable | E hold at shrine and Soul loop Sync view; cases 12–13 | `CORRECT` |
| Banner | §9 | `SoulBannerSystem.UpgradeCanonical` checks rank, shrine, boss fact and eligible owned Density | rank and contiguous gate receipts strictly restore | feature panel; case 18 | `CORRECT` |
| Summon / Recall / all variants | §11 | `SummonSystem.SummonCanonical`, `SummonAllCanonical`, `RecallCanonical`, `RecallAllCanonical` | mode, vitality, recovery, actor link and retained cooldowns capture/restore | per-Soul buttons; Shift+Q / Ctrl+Q; cases 10, 15 | `CORRECT` |
| Spirit and lifecycle | §11 | `V25SpiritSystem.Tick`, `SummonSystem.Update`, Player/Ally defeat subscriptions | Spirit milli and both carries, recovery and runtime actor data restore strictly | HUD + Soul loop panel; cases 10, 15 | `CORRECT` |
| Placement and Part-4 Ally pathing | §11 | `V25Navigation.FindNearestFree` / A* route used by `PlayerSystem`, `SummonSystem`, `AllySystem` | actor positions and AI route state validate on restore | no-space is non-mutating; cases 10, 15 | `CORRECT` |
| Ally AI / focus / formation | §11 | `AllySystem.UpdateCanonical`; `GameApplication.FocusAlliesAt` / `ToggleCanonicalAllyMode` | mode, focus, timers, target and formation-relevant actor state restore | G focus cursor, H Guard/Assault, feature panel; cases 10, 15 | `CORRECT` |
| Possession / capability source | §12 | `PossessionSystem.StartCanonical` / `EndCanonical`; source-instance capability grant/revoke | immutable snapshot, cooldowns and transition lock validate/restore | per-Soul possession and end button; case 11 | `CORRECT` |
| Part-4 persistence and retry | §13 | `GameApplication.CaptureCanonicalSave` → `V25SaveStore` WAL → isolated `RestoreCanonicalSave` | checksums, IDs/references and staged swap reject malformed state | Save/load controls; case 14 | `CORRECT` |
| Application / Presentation reachability | §3, §59 | `Arena` → `GameApplication` → `GameSession` canonical calls only | successful gameplay mutations request durable commit before further input | E, Q modifiers, G/H and panel controls; cases 4–6, 10–15 | `CORRECT` |
| Legacy isolation | removed-mechanics list | canonical `GameSession` does not construct Devour/Essence/Bloodline; canonical Application branches fail closed | legacy v1–v6 is recognized but not invented as V2.5 state | no legacy command in canonical panel | `CORRECT` |
| Real gameplay behavior | `AGENTS.md` | user runs the scene and records outcomes | not agent-verifiable | all 18 cases below | `USER_ACCEPTANCE_ONLY` |

## Confirmed gaps fixed in this closure

1. `V25SyncSystem` had producers only for generic hunt and elite/boss defeat. It now records Ally lethal kills, kills while actively possessed, and the authored three-second seven-source shrine ritual. Damage, death, movement, release, and changed prerequisites cancel the ritual without an award.
2. Canonical Summon placement had used a universal 18-unit body. `SummonSystem` now passes `V25ActorBodyRadii.ForSpeciesRole`, while `V25Navigation` keeps the locked South-first, clockwise 16-unit ring search and A* blocker routing.
3. Summon was not blocked during the canonical 300 ms possession transition. `GameSession` now provides the transition lock to the canonical summon guard.
4. `GameApplication.AcquireNearbySouls` permitted direct acquisition while the canonical Player was dead. The Application boundary now rejects it.
5. SummonAll, RecallAll, Ally focus/mode and possession termination lacked a usable canonical caller. `GameApplication`, `Arena`, and `project.godot` now expose panel controls plus Shift+Q, Ctrl+Q, G and H.
6. Recall discarded the live Ally UID that keyed combat cooldowns. `SummonSystem` transfers accepted cooldowns to a Ready species record, reattaches them to its next Ally UID, and persists/strictly validates that record. Empty cooldown collections for live Summoned/Dispersed rows no longer cause a valid save to be rejected.
7. The tutorial spawn endpoint accepted arbitrary species and level. It now accepts exactly the locked Skeleton Level 1 world Soul shape, keyed by its quest receipt.
8. Pity/world-drop and Sync-progress mutations could wait for periodic autosave. `SoulSystem` and `V25SyncSystem` now require the same immediate canonical WAL commit/retry boundary used by capture.

## Persistence coverage

| State | Capture | Validation | Restore |
|---|---|---|---|
| Ownership, Density, pending/proofs, ledger | `OwnedSpecies`, `DensityAwards` | `V25SaveCodec` plus `V25DensityEngine.RestoreSnapshot` | `SoulSystem.RestoreCanonicalState` |
| Pickup, pity, consumed IDs, tutorial receipt | `WorldSouls`, `SoulPity`, receipt collections | ID, species, source-rank and consumed-set checks | `SoulSystem.RestoreCanonicalState` |
| Sync sources/awards/milestones | `Sync`, receipt rows | source/award/version/dedup/milestone checks | `V25SyncSystem.Restore` |
| Banner | rank and `BannerReceipts` | contiguous rank/gate/version checks | `SoulBannerSystem.RestoreCanonicalState` |
| Spirit | Player runtime plus two Spirit carries | bounded integer carry checks | `V25SpiritSystem.RestoreRemainder/RestoreRateRemainder` |
| Summon / Ally | owned mode/link/vitality plus runtime actors, `SummonStates`, runtime cooldowns | one actor/species, mode/UID/life/skill-reference checks | `AllySystem.RestoreCanonicalRuntime`, `SummonSystem.RestoreCanonicalState` |
| Recalled cooldowns | `SummonStates.SkillCooldowns` | positive, unique, authored skills; only Ready may be non-empty | `SummonSystem.RestoreCanonicalStoredCooldowns` |
| Possession and capabilities | possession snapshot, cooldowns, transition lock | snapshot/range/source/species checks | `PossessionSystem.RestoreCanonical` re-applies only that source instance |
| WAL identity | envelope checksum, save/transaction/sequence | `V25SaveStore` recovery and `V25SaveCodec` validation | isolated `GameApplication` swap only after all checks pass |

## User manual acceptance handoff

Run in the real Arena scene. Capture a screenshot of the HUD/Soul loop panel and the save/reload result when a case fails; include the displayed IDs/values and the action sequence. Do not treat a build as a pass.

| Case | Source readiness | User action / expected result |
|---|---|---|
| 1 `rank.bounds` | `V25ProgressionRules`, Density projection | Inspect the listed level boundaries; ranks are 1,1,2,8,9,9. |
| 2 `rank.invalid` | canonical validation | Try invalid progression input through the available debug/manual route; it is rejected with no state mutation. |
| 3 `density.knots` | `V25DensityMath` | Inspect Soul levels at listed committed Density knots; expect 1,18,29,45,87,88,89,90,90. |
| 4 `density.pending` | `V25DensityEngine` | At D99, make a valid 30-point no-proof capture; expect D100/pending25/pass0/discard4. |
| 5 `density.proof_once` | `V25DensityEngine` | At D100/pending25, gain proof1 then retry its transaction; expect D125/pending0/pass1 and unchanged retry. |
| 6 `density.proof_with_gain` | `V25DensityEngine` | Apply proof1 and gain2 together; expect D127/pending0/pass1. |
| 7 `shield.full` | combat prerequisite | Hit a full 5 shield with mitigated 5; HP loss is zero and shield absorbs 5. |
| 8 `shield.minimum` | combat prerequisite | Hit shield1 with mitigated0.2; HP loss is zero and shield absorbs 1. |
| 9 `shield.immune` | combat prerequisite | Hit an immune target with raw100; HP/shield stay unchanged. |
| 10 `spirit.net_zero` | `V25SpiritSystem`, `SummonSystem` | With Spirit1, regen60/min and drain120/min, observe a 2s run: recall at 1s, Spirit1 at end, no auto-resummon. |
| 11 `possession.cap` | `PossessionSystem` | Use permanent100 + temporary200 with uncapped transfer200; inspect transfer60/final260. |
| 12 `sync.rebalance` | `V25SyncSystem` | Load an old 10-point source, change current definition only in a documented manual save flow, retry source; one award and total10 remain. |
| 13 `sync.milestone` | `V25SyncSystem` | With prior Sync20/milestone20, verify a new threshold does not erase historical award/milestone ID. |
| 14 `save.crash` | `V25SaveStore` | Induce the documented write interruption between staged reward and commit, then retry identical transaction; observe prior full or one full new state, never split/duplicate. |
| 15 `recall.lethal` | `SummonSystem`, combat coordinator | Cause Ally HP10 lethal10 and Recall in the same simulation tick; expect Dispersed/vitality0/recovery20,000 ms. |
| 16 `beta.xp_boss20` | progression prerequisite | Beta Player19 defeats level20 boss: positive global `XpRequirement(20)` reward. |
| 17 `beta.cap` | progression prerequisite | At beta Player20, grant XP; no extra XP and no rank3 breakthrough. |
| 18 `profile.upgrade` | Banner/Save profile path | After boss2 with Chaos, upgrade beta→full; preserve progress and award Despair/quest rewards only once. |

## Deferred cleanup only

`DevourSystem`, `EssenceSystem`, `BloodlineSystem`, legacy SoulBanner bind/tier code, old SoulNature definitions and v1–v6 payload types remain solely for compatibility/bootstrap. They are not constructed or called on the canonical V2.5 path. Physical removal waits for Part 6 dependency/migration gates.

`NO_ASSET_APPROVAL_REQUIRED_FOR_ISSUE_04 — visual asset production/in-engine visual acceptance belongs to Part 6/7/W09.`
