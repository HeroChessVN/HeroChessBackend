# HERO CHESS — Bộ DB prototype v0.1

> Schema mới ngày 24/09: chạy migration 06_user_inheritance.sql sau baseline 01/04/05 và trước seed 03. Bảng player + identity.app_user được hợp nhất thành user_account; auth_identity.user_id và admin_audit_log.actor_user_id thay tên cũ. Sơ đồ/dictionary dưới đây là baseline lịch sử. Xem [mô hình hiện tại](/D:/Code/Github/HeroChessBackend/docs/User_Player_Admin_Refactor.md).

Ngày: 21/09/2026. Nền tảng: PostgreSQL 16+; mục tiêu PostgreSQL 18 hoặc PostgreSQL do Supabase cung cấp. Đây là schema khởi tạo và hợp đồng tích hợp đề xuất; chưa phải backend hoặc bộ luật chạy được.

## 1. Bước tiếp theo của dự án

Phân tích yêu cầu đã đủ để bắt đầu thiết kế kỹ thuật prototype. Thực hiện một lát cắt chạy xuyên suốt: đăng nhập → lấy catalog → lưu lineup → hai người vào trận → server nhận một nước → cả hai client thấy cùng trạng thái.

1. Thống nhất DB v0.1 và DTO/API v0.1 trong bộ này.
2. Backend tạo migration/entity, API đọc catalog và API lineup; Unity dùng mock JSON trước.
3. Tạo game rules C# dùng chung. Client chỉ xem trước, server quyết định kết quả.
4. Ghép hai client với backend; lưu hành động và snapshot sau mỗi hành động hợp lệ.
5. Thêm từng hero/skill, coin/shop, replay và bot theo ưu tiên nhóm.

Frontend web cực đơn giản dùng để test là phù hợp: login, chọn lineup, bàn 9×10 bằng chữ, gửi action, xem JSON/log. Nó sử dụng đúng API và WebSocket của Unity; không xây thêm backend hay tự quyết định thắng/thua. Giữ app test nhỏ để tránh làm hai frontend hoàn chỉnh.

## 2. Danh sách file và cách chạy

| File | Mục đích |
|---|---|
| `01_schema.sql` | Tạo 25 bảng, khóa, chỉ mục và ràng buộc dữ liệu |
| `02_seed_catalog.sql` | Bảy class, 16 vị trí chuẩn, ruleset và ba Tượng đã mô tả |
| `03_seed_dev_only.sql` | Tùy chọn: hai player, 16 hero kỹ thuật, ba skill mock và hai lineup 43 SP |
| `04_ERD.md` | Ba sơ đồ Mermaid theo nhóm chức năng |
| `05_API_Contract.md` | API/WebSocket và DTO tối thiểu để giao Unity |
| `06_mock_data.json` | Dữ liệu mẫu có ID khớp seed dev, đầy đủ bàn cờ 32 quân |
| `07_Data_Dictionary.md` | Cột, kiểu dữ liệu và khả năng NULL của từng bảng |
| `08_Verification.md` | Môi trường và kết quả kiểm tra thực tế |
| `09_constraint_tests.sql` | Kiểm tra ràng buộc trong transaction rồi rollback |

### Supabase

Mở một project thử nghiệm → SQL Editor → chạy toàn bộ `01_schema.sql`, sau đó `02_seed_catalog.sql`. Nếu muốn có dữ liệu cho UI/backend test, chạy `03_seed_dev_only.sql`. Sau đó có thể mở Table Editor và chọn schema `hero_chess`.

Không cần `CREATE DATABASE` trong Supabase. Không đổi các bảng auth/storage của Supabase. Không thêm schema `hero_chess` vào danh sách schema được public Data API. Script thu hồi quyền PUBLIC và quyền `anon`/`authenticated` nếu các role này tồn tại. Schema phục vụ backend truy cập SQL qua Npgsql; không cung cấp chính sách RLS truy cập trực tiếp từ Unity.

DB role backend nên là role riêng, được cấp USAGE trên schema và quyền bảng cần dùng; credential chỉ đặt tại server. Nếu lúc thử nghiệm dùng connection quản trị, chỉ sử dụng ở máy backend. Không đưa connection string, mật khẩu DB hay service-role key vào Unity hoặc web test. Quyền đăng nhập tài khoản game khác với quyền đăng nhập PostgreSQL.

