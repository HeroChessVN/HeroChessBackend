# Kế hoạch sửa đồng bộ PvP và kỹ năng hero (09/10/2026)

Phạm vi: sửa luồng vào trận của hai trình duyệt, luật di chuyển Lý Thường Kiệt/Quang Trung/Phạm Ngũ Lão, cập nhật catalog tương ứng và kiểm thử trước khi đưa SQL lên Supabase. Không xóa dữ liệu DEV hoặc trận cũ trong đợt này.

## Trạng thái triển khai (09/10/2026)

- Đã sửa P1–P5 ở React, rules engine, API đóng băng đội hình và catalog. PNL có trait Hoành Sóc; Quang Trung đi thường 1 ô và bấm skill 1–3 ô; Xe/Pháo Lý Thường Kiệt xử lý Thành/Rào theo luật đã chốt.
- Đã thêm `09_hero_movement_fixes.sql` và bổ sung seed nền để cài database mới. Seed lặp giữ nguyên SP hero đã chỉnh trong DB. Migration 09 đã chạy trên Supabase sau dry-run rollback và kiểm tra idempotent; số hero/tài khoản/đội hình/trận, ID hero và SP không đổi.
- Build backend không có warning; 364 test rules, 30 test tích hợp trên PostgreSQL tạm và 3 test frontend đều qua; React production build thành công. Chưa chạy kiểm thử thao tác hai trình duyệt thật hoặc tự khởi động lại API đang chạy trên máy người dùng.

## 1. Nguyên nhân đã đối chiếu code

| Hiện tượng | Nguyên nhân trong code | Nơi sửa chính |
| --- | --- | --- |
| Một bên vào PvP, bên còn lại đứng ở sảnh đến khi bấm Làm mới | `MatchmakingService` chuyển cả hai ticket sang `matching` trước khi lưu trận. `App.jsx` chỉ poll khi `status === 'waiting'`, nên bên thấy `matching` sẽ ngừng poll và không bao giờ nhận `matchId`. WebSocket chỉ bắt đầu sau khi đã mở màn trận. | `frontend/src/App.jsx`; kiểm thử hai trình duyệt |
| Lý Thường Kiệt Xe gặp Thành/Rào như đi kiểu Pháo | `LyThuongKietXeHandler` chỉ tìm **một quân địch phía sau** vật cản rồi dừng; không cho đi vào ô vật cản hoặc ô trống sau nó. Bộ lọc legal còn chặn Xe vào ô Thành. | `MovementHandlers.cs`, `XiangqiRulesEngine.cs` |
| Quang Trung Tướng tự đi 1–3 ô | Trait `special_move` được `Freeze` gắn thẳng làm `MovementImplementationKey`; handler thường đi 1–3 ô. Handler skill riêng hiện lại cho đi tới **9 ô**. | Catalog trait, `MatchSelectionService`, `XiangqiRulesEngine`, `MovementHandlers`, FE |
| Phạm Ngũ Lão không có Hoành Sóc | `HoanhSocHandler` và logic tích điện đã tồn tại, nhưng hero `pham-ngu-lao-rook` trong `02_seed_catalog.sql` có `trait_id=NULL`, nên trận không bao giờ dùng handler. Handler còn cho vượt nhiều quân đồng minh và giữ điện qua nhiều nước của chính Phạm Ngũ Lão. | Catalog/SQL, `HoanhSocHandler`, `XiangqiRulesEngine`, FE |
| Lý Thường Kiệt Pháo giống Pháo thường | Cả Pháo thường và handler riêng đều dùng Rào làm ngòi, không dùng Thành; handler riêng chỉ có nhánh phá Thành. | `LyThuongKietPhaoHandler` và mô tả catalog |

## 2. Các việc triển khai theo thứ tự

### P1 — Đồng bộ ghép trận PvP

1. FE tiếp tục poll cùng ticket ở cả `waiting` **và** `matching`, dừng khi `matched`/`cancelled` hoặc có `matchId`. Hiện “Đang tạo trận” khi `matching`; ngăn request poll chồng nhau.
2. Khi nhận `matchId`, mở trận và làm mới danh sách trận. Nếu GET ticket trả 404 sau restart, làm mới `/matches` trước khi báo mất ticket; không buộc người chơi bấm Làm mới để tìm trận đã được tạo.
3. Kiểm thử hai trình duyệt/tài khoản, cố tình làm chậm `CreateMatchAsync` để một bên poll thấy `matching`; cả hai phải tự mở cùng `matchId`. Kiểm tra lỗi tạo trận và reconnect. Không thêm WebSocket cho lobby ở bước này; polling hiện có đủ cho prototype một API instance.

