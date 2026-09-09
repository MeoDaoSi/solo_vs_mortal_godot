# Báo cáo tiến độ triển khai V2.5 — kế hoạch 8 phần

Ngày cập nhật: 09/09/2026. Người rà soát: agent chính, trực tiếp đối chiếu source.

- Source: `C:/ws/solo_vs_mortal_godot`.
- Assets/workflow: `C:/ws/asset-production_system`.
- Đặc tả có thẩm quyền: `Solo_vs_Mortal_Gameplay_System_V2.5.md` và `game_spec/`, revision `2026-09-08.closed-1`, content `svm-content-2.5.1`, balance `svm-balance-2.5.1`.
- Báo cáo này là trạng thái triển khai và danh sách việc tiếp theo; không thay thế hoặc định nghĩa lại gameplay.
- Phiên xuất báo cáo chỉ đọc source và viết tài liệu. Không áp dụng lại các bản sửa còn treo, không chạy gameplay test, không xóa assets.

## 1. Cách đọc trạng thái

**complete**: phạm vi của phần đã hoàn tất, không còn yêu cầu triển khai chưa xử lý được biết đến. **in_progress**: đã có code nhưng còn công việc cụ thể bên dưới. **not_complete**: chưa đủ sản phẩm đầu ra hoặc chưa triển khai đầy đủ pipeline.

Build gần nhất ở phiên triển khai trước: `dotnet build solo_vs_mortal_godot.csproj --no-restore`, thành công, 0 lỗi và 0 cảnh báo; `git diff --check` không báo lỗi whitespace, còn cảnh báo chuẩn hóa newline của Git. Đây không phải kết quả nghiệm thu gameplay. Engine 4.5.2 Compatibility chưa được xác minh; project hiện dùng Godot.NET 4.7.2 và khai báo 4.7 Forward Plus.

Không đánh dấu cả phần complete chỉ vì có class, có API, subagent báo xong hoặc build thành công. User là người kiểm thử gameplay duy nhất. Không tạo/chạy automated tests, harness, parity runner, soak hay headless gameplay smoke.

Số phần dưới đây là kế hoạch 8 phần đã giao trong hội thoại. Trường `part` trong một số work item của audit ban đầu dùng cách nhóm khác; không lấy số đó để tự đổi thứ tự kế hoạch. `implementation-tasks-4-8.json` còn trạng thái pending và evidence cũ; phải cập nhật sau khi đối chiếu, không dùng nguyên trạng làm bằng chứng tiến độ.

| Phần | Phạm vi | Status |
|---|---|---|
| 1 | Audit ban đầu và lập kế hoạch migration | complete |
| 2 | Nền tảng authority, loader, cấu trúc và tài liệu | in_progress |
| 3 | Simulation/combat/player/AI nền tảng | in_progress |
| 4 | Soul, Density, Sync, Banner, Summon, Spirit, Possession, capability | in_progress |
| 5 | Inventory, skills, mastery, quests, loot, world, save/migration | in_progress |
| 6 | Adapter assets và dọn code/assets/docs cũ | not_complete |
| 7 | Hoàn thiện bộ Slice assets và export | not_complete |
| 8 | Tích hợp Godot, đóng gói, bàn giao và nghiệm thu | not_complete |

## 2. Phần 1 — Audit ban đầu

Status: **complete**.

## 3. Phần 2 — Nền tảng và tài liệu

Status: **in_progress**.

Đã có loader typed V2.5, bundle được khóa hash, lớp Core/Data/Simulation/Application/Presentation và index authority. Tài liệu cũ đã được nhận diện là historical; việc gom và loại bỏ định nghĩa gây nhầm lẫn chưa hoàn tất.

Các bước tiếp theo:

