# Hero Chess — Đặc tả luật chơi (Game Rules Specification)

> Phiên bản: 12/11/2026 · Phạm vi: gameplay runtime hiện có trong `HeroChess.Rules` + `HeroChess.Api`.
> Tài liệu này **chưa được phê duyệt**. Nó mô tả luật đang chạy trong code, không phải luật đã chốt cuối cùng.
> Danh sách mâu thuẫn và câu hỏi cần team xác nhận nằm ở `docs/gameplay/RULE_CONFLICTS_AND_OPEN_QUESTIONS.md`.

## 0. Cách đọc tài liệu

Mỗi luật có một **Rule ID** theo nhóm:

| Tiền tố | Nhóm |
|---|---|
| `BRD` | Bàn cờ và hình học |
| `MOV` | Luật di chuyển theo class / hero trait |
| `ARM` | Xây dựng đội hình (lineup / quân / điểm SP / slot) |
| `TRN` | Hệ thống lượt, đồng hồ, giới hạn hành động |
| `HES` | Hero Skill (kỹ năng chủ động của hero) |
| `HRT` | Hero Trait (nội tại / di chuyển đặc biệt) |
| `CMD` | Command Skill (3 ô skill của đội hình) |
| `FAC` | Faction Skill (skill theo phe) |
| `STK` | Cọc / River Stakes |
| `MOD` | Chế độ trận |
| `END` | Thắng / thua / hòa |
| `SRV` | Kiểm tra phía server |

Trạng thái mỗi luật:

| Trạng thái | Ý nghĩa |
|---|---|
| **IMPLEMENTED** | Có bằng chứng trực tiếp trong gameplay runtime (code +/hoặc test). |
| **PLANNED** | Có thiết kế được xác nhận trong tài liệu thiết kế nhưng runtime chưa hiện thực. |
| **UNCONFIRMED** | Thiếu bằng chứng, hoặc luật tồn tại nhưng chưa được ai chốt. |
| **CONFLICT** | Code và tài liệu thiết kế (hoặc code với code) đang mâu thuẫn. |

Quy ước nguồn tham chiếu: `đường/dẫn/file.cs` + tên symbol/hàm. Không có source reference nào trong tài liệu này là bịa.

### Tổng quan trạng thái

Tổng **158 luật** được liệt kê trong tài liệu này:

| Trạng thái | Số luật | Ghi chú |
|---|---:|---|
| **IMPLEMENTED** | 140 | Luật thuần IMPLEMENTED (trong đó `FAC-04` có ghi chú: `docs/Decisions.md` nói "lúc bắt đầu trận" còn code xét ở bước chọn/confirm đội hình — hành vi không thu hồi thì giống nhau). |
| **CONFLICT** | 10 | Luật thuần CONFLICT: `ARM-15`, `ARM-16`, `ARM-17`, `ARM-18`, `CMD-19`, `CMD-20`, `CMD-22`, `CMD-23`, `STK-01`, `SRV-17`. Xem `RULE_CONFLICTS_AND_OPEN_QUESTIONS.md` (C-01 … C-15). |
| **PLANNED** | 2 | `ARM-19` (dữ liệu Tốt thật chưa seed), `STK-13` (chế độ Cọc ẩn chưa tách cờ). |
| **UNCONFIRMED** | 2 | `CMD-24` (ai được phá Rào), `CMD-25` (cách chủ Effect huỷ khi bị chiếm). |
| **Hỗn hợp** | 4 | `FAC-02` (IMPLEMENTED / CONFLICT), `FAC-08` (IMPLEMENTED / CONFLICT mô tả catalog), `FAC-09` (IMPLEMENTED / PLANNED), `SRV-15` (IMPLEMENTED / CONFLICT mất dữ liệu khi nâng cấp state). |

Theo nhóm: `BRD` 8 · `MOV` 11 · `ARM` 19 · `HRT` 8 · `HES` 13 · `CMD` 25 · `FAC` 9 · `STK` 13 · `TRN` 12 · `MOD` 10 · `END` 13 · `SRV` 17.

---

## 1. Bàn cờ và hình học

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **BRD-01** | Bàn cờ 9×10, toạ độ `x` 0..8 (cột), `y` 0..9 (hàng). `IsOnBoard` kiểm tra cả hai trục. | Mọi ô và mục tiêu skill phải nằm trong bàn. | — | IMPLEMENTED | `HeroChess.Rules/GameModels.cs` (`BoardPoint.IsOnBoard`) |
| **BRD-02** | Nửa sân nhà: Đỏ `y ≤ 4`, Đen `y ≥ 5`. | `RulesGeometry.IsHomeSide(side, y)`. | — | IMPLEMENTED | `HeroChess.Rules/MovementHandlers.cs` (`RulesGeometry.IsHomeSide`) |
| **BRD-03** | Sông nằm ở hai hàng `y = 4` và `y = 5`. Một nước đi "cắt ngang sông" khi đi từ `y ≤ 4` sang `y ≥ 5` (Đỏ) hoặc ngược lại (Đen). | `XiangqiRulesEngine.IsRiverCrossing`. | — | IMPLEMENTED | `HeroChess.Rules/XiangqiRulesEngine.cs` |
| **BRD-04** | Ba "đường cắt ngang sông" (river path) là các cột `4, 5, 6` (U-GEO = A: 1 đường = 1 cột). | `RiverGeometry.RiverColumns`. | Chỉ dùng cho Vạn Cọc / Binh Lâm. | IMPLEMENTED | `HeroChess.Rules/Skills/RiverGeometry.cs` |
| **BRD-05** | Cung: `x` 3..5; Đỏ `y` 0..2, Đen `y` 7..9. Tướng chuẩn và Si không ra khỏi cung. | `XiangqiRulesEngine.InPalace`. | — | IMPLEMENTED | `HeroChess.Rules/XiangqiRulesEngine.cs` |
| **BRD-06** | Bàn khởi đầu 16 ô/quân: Xe(0,0) Mã(1,0) Tượng(2,0) Si(3,0) Tướng(4,0) Si(5,0) Tượng(6,0) Mã(7,0) Xe(8,0), Pháo(1,2) Pháo(7,2), Tốt ở `x = 0,2,4,6,8` `y=3`. Đen xoay 180°: `(8-x, 9-y)`. | `lineup_slot` seed; `MatchSelectionService.Start`. | — | IMPLEMENTED | `Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql`; `HeroChess.Api/Services/MatchSelectionService.cs` |
| **BRD-07** | Cọc (Bạch Đằng Giang) chỉ được đặt ở hai hàng sông `y = 4` hoặc `y = 5`. | `BachDangGiang.Execute` kiểm `py is not (4 or 5)`. | Ô phải trống. | IMPLEMENTED | `HeroChess.Rules/Skills/BachDangGiang.cs` |
| **BRD-08** | Bẫy Faction Skill (Bạch Đằng Trận Pháp) chỉ đặt ở `y = 4` hoặc `y = 5`, đúng 3 ô khác nhau. | `FactionSkillHandler.Execute` (Trap). | Ô phải trống, không có vật cản. | IMPLEMENTED | `HeroChess.Rules/Skills/FactionSkillHandler.cs` |

---

## 2. Xây dựng đội hình (Army Building)

