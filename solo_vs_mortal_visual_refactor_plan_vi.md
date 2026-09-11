# Solo vs Mortal — Kế hoạch Refactor Visual / Asset / World Presentation

**Đối tượng audit:** `MeoDaoSi/solo_vs_mortal_godot` @ `main` (commit gần nhất đã audit: `dfb79e9`, 2026-09-11)  
**Nguồn asset liên quan:** `MeoDaoSi/asset-production-system`  
**Phạm vi:** animation (hoạt ảnh), actor/object scale (tỉ lệ hiển thị nhân vật/vật thể), readability (độ dễ nhìn), world/map composition (bố cục thế giới/map), runtime asset integration (tích hợp asset vào game khi chạy).  
**Ngoài phạm vi:** thay đổi gameplay V2.5, save schema (cấu trúc save), combat balance (cân bằng chiến đấu), renderer baseline, simulation authority (quyền sở hữu logic mô phỏng).

---

# 0. Progress Tracking — Theo dõi tiến độ

> **Quy ước trạng thái**
>
> - `[ ] TODO` — chưa bắt đầu.
> - `[-] IN PROGRESS` — đang làm.
> - `[x] COMPLETE` — đã hoàn thành implementation và có evidence (bằng chứng kỹ thuật).
> - `[!] BLOCKED` — đang bị chặn.
> - `[?] USER REVIEW` — agent đã xong phần kỹ thuật, đang chờ user kiểm tra trực quan trong game.
>
> Agent phải cập nhật bảng này sau mỗi Work Item. Không được đánh `COMPLETE` chỉ vì `dotnet build` pass nếu task đó cần visual acceptance (nghiệm thu hình ảnh).

## 0.1 Tổng tiến độ theo Phase

| Phase | Nội dung | Trạng thái | Evidence / Ghi chú |
|---|---|---|---|
| Phase 0 | Freeze & Measure — audit asset/presentation hiện tại | [ ] TODO | |
| Phase 1 | Repair Animation Assets — sửa animation thật | [ ] TODO | |
| Phase 2 | World Scale Contract — chốt hệ thống tỉ lệ thế giới | [ ] TODO | |
| Phase 3 | Readability — làm rõ Player/quái/vật thể | [ ] TODO | |
| Phase 4 | Semantic World Layers — refactor cách build map | [ ] TODO | |
| Phase 5 | Y-sort / Occlusion / Anchoring | [ ] TODO | |
| Phase 6 | Camera / Viewport Composition | [ ] TODO | |

## 0.2 Tổng tiến độ theo Work Item

| Work Item | Nội dung | Trạng thái | Commit / PR / Evidence |
|---|---|---|---|
| A | Asset Truth Audit | [ ] TODO | |
| B | Player Animation Replacement | [ ] TODO | |
| C | World Scale Contract + VisualScaleLab | [ ] TODO | |
| D | Runtime Scale Application | [ ] TODO | |
| E | Semantic Ash Graves Foundation | [ ] TODO | |
| F | Environment Integration | [ ] TODO | |
| G | Readability Polish | [ ] TODO | |

---

# 1. Kết luận audit tổng thể

Screenshot hiện tại không bị lỗi bởi một bug Godot đơn lẻ. Có **4 nhóm lỗi độc lập đang trộn vào nhau**:

1. **Animation source data (dữ liệu frame nguồn) không tạo ra chuyển động thật.** Runtime khai báo 6 frame cho mỗi hướng di chuyển của Player, nhưng 6 vùng frame thực tế đang có pixel giống nhau. Godot vẫn đang chạy animation đúng timeline, nhưng vì nội dung frame giống nhau nên Player trông giống một ảnh tĩnh đang trượt trên map.

2. **World scale (tỉ lệ thế giới) hiện được quyết định bởi `intendedFootprint` nhập tay theo từng asset**, thay vì một hệ thống vật lý tương đối thống nhất. Player, grave/pillar/statue, shrine, portal, enemy và prop vì vậy không tạo ra một hierarchy (thứ bậc kích thước) hợp lý.

3. **Readability (độ dễ nhìn) hiện đang được xử lý theo hướng “phóng sprite lớn hơn”**, trong khi vấn đề thật còn nằm ở contrast (độ tương phản), silhouette separation (tách hình dáng), local background contrast (tương phản nền cục bộ), shadow, prop density (mật độ vật thể), occlusion (che khuất), Y-sort và camera framing.

4. **Map hiện vẫn đang được procedural paint (vẽ thủ tục bằng code)** bằng màu phẳng + một số texture 32×32 rải thưa + Wang mask cho road/ruins. Vì vậy map vẫn đọc giống các ô tile ráp lại, thậm chí xuất hiện các vùng màu lớn không có nội dung.

Lần refactor tiếp theo **không nên tiếp tục chỉnh từng con số scale hoặc rải thêm tile ngẫu nhiên**.

Cần khóa 3 contract (hợp đồng dữ liệu/thiết kế) rõ ràng trước:

- **Actor Animation Contract** (quy tắc animation của actor): mỗi clip phải có pose thật sự khác nhau, timing đúng và được verify.
- **World Scale Contract** (quy tắc tỉ lệ thế giới): kích thước tương đối theo species/object class, không phụ thuộc canvas PNG.
- **World Composition Contract** (quy tắc bố cục thế giới): terrain được author theo ý nghĩa gameplay/world, không phải rải ô tile ngẫu nhiên.