1. Đối chiếu bản spec-lock trong source với bundle có thẩm quyền. Lập bảng file nào là dữ liệu gameplay, file nào chỉ còn dùng cho compatibility/presentation. Không sửa hash để hợp thức hóa dữ liệu khác spec.
2. Rà `GameDefinitionLoader`, `WithCanonicalRoster`, đường bootstrap Main/Arena và restore. Xác nhận V2.5 dùng roster/profile canonical; metadata lấy từ template cũ không được làm loài mới thừa hưởng nature, art hoặc hành vi không có trong spec.
3. Tách metadata compatibility cần giữ thành adapter có tên rõ ràng. Chốt các trường có thể bỏ khi hoàn thành phần 6, tránh hai loader cùng quyết định gameplay.
4. Gom tài liệu hướng dẫn hiện hành về `docs/V2.5/`; đưa ARCHITECTURE/MAP_SYSTEM/GAME_TERMINOLOGY/GAME_TERMINOLOGY_VN/PERFORMANCE_BUDGET cũ vào vùng historical. Cập nhật link ở `docs/README.md`, AGENTS và các tài liệu có liên quan. Giữ license/tài liệu vendor ở đúng nơi.
5. Cập nhật work-items và evidence theo source hiện tại. Mỗi yêu cầu chỉ một dòng trạng thái hiện hành; ghi file/symbol thực hiện và mục còn thiếu. Giữ audit ban đầu dưới dạng lịch sử có ngày, không để bảng “missing” cũ bị hiểu là hiện trạng.
6. Giải quyết baseline engine: chuẩn bị Godot .NET 4.5.2 Compatibility cùng export templates tương ứng; đối chiếu API/SDK/project/export trước khi chuyển cấu hình. Nếu baseline không khả thi, báo rõ để user quyết định thay đổi; không tự coi 4.7.2 tương đương.
7. Build/export bằng toolchain đã chốt. Ghi phiên bản thực tế và kết quả; không chạy gameplay thay user.

**Điều kiện complete:** chỉ còn một authority gameplay hiện hành; bootstrap/loader không quyết định khác nhau; link tài liệu hợp lệ; baseline engine được giải quyết và có bằng chứng compile/package phù hợp.

## 4. Phần 3 — Simulation, combat, player và AI

Status: **in_progress**.

Đã có fixed tick, RNG PCG32, combat coordinator, Player Spirit/dodge, damage/status/shield, actor AI và runtime snapshot. Chưa được phép suy ra toàn bộ acceptance đã đạt từ build.

Các bước tiếp theo:

1. Trace thứ tự một fixed tick: input → accept/cost/cooldown → movement → release/hit → lethal/reward → recall/death → save boundary. Đối chiếu các tình huống cùng tick: boss và Player cùng chết, recall trùng lethal, cancel trước/sau release.
2. Rà mọi nguồn damage/heal/shield: hệ số rank/encounter áp đúng một lần; modifiers từ gear/passive/possession được sử dụng tại đúng thời điểm; không dùng animation để phát damage.
3. Rà grant khi cast và khi release: learned slot sai vũ khí phải bị từ chối; end possession hủy cast chưa release nhưng giữ projectile đã release và offense snapshot. Không reset shared cooldown khi đổi nguồn cấp cùng skill.
4. Rà movement/dodge/knockback/AI quanh collider, gap, water và gate; kiểm tra bán kính từng actor, placement và pathfinding. Hoàn thành các hạng mục world W03–W06 ở mục 10 của báo cáo.
5. Rà target UID, home position, leash, boss story instance và mastery budget khi actor chết, recall, rời vùng và tải lại. Không tạo UID mới cho một life vẫn tồn tại.
6. Đối chiếu 18 acceptance cases với file/symbol và chuỗi thao tác manual thực tế. Thiếu producer hoặc chỉ có API chưa có caller thì trả lại phần sở hữu để sửa trước khi đóng phần 3.
7. Build sau bản sửa cuối; bàn giao checklist cho user chạy. Ghi tách trạng thái code/build và kết quả user đã xác nhận.

**Điều kiện complete:** các nhánh combat thuộc phạm vi phần 3 đã nối runtime, không còn lỗi logic đã biết, persistence tương ứng đã qua review; checklist user có thể thực hiện mà không cần debug award hoặc giả lập completion.

## 5. Phần 4 — Vòng lặp Soul

Status: **in_progress**.

Đã triển khai các hệ thống chính và nối vào Application/HUD. Phiên sửa trực tiếp đã xử lý tốc độ Spirit theo phút/tick, khóa thao tác lúc suspend, lưu trước thông báo đột phá, loadout duplicate và passive. Những thay đổi này chưa đủ để đóng toàn bộ 12 task phần 4.

Các bước tiếp theo, theo nhóm task gốc:

1. **4.01–4.02 Density/capture:** đối chiếu immutable awards, proof-only, pending/discard, consumed pickup, pity và WorldSoul region. Rà invalid capture không consume/RNG; retry cùng transaction không cấp lại reward. Hoàn thành P03/P05/P06 trước khi đóng capture/save.
2. **4.03–4.04 facts/breakthrough/Banner:** trace boss thật → fact provenance → điều kiện nâng rank → receipt → durable save → UI. Kiểm tra cap Beta/Full; lệnh sai shrine/rank/density không thay state.
3. **4.05 Sync:** liệt kê đủ 8 nguồn cho từng species và caller thực tế. Rà quest/landmark/kill/secret tới source đúng; fact có trước ownership vẫn claim đúng một lần. Kiểm tra nghi thức giữ E theo spec: đủ thời gian, đúng shrine, move/damage cancel, không nhận bằng lệnh tức thời. Nối producer còn thiếu, không chỉ dựa vào `ClaimAt == shrine`.
4. **4.06 placement:** rà nearest-free và đường đi thật với collider/gate; actor không mượn capability của Player. Không-space phải giữ nguyên Soul mode, Spirit và UID.
5. **4.07–4.09 summon/Spirit/Ally:** rà một Ally/species, SummonAll từng kết quả độc lập, recall giữ tỷ lệ HP/CD, chết sang Dispersed/recovery, focus/Guard/Assault/formation và path-fail. Đối chiếu carry Spirit mới với save cũ và đúng zero-crossing.
6. **4.10 Possession:** rà snapshot duration/CD/stats/milestones; thu hồi đúng SourceInstance; chuyển vùng kết thúc full cooldown; restore không tính lại snapshot từ Soul đã thay đổi.
7. **4.11 traversal:** hoàn thành audit anchor/grace/rescue và hazard ở W03–W06. Bảo đảm damage rescue nonlethal dùng CurrentHP, không chỉ MaxHP.
8. **4.12 tích hợp/obsolete:** quét caller thực tế để chặn Devour/Essence/Bloodline/pills/cost-slot cũ trên canonical path; phần code legacy còn cần cho migration phải được cô lập và ghi lý do giữ. Việc xóa file vật lý phối hợp phần 6, không báo đã xóa khi mới ẩn HUD.
9. Cập nhật evidence 4.01–4.12 thành bảng yêu cầu → symbol/caller → save field → manual case → phần chưa đạt. Không đóng cả nhóm khi còn “producer làm sau”.

**Điều kiện complete:** vòng kill → pickup → Density/Sync → Banner → Summon/recall/death → Possession → save/load hoạt động qua runtime thật, đủ nguồn tiến trình, không còn đường gameplay cũ trên canonical flow và không còn lỗi đã biết thuộc phần này.

## 6. Phần 5 — Inventory, skills, mastery, quests, world và persistence

Status: **in_progress**.

Đã có inventory/equipment, passive modifiers, UI quản lý, shop/learn gần NPC, overflow tại shrine, các system quest/mastery/loot/unique; world/profile được bổ sung đáng kể. Mục 5.06 và 5.08 chưa đóng.

1. **5.01 Inventory:** rà từng lệnh buy/sell/equip/unequip/potion/withdraw theo nguyên tắc validate trước mutate. Kiểm tra túi đầy, stack 99, overflow không giới hạn, 2-hand đẩy OffHand, accessory family, rank và dead/combat/hazard/transition. Xác minh NextInstance mới lưu được và save cũ không tạo item ID đang tồn tại; kiểm tra partial mutation trong các nhánh AddItemInternal thất bại.
2. **5.02 Skill/loadout:** xác minh duplicate/category/rank capacity; passive thực sự đổi stats; đổi vũ khí giữ slot ID nhưng vô hiệu hóa với lý do WrongWeapon. UI phải hiển thị lý do từ chối cụ thể, không chỉ một câu “chưa đủ điều kiện”. Rà modifier capacity/regen và restore maxima theo P02.
3. **5.03 Mastery:** trace từng metric từ combat event thật; HP-loss budget theo life; heal debt chỉ từ hostile damage; shield credit là absorbed; prevention/distance/stealth/meaningful-use không cấp từ animation. Rà thăng rank phải cập nhật mastery và grant cùng transaction, không có API phụ chỉ đổi một phía.
4. **5.04 Quests/NPC:** rà 41 definitions, prerequisite, progress tới trước activation, thứ tự tương tác An/Kha/Linh, tutorial receipt và claim tại shrine. Đối chiếu event name/target của group/elite/boss với definition; mỗi reward có receipt và lưu trước thông báo.
5. **5.05 Loot/unique:** rà 17 family selection theo RNG stream canonical và receipt chống reroll. Đối chiếu reward rương hiện dùng lựa chọn cố định với contract reward, cập nhật nếu không đúng; không tự đổi gameplay mà không căn cứ. Kiểm tra Chaos/Despair/Covenant, điều kiện và thời lượng ritual, UI/caller thật, source grant và cooldown.
6. **5.06 World:** thực hiện W01–W09 trong mục 10. Hoàn thành topology/collider/hazard/interaction/lifecycle/streaming/fog; không coi marker world hiện tại là tileset hoàn chỉnh.
7. **5.07 Boss:** rà A/B/C, vị trí snapshot khi windup, stagger/recovery/immunity, pursuit/adds; adds không reward và không gây lỗi restore khi được lưu ở dormant region. Rà story instance/budget giữ qua failed attempt, death, rest, travel.
8. **5.08 Persistence:** thực hiện P01–P10 trong mục 10; đóng beta→full, lịch sử award, UID/RNG, các timer và WAL trước khi cho phép cleanup phụ thuộc.
9. Sau khi sửa, build và cập nhật cùng các task 5.01–5.08. Chỉ ghi complete cho task đã xử lý hết tiêu chí; không ghi “xong phần 5, còn migration/world làm sau”.

