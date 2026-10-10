# Hero Chess — Implementation status

> Cập nhật 24/09/2026: User → Player/Admin loại trừ nhau; Admin không chơi. Xem [refactor tài khoản](/D:/Code/Github/HeroChessBackend/docs/User_Player_Admin_Refactor.md) cho contract /me, audit và migration mới. Các kết quả ngày 23/09 phía dưới là lịch sử.

Ngày cập nhật: 23/09/2026.

## Cập nhật 24/09 — User, Player và Admin

- `User` là lớp cha, `Player`/`Admin` là hai subtype loại trừ nhau; EF TPH lưu trong `hero_chess.user_account`.
- Admin chỉ quản trị, không onboarding ví/rating/hero, bị chặn khỏi API và WebSocket gameplay. Policy đọc role/status hiện tại trong DB.
- Migration `06_user_inheritance.sql` hợp nhất tài khoản, giữ UUID/hash/stamp/FK/audit và lịch sử; kiểm tra orphan và rollback khi không tương thích; đã thử chạy hai lần.
- `/me` có userId chung và trường gameplay nullable; audit chuyển actorUserId. Xem tài liệu refactor trước khi cập nhật client.
- Kiểm chứng trên PostgreSQL 18 test riêng port 55434: **16 Rules + 25 Integration = 41 .NET tests PASS**, **5 web tests PASS**, **23 SQL constraints PASS**, build **0 warning / 0 error**.
- Database local chính chưa được nâng cấp trong đợt này. Bootstrap bản mới có thể áp dụng migration khi bật; dừng API cũ và sao lưu trước khi nâng cấp.

Các mục T01–T14 bên dưới ghi lại kết quả triển khai trước refactor.

### Cập nhật trait riêng cho từng hero — 24/09

- `Hero.TraitId` là quan hệ one-to-one tùy chọn, có unique index `uq_hero_trait_id`. Hero chưa có trait được để NULL; trait nháp chưa gán được phép tồn tại.
- Migration `07_exclusive_hero_traits.sql` clone định nghĩa bị chia sẻ cho từng hero, giữ nguyên kind/handler/parameters/description; không sửa snapshot trận. ID clone ổn định theo hero, seed mới dùng cùng ID.
- Trần Bình Trọng/Bùi Thị Xuân có trait riêng và vẫn dùng chung `elephant.diagonal_range`; tên/mô tả có thể chỉnh độc lập.
- Chạy lại solution: **16 Rules + 26 Integration = 42 test .NET PASS**, gồm test migration cũ → mới, chạy lặp, seed lặp, chặn trait trùng và đổi tên độc lập. Không chạy lại web tests vì không đổi client.
- Chỉ nâng cấp database test riêng trong đợt này; database local chính chưa đổi.

