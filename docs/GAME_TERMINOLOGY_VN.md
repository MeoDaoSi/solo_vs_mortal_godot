# Solo vs Mortal — Thuật ngữ gameplay

Tài liệu này giải thích các thuật ngữ gameplay bằng tiếng Việt và ánh xạ chúng tới type, property, ID thật trong codebase Godot C#. Tên type/property/ID giữ nguyên tiếng Anh để có thể tìm trực tiếp trong mã nguồn.

## Thực thể và tiến trình

### Player (Nhân vật)

Nhân vật do người chơi điều khiển. `PlayerSystem` sở hữu instance mutable `PlayerState`.

Các field chính: `Uid`, `Position`, `CurrentHp`, `MaxHp`, `Level`, `Xp`, `Rank`, `Stats`, `AttackCooldown`.

`PlayerSnapshot` là projection chỉ đọc được Application cung cấp cho Presentation.

### Monster (Quái vật)

Instance kẻ địch được tạo từ `MonsterDefinition`. `MonsterSystem` sở hữu `MonsterState`.

Các field chính: `Uid`, `DefinitionId`, `SpeciesId`, `Rank`, `Level`, `CurrentHp`, `Position`, `AiState`, `AttackCooldown`.

`MonsterSnapshot` là view bất biến dùng để render.

### Soul (Linh Hồn)

Tài nguyên có thể thu thập sau khi Monster bị đánh bại. Pickup trên map là `WorldSoulState`; sau khi thu thập, record lâu dài là `OwnedSoulState`. Cả hai đều do `SoulSystem` sở hữu.

Soul giữ `SoulNatureId` và `SoulOrigin`; đây không phải là Ally đang được triệu hồi.

### Owned Soul (Linh Hồn Sở Hữu)

Record Soul lâu dài của người chơi. `OwnedSoulState` gồm `Id`, `SoulNatureId`, `Origin`, `Level`, `Xp`.

Dữ liệu này thuộc `SoulSystem`, không thuộc node Godot hay Ally tạm thời.

### Soul Origin (Nguồn Gốc Linh Hồn)

Thông tin nguồn gốc được chụp lại khi Soul được tạo. `SoulOrigin` lưu `MonsterUid`, `MonsterDefinitionId`, `SpeciesId`, `DisplayName`, `Rank`, `RankKey`, `RankDisplayName`.

`Rank` trong origin là dữ liệu lịch sử của Soul, không phải reference mutable tới Monster.

### Rank (Cấp Bậc)

Cấp chiến đấu dùng chung cho Player, Monster và stat được tạo từ Soul.

`CombatPowerRules` cung cấp `RankStartLevel`, `RankEndLevel`, `GlobalLevelToRank`, `RankKey`, `RankDisplayName`.

### Level (Cấp Độ)

Level hiện tại của `PlayerState`, `MonsterState` hoặc `OwnedSoulState`.

Player dùng level toàn cục. Khoảng level của Monster nằm trong `MonsterDefinition.LevelRange`; số level trên mỗi rank nằm trong `BalanceDefinition.LevelsPerRank`.

Hiện không có một kiểu rank-local level riêng cho Soul; Soul dùng `OwnedSoulState.Level` và origin rank đã lưu.

### `powerTier` (Tầng Sức Mạnh)

Tier authored dùng để chọn scaling stat và sprite stage. Trong C# nó là `BalanceDefinition.SpeciesById(...).PowerTier`, với enum `TierId` gồm `Tier1`, `Tier2`, `Tier3`.

Tên cũ `potential` không được sử dụng.

### Combat Power (Lực Chiến)

Giá trị nền để tính combat stats. `CombatPowerRules.GetCp` áp dụng base value, rank multiplier và level growth từ `BalanceDefinition`.

Sau đó `CombatPowerRules.GetStatBlock` áp dụng archetype, species và các modifier đang hoạt động.

### HP (Sinh Mệnh)

Điểm sinh mệnh. `PlayerState`, `MonsterState` và `AllyState` có `CurrentHp` và `MaxHp`.

`Stats.Hp` là thành phần HP tối đa sau khi tính stat. Damage và death do `CombatSystem` cùng actor system liên quan xử lý.

### ATK, DEF, SPD / Speed (Công Kích, Phòng Ngự, Tốc Độ)

Các stat chính trong `StatBlock`: `Atk`, `Def`, `Speed`, cùng `DefRaw` và `SpeedRaw`.

`CombatPowerRules.GetMovementSpeed` và `GetAttackCooldown` chuyển Speed thành nhịp di chuyển/tấn công runtime. Presentation chỉ hiển thị snapshot.

## Hồn Phiên và vòng đời Soul

### Soul Banner (Hồn Phiên)

Container loadout dùng để tổ chức các Soul được trang bị.

`SoulBannerSystem` sở hữu mutable `SoulBannerState`; `SoulBannerDefinition` chứa dữ liệu authored bất biến.

Các field state gồm `Id`, `DefinitionId`, `Tier`, `Level`, `Computed`, `BoundSoulIds`. Soul thực tế vẫn thuộc `SoulSystem`.

### Soul Banner Slot (Ô Hồn Phiên)

