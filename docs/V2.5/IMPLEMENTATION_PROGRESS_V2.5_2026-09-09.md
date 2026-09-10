# Báo cáo tiến độ triển khai V2.5 — kế hoạch 8 phần

Ngày cập nhật: 10/09/2026. Người rà soát: agent chính, đối chiếu source `main`, đặc tả V2.5 khóa hash và toàn bộ evidence trong nhật ký thực hiện.

- Source: `C:/ws/solo_vs_mortal_godot`.
- Assets/workflow: `C:/ws/asset-production-system`.
- Đặc tả có thẩm quyền: `Solo_vs_Mortal_Gameplay_System_V2.5.md` và `game_spec/`, revision `2026-09-08.closed-1`, content `svm-content-2.5.1`, balance `svm-balance-2.5.1`.
- Báo cáo này là trạng thái triển khai và danh sách việc tiếp theo; không thay thế hoặc định nghĩa lại gameplay.
- Current-state trong các mục 1–11 đã được reconcile theo source/evidence mới nhất. Section 12 append-only vẫn là lịch sử bất biến: không sửa/xóa các entry cũ. Không chạy gameplay acceptance thay user và không xóa asset khi chưa qua dependency/delete gate.

## 1. Cách đọc trạng thái

**complete**: phạm vi của phần đã hoàn tất, không còn yêu cầu triển khai chưa xử lý được biết đến. **in_progress**: đã có code nhưng còn công việc cụ thể bên dưới. **not_complete**: chưa đủ sản phẩm đầu ra hoặc chưa triển khai đầy đủ pipeline.

Build/current package gần nhất: `dotnet build solo_vs_mortal_godot.csproj --no-restore` pass 0 error (1 warning `NU1900` vì không truy cập được NuGet vulnerability metadata); `dotnet publish ... --no-restore --configuration Release --runtime win-x64 --self-contained false` pass cùng warning. Godot .NET 4.7.2 Windows export retry của ISSUE-04 tới bước pack nhưng internal .NET publish báo fail; đây là package/toolchain follow-up, không phải gameplay evidence hay Part4 source defect. Theo quyết định user ngày 2026-09-10, Godot .NET 4.7.2 là implementation baseline chính thức; source pin `Godot.NET.Sdk/4.7.2` và project advertise 4.7 C# Forward Plus. Windows export preset hiện chọn `gl_compatibility`; khác biệt rendering này được ghi nhận riêng và không bị thay đổi bởi ISSUE-01.

Không đánh dấu cả phần complete chỉ vì có class/API, static audit hoặc build thành công. Agent tự chịu trách nhiệm audit source, sửa code/data/docs, build/export bằng toolchain 4.7.2 đã chốt, technical asset QA, dependency cleanup và chuẩn bị checklist. User giữ các gate không thể thay thế: chạy 18 manual gameplay acceptance cases trong game thật và approve/reject mỹ thuật. Không tạo/chạy automated gameplay tests, harness, parity runner, soak hay headless gameplay smoke theo policy hiện hành.

Số phần dưới đây là kế hoạch 8 phần đã giao trong hội thoại. Trường `part` trong một số work item của audit ban đầu dùng cách nhóm khác; không lấy số đó để tự đổi thứ tự kế hoạch. `implementation-tasks-4-8.json` còn trạng thái pending và evidence cũ; phải cập nhật sau khi đối chiếu, không dùng nguyên trạng làm bằng chứng tiến độ.

| Phần | Phạm vi | Status |
|---|---|---|
| 1 | Audit ban đầu và lập kế hoạch migration | complete |
| 2 | Nền tảng authority, loader, cấu trúc và tài liệu | in_progress |
| 3 | Simulation/combat/player/AI nền tảng | in_progress |
| 4 | Soul, Density, Sync, Banner, Summon, Spirit, Possession, capability | ISSUE-04 CODE_COMPLETE — READY_FOR_USER_ACCEPTANCE |
| 5 | Inventory, skills, mastery, quests, loot, world, save/migration | in_progress |
| 6 | Adapter assets và dọn code/assets/docs cũ | in_progress |
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
6. ISSUE-01 complete: dùng Godot .NET 4.7.2 làm implementation baseline chính thức. Xác minh executable/templates 4.7.2 cục bộ rồi build/export bằng chính baseline đó; APIs hỗ trợ bởi 4.7.2 được phép dùng. Không đổi renderer chỉ do đổi baseline.
7. Ghi phiên bản thực tế và kết quả build/export; không chạy gameplay thay user.

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

Status: **ISSUE-04 CODE_COMPLETE — READY_FOR_USER_ACCEPTANCE**.

Đã hoàn tất matrix requirement→owner→producer→caller→mutation→save→validation→restore→Application/UI cho toàn bộ Soul loop. Matrix hiện hành: `docs/V2.5/ISSUE-04_SOUL_LOOP_CLOSURE_MATRIX.md`.

Các gap được xác nhận và đã sửa: producer Sync cho Ally kill/possession kill/ritual giữ E; role radius và transition guard của Summon; caller SummonAll/RecallAll/focus/mode/end possession; cooldown Ally sau recall/restore; tutorial Soul chỉ Skeleton level1; durable-WAL signal cho world drop/pity/Sync progress. Legacy Devour/Essence/Bloodline còn tồn tại chỉ cho compatibility và không được construct trong canonical runtime.

Tất cả 152 Sync source đã được trace (19 species × 8), gồm source event, identity/dedup, award/version, total/milestone, save/restore và UI state. Pathing Part4 dùng nearest-free South-first/clockwise và A* có blocker; no-space không mutate. Không có asset blocker: `NO_ASSET_APPROVAL_REQUIRED_FOR_ISSUE_04 — visual asset production/in-engine visual acceptance belongs to Part 6/7/W09.`

**Remaining Part4 gate:** user chạy 18 canonical manual cases trong Arena thật, ghi screenshot/HUD/save-reload evidence khi fail. Không có automated gameplay test/harness nào được tạo hoặc chạy.

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

Status: **in_progress**.

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

## 10. Vấn đề 6 — current-state persistence và world sau reconciliation

Status: **in_progress; chỉ còn W09 phụ thuộc asset/presentation và user visual acceptance**. Đây là “vấn đề 6” của review cũ, KHÔNG phải Phần 6 cleanup.

### 10.1. Kết quả hiện tại sau khi đối chiếu toàn bộ nhật ký

Bản kế hoạch cũ ở phần đầu từng ghi một số sửa persistence/world còn treo do quota. Các thay đổi đó đã được áp dụng ở các entry sau trong nhật ký. Current-state phải đọc như sau:

| Nhóm | Current state | Kết luận |
|---|---|---|
| P01–P10 persistence | **complete** ở mức implementation/static review | Full canonical state coverage, resource restore ordering, checksum, UID/allocator, dormant actors, pickup-region map, timer/tick semantics, Beta→Full staged migration, WAL/exact retry và rebalance/version preservation đều đã có evidence compile-only. |
| W01–W08 world/runtime | **complete** ở mức implementation/static review | Topology, road/wall/camp, swept-circle movement, hazard, anchor/traversal, E interaction/ritual, durable region transition và RestReset/runtime encounter life đã được trace/sửa. |
| W09 fog/minimap/visual mapping | **in_progress** | Fog/VisitedTiles đúng và persist; phần còn thiếu là canonical UI/world/actor assets, marker thật, asset adapter mapping đầy đủ và in-engine visual acceptance. |

Như vậy **persistence/world không còn là blocker logic chính cho cleanup** ngoại trừ W09 visual dependency. Tuy nhiên các P/W “complete” ở đây vẫn chưa phải gameplay acceptance; các case save/load-in-combat, disk failure, hazard boundary, travel, fog và visual phải do user chạy trong build thật.

### 10.2. Những phần source hiện đã khớp tốt với V2.5

1. **Authority/data:** bundle V2.5 revision `2026-09-08.closed-1` được hash-pin; canonical loader/registry là nguồn gameplay, metadata legacy đã được cô lập để không tự cấp nature/drop/art cho species V2.5.
2. **Deterministic simulation:** `GameSession` là headless owner, fixed tick 60 Hz, RNG streams có seed, combat canonical giữ cast/projectile/cooldown/knockback ở simulation; animation không phát damage.
3. **Persistence:** save schema canonical dùng staged restore, strict validation, checksum, WAL/atomic commit/exact retry; nhiều state khó như RNG, Spirit carry, possession ticks, cooldown ledger, dormant region, pickup region, mastery budget/debt và world lifecycle đã được capture/restore.
4. **World:** topology/collider/hazard/traversal/interaction/transition/RestReset đã có owner/caller thật và fail-closed validation thay vì clamp/fallback ngầm.
5. **Legacy isolation:** canonical V2.5 không khởi tạo Devour/Essence/Bloodline; các đường legacy còn lại là compatibility/bootstrap debt, không còn là gameplay authority.

### 10.3. Những điểm CHƯA khớp/CHƯA đóng so với V2.5

1. **Engine baseline (ISSUE-01 complete):** user đã chọn **Godot .NET 4.7.2** làm implementation baseline chính thức. Đây không còn là blocker của Phần 2/8 và không đổi gameplay authority. Cấu hình hiện có vẫn khác renderer: editor advertise **Forward Plus**, Windows export preset dùng **`gl_compatibility`**; chỉ ghi nhận, không đổi renderer trong ISSUE-01.
2. **Style/runtime resolution mismatch:** style lock yêu cầu native **640×360**, nearest/integer scale/pixel snap; project/presentation hiện còn layout **1280×720** và nhiều HUD/WorldMap absolute coordinate nên chưa thể đổi một dòng config mà không migrate layout.
3. **Asset completeness:** canonical adapter đã có foundation nhưng catalog runtime hiện chỉ map được package Skeleton static nhỏ; phần lớn Beta/Slice asset vẫn chưa có user-approved, hash-versioned integration export. W09 vì vậy chưa đóng.
4. **Manual acceptance:** combat, Soul loop, save/load, world và UI mới có static/build evidence. 18 canonical acceptance cases chưa có user result; không được nâng Part 3/4/5/8 thành complete.
5. **Architecture debt:** layering `Core/Data → Simulation → Application → Presentation` vẫn đúng ở macro level, nhưng `Arena.cs`, `GameApplication.cs`, `V25CombatCoordinator.cs` và `CanonicalV25Definitions.cs` đã trở thành hotspot lớn. Presentation còn import một số `Simulation.*`/`Data.*`, và save/travel durable orchestration vẫn nằm nhiều trong `Arena`; đây không nhất thiết đổi gameplay V2.5 nhưng làm tăng rủi ro khi tiếp tục tích hợp.
6. **Repository hygiene:** `.codex_obj/` đang bị track dù là generated .NET state; `_beta_helpers/_beta_jobs` và một số asset tooling còn ở root; helper dùng workspace `C:\ws\asset-production-system`. Cần dọn/parameterize nhưng chỉ sau khi xác định dependency để không làm hỏng pipeline đang dùng.
7. **Commit discipline:** current `main` commit `66a0ea9` có message chỉ là `commit` dù thay đổi cross-cutting runtime/data/docs/assets/tooling. Các phase sau phải dùng commit message có scope và tránh gom migration + asset production + presentation cleanup vào một commit khó audit.

