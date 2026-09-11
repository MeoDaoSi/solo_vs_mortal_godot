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
| Phase 0 | Freeze & Measure — audit asset/presentation hiện tại | [x] COMPLETE | 2026-09-11 — evidence `docs/V2.5/VISUAL_ASSET_AUDIT_2026-09-11.md/.json`; tool `tools/AssetAudit` |
| Phase 1 | Repair Animation Assets — sửa animation thật | [ ] TODO | |
| Phase 2 | World Scale Contract — chốt hệ thống tỉ lệ thế giới | [ ] TODO | |
| Phase 3 | Readability — làm rõ Player/quái/vật thể | [ ] TODO | |
| Phase 4 | Semantic World Layers — refactor cách build map | [ ] TODO | |
| Phase 5 | Y-sort / Occlusion / Anchoring | [ ] TODO | |
| Phase 6 | Camera / Viewport Composition | [ ] TODO | |
| Phase 7 | Bug Fix — Sửa bugs nghiêm trọng trong source code | [ ] TODO | |

## 0.2 Tổng tiến độ theo Work Item

| Work Item | Nội dung | Trạng thái | Commit / PR / Evidence |
|---|---|---|---|
| A | Asset Truth Audit | [x] COMPLETE | Phase 0 evidence `docs/V2.5/VISUAL_ASSET_AUDIT_2026-09-11.md/.json` |
| B | Player Animation Replacement | [ ] TODO | |
| C | World Scale Contract + VisualScaleLab | [ ] TODO | |
| D | Runtime Scale Application | [ ] TODO | |
| E | Semantic Ash Graves Foundation | [ ] TODO | |
| F | Environment Integration | [ ] TODO | |
| G | Readability Polish | [ ] TODO | |
| H | Critical/High Bug Fix — Sửa bugs nghiêm trọng | [ ] TODO | |
| I | Medium Bug Fix — Sửa bugs trung bình | [ ] TODO | |

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

- [x] **P0.1** Export audit table cho mọi AssetId đang được load, gồm:
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
- [x] **P0.2** Detect identical frames (frame pixel giống nhau).
- [x] **P0.3** Detect opaque bounds drift (biên silhouette thay đổi bất thường).
- [x] **P0.4** Detect pivot drift.
- [x] **P0.5** Detect actor thiếu direction.
- [x] **P0.6** Detect actor thiếu `idle/move/attack`.
- [x] **P0.7** Detect asset thiếu visual metrics.
- [x] **P0.8** Detect duplicate scale ownership do `PresentationScale`.
- [x] **P0.9** Tạo `docs/V2.5/VISUAL_ASSET_AUDIT_2026-09-11.md`.
- [x] **P0.10** Tạo machine-readable JSON report (`docs/V2.5/VISUAL_ASSET_AUDIT_2026-09-11.json`).

### Exit Criteria

- [x] Có thể trả lời chính xác cho mọi visual asset: file nào, bao nhiêu frame thật, silhouette bao nhiêu px, pivot ở đâu, scale runtime bao nhiêu.
- [x] Không gọi đây là gameplay test.
- [x] Agent cập nhật Phase 0 thành `[x] COMPLETE`.

> **Kết quả (2026-09-11, tool `tools/AssetAudit`, compile + run `dotnet build`/`dotnet run` only — không phải gameplay test):**
> - 149 catalog assets trong `asset-integration-trial-v001` (catalog `complete=false`); công cụ đọc trực tiếp đủ 149 PNG (parse IHDR + IDAT + filter reconstruct, không dùng System.Drawing).
> - Người so sánh: `presentation-visual-metrics.v2.5.json`, khớp chính xác 100% opaque bounds cho các asset có metrics (vd `player.base.move.s` = 21,15,22,41; `soul.skeleton.rank01.enemy.south` = 15,12,34,44) → parser PNG đúng.
> - **Identical frames: 20 assets** — toàn bộ `player.base.*` (idle 4/4, move/attack/death 6/6, hit 2/2 đều chỉ có 1 unique hash) → khớp finding cũ "1 unique frame/6", thuộc pending `needs_rework` (ART-01 … ), chưa phải approved motion.
> - **Opaque bounds drift: 32** (chủ yếu Skeleton enemy hit/death — đổi tư thế trong clip là legit; cần xem lẻ trong Phase 1B).
> - **Missing visual metrics: 126** (bao gồm các bản generate, tile, prop); chỉ 23 asset có metrics; 23 scale not 1:1 (INFO `SCALE_OWNERSHIP`).
> - **Missing clip: 2 HIGH** (`soul.skeleton.rank01.enemy` thiếu `idle`, `move` — đúng theo `asset-requirements.v2.5.json` yêu cầu idle/move/attack/hit/death; weaponpose chỉ cần attack nên không tính).
> - **Pivot drift: 0** (mọi animated actor pivot đều (32,56)); **Markers `MISSING: <AssetId>` cho 126/149 asset** vì không có metrics (nhiều là legit — tile mask 1-frame 32×32 pivot (0,0)).
> - **Static reuse actors** (bị loại khỏi coverage vì thiết kế): `equipment.sword.rank01.attachment.*` (4), `soul.skeleton.rank01.enemy.south`, `soul.skeleton.rank01.ally.south`.
> - Ghi chú: 3 ID ban đầu dự kiến không tìm thấy trong catalog (so với worklist) — `player.base.idle.s` (`needs_rework`), `soul.skeleton.rank01.enemy.south`, `soul.skeleton.rank01.ally.south` đã được user cho phép tái sử dụng static, không phải animation task.
> - **Next (Phase 1A)**: regenerate player.body 6-frame locomotion; yêu cầu facet drift check + foot anchor.

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