| Task | Module | Status | Evidence / limitation |
|---|---|---|---|
| T01 | Contracts/Rules projects, controlled SQL bootstrap, EF mappings, health | implemented-tested | PostgreSQL 18 container healthy; bootstrap tạo 25 bảng, seed restart không lặp và 23 constraint tests PASS |
| T02 | Identity bearer register/login/refresh, atomic player onboarding, `/api/v1/me` | implemented-tested | Hai account thật register/login/refresh và đọc `/me`; anonymous 401, password sai 401, duplicate register 400 |
| T03 | Catalog/ownership và atomic lineup CRUD/validation | implemented-tested | Hai account sở hữu 16 fixture hero, lưu lineup 43 SP; stale revision 409, foreign player 404, invalid lineup 422 không ghi dở. Faction handler chỉ kiểm tra tối đa một faction skill vì catalog chưa có eligibility đã chốt |
| T04 | Xiangqi rules, legal actions, result và movement handler registry | implemented-tested | 15 unit tests PASS trên .NET 10; Rules target .NET Standard 2.1 |
| T05 | Queue ranked/bot, reservation, selection kín, confirm và start snapshot | implemented-tested | Hai account ghép ranked; bot tách mode; confirm đồng thời tạo đúng state/action version 0; content fingerprint chặn catalog đổi giữa chừng |
| T06 | Vé WebSocket, subscribe, command FIFO, dedup/version và broadcast | implemented-tested | Hai WebSocket thật nhận cùng snapshot; hai command version 0 cạnh tranh chỉ một commit; retry cũ idempotent, đổi payload 409 |
| T07 | Web test cùng origin | implemented-smoke | `wwwroot` có auth/catalog/lineup/queue/selection/board 9×10/legal targets/log; browser smoke thủ công chưa tự động hóa |
| T08 | Deadline, timeout, reconnect và startup recovery | implemented-tested | TimeProvider inject được; test trước/đúng/sau deadline không chờ 90 giây; recovery hủy match dang dở bằng `server_restart` |
| T09 | Result/action limit, RatingPolicy/RewardPolicy và settlement | implemented-tested | Elo dùng giá trị trước trận; lock UUID order; hai retry settlement không cộng coin/Elo lần hai |
| T10 | History/replay snapshot | implemented-tested | Actor-only, xem action sau khi trận bắt đầu, cursor/sequence pagination; xóa source lineup sau start không ảnh hưởng replay |
| T11 | Bot deterministic và append-only undo | implemented-tested | Bot dùng legal generator + pipeline chung; undo tăng version và khôi phục board/trait state; ranked undo bị từ chối |
| T12 | Shop hero, idempotency, admin price/audit | implemented-tested | Concurrent retry chỉ trừ 100 coin một lần; free hero không có ledger 0; disabled/insufficient/owned/key conflict; player 403 và admin audit PASS |
| T13 | Ba special elephant và điểm mở rộng hero | implemented-tested | Development overlay bật/grant ba hero; 16 Rules tests gồm JSON round-trip; undo integration giữ `lastMoveDistance`; có `AddingHeroLogic.md` |
| T14 | Web test đầy đủ ngày 2 | implemented-smoke | Legal targets, server clock, reconnect, stale resync, settlement, history/replay và bot undo; static page + JS syntax được kiểm tra, hai WebSocket integration PASS |

## T01 evidence

- `dotnet restore HeroChessBackend.slnx --configfile NuGet.Config`: PASS.
- `dotnet build HeroChessBackend.slnx --no-restore`: PASS, 0 warning, 0 error.
- PostgreSQL 18 Alpine chạy bằng Docker Compose tại port development 55432; healthcheck PASS.
- Host Development bootstrap thành công; `/health/live` 200 và `/health/ready` 200 khi DB kết nối. Khi thiếu DB, readiness trả 503.
- SQL nguồn `01_schema.sql`, catalog seed và dev fixture được copy nguyên vẹn vào output; bootstrap chỉ chạy schema khi `hero_chess` chưa tồn tại và không dùng `EnsureCreated`.
- Schema có đúng 25 gameplay table; schema có sẵn sai số bảng bị từ chối thay vì tự sửa/drop.
- Chạy `09_constraint_tests.sql` trên PostgreSQL native: 23/23 PASS và transaction ROLLBACK.

## T02 evidence and limitations

- Identity endpoint: `/api/v1/auth/register`, `/login`, `/refresh`; bearer authentication bật trước authorization.
- User store tạo Identity user → player → auth_identity → wallet/rating → ownership trong một transaction.
- Dev fixture ownership chỉ grant khi Development và explicit config bật.
- Data Protection keys persist ở `HeroChess.Api/keys/`, đã được gitignore. Local key encryption policy chưa được cấu hình cho container/production.
- Hai account có player ID riêng; tổng ownership của hai account là 32 và bootstrap/restart không grant lặp.
- Refresh token được kiểm tra qua security stamp; test thực tế trả 200.

## T03 evidence and limitations

