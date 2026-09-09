# Solo vs Mortal — Game terminology

> Historical prototype glossary. Current gameplay authority is the closed V2.5 bundle indexed by `docs/README.md`.

This glossary maps player-facing terms to the implementation concepts in the current Godot C# codebase. English terms are followed by the Vietnamese display term used in the game.

## Entities and progression

### Player (Nhân vật)

The player-controlled combatant. `PlayerSystem` owns the mutable `PlayerState` instance. Important fields are `Uid`, `Position`, `CurrentHp`, `MaxHp`, `Level`, `Xp`, `Rank`, `Stats`, and `AttackCooldown`. `PlayerSnapshot` is the read-only Application projection used by Presentation.

### Monster (Quái vật)

An enemy instance spawned from a `MonsterDefinition`. `MonsterSystem` owns `MonsterState`, including `Uid`, `DefinitionId`, `SpeciesId`, `Rank`, `Level`, `CurrentHp`, `Position`, `AiState`, and `AttackCooldown`. `MonsterSnapshot` is the immutable view used to render it.

### Soul (Linh Hồn)

The collectible gameplay resource created when a monster is defeated. A world pickup is `WorldSoulState`; after acquisition the persistent record is `OwnedSoulState`, both owned by `SoulSystem`. A Soul keeps its `SoulNatureId` and immutable `SoulOrigin`.

### Owned Soul (Linh Hồn Sở Hữu)

The player's persistent Soul record. `OwnedSoulState` contains `Id`, `SoulNatureId`, `Origin`, `Level`, and `Xp`. It is not the same object as the temporary summoned Ally or the world pickup.

### Soul Origin (Nguồn Gốc Linh Hồn)

Historical provenance captured when a Soul is created. `SoulOrigin` stores `MonsterUid`, `MonsterDefinitionId`, `SpeciesId`, `DisplayName`, `Rank`, `RankKey`, and `RankDisplayName`. Origin rank is intentionally retained state, not a mutable monster reference.

### Rank (Cấp Bậc)

The global combat tier shared by Player, Monster, and Soul-derived stats. `CombatPowerRules.RankStartLevel`, `RankEndLevel`, `GlobalLevelToRank`, `RankKey`, and `RankDisplayName` define conversion and display.

### Level (Cấp Độ)

The current progression level on `PlayerState`, `MonsterState`, and `OwnedSoulState`. Player level is global; rank-local ranges are authored by `MonsterDefinition.LevelRange` and `BalanceDefinition.LevelsPerRank`. There is no separate rank-local Soul level type: a Soul uses `OwnedSoulState.Level` and its captured origin rank.

### `powerTier` (Tầng Sức Mạnh)

The authored tier used to select species stat scaling and sprite stages. The C# representation is `BalanceDefinition.SpeciesById(...).PowerTier` with enum `TierId` (`Tier1`, `Tier2`, `Tier3`). The repository intentionally does not use the obsolete `potential` name.

### Combat Power (Lực Chiến)

The deterministic scalar from which combat stats are derived. `CombatPowerRules.GetCp` applies the `BalanceDefinition` base value, rank multiplier, and level growth terms. `CombatPowerRules.GetStatBlock` then resolves archetype, species, and active modifiers.

### HP (Sinh Mệnh)

Hit points. `PlayerState` and `MonsterState` expose `CurrentHp` and `MaxHp`; `AllyState` has the same pair. `Stats.Hp` is the authored/derived maximum-stat component. Damage and death are owned by `CombatSystem` and the relevant actor system.

### ATK (Công Kích), DEF (Phòng Ngự), SPD / Speed (Tốc Độ)

The primary combat stats in `StatBlock`: `Atk`, `Def`, and `Speed` (plus raw `DefRaw` and `SpeedRaw`). `CombatPowerRules.GetMovementSpeed` and `GetAttackCooldown` convert Speed into runtime pacing. Presentation only renders these values from snapshots.

## Soul Banner and runtime Soul lifecycle

### Soul Banner (Hồn Phiên)

The equipped Soul loadout container. `SoulBannerSystem` owns mutable `SoulBannerState`; `SoulBannerDefinition` supplies immutable tier data. State fields are `Id`, `DefinitionId`, `Tier`, `Level`, `Computed`, and `BoundSoulIds`. `SoulSystem` remains the owner of the Souls themselves.

### Soul Banner Slot (Ô Hồn Phiên)

One binding position in `SoulBannerState.BoundSoulIds`. `SoulBannerRules.Compute` derives `Computed.SlotLimit`; `SoulBannerSystem.Bind` validates slot and capacity limits and returns a `BindSoulResult` with `SlotIndex`.