**Điều kiện complete:** các task 5.01–5.08 và vấn đề 6 đều đóng ở mức implementation/review, không còn state bị bỏ khi save, không còn tương tác chỉ có API; gameplay acceptance vẫn ghi đúng kết quả do user cung cấp.

## 7. Phần 6 — Clean assets, code và docs cũ

Status: **not_complete**.

Hiện trạng đo từ filesystem khi xuất báo cáo:

| Khu vực | Số file | Dung lượng gần đúng | Ý nghĩa |
|---|---:|---:|---|
| assets/ | 5.018 | 200,13 MiB | Tổng file gồm import metadata; không phải tất cả được phép xóa |
| tests/ | 0 | 0 | Thư mục tests đã không còn |
| tiles/ | 256 | 0,21 MiB | Cần truy dependency trước khi phân loại |
| store_assets/ | 18 | 1,33 MiB | Chưa xác nhận là bỏ được |
| sample_maps/ | 4 | 0,04 MiB | Chưa xác nhận là bỏ được |
| sheets/ | 6 | 0,01 MiB | Chưa xác nhận là bỏ được |
| proof/ | 36 | 0,04 MiB | Phân biệt dữ liệu vendor/import với test trước khi dọn |

`DevourSystem.cs`, `EssenceSystem.cs`, `BloodlineSystem.cs` còn tồn tại. Các config/manifest và code presentation cũ vẫn được tham chiếu. Không có cơ sở báo cleanup hoàn tất hoặc coi toàn bộ 200 MiB là rác.

Các bước tiếp theo:

1. **6.01:** lập dependency inventory từ project.godot, autoload, scene, .tres/.res, script, asset manifest, animation path pattern, importer và export. Ghi từng file/group: chủ sở hữu, reference site, license, retain/replace/delete, replacement ID.
2. Khóa danh sách bảo vệ: 4 PNG Skeleton đã cho tái sử dụng, raw/master/normalized của Art, export có provenance, file user đang chỉnh và license còn dùng. Không xóa theo wildcard root.
3. **6.02:** tạo canonical asset adapter đọc AssetId, file/hash, frame rect, durations ms, pivot, clip/direction/rank, sockets/layering; map riêng static Skeleton, không giả thành animation hoặc đủ mọi rank.
4. Chuyển caller runtime sang adapter: Player/Monster/Ally/pickup/weapon/world/UI. Missing ID phải có danh sách và marker xác định; không âm thầm lấy Skeleton hay art cũ cho loài khác.
5. Sửa canvas/camera theo style lock 640×360, nearest/integer scale và pixel snap; bỏ scale hardcode gây sai size. Đồng bộ animation từ simulation và durations thực, không tính damage từ frame.
6. **6.03:** sau khi replacement có thật và caller đã chuyển, xóa từng nhóm obsolete thuộc quyền dự án; cập nhật import metadata và references cùng lượt. Giữ dependency vendor/legal đang dùng.
7. Quét lại các tên/ID/cơ chế cũ trong runtime, build config và export; xử lý import orphan, path động và sample dependency. Xác minh tests/probes không còn references; không tạo harness thay thế.
8. Hoàn thành gom MD ở phần 2, build, ghi inventory sau cleanup: số file giữ/xóa/thay và lý do còn giữ từng nhóm legacy.

**Điều kiện complete:** mọi file xóa đều có dependency đã giải quyết; game dùng adapter canonical; không còn caller obsolete ngoài compatibility được ghi rõ; build/package không missing reference; assets được bảo vệ còn nguyên.