`01_schema.sql` chạy một lần. Nếu schema đã tồn tại, script sẽ lỗi và rollback; không xóa dữ liệu cũ. Mỗi sửa đổi tiếp theo viết migration mới. Hai seed chạy lại không ghi đè dữ liệu đã có. Nếu đã tồn tại ruleset active khác, phải chủ động quyết định bản active trước khi seed; không tự tắt bản đang dùng.

### PostgreSQL cục bộ

Tạo database rỗng bằng công cụ quản trị, kết nối vào database đó rồi chạy các file theo cùng thứ tự. Ví dụ với psql đã cấu hình kết nối:

```sh
psql "$HERO_CHESS_DB_URL" -v ON_ERROR_STOP=1 -f 01_schema.sql
psql "$HERO_CHESS_DB_URL" -v ON_ERROR_STOP=1 -f 02_seed_catalog.sql
# Chỉ trên DB development:
psql "$HERO_CHESS_DB_URL" -v ON_ERROR_STOP=1 -f 03_seed_dev_only.sql
psql "$HERO_CHESS_DB_URL" -v ON_ERROR_STOP=1 -f 09_constraint_tests.sql
```

Với EF Core, map default schema `hero_chess`, tên bảng/cột snake_case, khóa UUID. Chọn một nguồn quản lý schema: migration đầu chạy nội dung SQL này, hoặc scaffold entity rồi baseline schema hiện có. Không vừa chạy SQL thủ công vừa để EF tạo lại các bảng giống nhau. Composite foreign key, CHECK, partial index và trigger phải được giữ trong migration; scaffold không thay thế toàn bộ các ràng buộc này.

## 3. Phạm vi

Giữ Ranked và bot; không thêm Casual, season, ban/pick, đa tiền tệ hay microservices. Có một ví coin/player, rating Elo, hero sở hữu, skin hero tùy chọn, lineup, trạng thái trận, replay và audit quản trị.

Ngân sách 50 SP; hai Sĩ và hai Tượng đều cơ bản 1 SP thì lineup 43 SP. Hai Tượng 2 SP và hai Sĩ 1 SP thì 45 SP. Lượt 90 giây; hết hạn mất lượt theo ruleset. Mốc 150 xét SP còn lại, bằng SP hòa. Chi tiết đếm giới hạn và một số hiệu ứng vẫn thuộc cấu hình/bộ luật được nhóm quyết định sau. Không gán thêm giá coin, reward, cooldown hoặc skill chưa được duyệt.

Ba hero lịch sử trong seed có giá SP và trait theo trao đổi; `is_enabled=false`, `is_starter=false`, giá coin mặc định 0 chỉ là trạng thái chưa cấu hình phát hành, không có nghĩa phải bán miễn phí hay bỏ khỏi roster cấp sẵn. Backend không cho mua/chọn hero bị tắt. Nhóm bật và cấu hình cấp sẵn khi nhập roster chính thức. Chưa gán faction lịch sử vì chưa chốt mapping nội dung.

Seed dev là fixture kỹ thuật: 16 hero khác ID, không gán tên nhân vật lịch sử; ba skill mock trả `SKILL_NOT_IMPLEMENTED` khi sử dụng. Player dev không có mật khẩu hoặc token và không thể đăng nhập tự động. Gắn identity thật qua backend có kiểm tra quyền, hoặc dùng auth giả chỉ trong môi trường development. Không đưa cơ chế chọn playerId để đăng nhập vào bản public.

## 4. Điều chỉnh từ ERD được cung cấp

| ERD ban đầu | Thiết kế vật lý |
|---|---|
| Player unlocks Hero | `player_hero` lưu quyền sở hữu |
| Player owns Cosmetic | `player_cosmetic`; `cosmetic.hero_id` xác định skin thuộc hero |
| Hero affiliated with Faction | `hero_faction` cho hero có nhiều faction |
| Lineup selects Command Skill | `lineup_skill`, ba vị trí; tên `team_skill` bao gồm command và faction active |
| Faction provides Skill | `team_skill.faction_id` nullable + UNIQUE: tối đa một skill cho mỗi faction |
| Hero has Trait | `hero.trait_id` nullable: tối đa một trait/hero; nhiều hero có thể dùng chung định nghĩa trait |
| Hero thuộc nhân vật lịch sử | `historical_character`: nhiều phiên bản hero/class của một nhân vật |
| Match sử dụng Lineup | `match_participant.lineup_snapshot` là bản sao; ID nguồn chỉ để truy vết |
| Match generates Replay | Replay đọc `match_action.state_after`; không cần bảng chứa cùng nội dung lần hai |
| Currency / Player Currency | Một `player_wallet` và `coin_transaction`; chỉ coin |
| Match affects Rating | Rating hiện tại trong `player_rating`; trước/sau trận trong participant |

