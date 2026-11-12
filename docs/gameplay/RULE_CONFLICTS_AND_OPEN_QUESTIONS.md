# Hero Chess — Mâu thuẫn luật và câu hỏi cần xác nhận

> Phiên bản: 12/11/2026 · Đọc kèm `docs/gameplay/GAME_RULES_SPECIFICATION.md`.
> Tài liệu này **chưa được phê duyệt**. Mỗi mục dưới đây là một mâu thuẫn **đã đối chiếu với code/SQL/tài liệu thiết kế**, không phải suy đoán.
> Mỗi mục có: Rule ID liên quan · Bằng chứng · Vì sao mâu thuẫn · Hệ quả trong gameplay · Đề xuất hướng xử lý (cần team chọn).

## Cách đọc

| Nhãn | Nghĩa |
|---|---|
| **CONFLICT** | Code và tài liệu thiết kế (hoặc code với code, hoặc SQL với SQL) đang đưa ra hai kết quả khác nhau cho cùng một tình huống. |
| **UNCONFIRMED** | Luật đang chạy trong code nhưng tài liệu thiết kế nói rõ chưa được người dùng chốt, hoặc còn nhiều phương án chưa chọn. |
| **PLANNED** | Thiết kế đã được ghi nhận nhưng runtime chưa hiện thực / dữ liệu chưa tồn tại. |

Mức độ ảnh hưởng: **Cao** = ảnh hưởng trực tiếp luật chơi trong trận đang chạy · **Trung** = ảnh hưởng khả năng dựng đội hình / catalog · **Thấp** = ảnh hưởng dữ liệu lịch sử hoặc mô tả.

### Tổng quan

**15 mâu thuẫn (C-01 … C-15)** và **12 câu hỏi cần team xác nhận (Q1 … Q12)**.

| Mức độ | Mã mâu thuẫn |
|---|---|
| Cao | C-01, C-02, C-03, C-04, C-05, C-06 |
| Trung | C-07, C-08, C-09, C-10, C-11, C-12 |
| Thấp | C-13, C-14, C-15 |

Các nhóm theo bản chất:
- **Luật Cọc / Hero Skill:** C-01, C-11 · **Command Skill:** C-02, C-03, C-04, C-14, C-15
- **Dữ liệu catalog & lineup:** C-06, C-07, C-08, C-10, C-13 · **Migration/bootstrap:** C-05, C-12
- **Faction Skill:** C-09 · **Tài liệu thiết kế:** C-14, C-15

### Quy ước mã vấn đề

Mỗi mâu thuẫn có mã `C-nn` (nằm ở tài liệu này) và được ánh xạ tới Rule ID trong
`docs/gameplay/GAME_RULES_SPECIFICATION.md`. Trạng thái trong tài liệu kia được ghi ở cột **Trạng thái**:
`CONFLICT` = có mâu thuẫn; `IMPLEMENTED` = chạy đúng như tài liệu thiết kế; `PLANNED` = thiết kế có nhưng chưa hiện thực;
`UNCONFIRMED` = luật đang chạy nhưng chưa ai chốt.

---

## C-01 — Cọc kích hoạt ở "ô dừng" hay ở "bất kỳ giao điểm trên đường"?

**Rule ID liên quan:** `STK-01`, `STK-02`, `STK-03`, `STK-07` · **Mức độ: Cao**

| Nguồn | Nội dung |
|---|---|
| `docs/PLAN_Hero_And_Command_Skills.md` §1 (dòng 19) | "Cọc là **bẫy tại ô dừng** … Chỉ khi quân địch **kết thúc nước đi đúng ô có Cọc** thì quân đó bị ăn. **Đi xuyên/nhảy qua ô Cọc không kích hoạt**" |
| `docs/PLAN_Hero_And_Command_Skills.md` §6 (dòng 101) | "Không hỏi lại **Cọc dừng ô** …: hai điểm đó **đã được người dùng chốt**" |
| `docs/PLAN_Khoi_Phuc_Hero_Trait.md` (08/11/2026) bảng "Luật Bạch Đằng Giang đã chốt" | "Quân địch không có Khiên bị loại tại **Cọc đầu tiên trên đường hoặc tại ô đích**. Nếu Cọc nằm giữa đường thì quân chưa chạm ô đích, nên chưa ăn quân hay phá vật cản ở đích." |
| Runtime | `XiangqiRulesEngine.CrossedCells(class, from, to)` trả về **mọi ô trung gian** (Xe/Pháo/trait thẳng–chéo), **ô chân Mã**, và **ô đích**. `FindPublicStake` quét toàn bộ danh sách đó. |