## 8. Phần 7 — Slice assets

Status: **not_complete**.

Snapshot catalog `updatedAt = 2026-09-09T04:51:10.917023+00:00`:

| Scope | Tổng logical asset | generated | needs_rework | not_generated |
|---|---:|---:|---:|---:|
| Full 01 | 10.700 | 200 | 4 | 10.496 |
| Beta 01 | 1.271 | 200 | 4 | 1.067 |
| Slice 01 | 161 | 101 | 4 | 56 |

Catalog generated chỉ nói có output theo workflow, không chứng minh đã user approve, đủ clip, đúng style, export hay chạy trong game. Kế hoạch cũ ghi 158 Slice ID, catalog hiện 161; phải reconcile trước khi tuyên bố đủ bộ. Export `beta-slice-v002` có 1 entry và `complete=false`; `skeleton-static-integration-v001` có 4 entry và `complete=false`.

Các bước tiếp theo:

1. **7.01:** đối chiếu scope Slice 158/161, slice-parts và catalog; chốt một danh sách có version, giải thích 3 entry chênh lệch, phân biệt reuse/metadata/code-rendered/raster. Không tự tăng/giảm scope để làm đủ phần trăm.
2. Đối chiếu từng logical ID với file thật, hash, manifest và quyết định user cho đúng phiên bản. Không đếm output cần làm lại hoặc được approve ở hash cũ là approved hiện tại.
3. Lập thứ tự các microtask còn thiếu theo dependency. Snapshot hiện thiếu 16 player và 40 world ID; xác minh lại trước khi dispatch. Gom thành đợt làm tự động, không yêu cầu user prompt từng microtask.
4. Khi thực thi generation, Art làm đúng phạm vi từng đợt; dừng và báo issue nếu bị tool/quota block. Không tự quyết định asset đẹp/xấu, không tự approve mỹ thuật.
5. Sửa 4 mục needs_rework theo feedback gắn với bản ảnh tương ứng; mọi output mới trở lại trạng thái chờ user review. Giữ lịch sử version/hash.
6. Kiểm tra kỹ thuật: frame count/duration, cell bounds/gutter không bleed, pivot ổn định, alpha/palette/nearest và sockets đủ clip/direction. Preview phải chạy theo durations ms và cho xem frame-by-frame để user xác nhận chuyển động.
7. **7.02:** assemble export version mới từ file thật. Manifest ghi rõ static fallback và phần excluded; không lấy raw contact sheet làm atlas. Bản package không ghi complete nếu còn required ID thiếu.
8. Bàn giao textures + manifest/asset-map + frame/socket metadata + loader/resources cần thiết + IMPORT + provenance/license cần dùng. Raw/master/dashboard thuộc production, không tự đưa cả thư mục đó vào game.

**Điều kiện complete:** scope Slice thống nhất; đủ file kỹ thuật và metadata; các ảnh cần mỹ thuật được user quyết định; export tự chứa dependencies, không còn required ID thiếu hoặc rework chưa xử lý.

## 9. Phần 8 — Tích hợp, build và bàn giao

Status: **not_complete**.

1. **8.01:** import bản export đã chốt vào vùng assets canonical của source; ghi mapping version/hash. Kiểm tra toàn bộ required ID, tránh atlas/font/icon/vendor ngoài package bị thiếu khi chạy máy khác.
2. Nối Player/Ally/Monster idle/move/attack/hit/death và summon/recall theo state simulation; weapon sockets đúng hand/frame/direction, layer trước/sau và Y-sort. Không dùng duration animation làm thời gian damage.
3. Tạo scene xem Slice phục vụ user kiểm tra size, hướng, pivot, timing, vũ khí, Soul và terrain. Scene preview không tạo fake reward/quest completion trên save chơi thật.
4. **8.02:** build và export bằng baseline đã chốt ở phần 2; ghi engine/templates/config; xác minh file package và hướng dẫn mở/chạy. Không tự chạy gameplay tests thay user.
5. Bàn giao checklist 18 canonical acceptance cases cùng kiểm tra hình ảnh trong game; tách bug chức năng, lỗi kỹ thuật asset và quyết định mỹ thuật của user.
6. Sửa mọi lỗi user báo trong phạm vi tương ứng, review lại chỉ phần bị ảnh hưởng và build. Không đóng phần 8 khi còn issue đã biết chỉ được ghi ở mục hạn chế.
7. **8.03:** cập nhật báo cáo tổng 8 phần bằng kết quả thực, link build/manifest/hướng dẫn. Chỉ cập nhật complete sau khi đủ đầu ra và tiêu chí; không dùng việc xuất báo cáo này làm bằng chứng kế hoạch đã xong.