Không lưu khóa ngoại trực tiếp từ trận vào lineup đang sửa/xóa. Snapshot giữ đủ thông tin trận cũ. `source_lineup_id` có thể trỏ tới ID đã xóa và không dùng để tái dựng lịch sử.

## 5. Trách nhiệm của DB và backend

| Quy tắc | Nơi bảo đảm |
|---|---|
| UUID duy nhất, FK tồn tại, số dư không âm | DB |
| Không sở hữu trùng cùng phiên bản hero/skin | Primary key DB |
| Slot 1–16, hero đúng class của slot | Composite FK DB |
| Skin phù hợp hero | Composite FK DB |
| Skill slot 1–3, không chọn cùng skill hai lần | CHECK + UNIQUE DB |
| Faction không có hai skill active khác nhau | UNIQUE DB |
| Một player không ở cả hai bên trong cùng trận | UNIQUE DB |
| Ranked không chứa bot/guest; active đủ hai bên | Deferred constraint trigger DB |
| Không ghi hai action cùng sequence/command ID | UNIQUE DB |
| Không cộng reward cùng trận hai lần | Partial UNIQUE DB + transaction backend |
| Đủ 16 slot, đúng 3 skill khi lưu lineup | Backend kiểm tra toàn bộ rồi commit một transaction |
| SP ≤ 50, ownership, hero enabled, faction hợp lệ | Backend; kiểm tra khi lưu và xác thực lại khi khóa vào trận |
| Cấm trùng nhân vật giữa các phiên bản/class | Backend; ngoại lệ mẫu cơ bản cần nhóm xác định |
| Skin sở hữu và phù hợp người chơi | Backend; DB chỉ kiểm tra skin đúng hero |
| Lineup thuộc người chọn; chỉ sửa dữ liệu của mình | Backend dựa trên identity đăng nhập |
| Skill hết charge/cooldown, chiếu, rào, hồi sinh | Bộ luật C# trên server |
| 150 nước, 90 giây, các tình huống hết hạn | Server scheduler + bộ luật |
| Một người không tham gia hai trận đang chạy | Match service trong prototype một server |
| Ledger khớp wallet; rank/reward đúng một lần | Transaction backend, khóa row và trạng thái settlement |
| JSON state/snapshot có đủ trường, đúng cấu trúc | DTO/schema validation backend; SQL chỉ kiểm tra object/array |
| Lịch sử không bị sửa, snapshot bất biến | Backend chỉ append; quyền DB theo môi trường triển khai |

DB không có trigger đếm đủ 16 quân ở mỗi INSERT, vì backend cần ghi nhiều entry trong cùng transaction. Vì vậy gọi SQL trực tiếp có thể tạo lineup thiếu quân. API phải từ chối và rollback toàn bộ yêu cầu không hợp lệ; không cho lưu draft trong UI/API hiện tại.

`is_guest` không được đổi từ false thành true khi đã tham gia Ranked. Trigger kiểm tra roster tại lúc trận/participant thay đổi; player service phải giữ quy tắc chuyển guest → registered một chiều.

## 6. Giao dịch backend cần làm

### Lưu lineup

Nhận toàn bộ 16 entry và ba skill → xác thực → BEGIN → kiểm tra `revision` hiện tại (row lock hoặc conditional UPDATE) → ghi lineup/entry/skill → tăng revision → COMMIT. Thất bại rollback toàn bộ. Không ghép 16 request độc lập từ Unity.

### Mua hero

BEGIN → khóa `player_wallet` bằng `FOR UPDATE` → tìm `(player_id,idempotency_key)` → nếu có, xác nhận request cùng loại/cùng hero rồi trả kết quả cũ → lấy giá hiện hành, kiểm tra enabled, chưa sở hữu và đủ tiền → trừ coin → INSERT ownership → INSERT ledger ghi giá âm và balance_after → COMMIT. Nếu request key cũ được dùng cho hero khác, trả `IDEMPOTENCY_CONFLICT`. Hero giá 0: chỉ grant ownership có PK chống trùng, không tạo ledger amount 0; endpoint lần sau trả trạng thái đã sở hữu.

Khi một lần gọi đồng thời làm UNIQUE lỗi, rollback rồi đọc kết quả đã commit, không tiếp tục dùng transaction đã lỗi. Sau timeout client có thể gửi lại cùng key an toàn.

### Khởi tạo và chơi trận