# Phase 7 — Bug Fix — Sửa bugs nghiêm trọng trong source code

Đây là phase fix bugs được phát hiện qua audit source code toàn diện (2026-09-11). Các bugs này **trong phạm vi** vì ảnh hưởng trực tiếp đến gameplay correctness, data integrity, và runtime stability — kể cả khi visual refactor plan tuyên bố "ngoài phạm vi gameplay V2.5", các bugs này phải fix trước khi refactor visual có ý nghĩa.

**Phạm vi phase này:** sửa logic bugs, crash bugs, data corruption bugs trong C# source code. Không thay đổi gameplay balance, save schema, hay visual presentation.

## 7A. Critical Bugs — Bugs nghiêm trọng nhất (phải fix trước)

### Bug H.1 — Operator precedence sai trong save validation (MaxHp corrupt restore im lặng)

**File:** `src/Application/GameApplication.cs:691-694`
**Severity:** Critical
**Ảnh hưởng:** Player có MaxHp sai suốt session nếu save bị corrupt — data corruption không detect được.

**Mô tả bug:**
```csharp
// CODE HIỆN TẠI (sai precedence):
if (save.BalanceVersion == V25SaveFormat.BalanceVersion &&
    (V25FixedPoint.RoundMilli(_session.Player.State.MaxHp) != player.MaxHpMilli
     || V25FixedPoint.RoundMilli(_session.Player.State.MaxSpirit) != player.MaxSpiritMilli
        && !(save.Payload.WorldLifecycle?.HazardTicks is null && player.MaxSpiritMilli == V25FixedPoint.RoundMilli(V25PlayerSpirit(player.Level, player.Rank)))))
    throw new InvalidDataException("Saved maxima disagree with restored equipment, passives and possession.");
```

`||` có ưu tiên thấp hơn `&&`. Logic thật sự là:
`HP_mismatch || (Spirit_mismatch && !Spirit_fallback)`

=> HP mismatch đơn lẻ **không bao giờ throw**. Chỉ Spirit mismatch mới trigger exception.

**Fix:**
```csharp
// CODE SAI (cần thêm parentheses):
if (save.BalanceVersion == V25SaveFormat.BalanceVersion &&
    ((V25FixedPoint.RoundMilli(_session.Player.State.MaxHp) != player.MaxHpMilli
      || V25FixedPoint.RoundMilli(_session.Player.State.MaxSpirit) != player.MaxSpiritMilli)
     && !(save.Payload.WorldLifecycle?.HazardTicks is null
          && player.MaxSpiritMilli == V25FixedPoint.RoundMilli(V25PlayerSpirit(player.Level, player.Rank)))))
    throw new InvalidDataException("Saved maxima disagree with restored equipment, passives and possession.");
```

**Tasks:**
- [ ] **H.1.1** Đọc `GameApplication.cs:691-694`, xác nhận logic sai.
- [ ] **H.1.2** Thêm parentheses bao quanh `(HP_mismatch || Spirit_mismatch)`.
- [ ] **H.1.3** Verify fix bằng `dotnet build`.
- [ ] **H.1.4** Kiểm tra các caller của `RestoreCanonicalSave` có affected paths không.

---

### Bug H.2 — Pcg32.Int crash với full int range

**File:** `src/Core/Rng/Pcg32.cs:58`
**Severity:** Critical
**Ảnh hưởng:** Any call to `Int(int.MinValue, int.MaxValue)` throws `OverflowException` trong checked context. Bất kỳ gameplay system nào dùng full-int range đều crash.

**Mô tả bug:**
`Int(int.MinValue, int.MaxValue)` tạo range = `4294967296` (toàn bộ uint space). `(int)(value % range)` cast uint > int.MaxValue sang int trong checked context => `OverflowException`.