### Soul Capacity / Soul Cost (Sức Chứa / Chi Phí Linh Hồn)

`SoulBannerState.Computed.CapacityLimit` is the banner's total capacity and `SoulBannerSystem.UsedCapacity` calculates current usage. Individual cost comes from `SoulSystem.Cost`, resolving the Soul Nature's `SoulCostProfileId` to a `SoulCostProfileDefinition.Cost`.

### Active Summon Limit (Giới Hạn Triệu Hồn)

`SoulBannerState.Computed.ActiveLimit` limits simultaneously summoned Souls. `SummonSystem` is the authority and returns `SummonFailure.ActiveLimitReached` when the limit is exceeded.

### Summon / Summoned Soul (Triệu Hồn)

`SummonSystem` turns a bound, ready `OwnedSoulState` into an `AllyState`. The runtime mapping is exposed as `SoulRuntimeView` and `SummonResult`; the Ally is a separate mutable entity owned by `AllySystem` and linked by `SourceSoulId`.

### Reserve Soul (Linh Hồn Dự Bị)

An owned Soul that is not currently summoned. There is no separate reserve collection: a Soul is reserve/dormant when `SoulRuntimeView.Status` is `Ready` and it is not active in `SummonSystem`.

### Soul Runtime State (Trạng Thái Linh Hồn)

The transient lifecycle record projected by `SummonSystem.Runtime`. `SoulRuntimeStatus` has the actual enum values `Ready`, `Summoned`, `Dispersed`, and `Possessed`; `SoulRuntimeView` also carries `SummonUid`, `RecoverySeconds`, and `Stability`.

### READY (Sẵn Sàng)

The `SoulRuntimeStatus.Ready` value: the Soul can be summoned, possessed, or devoured when the corresponding Application checks allow it.

### SUMMONED (Đang Triệu Hồn)

The `SoulRuntimeStatus.Summoned` value. `SoulRuntimeView.SummonUid` identifies the current `AllyState`.

### DISPERSED / Soul Dispersal (Hồn Tán)

The `SoulRuntimeStatus.Dispersed` value while recovery is active. `SummonSystem` owns recovery timers and persists them as `DispersedSoulSaveData` / `SoulRuntimeSaveData`.

### POSSESSED (Đang Phụ Hồn)

The `SoulRuntimeStatus.Possessed` value while `PossessionSystem` has an active Soul. A possessed Soul cannot be summoned and contributes temporary modifiers/capabilities through the Possession system.

### Reinforcement (Viện Hồn)

The current implementation has no distinct `Reinforcement` type or command. `SoulBannerSystem` binding and `SummonSystem` lifecycle are the implemented Soul support flows. Status: Planned / Not implemented as a separate mechanic.

## Advanced Soul systems

### Devour (Thôn Phệ)

The one-way consumption transaction in `DevourSystem`. `DevourMode` is `CultivationXp`, `Essence`, or `Bloodline`; `DevourPreview` describes the available reward and `DevourResult` reports execution. A bound or non-ready Soul is rejected by the system.

### Devour XP / Cultivation path (Kinh Nghiệm Tu Luyện)

`DevourMode.CultivationXp` awards Player XP through `ProgressionSystem.AddPlayerXp`. The authored formula is `DevourXpProfileDefinition` (`BaseXp`, `XpPerSoulLevel`, `XpPerOriginRank`).

### Essence (Tinh Hoa)

Devour progression stored by `EssenceSystem` per `ModifierProfileDefinition` profile ID. Points and derived modifier milestones are mutable Simulation state; `EssenceSystem` refreshes the `PlayerModifierSource.Essence` contribution.

### Bloodline (Huyết Mạch)

The parallel progression path owned by `BloodlineSystem`. It uses `SoulNatureDefinitions.BloodlineProfiles`, caps at the final milestone, and contributes through `PlayerModifierSource.Bloodline`.

### Possession (Phụ Hồn)

The temporary Soul-hosting flow owned by `PossessionSystem`. `PossessionProfileDefinition` supplies `DurationSeconds`, `CooldownSeconds`, `Modifiers`, and `CapabilityIds`; `PossessionSaveData` is the save projection. No separate strain or compatibility model exists. Status: Planned / Not implemented.

### Capability (Năng Lực)

An ability ID granted by an active source. `CapabilitySystem` aggregates `CapabilitySource.Possession` IDs and answers `Has(capabilityId)`. Authored capability definitions live in `SoulNatureDefinitions.Capabilities`.

