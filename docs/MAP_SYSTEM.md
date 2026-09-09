# World Map và Map Content

> Tài liệu prototype lịch sử. Gameplay hiện tại theo authority V2.5 được chỉ mục tại `docs/README.md`; nội dung dưới đây không override canonical region data.

Tài liệu này mô tả kiến trúc lâu dài của hệ thống World Map trong Solo vs Mortal. World Map là một lớp điều hướng và metadata phía trên các map gameplay; nó không thay thế `MapDefinition` và không chứa runtime gameplay state.

## Mục tiêu và phạm vi

- `M` mở overlay World Map trong Godot Presentation.
- Mỗi region có tên, mô tả ngắn, story, biome, tọa độ hiển thị, khoảng level đề xuất và trạng thái playable/unavailable.
- Người chơi chọn region để xem chi tiết. Region available có thể gửi command travel; region unavailable chỉ hiển thị lý do/điều kiện đã author.
- V1 có Desert là starter và Volcano là map playable dùng lại Arena scene. Forest, Frozen Wastes và Dark Realm là future entries không có map content.
- UI dùng Godot Controls kết hợp asset UI Adventure CC0; gameplay state vẫn chỉ đi qua Application snapshots.

Hệ thống này không thêm quest graph, unlock condition engine, fast-travel network hay gameplay mới. `travelConditionIds` và evaluator chỉ là extension point; policy V1 dùng `available` authored flag.

## Ownership và dependency

```text
Presentation (WorldMapUI, Arena input/rendering)
        -> Application (queries/commands/view snapshots)
        -> Simulation (WorldMapSystem, GameSession, map runtime systems)
        -> Data (WorldMapDefinitions, MapDefinitions, JSON loaders)
```

- `Data/Definitions` chứa immutable authored content. `RegionDefinition` mô tả một region; `MapDefinition` mô tả map content, object, zone, spawn, exit và placement policy.
- `Simulation/Systems/WorldMapSystem` sở hữu `CurrentRegionId` và kiểm tra transition. `GameSession` điều phối transition giữa World Map, Player, World Interaction, Monster và Summon systems.
- `Application/GameApplication` là boundary duy nhất của Presentation: `WorldMapRegions()`, `RegionDetails(id)` là query; `TravelToRegion(id)` là command.
- `Presentation/WorldMapUI` chỉ sở hữu UI selection/visibility. Godot nodes không sở hữu HP, XP, inventory, monster, summon, destroyed-object hay region runtime state.

## Definition và runtime state

`RegionDefinition` là metadata bất biến trong `data/configs/worldMap.json`:

- `Id`, `DisplayName`, `ShortDescription`, `Story`, `Biome`, `Tags`;
- `MapContentId`, tùy chọn `MapDefinitionFile`, `ScenePath`;
- `WorldMapPosition`, `WorldMapRadius`;
- `StarterCandidate`, `Available`, `RecommendedLevelRange`, `DefaultSpawnId`;
- `TravelConditionIds` (IDs cho evaluator tương lai, không phải mutable state).

`MapDefinition` là content bất biến của một map: kích thước, tile/object layers, terrain/object assets, zones, placement rules, object scale defaults, blockers, spawn points và exits.

Runtime state không nằm trong JSON definition:

- Region hiện tại thuộc `WorldMapSystem.CurrentRegionId`.
- Player position/colliders thuộc `PlayerSystem`; monster/ally thuộc các owner tương ứng.
- Destroyed world objects được `WorldInteractionSystem` lưu theo map ID; summon active được reset khi đổi map nhưng Soul sở hữu và progression vẫn tồn tại.
- Save projection hiện lưu thêm `world.currentRegionId` (optional để tương thích save cũ); khi restore, Simulation thử chuyển về region đó trước khi khôi phục destroyed objects.

## World Map flow