Nguồn chính: `HeroChess.Api/Services/LineupValidator.cs` (`ValidateAsync`), `HeroChess.Api/Services/LineupService.cs`, `HeroChess.Api/Services/MatchSelectionService.cs` (`Freeze`, `Start`), `Hero_Chess_DB_Prototype_v0_1/01_schema.sql`, `02_seed_catalog.sql`.

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **ARM-01** | Đội hình phải có **đúng 16 ô** (`ENTRY_COUNT`), mỗi ô khớp một `lineup_slot` (slot 1..16, mỗi slot dùng một lần → `DUPLICATE_SLOT`, `MISSING_SLOT`). | `request.Entries.Count != 16`; slot phải tồn tại trong bảng `lineup_slot`. | — | IMPLEMENTED | `LineupValidator.ValidateAsync`; `02_seed_catalog.sql` (bảng `lineup_slot`) |
| **ARM-02** | Mỗi slot có **class cố định**; hero đặt vào slot phải đúng class của slot, không sai (`WRONG_CLASS`). | `hero.ClassCode != slot.ClassCode`. | Tượng 2, Si 2, Tướng 1, Xe 2, Pháo 2, Mã 2, Tốt 5. | IMPLEMENTED | `LineupValidator.ValidateAsync`; `02_seed_catalog.sql` (`chess_class.required_count`) |
| **ARM-03** | Hero phải **tồn tại** (`HERO_NOT_FOUND`), **được bật** (`HERO_DISABLED`) và **người chơi đã sở hữu** (`HERO_NOT_OWNED`). | Kiểm tra `hero`, `hero.IsEnabled`, `player_hero`. | — | IMPLEMENTED | `LineupValidator.ValidateAsync` |
| **ARM-04** | **Không được trùng nhân vật lịch sử**: cùng một `character_id` không xuất hiện hai lần trong một đội hình (`DUPLICATE_CHARACTER`). | Nhóm theo `hero.character_id`. | Chỉ áp dụng hero có `character_id` khác null. | IMPLEMENTED | `LineupValidator.ValidateAsync` |
| **ARM-05** | **5 ô Tốt phải dùng chung một cosmetic/model** (`INCONSISTENT_SOLDIER_COSMETIC`). | Đủ 5 ô Tốt → 5 `cosmeticId` phải giống nhau. | Chỉ kiểm tra khi đủ 5 ô Tốt. | IMPLEMENTED | `LineupValidator.ValidateAsync` |
| **ARM-06** | Cosmetic phải **thuộc hero** đang đặt, **được bật**, **đã sở hữu**; ngược lại `INVALID_COSMETIC` / `COSMETIC_NOT_OWNED`. | `cosmetic.HeroId == hero.Id && cosmetic.IsEnabled`. | Cosmetic tùy chọn (`null` = mặc định). | IMPLEMENTED | `LineupValidator.ValidateAsync` |
| **ARM-07** | **Ngân sách SP**: tổng `setup_points` của 16 hero không được vượt `ruleset.setup_budget` (mặc định **50**) → `SP_BUDGET_EXCEEDED`. | `totalSp > ruleset.SetupBudget`. | Giá trị budget đóng băng theo ruleset trong trận. | IMPLEMENTED | `LineupValidator.ValidateAsync`; `01_schema.sql` (`setup_budget`); `02_seed_catalog.sql` (ruleset `prototype-v0.1`: 50) |
| **ARM-08** | Đội hình phải có **đúng 3 ô Command Skill** (`SKILL_COUNT`), slot **1..3 duy nhất** (`INVALID_SKILL_SLOTS`), **không trùng skill** (`DUPLICATE_SKILL`), skill phải tồn tại và `is_enabled` (`SKILL_UNAVAILABLE`). | `request.Skills.Count != 3`; `slotNo` ∈ {1,2,3}. | — | IMPLEMENTED | `LineupValidator.ValidateAsync` |
| **ARM-09** | Trong 3 ô skill, **tối đa 1 skill thuộc phe** (`MULTIPLE_FACTION_SKILLS`). | `skills.Count(x => x.FactionId is not null) > 1`. | Skill thuộc phe = `team_skill.faction_id` khác null. | IMPLEMENTED | `LineupValidator.ValidateAsync` |
| **ARM-10** | Tên đội hình 1..80 ký tự (`INVALID_NAME`); ruleset phải tồn tại và đang active (`RULESET_UNAVAILABLE`). | `string.IsNullOrWhiteSpace(name) \|\| name.Length > 80`. | — | IMPLEMENTED | `LineupValidator.ValidateAsync` |
| **ARM-11** | Đội hình được **validate lại** khi chọn và khi confirm cho trận; lineup cũ/sai ruleset bị từ chối (`RULESET_MISMATCH`, `STALE_REVISION`). | `MatchSelectionService.SelectAsync` / `ConfirmAsync` gọi lại `ValidateAsync`. | — | IMPLEMENTED | `MatchSelectionService.SelectAsync`, `ConfirmAsync` |
| **ARM-12** | Khi trận bắt đầu, đội hình được **đóng băng** (`FrozenLineup`): hero, class, SP, vị trí xuất phát, cosmetic, trait/parameters, và 3 skill kèm CD/`MaxUses`. | `MatchSelectionService.Freeze`. | Trait chỉ được đóng băng khi ruleset bật `HeroTraitsEnabled`. | IMPLEMENTED | `MatchSelectionService.Freeze`; `MatchModels.cs` (`FrozenLineup`, `FrozenSkill`) |
| **ARM-13** | Chế độ `bot`: bot dùng **bản sao đội hình** của người chơi (cùng hero/skill, `LineupId` và `Revision` được reset). | `SelectAsync` / `ConfirmAsync` copy `frozen` cho bot. | Bot không có lineup riêng. | IMPLEMENTED | `MatchSelectionService.SelectAsync`, `ConfirmAsync` |
| **ARM-14** | Cấu hình trait Bạch Đằng Giang sai kiểu/giá trị ≤ 0 → trận bị chặn lúc chọn đội hình (`INVALID_HERO_SKILL_CONFIG`). | `BachDangGiang.TryReadParameters`. | Chỉ kiểm tra hero có trait `tran_hung_dao_tuong.coc`. | IMPLEMENTED | `MatchSelectionService.Freeze`; `BachDangGiang.TryReadParameters` |
| **ARM-15** | **Catalog thiếu hero lớp SOLDIER**: `02_seed_catalog.sql` seed 44 hero, **0** hero có `class_code = 'SOLDIER'` (chỉ có 4 fixture `dev-*-soldier` trong `03_seed_dev_only.sql`, chỉ chạy ở Development + `SeedDevelopmentFixtures=true` + `IsProductionDatabase=false`). Trong khi `lineup_slot` bắt buộc có **5** ô SOLDIER → không thể tạo đội hình hợp lệ từ catalog thật. | `LineupValidator` yêu cầu đủ 16 slot đúng class. | `14_hero_names_and_setup_points.sql` tham chiếu `linh-nha-le-soldier`, `linh-nha-tay-son-soldier`, `linh-nha-tran-soldier` nhưng **không script nào** insert các hero này vào bảng `hero`. | **CONFLICT** (thiếu dữ liệu seed) | `Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql`; `03_seed_dev_only.sql`; `HeroChess.Api/Data/Bootstrap/14_hero_names_and_setup_points.sql`; `DatabaseBootstrapHostedService.cs` |
| **ARM-16** | **Lệch mã hero `ly-van-bu-horse` vs `ly-van-buu-horse`**: catalog seed dùng `ly-van-bu-horse` (một `u`), còn `13_hero_factions.sql` map `ly-van-buu-horse` (hai `u`) vào phe Tây Sơn → link không khớp, hero không thuộc phe nào. `15_remove_duplicate_ly_van_buu.sql` yêu cầu `ly-van-buu-horse` tồn tại và `RAISE EXCEPTION` nếu không → **bootstrap fail trên DB mới**. | `15_remove_duplicate_ly_van_buu.sql` dòng 9–14. | Script này **không** nằm trong danh sách file bootstrap của `DatabaseBootstrapHostedService` (chạy tay). | **CONFLICT** | `02_seed_catalog.sql` (dòng 214); `HeroChess.Api/Data/Bootstrap/13_hero_factions.sql` (dòng 47); `15_remove_duplicate_ly_van_buu.sql` |
| **ARM-17** | **Biến thể cùng nhân vật dùng chung `character_id`** trong seed → `ARM-04` cấm đưa cả hai vào đội hình: `tran-hung-dao-general` + `tran-hung-dao-elephant` (cùng `...0011`), `nguyen-nhac-advisor` + `nguyen-nhac-general` (`...0009`), `nguyen-lu-advisor` + `nguyen-lu-general` (`...0010`), `ly-thuong-kiet-rook` + `ly-thuong-kiet-cannon` (`...0028`). Các `historical_character` `...0014`, `...0015`, `...0017`, `...0035` **không hero nào tham chiếu** (orphan). | `LineupValidator.ValidateAsync` (`DUPLICATE_CHARACTER`); `02_seed_catalog.sql` cột `character_id`. | `docs/PLAN_Hero_And_Command_Skills.md` §4 ghi nhận vấn đề này và yêu cầu mapping nhân vật chung hoặc tách nhân vật. | **CONFLICT** | `02_seed_catalog.sql`; `HeroChess.Api/Services/LineupValidator.cs`; `docs/PLAN_Hero_And_Command_Skills.md` §4 |
| **ARM-18** | **Cờ `is_enabled` của catalog mâu thuẫn với chính file seed**: comment trong `02_seed_catalog.sql` ghi "is_enabled = false: not released", giá trị trong `VALUES` cũng là `false`, nhưng câu `SELECT` lại insert **hằng số `true`** cho `is_enabled`, và `ON CONFLICT (code) DO UPDATE` cũng set `is_enabled = true`. Kết quả: **toàn bộ 44 hero catalog được bật** khi seed. | `02_seed_catalog.sql` (câu `SELECT ... true, seeded.is_test_fixture, ...` và mệnh đề `ON CONFLICT`). | Cũng là lý do `ARM-03` (hero phải `is_enabled`) không chặn được hero catalog chưa thiết kế xong. | **CONFLICT** | `Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql` (dòng 154–158, 220–222) |
| **ARM-19** | **Tốt thật theo phe chưa tồn tại**: thiết kế định danh 3 loại Tốt — `linh-nha-le-soldier` ("Lính nhà Lê"), `linh-nha-tay-son-soldier` ("Lính nhà Tây Sơn"), `linh-nha-tran-soldier` ("Lính nhà Trần") — và script `14_hero_names_and_setup_points.sql` đã gán tên cho chúng, nhưng **không script nào `INSERT` các hero này vào bảng `hero`**. | Script `14_...` chỉ có `UPDATE hero ... FROM roster` (không có `INSERT`). | Cho tới khi có dòng seed, 5 ô Tốt trong mọi đội hình phải lấy từ `dev-*-soldier` (chỉ có ở Development). | PLANNED (thiết kế có, dữ liệu chưa seed) | `HeroChess.Api/Data/Bootstrap/14_hero_names_and_setup_points.sql` (dòng 49–51); `Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql` |

---

## 3. Luật di chuyển theo class

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **MOV-01** | **Tướng chuẩn**: đi 1 ô theo 4 hướng thẳng, trong cung. | Không bị chặn bởi hiệu ứng. | Không ra khỏi cung. | IMPLEMENTED | `XiangqiRulesEngine.GeneratePseudoDestinations` (case `General`) |
| **MOV-02** | **Si**: đi 1 ô chéo, trong cung. | Như trên. | Trong cung. | IMPLEMENTED | `XiangqiRulesEngine.GeneratePseudoDestinations` (case `Advisor`) |
| **MOV-03** | **Tượng chuẩn**: đi 2 ô chéo, mắt (ô giữa) phải trống, **không qua sông**. | `!RulesGeometry.IsBlocked(state, eye)`. | Nửa sân nhà. | IMPLEMENTED | `XiangqiRulesEngine.GeneratePseudoDestinations` (case `Elephant`) |
| **MOV-04** | **Xe**: đi thẳng xa, dừng ở quân địch (ăn) hoặc quân đồng minh (chặn). | — | Vật cản chặn tia. | IMPLEMENTED | `XiangqiRulesEngine.RayMoves(cannon: false)` |
| **MOV-05** | **Pháo**: đi thẳng xa; cần đúng một ngòi (quân hoặc vật cản) để ăn quân sau ngòi. | Ngòi = ô có quân sống **hoặc** vật cản. | — | IMPLEMENTED | `XiangqiRulesEngine.RayMoves(cannon: true)` |
| **MOV-06** | **Mã**: đi hình chữ L (2×1), **ô chân** phải trống. | `!RulesGeometry.IsBlocked(state, leg)`. | — | IMPLEMENTED | `XiangqiRulesEngine.GeneratePseudoDestinations` (case `Horse`) |
| **MOV-07** | **Tốt**: đi 1 ô thẳng về phía địch; sau khi đã qua sông (`Đỏ y ≥ 5`, `Đen y ≤ 4`) được đi ngang 1 ô. | — | Không đi lùi. | IMPLEMENTED | `XiangqiRulesEngine.GeneratePseudoDestinations` (case `Soldier`) |
| **MOV-08** | **Không được ăn Tướng**: mọi nước đi có đích đặt vào ô Tướng đều bị loại khỏi danh sách nước hợp lệ. | `target?.Class == PieceClass.General → skip`. | — | IMPLEMENTED | `XiangqiRulesEngine.GenerateLegalActions` |
| **MOV-09** | **Không được tự chiếu**: mọi nước đi khiến Tướng bên đi bị chiếu (kể cả do lộ mặt hai Tướng) đều bị loại. | `!IsInCheck(candidate, actor)`. | — | IMPLEMENTED | `XiangqiRulesEngine.GenerateLegalActions` |
| **MOV-10** | **Chiếu**: Tướng bị chiếu khi (a) có quân địch tấn công được ô Tướng, (b) hai Tướng đối mặt cùng cột không có vật cản, hoặc (c) Tướng không còn tồn tại. | `IsInCheck(state, side)`. | Quân có Khiên làm Tướng không bị chiếu (xem CMD-05). | IMPLEMENTED | `XiangqiRulesEngine.IsInCheck` |
| **MOV-11** | Không ăn được quân đang có **Khiên** (hiệu ứng `shield` còn lượt). | `target.Effects.Any(shield && RemainingTurns > 0) → skip`. | — | IMPLEMENTED | `XiangqiRulesEngine.GenerateLegalActions` |