Sau khi 3 contract này ổn, Godot presentation code mới chỉ làm nhiệm vụ consume (đọc và áp dụng) chúng.

---

# 2. Evidence từ source hiện tại

## 2.1 Animation (hoạt ảnh)

Runtime wiring (cách source đang nối animation) nhìn chung đang đúng:

- `CanonicalAssetCatalog.BuildFrames(...)` tạo `SpriteFrames` và giữ `durationMs` của từng frame.
- `Arena.PlayActorAnimation(...)` chỉ gọi `sprite.Play(...)` khi animation thay đổi hoặc sprite không chạy, tức là **không restart cùng một animation ở mỗi physics frame**.
- Khi input movement khác zero, Player chọn `move_{direction}`.

Nhưng asset audit gần nhất đã chỉ ra:

| AssetId | Declared Frames | Unique Pixel Frames |
|---|---:|---:|
| `player.base.move.s` | 6 | 1 |
| `player.base.move.w` | 6 | 1 |
| `player.base.move.e` | 6 | 1 |
| `player.base.move.n` | 6 | 1 |

=> Lỗi chính của Player animation nằm ở **asset-production output / atlas content**, không phải timer playback của Godot.

Ngoài ra catalog hiện tại vẫn `complete: false`. Một số enemy clips/directions (clip/hướng quái) còn thiếu hoặc chỉ ở trạng thái `integration_trial_authorized`. Sau khi sửa Player, quái vẫn có thể bị static hoặc fallback nếu asset set chưa đủ.

---

## 2.2 Scale (tỉ lệ kích thước)

Code hiện có `PresentationVisualMetrics` với công thức:

```text
UniformScale = min(
    intendedWidth / opaqueWidth,
    intendedHeight / opaqueHeight
)
```

Cách này tốt hơn scale theo canvas 64×64, nhưng **`intendedFootprint` vẫn đang được nhập tay từng asset**.

Ví dụ target hiện tại xấp xỉ:

| Object | Intended visible size |
|---|---:|
| Player move south | 30×55 logical px |
| Pillar / grave | 32×54 |
| Shrine | 88×105 |
| Portal | ~128×130 |

Nghĩa là Player và pillar/grave đang được target gần như cùng chiều cao. Screenshot hiện tại phản ánh đúng dữ liệu này.

Ngoài ra world object còn có:

```text
metricScale * obj.PresentationScale
```

=> Có **2 nguồn sở hữu scale** cùng lúc:
- scale từ `PresentationVisualMetrics`;
- scale từ `PresentationScale` của object instance.

Điều này khiến việc reason (suy luận) kích thước rất khó và dễ phát sinh override ngầm.

---

## 2.3 Readability (độ dễ nhìn)

Dù đã thêm feet shadow (bóng chân) và scale silhouette, Player/quái/vật thể vẫn khó nhìn vì:

- actor và terrain đều thiên dark gray/brown;
- nhiều prop cao gần cùng band kích thước với actor;
- background quanh actor thiếu local contrast;
- nhiều prop có shape/value tương tự nhau;
- foreground/background separation chưa có rule thống nhất;
- asset thiếu/chưa QA làm chất lượng actor không đồng đều;
- lower body dễ chìm vào vùng tối;
- hostility/selection state chưa gắn trực quan đủ mạnh với actor.

=> Đây là **scene readability problem** (vấn đề đọc cảnh tổng thể), không phải chỉ PNG quá nhỏ.

---

## 2.4 Map Composition (bố cục map)

`AshGravesTerrainLayer.DrawChunk()` hiện đang:

1. fill toàn bộ chunk bằng `#20212a`;
2. fill focal clearing bằng màu phẳng `#29242d`;
3. chỉ rải ground texture ở một số ô theo deterministic hash;
4. vẽ road bằng Wang mask;
5. vẽ 3 vùng ruin rectangle hard-code;
6. vẽ outer wall;
7. sau đó `Arena` vẽ thêm một số scenery object.

Điều này giải thích trực tiếp:

- **“map chỉ là các ô vuông ráp lại”** — vì visual grammar (ngôn ngữ hình ảnh) vẫn dựa trên grid 32×32;
- **“một số ô thành vùng màu”** — vì rất nhiều cell chỉ được `DrawRect`, không có texture/material thực.

Không nên sửa bằng cách tăng số lượng random ground patches. Cần thay **terrain composition model**.

---

# 3. Target World Scale Model — Hệ thống tỉ lệ mục tiêu

Không cần đưa literal meters (mét thật) vào physics. Dùng **relative visual height ratio** (tỉ lệ chiều cao nhìn thấy tương đối).

Quy ước:

```text
Adult Human Player = 1.00
```

| Category | Relative visible height vs Player | Giải thích |
|---|---:|---|
| Soul pickup / small loot | 0.25–0.40 | nhỏ hơn đầu gối |
| Debris / low rock | 0.25–0.50 | vật trang trí sát đất |
| Chest | 0.45–0.60 | khoảng ngang eo |
| Small slime | 0.35–0.55 | monster nhỏ |
| Goblin adult | 0.80–1.00 | goblin trưởng thành có thể gần bằng người |
| Skeleton adult | 0.95–1.05 | gần bằng Player |
| Player | 1.00 | mốc chuẩn |
| Goblin chieftain / large goblin | 1.05–1.20 | lớn hơn goblin thường |
| Wolf | shoulder 0.55–0.75 | so theo vai/footprint, không theo canvas |
| Stone grave marker | 1.10–1.40 | cao hơn đầu/vai người |
| Tall statue / ritual pillar | 1.50–2.30 | phải đọc như kiến trúc |
| Shrine | 1.80–2.80 | công trình lớn |
| Major portal / gate | 2.50–3.50 | Player rõ ràng nhỏ hơn |
| Landmark / spire | 2.00–4.00 | có thể cao vượt viewport |