- Catalog trả DTO có kiểu và ownership theo actor, không serialize EF entity.
- Lineup validate 16 slot, class, SP, ownership, enabled, historical character, cosmetics, ba skill, duplicate skill và tối đa một faction skill.
- PUT/DELETE ràng buộc player + expected revision ngay trong câu lệnh DB; children được thay trong cùng transaction.
- Fixture 43 SP lưu thành công cho hai account. PUT cùng expected revision: lần đầu 200, retry key revision cũ 409.
- Account B cập nhật lineup A nhận 404. Lineup 0 entry/0 skill nhận 422 và số lineup không đổi.

## T04 evidence

- Có luật Tướng/Sĩ/Tượng/Xe/Pháo/Mã/Tốt cho cả Red và Black; lọc nước tự chiếu và lộ mặt hai Tướng.
- Pháo yêu cầu đúng một ngòi; Mã bị cản chân; Tượng bị cản mắt và không qua sông; Tốt chỉ đi ngang sau qua sông.
- Không ăn Tướng để kết thúc; hết legal actions tạo `checkmate` hoặc `no_legal_actions`.
- Registry dùng đúng implementation key từ seed: `elephant.diagonal_range`, `elephant.alternating_distance`.
- Dã Tượng lưu `lastMoveDistance`; clone/snapshot giữ trait state. Handler không biết không được âm thầm fallback sang luật quân cơ bản.
- Team skill chưa có handler trả `SKILL_NOT_IMPLEMENTED` và không mutate state.

## T05–T11 evidence and limitations

- Matchmaking giữ queue/reservation trong RAM đúng phạm vi prototype. Nhiều process backend cần queue/lock phân tán ở giai đoạn production.
- Match tạo `contentVersion` SHA-256 từ catalog gameplay; confirm tính lại fingerprint và trả `CONTENT_VERSION_CHANGED` nếu nội dung đã đổi.
- Dispatcher dùng FIFO gate riêng mỗi match cho REST, WebSocket, bot và timeout. Authorization participant chạy trước lookup command; action/state commit trước broadcast.
- WebSocket ticket là random 256-bit, hết hạn 30 giây, dùng một lần; socket giới hạn 64 KiB, 60 message/10 giây, serialized send và keepalive 20 giây.
- Deadline nằm trong `match_state`; timeout tại đúng deadline được nhận, timer lặp không áp dụng lại version cũ. Theo luật chốt 23/09/2026: đang chiếu thì thua `timeout_in_check`; trường hợp thường mất lượt, hai lần liên tiếp trên lượt riêng của cùng người chơi thua `afk`. Timeout luôn tăng countedActions.
- Startup recovery cancel `selecting`/`active`; active append cancel snapshot, selecting không tạo giả start sequence 0. Terminal chưa settle được retry idempotent.
- Action limit lấy từ frozen ruleset snapshot. Đến giới hạn, checkmate/no-legal đã được ưu tiên; nếu chưa kết thúc thì so SP quân còn sống.
- Ranked Development reward: thắng 10 coin, hòa 5, thua 0; Elo K=32. Bot/cancel không đổi coin/Elo/stats.
- Replay đọc trực tiếp `state_after`, chỉ participant được xem từ lúc trận bắt đầu. Test xóa lineup nguồn sau start vẫn replay đủ sequence.
- Bot có budget 250 ms, ưu tiên captured SP rồi tie-break piece/coordinate. Undo chỉ bot active, append event mới, cấp deadline mới và không xóa lịch sử.
- Các fault-injection hiếm như PostgreSQL chết đúng giữa settlement và test browser tự động chưa có trong suite; transaction/recovery path đã được triển khai để retry.

## T12–T14 evidence and limitations