1. `GameDefinitionLoader` load và validate `worldMap.json`, sau đó load mọi map có `mapDefinitionFile`; `mapContentId` phải khớp `MapDefinition.Id`.
2. `GameSession` khởi tạo `WorldMapSystem` tại `starterRegionId` và spawn Player tại `defaultSpawnId` của region.
3. `Arena` bắt phím `M`, mở `WorldMapUI` và tạm dừng gửi input/tick gameplay trong lúc overlay mở.
4. UI gọi `GameApplication.WorldMapRegions()` để lấy snapshot. Region hiện tại được đánh dấu; available/unavailable được thể hiện bằng màu; click region hiển thị story/details.
5. Nút travel gọi `GameApplication.TravelToRegion(id)`. `WorldMapSystem.PreviewTravel` kiểm tra region tồn tại, availability, condition evaluator, map content và entry spawn trước khi đổi current region.
6. Khi command thành công, `GameSession` đổi map cho `WorldInteractionSystem`, xóa monster và summon active của map cũ, cập nhật colliders và đưa Player tới entry spawn. `Arena` tái tạo map textures/visual representatives rồi đóng overlay.

Transition thất bại không đổi `CurrentRegionId` và trả `RegionTravelFailure` để Presentation hiển thị feedback.

## Map zones và placement rules

Mỗi map có thể khai báo `zones` với bounds và danh sách `placementRuleIds`. Rule là metadata kiểm tra/authoring, gồm `category`, `minimumSpacing` và `spawnClearance`. Loader hiện thực hiện các invariant nền:

- zone, object collision và exit trigger phải nằm trong map bounds;
- object phải tham chiếu layer/asset hợp lệ và zone (nếu có) phải tồn tại;
- object được gán zone phải nằm trong bounds của zone;
- spawn không nằm trong `spawnSafetyRadius` hoặc `spawnClearance` của blocker;
- blocker collision không được chồng lấn; blocker được gán cùng zone còn được kiểm tra `minimumSpacing` của zone rule.

Các rule chưa phải một runtime spawn engine. Khi thêm content procedural hoặc editor tooling, dùng rule IDs này thay vì hard-code theo tên map.

Desert đã được dọn theo các zone `entry`, `combat`, `ruins`, `oasis`, `cliff-edge`; các spawn `default`, `fromForest`, `fromVillage` đã được đặt lại để vượt qua spawn-safety validation. Những thay đổi này chỉ làm sạch placement/collision, không tạo asset mới.

## Cách thêm một region mới

1. Thêm object vào `data/configs/worldMap.json` với ID kebab-case, metadata story/biome/coords, level range và `defaultSpawnId`.
2. Nếu playable, cung cấp `mapDefinitionFile` an toàn tương đối dưới `data/configs/` và `scenePath`; map `id` phải bằng `mapContentId` và spawn ID phải tồn tại. Nếu chưa có content, để cả hai là `null` và đặt `available: false`.
3. Khai báo zones/rules trong map JSON khi map có placement constraints; đặt blockers, collision, exits và spawns rồi chạy loader và kiểm tra thủ công trong Godot để bắt lỗi bounds/overlap.
4. Không thêm switch `if (regionId == ...)` vào Presentation hoặc Simulation. Logic chung đi qua `WorldMapSystem`, còn khác biệt content nằm trong definitions.
5. Nếu region cần unlock gameplay, triển khai một `IRegionTravelConditionEvaluator` dựa trên query Simulation/Application phù hợp; không nhét cờ unlock mutable vào `RegionDefinition`.

## Validation

- Các kiểm tra parity prototype đã được gỡ theo chính sách manual testing V2.5; hãy dùng Godot playtest theo checklist trong `docs/V2.5/source-audit.md`.
- `dotnet build solo_vs_mortal_godot.sln -c Debug --no-restore` kiểm tra C# project.
- Godot headless startup kiểm tra scene/Arena và managed assembly load. Các lỗi `user://logs`/save trong môi trường headless không thuộc map runtime; cần writable user data khi chạy desktop bình thường.