## Rule quan trọng

**Object class (loại vật thể) quyết định intended real-world scale; PNG canvas không quyết định scale.**

`64×64` chỉ là canvas storage (khung lưu ảnh). Player 64×64 và Portal 64×64 không có nghĩa phải hiển thị cùng kích thước.

## Dữ liệu scale mới đề xuất

Ví dụ world object:

```json
{
  "assetId": "world.ash_graves.pillar",
  "class": "architectural_prop",
  "visualHeightRatio": 1.75,
  "groundFootprintTiles": [0.65, 0.45],
  "anchor": "feet_or_base",
  "scaleOverride": null
}
```

Ví dụ actor/species:

```json
{
  "speciesId": "goblin",
  "representation": "enemy",
  "rankRange": [1, 3],
  "visualHeightRatio": 0.90
}
```

Pixel scale phải được derive (tính ra) từ một Player baseline duy nhất.

---

# 4. Detailed Task Breakdown — Breakdown công việc chi tiết

# Phase 0 — Freeze & Measure (khóa trạng thái và đo trước khi sửa)

## Goal
Tạo evidence chính xác để refactor tiếp theo không dựa vào phỏng đoán bằng mắt.

## Source cần audit

- `src/Presentation/Arena.cs`
- `src/Presentation/CanonicalAssetCatalog.cs`
- `src/Presentation/PresentationVisualMetrics.cs`
- `data/v2.5/asset-catalog.v2.5.json`
- `data/v2.5/presentation-visual-metrics.v2.5.json`

## Tasks

- [ ] **P0.1** Export audit table cho mọi AssetId đang được load, gồm:
  - AssetId
  - role / representation / rank / clip / direction
  - source PNG dimensions
  - frame size
  - frame count
  - unique frame hash count
  - opaque bounds từng frame
  - pivot
  - current visual scale
  - final visible height
  - QA state
- [ ] **P0.2** Detect identical frames (frame pixel giống nhau).
- [ ] **P0.3** Detect opaque bounds drift (biên silhouette thay đổi bất thường).
- [ ] **P0.4** Detect pivot drift.
- [ ] **P0.5** Detect actor thiếu direction.
- [ ] **P0.6** Detect actor thiếu `idle/move/attack`.
- [ ] **P0.7** Detect asset thiếu visual metrics.
- [ ] **P0.8** Detect duplicate scale ownership do `PresentationScale`.
- [ ] **P0.9** Tạo `docs/V2.5/VISUAL_ASSET_AUDIT_<date>.md`.
- [ ] **P0.10** Tạo machine-readable JSON report.

### Exit Criteria

- [ ] Có thể trả lời chính xác cho mọi visual asset: file nào, bao nhiêu frame thật, silhouette bao nhiêu px, pivot ở đâu, scale runtime bao nhiêu.
- [ ] Không gọi đây là gameplay test.
- [ ] Agent cập nhật Phase 0 thành `[x] COMPLETE`.

---

# Phase 1 — Repair Animation Assets (sửa asset animation thật)

## 1A. Player Movement

### AssetIds bắt buộc

- `player.base.move.s`
- `player.base.move.w`
- `player.base.move.e`
- `player.base.move.n`

### Asset tasks

- [ ] **P1.1** Regenerate hoặc repair 6 frame mỗi direction sao cho có locomotion cycle (chu kỳ bước đi) thật.
- [ ] **P1.2** Giữ body scale cố định cho cả clip.
- [ ] **P1.3** Giữ foot anchor / ground pivot cố định.
- [ ] **P1.4** Không resize riêng từng frame theo bounding box.
- [ ] **P1.5** Giữ transparent canvas và palette ổn định.
- [ ] **P1.6** Motion phải nhìn thấy ở native scale (kích thước thật trong game), không chỉ preview 3×.
- [ ] **P1.7** Export contact sheet / frame-step preview.
- [ ] **P1.8** Export GIF preview.
- [ ] **P1.9** Chạy identical-frame detection.
- [ ] **P1.10** `uniqueFrames >= 4` với clip 6 frame; tốt nhất 6/6 đều có motion hợp lý.
- [ ] **P1.11** Validate foot vertical drift.
- [ ] **P1.12** Ghi provenance / transformation recipe vào production manifest.

### Gợi ý walk cycle 6 frame

```text
0: contact A
1: recoil/down
2: passing A
3: contact B
4: recoil/down B
5: passing B
```

Không được chỉ làm flame/noise khác nhau trong khi body pose đứng yên.

---

## 1B. Enemy / Ally Animation Completeness

Audit toàn bộ actor hiện có trong beta, ưu tiên Skeleton Rank 1.

Minimum integration set:

- idle × 4 directions
- move × 4 directions
- attack × 4 directions
- hit × 4 directions
- death/disperse khi cần

Mỗi clip thiếu phải được classify:

- `missing_source`
- `generated_not_qa`
- `integration_trial`
- `integration_ready`
- `release_ready`