- Purchase chỉ nhận hero ID và `Idempotency-Key`; coin price/enable được đọc sau khi lock wallet. Cùng key trả kết quả cũ, cùng key đổi hero trả 409, key mới mua hero đã sở hữu trả 409.
- `05_app_extensions.sql` thêm `player_hero.idempotency_key` và unique partial index. Entitlement miễn phí giữ key nhưng không tạo `coin_transaction` amount 0.
- Development onboarding cấp 1000 coin bằng ledger key cố định; cấu hình production mặc định 0. Không có endpoint public điều chỉnh số dư.
- Admin bearer claim lấy từ `player.role`. Danh sách admin chỉ được seed qua `Onboarding:DevelopmentAdminEmails` khi environment là Development; player thường nhận 403.
- PATCH giá hero/cosmetic và `admin_audit_log` commit cùng transaction. Test đổi giá khi match đang chạy xác nhận frozen setup points/replay không đổi.
- Catalog nguồn vẫn để ba hero đặc biệt disabled. `03_seed_dev_only.sql` là overlay duy nhất bật/grant chúng; release policy chưa bị giả định.
- Web UI giữ token trong memory, tự lấy ws-ticket mới khi reconnect, resync khi stale/deadline, và không tự phát commandId mới. Reload yêu cầu login lại theo thiết kế prototype.
- Browser automation trực quan chưa có; static page được serve trong integration test và `node --check` PASS.

## Test result

```text
HeroChess.Rules.Tests: Passed 16, Failed 0, Skipped 0
HeroChess.IntegrationTests: Passed 5, Failed 0, Skipped 0
PostgreSQL constraint tests: Passed 23, Failed 0
```

Kết quả PGlite 23 PASS trong bộ DB nguồn không được tính là backend integration test.

## Review fixes R01–R11 — 23/09/2026

Đã sửa và kiểm chứng 11 phát hiện trong báo cáo Astra. Xem [Review_Fix_Status_2026-09-23.md](Review_Fix_Status_2026-09-23.md) để đối chiếu từng lỗi với regression test.

Kết quả mới: 16 Rules + 15 integration tests PASS; 5 web logic tests PASS; Kestrel/PostgreSQL/native WebSocket smoke PASS. Có fault injection hủy request sau commit, lỗi settlement và mất tín hiệu bot. Database test tách riêng; chưa chạy smoke trực quan hai trình duyệt hoặc kill PostgreSQL giữa COMMIT.

Selection có cancel/expiry 120 giây; worker phục hồi settlement/bot trong process đang chạy; WS gửi qua bounded outbox; pending retry cùng commandId; state web tách theo match/replay. State schema v2 lưu trait kind/key riêng, passive/active không thay base movement. Các phần hero effect chưa có đặc tả vẫn giữ nguyên phạm vi chưa triển khai.


## Luật timeout/AFK và gameplay đã chốt — 23/09/2026

Đã thay cancellation khi hết giờ trong chiếu bằng thua ngay, thêm chuỗi timeout riêng từng bên; nước đi hợp lệ xóa chuỗi của actor. Hai timeout liên tiếp trên các lượt của cùng người chơi xử thua AFK. Kết quả trực tiếp ưu tiên trước so SP ở nước thứ 150. Snapshot schema v3 lưu chuỗi AFK, clone/replay/undo giữ nguyên và snapshot cũ thiếu field mặc định 0.

Kiểm chứng sau thay đổi: `dotnet test HeroChessBackend.slnx --no-restore --verbosity quiet` PASS **16 Rules + 21 integration tests**, 0 failed/skip. Sáu case mới kiểm tra AFK giữa hai bên, reset chuỗi/command sai, hết giờ trong chiếu và AFK tại nước 150, SP tại nước 150, tương thích snapshot/clone.

Luật skill tiêu thụ lượt/cooldown chung/ưu tiên thủ, phe đơn và quyền mở từ đầu trận đã ghi ở Decisions.md. Các skill chưa có đặc tả vẫn chưa có handler; chưa triển khai dispatcher cooldown/effect hoặc ngưỡng phe cụ thể. Bot cơ bản đã có, chưa có mức Elo; mục tiêu khoảng 20 hero và khả năng demo LAN là phạm vi nội dung/triển khai tiếp theo, không phải các hạng mục đã nghiệm thu.