### 10.4. Gate đóng vấn đề 6

Vấn đề 6 chỉ được coi là đóng khi:

- W09 có canonical marker/world/actor mapping đủ cho scope đang nghiệm thu;
- không còn technical fallback bị hiểu nhầm là final visual;
- user đã xác nhận fog/minimap/visual visibility trong build thật;
- các manual persistence/world cases liên quan không phát hiện lỗi mới làm mở lại P/W đã đóng.

## 11. Tình hình source so với V2.5 và kế hoạch triển khai kế tiếp

### 11.1. Kết luận current-state

Source hiện **không ở trạng thái “thiếu core gameplay”**. Phần lớn nền tảng V2.5 quan trọng đã có: authority loader, deterministic simulation, combat runtime, Soul systems, inventory/skill/mastery/quest/loot/world, staged persistence và asset adapter foundation. ISSUE-01 baseline engine đã complete; blocker lớn nhất hiện tại chuyển từ “thiếu system” sang ba nhóm:

1. **đóng static review/caller coverage còn lại ở Part 4–5 và chuẩn bị manual acceptance**;
2. **hoàn thiện asset production → user approval → versioned export → canonical mapping**;
3. **giảm architecture debt của Presentation/Application trước khi tích hợp visual cuối và cleanup legacy**.

Do đó hướng xử lý đúng là **không rewrite gameplay**, không đổi formula/spec đang khóa. Tiếp tục theo chiến lược: `close functional delta → stabilize boundary → integrate approved assets → cleanup → baseline build → user acceptance`.

### 11.2. Phân công: việc agent tự làm và việc user bắt buộc làm

| Việc | Agent tự làm | User phải làm |
|---|---|---|
| Audit source/spec/hash/caller/save field | **Có** | Không cần prompt từng microtask |
| Sửa C#/JSON/MD/project config/export preset | **Có**, nếu không đổi gameplay authority | Chỉ cần quyết định khi thay gameplay authority hoặc rendering intent |
| Build `dotnet` và Godot CLI/export | **Có**, khi executable/templates tương ứng tồn tại trong environment | Cài/cung cấp toolchain còn thiếu |
| Verify Godot .NET 4.7.2 executable/templates và baseline package | **Có**, khi toolchain hiện diện | Không cần hành động sau quyết định baseline 2026-09-10 |
| Quyết định implementation baseline Godot .NET 4.7.2 | Đã ghi vào policy/docs | **User đã quyết định — complete** |
| Refactor architecture không đổi behavior | **Có** | Không cần duyệt từng file; report nếu có risk gameplay/save |
| Generate asset theo batch, technical QA, hash/manifest/export | **Có** | Không cần prompt từng AssetId |
| Đánh giá “đẹp/xấu”, approve/reject art, identity/style | Không tự approve | **User quyết định** |
| Chạy 18 canonical gameplay acceptance cases trong game thật | Không chạy thay user theo policy | **User thực hiện** |
| Cung cấp screenshot/log/runtime symptom khi manual case fail | Phân tích/fix | **User cung cấp evidence runtime** |
| Cleanup dead code/assets đã qua dependency gate | **Có** | User chỉ cần can thiệp với asset/file protected hoặc quyết định phá vỡ compatibility |

Quy tắc vận hành: user **không phải ngồi giao từng việc nhỏ cho agent**. Agent tiếp tục tự audit/sửa/build/QA theo plan; chỉ dừng ở các gate thật sự cần con người: visual approval và gameplay acceptance.

### 11.3. Bước A — ISSUE-01 baseline engine complete; giữ package gate

User đã quyết định ngày 2026-09-10: **Godot .NET 4.7.2** là implementation baseline chính thức. Không cần cài, compile, export hoặc verify một baseline Godot cũ hơn. Agent xác minh executable/templates 4.7.2 cục bộ, chạy `dotnet build` và Godot export bằng đúng 4.7.2, rồi ghi evidence kỹ thuật. APIs do 4.7.2 hỗ trợ được phép dùng; không đổi `project.godot`/export renderer chỉ vì quyết định baseline. Compile/export pass là baseline technical evidence, không phải gameplay acceptance và không tự đóng Part 2 hoặc các part gameplay.

### 11.4. Bước B — đóng phần code V2.5 còn mở trước visual integration lớn

Agent thực hiện liên tục, không cần user prompt từng bước:

1. **Part 4 Soul loop:** đã code-complete bởi ISSUE-04; giữ matrix/manual handoff và chỉ mở lại khi user báo lỗi từ real-scene acceptance.
2. **Part 5 gameplay/content:** review nốt Inventory/skill-loadout/mastery/quest/loot/unique/boss ở runtime caller thật; không chỉ dựa vào class/API tồn tại.
3. Rà các transaction cần durable save để không còn đường state mutation chờ autosave ngoài contract.
4. Rà exact source/grant/cooldown ownership khi đổi equipment, passive, possession, summon/recall/death/travel.
5. Build sau mỗi nhóm thay đổi logic; nếu phát hiện mismatch với spec khóa thì sửa code, **không sửa spec/hash để hợp thức hóa implementation**.
6. Tạo một current matrix ngắn cho 18 acceptance cases: case nào code-ready, case nào asset-blocked, case nào user có thể chạy ngay.

### 11.5. Bước C — refactor architecture có kiểm soát, không rewrite gameplay

Thực hiện sau khi functional delta của nhóm tương ứng đã ổn, trước khi thêm nhiều UI/assets mới:

1. **Arena:** tách input adapter, save/travel presentation coordinator, actor/world renderer và HUD/screens khỏi một `Arena.cs` quá lớn. `Arena` giữ vai trò scene composition + Godot lifecycle.
2. **Application boundary:** Presentation chỉ dùng command/query/DTO của Application cho gameplay flow; giảm import trực tiếp `Simulation.*` và `Data.*`.
3. **Persistence orchestration:** chuyển transaction logic thuần application (prepare/capture/commit/rollback decision) ra khỏi Presentation; `Arena` chỉ gọi use case và hiển thị kết quả/I/O state cần thiết.
4. **GameApplication:** split facade theo use-case nhóm (combat/soul/world/progression/persistence) hoặc partial/service nội bộ, vẫn giữ một public application boundary ổn định cho Presentation.
5. **GameSession construction:** tách bootstrap/wiring V2.5 khỏi constructor khổng lồ nhưng giữ `GameSession` là simulation composition/tick owner.
6. **V25CombatCoordinator / CanonicalV25Definitions:** chỉ split implementation theo responsibility; không đổi timing/order/formula đã static-audit nếu không có bug cụ thể.
7. Mọi refactor phải build pass và không thay save schema/asset ID/gameplay number trừ khi task gốc yêu cầu.

### 11.6. Bước D — hoàn thiện asset production và user review theo gate

**Agent tự chạy pipeline:**

1. Queue world độc lập có thể tiếp tục generate/technical-QA theo dispatch plan mà không chờ Player.
2. Player phải **rework 4 hướng Idle trước** vì identity/scale/clothing là dependency của Move/Attack/Hit/Death; không nhân một base sai sang hàng chục clip.
3. Mỗi batch giữ version/hash/manifest, frame/pivot/gutter/alpha/palette/timing/sockets/layering evidence.
4. Không promote `generated` thành `approved`; không overwrite revision cũ.
5. Khi đủ một nhóm integration-ready, assemble **versioned export** chỉ chứa dependency cần cho game, không bê cả production workspace vào repo.

**User tại gate review:**

1. Review các base/animation/visual quan trọng theo batch, không cần duyệt từng bước kỹ thuật.
2. Quyết định `approved / needs_rework / rejected` cho đúng revision/hash.
3. Kiểm trực quan trong scene/game build khi agent đã map asset; screenshot/feedback đủ để agent sửa batch.

### 11.7. Bước E — migrate presentation sang canonical visual và đóng W09

Sau khi có approved export:

1. Agent add AssetId/file/hash/frame/pivot/duration/socket/layer metadata vào canonical catalog.
2. Migrate lần lượt Player → Monster/Ally/Soul → world/terrain → equipment → UI marker.
3. Giữ explicit `MISSING:<AssetId>` cho ID chưa có; không fallback sang prototype sai loài/style.
4. Migrate HUD/WorldMap layout từ 1280×720 sang **native 640×360** đồng bộ với style lock; bật nearest/integer scale/pixel snap sau khi control anchors/layout đã phù hợp.
5. Kiểm clip direction, Y-sort, front/back weapon layer, socket alignment và animation timing; gameplay damage vẫn theo Simulation tick.
6. User chạy visual/in-engine review. Khi marker/world/actor visibility đúng và fog/minimap không lộ state sai, đóng W09.

### 11.8. Bước F — cleanup source/repository sau replacement gate

Agent thực hiện theo dependency, không xóa wildcard:

1. Xóa dead presentation API/legacy asset caller đã có canonical replacement.
2. Cô lập hoặc loại `GameDefinitions`/asset-manifest/characterAnimations compatibility dependency khi bootstrap V2.5 không còn cần.
3. Chỉ xóa Devour/Essence/Bloodline physical files khi legacy-save/support decision cho phép; canonical runtime isolation hiện đã đạt.
4. Bỏ generated `.codex_obj/` khỏi Git và thêm ignore phù hợp nếu xác nhận không có dependency runtime.
5. Gom `_beta_helpers/_beta_jobs` vào `tools/asset_pipeline/` hoặc production workspace; đổi hard-coded `C:\ws\asset-production-system` thành CLI/config/environment input.
6. Chuẩn hóa commit theo scope (`fix(v2.5): ...`, `refactor(presentation): ...`, `feat(assets): ...`) và tránh commit message kiểu `commit`.
7. Build/export lại sau mỗi cleanup group; ghi replacement ID và file xóa trong dependency inventory.

### 11.9. Bước G — manual acceptance và đóng 8 phần

Khi code + approved asset scope + baseline engine đều sẵn:

1. Agent build/export package bằng Godot .NET 4.7.2 baseline đã chốt.
2. Agent đưa checklist 18 case theo thứ tự dễ chạy, kèm expected result và cách lấy log/screenshot khi fail.
3. **User chạy game thật** và ghi pass/fail; agent không tự đánh dấu pass.
4. Agent sửa từng lỗi user phát hiện, static review vùng bị ảnh hưởng, build lại.
5. User rerun đúng case bị ảnh hưởng; không bắt chạy lại toàn bộ nếu dependency không lan rộng.
6. Khi Part 3/4/5 manual cases pass, Part 6 cleanup dependency pass, Part 7 art decision/export pass, Part 8 package pass thì mới đổi status tổng sang complete.

### 11.10. Thứ tự ưu tiên ngay từ current commit

1. **Không thêm gameplay feature mới ngoài V2.5** cho tới khi current requirements/caller matrix được reconcile.
2. Agent tiếp tục Part 5 static closure + architecture boundary cleanup có kiểm soát; Part4 chỉ nhận regression từ user acceptance.
3. Song song Art chạy world batches; Player dependent clips chờ 4-direction Idle rework + user decision.
4. Agent duy trì evidence executable/templates Godot .NET 4.7.2 và baseline build/export khi toolchain/package configuration thay đổi.
5. Khi approved asset export xuất hiện: map catalog → migrate 640×360 presentation → đóng W09.
6. Sau đó cleanup legacy/repo hygiene.
7. Cuối cùng build/export bằng Godot .NET 4.7.2 baseline và user chạy 18 acceptance cases.

## 12. Nhật ký thực hiện append-only

> Phần này chỉ bổ sung trạng thái và evidence mới; không sửa hoặc xóa nội dung kế hoạch/báo cáo ở các mục trên. Mỗi mục tham chiếu chính xác phần/bước gốc để giữ lịch sử audit.

### 2026-09-09 — Phần 2 / bước 1: khóa authority và phân loại nguồn

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Đối chiếu `data/v2.5/spec-lock.json` của source với `C:/ws/asset-production_system/game_spec/spec-lock.json`, rồi tính SHA-256 lại toàn bộ sáu file authority được khóa: tài liệu V2.5, content, balance, asset requirements, style lock và acceptance cases.
- **Evidence:** 6/6 hash khớp revision `2026-09-08.closed-1`; hai file `spec-lock.json` cũng có cùng SHA-256 `a4c70efaba0e4aa3bf72181059e8f229a5d2b5c3f49d8bab848ea6779b23b492`.
- **Hệ quả:** Từ bước này, chỉ bundle authority đã khóa được dùng để quyết định gameplay. Các báo cáo/work-item vẫn là tài liệu triển khai, không phải nguồn thay đổi balance hay mechanic.

### 2026-09-09 — Phần 2 / bước 2: audit loader và canonical bootstrap roster

- **Trạng thái:** `complete`.
- **Lỗi đã xác nhận:** `GameDefinitions.WithCanonicalRoster` từng copy `SoulNatureId` và `SoulDrop` từ definition Skeleton cho 16 species không có trong config cũ. Như vậy UI/legacy bridge có thể gán sai bản chất Soul; một lần refactor caller về sau cũng có nguy cơ dùng lại drop metadata cũ.
- **Đã thực hiện:** Thay template-copy bằng record adapter được tạo cho **mọi** canonical species. Adapter chỉ chứa ID/display name, level envelope, range từ combat-style và asset/audio rỗng; combat, AI, drop, capability và skill tiếp tục lấy từ `CanonicalContentRegistry`.
- **Evidence:** `src/Data/Definitions/GameDefinitions.cs` tạo `V25_<SPECIES>` SoulNature compatibility metadata riêng, với trait/cost neutral; không một species canonical nào còn mang `UNDEAD_WARRIOR`, `GOBLIN_RAIDER` hoặc `STONE_CONSTRUCT` từ config cũ. Build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Phần 2 / bước 3: adapter metadata compatibility

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Adapter metadata được giới hạn ở `GameDefinitions` và không phát sinh asset fallback, audio fallback hay legacy drop behavior. Các nature cũ vẫn tồn tại chỉ để đọc save/legacy path, còn V2.5 references `V25_<SPECIES>`.
- **Evidence:** Build compile-only pass sau thay đổi; chưa được xem là nghiệm thu gameplay/visual.

### 2026-09-09 — Vấn đề 6 / P04: UID và allocator

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Restore dormant nhận tập UID actor đang active để reject trùng UID xuyên vùng. Restore pre-swap tính suffix lớn nhất của actor/cast/projectile/cooldown/hit/knockback/world Soul/dormant/possession IDs và từ chối save có `uidNext` đi lùi. Inventory restore giữ `NextInstance` ít nhất bằng suffix `item.<n>` cao nhất, nên không tái cấp instance đã tồn tại.
- **Bảo toàn:** Mọi validation này chạy trên staged session trước swap; save lỗi bị từ chối, không ghi đè file hay thay session đang chạy.
- **Evidence:** `GameApplication.ValidateUidAllocatorFloor`, `MonsterSystem.RestoreDormantRegions`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / P05: dormant actor contract

- **Trạng thái:** `in_progress`.
- **Đã hoàn thành trong bước này:** Dormant row hiện phải có authored encounter đúng region/species/level/type/reward eligibility, metadata species canonical đúng, `definitionId` đúng adapter, vị trí và home finite/in-map, home đúng authored encounter, UID không đụng actor active hay dormant khác. Boss add runtime không bị ép vào dormant authored-row contract.
- **Còn lại trước khi đóng:** Rà chính xác expiry status/shield, cooldown/target links khi park-resume và lifecycle Rest/travel với actor non-static trong các đường gọi còn lại.
- **Evidence:** `src/Simulation/Systems/MonsterSystem.Regions.cs`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / P01: inventory state persistence

- **Trạng thái:** `in_progress`.
- **Đã audit:** `CaptureCanonicalSave` có capture → serialize/checksum cho player resources/runtime actor/cast/projectile/cooldown/hit/knockback, Soul density/pity/pickup/consumed receipts, summon/possession/cooldown, traversal, inventory/overflow/equipment/`NextInstance`, skill/passive/mastery budget+debt, quest/loot/unique/world lifecycle, RNG và fixed-tick Spirit carries. `RestoreCanonicalSaveInPlace` có đường restore tương ứng trong staged session.
- **Đã xử lý mismatch:** World state không thể suy ra (`HazardTicks`, `DormantRegions`, `PickupRegions`, `VisitedTiles`) thiếu ở save cũ nay bị reject có chủ đích; không còn restore bằng default làm mất/reposition tiến trình.
- **Còn lại trước khi đóng:** Tiếp tục trace source/target links của combat runtime, expiry semantics và selection/command state để tách thật sự transient khỏi state phải persist.
- **Evidence:** `src/Application/GameApplication.cs`, `src/Application/Persistence/V25/V25SaveData.cs`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / P02: thứ tự restore resources và weapon style

- **Trạng thái:** `complete`.
- **Đã audit:** Player runtime được restore trước để giữ current HP/Spirit chính xác; inventory, passive và possession sau đó dựng modifier sources; cuối restore recompute derived maxima rồi set lại absolute current values, chỉ clamp ở max đã xác minh. Save current không được refill theo tỉ lệ.
- **Lỗi đã xác nhận và sửa:** Combat style trước đây chỉ tin runtime actor và không được suy lại từ main-hand. `V25InventorySystem` nay set style từ main-hand (không có main-hand thì `fist`); restore reject nếu style đã lưu khác style của equipment đã restore.
- **Evidence:** `PlayerSystem.SetCanonicalCombatStyle`, `V25InventorySystem.ApplyEquipmentModifiers`, `GameApplication.RestoreCanonicalSaveInPlace`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / P03: checksum, normalization và compatibility

- **Trạng thái:** `complete`.
- **Đã audit:** Codec deserialize bằng schema đóng, validate shape/reference, tính lại envelope SHA-256 trên representation deserialize rồi mới restore. `JsonIgnore(WhenWritingNull/Default)` giữ representation của file cũ có optional field vắng mặt; dictionary/list được capture từ snapshot có thứ tự rõ ràng trước khi checksum.
- **Quyết định compatibility:** File checksum cũ vẫn có thể được đọc và checksum được kiểm; nếu thiếu state không thể migrate chính xác thì restore bị từ chối trước swap và file không bị ghi đè. Checksum sai luôn bị từ chối.
- **Evidence:** `V25SaveCodec.Deserialize/Normalize/ComputeEnvelopeChecksum`, `GameApplication.ValidateImplementedCanonicalPayload`; build compile-only pass, 0 warning / 0 error. Manual disk-failure/checksum case vẫn thuộc checklist user.

### 2026-09-09 — Vấn đề 6 / P06: pickup region map

- **Trạng thái:** `complete`.
- **Đã audit:** Mỗi canonical pickup được capture cùng `PickupRegions`; restore yêu cầu region tồn tại trong active profile, reject key không có pickup, và `WorldSouls`/auto-collect chỉ nhìn pickup của current region.
- **Đã xử lý compatibility:** Missing mapping với pickup chưa consume bị reject thay vì đưa pickup sang current region. Điều này bảo toàn file cũ và chặn Soul nhảy vùng hoặc bị consume hai lần.
- **Evidence:** `SoulSystem.RestorePickupRegions`, `SoulSystem.PickupInCurrentRegion`, `GameApplication.CaptureCanonicalSave`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / P07: timer save/suspend/restore/travel