---

## 4. Hero Trait (nội tại / di chuyển đặc biệt)

Các trait dưới đây được nạp vào bàn cờ dưới dạng `MovementImplementationKey` chỉ khi `ruleset.HeroTraitsEnabled = true` (mặc định `true` trong `appsettings.json`). Nguồn: `MatchSelectionService.Freeze`, `MatchSelectionService.Start`.

| Rule ID | Hero | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|---|
| **HRT-01** | Trần Bình Trọng, Bùi Thị Xuân | `elephant.diagonal_range`: đi chéo 1–2 ô trong nửa sân nhà. | Handler `HomeDiagonalHandler(2)`. | Không qua sông. | IMPLEMENTED | `MovementHandlers.cs`; `02_seed_catalog.sql` |
| **HRT-02** | Dã Tượng | `elephant.alternating_distance`: đi chéo 1–4 ô, **không lặp lại độ dài nước vừa đi**. | `TraitState["lastMoveDistance"]`. | Trong nửa sân nhà; không qua sông. | IMPLEMENTED | `MovementHandlers.cs` (`WildElephantHandler`) |
| **HRT-03** | Trưng Trắc, Trưng Nhị | `elephant.river_crossing`: đi chéo 1–2 ô, mắt phải trống, **được qua sông**. | Handler `RiverCrossingDiagonalHandler`. | Tổng tối đa 2 bước chéo. | IMPLEMENTED | `MovementHandlers.cs`; `02_seed_catalog.sql` |
| **HRT-04** | Lê Lợi | `general.king_move`: đi 1 ô theo 8 hướng, trong cung. | Handler `LeLoiHandler`. | Trong cung. | IMPLEMENTED | `MovementHandlers.cs` |
| **HRT-05** | Trần Hưng Đạo (Tướng) | `general.orthogonal_range_1_no_palace`: đi 1 ô thẳng, **được ra khỏi cung**, **không qua sông**. | Handler `TranHungDaoGeneralHandler`. | 1 ô, 4 hướng. | IMPLEMENTED | `MovementHandlers.cs` |
| **HRT-06** | Phạm Ngũ Lão | `rook.hoanh_soc` — **Hoành Sóc Giang Sơn**: sau khi chính quân này ăn một quân, nước đi kế tiếp của chính quân ấy được xuyên **tối đa một** quân đồng minh. | `TraitState["hoanhSocCharged"] == 1`. | Không xuyên vật cản; quân địch sau quân đồng minh vẫn là mục tiêu hợp lệ; quyền không cộng dồn, bị tiêu khi quân này đi bất kỳ nước nào. | IMPLEMENTED | `MovementHandlers.cs` (`HoanhSocHandler`); `XiangqiRulesEngine.GrantHoanhSocChargeAfterCapture` |
| **HRT-07** | Lý Thường Kiệt (Xe) | `rook.ly_thuong_kiet`: Xe đi thẳng như Xe thường nhưng **Thành/Rào không chặn**; có thể dừng tại ô Thành/Rào để phá, hoặc đi qua mà vật cản vẫn còn. | Handler `LyThuongKietXeHandler`. | Quân đồng minh/địch vẫn chặn tia như Xe thường. | IMPLEMENTED | `MovementHandlers.cs`; `XiangqiRulesEngine.MoveUnchecked` |
| **HRT-08** | Lý Thường Kiệt (Pháo) | `cannon.ly_thuong_kiet`: Pháo dùng được **cả Thành lẫn Rào làm ngòi**; có thể đi vào ô Thành để phá. | Handler `LyThuongKietPhaoHandler`. | Pháo thường không có quyền này. | IMPLEMENTED | `MovementHandlers.cs`; `XiangqiRulesEngine.MoveUnchecked` |

---

## 5. Hero Skill (kỹ năng chủ động của hero)

Hero Skill kích hoạt bằng action `hero_active` (`pieceId`, `skillCode`, `target`). Luồng: `MatchCommandService` (case `hero_active`) → `MatchTurnRules.ApplyHeroSkill`.

Điều kiện chung cho mọi Hero Skill:

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **HES-01** | Hero Skill chỉ dùng được khi `state.PublicRiverStakes == true` (tức ruleset bật `HeroTraitsEnabled`) và `state.Result == null`. | `state.PublicRiverStakes == true`; `state.Result == null`. | `PublicRiverStakes` gắn với cờ `HeroTraitsEnabled` → tắt trait là tắt luôn Hero Skill (xem STK-11). | IMPLEMENTED | `MatchCommandService` (case `hero_active`); `MatchTurnRules.ApplyHeroSkill` |
| **HES-02** | Hero phải thuộc phe đang đi, còn sống, và `TraitImplementationKey` phải khớp `skillCode` gửi lên. | `hero.Side == state.SideToMove`; `hero.IsAlive`; `TraitImplementationKey == skillCode`. | — | IMPLEMENTED | `MatchTurnRules.ApplyHeroSkill` |
| **HES-03** | Hero Skill được phép là `quang_trung.special_move` hoặc `tran_hung_dao_tuong.coc`; key khác trả `HERO_SKILL_NOT_AVAILABLE`. | Danh sách key cho phép trong `MatchTurnRules.ApplyHeroSkill`. | — | IMPLEMENTED | `MatchTurnRules.ApplyHeroSkill` |
| **HES-04** | Dùng Hero Skill **thay cho một nước đi**: `Version`, `TurnIndex`, `CountedActions` tăng 1, chuyển lượt, reset chuỗi timeout của người dùng, rồi tick vòng đời lượt mới. | Skill thành công. | Không thể vừa dùng skill vừa đi quân trong cùng lượt. | IMPLEMENTED | `MatchTurnRules.AdvanceSkill`; `MatchTurnRules.AfterAction` |
| **HES-05** | Hero Skill không được để lại Tướng mình bị chiếu; nếu có → lỗi `KING_IN_CHECK` và **không** tiêu lượt/CD. | `engine.IsInCheck(state, actor)` sau khi kích hoạt. | Lỗi → không mutate state. | IMPLEMENTED | `MatchTurnRules.ApplyHeroSkill` (`IsInCheck` sau khi kích hoạt) |
| **HES-06** | Hero Skill làm một hành động: không được vừa dùng skill vừa đi quân trong cùng lượt. | Mỗi request chỉ chứa một `action`. | — | IMPLEMENTED | `MatchCommandService` (mỗi request chỉ một action) |

| Rule ID | Hero | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|---|
| **HES-07** | **Quang Trung** (`quang_trung.special_move`) | Kỹ năng chủ động "Hành Quân Thần Tốc": Tướng đi thẳng **1–3 ô**. | Chỉ dùng được khi `TraitState["cooldownReady"] == 1` và `quangTrungCooldownRemaining` không > 0; không bị "đóng rễ". | Không xuyên quân/vật cản; **không qua sông**; không ăn Tướng; không tự chiếu. | IMPLEMENTED | `XiangqiRulesEngine.GenerateQuangTrungSpecialMoves`; `MovementHandlers.cs` (`OrthogonalRangeHandler(3)`) |
| **HES-08** | **Quang Trung** — nước đi thường | Nước đi thường của Quang Trung dùng handler 1 ô (`general.orthogonal_range_1_no_palace`), **không** phải handler 1–3 ô. | `Freeze` ghi đè `MovementImplementationKey`. | Ra được khỏi cung, không qua sông. | IMPLEMENTED | `MatchSelectionService.Freeze` |
| **HES-09** | **Quang Trung** — hồi chiêu | CD riêng **3 lượt chung**, lưu trên `PieceId` (không dùng `SkillStates` của Command Skill). | `TraitState["quangTrungCooldownRemaining"]`. | Hồi chiêu của Command Skill **không** làm Quang Trung sẵn sàng và ngược lại. | IMPLEMENTED | `XiangqiRulesEngine.ApplyHeroSpecialMove`; `TurnLifecycle.ApplyCore` |
| **HES-10** | **Trần Hưng Đạo — Tượng** (`tran_hung_dao_tuong.coc`) | **Bạch Đằng Giang**: đặt một **Cọc** ở một ô trống trên `y = 4` hoặc `y = 5`. | Quân phải là Tượng, `TraitKind == "active"`, `TraitImplementationKey == tran_hung_dao_tuong.coc`, thuộc phe đang đi, còn sống, CD = 0. | Mỗi quân Trần Hưng Đạo–Tượng chỉ có **một** Cọc tồn tại tại một thời điểm; CD hết mà Cọc cũ còn thì phải chờ (`STAKE_ALREADY_ACTIVE`). | IMPLEMENTED | `HeroChess.Rules/Skills/BachDangGiang.cs` |
| **HES-11** | **Bạch Đằng Giang** — tham số | CD và thời lượng đọc từ `hero_trait.parameters` của snapshot trận (`cooldownTurns`, `durationTurns`); mặc định **CD 5, tồn tại 3** lượt chung. | `BachDangGiang.TryReadParameters`; giá trị phải là số nguyên > 0, sai thì trận bị chặn lúc chọn đội hình (`INVALID_HERO_SKILL_CONFIG`). | — | IMPLEMENTED | `BachDangGiang.cs`; `MatchSelectionService.Freeze` |
| **HES-12** | **Bạch Đằng Giang** — đặt ô | Ô đích phải trống, `y ∈ {4, 5}`. | Nếu đã có Cọc (cùng loại) → `CELL_OCCUPIED`. | IMPLEMENTED | `BachDangGiang.Execute` |
| **HES-13** | **Bạch Đằng Giang** — mô tả công khai | Mô tả hiển thị cho client dùng chuẩn cố định `BachDangGiang.PublicDescription`, không dùng `hero_trait.description` trong DB. | — | Mô tả trong DB (`08_step6_skills.sql`) nói "Cọc **ẩn**" trong khi runtime dùng Cọc công khai → xem CONFLICT. | IMPLEMENTED | `BachDangGiang.PublicDescription`; `CatalogController.Trait` |