**Vì sao mâu thuẫn:** Hai tài liệu thiết kế đều tự nhận là "đã chốt" nhưng quy định **hai luật đối nghịch**. Runtime chọn luật thứ hai (theo đường đi).

**Hệ quả:** Một quân Mã đi từ `(1,4)` sang `(3,6)` qua ô chân `(2,4)` sẽ bị loại nếu `(2,4)` có Cọc địch — dù quân **không bao giờ dừng** ở `(2,4)`. Với luật "ô dừng", quân này đi bình thường. Với luật "theo đường", quân bị loại **tại ô xuất phát**, không ăn quân và không phá vật cản ở đích (`STK-03`).

**Đề xuất:** Chọn một luật và **viết lại tài liệu cũ** để không còn hai nguồn. Nếu giữ "theo đường đi" (runtime hiện tại + `PLAN_Khoi_Phuc_Hero_Trait` mới hơn), cần sửa `PLAN_Hero_And_Command_Skills.md` §1 và §6, và cập nhật test hiện có (`tests/HeroChess.Rules.Tests/PublicRiverStakeTests.cs`).

---

## C-02 — Phản Kỳ Đoạt Thế: tên trộm không bao giờ nắm quyền, và không ai được chọn huỷ

**Rule ID liên quan:** `CMD-18`, `CMD-19`, `CMD-20`, `CMD-25` · **Mức độ: Cao**

**Bằng chứng trong code:**

```106:116:HeroChess.Rules/TurnLifecycle.cs
    public static LifecycleResult Apply(GameState state, Side sideToMove, bool resolveCreatorCancellation = false)
    {
        // Step 1-4: Apply core lifecycle exactly once (returns cloned state with mutations)
        var core = ApplyCore(state, sideToMove);
        var removedEffects = new List<EffectRemovedEvent>();

        // Step 5: Creator Cancellation (only runs if resolveCreatorCancellation is true)
        if (resolveCreatorCancellation)
        {
            ApplyCreatorCancellation(core.State, sideToMove, removedEffects);
        }
```

Bước 4 trong `ApplyCore` chốt steal (`PositionControllers[pos] = PendingController`), bước 5 (`ApplyCreatorCancellation`) kết thúc Effect nếu `effect.Creator == sideToMove && PositionControllers.Values.Any(c => c != sideToMove)`. Vì `resolveCreatorCancellation: true` được truyền ở **mọi** call site (`MatchCommandService.cs:93`, `MatchCommandService.cs:329`, `MatchTurnRules.cs:14`, `MatchTurnRules.cs:113`), bước 4 và bước 5 chạy **cùng một tick** khi lượt của chủ Effect bắt đầu.

**Hệ quả 1:** Effect vừa được chuyển quyền sang phe tên trộm thì bị `State = Ended` ngay lập tức → **tên trộm không bao giờ thực sự kiểm soát Effect**. Chức năng "chiếm quyền" của Phản Kỳ không tạo ra tác dụng nào.

**Hệ quả 2 (thiết kế vs code):** `HERO_CHESS_PHASE3_IMPLEMENTATION_PLAN.md` §3.3 liệt kê 3 phương án cách chủ Effect huỷ và **khuyến nghị phương án A** (nút hành động riêng cho người chơi). Runtime chạy **phương án B** (tự động huỷ) → người chơi không có lựa chọn, và cũng không được báo trước.

**Đề xuất:** Tách bước 5 ra khỏi tick tự động và làm nó thành hành động **tường minh** của chủ Effect (phương án A). Nếu giữ phương án B thì phải sửa `HERO_CHESS_PHASE3_IMPLEMENTATION_PLAN.md` §3.3 và bổ sung event để client biết Effect đã bị huỷ.

---

## C-03 — Phá Trận Đoạt Phong luôn trả `EFFECT_NOT_ASSIGNED`

**Rule ID liên quan:** `CMD-21`, `CMD-22` · **Mức độ: Trung (skill chưa bật được nên chưa ảnh hưởng trận đang chạy)**

`PhaTranDoatPhongHandler` yêu cầu `effect.Payload["assignedPieceId"]` trỏ tới một quân của người dùng:

```66:74:HeroChess.Rules/Skills/PhaTranDoatPhongHandler.cs
        var assignedPieceId = effect.PayloadGuid("assignedPieceId");
        if (!assignedPieceId.HasValue)
            return CommandSkillResult.Failure(ctx.State, "EFFECT_NOT_ASSIGNED",
                "The Effect is not assigned to any of your pieces.");
```

Grep toàn repo cho `assignedPieceId` chỉ ra **đúng 3 chỗ, đều nằm trong chính file này** (comment + đọc + trả event). Không handler nào khác (`VanCocTranGiangHandler`, `BinhLamThuyHienHandler`) ghi field này vào payload Effect — chúng chỉ ghi `kind`/`paths`.