- **Trạng thái:** `in_progress`.
- **Đã audit:** Fixed clock chạy 60 Hz; combat status/shield expiry lọc theo `ExpireTick > currentTick`; Spirit lưu cả fractional carry và rate carry; potion, dodge, respawn, summon recovery, hazard accumulator, crumble và traversal grace đều là tick state được capture/restore. Suspend dừng `_PhysicsProcess`, vì vậy không có catch-up offline.
- **Đã sửa:** Transition lock 300 ms sau Possession trước đây chỉ có trong RAM. `PossessionTransitionLockTicks` nay được ghi/validate/restore như state bắt buộc (0..18 ticks). Save thiếu field này bị từ chối giữ nguyên, thay vì reset lock và cho phép thao tác sớm sau load.
- **Còn lại trước khi đóng:** Chuẩn hóa Possession duration/cooldown từ số thực sang representation tick trong persistence, rồi trace lại cooldown của actor dormant và manual suspend tại ranh giới hazard/possession. Chưa đánh dấu complete vì các timer Possession hiện vẫn capture dưới đơn vị seconds.
- **Evidence:** `PossessionSystem.CanonicalTransitionLockTicks`, `V25SaveDocument.PossessionTransitionLockTicks`, `GameApplication.CaptureCanonicalSave/RestoreCanonicalSaveInPlace`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / W04: hazard và môi trường theo actor

- **Trạng thái:** `in_progress`.
- **Đã thực hiện:** Hazard clock 500 ms được lưu theo UID và reset khi actor rời vùng hazard; environmental HP loss đi thẳng vào HP, không gọi damage resolver nên không có shield/crit/mastery. FrostFloor của Ally dùng `AllySystem.EnvironmentSpeedMultiplier` và capability của chính species, không mượn capability Player. Return movement của Ally cũng đã dùng canonical move speed thay vì di chuyển cố định theo delta.
- **Còn lại trước khi đóng:** Rà contract Ward riêng từng actor và phạm vi CrumblingFloor/rescue để xác nhận mọi actor thuộc scope có producer/caller đúng V2.5. Chưa có evidence manual cho exit/reload đúng tick 500 ms.
- **Evidence:** `GameSession.TickCanonicalWorld`, `GameSession` canonical environment setup, `AllySystem.CanonicalMoveSpeed`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / W07: region transition và durable boundary

- **Trạng thái:** `in_progress`.
- **Lỗi đã xác nhận:** Presentation trước đây gọi `TravelToRegion`, đổi runtime/map ngay, rồi mới thử save. Nếu ghi đĩa thất bại, UI trả failure nhưng session vẫn ở region mới chưa được durable commit.
- **Đã sửa:** `Arena.TravelToRegion` nay flush mutation bền vững có trước, giữ envelope trước travel trong RAM, chỉ rebuild sprite/map sau khi post-transition commit thành công. Nếu commit đích thất bại, runtime restore từ envelope cũ; nếu rollback cũng lỗi thì chặn ghi tiếp và yêu cầu load save hợp lệ.
- **Còn lại trước khi đóng:** Rà toàn bộ mutation region/background/asset khi recovery và user chạy manual disk-failure retry; trace world lifecycle/RestReset để hoàn tất W08.
- **Evidence:** `Presentation/Arena.cs:TravelToRegion`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Cập nhật trạng thái Vấn đề 6 / P05: dormant actor contract

- **Trạng thái:** `complete`.
- **Đã hoàn thành phần còn lại:** Status/shield của dormant actor giữ nguyên trong snapshot nhưng chỉ còn hiệu lực khi `ExpireTick > tick` lúc resume. Cooldown cast canonical giờ dừng cùng dormant actor; vì trạng thái/AI/cooldown của vùng park đều pause, không có một loại timer nào tiếp tục chạy riêng khi vùng không active. `TargetUid` không được đưa vào dormant row: canonical travel bị chặn khi combat/target còn active, còn row resume khởi đầu từ authored home/AI state và target được acquire lại trong fixed tick.
- **Quyết định contract:** Chỉ authored normal/elite/boss encounter lives được dormant. Boss add là actor runtime của combat và không được serialize thành dormant row; travel ngoài combat nên không có add hợp lệ ở ranh giới park. Save giả mạo bị reject ở staged restore.
- **Evidence:** `MonsterSystem.Regions.ParkRegion/ResumeRegion/RestoreDormantRegions`, `V25CombatCoordinator.DecrementCooldowns`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Cập nhật trạng thái Vấn đề 6 / P07: timer save/suspend/restore/travel

- **Trạng thái:** `complete`.
- **Đã hoàn thành phần còn lại:** Possession duration/cooldown được giữ nội bộ bằng integer tick; seconds trong HUD/save chỉ là giá trị `ticks / 60` dẫn xuất. Transition lock 300 ms cũng là tick state bắt buộc. Cooldown cast canonical, status/shield, dodge, potion, summon recovery, hazard, crumble/grace, respawn, Spirit carry đều là tick/fixed-point state có capture/restore; suspend dừng fixed tick và không có offline catch-up.
- **Đã loại ảnh hưởng legacy:** `AttackCooldown` dạng double của Player/Monster/Ally/Summon không còn điều khiển đường canonical và được canonical capture/restore về 0. Cooldown gameplay duy nhất của canonical là `V25CombatCoordinator` tick ledger, tránh serialize một timer số thực cạnh tranh với timer tick.
- **Evidence:** `PossessionSystem`, `V25SaveDocument.PossessionTransitionLockTicks`, `V25CombatCoordinator`, `AllySystem.UpdateCanonical`, `GameApplication.CanonicalRuntimeSnapshot`; build compile-only pass, 0 warning / 0 error. Case suspend/hazard/possession tại tick biên vẫn là manual acceptance của user theo `AGENTS.md`.

### 2026-09-09 — Vấn đề 6 / P08: upgrade Beta 01 sang Full 01

- **Trạng thái:** `complete`.
- **Đã audit:** Nâng profile capture beta envelope trước, dựng `GameApplication`/`GameSession` full trong isolation, validate payload beta theo registry beta rồi restore trên runtime full. Density tạo ownership rỗng/locked cho species full chưa có; Sync và Quest nối default rows full còn thiếu. Các award, receipt, proof, density/pity, inventory/equipment, mastery, quest beta, world lifecycle, RNG và runtime beta giữ nguyên payload/identity.
- **Commit boundary:** UI buộc save slot hiện tại trước nâng và commit lại chính slot đó sau khi staged migration thành công. Nếu staged restore lỗi, session beta đang chạy và file cũ không bị thay.
- **Evidence:** `GameApplication.UpgradeCanonicalProfile/RestoreCanonicalSave/BuildCanonicalDensitySnapshot`, `SoulSystem.RestoreCanonicalState`, `Arena.cs` callback `Mở Full 01`; static trace đã xác nhận profile mismatch branch của Sync/Quest chỉ bổ sung default state cho content mới. Build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Vấn đề 6 / W08: RestReset và runtime encounter life

- **Trạng thái:** `complete`.
- **Lỗi đã xác nhận:** `RestReset` có thể spawn normal/elite mới khi runtime vẫn giữ actor đã chết cùng `EncounterId`, khiến một encounter tích lũy nhiều life record qua nhiều vòng nghỉ.
- **Đã sửa:** Reset giờ yêu cầu đúng một retired/dead life của encounter, xóa row đó rồi mới spawn life kế tiếp. `_defeated` chỉ được bỏ với normal/elite của region hiện tại sau khi replacement hợp lệ; boss không nằm trong reset scope. Nếu lifecycle/runtime không khớp, thao tác bị fail thay vì tạo duplicate actor.
- **Evidence:** `MonsterSystem.RemoveDefeatedEncounter`, `GameSession.ResetCanonicalEncounter`, `V25WorldLifecycleSystem.RestReset`; build compile-only pass, 0 warning / 0 error.

### 2026-09-09 — Cập nhật trạng thái Vấn đề 6 / P01: full canonical persistence coverage

- **Trạng thái:** `complete`.
- **Lỗi đã xác nhận và sửa:** Ally có lưu `RecentAttackerUid/age`, nhưng target selector thực tế đọc thêm index `_recentPlayerAttackers` trong RAM. Restore trước đây không rebuild index này, nên priority bảo vệ Player mất âm thầm sau load. Restore giờ dựng lại index theo tick tuổi còn lại; clear map cũng clear index để không kéo attacker của map cũ sang map mới.
- **Đã siết reference graph:** Target của Monster phải là Player/Ally sống; target/focus của Ally phải là Monster sống; Player không có AI target; focus UID/duration phải nhất quán; recent attacker của Ally phải là Monster runtime. Cast/projectile/cooldown/hit/knockback, navigation, AI mode/think/focus/path state, input buffer, summon links và RNG đã có capture/restore staged trước đó.
- **Evidence:** `AllySystem.RestoreCanonicalRuntime/Clear`, `GameApplication.ValidateCanonicalRuntimeBeforeSwap`, `CanonicalRuntimeSnapshot`; build compile-only pass, 0 warning / 0 error. Nghiệm thu save/load trong combat vẫn thuộc manual checklist user theo `AGENTS.md`.

### 2026-09-09 — Vấn đề 6 / P09: WAL, atomic commit và exact retry

- **Trạng thái:** `complete`.
- **Đã audit:** Save writer normalize/validate rồi ghi `.tmp` bền vững, ghi WAL `staged`, đọc-validate lại temp, promote atomically, copy backup, chuyển WAL `committed` và append history. Recovery chỉ promote staged file khi checksum/transaction/sequence/slot khớp; candidate khác slot hoặc version không hỗ trợ bị giữ nguyên/reject. `.previous`, `.backup`, `.wal`, `.rejected` và history tạo sequence fence, không được coi foreign slot là fallback.
- **Exact retry:** `Arena` chỉ capture một `_pendingSave`; nếu I/O thất bại simulation bị pause và F5 dùng lại envelope/hash/transaction đó. `PrepareCanonicalRewardCommit` chạy trước lần capture duy nhất; `CommitCanonicalAcquisitionEvents` chỉ phát event và clear durable flag sau store commit thành công. Không có UI success hoặc callback reward trước commit.
- **Evidence:** `V25SaveStore.Commit/Recover/RecoverFromCandidates`, `Arena.Save/TryLoad`, `GameApplication.PrepareCanonicalRewardCommit/CommitCanonicalAcquisitionEvents`; static audit, build compile-only pass trước đó. Disk-failure sequence là manual acceptance case của user.