### P2 — Lý Thường Kiệt Xe

1. Cho Xe đi thẳng như Xe thường; Thành/Rào trên tia **không chặn** các ô phía sau. Xe có thể dừng tại ô Thành/Rào để phá nó hoặc đi qua tới ô trống/quân địch; nếu đi qua thì vật cản vẫn ở đó. Quân đồng minh/địch vẫn chặn tia theo luật Xe. Cọc ẩn không phải vật cản vật lý.
2. Sửa đồng thời handler sinh đích, bộ lọc legal cho ô Thành và bước áp dụng nước đi phá vật cản. Dùng cùng đường tính nước hợp lệ cho nước thật, chiếu Tướng và bot.
3. Test: vào ô Thành/Rào; đi qua một/nhiều vật cản; ăn địch phía sau; không xuyên quân; không tự lộ chiếu; replay khôi phục đúng vật cản còn/mất.

### P3 — Quang Trung Tướng: tách nước thường và nút skill

1. **Đã chốt:** nước thường đi **1 ô thẳng, được ra khỏi cung nhưng không qua sông**; nước skill đi **1–3 ô thẳng** bằng lệnh `hero_active` chủ động, tốn một lượt và kích hoạt CD riêng đang có. Skill không xuyên quân/vật cản, không qua sông. Giới hạn handler skill từ 9 xuống 3. Nước thường phải dùng handler 1 ô không giới hạn cung, không rơi về handler Tướng mặc định chỉ đi trong cung.
2. Trait catalog phải mô tả là skill chủ động. Không dùng trait đó làm `MovementImplementationKey` của nước thường. Đổi mọi chỗ hiện nhận diện Quang Trung qua `MovementImplementationKey` sang trait/skill key: tạo state ban đầu, `GenerateQuangTrungSpecialMoves`, `IsInCheck`, kiểm tra hết hành động, endpoint `/hero-actions` và FE. FE không được đưa Quang Trung vào nút đặt Cọc dành riêng cho Bạch Đằng Giang.
3. Test nước thường 1 ô cả trong và ngoài cung; skill/CD, nước 2–3 ô khi sẵn sàng, từ chối ô thứ 4, bị chặn, chiếu và thoát chiếu, replay/snapshot cũ. Kiểm tra đặc biệt: sau khi skill đưa Tướng ra khỏi cung, nó vẫn đi thường 1 ô được trong lúc chờ CD.

### P4 — Phạm Ngũ Lão Xe: Hoành Sóc Giang Sơn

1. Thêm trait riêng, `implementation_key=rook.hoanh_soc`, gắn đúng `pham-ngu-lao-rook` bằng migration/seed idempotent. Đây là passive có điều kiện, không phải Command Skill hoặc nút kích hoạt.
2. Sau khi **chính quân Phạm Ngũ Lão** ăn một quân, đánh dấu cho **nước di chuyển kế tiếp của chính quân ấy** được xuyên tối đa **một** quân đồng minh. Nước kế tiếp tiêu quyền này dù chọn đi Xe thường; lượt của quân khác không tiêu. Vật cản Thành/Rào và quân địch vẫn chặn như Xe thường. Nếu nước xuyên quân đồng thời ăn địch, coi đó là lần ăn mới để cấp quyền cho nước kế tiếp (đề xuất theo câu “sau khi ăn 1 quân”).
3. Sửa scan và nhận diện nước xuyên theo cùng một điều kiện; hiện tại `passedAlly` cho lọt nhiều đồng minh và scan bỏ qua vật cản. FE hiển thị trạng thái “Hoành Sóc sẵn sàng” trên quân; không hiện CD giả.
4. Test ăn thường → xuyên đúng một đồng minh → mất quyền; ăn rồi đi thường → mất quyền; nhiều đồng minh/vật cản/địch chặn; quân khác ăn không cấp quyền; capture sau xuyên, undo/replay và bot.

### P5 — Lý Thường Kiệt Pháo