**Hệ quả:** `Phá Trận Đoạt Phong` **luôn** thất bại với mọi Effect do Vạn Cọc / Binh Lâm tạo. Đây là skill chết về mặt cơ chế, dù handler đã đăng ký trong `Program.cs`.

**Đề xuất:** Xác nhận ý nghĩa thiết kế của `assignedPieceId`: Phá Trận nhắm vào **một quân cụ thể** (giống Khiên) hay nhắm vào **một Effect bất kỳ**. Nếu là quân cụ thể thì phải thêm bước chọn quân vào `target` và cho Vạn Cọc / Binh Lâm ghi `assignedPieceId`. Nếu là Effect bất kỳ thì bỏ hẳn điều kiện này khỏi handler.

---

## C-04 — Catalog không có dòng cho 4 Command Skill (Vạn Cọc, Binh Lâm, Phản Kỳ, Phá Trận)

**Rule ID liên quan:** `CMD-23` · **Mức độ: Trung**

Handler đã được đăng ký:

```78:95:HeroChess.Api/Program.cs
builder.Services.AddSingleton<HeroChess.Rules.Skills.CommandSkillRegistry>(sp =>
    new HeroChess.Rules.Skills.CommandSkillRegistry(new HeroChess.Rules.Skills.ICommandSkillHandler[]
    {
        new HeroChess.Rules.Skills.VanCocTranGiangHandler(),
        new HeroChess.Rules.Skills.PhanKyDoatTheHandler(),
        new HeroChess.Rules.Skills.PhaTranDoatPhongHandler(),
        new HeroChess.Rules.Skills.BinhLamThuyHienHandler(),
```

nhưng grep toàn bộ `*.sql` trong repo cho `command_skill` / `van_coc_tran_giang` **không ra dòng `team_skill` nào** cho 4 key này. Các dòng `team_skill` tồn tại chỉ cho: `thanh`, `rao`, `khien`, `tran-hung-dao-tuong-coc` (tắt) và 3 Faction Skill (`bach_dang_tran_phap`, `lam_son_luoc`, `hich_xuat_quan`).

**Hệ quả:** `LineupValidator` yêu cầu đúng 3 ô skill lấy từ `team_skill` → **không thể đưa 4 skill này vào bất kỳ đội hình nào**. Trong khi `AdminCatalogService` lại đánh dấu chúng là `LIVE`:

```264:271:HeroChess.Api/Services/AdminCatalogService.cs
        var live = new HashSet<string>(StringComparer.Ordinal)
        {
            "van_coc_tran_giang", "phan_ky_doat_the", "pha_tran_doat_phong", "binh_lam_thuy_hien", "thanh", "rao", "khien"
        };
```

→ Admin hiển thị "LIVE" cho skill mà người chơi không thể chọn.

**Đề xuất:** Quyết định 4 skill này có lên phát hành không. Nếu **có**: viết dòng `team_skill` (kèm `cooldown_turns`, `description` đúng luật) và thêm vào danh sách script bootstrap. Nếu **không**: bỏ chúng khỏi `live` set của `AdminCatalogService` và ghi rõ trạng thái "DRAFT / chưa phát hành".

---

## C-05 — Nâng cấp state v3 → v4 làm mất Cọc và bẫy sông

**Rule ID liên quan:** `SRV-15` · **Mức độ: Cao (ảnh hưởng mọi trận đã lưu)**

```26:47:HeroChess.Rules/StateSchemaUpgrade.cs
    private static GameState UpgradeFromV3(GameState state)
    {
        var upgraded = new GameState
        {
            StateSchemaVersion = 4,
            EffectInstances = new List<Effects.EffectInstance>(),
            NextCreationOrder = 0,
            ProcessedTurns = new Dictionary<Side, List<int>>()
            {
                [Side.Red] = new List<int>(),
                [Side.Black] = new List<int>()
            },
            StakeMetadata = new Dictionary<Guid, StakeMetadata>(),
            RulesetCode = state.RulesetCode,
            ContentVersion = state.ContentVersion,
            SideToMove = state.SideToMove,
            Version = state.Version,
            TurnIndex = state.TurnIndex,
            CountedActions = state.CountedActions,
            Result = state.Result,
            EndReason = state.EndReason,
            Pieces = state.Pieces,
            Obstacles = state.Obstacles,
            PendingEffects = state.PendingEffects,
            ConsecutiveTimeouts = state.ConsecutiveTimeouts,
            SkillStates = state.SkillStates
        };
        return upgraded;
    }
```