**Fix:** Dùng `unchecked` cho cast, hoặc xử lý `int.MinValue` edge case riêng.

**Tasks:**
- [ ] **H.2.1** Đọc `Pcg32.cs:49-58`, xác nhận bug trong checked context.
- [ ] **H.2.2** Sửa cast sang `unchecked((int)(value % range))` hoặc fix range handling.
- [ ] **H.2.3** Thêm test case `Int(int.MinValue, int.MaxValue)` không crash.
- [ ] **H.2.4** Verify fix bằng `dotnet build`.
- [ ] **H.2.5** Audit tất cả callers của `Pcg32.Int` để xác nhận không có other edge cases.

---

### Bug H.3 — Travel rollback thiếu RefreshSnapshot — presentation desync

**File:** `src/Presentation/Arena.cs:1000-1001`
**Severity:** Critical
**Ảnh hưởng:** Khi travel succeed nhưng save fail, application state rollback nhưng presentation vẫn hiển thị vùng mới — sprite, minimap, status bar desync.

**Mô tả bug:**
```csharp
// Travel succeeds, Save() fails:
if (_application.HasCanonicalDurableChanges && !Save(showMessage: false)) return;
// ← return đây KHÔNG gọi RefreshSnapshot()
// Lines 1004-1012 (RefreshSnapshot + sprite cleanup) chỉ chạy trên success path
```

**Fix:** Sau khi rollback `_application.RestoreCanonicalSave(...)`, thêm `RefreshSnapshot()` trước khi return.

**Tasks:**
- [ ] **H.3.1** Đọc `Arena.cs:990-1020`, xác nhận travel rollback path.
- [ ] **H.3.2** Thêm `RefreshSnapshot()` sau `_application.RestoreCanonicalSave(...)` trong error path.
- [ ] **H.3.3** Verify: sau rollback, presentation phải reflect pre-travel state.
- [ ] **H.3.4** Verify fix bằng `dotnet build`.

---

### Bug H.4 — Silent simulation halt khi save fail không set _saveCommitFailed

**File:** `src/Presentation/Arena.cs:208`
**Severity:** Critical
**Ảnh hưởng:** Game đông cứng hoàn toàn — monster death animation không chạy, input không xử lý, không có lỗi UI hiển thị.

**Mô tả bug:**
```csharp
if (_application.HasCanonicalDurableChanges && !Save(showMessage: false)) return;
```

Nếu `Save()` return `false` mà không set `_saveCommitFailed` (xảy ra trong branch `_saveWritesBlocked` hoặc `_application.CanonicalContent is null`), `_PhysicsProcess` bị skip hoàn toàn. Player thấy game freeze, không có toast lỗi, không recover được.

**Fix:** Xác nhận tất cả failure paths của `Save()` đều set `_saveCommitFailed = true`, hoặc thêm fallback flag set ở đây.

**Tasks:**
- [ ] **H.4.1** Đọc toàn bộ `Save()` method trong `Arena.cs`, liệt kê tất cả return false paths.
- [ ] **H.4.2** Xác nhận mỗi return false path có set `_saveCommitFailed`.
- [ ] **H.4.3** Sửa các paths thiếu flag.
- [ ] **H.4.4** Verify: khi save fail vì bất kỳ lý do gì, player phải thấy error toast và game vẫn respond input.
- [ ] **H.4.5** Verify fix bằng `dotnet build`.

---

### Bug H.5 — Vec2.Normalized() tạo Infinity từ near-zero vectors

**File:** `src/Core/Math/Vec2.cs:9-12`
**Severity:** Critical
**Ảnh hưởng:** Bất kỳ physics/movement path nào chạm zero-magnitude vector sẽ poison toàn bộ downstream calculations với `Infinity`.

**Mô tả bug:**
```csharp
// CODE HIỆN TẠI:
public Vec2 Normalized()
{
    var length = Length;
    if (length == 0) return this;  // so sánh exact == 0
    return new Vec2(X / length, Y / length);
}
```

Vector `(1e-300, 0)` có `Length = 1e-300` (non-zero) => `Normalized()` proceed => `X / length = 1e300` => `Infinity`.

**Fix:** Dùng epsilon threshold thay vì exact comparison:
```csharp
if (length < 1e-10) return this;
```

**Tasks:**
- [ ] **H.5.1** Đọc `Vec2.cs`, xác nhận `== 0` comparison.
- [ ] **H.5.2** Sửa thành `length < 1e-10` hoặc epsilon derived từ context.
- [ ] **H.5.3** Audit tất cả callers của `Normalized()` trong codebase.
- [ ] **H.5.4** Verify fix bằng `dotnet build`.