Một vị trí binding trong `SoulBannerState.BoundSoulIds`.

`SoulBannerRules.Compute` tính `Computed.SlotLimit`; `SoulBannerSystem.Bind` kiểm tra slot và trả về `BindSoulResult.SlotIndex`.

### Soul Capacity / Soul Cost (Sức Chứa / Chi Phí Linh Hồn)

`SoulBannerState.Computed.CapacityLimit` là tổng sức chứa của banner. `SoulBannerSystem.UsedCapacity` tính lượng đã dùng.

Chi phí từng Soul được `SoulSystem.Cost` lấy từ `SoulNatureDefinition.SoulCostProfileId`, rồi resolve tới `SoulCostProfileDefinition.Cost`.

### Active Summon Limit (Giới Hạn Triệu Hồn)

`SoulBannerState.Computed.ActiveLimit` giới hạn số Soul được triệu hồi đồng thời.

`SummonSystem` là owner kiểm tra giới hạn và trả `SummonFailure.ActiveLimitReached` nếu vượt quá.

### Summon / Summoned Soul (Triệu Hồn)

`SummonSystem` biến một `OwnedSoulState` đã bind và sẵn sàng thành `AllyState`.

Trạng thái được công bố qua `SoulRuntimeView` và `SummonResult`. Ally là entity mutable riêng do `AllySystem` sở hữu, liên kết ngược bằng `SourceSoulId`.

### Reserve Soul (Linh Hồn Dự Bị)

Soul đã sở hữu nhưng chưa được triệu hồi. Hiện không có collection reserve riêng; Soul được xem là dự bị khi `SoulRuntimeView.Status` là `Ready` và không có summon active.

### Soul Runtime State (Trạng Thái Linh Hồn)

Trạng thái vòng đời tạm thời do `SummonSystem.Runtime` tạo projection.

Enum `SoulRuntimeStatus` hiện có: `Ready`, `Summoned`, `Dispersed`, `Possessed`.

`SoulRuntimeView` còn có `SummonUid`, `RecoverySeconds`, `Stability`.

### READY (Sẵn Sàng)

Giá trị `SoulRuntimeStatus.Ready`. Soul có thể được summon, possess hoặc devour nếu các điều kiện Application tương ứng cho phép.

### SUMMONED (Đang Triệu Hồn)

Giá trị `SoulRuntimeStatus.Summoned`. `SoulRuntimeView.SummonUid` trỏ tới `AllyState` hiện tại.

### DISPERSED / Soul Dispersal (Hồn Tán)

Giá trị `SoulRuntimeStatus.Dispersed` trong thời gian hồi phục.

`SummonSystem` sở hữu recovery timer và lưu qua `DispersedSoulSaveData` / `SoulRuntimeSaveData`.

### POSSESSED (Đang Phụ Hồn)

Giá trị `SoulRuntimeStatus.Possessed` khi `PossessionSystem` đang dùng Soul đó.

Soul đang possessed không thể summon; nó cấp modifier và capability tạm thời thông qua `PossessionSystem`.

### Reinforcement (Viện Hồn)

Hiện chưa có type hoặc command `Reinforcement` riêng. Các flow hỗ trợ Soul đang được implement là binding qua `SoulBannerSystem` và lifecycle qua `SummonSystem`.

Trạng thái: Planned / Not implemented như một mechanic riêng.

## Các hệ thống Soul nâng cao

### Devour (Thôn Phệ)

Transaction tiêu thụ Soul một chiều do `DevourSystem` sở hữu.

`DevourMode` gồm `CultivationXp`, `Essence`, `Bloodline`. `DevourPreview` mô tả reward; `DevourResult` ghi nhận kết quả thực thi.

Soul đã bind hoặc chưa `Ready` sẽ bị từ chối.

### Devour XP / Cultivation path (Kinh Nghiệm Tu Luyện)

`DevourMode.CultivationXp` cộng XP cho Player qua `ProgressionSystem.AddPlayerXp`.

Công thức authored nằm trong `DevourXpProfileDefinition`: `BaseXp`, `XpPerSoulLevel`, `XpPerOriginRank`.

### Essence (Tinh Hoa)

Nhánh tiến trình Devour do `EssenceSystem` sở hữu theo profile ID của `ModifierProfileDefinition`.

Điểm và milestone modifier là mutable Simulation state; `EssenceSystem` cập nhật contribution `PlayerModifierSource.Essence`.

### Bloodline (Huyết Mạch)

Nhánh tiến trình song song do `BloodlineSystem` sở hữu.

Nó dùng `SoulNatureDefinitions.BloodlineProfiles`, giới hạn ở milestone cuối và đóng góp qua `PlayerModifierSource.Bloodline`.

### Possession (Phụ Hồn)

Flow tạm thời cho Player sử dụng một Soul, do `PossessionSystem` sở hữu.

`PossessionProfileDefinition` cung cấp `DurationSeconds`, `CooldownSeconds`, `Modifiers`, `CapabilityIds`; `PossessionSaveData` là projection lưu trữ.

Chưa có model strain hoặc compatibility riêng. Trạng thái: Planned / Not implemented.