### World Interaction (Tương Tác Thế Giới)

The map-object interaction boundary in `WorldInteractionSystem`. `MapObjectDefinition` and `MapInteractionDefinition` describe authored objects; the system owns destroyed-object IDs, checks capability/radius, and returns `WorldInteractionResult`.

## Architecture and boundaries

### Definition

Immutable authored content in `src/Data/Definitions`, such as `MonsterDefinition`, `SoulNatureDefinition`, `SoulBannerDefinition`, `MapDefinition`, `AssetDefinition`, and `CharacterAnimationDefinition`. Definitions contain stable IDs and tuning, never instance UID, current HP, timers, or live node references.

### Runtime State

Mutable play-session records under `src/Simulation/State` or owned internally by a Simulation `*System`, such as `PlayerState`, `MonsterState`, `OwnedSoulState`, `SoulBannerState`, and `GameSessionState`. Simulation is the source of truth.

### Registry

Validated immutable lookup collections. `AssetDefinitions`, `SoulNatureDefinitions`, and `GameDefinitions` expose registry-like accessors (`Get`, `Monster`, `SoulBanner`, `Map`) and are not entity stores.

### Rules

Stateless deterministic formulas in `src/Simulation/Rules`: `CombatPowerRules`, `CombatRules`, `ProgressionRules`, `SoulBannerRules`, and `SpriteStageRules`.

### System

A stateful Simulation owner for one gameplay capability, for example `PlayerSystem`, `MonsterSystem`, `SoulSystem`, `SoulBannerSystem`, `SummonSystem`, `PossessionSystem`, and `WorldInteractionSystem`.

### Command

A request to mutate gameplay, exposed at the Application boundary by methods such as `GameApplication.BindSoul`, `DevourSoul`, `SummonSoul`, `StartPossession`, `Craft`, and `Interact`. There are no separate `*Command` classes in the current codebase.

### Query

A read operation exposed by Application, such as `Snapshot`, `Inventory`, `SoulLinks`, `SoulRuntime`, `WorldObjects`, and animation/asset lookup methods. Queries return copies or immutable views.

### Domain Event

A fact emitted after a Simulation mutation through `EventBus`, for example `MonsterDefeatedEvent`, `SoulBoundEvent`, `SoulSummonedEvent`, `SoulDevouredEvent`, and `WorldObjectDestroyedEvent`.

### Snapshot / View DTO

Immutable Application projections for Presentation: `GameSnapshot`, `PlayerSnapshot`, `MonsterSnapshot`, `OwnedSoulSnapshot`, `SoulBannerSnapshot`, `SoulRuntimeView`, `SoulLinkView`, and related records. They never own the underlying mutable state.

### Save DTO

Versioned serialization records under `src/Application/Persistence`: `GameSaveData`, `PlayerSaveData`, `OwnedSoulSaveData`, `SoulBannerSaveData`, `ProgressionSaveData`, `SoulRuntimeSaveData`, `PossessionRuntimeSaveData`, and `WorldSaveData`. `GameSaveCodec` handles schema versioning and legacy aliases.

### UID / runtime entity ID

Gameplay instance identity generated by `Core.Ids.UidGenerator` (for example `monster_2`, `ally_3`, or a Soul/Banner ID). UIDs are runtime state and save references, not authored definition IDs.

### Godot `.uid`

A Godot C# script sidecar such as `Arena.cs.uid`. It identifies the editor resource/import, not a gameplay entity, and must not be used in save data or Simulation logic.

### Asset/profile IDs

Stable logical IDs in `data/asset-manifest.json` and the JSON definitions. `AssetDefinitions.Get` resolves asset metadata; animation and Soul Nature/profile definitions reference IDs rather than persisting `res://` paths. `PossessionProfileDefinition`, `ModifierProfileDefinition`, and `SoulCostProfileDefinition` are authored profile records.

## Naming conventions

- `*Definition`: immutable authored content.
- `*State`: mutable runtime record.
- `*Rules`: pure deterministic calculation with no session state.
- `*System`: owner/coordinator of mutable gameplay state.
- `*Registry`: validated definition index; do not use it as an entity store.
- `*Command`: reserved for a future explicit command DTO; current requests are Application methods.
- `*Query`: reserved for explicit query DTOs; current read APIs are Application methods.
- `*Snapshot` / `*View`: immutable boundary projection.
- `*SaveDto` (and the current `*SaveData` records): versioned persistence projection under `Application/Persistence`.