---

### Bug H.6 — SeededRng.Int bị bias thống kê

**File:** `src/Core/Rng/SeededRng.cs:39`
**Severity:** High
**Ảnh hưởng:** Gameplay RNG (item tiers, loot rolls, etc.) dùng SeededRng bị measurably unfair so với Pcg32 rejection sampling.

**Mô tả bug:**
```csharp
// CODE HIỆN TẠI:
public int Int(int min, int max) => (int)Math.Floor(Range(min, (double)max + 1));
```

`Math.Floor(Range(...))` map `[0, 1)` từ 32-bit LCG onto integer range. Với range không chia hết cho 2^32, một số giá trị có xác suất cao hơn → bias.

**Fix:** Implement rejection sampling giống Pcg32, hoặc documented bias là intentional.

**Tasks:**
- [ ] **H.6.1** Đọc `SeededRng.cs`, xác nhận bias.
- [ ] **H.6.2** Quyết định: rejection sampling hay documented bias.
- [ ] **H.6.3** Implement fix.
- [ ] **H.6.4** Verify fix bằng `dotnet build`.

---

### Bug H.7 — EventBus — unhandled exception kill remaining handlers

**File:** `src/Core/Events/EventBus.cs:19`
**Severity:** High
**Ảnh hưởng:** Handler ném exception → các handlers tiếp theo cho cùng event type bị skip im lặng. Có thể break event pipeline.

**Mô tả bug:**
```csharp
// CODE HIỆN TẠI:
foreach (var handler in handlers.ToArray())
    handler.DynamicInvoke(args);  // ← nếu throw, các handler tiếp bị skip
```

**Fix:** Wrap mỗi handler trong try/catch, log exception, continue:
```csharp
foreach (var handler in handlers.ToArray())
{
    try { handler.DynamicInvoke(args); }
    catch (Exception ex) { /* log */ }
}
```

**Tasks:**
- [ ] **H.7.1** Đọc `EventBus.cs`, xác nhận lack of per-handler exception handling.
- [ ] **H.7.2** Thêm try/catch per handler với logging.
- [ ] **H.7.3** Verify fix bằng `dotnet build`.

---

### Bug H.8 — GameApplication.Inventory() NullReferenceException

**File:** `src/Application/GameApplication.cs:227-229`
**Severity:** High
**Ảnh hưởng:** Crash khi `CanonicalContent is not null` nhưng `_session.InventoryV25` null.

**Mô tả bug:**
```csharp
// CODE HIỆN TẠI:
public IReadOnlyList<InventoryItem> Inventory() => CanonicalContent is not null
    ? (_session.InventoryV25?.Items.Concat(_session.InventoryV25.Overflow).Select(...).ToArray() ?? ...)
    : _session.Progression.InventorySnapshot();
```

`InventoryV25?.Items` trả null nếu `InventoryV25` null, nhưng `.Concat(InventoryV25.Overflow)` dereference null.

**Fix:** Null-check đầy đủ:
```csharp
var inv = _session.InventoryV25;
if (inv is null) return Array.Empty<InventoryItem>();
return inv.Items.Concat(inv.Overflow).Select(...).ToArray();
```

**Tasks:**
- [ ] **H.8.1** Đọc `GameApplication.cs:227-229`, xác nhận NRE risk.
- [ ] **H.8.2** Sửa null-check chain.
- [ ] **H.8.3** Verify fix bằng `dotnet build`.

---

### Bug H.9 — AshGravesTerrainLayer ChunkGrid["Field"] crash

**File:** `src/Presentation/AshGravesTerrainLayer.cs:132`
**Severity:** High
**Ảnh hưởng:** `KeyNotFoundException` nếu layout không có key `"Field"`.

**Mô tả bug:**
```csharp
// CODE HIỆN TẠI:
var landmarkChunk = _layout.ChunkGrid["Field"];  // ← direct indexer
```

Cùng file, method `IsRuinPatch` (line 116) đúng cách dùng `TryGetValue`. Đây là inconsistency.

**Fix:** Dùng `TryGetValue`:
```csharp
if (!_layout.ChunkGrid.TryGetValue("Field", out var landmarkChunk)) return;
```

**Tasks:**
- [ ] **H.9.1** Đọc `AshGravesTerrainLayer.cs:130-137`, xác nhận direct indexer.
- [ ] **H.9.2** Sửa `ChunkGrid["Field"]` → `TryGetValue`.
- [ ] **H.9.3** Kiểm tra `ShrineTile`, `LandmarkTile`, `ExitTile` có cần null-check không.
- [ ] **H.9.4** Verify fix bằng `dotnet build`.

---