### Tasks

- [ ] **P1.13** Audit Skeleton enemy Rank 1.
- [ ] **P1.14** Audit Skeleton ally Rank 1.
- [ ] **P1.15** Không silent fallback sang clip/hướng không liên quan.
- [ ] **P1.16** Tạo missing animation matrix.

---

## 1C. Godot Animation Integration Cleanup

### Files

- `src/Presentation/CanonicalAssetCatalog.cs`
- `src/Presentation/Arena.cs`

### Tasks

- [ ] **P1.17** Giữ duration-driven `BuildFrames`.
- [ ] **P1.18** Add catalog validation cho actor animation.
- [ ] **P1.19** Validate minimum frame count theo clip.
- [ ] **P1.20** Validate unique frame-content hash.
- [ ] **P1.21** Validate frame dimensions consistency.
- [ ] **P1.22** Validate pivot family consistency.
- [ ] **P1.23** Tách “clip tồn tại” khỏi “clip được phép dùng trong gameplay”.
- [ ] **P1.24** Thêm debug-only animation overlay:
  - AssetId
  - animation
  - current frame
  - frame count
  - elapsed time
  - scale
- [ ] **P1.25** Không đổi movement speed để che lỗi animation.

### Exit Criteria

- [ ] Player idle hoạt động đúng theo design.
- [ ] Player move có body/leg cycle thật.
- [ ] Player không còn cảm giác một ảnh tĩnh trượt trên nền.
- [ ] 4 hướng đều visual pass.
- [?] User review screenshot/gameplay.
- [ ] Sau khi user accept, Phase 1 = `[x] COMPLETE`.

---

# Phase 2 — World Scale Contract (hệ thống tỉ lệ thế giới)

## 2A. Asset Measurement

Với mọi actor/world object đang thấy trong Ash Graves:

- [ ] **P2.1** Measure opaque body bounds.
- [ ] **P2.2** Identify ground/contact anchor.
- [ ] **P2.3** Classify physical category.
- [ ] **P2.4** Assign relative height ratio vs Player.
- [ ] **P2.5** Record ground footprint riêng.
- [ ] **P2.6** Không dùng visual height làm collision footprint.

---

## 2B. Source-side Data Structure

Refactor `presentation-visual-metrics.v2.5.json` thành hai loại dữ liệu:

### A. Asset Measurement Facts
Dữ liệu đo được tự động:

- canvas
- opaque bounds
- pivot
- alpha bounds
- frame dimensions

### B. World Scale Policy
Dữ liệu design được user approve:

- Player baseline visible height
- category/species ratio
- ground footprint
- rare scale override

### Tasks

- [ ] **P2.7** Split measurement và policy.
- [ ] **P2.8** Refactor `PresentationVisualMetrics.cs`.
- [ ] **P2.9** Add `VisualScaleFor(assetId)`.
- [ ] **P2.10** Add `VisibleHeightFor(assetId)`.
- [ ] **P2.11** Add `GroundFootprintFor(assetId)`.
- [ ] **P2.12** Validate ratio range theo class.
- [ ] **P2.13** Warning/reject nếu `architectural_prop ≈ player` mà không có explicit override.

---

## 2C. Remove Duplicate Scale Ownership

Hiện tại:

```text
metricScale * obj.PresentationScale
```

### Tasks

- [ ] **P2.14** Chọn một owner chính cho physical scale.
- [ ] **P2.15** `PresentationVisualMetrics/WorldScalePolicy` sở hữu scale chính.
- [ ] **P2.16** Instance override chỉ được phép trong khoảng hẹp, ví dụ `0.8–1.2`.
- [ ] **P2.17** Log warning nếu override vượt safe range.
- [ ] **P2.18** Không dùng range arbitrary `0.1–3.0`.

---

## 2D. VisualScaleLab

Tạo debug-only scene:

```text
VisualScaleLab
```

Hiển thị cùng ground line:

- Player
- adult Goblin
- Skeleton
- chest
- grave marker
- pillar/statue
- shrine
- portal

### Tasks

- [ ] **P2.19** Tạo scene.
- [ ] **P2.20** Hiển thị calculated visible height.
- [ ] **P2.21** Hiển thị ratio.
- [ ] **P2.22** Không chỉnh scale bằng gameplay screenshot trước khi Scale Lab pass.

### Exit Criteria

- [ ] Player rõ là human-size.
- [ ] Goblin/Skeleton đọc như creature ngang tầm người.
- [ ] Grave/statue/pillar đọc như architecture.
- [ ] Shrine/portal lớn rõ ràng.
- [ ] Loot/rock/chest nhỏ hợp lý.
- [?] Chờ user approve Scale Lab.
- [ ] Sau approve, Phase 2 = `[x] COMPLETE`.

---

# Phase 3 — Readability (làm rõ Player / enemy / object)

## 3A. Value & Silhouette Separation

Test asset trên:

- darkest Ash Graves ground
- busy ruins/path tile

### Tasks

- [ ] **P3.1** Test Player trên 2 loại background.
- [ ] **P3.2** Test enemy trên 2 loại background.
- [ ] **P3.3** Nếu silhouette chìm, tăng local midtone/highlight vừa đủ.
- [ ] **P3.4** Giữ art direction đã chốt.
- [ ] **P3.5** Không thêm neon outline quanh toàn bộ actor.
- [ ] **P3.6** Enemy và Ally phải khác nhau ở gameplay distance.
- [ ] **P3.7** Weapon silhouette phải tách khỏi torso.