Không copy `PublicRiverStakes`, `ObstacleMetadata`, `ThdTuongCocStakes`, `RiverSnares`.

**Hệ quả:**
- `PublicRiverStakes` mặc định `false` → Hero Skill bị chặn (`HES-01`), nhánh lọc bí mật Cọc chạy và xoá metadata Cọc khỏi state người xem (`STK-10`), các bẫy sông biến mất.
- `ThdTuongCocStakes` rỗng → mất ràng buộc "mỗi quân Trần Hưng Đạo chỉ có một Cọc" (`HES-10`).
- `RiverSnares` rỗng → bẫy Bạch Đằng Trận Pháp của trận cũ biến mất (`FAC-06`).

**Đề xuất:** Bổ sung 4 field vào `UpgradeFromV3` (copy nguyên trạng từ `state`), và viết một test nâng cấp khẳng định Cọc/bẫy còn nguyên sau khi nâng.

---

## C-06 — Catalog không có hero lớp SOLDIER: không dựng được đội hình hợp lệ trên DB thật

**Rule ID liên quan:** `ARM-02`, `ARM-15`, `ARM-19` · **Mức độ: Cao (chặn toàn bộ luồng chọn đội hình)**

`lineup_slot` bắt buộc có **5 ô SOLDIER** (slot 12–16):

```24:29:Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql
INSERT INTO lineup_slot(slot_no,class_code,start_x,start_y) VALUES
 (1,'ROOK',0,0),(2,'HORSE',1,0),(3,'ELEPHANT',2,0),(4,'ADVISOR',3,0),
 (5,'GENERAL',4,0),(6,'ADVISOR',5,0),(7,'ELEPHANT',6,0),(8,'HORSE',7,0),
 (9,'ROOK',8,0),(10,'CANNON',1,2),(11,'CANNON',7,2),
 (12,'SOLDIER',0,3),(13,'SOLDIER',2,3),(14,'SOLDIER',4,3),
 (15,'SOLDIER',6,3),(16,'SOLDIER',8,3)
```

nhưng trong `02_seed_catalog.sql` có **44 dòng hero và 0 dòng `class_code = 'SOLDIER'`**. Chỉ `03_seed_dev_only.sql` có 4 hero `dev-*-soldier`, và file đó chỉ chạy khi `environment.IsDevelopment() && options.Value.SeedDevelopmentFixtures`.

**Hệ quả:** Trên DB production (hoặc DB staging chạy `IsProductionDatabase=true`), **không tồn tại hero Tốt nào** → không thể tạo đội hình 16 ô hợp lệ → toàn bộ luồng chơi bị chặn. Thiết kế đã có tên 3 loại Tốt thật (`linh-nha-le-soldier`, `linh-nha-tay-son-soldier`, `linh-nha-tran-soldier`) và `14_hero_names_and_setup_points.sql` đã gán tên cho chúng, nhưng **không script nào `INSERT` các hero này vào bảng `hero`** — script 14 chỉ có `UPDATE hero ... FROM roster`.

`docs/PLAN_PvP_And_Hero_Movement_Fixes.md` §4 cũng ghi nhận: "Khi đã có đủ hero thật cho **mọi vị trí của 16 slot** (đặc biệt hiện catalog hero lịch sử **chưa có Tốt**)…".

**Đề xuất:** Bổ sung dòng seed cho 5 hero Tốt thật (3 "Lính nhà …" theo thiết kế + 2 dự phòng nếu cần), khớp `character_id` và `setup_points` với `lineup_slot` (Tốt = 1 SP). Cần chốt: 5 ô Tốt dùng **chung một cosmetic/model** (`ARM-05`) — vậy 5 hero Tốt có thể là 5 hero khác nhau nhưng cùng cosmetic, hay 1 hero Tốt dùng cho cả 5 ô?

---

## C-07 — Lệch mã `ly-van-bu-horse` vs `ly-van-buu-horse`: link phe sai và script 15 vỡ trên DB mới

**Rule ID liên quan:** `ARM-16` · **Mức độ: Trung**

| Nơi | Mã dùng |
|---|---|
| `Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql` (dòng 214) | `ly-van-bu-horse` (một `u`) |
| `HeroChess.Api/Data/Bootstrap/13_hero_factions.sql` (dòng 47) | `ly-van-buu-horse` (hai `u`) → map vào phe Tây Sơn |
| `HeroChess.Api/Data/Bootstrap/14_hero_names_and_setup_points.sql` (dòng 37–38) | **cả hai** (`ly-van-bu-horse` và `ly-van-buu-horse`) |
| `HeroChess.Api/Data/Bootstrap/15_remove_duplicate_ly_van_buu.sql` (dòng 10) | `ly-van-buu-horse` — nếu không tồn tại thì `RAISE EXCEPTION` |