Trong giai đoạn selecting, server nhận lineup của từng bên; chỉ gửi skill đối thủ, không gửi hero/trait chưa được phép lộ. Khóa lineup → lưu snapshot → sau hai bên xác nhận, tạo state version 0 và action kind=start sequence 0 trong cùng transaction, chuyển active.

Mỗi lệnh: xác thực actor → kiểm tra command_id → khóa match_state hoặc dùng version condition → so expectedVersion → kiểm tra hạn theo thời điểm server nhận → áp dụng rules engine → INSERT action với sequence = version+1 và snapshot → UPDATE state/version → cập nhật kết quả nếu cần → COMMIT → phát thông báo.

Nếu commit xong nhưng phát WebSocket thất bại: reconnect lấy lại current snapshot. Nếu action được gửi lại, trả kết quả của command cũ. Command ID trùng nhưng payload khác phải bị từ chối. `sequence_no` là thứ tự log; `turn_index` là lượt gameplay; `counted_actions` là bộ đếm mốc 150. Không mặc định ba giá trị luôn bằng nhau khi có timeout/cướp lượt/undo.

### Kết thúc trận và thưởng

BEGIN → khóa `game_match` → nếu settled_at có giá trị trả settlement cũ → khóa wallet/rating của hai người theo thứ tự player UUID nhất quán → áp dụng công thức thưởng/rating từ backend → INSERT ledger cho khoản thưởng >0 → ghi participant elo_before/after, coin_reward → đặt settled_at → COMMIT. Hòa, hủy và reward 0 vẫn cần settled_at để không xử lý lại. Không dùng số coin client gửi lên.

Đây là đặc tả giao dịch, chưa phải stored procedure hay implementation C#.

## 7. Snapshot, replay và bot undo

`match_state` giữ trạng thái mới nhất. `match_action` giữ toàn bộ các sự kiện được chấp nhận và state_after; có bản start ở sequence 0 để replay trở về bàn đầu. Không ghi yêu cầu nước đi bị từ chối thành action chính thức.

Snapshot có pieceId độc lập với heroId: hai người có thể cùng chọn hero, mỗi quân vẫn là một instance riêng. Lưu cả vị trí xuất phát, trạng thái bị bắt/hồi sinh chờ, SP đóng băng, trait state (ví dụ lastMoveDistance), rào/thành, tài nguyên skill, thời điểm hết buff và kết quả trận. Giá trị null không được hiểu tùy ý thành vô hạn hoặc chưa bị dùng.

Bot undo tạo action mới kind=undo, request/resolved event có targetSequence. state_after khôi phục dữ liệu gameplay của trạng thái mục tiêu, còn version/log sequence tiếp tục tăng. Không DELETE các action cũ. Chỉ cho undo khi bot match đang active; reward chỉ settlement sau kết thúc. Replay hiển thị lịch sử thực tế, có cả sự kiện undo. Ranked không nhận undo.

Ruleset và lineup snapshot phải đóng băng giá SP, tham số hero/skill và mã phiên bản. Update catalog không được làm thay đổi replay. Với snapshot replay, không cần chạy lại luật mới để tái dựng quân. Phải giữ asset key/fallback để vẫn hiển thị được nội dung cũ.

## 8. Thứ tự giao cho nhóm

- Unity: nhận `05_API_Contract.md` và `06_mock_data.json`, dùng API adapter/mock adapter; không phụ thuộc tên cột SQL.
- Backend: chạy schema/seed, map entity, thực hiện catalog + lineup trước; bước tiếp theo session/match/state/action.
- App test: dùng cùng DTO, vẽ ô/giao điểm bằng HTML; mở hai phiên đăng nhập trên hai cửa sổ để kiểm tra.
- Cả nhóm: thống nhất codes và version v0.1, không đổi tên enum/DTO riêng ở mỗi bên.

Prototype đầu tiên đạt khi hai client xem cùng version, server chặn nước sai lượt, gửi lại command không đi hai lần và tải lại trang nhận đúng trạng thái. Chất lượng skill/UI tiếp tục hoàn thiện sau.

## 9. Nguồn kỹ thuật

- PostgreSQL constraints: https://www.postgresql.org/docs/18/ddl-constraints.html
- PostgreSQL JSON: https://www.postgresql.org/docs/current/datatype-json.html
- Supabase roles: https://supabase.com/docs/guides/database/postgres/roles
- Npgsql EF Core: https://www.npgsql.org/efcore/

Không có thao tác tạo/chỉnh DB cloud nào được thực hiện trong việc bàn giao này. Kết quả kiểm tra nằm trong `08_Verification.md`.