**Điều kiện complete:** có package thật, assets được map đầy đủ theo scope đã chốt, hướng dẫn và checklist dùng được; các issue thuộc phạm vi đã xử lý, kết quả user nghiệm thu được ghi rõ.

## 10. Vấn đề 6 còn mở — rà soát persistence và world

Status: **in_progress; chưa đóng**. Đây là vấn đề thứ 6 của review trước, KHÔNG đồng nghĩa phần 6 cleanup của kế hoạch.

### 10.1. Những gì đã có và điểm chưa được ghi

Đã có: ActiveProfileId và bootstrap roster 19 species; staged restore/upgrade Beta→Full; world objects/roads/terrain từ blueprint; tương tác NPC/chest/landmark/secret/portal; fog visited tiles; hazard clock; dormant-region monsters; pickup→region map; Item NextInstance; kiểm tra derived maxima sau khi restore modifiers; optional save fields có JsonIgnore để không tự chèn null/default vào checksum cũ.

Bản sửa cuối bị quota chặn CHƯA áp dụng:

- `RestoreDormantRegions` chưa nhận tập activeActorIds để phát hiện UID trùng giữa vùng hoạt động và vùng đã rời.
- Chưa bổ sung đầy đủ kiểm tra encounter/species/level/home bounds và quan hệ allocator với toàn bộ actor ID như bản sửa dự kiến.
- Chưa nối EnvironmentSpeedMultiplier để Ally chịu FrostFloor độc lập với Player.

Không copy lại nguyên lệnh cũ một cách mù quáng. Đọc source mới, xác định delta còn thiếu rồi áp dụng từng thay đổi.

### 10.2. Quy trình audit → quyết định update → đóng mục

1. Tạo bảng field/state có các cột: owner, producer, capture, serialize/checksum, validate, restore, liên kết UID/region/source, quy tắc timer và xử lý phiên bản cũ.
2. Với từng mục P/W dưới đây, kết luận một trong ba loại: **đã có và đúng** (không sửa); **thiếu/sai đã chứng minh** (sửa ngay đúng owner); **thiếu dữ liệu để xác nhận** (tiếp tục đọc spec/caller; ghi chính xác case cần user kiểm thử nếu chỉ runtime mới xác minh được).
3. Update khi có mismatch cụ thể giữa spec và code, state có producer nhưng không lưu, restore tạo mặc định làm mất tiến trình, hoặc validation cho phép identity không hợp lệ. Không update bằng cách nới validation chung để “load được”.
4. Bản sửa phải gồm producer, capture, validation, restore, UI/command liên quan và tài liệu migration nếu cần. Nếu sửa save contract, chứng minh cách giữ checksum/version cũ và cách reject bảo toàn file khi không thể migrate chính xác.
5. Review lại đường gọi từ UI tới commit. Build; không tạo automated tests. Giao các case gameplay/disk-failure thực tế cho user theo checklist, không tự thao tác phá save thật.
6. Chỉ đổi từng mục sang complete khi đủ evidence và không còn phần phụ thuộc bị bỏ. Nếu P/W chưa đóng thì vấn đề 6 và 5.06/5.08 vẫn in_progress; không chuyển sang cleanup có dependency.

### 10.3. P — các bước rà soát persistence