**Hệ quả:**
1. `13_hero_factions.sql` join `hero.code = 'ly-van-buu-horse'` không khớp → Lý Văn Bưu **không thuộc phe nào**, ảnh hưởng đếm ngưỡng Faction Skill (`FAC-02`).
2. `15_remove_duplicate_ly_van_buu.sql` `RAISE EXCEPTION 'The retained hero and the Tay Son faction must exist first.'` khi chạy trên DB mới chỉ có seed 02 → **script chết**.
3. `14_hero_names_and_setup_points.sql` gán tên cho cả hai mã → nếu DB có cả hai, tên trùng.

**Đề xuất:** Chốt **một** mã chuẩn (đề xuất `ly-van-buu-horse` vì 13/15/14 đã dùng mã đó và `15_...` được viết để dọn bản `ly-van-bu-horse`), rồi sửa `02_seed_catalog.sql` theo mã đó **mà không đổi `hero.id`** (`docs/PLAN_Hero_And_Command_Skills.md` §4 yêu cầu giữ nguyên ID).

---

## C-08 — Biến thể cùng nhân vật dùng chung `character_id`, và 4 nhân vật lịch sử không hero nào dùng

**Rule ID liên quan:** `ARM-04`, `ARM-17` · **Mức độ: Trung**

`LineupValidator` cấm hai hero cùng `character_id` trong một đội hình (`DUPLICATE_CHARACTER`), nhưng seed gán **cùng** `character_id` cho các biến thể cùng nhân vật:

| `character_id` | Hero dùng chung |
|---|---|
| `50000000-...-000000000009` | `nguyen-nhac-advisor` + `nguyen-nhac-general` |
| `50000000-...-000000000010` | `nguyen-lu-advisor` + `nguyen-lu-general` |
| `50000000-...-000000000011` | `tran-hung-dao-general` + `tran-hung-dao-elephant` |
| `50000000-...-000000000028` | `ly-thuong-kiet-rook` + `ly-thuong-kiet-cannon` |

Ngược lại, 4 `historical_character` **không hero nào tham chiếu**: `...000000000014` (`nguyen-nhac-tuong`), `...000000000015` (`nguyen-lu-tuong`), `...000000000017` (`tran-hung-dao-tuong2`), `...000000000035` (`ly-thuong-kiet-phao`).

**Hệ quả:** Đội hình muốn dùng **cả** Tướng và Tượng Trần Hưng Đạo (rất tự nhiên về gameplay — Tượng mới có Bạch Đằng Giang) sẽ bị chặn. Ngược lại có 4 nhân vật lịch sử "mồ côi" không dùng được.

`docs/PLAN_Hero_And_Command_Skills.md` §4 đã ghi nhận đúng vấn đề này: "Seed đang tạo `historical_character` riêng cho các biến thể cùng nhân vật … `LineupValidator` cấm trùng theo `CharacterId`, nên cách seed này lọt đội hình có hai Trần Hưng Đạo. Cần **mapping người lịch sử chung** cho các biến thể nếu giữ luật hiện tại".

**Đề xuất:** Chọn 1 trong 2 hướng:
- **(a) Giữ luật `ARM-04`**: gộp `character_id` cho các biến thể cùng nhân vật (Trần Hưng Đạo chung một nhân vật), và dùng 4 nhân vật mồ côi cho các biến thể đang cần nhân vật riêng.
- **(b) Bỏ luật `ARM-04`**: cho phép hai biến thể cùng nhân vật cùng lúc (cần chốt rõ luật này là luật gameplay hay chỉ là chống lỗi seed).

Nếu chọn (a), cần rà lại các đội hình đã lưu trước khi đổi (theo ghi chú trong plan §4).

---

## C-09 — Ngưỡng Faction Skill: 5 tướng hay 2–3 tướng?

**Rule ID liên quan:** `FAC-02` · **Mức độ: Trung**

- `docs/Decisions.md` (23/09): "Command skill phe dự kiến cần **2–3 tướng** của phe đó trong đội hình … ngưỡng chính xác sẽ cấu hình theo từng skill sau khi thiết kế."
- Code: `FactionSkillEligibility.TranRequiredHeroes = HauLeRequiredHeroes = TaySonRequiredHeroes = 5`.
- Catalog: `eligibility.minNonSoldierFactionHeroes = 5` (do `16_faction_skills.sql` và `18_faction_skill_requirements.sql` đặt).

**Hệ quả:** Với ngưỡng 5 và mỗi phe có ~15 hero (trong đó nhiều hero là biến thể trùng nhân vật — xem C-08), đội hình 16 ô chỉ có 11 ô không phải Tốt → đội hình phải có **gần như toàn bộ** các ô không-Tốt thuộc cùng một phe mới đủ 5. Điều này gần như không khả thi với điều kiện "không trùng nhân vật" (`ARM-04`).