---

## 6. Command Skill (3 ô skill của đội hình)

Điều kiện chung do dispatcher áp dụng (`CommandSkillDispatcher.Dispatch`):

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **CMD-01** | Phải đúng lượt (`state.SideToMove == actorSide`), trận chưa kết thúc, skill nằm trong lineup của actor, **CD = 0**, còn lượt dùng (`UsesRemaining != 0`). | `state.SideToMove == actorSide`; `state.Result == null`; skill ∈ lineup; `CooldownTurns == 0`; `UsesRemaining != 0`. | — | IMPLEMENTED | `CommandSkillDispatcher.Dispatch` |
| **CMD-02** | Dùng skill **thay cho một nước đi**: `Version/TurnIndex/CountedActions` tăng 1, chuyển lượt, reset chuỗi timeout, tick vòng đời lượt mới. | Skill thành công. | — | IMPLEMENTED | `MatchTurnRules.ApplySkill` → `AdvanceSkill` → `AfterAction` |
| **CMD-03** | Skill không được để lại Tướng mình bị chiếu → lỗi `KING_IN_CHECK`, không tiêu lượt/CD. | `engine.IsInCheck` sau khi áp dụng skill. | Lỗi → không mutate state. | IMPLEMENTED | `MatchTurnRules.ApplySkill` |
| **CMD-04** | Command Skill không được để lại Tướng mình bị chiếu **bởi chính hiệu ứng mới tạo** (ưu tiên phòng thủ trước tấn công). | Kiểm tra chiếu với hiệu ứng vừa tạo. | — | IMPLEMENTED | `MatchTurnRules.ApplySkill` |
| **CMD-05** | Sau khi dùng skill thành công, CD được đặt bằng `frozenSkill.CooldownTurns` (giá trị đóng băng lúc tạo trận), `UsesRemaining` giảm 1 (nếu có). | Skill thành công. | — | IMPLEMENTED | `CommandSkillDispatcher.Dispatch` |
| **CMD-06** | CD và thời lượng được **đóng băng theo trận**; sửa catalog sau đó không đổi hành vi trận đang chơi/replay. | `FrozenSkill` tạo lúc `Freeze`. | — | IMPLEMENTED | `MatchModels.cs` (`FrozenSkill`); `MatchSelectionService.Freeze` |
| **CMD-07** | Skill lỗi **không** tiêu lượt, không tiêu CD, không mutate state (dispatcher trả về state gốc). | Nhánh `!result.Accepted`. | — | IMPLEMENTED | `CommandSkillDispatcher.Dispatch` (nhánh `!result.Accepted`) |
| **CMD-08** | Mỗi đội hình có **đúng 3** ô Command Skill, slot 1..3, không trùng skill, không được chọn 2 skill cùng phe, skill phải `is_enabled`. | `LineupValidator.ValidateAsync`. | — | IMPLEMENTED | `LineupValidator.ValidateAsync` |

| Rule ID | Skill | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|---|
| **CMD-09** | **Thành** (`thanh`) | Đặt một vật cản Thành ở **ô trống trong nửa sân mình** (Đỏ `y ≤ 4`, Đen `y ≥ 5`). | Ô trống, không có vật cản khác. | Tồn tại **6 lượt chung**, CD **8 lượt chung**. | IMPLEMENTED | `HeroChess.Rules/Skills/ThanhHandler.cs`; `08_step6_skills.sql` |
| **CMD-10** | **Thành** — tương tác | Thành chặn đường đi như vật cản. **Thành không được dùng làm ngòi Pháo.** Chỉ **Pháo** (hoặc Xe Lý Thường Kiệt) được phá Thành. | — | Khi Pháo phá Thành, **Pháo tiến vào ô Thành** và mất một lượt. | IMPLEMENTED | `XiangqiRulesEngine.RayMoves`, `GenerateLegalActions`, `MoveUnchecked` |
| **CMD-11** | **Rào** (`rao`) | Đặt một vật cản Rào ở **ô trống trong nửa sân mình**. | Ô trống, không có vật cản khác. | Tồn tại **4 lượt chung**, CD **6 lượt chung**. | IMPLEMENTED | `HeroChess.Rules/Skills/RaoHandler.cs`; `08_step6_skills.sql` |
| **CMD-12** | **Rào** — tương tác | Rào chặn đường như vật cản nhưng **được Pháo dùng làm ngòi**. Bất kỳ quân nào đi vào ô Rào đều phá được Rào. | — | — | IMPLEMENTED | `XiangqiRulesEngine.RayMoves`; `MoveUnchecked` |
| **CMD-13** | **Khiên** (`khien`) | Gắn Khiên cho **một quân đồng minh còn sống** (kể cả Tướng); Khiên đi theo quân. | Target phải là `pieceId` của quân đồng minh còn sống; quân đã có Khiên → `ALREADY_SHIELDED`. | Tồn tại **4 lượt chung**, CD **8 lượt chung**. | IMPLEMENTED | `HeroChess.Rules/Skills/KhienHandler.cs`; `08_step6_skills.sql` |
| **CMD-14** | **Khiên** — tác dụng | Quân có Khiên **không thể bị ăn**, kể cả bị Cọc loại. Nếu Tướng có Khiên thì **không tạo trạng thái chiếu** trong suốt thời gian hiệu lực. | `RemainingTurns > 0`. | Hết Khiên → tính lại chiếu theo vị trí lúc đó (chiếu xuất hiện ở đầu lượt kế tiếp của chủ quân). | IMPLEMENTED | `XiangqiRulesEngine.GenerateLegalActions`, `IsInCheck`, `FindPublicStake`; `TurnLifecycle.ApplyCore` |
| **CMD-15** | **Vạn Cọc Trấn Giang** (`van_coc_tran_giang`) | Tạo một Effect chặn cắt ngang sông trên **3 cột liên tiếp trong {4,5,6}**, kèm 30 vật cản vật lý (10 hàng × 3 cột) làm ngòi/chặn đường. | `target.paths` phải đúng 3 cột liên tiếp hợp lệ; **cả 3 cột** phải không xung đột với Effect cắt ngang sông đang có, nếu không lần kích hoạt hỏng hoàn toàn (`PATH_OCCUPIED`). | Effect tồn tại **2 lượt chung**; vật cản vật lý tồn tại **2 lượt chung**. **Chưa có dòng `team_skill` trong SQL seed** → không chọn được trong đội hình (xem CMD-23). | IMPLEMENTED (cơ chế) | `HeroChess.Rules/Skills/VanCocTranGiangHandler.cs` |
| **CMD-16** | **Vạn Cọc** — hiệu lực | Đối thủ **không được cắt ngang sông** qua các cột bị phủ. Chủ Effect được cắt sông tự do. Cùng phe vẫn đi bình thường trong vùng phủ. | Chỉ Effect `Active` mới chặn (`Disabled`/`Ended` không chặn). | Kiểm theo cột: nước thẳng dọc lấy cột nguồn, nước ngang lấy cột đích, nước nhảy lấy cột đích. | IMPLEMENTED | `XiangqiRulesEngine.IsRiverCrossingBlockedByEffect` |
| **CMD-17** | **Binh Lâm Thủy Hiểm** (`binh_lam_thuy_hien`) | Tạo Effect "terrain" chặn cắt ngang sông trên 3 cột liên tiếp. | Cùng quy tắc cột hợp lệ. | **Áp dụng một phần**: cột xung đột bị loại, cột còn lại vẫn áp dụng; nếu **cả 3** cột xung đột thì lần kích hoạt hỏng hoàn toàn (`ALL_PATHS_BLOCKED`). Tồn tại **1 lượt**. **Chưa có dòng `team_skill` trong SQL seed** (xem CMD-23). | IMPLEMENTED (cơ chế) | `HeroChess.Rules/Skills/BinhLamThuyHienHandler.cs` |
| **CMD-18** | **Phản Kỳ Đoạt Thế** (`phan_ky_doat_the`) | **Chiếm quyền kiểm soát** Effect "terrain" của đối thủ (hiện chỉ có Binh Lâm). | Target phải là `effectId`; Effect phải `Active`, do đối thủ tạo, `kind == "terrain"`; `position` (nếu có) phải thuộc Effect. | Không tạo Effect mới, không đổi Creator/CreationOrder/Duration. Việc chiếm là **pending**: `PositionController` chính thức **chưa** đổi ngay. **Chưa có dòng `team_skill` trong SQL seed** (xem CMD-23). | IMPLEMENTED (cơ chế) | `HeroChess.Rules/Skills/PhanKyDoatTheHandler.cs` |
| **CMD-19** | **Phản Kỳ** — thời điểm chốt | Pending steal được chốt ở **đầu lượt của chủ Effect** (`TurnLifecycle` bước 4): `PositionControllers` chuyển sang phe mới, `PendingController` xoá. | Effect còn `Active`. | Vì `resolveCreatorCancellation: true` được truyền ở **mọi** call site (`MatchCommandService` dòng 93 và 329, `MatchTurnRules` dòng 14 và 113), bước 4 (chốt) và bước 5 (huỷ của chủ) chạy **cùng một tick**: Effect vừa chuyển quyền sang tên trộm thì bị chủ Effect kết thúc ngay (`State = Ended`) → tên trộm **không bao giờ** nắm quyền. Xem `RULE_CONFLICTS_AND_OPEN_QUESTIONS.md` (C-02). | **CONFLICT** | `TurnLifecycle.ApplyCore` (bước 4); `TurnLifecycle.ApplyCreatorCancellation`; `MatchCommandService.cs`; `MatchTurnRules.cs` |
| **CMD-20** | **Phản Kỳ** — huỷ của chủ Effect | Ở lượt của chủ Effect, nếu Effect đã bị chiếm thì chủ Effect có thể **huỷ** nó (internal resolution, **không phải** Command Skill): `State = Ended`, không tạo Effect mới, không reset Duration/Creator/CreationOrder. | `resolveCreatorCancellation: true` khi tick đầu lượt. | Ghi nhận bằng event `phan_ky.cancelled`. **Không có lựa chọn cho người chơi**: huỷ là tự động, không phải hành động của chủ Effect → mâu thuẫn với thiết kế "chủ Effect **có thể** huỷ" (xem C-02). | **CONFLICT** | `TurnLifecycle.ApplyCreatorCancellation`; `HERO_CHESS_PHASE3_IMPLEMENTATION_PLAN.md` (§3.3) |
| **CMD-21** | **Phá Trận Đoạt Phong** (`pha_tran_doat_phong`) | Giảm **1** đơn vị thời lượng còn lại của Effect đối thủ; nếu còn 0 thì Effect kết thúc ngay. | Effect phải tồn tại, do đối thủ tạo, chưa `Ended`, `RemainingDuration > 0`, và **phải được gán cho một quân của người dùng** qua `payload["assignedPieceId"]`. | Có thể tác động Effect `Disabled` (timer vẫn chạy). **Chưa có dòng `team_skill` trong SQL seed** (xem CMD-23). | IMPLEMENTED (cơ chế) | `HeroChess.Rules/Skills/PhaTranDoatPhongHandler.cs` |
| **CMD-22** | **Phá Trận** — gán quân | Hiện **không handler nào** ghi `assignedPieceId` vào payload Effect. Vạn Cọc và Binh Lâm chỉ ghi `kind`/`paths`. | — | Vì vậy `Phá Trận Đoạt Phong` luôn trả `EFFECT_NOT_ASSIGNED` với Effect do Vạn Cọc/Binh Lâm tạo. | **CONFLICT** | `PhaTranDoatPhongHandler.cs` vs `VanCocTranGiangHandler.cs` / `BinhLamThuyHienHandler.cs` |
| **CMD-23** | **Catalog Command Skill** — handler không đồng nghĩa với skill chọn được. Trong `HeroChess.Api/Data/Bootstrap/*.sql` và `Hero_Chess_DB_Prototype_v0_1/*.sql` **chỉ có** các dòng `team_skill` cho `thanh`, `rao`, `khien`, `tran-hung-dao-tuong-coc` (tắt) và 3 Faction Skill. | — | `van_coc_tran_giang`, `binh_lam_thuy_hien`, `phan_ky_doat_the`, `pha_tran_doat_phong` **không có dòng catalog** → không thể đưa vào 3 ô skill của đội hình, dù handler đã được đăng ký trong `Program.cs`. `AdminCatalogService` đánh dấu trạng thái `LIVE` cho các key này. | **CONFLICT** (thiếu dữ liệu seed) | `HeroChess.Api/Data/Bootstrap/08_step6_skills.sql`; `16_faction_skills.sql`; `HeroChess.Api/Program.cs`; `HeroChess.Api/Services/AdminCatalogService.cs` (`live` set) |
| **CMD-24** | **Ai được phá Rào**: runtime cho phép **bất kỳ quân nào** đi vào ô Rào đều phá được Rào, kể cả Rào **của chính mình** và kể cả quân địch. `docs/PLAN_Hero_And_Command_Skills.md` §6.1 nói đúng hành vi này nhưng ghi rõ "**luật này cần xác nhận trước bản phát hành**". | `XiangqiRulesEngine.MoveUnchecked` xoá vật cản `thd_tuong_rao` bất kể phe. | Chưa được người dùng chốt. | UNCONFIRMED | `XiangqiRulesEngine.MoveUnchecked`; `docs/PLAN_Hero_And_Command_Skills.md` §6.1 |
| **CMD-25** | **Cách chủ Effect huỷ khi bị chiếm**: `HERO_CHESS_PHASE3_IMPLEMENTATION_PLAN.md` §3.3 liệt kê 3 phương án (A: nút hành động riêng; B: tự động; C: gợi ý khi di chuyển vào vùng bị chiếm) và **khuyến nghị A**, nhưng runtime chạy **B** (tự động huỷ trong cùng tick). Xem `RULE_CONFLICTS_AND_OPEN_QUESTIONS.md` (C-02). | `TurnLifecycle.ApplyCreatorCancellation`; `MatchCommandService` (dòng 93 và 329 truyền `resolveCreatorCancellation: true`). | Không có lựa chọn cho người chơi trong UI hiện tại. | UNCONFIRMED | `HERO_CHESS_PHASE3_IMPLEMENTATION_PLAN.md` §3.3; `TurnLifecycle.ApplyCreatorCancellation`; `HeroChess.Api/Program.cs` (dòng 92–94) |