| ID | Thực hiện tiếp theo | Tiêu chí cập nhật/đóng |
|---|---|---|
| P01 | Lập inventory toàn bộ state, đặc biệt HazardTicks, DormantRegions, PickupRegions, VisitedTiles, NextInstance, Spirit carry, cast/projectile/CD, boss/adds, mastery budgets/debts và selection. Tìm field chỉ tồn tại trong RAM hoặc capture nhưng không restore. | Không bỏ sót state làm thay kết quả khi reload; field transient phải có lý do không persist. |
| P02 | Rà thứ tự restore Player → equipment/passive/possession → recompute → current resources. So cả HP/Spirit/maxima/weapon style; phân biệt save cũ chưa áp passive với save sai. | Không refill hoặc clamp mất resource vì thứ tự tạm thời; chỉ chấp nhận migration cũ có công thức xác định, save mới vẫn validate chặt. |
| P03 | Rà checksum cũ/mới: JsonIgnore của field optional, ordering của dictionary, normalization và deserialize. Không sửa file user để thử. Dùng đọc source/file save mẫu được phép, ghi manual case nếu cần. | Save đúng phiên bản cũ vẫn được kiểm checksum theo representation hợp lệ; checksum sai bị từ chối, nguyên bản không bị ghi đè. |
| P04 | Bổ sung kiểm tra UID duy nhất trên Player/active/dormant actor; allocator ≥ suffix ID lớn nhất; item NextInstance không tái cấp ID đang tồn tại. Rà source/cast/projectile/target/cooldown links. | Restore lỗi chỉ hủy staged session, không đổi session sống/UID/RNG. ID không trùng và reference không treo. |
| P05 | Validate dormant rows với region/encounter/species/level/type/rewardEligible/home/bounds; xử lý boss adds đúng contract thay vì bắt mọi actor phải là encounter tĩnh. Rà TargetUid, IsReturning, CD, status/shield expiry khi park/resume. | Quay lại giữ đúng life và budget; không respawn/reset HP tùy tiện, không reject save hợp lệ có adds; state giả mạo bị từ chối. |
| P06 | Rà pickup map: mỗi pickup chưa consume có đúng region; không duplicate giữa active/dormant, không unknown region/key. Rà capture/auto-collect chỉ ở region hiện tại. | Hồn không nhảy map, mất hoặc được consume hai lần; save cũ thiếu region có migration xác định hoặc explicit reject giữ file. |
| P07 | Rà từng timer khi save/suspend/restore/travel: Spirit denominator/carry, potion CD, possession, recovery, dodge, status, hazard500ms, crumble2s, grace, respawn. Đối chiếu timer dormant có pause hay tiến theo world tick. | Không đổi đơn vị, không chạy offline, không reset accumulator để né damage; không tự hồi đầy hoặc triệu hồi lại. |
| P08 | Rà Beta→Full trong staged session: giữ exact awards/receipts/milestones/proofs/pity/inventory/mastery/quest/world/RNG; chỉ bổ sung state rỗng cho content mới. Lưu trước nâng và commit bản sau nâng đúng slot. | Không re-award, re-roll, mất progress hoặc hạ cap ngầm; lỗi giữa chừng giữ original và không ghi nhầm slot. |
| P09 | Rà WAL/temp/flush/rename/backup/history/exact retry; UI success sau commit; mọi callback khi suspend/save-failed bị khóa. Kiểm tra snapshot chứa dictionary mutable không bị thay khi chờ retry. | Retry dùng cùng payload/hash/transaction; không success sớm; không recovery từ foreign slot. |
| P10 | Rà rebalance/removed content: immutable amount/source/version/milestone, không sum lại từ balance mới. Với version không hỗ trợ, giữ nguyên file; migration chỉ có explicit mapping/LegacyRelic đủ metadata. | Không giảm tiến trình hay xóa ownership. Phân biệt rõ “bảo toàn bằng reject” với “đã migrate hỗ trợ”; không tuyên bố rebalance migration hoàn chỉnh nếu mới reject. |

### 10.4. W — các bước rà soát world