### 2026-09-09 — Vấn đề 6 / P10: rebalance, removed content và award ledger

- **Trạng thái:** `complete`.
- **Quyết định compatibility:** Build này chỉ hỗ trợ đúng `format/schema/spec/content/balance` đã pin. File có version khác bị reject-preserved từ đầu recovery, không downgrade sang backup, không recompute award và không ghi đè. Đây là bảo toàn bằng từ chối rõ ràng, không phải tuyên bố migration rebalance.
- **Đã audit:** Density ledger giữ `Granted/Applied/Pending/Discarded`, proof/result/source-level/rank tại thời điểm award; Sync giữ award amount/source/version/commit sequence; mastery, loot, receipt và fact có identity/provenance riêng. Restore kiểm tổng ledger/ID/proof để không sum lại từ balance hiện tại. Không có `LegacyRelic` hoặc mapping migration explicit trong authority/runtime, nên không có đường xóa ownership hay hạ progress ngầm.
- **Evidence:** `V25SaveCodec.Validate`, `V25SaveStore.ReadVersionIncompatibility/Recover`, `V25DensityEngine.Restore`, `V25SyncSystem.Restore`; static audit, build compile-only pass trước đó.

### 2026-09-10 — Vấn đề 6 / W01: blueprint topology, tọa độ và roster

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Loader canonical hiện validate trước khi dựng map: topology cố định 4 chunk trong lưới 2×2, `chunk=128`, `tile=32`, road4, camp8, outer-wall2; uniqueness của chunk cell; toàn bộ local/global coordinate của shrine/NPC/entry/exit/road/chest/tutorial/arena/secret và encounter. Encounter bắt buộc có đúng một nguồn tâm và tọa độ thuộc authored chunk.
- **Fail closed:** `GameSession.SpawnCanonicalEncounter` không còn clamp/reposition tọa độ encounter. Nếu dữ liệu ngoài map, load/spawn báo lỗi rõ ràng thay vì tạo quái ở tọa độ khác.
- **Evidence:** `CanonicalV25Definitions.ValidateLayoutBlueprint`, `GameSession.SpawnCanonicalEncounter`; build compile-only ngày 2026-09-10 pass, 0 warning / 0 error.

### 2026-09-10 — Vấn đề 6 / W02: road, wall, camp và walkmesh topology

- **Trạng thái:** `complete`.
- **Đã audit:** `V25WorldLayout.Roads/Populate` dùng road rộng 4 tile, wall ngoài dày 2 tile, decor loại trừ camp 8 tile, encounter clearance 6 tile, road clearance 4 tile và hazard/object clearance. Road không phải collider giả; map base walkmesh trống và chỉ `World.BlockingRects()` mới chặn, vì vậy main path không có collider road ẩn.
- **Quyết định topology:** Entry/exit là portal object không blocking nằm trong map, không phải lỗ trên outer wall. Chúng chỉ thực hiện transition khi `PreviewCanonicalRegion` xác nhận adjacency, vị trí portal, boss/rank/fact gate. Điều này khớp blueprint hiện có và không tạo đường đi xuyên wall.
- **Nghiệm thu còn lại:** User kiểm tra trực quan/đi bộ path trong game; không có gameplay runner mới theo `AGENTS.md`.

### 2026-09-10 — Vấn đề 6 / W03: movement, collider và swept collision

- **Trạng thái:** `complete`.
- **Lỗi đã sửa:** Monster/Ally từng dùng fixed radius18 dù content có role `actor`, `large_actor`, `huge_actor`. Đã thêm `V25ActorBodyRadii` (10/18/26) lấy từ canonical species role và nối vào AI chase/return, Ally path/rescue, dash và knockback. Player tiếp tục dùng radius10.
- **Đã siết capability:** Non-player terrain barriers gồm Gap/ShallowWater/PhasePassable/CrumblingFloor; Ally/Monster không mượn capability của Player possession. Frost của Ally là multiplier theo species capability riêng. Walk/dodge/dash/knockback giữ swept-circle path; restore player position reject fail-closed nếu ngoài walkmesh/collider thay vì clamp thành vị trí khác.
- **Evidence:** `V25ActorBodyRadii`, `PlayerSystem.NonPlayerSweptPosition/RestoreCanonicalPosition`, `MonsterSystem`, `AllySystem`, `V25CombatCoordinator`, `GameSession`; build compile-only pass, 0 warning / 0 error.

### 2026-09-10 — Vấn đề 6 / W04: hazard và môi trường theo actor

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Hazard Fire/Toxic query được parenthesize rõ terrain+rect, tick 30 fixed ticks (=500ms), lưu accumulator từng actor và reset khi rời vùng. Damage đi thẳng HP (`shieldAbsorbed=0`), không crit/shield/mastery. Player Ward chỉ áp Player capability; Ally không mượn Ward từ Player. Ally FrostFloor dùng capability species riêng; CrumblingFloor bị coi là non-player barrier vì không có rescue contract cho AI.
- **Evidence:** `GameSession.TickCanonicalWorld`, `AllySystem.EnvironmentSpeedMultiplier/ApplyEnvironmentalDamage`, `Player.NonPlayerTerrainBarriers`; build compile-only pass, 0 warning / 0 error.

### 2026-09-10 — Vấn đề 6 / W05: safe anchor, traversal state và capability expiry

- **Trạng thái:** `complete`.
- **Đã siết restore:** Save traversal giờ phải khớp terrain thực tại vị trí Player, region gate-state hiện hành và safe-anchor/candidate gate-state của region. Sai lệch bị reject trong staged session; không clamp vị trí rồi giữ grace/anchor cũ.
- **Quyết định gate identity:** Blueprint V2.5 không định nghĩa nhiều locked gate trong cùng region; `CanonicalRegionId` là identity duy nhất của gate context. Portal progression được kiểm ở `PreviewCanonicalRegion`; các shortcut vẫn dùng `V25TraversalSystem` capability/grace/rescue. Không có path nào teleport qua portal chưa đạt fact/rank.
- **Evidence:** `GameApplication.RestoreCanonicalSaveInPlace`, `V25TraversalSystem`; build compile-only pass, 0 warning / 0 error.

### 2026-09-10 — Vấn đề 6 / W06: E interaction, shrine và ritual

- **Trạng thái:** `complete`.
- **Đã thực hiện:** E chạy theo thứ tự khóa `quest → pickup → shrine → NPC`, dùng phạm vi48 và tie theo distance/ID ở query NPC. Quest NPC có query pending riêng; pickup dùng canonical current-region map; portal chỉ được thực hiện sau các nhánh ưu tiên và đi qua durable transaction ở `Arena.TravelToRegion`.
- **Ritual:** `V25UniquePowerSystem` có ritual state machine 180 tick cho Covenant. Chỉ start tại shrine khi đủ fact; phải giữ E; move/rời shrine, nhả E, nhận damage hoặc chết sẽ cancel không tạo receipt. Chỉ khi complete mới commit Unique Power/durable flag. Rest 1 giây và RestReset đã có UI action, gate tại shrine/out-combat/hazard và save khi state đổi.
- **Evidence:** `V25QuestSystem.HasPendingNpcObjective`, `GameApplication.NearbyCanonicalNpcId/BeginCanonicalUniqueRitual`, `V25UniquePowerSystem.BeginAvailableRitual/AdvanceRitual`, `Arena._PhysicsProcess/RebuildFeaturePanel`; build compile-only pass, 0 warning / 0 error.

### 2026-09-10 — Cập nhật trạng thái Vấn đề 6 / W07: transition và durable boundary

- **Trạng thái:** `complete`.
- **Lỗi đã sửa:** E tại portal từng gọi thẳng simulation travel, nên save failure không dùng rollback transaction của presentation. E portal giờ chỉ resolve target trong Application; `Arena.TravelToRegion` là owner duy nhất của mutation/save/rollback. UI không báo success trước durable commit.
- **Lỗi đã sửa:** `ClearActiveForMapChange` xóa Ally runtime nhưng giữ `Summoned` state/UID, tạo snapshot summon treo sau transition. Canonical transition nay gọi `RecallCanonical` trước khi remove actor, giữ HP ratio/vitality và chuyển Soul sang Ready; possession kết thúc qua cooldown canonical đã có.
- **Evidence:** `GameApplication.NearbyCanonicalPortalTarget`, `Arena.TravelToRegion`, `SummonSystem.ClearActiveForMapChange/RecallCanonical`; build compile-only pass, 0 warning / 0 error.

### 2026-09-10 — Vấn đề 6 / W09: fog/minimap và marker asset

- **Trạng thái:** `in_progress`.
- **Đã xác nhận đúng:** Fog reveal 12 tile, persist per-region (`VisitedTiles`) và `HudMinimap` lọc object/Soul marker bằng `WasCanonicalTileVisited`; load/travel không lộ marker vùng chưa reveal.
- **Chưa hoàn thành:** `Arena` vẫn dùng circle/text technical fallback khi asset hoặc animation species thiếu. Đây không thể được tính là asset hoàn chỉnh; marker canonical (`ui.minimap_marker`, `ui.quest_marker`) và asset adapter thuộc dependency phần6/7 chưa hoàn tất. Không thay fallback thành artwork giả hoặc sửa catalog asset trong khi export chưa được chốt.
- **Bước tiếp theo:** Hoàn thành phần6 canonical asset adapter + mapping manifest, sau đó thay marker/fallback bằng asset ID có version/hash và chạy user visual acceptance trong game.