---

## 7. Faction Skill (skill theo phe)

Điều kiện chung:

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **FAC-01** | Chỉ được chọn **tối đa 1** Faction Skill trong một đội hình. | `skills.Count(x => x.FactionId is not null) > 1` → `MULTIPLE_FACTION_SKILLS`. | — | IMPLEMENTED | `LineupValidator.ValidateAsync` (`MULTIPLE_FACTION_SKILLS`) |
| **FAC-02** | Đội hình phải có **≥ 5 hero thuộc phe, không tính Tốt** mới được dùng Faction Skill (`FACTION_REQUIREMENT_NOT_MET`). | `FactionSkillEligibility.CanUse` với `TranRequiredHeroes = HauLeRequiredHeroes = TaySonRequiredHeroes = 5`; đếm `hero_faction` của các hero **không phải SOLDIER** trong đội hình. | Ngưỡng 5 được ghi cứng trong code **và** trong `eligibility.minNonSoldierFactionHeroes` của catalog. `docs/Decisions.md` (23/09) nói "2–3 tướng" → xem CONFLICT. **Thêm nữa**: sau bootstrap tự động, bảng `hero_faction` còn **rỗng** (script `13_hero_factions.sql` không được chạy — xem SRV-17) nên **không hero nào** được tính là thuộc phe → Faction Skill không thể dùng với bất kỳ đội hình nào. | IMPLEMENTED (code + catalog) / CONFLICT (với `docs/Decisions.md`) | `HeroChess.Api/Services/FactionSkillEligibility.cs`; `HeroChess.Api/Services/LineupValidator.cs`; `18_faction_skill_requirements.sql`; `13_hero_factions.sql` |
| **FAC-03** | Faction Skill mới chỉ được dùng được khi server bật `MatchRuntime:NewFactionSkillsEnabled`; nếu tắt, catalog và lineup đều không cho chọn. | `runtime.Value.NewFactionSkillsEnabled`; mặc định `false` (không có trong `appsettings.json`). | — | IMPLEMENTED | `LineupValidator.ValidateAsync`; `CatalogController` |
| **FAC-04** | Quyền Faction Skill được xét **ở bước chọn/confirm đội hình** (trước khi trận bắt đầu) và **không bao giờ bị thu hồi** trong trận, kể cả khi các hero của phe đó đã bị ăn. | Không có đoạn code nào kiểm tra lại ngưỡng trong `GameState` sau khi trận bắt đầu. | `docs/Decisions.md` (23/09) xác nhận "xét/mở lúc bắt đầu trận và giữ nguyên dù tướng sau đó bị ăn". | IMPLEMENTED (hành vi không thu hồi) — điểm "lúc bắt đầu trận" trong tài liệu quyết định được code hiện thực ở bước chọn/confirm đội hình | `HeroChess.Api/Services/LineupValidator.cs`; `HeroChess.Api/Services/MatchSelectionService.cs` (`Freeze`, `Start`); `docs/Decisions.md` |
| **FAC-05** | Faction Skill cũng dùng chung quy tắc Command Skill: đúng lượt, CD, tiêu một lượt, không để lại Tướng bị chiếu. | `CommandSkillDispatcher.Dispatch`; `MatchTurnRules.ApplySkill`. | — | IMPLEMENTED | `CommandSkillDispatcher`; `MatchTurnRules.ApplySkill` |

| Rule ID | Skill | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|---|
| **FAC-06** | **Bạch Đằng Trận Pháp** (`bach_dang_tran_phap`) | Đặt **3 bẫy ẩn** trên sông. Quân địch đi qua (kể cả đi xuyên qua ô bẫy) bị **khóa chân**; bẫy bị tiêu sau khi kích hoạt. | `target.positions` đúng 3 ô `y ∈ {4,5}`, trống, khác nhau. | Bẫy tồn tại **6 lượt chung** (`ExpiresAt = TurnIndex + 6`); hiệu ứng khóa chân `bach_dang_rooted` được gắn với `RemainingTurns = 5` ngay khi kích hoạt, rồi giảm 1 mỗi lượt chung → quân bị khóa còn **4 lượt chung** kể từ lượt kế tiếp (test `PublicRiverStakeTests.Hidden_snare_at_horse_leg_consumes_one_trap_and_locks_for_four_turns`); mỗi bẫy dùng một lần; bẫy là **bẫy theo đường đi**, không chặn tia và không làm ngòi Pháo. | IMPLEMENTED | `HeroChess.Rules/Skills/FactionSkillHandler.cs`; `XiangqiRulesEngine.MoveUnchecked`; `tests/HeroChess.Rules.Tests/PublicRiverStakeTests.cs` |
| **FAC-07** | **Lam Sơn Quân Lược** (`lam_son_quan_luoc`) | "Xem" tối đa 3 hành động dự đoán và 2 phương án nguy hiểm của địch ở lượt kế tiếp. | Skill không có tác dụng trực tiếp lên state; dữ liệu lấy qua `GET /matches/{id}/forecast?version=`. | Chỉ dự đoán được hành động của lượt kế tiếp (bot dự đoán 3 ply, 50 ms). | IMPLEMENTED | `FactionSkillHandler.cs`; `MatchReadService.ForecastAsync`; `BotEngine.Forecast` |
| **FAC-08** | **Hịch Xuất Quân** (`hich_xuat_quan`) | Trong **6 lượt chung**, Tốt của phe được nhảy **2 ô** theo hướng thẳng về phía địch nếu ô trung gian trống và không có vật cản. | Tốt có hiệu ứng `hich_xuat_quan` còn lượt (gắn lúc kích hoạt skill cho mọi Tốt sống của phe). | Sau khi đã qua sông, Tốt được nhảy **2 ô ngang** (trái/phải) theo cùng điều kiện ô trung gian trống. Mô tả catalog (`16_faction_skills.sql`) chỉ nói "đi thêm một ô theo hướng hợp lệ nếu ô trung gian trống" → **không nêu** nhảy 2 ô, **không nêu** nhảy ngang sau khi qua sông → xem CONFLICT (mô tả catalog thiếu/không khớp). | IMPLEMENTED (cơ chế) / CONFLICT (mô tả catalog) | `HeroChess.Rules/Skills/FactionSkillHandler.cs`; `XiangqiRulesEngine.GeneratePseudoDestinations` (case `Soldier`); `16_faction_skills.sql` |
| **FAC-09** | **Bật Faction Skill mới**: `MatchRuntime:NewFactionSkillsEnabled` mặc định `false` và **không có** trong `appsettings.json` → cả 3 Faction Skill đều bị chặn ở bước chọn đội hình (`SKILL_UNAVAILABLE`) và bị lọc khỏi `GET /catalog`. | `LineupValidator.ValidateAsync`; `CatalogController` (`runtime.Value.NewFactionSkillsEnabled \|\| !IsNewFactionSkill(...)`). | Cần bật bằng biến môi trường `MatchRuntime__NewFactionSkillsEnabled=true`; khi bật, ngưỡng `minNonSoldierFactionHeroes = 5` mới được áp dụng (xem FAC-02). | IMPLEMENTED (cơ chế) / PLANNED (chưa bật) | `HeroChess.Api/appsettings.json`; `HeroChess.Api/Services/MatchModels.cs` (`MatchRuntimeOptions`); `LineupValidator.cs`; `CatalogController.cs` |