**Đề xuất:** Chốt ngưỡng theo từng skill (đúng như `docs/Decisions.md` nói "sẽ cấu hình theo từng skill sau khi thiết kế") và sửa `FactionSkillEligibility` để đọc `eligibility.minNonSoldierFactionHeroes` từ catalog thay vì hằng số `5`.

---

## C-10 — Mô tả catalog của "Hịch Xuất Quân" không khớp runtime

**Rule ID liên quan:** `FAC-08` · **Mức độ: Thấp (chỉ mô tả, nhưng client đọc mô tả này)**

`16_faction_skills.sql` mô tả: *"Trong 6 lượt, Tốt phe mình có thể **đi thêm một ô** theo hướng hợp lệ nếu ô trung gian trống."*

Runtime (`XiangqiRulesEngine.GeneratePseudoDestinations`, case `Soldier`):
- Tốt **nhảy 2 ô** thẳng về phía địch (ô trung gian trống, ô đích trong bàn, không bị Effect chặn).
- **Sau khi đã qua sông**, Tốt được nhảy **2 ô ngang** (trái/phải).

→ Mô tả catalog thiếu cả "2 ô", thiếu cả "nhảy ngang sau khi qua sông", và nói "đi thêm một ô" (1 ô) trong khi runtime là 2 ô.

**Đề xuất:** Sửa `description` trong `16_faction_skills.sql` cho khớp runtime (hoặc sửa runtime nếu luật đúng là 1 ô — cần chốt).

---

## C-11 — "Cọc ẩn" trong mô tả catalog và tài liệu thiết kế, nhưng runtime mặc định là Cọc **công khai**

**Rule ID liên quan:** `STK-10`, `STK-11`, `STK-13`, `HES-13` · **Mức độ: Cao**

| Nơi | Nói gì |
|---|---|
| `08_step6_skills.sql` (dòng 24) | "Tạo 1 **Cọc ẩn** trên hai hàng sông; quân địch **dừng ở ô Cọc** bị ăn." |
| `docs/PLAN_Hero_And_Command_Skills.md` §1 (dòng 16), §2 | "vị trí Cọc **chỉ hiện cho chủ sở hữu**"; "đối thủ không thể tìm tọa độ Cọc bằng Network tab, WebSocket hay replay" |
| `docs/PLAN_Khoi_Phuc_Hero_Trait.md` (08/11/2026) | "**Hai bên đều thấy Cọc và phe sở hữu**." |
| Runtime | `PublicRiverStakes = rules.HeroTraitsEnabled` (mặc định `true` trong `appsettings.json`) → Cọc **công khai**; nhánh lọc bí mật trong `MatchStateProjection` **không chạy**. |
| Runtime | `BachDangGiang.PublicDescription` = "Đặt **Cọc công khai** trên sông…" (mô tả cố định, **không** dùng mô tả trong DB). |

**Hệ quả:** Mô tả trong DB và trong tài liệu cũ nói "ẩn"/"dừng ở ô", còn runtime dùng Cọc công khai kích hoạt theo đường đi. Người chơi đọc mô tả skill sẽ hiểu sai luật.

**Đề xuất:** Chốt Cọc là **công khai** hay **ẩn**. Nếu công khai (đúng runtime hiện tại và plan mới hơn): sửa mô tả `08_step6_skills.sql`, sửa `PLAN_Hero_And_Command_Skills.md` §1–§2 và mục nghiệm thu §5, đồng thời bỏ chế độ ẩn hoặc tách nó thành cờ ruleset riêng. Nếu ẩn: phải tách `PublicRiverStakes` khỏi `HeroTraitsEnabled` (xem `STK-13`).

---

## C-12 — Bootstrap tự động bỏ qua 4 script migration quan trọng

**Rule ID liên quan:** `SRV-17`, `ARM-15`, `ARM-19`, `FAC-02` · **Mức độ: Cao**

`DatabaseBootstrapHostedService` chạy: 01, 04, 05, 06, 07, 02, 08, 09, 10, 11, 12, `13_admin_module`, 17, 18 (và 03 khi Development). **Không chạy**: `13_hero_factions.sql`, `14_hero_names_and_setup_points.sql`, `15_remove_duplicate_ly_van_buu.sql`, `16_faction_skills.sql`.