### 2026-09-10 — Phần 2 / bước 4: single V2.5 documentation entry point và historical boundary

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Bổ sung `docs/V2.5/README.md` làm điểm vào duy nhất cho authority, evidence và validation boundary. `docs/README.md` trỏ về đây. Các tài liệu prototype `ARCHITECTURE`, `MAP_SYSTEM`, `GAME_TERMINOLOGY`, `GAME_TERMINOLOGY_VN`, `PERFORMANCE_BUDGET` đã được giữ lại với header historical; không còn được dùng để thay đổi rule/balance/save/asset của V2.5.
- **Evidence:** `docs/V2.5/README.md`, `docs/README.md`; không xóa tài liệu vendor/license hay historical record.

### 2026-09-10 — Phần 2 / bước 5: current evidence index, không rewrite historical plan

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Thêm `docs/V2.5/CURRENT_IMPLEMENTATION_STATUS.md` làm một điểm đọc current status. File này liên kết authority, progress journal, trạng thái từng phần, giới hạn validation và dependency asset hiện hành. `work-items.json`, `implementation-tasks-4-8.json` và `source-audit.md` được giữ nguyên là planning/audit snapshot lịch sử; không có hai nguồn cùng tự nhận là current status.
- **Evidence:** `docs/V2.5/CURRENT_IMPLEMENTATION_STATUS.md`; các audit cũ vẫn truy vết được đầy đủ.

### 2026-09-10 — Phần 2 / bước 6: canonical Godot 4.5.2 Compatibility baseline

- **Trạng thái:** `not_complete`.
- **Lý do:** Môi trường hiện chỉ có Godot .NET 4.7.2 và project hiện pin `Godot.NET.Sdk/4.7.2`, feature `Godot 4.7 C# Forward Plus`. Không có executable/template Godot .NET 4.5.2 để compile/export baseline; không được suy diễn compatibility từ export 4.7.2.
- **Bước tiếp theo:** Khi environment có Godot .NET 4.5.2 và export templates tương ứng, kiểm tra project configuration/API compatibility, chạy compile-only và export debug; ghi kết quả độc lập, không chạy gameplay acceptance tự động.

### 2026-09-10 — Phần 2 / bước 7: compile và package bằng toolchain hiện có

- **Trạng thái:** `complete` (giới hạn Godot 4.7.2).
- **Đã thực hiện:** `dotnet build solo_vs_mortal_godot.csproj --no-restore` pass với 0 warning/0 error. Godot 4.7.2 export-debug đã tạo `build/v25-4.7.2/SoloVsMortal.exe`, `SoloVsMortal.console.exe`, `SoloVsMortal.pck`.
- **Giới hạn:** Artifact xác nhận đóng gói kỹ thuật ở 4.7.2, không thay thế baseline 4.5.2 hoặc 18 manual acceptance cases của user.

### 2026-09-10 — Phần 6 / bước 6.01: dependency inventory và danh sách bảo vệ asset

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Tạo `docs/V2.5/ASSET_DEPENDENCY_INVENTORY_V2.5.md`. Inventory ghi 10,700 requirement V2.5 (beta_01: 1,271), snapshot file/byte của `assets`, `spritesheets`, `spriteframes`, `build`, runtime caller hiện hành, legal/vendor retain và delete gate theo từng nhóm. Không có thao tác delete nào.
- **Đã khóa bảo vệ:** Bốn PNG Skeleton static đã được user cho phép tái sử dụng được copy nguyên vẹn vào `assets/v2.5/skeleton-static-integration-v001/`. Raw/master/normalized/preview Art, source export và license không nằm trong cleanup scope.
- **Evidence:** `ASSET_DEPENDENCY_INVENTORY_V2.5.md`, `data/v2.5/asset-catalog.v2.5.json`, bốn SHA-256 từ source `skeleton-static-integration-v001/asset-map.json`.

### 2026-09-10 — Phần 6 / bước 6.02: canonical asset adapter và migration presentation từng phần

- **Trạng thái:** `in_progress`.
- **Đã thực hiện:** Thêm `CanonicalAssetCatalog`: parse strict schema, reject unknown field/AssetId/path/hash/frame rectangle sai, đọc metadata file/hash/frame rect/duration/pivot/clip/direction/rank/sockets/layering/QA. `Arena` chỉ query catalog V2.5 cho map/world/pickup/actor; ID không có art hiển thị `MISSING: <AssetId>` thay vì dùng art prototype hoặc Skeleton sai species. Skeleton rank01 pickup/enemy/ally static dùng frame/pivot/layer riêng; player đã bỏ đường sprite-sheet cũ và hiển thị missing marker cho đến khi có package approved. `HudMinimap` bỏ hard-coded Kenney path và dùng primitive fallback trong lúc chưa có marker UI canonical.
- **Đã xác minh:** `dotnet build solo_vs_mortal_godot.csproj --no-restore` pass, 0 warning/0 error. Không chạy gameplay.
- **Chưa complete:** Catalog mới chỉ có 4 static Skeleton AssetId; 1,267 requirement beta còn chưa có approved export. Chưa có player clips/directions, actor animation, world/tile, equipment, UI marker, socket/layer assets đầy đủ; không thể chuyển mọi caller sang art hoàn chỉnh hoặc xóa nhóm cũ.
- **Bước tiếp theo:** Art export thêm package versioned; Code thêm entry hash-verified vào catalog, migrate ID tương ứng và giữ explicit missing list. Chỉ khi một group không còn caller và license/provenance rõ ràng mới thực hiện 6.03 delete theo group.

### 2026-09-10 — Vấn đề 6 / W09: tiếp tục sau asset adapter foundation

- **Trạng thái:** `in_progress`.
- **Đã cập nhật:** Runtime không còn tự lấy art prototype hoặc Kenney ring cho visual canonical đã chạm tới; fog/visited marker filter vẫn giữ đúng. Marker minimap/object còn là primitive/magenta technical fallback vì `ui.minimap_marker`, `ui.quest_marker` và world export canonical chưa tồn tại.
- **Chưa complete:** User cần review in-engine Skeleton static và approve art package có marker/world/UI trước khi fallback được thay thế; không thể gọi visual acceptance này là đã pass bằng compile-only.

### 2026-09-10 — Phần 6 / bước 6.03: physical cleanup obsolete asset/code

- **Trạng thái:** `not_complete`.
- **Rà soát dependency:** `GameDefinitions.Load` vẫn bootstrap `data/asset-manifest.json` và `characterAnimations.json`; `tools/export_windows.ps1` vẫn package asset manifest. Đây là dependency compatibility/bootstrap có thật, không phải file mồ côi có thể xóa. `assets`, `spritesheets`, `spriteframes` còn chứa old runtime/vendor/generated material và chưa có catalog V2.5 replacement đầy đủ.
- **Quyết định:** Không xóa file/group nào ở bước này. Xóa bây giờ sẽ làm canonical bootstrap/package fail hoặc mất legal/provenance, trái điều kiện complete. Inventory đã ghi replacement ID/delete gate để cleanup sau không phải suy đoán theo wildcard.
- **Bước tiếp theo:** Sau mỗi Art export được approve, migrate AssetId/caller tương ứng, remove dependency legacy đã được chứng minh không còn dùng, kiểm tra build/export và append danh sách file xóa cùng replacement ID. Chưa có bằng chứng để đóng phần 6.

### 2026-09-10 — Phần 6 / bước 6.03: continuation — dead presentation API cleanup

- **Trạng thái:** `in_progress`.
- **Đã thực hiện:** Sau khi Arena chuyển qua `CanonicalAssetCatalog`, xóa API presentation cũ không còn bất kỳ caller nào: `GameApplication.HasSpeciesAnimation/HasAsset/Asset/MonsterAnimation/PlayerAnimation` và snapshot descriptors `AssetSnapshot/AnimationClipSnapshot/PlayerAnimationClipSnapshot`. Không còn raw path expansion hoặc hard-coded player/monster scale ở canonical presentation caller.
- **Giữ lại có chủ ý:** `GameDefinitions.Load`, `AssetDefinitions`, `CharacterAnimationDefinitions`, `data/asset-manifest.json` và `characterAnimations.json` vẫn là bootstrap/compatibility dependency hiện hữu; không gắn nhãn dead code hoặc xóa vật lý khi chưa refactor bootstrap sang definition/map canonical hoàn toàn.
- **Evidence:** static caller scan; `dotnet build solo_vs_mortal_godot.csproj --no-restore` pass, 0 warning/0 error. Physical cleanup vẫn `not_complete` cho tới khi replacement và dependency delete gate đầy đủ.

### 2026-09-10 — Phần 6 / bước 6.02: continuation — export inclusion verification

- **Trạng thái:** `in_progress`.
- **Đã thực hiện:** `tools/export_windows.ps1` nay copy riêng `assets/v2.5` và yêu cầu `data/v2.5/asset-catalog.v2.5.json`; không package kho production Art. Godot 4.7.2 `--export-pack` tạo `build/v25-4.7.2/SoloVsMortal-current.pck`; static inspection xác nhận PCK có catalog và `soul.skeleton.rank01.enemy.south.png`.
- **Giới hạn:** Đây chỉ xác minh included resource/package. Không có gameplay launch, in-engine QA hay claim baseline 4.5.2. `project.godot`/preset hiện còn viewport 1280×720 trong khi style-lock V2.5 yêu cầu native 640×360; chưa được đổi vì HUD/WorldMap hiện dùng absolute layout 1280 và cần migrate đồng bộ để không cắt UI. Mục này vẫn open cho phase layout/pixel-snap và asset package tiếp theo.

### 2026-09-10 — Phần 3 / bước 1–5: fixed-tick combat static trace và deterministic ordering

- **Trạng thái:** `complete` (static implementation review).
- **Đã thực hiện:** Tạo `docs/V2.5/COMBAT_RUNTIME_TRACE_V2.5.md`, trace authority tick order qua `GameSession.FixedStep` và `V25CombatCoordinator`. Audit phát hiện các path cast/projectile/cooldown/knockback/DoT từng phụ thuộc dictionary/list iteration không khai báo; đã đặt sort key deterministic cho từng path. Damage tiếp tục resolve defense lúc hit, snapshot offense lúc release; death/revoke cancel unreleased cast, projectile released giữ snapshot.
- **Evidence:** `V25CombatCoordinator`, `V25StatusStore.Tick`, `COMBAT_RUNTIME_TRACE_V2.5.md`; `dotnet build solo_vs_mortal_godot.csproj --no-restore` pass, 0 warning/0 error.
- **Giới hạn:** Không đánh dấu user acceptance. Same-tick boss/player death, projectile/LOS, dodge/collision, combat save/load và case 7–9/10/15–18 phải do user kiểm trong game.