---

## 8. Cọc / River Stakes

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **STK-01** | Cọc là **bẫy theo đường đi**, không phải vật cản chặn đường, không phải ngòi Pháo. Runtime hiện kích hoạt khi quân địch **xuyên/nhảy qua** ô Cọc trên đường đi (kể cả ô chân Mã, ô đáp) — **không chỉ** ở ô dừng. | `RulesGeometry.IsBlockingObstacle` trả `false` cho `thd_tuong_coc`; `XiangqiRulesEngine.CrossedCells` + `FindPublicStake`. | Hai tài liệu thiết kế **mâu thuẫn nhau**: `docs/PLAN_Hero_And_Command_Skills.md` §1 (và §6 khẳng định điểm này "đã được người dùng chốt") quy định Cọc **chỉ** kích hoạt ở **ô dừng**, "đi xuyên/nhảy qua không kích hoạt"; `docs/PLAN_Khoi_Phuc_Hero_Trait.md` (08/11/2026, mới hơn) lại mô tả "bị loại tại Cọc đầu tiên **trên đường** hoặc tại ô đích". Runtime theo hướng thứ hai → xem `RULE_CONFLICTS_AND_OPEN_QUESTIONS.md` (C-01). | **CONFLICT** | `MovementHandlers.cs` (`RulesGeometry.IsBlockingObstacle`); `XiangqiRulesEngine.CrossedCells`; `XiangqiRulesEngine.FindPublicStake`; `docs/PLAN_Hero_And_Command_Skills.md` §1, §6; `docs/PLAN_Khoi_Phuc_Hero_Trait.md` |
| **STK-02** | Quân **đối thủ** đi qua ô Cọc (ở bất kỳ giao điểm nào trên đường, kể cả ô đích) bị loại (bị ăn). Quân **ta** đi qua Cọc của mình không sao. | `FindPublicStake` chỉ khớp Cọc có `Placer != piece.Side`. | Quân có Khiên không bị loại (`FindPublicStake` bỏ qua quân đang có `shield`). | IMPLEMENTED | `XiangqiRulesEngine.FindPublicStake`; `XiangqiRulesEngine.ResolveMove` |
| **STK-03** | Thứ tự xử lý: xác định nước hợp lệ → hiệu ứng phòng thủ (Khiên) → **kiểm tra Cọc trên toàn bộ đường đi** → ăn quân / phá vật cản → xét chiếu / kết thúc trận. | `ResolveMove` gọi `FindPublicStake` **trước** khi `MoveUnchecked`. | Khi Cọc nằm trên đường (không phải ở đích): quân bị loại **tại ô xuất phát**, **không** ăn quân và **không** phá vật cản ở ô đích. Khi Cọc ở đúng ô đích: **Cọc được giải trước**, quân bị loại, quân/vật cản ở ô đích giữ nguyên. | IMPLEMENTED | `XiangqiRulesEngine.ResolveMove`; `XiangqiRulesEngine.GenerateLegalActions` |
| **STK-04** | Cọc **vẫn còn** sau khi bắt quân (không bị tiêu thụ). | — | Không có cơ chế thu hồi Cọc. | IMPLEMENTED | `XiangqiRulesEngine.ResolveMove`; test `PublicRiverStakeTests.Stake_prevents_capture_and_leaves_the_victim_in_place` |
| **STK-05** | Tướng bị Cọc loại trực tiếp → bên đó **thua ngay**, `endReason = "river_stake"`. | `CompleteMove` kiểm Tướng của phe vừa đi còn sống hay không. | Không trả lỗi `ILLEGAL_MOVE`. | IMPLEMENTED | `XiangqiRulesEngine.CompleteMove` |
| **STK-06** | Quân (không phải Tướng) bị Cọc loại mà làm lộ Tướng của phe vừa đi (kể cả lộ mặt hai Tướng) → bên vừa đi **thua ngay**. | `CompleteMove`: `stakeHit is not null && IsInCheck(next, mover)`. | Kết quả này được ưu tiên trước giới hạn SP. | IMPLEMENTED | `XiangqiRulesEngine.CompleteMove` |
| **STK-07** | Đường đi kiểm tra Cọc: Xe/Pháo/trait đi thẳng hoặc chéo xét lần lượt các giao điểm trên đường; Mã xét **ô chân trước, ô đáp sau**; điểm xuất phát không tính. | `CrossedCells(class, from, to)`. | Đường tránh được giao điểm có Cọc thì đi bình thường. | IMPLEMENTED | `XiangqiRulesEngine.CrossedCells` |
| **STK-08** | Một quân bị Cọc chặn **không tạo chiếu** cho Tướng địch. | `IsInCheck` loại quân có Cọc trên đường tới ô Tướng. | — | IMPLEMENTED | `XiangqiRulesEngine.IsInCheck` |
| **STK-09** | Thời lượng Cọc giảm theo **lượt chung**; hết hạn thì vật cản bị gỡ, metadata và `ThdTuongCocStakes` được dọn. | `TurnLifecycle.ApplyCore` bước 3. | — | IMPLEMENTED | `TurnLifecycle.ApplyCore` |
| **STK-10** | **Bí mật Cọc**: khi `PublicRiverStakes == false`, vật cản Cọc, `ObstacleMetadata`, `StakeMetadata`, `ThdTuongCocStakes` và event liên quan đều **không** được gửi cho người xem; đồng thời trait/movement metadata của quân cũng bị gỡ khỏi state view. | `MatchStateProjection.State` / `.Events`. | Chỉ khi `PublicRiverStakes == true` mới công khai. | IMPLEMENTED | `HeroChess.Api/Services/MatchStateProjection.cs` |
| **STK-11** | `PublicRiverStakes` được set từ `ruleset.HeroTraitsEnabled` khi tạo trận — **cùng một cờ** cho cả "bật hero trait" và "Cọc công khai". | `MatchSelectionService.Start`. | Vì vậy khi `HeroTraitsEnabled = false`, Hero Skill cũng bị chặn (HES-01). | IMPLEMENTED | `MatchSelectionService.Start`; `MatchmakingService.NewMatchAsync` |
| **STK-12** | Vạn Cọc (Command Skill) tạo **vật cản vật lý** trên cả 3 cột × 10 hàng, và các vật cản này **chặn đường** (khác Cọc Bạch Đằng Giang). | `RulesGeometry.IsBlockingObstacle` trả `true` cho kind `"stake"`. | — | IMPLEMENTED | `VanCocTranGiangHandler.cs`; `MovementHandlers.cs` (`RulesGeometry`) |
| **STK-13** | **Chế độ "Cọc ẩn" theo thiết kế chưa tách được khỏi "bật Hero Trait"**: `docs/PLAN_Hero_And_Command_Skills.md` §2 yêu cầu vị trí Cọc **chỉ hiện cho chủ sở hữu**, đối thủ không đọc được tọa độ. Runtime có đủ logic lọc bí mật (`MatchStateProjection`), nhưng `PublicRiverStakes` được set từ `ruleset.HeroTraitsEnabled` (mặc định `true`) nên trong gameplay mặc định Cọc là **công khai** và nhánh lọc bí mật không chạy. | `MatchSelectionService.Start` (`PublicRiverStakes = rules.HeroTraitsEnabled`); `MatchStateProjection.State` / `.Events`. | Cần một cờ ruleset riêng (ví dụ `PublicRiverStakes`) mới bật được chế độ ẩn mà vẫn bật Hero Skill. | PLANNED | `MatchSelectionService.cs`; `MatchStateProjection.cs`; `docs/PLAN_Hero_And_Command_Skills.md` §2 |

---