### Bug H.10 — Shield refresh dùng raw grant thay vì quantized

**File:** `src/Simulation/Rules/V25SimulationRules.cs:305`
**Severity:** High
**Ảnh hưởng:** So sánh precision sai giữa quantized và raw value — shield refresh có thể set giá trị sai.

**Mô tả bug:**
```csharp
// CODE HIỆN TẠI:
var amount = V25FixedPoint.QuantizeMilli(grant);       // line 302: quantized
if (_shields.TryGetValue(key, out var previous))
{
    amount = Math.Max(previous.Amount, grant);          // ← dùng raw `grant` thay vì `amount`
```

`previous.Amount` là quantized (milli-rounded), `grant` là raw double. So sánh sai precision.

**Fix:** `Math.Max(previous.Amount, amount)` (dùng `amount` đã quantize).

**Tasks:**
- [ ] **H.10.1** Đọc `V25SimulationRules.cs:300-310`, xác nhận bug.
- [ ] **H.10.2** Sửa `Math.Max(previous.Amount, grant)` → `Math.Max(previous.Amount, amount)`.
- [ ] **H.10.3** Verify fix bằng `dotnet build`.

---

## 7B. Medium Bugs — Bugs trung bình

### Bug I.1 — XP accepted/discard accounting sai cho UI

**File:** `src/Simulation/Rules/V25SimulationRules.cs:249-253, 269`
**Severity:** Medium
**Ảnh hưởng:** Player thấy "+N XP" nhưng XP bar không di chuyển — UI misleading. (XP discard là thiết kế đúng spec, nhưng accounting cho UI sai.)

**Mô tả bug:**
```csharp
// Breakthrough path (level % 10 == 0):
if (available < requirement) break;  // xp KHÔNG update
// Sau loop:
return new(...) { Accepted = amount, Discarded = 0 };  // ← sai: accepted phải = amount - discarded
```

**Fix:**
```csharp
// Tính discarded đúng:
var discarded = amount - (available - xp);  // hoặc logic tương đương
return new(...) { Accepted = amount - discarded, Discarded = discarded };
```

**Tasks:**
- [ ] **I.1.1** Đọc `V25SimulationRules.cs:240-280`, xác nhận accepted/discard accounting.
- [ ] **I.1.2** Sửa accepted = amount - discarded trong breakthrough path.
- [ ] **I.1.3** Verify UI hiển thị đúng: XP bar chỉ di chuyển bằng accepted amount.
- [ ] **I.1.4** Verify fix bằng `dotnet build`.

---

### Bug I.2 — V25CombatCoordinator DOT hit-key collision

**File:** `src/Simulation/Systems/V25CombatCoordinator.cs:625`
**Severity:** Medium
**Ảnh hưởng:** Hai DOT status từ cùng source trên cùng target, tick cùng frame → hit-key trùng → damage bị drop im lặng.

**Mô tả bug:**
`ApplyDot` build hit-key từ `(int)Math.Min(int.MaxValue, tick)` alone. Hai DOT status tick cùng frame tạo cùng key → `_hitKeys.Add` fail → damage silently dropped.

**Fix:** Thêm discriminator vào hit-key: `status.SourceId + status.EffectId + instance ordinal`.

**Tasks:**
- [ ] **I.2.1** Đọc `V25CombatCoordinator.cs:620-630`, xác nhận hit-key construction.
- [ ] **I.2.2** Thêm per-status discriminator vào hit-key.
- [ ] **I.2.3** Verify: hai DOT trên cùng target không drop damage.
- [ ] **I.2.4** Verify fix bằng `dotnet build`.

---

### Bug I.3 — Health bar NaN khi MaximumHp = 0

**File:** `src/Presentation/Arena.cs:369, 377`
**Severity:** Medium
**Ảnh hồi:** Health bar width = `NaN`/`Infinity` khi monster/ally MaximumHp = 0 → Godot render corrupted rect.

**Mô tả bug:**
```csharp
DrawRect(new Rect2(p.X - 22, p.Y - 31, (float)(44 * monster.CurrentHp / monster.MaximumHp), 5), ...);
```

`MaximumHp = 0` → division by zero → `NaN`.

**Fix:** Guard:
```csharp
var barWidth = monster.MaximumHp > 0 ? (float)(44 * monster.CurrentHp / monster.MaximumHp) : 0;
```

**Tasks:**
- [ ] **I.3.1** Đọc `Arena.cs:365-380`, xác nhận division by zero.
- [ ] **I.3.2** Thêm guard cho cả monster và ally health bars.
- [ ] **I.3.3** Verify fix bằng `dotnet build`.

---

### Bug I.4 — Arena英文字符串 "RestReset encounters" trong Vietnamese UI