### 2026-09-10 — Phần 3 / bước 6–7: acceptance map và build boundary

- **Trạng thái:** `in_progress`.
- **Đã thực hiện:** Combat map liên kết requirement→symbol→manual verification trong `COMBAT_RUNTIME_TRACE_V2.5.md`; build compile-only đã pass sau sửa.
- **Chưa complete:** 18 acceptance cases là user-owned theo `AGENTS.md`; chưa có kết quả manual playtest. Phần 3 vì vậy vẫn `in_progress`, không được đổi status top table.

### 2026-09-10 — Phần 4 / bước 4.12: canonical-flow isolation of legacy Soul mechanics

- **Trạng thái:** `complete` (canonical runtime isolation; physical removal thuộc phần 6).
- **Đã thực hiện:** Khi canonical V2.5 active, `GameSession` không khởi tạo `DevourSystem`, `EssenceSystem` hoặc `BloodlineSystem`; các property được ghi rõ legacy-compatibility-only. `ProgressionSystem.Craft/UsePlayerStatPill` fail closed trong canonical mode. `Arena` dùng canonical branch của `SoulLinks` (không expose Devour); Soul/Inventory/Quest V2.5 vẫn dùng system riêng.
- **Giữ lại:** File/class legacy và legacy save payload vẫn giữ cho namespace v1–v6, không là route canonical. `GameDefinitions` legacy bootstrap chưa refactor hoàn toàn nên physical delete phải chờ phần 6 replacement/dependency gate.
- **Safety note:** Không thêm guard mới trực tiếp vào legacy `CaptureSave/RestoreSave` vì automatic approval review từ chối thay đổi đó với lý do có thể chặn save/restore và làm mất tiến trình. Không có thay đổi nào vào hai API này; canonical route hiện dùng `CaptureCanonicalSave/RestoreCanonicalSave` riêng.
- **Evidence:** `GameSession`, `ProgressionSystem`, `GameApplication.SoulLinks`; build compile-only pass, 0 warning/0 error.

### 2026-09-10 — Phần 4 / bước 4.07–4.08: durable boundary for Soul/Spirit/Possession state changes

- **Trạng thái:** `complete` (implementation/static review).
- **Lỗi đã sửa:** Canonical durability flag trước đây chỉ subscribe `SoulSummoned` và `PossessionStarted`. Recall, Ally disperse/recover, end Possession và Player death có thể chờ autosave 10 giây thay vì đi qua durable boundary.
- **Đã thực hiện:** `GameSession` giờ mark canonical durable commit cho `SoulUnsummoned`, `SoulDispersed`, `SoulRecovered`, `PossessionEnded` và `PlayerDefeated`, cùng các event đã có. Arena nhận flag trong physics loop và commit envelope hiện hành trước khi tiếp tục; manual save vẫn capture đúng mid-recovery/mid-cooldown state khi user yêu cầu.
- **Evidence:** `GameSession._canonicalDurabilitySubscriptions`, `SummonSystem.RecallCanonical/OnAllyDefeated`, `PossessionSystem.EndCanonical`; build compile-only pass, 0 warning/0 error. User vẫn cần manual check zero-crossing, same-tick lethal/recall và reload trong real Godot.

### 2026-09-10 — Phần 7 / bước 7.01: reconcile logical Slice scope

- **Trạng thái:** `complete` cho scope reconciliation; Phần 7 vẫn `not_complete` vì chưa có art approval, export đủ dependency hoặc in-engine QA.
- **Đã thực hiện:** Đối chiếu `art/production/catalog.json` với `art/production/slice-parts.json`. Catalog có đúng 161 AssetId với `slice01=true` (101 `generated`, 4 `needs_rework`, 56 `not_generated`); worklist 49 part có 158 AssetId unique, không trùng và toàn bộ đều nằm trong catalog. Chốt membership theo hash bằng `data/v2.5/slice-01.scope-lock.v2.5.json`, scope version `slice-01.reconciled.2026-09-10`.
- **Đã giải thích chênh lệch 3 ID:** `player.base.idle.s` là candidate `needs_rework` bị loại khỏi generation worklist; `soul.skeleton.rank01.enemy.south` và `soul.skeleton.rank01.ally.south` là static Skeleton hướng Nam đã được user cho phép tái sử dụng, không phải animation task. `pickup` và `banner.icon` reuse nằm trong `ART-34`, nên không tạo thêm chênh lệch.
- **Approval boundary:** `generated`/technical QA/reuse permission không được coi là user approval. Tại snapshot, 105 entry có output record, 105 technical QA, 5 visual QA, 5 frame-isolation QA, 1 motion QA và 0 in-engine QA. Tài liệu `SLICE_SCOPE_RECONCILIATION_V2.5.md` chốt trạng thái `pending_user_review → approved|needs_rework|rejected` cho output mới và yêu cầu mỗi rework có version/hash mới.
- **Evidence:** `docs/V2.5/SLICE_SCOPE_RECONCILIATION_V2.5.md`, `data/v2.5/slice-01.scope-lock.v2.5.json`; không generate, không tự approve mỹ thuật và không thay đổi catalog Art gốc trong bước này.
- **Bước tiếp theo:** 7.02 dispatch theo 158 ID production-worklist đã khóa, giữ ba catalog-only ID ở handling đã nêu; trước assemble export phải hoàn tất metadata/file/hash và user decision cho từng ID required.

### 2026-09-10 — Phần 7 / bước 2: audit file, hash, manifest và decision hiện hành

- **Trạng thái:** `in_progress`.
- **Đã hoàn thành phần kỹ thuật:** Kiểm 161 ID locked với filesystem Art. Có 105 output record; cả 105 file, SHA-256 và manifest SHA-256 đều khớp, không thiếu file/manifest. Chạy read-only technical validator trên 18 manifest chứa các output Slice: 18/18 pass. `SLICE_OUTPUT_AUDIT_V2.5.json` giữ snapshot từng AssetId, bao gồm trạng thái output/review/evidence hiện hành.
- **Đã xác định decision chính xác:** 4 Player idle S/W/E/N đang `needs_rework` theo decision ledger của user; 101 output khác vẫn `generated`/chờ decision. Bốn static Skeleton chỉ có quyền reuse đã được user cho phép, không là visual hoặc in-engine approval. Không có asset nào đủ điều kiện integration export.
- **Chưa hoàn thành / lý do:** Không thể tự ghi user decision `approved`, `needs_rework` hay `rejected` cho 101 output chờ review. Đây là quyền của user theo approval policy, nên bước này còn mở cho tới khi các decision hash-pinned được ghi.
- **Evidence:** `docs/V2.5/SLICE_OUTPUT_AUDIT_V2.5.json`, `art/production/catalog.json`, `art/production/user-reviews.json`; không thay output, hash hoặc review ledger trong audit.

### 2026-09-10 — Phần 7 / bước 3: thứ tự microtask theo dependency

- **Trạng thái:** `complete`.
- **Đã thực hiện:** Tạo `docs/V2.5/SLICE_DISPATCH_PLAN_V2.5.md`. Plan tách trạng thái catalog (output/review) khỏi `slice-parts` part scheduling, nên 48 part `pending` không bị hiểu sai là chưa có file. Queue A gồm 7 batch world độc lập `ART-43`–`ART-49` với 40 ID; Queue B yêu cầu làm lại cả 4 Player idle S/W/E/N trước, sau đó dừng 16 Player move/attack/hit/death ở gate user decision để không nhân lỗi identity/size/clothing sang clip phụ thuộc.
- **Đã khóa cách tiếp tục:** Mỗi batch có delivery/record/technical-check riêng, failure chỉ block AssetId/part tương ứng và các batch world độc lập vẫn tiếp tục. Không dùng raw contact sheet làm atlas, không overwrite revision cũ, không tự promote output thành approved.
- **Evidence:** `SLICE_DISPATCH_PLAN_V2.5.md`, `slice-01.scope-lock.v2.5.json` (`scopeMembershipSha256=994ebfd3d946d506a085452da40523ce141cd5b22ab020bdd95d301f15143df6`).

### 2026-09-10 — Phần 7 / bước 6: technical animation/asset QA baseline

- **Trạng thái:** `in_progress`.
- **Đã hoàn thành:** Validator pass 18/18 manifest hiện có. Animation preview hiện dùng timestamp elapsed ms và duration array trong manifest, không dùng counter frame-rate dependent; snapshot có 28 Skeleton clips, Idle 4×200 ms và Move 6×100 ms. Kiểm data hiện tại không phát hiện frame nào chạm gutter 2px.
- **Chưa hoàn thành / lý do:** Đây không xác nhận pixel ownership của slash/effect, motion/identity hoặc visual phù hợp; 0 output có in-engine QA. Mỗi output mới trong Queue A/B phải có kiểm frame/pivot/gutter/alpha/palette/hash và animation phải có frame-step/onion/timing evidence trước khi có thể chuyển sang integration-ready.

### 2026-09-10 — ISSUE-01: Godot .NET 4.7.2 implementation baseline