## 9. Hệ thống lượt, đồng hồ và giới hạn hành động

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **TRN-01** | Mỗi lượt có **90 giây** (mặc định `ruleset.turn_seconds = 90`). Deadline được set lại sau mỗi action. | `row.TurnDeadlineAt = now.AddSeconds(rules.TurnSeconds)`. | — | IMPLEMENTED | `01_schema.sql`; `02_seed_catalog.sql`; `MatchCommandService` |
| **TRN-02** | Vòng đời lượt (cooldown, thời lượng hiệu ứng, tuổi vật cản, chốt steal) chạy **đúng một lần** cho mỗi lượt của mỗi phe, được bảo vệ bằng `ProcessedTurns[side][turnIndex]`. | `TurnLifecycle.IsTurnProcessed` / `MarkTurnProcessed`. | — | IMPLEMENTED | `TurnLifecycle.cs`; `MatchCommandService` |
| **TRN-03** | CD và thời lượng được đo bằng **lượt chung của bàn cờ**, không phải lượt của chủ skill. | `TurnLifecycle.ApplyCore` giảm CD của **cả hai phe**. | — | IMPLEMENTED | `TurnLifecycle.ApplyCore` |
| **TRN-04** | Lượt dùng skill **được tính vào** thời lượng hiệu ứng: skill dùng ở lượt 1 thì hiệu lực còn tới hết lượt 4 của địch (Khiên 4 lượt). | `TurnLifecycle` giảm tuổi ở đầu lượt kế tiếp. | — | IMPLEMENTED | `TurnLifecycle.ApplyCore`; test `NewSkillGameplayTests.Shield_prevents_capture_and_check_until_four_shared_turns_pass` |
| **TRN-05** | Hết giờ: tăng `TurnIndex`/`CountedActions`, chuyển lượt, tick vòng đời lượt mới. | — | — | IMPLEMENTED | `MatchCommandService.TimeoutAsync` |
| **TRN-06** | Hết giờ khi **đang bị chiếu** → thua ngay, `endReason = "timeout_in_check"`. | `engine.IsInCheck(state, timedOut)`. | Không hủy trận. | IMPLEMENTED | `MatchCommandService.TimeoutAsync` |
| **TRN-07** | **Hai lần hết giờ liên tiếp của cùng một người** (kể cả có lượt đối thủ xen giữa) → thua AFK, `endReason = "afk"`. | `ConsecutiveTimeouts[side] >= 2`. | Hành động hợp lệ của chính người đó xoá chuỗi; lệnh bị từ chối không xoá. | IMPLEMENTED | `MatchCommandService.TimeoutAsync`; `XiangqiRulesEngine.CompleteMove`; `MatchTurnRules.AdvanceSkill` |
| **TRN-08** | Giới hạn **150 hành động chung** của cả hai phe. `move`, `team_skill`, `hero_active` và `timeout` đều tiêu 1 lượt và tăng counter 1 lần. | `CountedActions`. | `resign`, `undo`, lỗi, dedup `commandId`, reload, replay **không** tick thêm. | IMPLEMENTED | `XiangqiRulesEngine.CompleteMove`; `MatchTurnRules.AdvanceSkill`; `MatchCommandService.TimeoutAsync` |
| **TRN-09** | Khi đạt giới hạn 150 mà chưa có kết quả trực tiếp: so tổng **SP đã đóng băng** của quân còn sống; bên nào nhiều hơn thắng, **bằng nhau thì hoà**. | `MatchTurnRules.ApplyActionLimit`. | Kết quả trực tiếp (chiếu bí, AFK, timeout trong chiếu) **được ưu tiên trước** so SP. | IMPLEMENTED | `MatchTurnRules.ApplyActionLimit`; `MatchCommandService.TimeoutAsync` |
| **TRN-10** | `resign` kết thúc trận ngay: đối thủ thắng, `endReason = "resign"`. | Chỉ người đang trong trận mới gọi được. | Không tiêu lượt. | IMPLEMENTED | `MatchCommandService` (case `resign") |
| **TRN-11** | `undo` chỉ dành cho người chơi trong trận **chế độ bot**; phục hồi snapshot trước đó và xoá `Result`/`EndReason`. | `match.Mode == "bot"`, không phải bot. | Không tick lại vòng đời lượt. | IMPLEMENTED | `MatchCommandService` (case `undo") |
| **TRN-12** | Turn đầu tiên của phe Đỏ **không** chạy vòng đời lượt (đã đánh dấu processed lúc tạo trận); turn đầu của phe Đen có chạy. | `TurnLifecycle.MarkTurnProcessed(state, Side.Red, state.TurnIndex)` lúc `Start`. | Lệch nhẹ 1 tick giữa hai phe ở đầu trận. | IMPLEMENTED | `MatchSelectionService.Start` |

---

## 10. Chế độ trận

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **MOD-01** | Ba chế độ: `ranked` (ghép Elo), `bot` (đấu bot), `custom` (phòng riêng, 2 người). | `game_match.mode` có CHECK constraint. | — | IMPLEMENTED | `12_custom_rooms.sql`; `MatchmakingService`; `MatchmakingService.Rooms.cs` |
| **MOD-02** | Chế độ `ranked`: ghép với người chơi có Elo trong cửa sổ ±200; màu phe được phân bự theo điểm màu gần đây (8 ván), người chờ lâu hơn giữ màu đỏ khi hòa. | `MatchmakingOptions.EloWindow = 200`. | — | IMPLEMENTED | `MatchmakingService.cs`; `MatchModels.cs` |
| **MOD-03** | Chế độ `bot`: người chơi luôn là **Đỏ**, bot là **Đen**; bot dùng **bản sao đội hình** của người chơi. | `MatchSelectionService.SelectAsync`; `MatchmakingService.CreateMatchAsync`. | — | IMPLEMENTED | `MatchSelectionService.cs`; `MatchmakingService.cs` |
| **MOD-04** | Độ khó bot: `easy` / `medium` / `hard` (beam bounded, không phải minimax đầy đủ). Profile bất biến; đổi cấu hình cần version mới. | `BotConfiguration.ForRequest`. | `difficulty` chỉ hợp lệ với mode `bot`. | IMPLEMENTED | `HeroChess.Api/Services/BotConfiguration.cs`; `BotEngine.cs` |
| **MOD-05** | Bot "legacy" (`capture_sp_v1`, `BotConfiguration.Legacy`) **không** bị hủy với `bot_configuration_error`: điều kiện hủy là `config != BotConfiguration.Legacy && !BotEngine.Supports(state)`. Khi trận **có** trait (`state.PublicRiverStakes == true` và ít nhất một quân có `TraitImplementationKey`), bot legacy được **chuyển sang `BotEngine` với profile `medium`** để tìm nước. Chỉ khi trận **không** có trait mà bot legacy chạy đường cũ (`GenerateLegalActions` + ưu tiên ăn quân). | `BotTurnScheduler`: `legacyNeedsTraitSearch`; `MatchCommandService.CancelBotFailureAsync`. | Bot legacy vẫn **không** dùng được Command/Hero Skill qua đường cũ. | IMPLEMENTED | `HeroChess.Api/Services/BotTurnScheduler.cs`; `MatchCommandService.cs` (`CancelBotFailureAsync`); `BotConfiguration.cs` |
| **MOD-06** | Bot tìm nước bằng **cùng Rules** với người chơi (sinh legal actions từ engine chung), và có thể dùng Hero Skill/Command Skill khi không còn nước đi thường. | `BotEngine.Simulate` dùng `MatchTurnRules.ApplyHeroSkill` / `ApplySkill`. | — | IMPLEMENTED | `BotEngine.cs` |
| **MOD-07** | **Undo** chỉ có ở chế độ bot. | `match.Mode == "bot"`. | — | IMPLEMENTED | `MatchCommandService` (case `undo") |
| **MOD-08** | Thưởng coin và Elo chỉ áp dụng cho trận `ranked` kết thúc `completed`; bot/custom/cancel không thưởng, không đổi Elo. | `RewardPolicy.Reward`. | Thắng 10 coin, hòa 5 coin, Elo K = 32 (mặc định dev). | IMPLEMENTED | `SettlementPolicies.cs`; `MatchModels.cs` (`MatchRuntimeOptions`) |
| **MOD-09** | Giai đoạn chọn đội hình có **120 giây**; hết hạn → trận `cancelled` với `selection_timeout`. | `MatchRuntime.SelectionSeconds = 120`. | — | IMPLEMENTED | `MatchModels.cs`; `MatchSelectionLifetime.cs` |
| **MOD-10** | `custom` bắt buộc đúng **2 người thật** (constraint trigger DB). | `12_custom_rooms.sql`. | — | IMPLEMENTED | `12_custom_rooms.sql` |

---

## 11. Thắng / thua / hòa

| Rule ID | Điều kiện kết thúc | `result` | `endReason` | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **END-01** | Bên đang đi **không còn nước đi hợp lệ nào** (move) **và không dùng được skill nào cứu chiếu** (Hero Skill hoặc Command Skill đúng CD/mục tiêu). | Phe đối thủ thắng | `checkmate` nếu đang bị chiếu, ngược lại `no_legal_actions` | IMPLEMENTED | `MatchTurnRules.Finish`; `MatchCommandService.FindLegalSkillAction` |
| **END-02** | Hết giờ khi đang bị chiếu. | Phe đối thủ thắng | `timeout_in_check` | IMPLEMENTED | `MatchCommandService.TimeoutAsync` |
| **END-03** | Hai lần hết giờ liên tiếp của cùng một người. | Phe đối thủ thắng | `afk` | IMPLEMENTED | `MatchCommandService.TimeoutAsync` |
| **END-04** | Tướng bị Cọc loại, hoặc quân bị Cọc loại làm lộ Tướng. | Phe đối thủ thắng | `river_stake` | IMPLEMENTED | `XiangqiRulesEngine.CompleteMove` |
| **END-05** | Đầu hàng (`resign`). | Phe đối thủ thắng | `resign` | IMPLEMENTED | `MatchCommandService` (case `resign") |
| **END-06** | Đạt 150 hành động, so SP quân còn sống. | Theo SP | `action_limit_sp` | IMPLEMENTED | `MatchTurnRules.ApplyActionLimit` |
| **END-07** | Đạt 150 hành động, SP bằng nhau. | `draw` | `action_limit_sp` | IMPLEMENTED | `MatchTurnRules.ApplyActionLimit` |
| **END-08** | Trận bị hủy do timeout chọn đội hình. | Không có kết quả | `selection_timeout` | IMPLEMENTED | `MatchSelectionLifetime.cs` |
| **END-09** | Trận bị hủy do người chơi hủy khi đang chọn đội hình. | Không có kết quả | `selection_cancelled` | IMPLEMENTED | `MatchSelectionLifetime.cs` |
| **END-10** | Server khởi động lại giữa trận → trận đang chơi bị hủy (không resume). | Không có kết quả | `server_restart` | IMPLEMENTED | `MatchRecoveryHostedService.cs` |
| **END-11** | Cấu hình bot hỏng/không hỗ trợ → hủy trận, **không** tính là thua của người chơi. | Không có kết quả | `bot_configuration_error` | IMPLEMENTED | `MatchCommandService.CancelBotFailureAsync` |
| **END-12** | Kết quả trực tiếp (END-01..END-05) **luôn ưu tiên trước** so SP ở mốc 150. | — | — | IMPLEMENTED | `MatchCommandService.TimeoutAsync` (`if (state.Result is null) FinishTurn(...)`); `MatchTurnRules.ApplyActionLimit` (thoát sớm nếu đã có Result) |
| **END-13** | `endReason` được ánh xạ sang vực client: `no_legal_actions → stalemate`, `afk`/`timeout_in_check`/`selection_timeout → timeout`, `action_limit_sp`/`selection_cancelled`/`server_restart → draw`. | — | — | IMPLEMENTED | `MatchHistoryService.EndReason` |

---

## 12. Kiểm tra phía server

| Rule ID | Mô tả | Điều kiện | Giới hạn | Trạng thái | Nguồn |
|---|---|---|---|---|---|
| **SRV-01** | Mọi lệnh gameplay đi qua **một pipeline duy nhất** (`MatchCommandService.ExecuteActorAsync`) cho cả REST, WebSocket, bot và timeout. | — | — | IMPLEMENTED | `MatchCommandService.cs` |
| **SRV-02** | **Khóa trận** (FIFO gate) + `SELECT … FOR UPDATE` trên `game_match`/`match_state` để serialize mutation. | Mọi lệnh trận. | Trận đang chạy. | IMPLEMENTED | `MatchLockRegistry`; `MatchCommandService` |
| **SRV-03** | **Idempotency**: `commandId` đã dùng với payload khác → `409 COMMAND_ID_CONFLICT`; dùng lại với payload giống → trả lại kết quả cũ, **không** mutate state. | So sánh hash payload của `commandId`. | — | IMPLEMENTED | `MatchCommandService` |
| **SRV-04** | **Version check**: `expectedVersion` phải khớp `row.Version`, nếu không → `409 STALE_STATE`. | `expectedVersion != current`. | — | IMPLEMENTED | `MatchCommandService` |
| **SRV-05** | **Deadline check**: lệnh đến sau `TurnDeadlineAt` → `409 TURN_DEADLINE_PASSED`. | `now > TurnDeadlineAt`. | — | IMPLEMENTED | `MatchCommandService` |
| **SRV-06** | **Validate shape**: sai kiểu/range của `action` → `400 INVALID_ACTION`, không mutate state. | Deserialize `action`. | — | IMPLEMENTED | `MatchCommandService.ValidateAction` |
| **SRV-07** | **Quyền truy cập**: chỉ participant của trận mới đọc/ghi; `viewerSide` lấy từ participant đã xác thực, không tin `side` client gửi. | Token hợp lệ. | — | IMPLEMENTED | `MatchReadService.Authorize`; `MatchCommandService` |
| **SRV-08** | **Nước đi hợp lệ do server sinh**: `ApplyMove` chỉ chấp nhận nước nằm trong `GenerateLegalActions`; nếu không → `422 ILLEGAL_MOVE`. | Tọa độ `from`/`to` phải trong danh sách hợp lệ. | — | IMPLEMENTED | `XiangqiRulesEngine.ApplyMove` |
| **SRV-09** | **Trạng thái trận là server-authoritative**: client không gửi state; `GET /state` trả snapshot đã lọc theo người xem. | — | — | IMPLEMENTED | `MatchReadService.StateAsync`; `MatchStateProjection` |
| **SRV-10** | **Lọc dữ liệu theo người xem**: Cọc ẩn, bẫy của địch và trait metadata bị gỡ khỏi state/event trả về. | Áp dụng theo `viewerSide`. | Nhánh này **không chạy** khi `PublicRiverStakes = true` (xem `STK-10` / C-11). | IMPLEMENTED | `MatchStateProjection.State` / `.Events` |
| **SRV-11** | **Đóng băng trận**: `FrozenLineup` chứa hero/class/SP/trait/parameters/skill CD; sửa lineup nguồn không ảnh hưởng trận đã chạy. | Lúc `Freeze`. | — | IMPLEMENTED | `MatchModels.cs`; `MatchSelectionService.Freeze` |
| **SRV-12** | **Fingerprint catalog**: `ContentVersion` (SHA-256 trên hero/skill) được kiểm lại lúc confirm; đổi catalog giữa lúc chờ → `409 CONTENT_VERSION_CHANGED`. | So sánh hash catalog hiện tại với lúc chọn. | — | IMPLEMENTED | `CatalogVersionService.cs`; `MatchSelectionService.ConfirmAsync` |
| **SRV-13** | **Giao dịch nguyên tử**: state, action log và match row được ghi trong **cùng một transaction**; commit trước khi broadcast. | Một transaction cho cả 3 bảng. | — | IMPLEMENTED | `MatchCommandService.ExecuteActorAsync` |
| **SRV-14** | **Replay** đọc `stateAfter` đã lưu, không mô phỏng lại bằng catalog/luật hiện tại. | — | — | IMPLEMENTED | `MatchHistoryService.ReplayAsync` |
| **SRV-15** | **Nâng cấp schema state** v3 → v4 chạy idempotent trước mọi lần gọi Rules; snapshot cũ vẫn đọc được. **Nhưng** `UpgradeFromV3` **không** copy `PublicRiverStakes`, `ObstacleMetadata`, `ThdTuongCocStakes`, `RiverSnares` → state v3 được nâng cấp sẽ mất Cọc công khai và toàn bộ bẫy sông. | `state.Version < 4` → chạy upgrade trước khi gọi Rules. | Snapshot đã lưu trong DB từ v3 trở xuống. | IMPLEMENTED (idempotent) / **CONFLICT** (mất dữ liệu) | `HeroChess.Rules/StateSchemaUpgrade.cs` (`UpgradeFromV3`); xem `RULE_CONFLICTS_AND_OPEN_QUESTIONS.md` (C-05) |
| **SRV-16** | Server restart **không resume** trận đang chơi — hủy với `server_restart`. | Phát hiện trận `InProgress` lúc khởi động. | — | IMPLEMENTED | `MatchRecoveryHostedService.cs` |
| **SRV-17** | **Bootstrap tự động không chạy hết script migration**: `DatabaseBootstrapHostedService` chỉ chạy 01, 04, 05, 06, 07, 02, 08, 09, 10, 11, 12, `13_admin_module`, 17, 18 (và 03 khi Development). **Bỏ qua** `13_hero_factions.sql`, `14_hero_names_and_setup_points.sql`, `15_remove_duplicate_ly_van_buu.sql`, `16_faction_skills.sql` → sau khi bootstrap, bảng `hero_faction` **rỗng** (không ai thuộc phe), tên/SP hero chưa cập nhật, và **Faction Skill chưa tồn tại trong catalog**. | Danh sách `ExecuteFile` trong `DatabaseBootstrapHostedService.ExecuteAsync`. | 4 script này được chạy **tay** theo hướng dẫn trong file (`Apply manually in Supabase SQL Editor`). | **CONFLICT** (thiếu dữ liệu seed) | `HeroChess.Api/Data/Bootstrap/DatabaseBootstrapHostedService.cs`; `13_hero_factions.sql`; `14_hero_names_and_setup_points.sql`; `15_remove_duplicate_ly_van_buu.sql`; `16_faction_skills.sql` |

---

## 13. Bảng tra nhanh: điều kiện thắng

| Điều kiện | `result` | `endReason` |
|---|---|---|
| Không còn nước đi hợp lệ **và** không dùng được skill cứu chiếu | phe đối thủ thắng | `checkmate` / `no_legal_actions` |
| Hết giờ khi đang bị chiếu | phe đối thủ thắng | `timeout_in_check` |
| 2 lần hết giờ liên tiếp của cùng một người | phe đối thủ thắng | `afk` |
| Tướng bị Cọc loại / lộ Tướng sau khi quân bị Cọc loại | phe đối thủ thắng | `river_stake` |
| Đầu hàng | phe đối thủ thắng | `resign` |
| 150 hành động, so SP quân còn sống | theo SP hoặc `draw` | `action_limit_sp` |
| Trận bị hủy (selection timeout / hủy tay / restart / bot lỗi) | không có kết quả | `selection_timeout` / `selection_cancelled` / `server_restart` / `bot_configuration_error` |

---

## 14. Nguồn tham chiếu chính

| Khu vực | File |
|---|---|
| Bàn cờ, state, model | `HeroChess.Rules/GameModels.cs` |
| Luật di chuyển / chiếu / Cọc | `HeroChess.Rules/XiangqiRulesEngine.cs` |
| Handler di chuyển đặc biệt | `HeroChess.Rules/MovementHandlers.cs` |
| Vòng đời lượt, CD, hiệu ứng, vật cản | `HeroChess.Rules/TurnLifecycle.cs` |
| Command Skill handler | `HeroChess.Rules/Skills/*.cs` |
| Effect model | `HeroChess.Rules/Effects/*.cs` |
| Pipeline lệnh, timeout, settlement | `HeroChess.Api/Services/MatchCommandService.cs` |
| Luật lượt dùng chung cho command/bot | `HeroChess.Api/Services/MatchTurnRules.cs` |
| Đóng băng lineup, khởi tạo trận | `HeroChess.Api/Services/MatchSelectionService.cs` |
| Lọc dữ liệu theo người xem | `HeroChess.Api/Services/MatchStateProjection.cs` |
| Validate lineup | `HeroChess.Api/Services/LineupValidator.cs` |
| Điều kiện Faction Skill | `HeroChess.Api/Services/FactionSkillEligibility.cs` |
| Bot | `HeroChess.Api/Services/BotEngine.cs`, `BotConfiguration.cs` |
| Schema + seed | `Hero_Chess_DB_Prototype_v0_1/01_schema.sql`, `02_seed_catalog.sql`, `00_install_fresh_database.sql` |
| Migration catalog | `HeroChess.Api/Data/Bootstrap/08_step6_skills.sql`, `09_hero_movement_fixes.sql`, `13_hero_factions.sql`, `16_faction_skills.sql`, `18_faction_skill_requirements.sql` |
| Quyết định đã xác nhận | `docs/Decisions.md` |
| Kế hoạch thiết kế | `docs/PLAN_Hero_And_Command_Skills.md`, `docs/PLAN_Khoi_Phuc_Hero_Trait.md`, `docs/PLAN_PvP_And_Hero_Movement_Fixes.md` |