---

## 3B. Runtime Grounding

### Tasks

- [ ] **P3.8** Shadow radius lấy từ ground footprint.
- [ ] **P3.9** Shadow nhỏ và đủ tối, không thành black blob.
- [ ] **P3.10** Shadow draw trước actor.
- [ ] **P3.11** Shadow anchor theo pivot/base.
- [ ] **P3.12** Target ring chỉ hiện khi selected/engaged nếu dùng.

---

## 3C. Local Contrast Clearing

Giảm high-frequency detail quanh:

- Player spawn
- enemy encounter
- Shrine
- Portal/exit
- chest/NPC

### Tasks

- [ ] **P3.13** Implement local detail suppression.
- [ ] **P3.14** Không brighten toàn map.
- [ ] **P3.15** Focal area vẫn giữ biome identity.

---

## 3D. Prop Density Rules

### Tasks

- [ ] **P3.16** Rule khoảng cách tối thiểu giữa tall props.
- [ ] **P3.17** Main road phải clear.
- [ ] **P3.18** Không đặt same-height prop sau Player spawn.
- [ ] **P3.19** Cluster phải có hierarchy: `1 dominant + 1–2 support + low debris`.

### Exit Criteria

- [ ] Trong screenshot gameplay bình thường, nhìn ~1 giây có thể thấy Player.
- [ ] Nhìn ~1 giây thấy enemy.
- [ ] Nhìn ~1 giây thấy landmark chính.
- [ ] Nhìn rõ walkable path.
- [?] User review.
- [ ] Phase 3 = `[x] COMPLETE` sau accept.

---

# Phase 4 — Semantic World Layers (refactor nền/map theo ý nghĩa)

Đây là phase source refactor lớn nhất.

## 4A. Stop using `DrawRect` as Final Terrain

File hiện tại:

`src/Presentation/AshGravesTerrainLayer.cs`

### Tasks

- [ ] **P4.1** `DrawRect` chỉ giữ làm fallback/debug underlay.
- [ ] **P4.2** Không dùng màu phẳng làm normal final terrain.
- [ ] **P4.3** Remove final-path `IsAshTexturePatch(hash)`.
- [ ] **P4.4** Remove hard-coded rectangular ruin patches.

---

## 4B. Semantic Terrain Layers

Target scene structure:

```text
WorldRoot
  GroundBase
  GroundVariation
  Paths
  RuinsFloor
  WallsCliffs
  DecorationBack
  Actors
  DecorationFront
  InteractionMarkers/VFX
```

Giải thích:

- `GroundBase`: nền chính.
- `GroundVariation`: biến thể texture nhẹ.
- `Paths`: đường đi.
- `RuinsFloor`: nền phế tích.
- `WallsCliffs`: tường/vách.
- `DecorationBack`: vật thể nằm sau actor.
- `Actors`: Player/quái/ally, dùng Y-sort.
- `DecorationFront`: vật thể có thể che actor.
- `InteractionMarkers/VFX`: marker và effect.

### Tasks

- [ ] **P4.5** Tạo layer structure.
- [ ] **P4.6** Mỗi layer có responsibility rõ ràng.
- [ ] **P4.7** Terrain không còn do `Arena.cs` tự vẽ lẫn lộn.

---

## 4C. Environment Kit cho Ash Graves

### Ground families

- base ash soil A/B/C
- cracked ash
- dark compact soil
- scorched patch
- low pebble/debris

### Path families

- main worn road
- road edge
- corner
- junction
- T-intersection

### Ruins families

- floor slab
- broken slab
- edge/corner
- ruined wall base
- collapsed stone

### Focal-area families

- shrine floor / rune base
- portal approach
- arena boundary treatment

### Transitions

- ash ↔ path
- ash ↔ ruin
- ruin ↔ path
- wall/cliff edge

### Tasks

- [ ] **P4.8** Audit existing kit trước khi generate mới.
- [ ] **P4.9** Generate thiếu ground family.
- [ ] **P4.10** Complete path transitions.
- [ ] **P4.11** Complete ruins family.
- [ ] **P4.12** Complete shrine floor family.
- [ ] **P4.13** Complete portal approach family.
- [ ] **P4.14** QA + provenance + catalog integration.

---

## 4D. Author Real Map Blueprint

Map không được derive từ `hash(x,y)` nữa.

Target semantic route:

```text
Spawn Clearing
  -> Main Ash Road
      -> Shrine Court
      -> Ruined Graveyard
      -> Skeleton Encounter Court
      -> Portal Approach
```

Mỗi zone phải có:

- semantic type
- polygon/rect bounds
- entrance/exit connector
- terrain palette
- prop budget
- encounter/open-space budget
- landmark position
- collision/navigation constraints

### Tasks

- [ ] **P4.15** Define zone data schema.
- [ ] **P4.16** Define Spawn Clearing.
- [ ] **P4.17** Define Main Ash Road.
- [ ] **P4.18** Define Shrine Court.
- [ ] **P4.19** Define Ruined Graveyard.
- [ ] **P4.20** Define Skeleton Encounter Court.
- [ ] **P4.21** Define Portal Approach.
- [ ] **P4.22** Define connectors giữa zones.

---

## 4E. Irregular Boundaries

### Tasks