**File:** `src/Presentation/Arena.cs:841`
**Severity:** Medium
**Ảnh hưởng:** English string leak vào Vietnamese production UI.

**Mô tả bug:**
```csharp
var reset = new Button { Text = "RestReset encounters" };
```

Tất cả button/label khác đều dùng Vietnamese.

**Fix:** Đổi sang Vietnamese, ví dụ: `"Đặt lại quái"` hoặc appropriate translation.

**Tasks:**
- [ ] **I.4.1** Đọc context quanh `Arena.cs:841`, xác nhận English string.
- [ ] **I.4.2** Dịch sang Vietnamese phù hợp với game context.
- [ ] **I.4.3** Verify fix bằng `dotnet build`.

---

### Bug I.5 — PresentationVisualMetrics UniformScale Infinity

**File:** `src/Presentation/PresentationVisualMetrics.cs:20`
**Severity:** Medium
**Ảnh hưởng:** `UniformScale` → `Infinity` khi `OpaqueBounds.Size` có zero component → sprite scaled to invisible.

**Mô tả bug:**
```csharp
public float UniformScale => Mathf.Min(IntendedFootprint.X / OpaqueBounds.Size.X,
                                       IntendedFootprint.Y / OpaqueBounds.Size.Y);
```

`OpaqueBounds.Size.X = 0` → `IntendedFootprint.X / 0 = Infinity`.

**Fix:** Guard:
```csharp
public float UniformScale
{
    get
    {
        if (OpaqueBounds.Size.X <= 0 || OpaqueBounds.Size.Y <= 0) return 1.0f;
        return Mathf.Min(IntendedFootprint.X / OpaqueBounds.Size.X,
                         IntendedFootprint.Y / OpaqueBounds.Size.Y);
    }
}
```

**Tasks:**
- [ ] **I.5.1** Đọc `PresentationVisualMetrics.cs`, xác nhận division risk.
- [ ] **I.5.2** Thêm zero guard.
- [ ] **I.5.3** Verify fix bằng `dotnet build`.

---

### Bug I.6 — FancyUi.GetStyle return null im lặng

**File:** `src/Presentation/FancyUi.cs:73`
**Severity:** Medium
**Ảnh hưởng:** `GetStyle` return null khi property không tồn tại → NullReferenceException tại call sites với error message không hữu ích.

**Mô tả bug:**
```csharp
private static StyleBox GetStyle(string name) => (StyleBox)Styles.Get(name);
```

`Node.Get()` return null nếu property không tồn tại. Cast `(StyleBox)null` succeed nhưng return null.

**Fix:**
```csharp
private static StyleBox GetStyle(string name)
{
    var result = Styles?.Get(name);
    if (result is null) throw new InvalidOperationException($"Style '{name}' not found in FancyUiStyles autoload.");
    return (StyleBox)result;
}
```

**Tasks:**
- [ ] **I.6.1** Đọc `FancyUi.cs:70-75`, xác nhận null return risk.
- [ ] **I.6.2** Thêm null check với meaningful error message.
- [ ] **I.6.3** Verify fix bằng `dotnet build`.

---

### Bug I.7 — HudMinimap.Project division by zero

**File:** `src/Presentation/HudMinimap.cs:63`
**Severity:** Medium
**Ảnh hưởng:** Minimap render NaN/Infinity positions khi world Width/Height = 0.

**Mô tả bug:**
```csharp
return center + new Vector2((float)(point.X / world.Width - 0.5) * radius * 2,
                            (float)(point.Y / world.Height - 0.5) * radius * 2);
```

**Fix:** Guard:
```csharp
if (world.Width <= 0 || world.Height <= 0) return center;
```

**Tasks:**
- [ ] **I.7.1** Đọc `HudMinimap.cs:60-65`, xác nhận division risk.
- [ ] **I.7.2** Thêm zero guard.
- [ ] **I.7.3** Verify fix bằng `dotnet build`.

---

### Bug I.8 — V25MasterySystem KeyNotFoundException

**File:** `src/Simulation/Systems/V25MasterySystem.cs:236,239`
**Severity:** Medium
**Ảnh hưởng:** `_skills[skill.Id]` direct indexer có thể throw `KeyNotFoundException` nếu skill active nhưng không có trong `_skills` dictionary (restore path).

**Mô tả bug:**
`_skills[skill.Id]` pre-/post-`AddCredit` (mà `AddCredit` dùng `TryGetValue`). Nếu injected `_isLearnedActive` delegate report skill active mà không có trong `_skills` → crash.

**Fix:** Dùng `TryGetValue` thay vì direct indexer:
```csharp
if (!_skills.TryGetValue(skill.Id, out var entry)) continue;
```