**Hệ quả sau khi bootstrap trên DB mới:**
- Bảng `hero_faction` **rỗng** → **không hero nào thuộc phe** → `FACTION_REQUIREMENT_NOT_MET` cho mọi đội hình (`FAC-02`).
- `team_skill` chưa có 3 dòng Faction Skill → không chọn được Faction Skill trong 3 ô skill.
- Tên và `setup_points` theo `14_hero_names_and_setup_points.sql` (48 dòng roster) chưa được đặt — ví dụ `ly-thuong-kiet-rook` cần 8 SP, `quang-trung-general` cần 2 SP, `tran-hung-dao-general` cần 1 SP.
- `15_remove_duplicate_ly_van_buu.sql` chưa chạy → nếu DB có cả hai mã Lý Văn Bưu thì còn hai hero trùng nhân vật (xem C-07).

**Đề xuất:** Thêm 4 file vào danh sách `ExecuteFile` của `DatabaseBootstrapHostedService` (đúng thứ tự: 13 → 14 → 15 → 16), và sửa `15_...` để không `RAISE EXCEPTION` khi hero giữ lại chưa tồn tại trên DB mới (chỉ cần chạy sau khi seed có cả hai mã).

---

## C-13 — Cờ `is_enabled` của seed catalog tự mâu thuẫn với chính nó

**Rule ID liên quan:** `ARM-18` · **Mức độ: Thấp–Trung**

Trong `02_seed_catalog.sql`, phần `VALUES` ghi `is_enabled = false` cho **toàn bộ** 44 hero (kèm comment "not released"), nhưng câu `INSERT ... SELECT` lại insert **hằng số `true`**:

```154:158:Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql
INSERT INTO hero(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,description)
SELECT CASE WHEN EXISTS (SELECT 1 FROM hero existing WHERE existing.id = seeded.id::uuid AND existing.code <> seeded.code)
            THEN gen_random_uuid() ELSE seeded.id::uuid END,
       seeded.code, actual_character.id, seeded.class_code, seeded.trait_id::uuid, seeded.name,
       seeded.setup_points, true, seeded.is_test_fixture, seeded.coin_price, seeded.description
```

và mệnh đề `ON CONFLICT (code) DO UPDATE` cũng set `is_enabled = true`.

**Hệ quả:** Sau khi seed, **mọi** hero catalog đều `is_enabled = true`, kể cả hero chưa có thiết kế gameplay đầy đủ (mô tả vẫn ghi "COMPLETE" / "red-marked - no gameplay meaning"). Luật `ARM-03` (hero phải được bật) trở nên vô nghĩa vì không có gì bị chặn.

**Đề xuất:** Chọn một nguồn sự thật: hoặc để `is_enabled` lấy từ `VALUES` (và chỉ bật hero đã thiết kế xong), hoặc để toàn bộ `false` và bật dần bằng `UPDATE` có kiểm soát.

**Ghi chú phụ (doc vs code):** `docs/PLAN_PvP_And_Hero_Movement_Fixes.md` §3 viết "Seed `02_seed_catalog.sql` hiện `ON CONFLICT ... setup_points=EXCLUDED.setup_points`; bật bootstrap lại sẽ **ghi đè** SP đã chỉnh tay trong DB". Câu này **không còn đúng** với code hiện tại: mệnh đề `ON CONFLICT (code) DO UPDATE` của seed 02 chỉ set `character_id, class_code, trait_id, name, description, is_enabled` — **không** set `setup_points`. Tức là bật bootstrap lại **không** ghi đè SP đã chỉnh tay; nhưng `14_hero_names_and_setup_points.sql` (chạy tay) **có** set `setup_points` cho 48 dòng roster → vẫn còn hai nguồn ghi SP. Cần chốt nguồn sự thật của SP: seed 02, script 14, hay DB.

---

## C-14 — Tài liệu thiết kế và code lệch về "giới hạn hành động" (mở rộng skill)

**Rule ID liên quan:** `END-01`, `TRN-08` · **Mức độ: Thấp (đã được xử lý trong code, tài liệu còn cũ)**

`docs/PLAN_Hero_And_Command_Skills.md` §4 (dòng 60) nêu: "**Không kết luận thua chỉ vì hết nước di chuyển.** … Phải kiểm tra còn ít nhất một hành động hợp lệ gồm move, hero skill, command skill". Runtime đã làm đúng (`END-01` dùng `MatchTurnRules.Finish` + `MatchCommandService.FindLegalSkillAction`). Điểm này **không còn là mâu thuẫn**, được ghi ở đây để tránh hiểu nhầm là còn tồn đọng.

---

## C-15 — `docs/Decisions.md` còn mô tả trạng thái cũ (skill là stub)

**Rule ID liên quan:** `END-01`, `CMD-01`… · **Mức độ: Thấp (vấn đề tài liệu)**