- [ ] **P4.23** Replace giant rectangle bằng authored polygon/stepped mask.
- [ ] **P4.24** Có thể dùng macro stamp (ví dụ 6×4, 8×6) nhưng không lặp như checkerboard.
- [ ] **P4.25** Terrain transition phải che cảm giác grid.

---

## 4F. Environment Asset Integration

Trước khi dùng asset như water/bridge/seal/banner/torch:

- [ ] **P4.26** Verify asset-production scope.
- [ ] **P4.27** Verify `integration_ready`.
- [ ] **P4.28** Add exact AssetId vào canonical catalog.
- [ ] **P4.29** Chỉ place nếu semantic zone thật sự cần.
- [ ] **P4.30** Không thêm bridge/water chỉ vì file tồn tại.

### Exit Criteria

- [ ] HUD hidden screenshot vẫn nhìn ra Ash Graves/graveyard/ruins/shrine/portal.
- [ ] Không còn cảm giác checkerboard.
- [ ] Không còn vùng màu lớn vô nghĩa.
- [ ] Road có shape liên tục.
- [ ] Mỗi zone có identity khác nhau.
- [?] User review.
- [ ] Phase 4 = `[x] COMPLETE`.

---

# Phase 5 — Y-sort / Occlusion / Object Anchoring

**Y-sort** (sắp thứ tự render theo trục Y) dùng để actor đứng phía trước/sau vật thể đúng theo vị trí chân.

### Tasks

- [ ] **P5.1** Actors và tall props vào common Y-sorted flow khi phù hợp.
- [ ] **P5.2** Dùng ground pivot/base làm sort anchor.
- [ ] **P5.3** Roof/foreground overhang tách sang foreground layer.
- [ ] **P5.4** Tall collision block ở base, không theo full sprite height.
- [ ] **P5.5** Test Player đi trước statue.
- [ ] **P5.6** Test Player đi sau statue.
- [ ] **P5.7** Test Player cạnh portal.
- [ ] **P5.8** Test Player đi sau grave marker.
- [ ] **P5.9** Test Player đi qua prop cluster.

### Exit Criteria

- [ ] Không còn actor “dán lên trên” vật thể cao sai chiều sâu.
- [ ] Tall sprite không tạo giant collision box.
- [?] User review.
- [ ] Phase 5 = `[x] COMPLETE`.

---

# Phase 6 — Camera / Viewport Composition Audit

Giữ logical viewport `640×360` trước.

Không dùng camera zoom để chữa sai scale từ đầu.

### Tasks

- [ ] **P6.1** Measure Player visible height % so với viewport.
- [ ] **P6.2** Target gameplay-readable band khoảng `12–17%` viewport height, tùy art finalized.
- [ ] **P6.3** Shrine/portal phải lớn nhưng không phá composition.
- [ ] **P6.4** Camera smoothing không được reintroduce jitter.
- [ ] **P6.5** Pixel snapping chỉ ở final rendered transform.
- [ ] **P6.6** Nếu actor còn nhỏ, chỉnh Player baseline / camera relationship một lần, không chỉnh từng asset.

### Exit Criteria

- [ ] Camera composition ổn ở movement bình thường.
- [ ] Không jitter.
- [ ] Player đủ rõ.
- [ ] Architecture vẫn đúng scale.
- [?] User review.
- [ ] Phase 6 = `[x] COMPLETE`.

---

# 5. File-by-File Refactor Map — Sửa file nào, trách nhiệm gì

## `src/Presentation/Arena.cs`

Hiện class này đang ôm quá nhiều responsibility.

### Move out (tách ra)

- actor sprite creation → `ActorPresentationFactory.cs`
- animation state selection → `ActorAnimationController.cs`
- scale lookup/application → `WorldScaleService.cs`
- world prop drawing → `WorldPropRenderer.cs`
- terrain composition → chuyển sang map subsystem
- debug presentation probes → `PresentationDebugOverlay.cs`

### Keep in `Arena.cs`

- input adapter
- snapshot handoff
- high-level scene orchestration
- UI orchestration

### Tracking

- [ ] Arena actor creation extracted.
- [ ] Arena animation logic extracted.
- [ ] Arena scale logic extracted.
- [ ] Arena prop render extracted.
- [ ] Arena terrain responsibility removed.
- [ ] Debug overlay extracted.

---

## `src/Presentation/CanonicalAssetCatalog.cs`

### Add

- `ValidateAnimationClip()`
- `CountUniqueFrameContent()`
- `ValidatePivotFamily()`
- `ValidateRequiredActorSet()`

### Tracking

- [ ] Animation validation.
- [ ] Unique frame detection.
- [ ] Pivot family validation.
- [ ] Required actor set validation.
- [ ] No unrelated silent fallback.

---

## `src/Presentation/PresentationVisualMetrics.cs`

Nên split thành:

### `AssetMeasurementCatalog`
Dữ liệu khách quan:
- opaque bounds
- pivot
- canvas
- frame size

### `WorldScalePolicy`
Dữ liệu design:
- Player baseline
- class/species ratios
- approved override

### Tracking

- [ ] Measurement split.
- [ ] Scale policy split.
- [ ] Runtime consume new APIs.
- [ ] Old `intendedFootprint` one-off logic removed.

---

## `src/Presentation/AshGravesTerrainLayer.cs`

Refactor theo hướng:

- `AshGravesMapDefinition.cs`
- `AshGravesMapBuilder.cs`
- `TerrainTileCatalog.cs`
- `TerrainStampCatalog.cs`
- `WorldDecorationSpawner.cs`

### Tracking

- [ ] `IsAshTexturePatch(hash)` removed from final render path.
- [ ] Hard-coded ruin rectangles removed.
- [ ] `DrawRect` only debug/fallback.
- [ ] Map builder semantic.
- [ ] Zone blueprint integrated.
- [ ] Terrain layers integrated.

---

## `data/v2.5/presentation-visual-metrics.v2.5.json`

### Tracking

- [ ] Split measured facts khỏi design policy.
- [ ] Opaque bounds auto-generated, không nhập tay nếu tool đo được.
- [ ] Player baseline defined.
- [ ] Physical class ratios approved.

---

## `data/v2.5/asset-catalog.v2.5.json`

Giữ vai trò exact provenance contract.

Asset mới chỉ được thêm khi có:

- exact file
- hash
- frames
- pivot
- QA status
- approval status

### Tracking

- [ ] Không fake AssetId.
- [ ] Repaired animations updated exact hash.
- [ ] Environment assets integrated đúng scope.
- [ ] Missing assets vẫn explicit.

---

# 6. Asset Production Priority

## P0 — Blocker trực tiếp

- [ ] Repair Player move S/W/E/N.
- [ ] Audit Player idle/attack duplicate-frame.
- [ ] Audit Skeleton active clips.
- [ ] Build missing Skeleton required directions hoặc mark blocked.
- [ ] Generate measurement report.

## P1 — Scale / Readability

- [ ] Approve relative scale: Player / Goblin / Skeleton / grave / pillar / shrine / portal.
- [ ] Adjust actor palette nếu contrast test fail.
- [ ] Create common-ground scale comparison sheet.

## P2 — Semantic Ash Graves Kit

- [ ] Base ground variations.
- [ ] Path transition family.
- [ ] Ruin floor/boundary family.
- [ ] Shrine court ground.
- [ ] Portal approach.
- [ ] Additional scenery chỉ sau các mục trên.

---

# 7. Source Implementation Priority

## P0 — Correctness

- [ ] Asset audit report generation.
- [ ] Duplicate-frame validation.
- [ ] World Scale Contract.
- [ ] VisualScaleLab.
- [ ] Verify Player animation không bị code restart.

## P1 — Map Architecture

- [ ] Semantic terrain layers.
- [ ] Authored Ash Graves blueprint.
- [ ] Remove sparse hash ground.
- [ ] Remove hard-coded rectangular ruins.
- [ ] Terrain transition integration.
- [ ] Correct Y-sort flow.

## P2 — Readability

- [ ] Footprint-driven shadow.
- [ ] Local contrast clearing.
- [ ] Prop spacing/density rules.
- [ ] Target/hostility indicators.
- [ ] Final camera calibration.

---

# 8. Recommended Agent Work Items — Thứ tự agent nên làm

Không làm tất cả trong một giant patch.

## Work Item A — Asset Truth Audit

**Scope:** tooling + docs, chưa thay visual gameplay.

### Checklist

- [ ] Audit tool implemented.
- [ ] Audit report generated.
- [ ] Missing/duplicate frame matrix generated.
- [ ] Scale ownership issues listed.
- [ ] Commit/PR linked.
- [ ] Status: `[x] COMPLETE`

---

## Work Item B — Player Animation Replacement

Chỉ thay repaired Player animation assets + catalog metadata/hash.

Không sửa map.

### Checklist

- [ ] move.s repaired.
- [ ] move.w repaired.
- [ ] move.e repaired.
- [ ] move.n repaired.
- [ ] GIF/contact sheet ready.
- [ ] Catalog hash updated.
- [ ] Runtime plays real frames.
- [?] User review.
- [ ] Sau user accept → `[x] COMPLETE`.

---

## Work Item C — World Scale Contract

Implement scale model + `VisualScaleLab`.

Không rewrite terrain.

### Checklist

- [ ] Scale data model added.
- [ ] Player baseline defined.
- [ ] Species/object classes defined.
- [ ] Duplicate scale owner removed.
- [ ] VisualScaleLab created.
- [?] User approves lineup.
- [ ] Sau approve → `[x] COMPLETE`.

---

## Work Item D — Runtime Scale Application

Áp scale đã approve vào gameplay world.

### Checklist

- [ ] Player scale applied.
- [ ] Enemy scale applied.
- [ ] World prop scale applied.
- [ ] Shadow scale applied.
- [ ] Y-sort anchor rechecked.
- [?] User gameplay screenshot review.
- [ ] Sau approve → `[x] COMPLETE`.

---

## Work Item E — Semantic Ash Graves Foundation

Layered map builder + authored zone blueprint.

### Checklist

- [ ] Semantic layer hierarchy implemented.
- [ ] Spawn Clearing implemented.
- [ ] Main Ash Road implemented.
- [ ] Shrine Court implemented.
- [ ] Ruined Graveyard implemented.
- [ ] Skeleton Encounter Court implemented.
- [ ] Portal Approach implemented.
- [ ] Hash-pattern terrain removed.
- [ ] Flat color normal terrain removed.
- [?] User review.
- [ ] Sau approve → `[x] COMPLETE`.

---

## Work Item F — Environment Integration

Import/authorize environment tiles/props còn thiếu.

### Checklist