1. Handler riêng cho phép cả **Thành và Rào làm ngòi** để ăn quân đầu tiên phía sau. Giữ lựa chọn đang có: có thể vào ô Thành để phá rồi đứng ở đó; khi chọn bắn qua Thành, Thành còn nguyên. Pháo thường giữ luật hiện tại.
2. Cập nhật mô tả trait/hero trong SQL và FE nếu UI dùng mô tả. Test phân biệt Pháo thường và Lý Thường Kiệt với từng loại ngòi, nhiều ngòi, mục tiêu có Khiên và nước gây tự chiếu.

### P6 — SQL và phát hành

1. Sửa seed nguồn và thêm SQL cập nhật idempotent cho DB hiện có: trait Phạm Ngũ Lão, định nghĩa Quang Trung chủ động, mô tả Xe/Pháo Lý Thường Kiệt. Giữ nguyên `hero.id`, ownership, lineup và snapshot trận. Đảm bảo lần bật `DatabaseBootstrap` sau **không hoàn tác** các liên kết trait vừa sửa.
2. Chạy unit/integration Rules, build FE, thử PvP hai tài khoản và trận bot trên DB local/test. Kiểm tra API catalog, `/hero-actions`, lineup 16 ô, nước đi, undo và replay. Không thử integration test có ghi trên Supabase đang dùng.
3. Sau khi code runtime và SQL cùng được nghiệm thu, áp SQL lên Supabase trong transaction, kiểm tra lại catalog và các row tài khoản/trận/đội hình, rồi chạy API bản mới. Tránh đổi catalog lúc có trận `selecting` vì `ContentVersion` sẽ khiến confirm trả `CONTENT_VERSION_CHANGED`.

## 3. Đổi setup point trực tiếp trong DB

- Code đọc `hero.setup_points` từ DB khi xem catalog/validate đội hình; không có giá trị SP hard-code cho từng hero. Đổi SP hợp lệ **không làm crash** API. DB yêu cầu SP ≥ 0, riêng hero không phải GENERAL phải ≥ 1.
- Đội hình đã lưu được tính lại mỗi lần đọc và khi chọn/xác nhận trận. Nếu vượt `ruleset.setup_budget`, nó sẽ thành không hợp lệ (`SP_BUDGET_EXCEEDED`) cho tới khi chỉnh đội hình. Trận đã active giữ `FrozenPiece.SetupPoints` và điểm phân xử ở mốc 150 nước theo giá trị lúc bắt đầu, nên không đổi giữa trận.
- `CatalogVersionService` có đưa SP vào hash: thay SP trong khi trận đang `selecting` có thể khiến confirm trả `CONTENT_VERSION_CHANGED`. Seed `02_seed_catalog.sql` hiện `ON CONFLICT ... setup_points=EXCLUDED.setup_points`; bật bootstrap lại sẽ **ghi đè SP anh vừa sửa trực tiếp trong DB**. Lần áp seed 02 lên Supabase trước kế hoạch này cũng có thể đã ghi đè giá trị thủ công của những hero cùng `code`. Trong đợt sửa này phải chọn một nguồn sự thật: cập nhật giá trị vào seed/migration hoặc đổi seed để không tự đè cân bằng đang được quản trị qua DB.

## 4. Có được xóa `dev-slot-*` không?

**Chưa xóa thẳng.** `player_hero`, `lineup_entry`, và có thể `hero_faction`, `cosmetic`, `coin_transaction` đang tham chiếu `hero.id`; khóa ngoại sẽ từ chối DELETE, còn xóa dây chuyền sẽ làm mất ownership/đội hình/lịch sử kinh tế. Trận cũ giữ hero trong JSON snapshot nên replay có thể vẫn đọc, nhưng không nên dựa vào đó để xóa catalog.

Khi đã có đủ hero thật cho **mọi vị trí của 16 slot** (đặc biệt hiện catalog hero lịch sử chưa có Tốt), làm theo thứ tự: cấp/mua hero thay thế → chuyển và validate từng đội hình → tắt cấp DEV khi tạo tài khoản (`Onboarding:GrantDevelopmentFixtureHeroes=false`) và tắt dev seed → đặt `is_enabled=false` cho DEV để ẩn khỏi catalog → chỉ xóa vật lý sau khi kiểm tra mọi FK bằng SQL và thật sự cần dọn DB. Giữ DEV đã dùng trong lịch sử là lựa chọn đơn giản và an toàn hơn.