`docs/Decisions.md` (mục "Trạng thái triển khai liên quan") vẫn ghi: "Skill vẫn đang là extension/stub; chưa triển khai hiệu ứng, cooldown dispatcher hay kiểm tra ngưỡng phe cụ thể" và "dispatcher skill … hiện trả `SKILL_NOT_IMPLEMENTED`". Thực tế: dispatcher đã có (`CommandSkillDispatcher`), CD đã giảm theo lượt chung (`TRN-03`), ngưỡng phe đã có (`FAC-02`). `XiangqiRulesEngine.ApplyTeamSkill` vẫn còn stub `SKILL_NOT_IMPLEMENTED` nhưng **không còn được gọi** (luồng thật đi qua `MatchTurnRules.ApplySkill`).

**Đề xuất:** Cập nhật `docs/Decisions.md` để phản ánh trạng thái hiện tại, hoặc ghi rõ "mục này mô tả trạng thái tại 23/09".

---

## Danh sách câu hỏi cần team xác nhận

| # | Câu hỏi | Liên quan | Chặn cái gì |
|---|---|---|---|
| Q1 | Cọc kích hoạt ở **ô dừng** hay ở **bất kỳ giao điểm trên đường**? | C-01 | Luật cốt lõi của Hero Skill Bạch Đằng Giang; test và tài liệu |
| Q2 | Cọc là **công khai** hay **ẩn** với đối thủ? | C-11, `STK-13` | UI, replay, bảo mật thông tin trận |
| Q3 | Phản Kỳ: chủ Effect **huỷ thủ công** (phương án A) hay **tự động** (phương án B)? Nếu A, chốt thời điểm chốt steal (đầu lượt chủ hay cuối lượt tên trộm)? | C-02 | Toàn bộ cơ chế Effect |
| Q4 | `Phá Trận Đoạt Phong` nhắm vào **một quân** hay **một Effect bất kỳ**? | C-03 | Handler đang chết |
| Q5 | 4 Command Skill (Vạn Cọc, Binh Lâm, Phản Kỳ, Phá Trận) có lên phát hành không? | C-04 | Catalog, đội hình |
| Q6 | Tốt thật: dùng **3 "Lính nhà …"** theo phe, hay **1 loại Tốt chung**? 5 ô Tốt dùng chung 1 cosmetic nghĩa là gì với `ARM-05`? | C-06, `ARM-19` | Chặn toàn bộ luồng dựng đội hình trên DB production |
| Q7 | Mã chuẩn của Lý Văn Bưu: `ly-van-bu-horse` hay `ly-van-buu-horse`? | C-07 | Link phe, script 15 |
| Q8 | Giữ luật cấm trùng nhân vật (`ARM-04`) hay bỏ? Nếu giữ, gộp `character_id` cho 4 cặp biến thể và xử lý 4 nhân vật mồ côi thế nào? | C-08 | Cấu trúc lineup |
| Q9 | Ngưỡng Faction Skill là **5** hay **2–3**, và có khác nhau theo từng skill không? | C-09 | `FactionSkillEligibility` |
| Q10 | `Hịch Xuất Quân`: Tốt nhảy **2 ô** (runtime) hay **1 ô** (mô tả catalog)? Nhảy ngang 2 ô sau khi qua sông có đúng ý không? | C-10 | Mô tả + runtime |
| Q11 | `is_enabled` của catalog lấy từ `VALUES` hay hard-code `true`? Và nguồn sự thật của `setup_points` là seed hay DB? | C-13 | Catalog, cân bằng SP |
| Q12 | Có bật `MatchRuntime:NewFactionSkillsEnabled` trong phát hành này không? | `FAC-09` | Tính dụng của cả hệ Faction Skill |

---

## Phụ lục — Bản đồ Rule ID → mâu thuẫn

| Rule ID | Mâu thuẫn liên quan |
|---|---|
| `STK-01`, `STK-02`, `STK-03`, `STK-07` | C-01 |
| `STK-10`, `STK-11`, `STK-13`, `HES-13` | C-11 |
| `CMD-18`, `CMD-19`, `CMD-20`, `CMD-25` | C-02 |
| `CMD-21`, `CMD-22` | C-03 |
| `CMD-23` | C-04 |
| `SRV-15` | C-05 |
| `ARM-02`, `ARM-15`, `ARM-19` | C-06 |
| `ARM-16` | C-07 |
| `ARM-04`, `ARM-17` | C-08 |
| `FAC-02` | C-09, C-12 |
| `FAC-08` | C-10 |
| `ARM-18` | C-13 |
| `FAC-09` | Q12 |
| `END-01`, `TRN-08` | C-14 |
| `END-01`, `CMD-01`…`CMD-08` | C-15 |