**Tasks:**
- [ ] **I.8.1** Đọc `V25MasterySystem.cs:230-245`, xác nhận direct indexer risk.
- [ ] **I.8.2** Sửa thành `TryGetValue` pattern.
- [ ] **I.8.3** Verify fix bằng `dotnet build`.

---

## 7C. Exit Criteria cho Phase 7

- [ ] Tất cả Critical bugs (H.1–H.5) đã fix và verify bằng `dotnet build`.
- [ ] Tất cả High bugs (H.6–H.10) đã fix và verify bằng `dotnet build`.
- [ ] Tất cả Medium bugs (I.1–I.8) đã fix và verify bằng `dotnet build`.
- [ ] Không có regression: `dotnet build` pass.
- [ ] Agent cập nhật Phase 7 thành `[x] COMPLETE`.
- [?] User review gameplay để xác nhận không có side effect.

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

### Bug Fix Tracking (Phase 7)

- [ ] H.3.1–H.3.4: Travel rollback RefreshSnapshot
- [ ] H.4.1–H.4.5: Save failure _saveCommitFailed flag
- [ ] I.3.1–I.3.3: Health bar NaN guard
- [ ] I.4.1–I.4.3: Vietnamese string translation

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

### Bug Fix Tracking (Phase 7)

- [ ] H.9.1–H.9.4: ChunkGrid["Field"] → TryGetValue + null checks

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

## Files chỉ có Bug Fix (Phase 7) — Không nằm trong visual refactor

Các file này chỉ cần fix bugs, không cần refactor visual.

### `src/Application/GameApplication.cs`

**Bug Fix Tracking:**
- [ ] H.1.1–H.1.4: Operator precedence trong save validation
- [ ] H.8.1–H.8.3: Inventory() NullReferenceException

### `src/Core/Rng/Pcg32.cs`

**Bug Fix Tracking:**
- [ ] H.2.1–H.2.5: Int(int.MinValue, int.MaxValue) OverflowException

### `src/Core/Math/Vec2.cs`

**Bug Fix Tracking:**
- [ ] H.5.1–H.5.4: Normalized() Infinity từ near-zero vectors

### `src/Core/Rng/SeededRng.cs`

**Bug Fix Tracking:**
- [ ] H.6.1–H.6.4: Int() bias thống kê

### `src/Core/Events/EventBus.cs`

**Bug Fix Tracking:**
- [ ] H.7.1–H.7.3: Unhandled exception kill remaining handlers

### `src/Simulation/Rules/V25SimulationRules.cs`

**Bug Fix Tracking:**
- [ ] H.10.1–H.10.3: Shield refresh dùng raw grant
- [ ] I.1.1–I.1.4: XP accepted/discard accounting

### `src/Simulation/Systems/V25CombatCoordinator.cs`

**Bug Fix Tracking:**
- [ ] I.2.1–I.2.4: DOT hit-key collision

### `src/Presentation/PresentationVisualMetrics.cs`

**Bug Fix Tracking:**
- [ ] I.5.1–I.5.3: UniformScale Infinity

### `src/Presentation/FancyUi.cs`

**Bug Fix Tracking:**
- [ ] I.6.1–I.6.3: GetStyle return null

### `src/Presentation/HudMinimap.cs`

**Bug Fix Tracking:**
- [ ] I.7.1–I.7.3: Project() division by zero

### `src/Simulation/Systems/V25MasterySystem.cs`

**Bug Fix Tracking:**
- [ ] I.8.1–I.8.3: KeyNotFoundException từ direct indexer

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

- [ ] **Bug Fix: Critical/High bugs (Phase 7A)** — fix trước tất cả crash/data corruption bugs.
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

## Work Item H — Critical/High Bug Fix

**Scope:** sửa 5 Critical + 5 High bugs (Phase 7A). Không thay đổi gameplay balance, save schema, hay visual presentation. Đây là **Work Item đầu tiên phải làm**, trước Work Item A.

### Checklist

- [ ] **H.1** Operator precedence trong save validation (`GameApplication.cs:691`) — thêm parentheses.
- [ ] **H.2** Pcg32.Int full range không crash (`Pcg32.cs:58`) — unchecked cast.
- [ ] **H.3** Travel rollback RefreshSnapshot (`Arena.cs:1000`) — presentation sync.
- [ ] **H.4** Save failure luôn set `_saveCommitFailed` (`Arena.cs:208`) — game không freeze.
- [ ] **H.5** Vec2.Normalized() epsilon (`Vec2.cs:9`) — không tạo Infinity.
- [ ] **H.6** SeededRng.Int bias resolve (`SeededRng.cs:39`) — rejection sampling hoặc documented.
- [ ] **H.7** EventBus per-handler try/catch (`EventBus.cs:19`) — pipeline không bị kill.
- [ ] **H.8** Inventory() null-check chain (`GameApplication.cs:227`) — không NRE.
- [ ] **H.9** ChunkGrid["Field"] TryGetValue (`AshGravesTerrainLayer.cs:132`) — không crash.
- [ ] **H.10** Shield refresh quantized value (`V25SimulationRules.cs:305`).
- [ ] Có evidence `dotnet build` pass.
- [?] User review gameplay xác nhận không regression.
- [ ] Sau user accept → `[x] COMPLETE`.