| ID | Thực hiện tiếp theo | Tiêu chí cập nhật/đóng |
|---|---|---|
| W01 | Đối chiếu 9 region, 4 chunk, tile32/chunk128, tọa độ local/global của spawn/shrine/NPC/landmark/chest/elite/boss/entry/exit; bỏ clamp che sai authored position. Rà roster metadata và unknown ID. | Mọi object/encounter đúng blueprint; dữ liệu sai bị báo rõ trước sử dụng, không reposition/random fallback. |
| W02 | Rà road width4, outer wall2, portal opening, detour, safe camp radius8, encounter clearance6 và grid props. Xác minh tile/walkmesh/material thực tế, không chỉ đường vẽ debug. | Main path đi bộ liên tục; road/safe zone không hazard/prop blocker; không vẽ đường nhưng collision chặn. |
| W03 | Trace mọi movement entry point: walk, dodge, dash, knockback, AI chase/return, summon/rescue. Tách capability actor; rà circle collider thay vì chỉ tâm điểm và đường swept không bỏ qua terrain. | Không qua wall/gate/gap/water trái phép; Ally/Monster không hưởng capability Player; no-space không partial mutate. |
| W04 | Rà hazard rectangles, timer500ms, true environmental HP loss, Ward và reset khi rời; thêm FrostFloor cho Ally; rà CrumblingFloor/rescue từng actor thuộc scope. | Damage/slow đúng spec, không shield/crit/mastery; không stack sai; không reset timer bằng reload; không quên actor ngoài Player. |
| W05 | Rà safe anchor500ms, gate-state identity và capability expiry. Khi Player bước ra vùng, active terrain cập nhật đúng; anchor cũ không nằm trong hazard. | Grace đúng2s/3s; cancel unreleased; return hoặc rescue đúng route; HP loss nonlethal theo CurrentHP; không teleport qua gate chưa mở. |
| W06 | Rà E priority và khoảng cách48, NPC gates, shrine/rest/claim, chest reward/receipt, secret đúng species+Sync+capability. Hoàn thiện ritual hold/cancel thật, UI failure reason. | Không mua/học/claim từ xa; không mở lại reward; không auto-complete bằng API chưa đủ điều kiện; thao tác user reachable. |
| W07 | Rà region transition hai chiều: adjacency, đứng portal/entry, boss/rank gate, recall giữ vitality, end possession full CD, save boundary. Rà độ an toàn lúc mutation rồi commit thất bại. | Không nhảy tới region bất kỳ; không mất life/pickup/budget, không show success trước durable; entry/exit đúng hướng. |
| W08 | Rà encounter lifecycle/rest/death: RestReset chỉ hồi sinh eligible defeated theo spec, living life giữ UID; boss/adds và mastery debt/budget không reset sai. Hoàn thiện chunk preparation trong256units và không unload combat. | Không duplication/reward farming bằng travel/rest/load; streaming có implementation thực hoặc ghi rõ chưa làm, không suy từ region cache. |
| W09 | Rà fog reveal radius12tiles và persist từng region, dùng dữ liệu đó cho minimap/world theo spec. Rà marker missing assets và adapter phần6, xác minh không còn actor vô hình. | Reveal đúng, giữ khi load/travel, không lộ state trái contract; marker chỉ là fallback kỹ thuật, không được tính asset hoàn chỉnh. |

### 10.5. Thứ tự sửa sau audit và tiêu chí kết thúc vấn đề 6

1. Sửa P01–P04 trước: state inventory, resource restore, checksum/version và identity. Đây là lớp bảo vệ để các bước world sau không tạo save khó khôi phục.
2. Sửa P05–P07 cùng W03–W05: dormant/pickup/timers và movement/hazard/anchor. Hoàn thành bản sửa cuối bị quota chặn sau khi đối chiếu lại source.
3. Sửa W01–W02 và W06–W08: topology/interaction/travel/lifecycle/streaming; đồng bộ capture/restore mỗi khi thêm state mới, không để persistence cho “lượt sau”.
4. Đóng P08–P10 và W09: profile upgrade, WAL/rebalance, fog/presentation. Rà lại từ save có đầy đủ Soul/gear/quests/boss/world, không chỉ NewGame rỗng.
5. Build và kiểm dependency/format cho file vừa thay. Chuẩn bị manual sequence cho user: đang gear/passive → save/load; suspend và click HUD; mở rương rồi reload; rời/quay vùng; Soul rơi hai vùng; hazard/grace; upgrade Beta→Full; save-failed retry bằng môi trường user cho phép.
6. Cập nhật evidence mỗi P/W: kết luận, file/symbol sửa, save field, lý do tương thích cũ, build và manual case. Mục chưa có kết luận không được để complete.
7. Chỉ đóng vấn đề 6 khi tất cả P/W đạt, không còn lỗi known hoặc producer thiếu; không còn tiến trình reset âm thầm, không còn đường world chưa nối. Sau đó mới đóng 5.06/5.08 và mở bước cleanup phụ thuộc của phần6.

## 11. Thứ tự triển khai kế tiếp

1. Ưu tiên vấn đề 6 theo mục 10; không cần user prompt từng microtask.
2. Hoàn thiện các phần 3/4/5 còn mở và đồng bộ trạng thái tài liệu phần2; report issue ngay nếu ảnh hưởng tính đúng đắn.
3. Art có thể tiếp tục production trong folder riêng khi được giao, nhưng giữ versioned exports và danh sách bảo vệ để không conflict cleanup. Không nhập/xóa artifact đang được Art ghi.
4. Sau gate world/persistence: triển khai phần6 adapter → chuyển callers → cleanup; song song hoàn thiện phần7 assets theo scope đã chốt.
5. Chỉ triển khai phần8 bàn giao cuối khi code/manifest/asset dependencies đủ. User tiếp tục là người quyết định mỹ thuật và nghiệm thu gameplay.