### Capability (Năng Lực)

ID năng lực được cấp bởi một source đang hoạt động.

`CapabilitySystem` tổng hợp các ID từ `CapabilitySource.Possession` và trả lời qua `Has(capabilityId)`. Definition authored nằm trong `SoulNatureDefinitions.Capabilities`.

### World Interaction (Tương Tác Thế Giới)

Boundary tương tác với object trên map do `WorldInteractionSystem` sở hữu.

`MapObjectDefinition` và `MapInteractionDefinition` mô tả object authored. System quản lý destroyed-object IDs, kiểm tra capability/radius và trả `WorldInteractionResult`.

## Kiến trúc và boundary

### Definition

Nội dung authored bất biến trong `src/Data/Definitions`, ví dụ `MonsterDefinition`, `SoulNatureDefinition`, `SoulBannerDefinition`, `MapDefinition`, `AssetDefinition`, `CharacterAnimationDefinition`.

Definition chứa stable ID và tuning; không chứa instance UID, HP hiện tại, timer hoặc reference tới Godot node.

### Runtime State

Record mutable của play session trong `src/Simulation/State` hoặc bên trong Simulation `*System`, ví dụ `PlayerState`, `MonsterState`, `OwnedSoulState`, `SoulBannerState`, `GameSessionState`.

Simulation là source of truth duy nhất cho gameplay state.

### Registry

Collection lookup bất biến đã validate. `AssetDefinitions`, `SoulNatureDefinitions` và `GameDefinitions` cung cấp các accessor như `Get`, `Monster`, `SoulBanner`, `Map`; chúng không phải entity store.

### Rules

Công thức deterministic, stateless trong `src/Simulation/Rules`: `CombatPowerRules`, `CombatRules`, `ProgressionRules`, `SoulBannerRules`, `SpriteStageRules`.

### System

Owner có state của một gameplay capability trong Simulation, ví dụ `PlayerSystem`, `MonsterSystem`, `SoulSystem`, `SoulBannerSystem`, `SummonSystem`, `PossessionSystem`, `WorldInteractionSystem`.

### Command

Yêu cầu mutation gameplay, hiện được expose bằng method ở Application như `GameApplication.BindSoul`, `DevourSoul`, `SummonSoul`, `StartPossession`, `Craft`, `Interact`.

Codebase hiện chưa có các class DTO `*Command` riêng.

### Query

Thao tác đọc từ Application như `Snapshot`, `Inventory`, `SoulLinks`, `SoulRuntime`, `WorldObjects` và các method lookup asset/animation.

Query trả về bản copy hoặc immutable view.

### Domain Event

Fact được phát ra sau khi Simulation mutation qua `EventBus`, ví dụ `MonsterDefeatedEvent`, `SoulBoundEvent`, `SoulSummonedEvent`, `SoulDevouredEvent`, `WorldObjectDestroyedEvent`.

### Snapshot / View DTO

Projection bất biến cho Presentation: `GameSnapshot`, `PlayerSnapshot`, `MonsterSnapshot`, `OwnedSoulSnapshot`, `SoulBannerSnapshot`, `SoulRuntimeView`, `SoulLinkView` và các record liên quan.

Các DTO này không sở hữu state mutable phía dưới.

### Save DTO

Record serialization có version trong `src/Application/Persistence`: `GameSaveData`, `PlayerSaveData`, `OwnedSoulSaveData`, `SoulBannerSaveData`, `ProgressionSaveData`, `SoulRuntimeSaveData`, `PossessionRuntimeSaveData`, `WorldSaveData`.

`GameSaveCodec` xử lý version schema và legacy aliases.

### UID / runtime entity ID

Identity của instance gameplay, tạo bởi `Core.Ids.UidGenerator`, ví dụ `monster_2`, `ally_3` hoặc ID của Soul/Banner.

UID thuộc runtime state/save reference, không phải authored definition ID.

### Godot `.uid`

Sidecar của script C# trong Godot, ví dụ `Arena.cs.uid`. Nó định danh resource/editor import, không phải gameplay entity và không được đưa vào save data hoặc Simulation logic.

### Asset/profile IDs

Stable logical ID trong `data/asset-manifest.json` và các JSON definition.

`AssetDefinitions.Get` resolve metadata asset; animation và Soul Nature reference ID thay vì lưu path `res://`.

Các profile authored gồm `PossessionProfileDefinition`, `ModifierProfileDefinition`, `SoulCostProfileDefinition`.

## Quy ước đặt tên

- `*Definition`: nội dung authored bất biến.
- `*State`: record runtime mutable.
- `*Rules`: công thức deterministic, không giữ session state.
- `*System`: owner/coordinator của gameplay state.
- `*Registry`: index definition đã validate, không phải entity store.
- `*Command`: dành cho command DTO rõ ràng nếu sau này cần; hiện request là Application method.
- `*Query`: dành cho query DTO rõ ràng; hiện read API là Application method.
- `*Snapshot` / `*View`: projection bất biến ở boundary.
- `*SaveDto` và các record `*SaveData` hiện tại: projection persistence có version trong `Application/Persistence`.