---

## Work Item I — Medium Bug Fix

**Scope:** sửa 8 Medium bugs (Phase 7B). Làm sau Work Item H.

### Checklist

- [ ] **I.1** XP accepted/discard accounting (`V25SimulationRules.cs:269`) — UI đúng.
- [ ] **I.2** DOT hit-key collision (`V25CombatCoordinator.cs:625`) — no damage drop.
- [ ] **I.3** Health bar NaN guard (`Arena.cs:369,377`) — MaximumHp = 0 safe.
- [ ] **I.4** Vietnamese string translation (`Arena.cs:841`).
- [ ] **I.5** UniformScale Infinity guard (`PresentationVisualMetrics.cs:20`).
- [ ] **I.6** FancyUi.GetStyle null check (`FancyUi.cs:73`).
- [ ] **I.7** HudMinimap division by zero guard (`HudMinimap.cs:63`).
- [ ] **I.8** V25MasterySystem TryGetValue (`V25MasterySystem.cs:236,239`).
- [ ] Có evidence `dotnet build` pass.
- [?] User review gameplay xác nhận không regression.
- [ ] Sau user accept → `[x] COMPLETE`.

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

## Visual Refactor

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

## Bug Fix (Phase 7)

- [ ] Operator precedence trong save validation đã fix (H.1).
- [ ] Pcg32.Int full range không crash (H.2).
- [ ] Travel rollback RefreshSnapshot hoạt động (H.3).
- [ ] Save failure luôn set _saveCommitFailed (H.4).
- [ ] Vec2.Normalized() không tạo Infinity (H.5).
- [ ] SeededRng.Int bias đã resolve (H.6).
- [ ] EventBus handler exceptions không kill pipeline (H.7).
- [ ] GameApplication.Inventory() không NRE (H.8).
- [ ] AshGravesTerrainLayer ChunkGrid không crash (H.9).
- [ ] Shield refresh dùng quantized value (H.10).
- [ ] XP accepted/discard accounting đúng cho UI (I.1).
- [ ] DOT hit-key không collision (I.2).
- [ ] Health bar không NaN (I.3).
- [ ] UI strings đều Vietnamese (I.4).
- [ ] PresentationVisualMetrics không Infinity (I.5).
- [ ] FancyUi.GetStyle không return null (I.6).
- [ ] HudMinimap không division by zero (I.7).
- [ ] V25MasterySystem không KeyNotFoundException (I.8).
- [ ] `dotnet build` pass, không có regression.
- [?] User review gameplay xác nhận không side effect.

---

# 12. Final Diagnosis — Chẩn đoán cuối

**Game hiện không còn bị block chủ yếu bởi movement jitter. Blocker chính hiện tại là:**

1. **invalid animation content** (frame animation không có motion thật);
2. **ungrounded world scale contract** (tỉ lệ vật thể chưa dựa trên hệ thống thực tế);
3. **procedural tile-painting architecture** (cách vẽ map bằng ô/màu không thể tạo cảm giác một thế giới được author hoàn chỉnh);
4. **source code bugs** (14 bugs nghiêm trọng trong Core/Simulation/Application/Presentation layers — data corruption, crash, silent failures).

Thứ tự sửa đúng là:

```text
Bug Fixes (Phase 7) — Critical/High trước
→ Asset Truth
→ Real Animation
→ World Scale Contract
→ Runtime Scale
→ Semantic Map Architecture
→ Environment Integration
→ Readability / Y-sort / Camera Polish
```

**Bug fixes phải đi trước visual refactor** vì:
- H.1 (operator precedence) cho phép MaxHp corrupt restore → gameplay broken
- H.2 (Pcg32 crash) có thể crash bất kỳ lúc nào dùng full-int range
- H.3 + H.4 (Arena desync/halt) làm game freeze hoặc visual desync
- H.5 (Vec2 Infinity) poison physics/movement calculations
- Các bugs khác (NRE, division by zero, bias RNG) ảnh hưởng stability

Không đảo thứ tự này nếu không có blocker kỹ thuật cụ thể.