- [ ] Required environment asset list confirmed.
- [ ] Asset scope verified.
- [ ] Integration-ready assets copied.
- [ ] Catalog updated.
- [ ] Semantic placement done.
- [ ] No fake/unauthorized asset.
- [?] User review.
- [ ] Sau approve → `[x] COMPLETE`.

---

## Work Item G — Readability Polish

### Checklist

- [ ] Actor/background contrast.
- [ ] Prop density.
- [ ] Focal clearing.
- [ ] Selection/hostility cues.
- [ ] Camera composition.
- [ ] Final Y-sort/occlusion pass.
- [?] Final user visual acceptance.
- [ ] Sau approve → `[x] COMPLETE`.

---

# 9. Manual Acceptance Checklist — Checklist user test

Theo repo policy, **user là gameplay tester**. Build pass không thay thế visual acceptance.

## Animation

- [ ] Idle không giống frozen accidental frame.
- [ ] Walk down có body/leg motion.
- [ ] Walk up có body/leg motion.
- [ ] Walk left có body/leg motion.
- [ ] Walk right có body/leg motion.
- [ ] Animation không restart mỗi frame.
- [ ] Foot anchor không nhảy mạnh.

## Scale

- [ ] Player nhỏ hơn tall statue/pillar rõ ràng.
- [ ] Adult Goblin gần human scale khi design yêu cầu.
- [ ] Skeleton gần Player scale.
- [ ] Chest/rock/debris nhỏ hơn Player rõ ràng.
- [ ] Shrine đọc như architecture.
- [ ] Portal/gate lớn vượt Player rõ ràng.

## Readability

- [ ] Player nhìn rõ trên dark ground.
- [ ] Enemy nhìn rõ không cần debug text.
- [ ] Enemy/Ally phân biệt được.
- [ ] Actor vẫn rõ trên road.
- [ ] Actor vẫn rõ trên ruins.
- [ ] Shadow đủ grounding nhưng không thành black circle lớn.

## Map

- [ ] Không có large unexplained flat-color region.
- [ ] Main path có shape liên tục.
- [ ] Spawn / Shrine / Ruins / Encounter / Portal khác nhau.
- [ ] Terrain boundary không còn giant rectangle.
- [ ] Props hỗ trợ identity của zone.
- [ ] HUD hidden vẫn nhìn ra đây là một world/map hoàn chỉnh.

## Layering

- [ ] Player đi trước tall prop đúng.
- [ ] Player đi sau tall prop đúng.
- [ ] Collision theo base/footprint.
- [ ] Không có object floating.
- [ ] Không có actor render sai chiều sâu.

---

# 10. Do-Not-Do Rules — Những điều không được làm

1. **Không** tiếp tục chỉnh `intendedFootprint` từng asset dựa trên screenshot.
2. **Không** regenerate toàn bộ asset library.
3. **Không** sửa animation bằng cách đổi FPS/speed khi source frames vẫn giống nhau.
4. **Không** dùng random/hash tile sprinkling làm final map.
5. **Không** dùng `DrawRect` flat region làm normal visible ground.
6. **Không** coi canvas `64×64` là physical size.
7. **Không** tự scale collision theo sprite visual scale.
8. **Không** silent fallback missing AssetId sang art khác.
9. **Không** sửa gameplay balance/save/state trong presentation refactor này.
10. **Không** gọi `dotnet build` là visual acceptance.
11. **Không** mark Work Item `[x] COMPLETE` nếu task yêu cầu user review mà user chưa accept.
12. **Không** mix Animation + Scale + Map + Readability vào cùng một giant patch.

---

# 11. Definition of Done — Khi nào toàn bộ refactor được coi là xong

- [ ] Player có real multi-frame animation.
- [ ] Active beta enemies có real animation cần thiết.
- [ ] Relative scale chạy theo Player-centered World Scale Contract.
- [ ] Player / monster / architecture có size class hợp lý.
- [ ] Ash Graves dùng semantic authored terrain layers.
- [ ] Không còn random sparse rectangle painting làm final map.
- [ ] Không còn unexplained flat-color block.
- [ ] Player/enemy silhouette rõ ở normal zoom.
- [ ] Y-sort/ground pivot đúng.
- [ ] AssetId/provenance exact.
- [ ] Missing art explicit.
- [ ] User accept Player 4-direction movement.
- [ ] User accept Player + Goblin/Skeleton + tall statue scale.
- [ ] User accept Shrine/Portal scale.
- [ ] User accept representative Ash Graves screenshot với HUD hidden.
- [ ] User accept representative Ash Graves screenshot với HUD shown.

---

# 12. Final Diagnosis — Chẩn đoán cuối

**Game hiện không còn bị block chủ yếu bởi movement jitter. Blocker chính hiện tại là:**

1. **invalid animation content** (frame animation không có motion thật);
2. **ungrounded world scale contract** (tỉ lệ vật thể chưa dựa trên hệ thống thực tế);
3. **procedural tile-painting architecture** (cách vẽ map bằng ô/màu không thể tạo cảm giác một thế giới được author hoàn chỉnh).

Thứ tự sửa đúng là:

```text
Asset Truth
→ Real Animation
→ World Scale Contract
→ Runtime Scale
→ Semantic Map Architecture
→ Environment Integration
→ Readability / Y-sort / Camera Polish
```

Không đảo thứ tự này nếu không có blocker kỹ thuật cụ thể.