- **Trạng thái:** `complete` — user đã quyết định Godot .NET 4.7.2 là implementation/toolchain baseline chính thức cho V2.5. Quyết định này không thay gameplay mechanics, balance, save rules, AssetIds, style rules, 18 acceptance cases hoặc hash-pinned authority.
- **Cấu hình thực tế:** `solo_vs_mortal_godot.csproj` pin `Godot.NET.Sdk/4.7.2`; `project.godot` advertise `4.7`, `C#`, `Forward Plus`; preset Windows trong `export_presets.cfg` dùng `gl_compatibility`. Khác biệt renderer editor/export được ghi nhận, không đổi trong ISSUE-01.
- **Evidence toolchain/package:** executable `C:/Users/levan/Downloads/Godot/Godot.exe` báo `4.7.2.stable.mono.official.ed1daf0bf`; templates `C:/Users/levan/AppData/Roaming/Godot/export_templates/4.7.2.stable.mono/` có Windows debug/release x86_64. `dotnet build solo_vs_mortal_godot.csproj --no-restore` pass 0 warning/0 error. Sau restore target `win-x64` (sửa `NETSDK1047` assets thiếu runtime target), `dotnet publish` và Godot `--export-release` exit 0, tạo `build/issue-01-4.7.2/SoloVsMortal.exe` và `.pck`. Godot có warning root-certificate/editor-settings không block export; không chạy gameplay/harness.
- **Tài liệu/evidence đã reconcile:** `AGENTS.md`, `docs/README.md`, `docs/V2.5/README.md`, `CURRENT_IMPLEMENTATION_STATUS.md`, `work-items.json`, `implementation-tasks-4-8.json`, và sections 1–11 của báo cáo này nay dùng baseline 4.7.2; `source-audit.md` giữ evidence cũ kèm supersession note. `C:/ws/asset-production_system/game_spec/README.md` đổi prompt implementation hiện hành; `deliverables/review-beta/REVIEW.md` giữ review lịch sử kèm supersession note.
- **Historical/authority safety:** các entry 4.5.2 trước đây trong Section 12 vẫn là historical evidence hợp lệ tại thời điểm ghi nhưng bị supersede cho implementation tương lai. `data/v2.5/spec-lock.json` không đổi. Hash-pinned `C:/ws/asset-production_system/Solo_vs_Mortal_Gameplay_System_V2.5.md` vẫn giữ wording baseline cũ và SHA-256 `402c93eff6879ec25076aeea10c3d3ff4ec75580a518d00d89ac8b74f93d1b53`; không regenerate authority chỉ để đổi toolchain.
- **Bước tiếp theo:** giữ executable/templates 4.7.2 và lặp lại baseline build/export khi toolchain/package configuration đổi. Gameplay acceptance 1–18 vẫn hoàn toàn thuộc user; next implementation gate vẫn là Part 6 asset mapping/user approval.

### 2026-09-10 — Reconcile active asset-production workspace path

- **Trạng thái:** `complete`.
- **Đã cập nhật current-state:** active workspace path là `C:/ws/asset-production-system`; policy, current status, execution plans, dependency inventory, runtime asset-catalog provenance và live helper configuration dùng form hyphen.
- **Historical safety:** các entry cũ dưới Section 12 không bị sửa hoặc xóa; chúng giữ nguyên path được ghi tại thời điểm evidence/journal được tạo. Audit snapshots, scope locks, generated provenance và review/error records cũng được giữ nguyên để không làm sai hash hoặc provenance lịch sử.

### 2026-09-10 — ISSUE-04: Closure Part 4 — Soul Loop

- **Trạng thái:** `ISSUE-04 CODE_COMPLETE — READY_FOR_USER_ACCEPTANCE`.
- **Scope/audit:** Đọc policy/repository records, authority Markdown và bundle hash-pinned. SHA-256 của toàn bộ sáu entry trong `data/v2.5/spec-lock.json` khớp `C:/ws/asset-production-system`; không đổi spec-lock hoặc gameplay authority. Matrix requirement→owner→producer→caller→mutation→persistence→validation→restore→Application/UI→manual case nằm tại `docs/V2.5/ISSUE-04_SOUL_LOOP_CLOSURE_MATRIX.md`.
- **Confirmed gaps/fixes:** Bổ sung producer runtime `AllySpeciesKills`, `KillsWhilePossessed` và `ClaimOtherSyncSources` (hold E 3s, shrine, move/damage/death/release cancel) cho Sync; Summon dùng role body radius thay hardcode và bị chặn ở possession transition; Application/Arena có command thật cho SummonAll, RecallAll, focus/mode và end possession; Recall chuyển cooldown của Ally UID sang Ready species và restore chỉ cho phép row Ready không-empty; tutorial reward bị khóa Skeleton Level1; drop/pity và Sync progress yêu cầu WAL commit ngay thay vì đợi autosave.
- **Runtime callers/persistence:** `GameSession.FixedStep` → Soul/Sync/Summon/Spirit/Ally/Possession; `Arena` → `GameApplication` → Simulation. Capture/validate/staged restore bao phủ ownership, Density ledger/pending/proofs, pickups/pity/receipts, Sync awards/progress/milestones, Banner receipts, Spirit carries, summon/Ally state/cooldowns, possession snapshot/cooldowns/transition. `V25SaveStore` giữ exact envelope/transaction retry.
- **Legacy:** canonical `GameSession` không construct Devour/Essence/Bloodline; legacy files/payload giữ cho compatibility v1–v6/cleanup Part6, không có caller canonical.
- **Build/export evidence:** `dotnet build solo_vs_mortal_godot.csproj --no-restore` succeeded, 0 errors, warning `NU1900` (NuGet vulnerability metadata unreachable). `dotnet publish solo_vs_mortal_godot.csproj --no-restore --configuration Release --runtime win-x64 --self-contained false` succeeded with the same warning. Godot `4.7.2.stable.mono.official.ed1daf0bf` Windows Desktop export was attempted twice; internal .NET publish reported failure although command-line publish succeeded. Đây là package/toolchain follow-up, không phải gameplay acceptance hay Part4 source blocker.
- **Asset result:** `NO_ASSET_APPROVAL_REQUIRED_FOR_ISSUE_04 — visual asset production/in-engine visual acceptance belongs to Part 6/7/W09.`
- **User handoff:** 18 canonical cases, exact actions/expected results và screenshot/save-reload evidence khi fail được map trong closure matrix. Agent không tạo/chạy gameplay test, parity runner, soak hoặc headless acceptance. User là sole gameplay tester theo `AGENTS.md`.

### 2026-09-10 — Godot self-contained Windows export verification

- **Trạng thái:** `complete` (compile/package evidence only; không phải gameplay acceptance).
- **Reproduction:** Godot 4.7.2 verbose export cho thấy exact publish command là `dotnet publish solo_vs_mortal_godot.csproj -c ExportRelease -r win-x64 --self-contained true -v normal`, kèm `GodotTargetPlatform=windows`, Godot build logger, và temp publish output.
- **First real MSBuild error:** `MSB1029`, logger không thể tạo `msbuild_log.txt` khi export process bị sandbox chặn quyền ghi AppData. Đây là permission boundary của toolchain host, không phải source hoặc project configuration error. Export destination cũng phải được tạo trước theo CLI contract.
- **Resolution/verification:** chạy Godot ngoài sandbox với AppData logging bình thường, chờ process thực kết thúc. Process exit `0`; log có publish và packing hoàn tất, không có MSBuild errors (chỉ hai warning `NU1900` về NuGet vulnerability metadata). Artifacts: `build/issue-04-export-verified/solo_vs_mortal_godot.exe` (109,513,728 bytes) và `.pck` (278,758,156 bytes).
- **Scope safety:** không thay gameplay code, authority, renderer hoặc asset mapping; không chạy gameplay scene, automated runner, parity hay soak test.

### 2026-09-11 — Asset Integration Trial v001 (source snapshot 2026-09-10)

- **Trạng thái:** `ASSET_INTEGRATION_TRIAL_PARTIAL_READY_FOR_USER_VISUAL_REVIEW`.
- **Đã thực hiện:** Freeze đúng 161 AssetId từ catalog `slice01=true`; `slice-parts` có 158 work ID và ba catalog-only ID đã được reconcile trước đó. Runtime-trial cutoff `2026-09-10T11:33:56.999777+00:00` cho phép 145 generated/needs-rework output cùng bốn Skeleton static reuse output. Cả 149 PNG được copy vào `assets/v2.5/asset-integration-trial-v001/`, có source/copy SHA-256, source manifest, frame rect/duration/pivot/role/QA/review state trong `provenance.json`; catalog canonical vẫn `complete=false`.
- **Presentation:** Player idle/move/attack theo hướng canonical; Ally idle/move/attack theo snapshot/movement delta và bỏ green tint cũ; pickup/Banner icon, icon Sword khi equipped, prop Ash Graves exact ID và renderer mask NESW presentation-only đã được nối. Enemy dùng các attack/hit/death clip được cấp quyền và marker explicit cho clip không có. Viewer development-only mở từ Feature panel để xem toàn bộ 149 asset ở timing metadata, không gọi Simulation hoặc chạm save. Sword attachment/weaponpose không overlay vì thiếu tọa độ socket thực đo.
- **Source gap giữ nguyên:** 12 Enemy ID `idle.s/w/e/n`, `move.s/w/e/n`, `attack.w/e/n`, `hit.n` đang `integration_ready`, không thuộc base states của runtime-trial; không copy/thay thế. `soul.skeleton.rank01.enemy.attack.s` vẫn `needs_rework`; không sửa hay tăng approval. Không có output sau cutoff, technical/file/hash/manifest/metadata failure.
- **Evidence:** provenance/static validator pass 161 unique scope, 149 source/copy SHA + 23 manifest, frame bounds/count/durations và no-path-escape; `dotnet build solo_vs_mortal_godot.csproj --no-restore -v:minimal` pass 0 warning/0 error. Godot 4.7.2 export log kết thúc `[ DONE ] savepack`, stderr rỗng, PCK có Trial provenance/imported paths; artifact `build/asset-integration-trial-v001/solo_vs_mortal_godot.exe` (109,513,728 bytes) + `.pck` (279,539,536 bytes). Host wrapper detach khỏi Godot child dài hạn trước final exit code, nên evidence export được ghi là completed packing/artifact, không assert exit 0. Đây chỉ là evidence kỹ thuật, không gameplay/visual/in-engine approval. Xem `ASSET_INTEGRATION_TRIAL_2026-09-10.md` để có manual visual-review route và giới hạn.
- **Scope safety:** không đổi gameplay/balance/Soul/Inventory/save/spec-lock, không ISSUE-05/Part5, không 640×360/renderer change, không cleanup, commit hoặc push. Part6/7, W09 và 18 manual gameplay cases vẫn open.
