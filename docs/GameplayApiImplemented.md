# Gameplay API implemented (T05–T11)

> Cập nhật 24/09/2026: User → Player/Admin loại trừ nhau; Admin không chơi. Xem [refactor tài khoản](/D:/Code/Github/HeroChessBackend/docs/User_Player_Admin_Refactor.md) cho contract /me, audit và migration mới. Các kết quả ngày 23/09 phía dưới là lịch sử.

Base path `/api/v1`; mọi endpoint dưới đây yêu cầu bearer auth, trừ `/ws/v1` dùng vé một lần.

| Method | Path | Ghi chú |
|---|---|---|
| POST | `/matchmaking/tickets` | Body `{ "mode": "ranked" | "bot" }` |
| GET | `/matchmaking/tickets/{ticketId}` | Poll ticket RAM để lấy `matchId` |
| DELETE | `/matchmaking/tickets/{ticketId}` | Chỉ hủy khi còn `waiting` |
| GET/PUT | `/matches/{matchId}/selection` | PUT nhận `lineupId`, `expectedRevision`; không trả hero đối phương |
| POST | `/matches/{matchId}/confirm` | Idempotent ngay khi participant đã confirm, không đọc lại lineup nguồn |
| DELETE | `/matches/{matchId}/selection` | Participant hủy trận selecting; retry trận cancelled trả 204 |
| GET | `/matches/{matchId}/state` | Snapshot server-authoritative và `serverNow` |
| GET | `/matches/{matchId}/legal-actions` | Bổ sung cho web/Unity tô legal targets |
| POST | `/matches/{matchId}/commands` | REST fallback dùng cùng command pipeline với WebSocket |
| GET | `/matches?cursor=&limit=` | Lịch sử của actor, tối đa 50 |
| GET | `/matches/{matchId}/replay?afterSequence=&limit=` | Chỉ participant, sau khi trận bắt đầu (active/terminal); sequence 0 là bàn cờ ban đầu |
| POST | `/ws-ticket` | Vé 30 giây, dùng một lần |
| POST | `/shop/heroes/{heroId}/purchase` | Header `Idempotency-Key` UUID; giá lấy từ server |
| PATCH | `/admin/heroes/{id}/price` | Role `admin`; body `coinPrice` string và `reason` |
| PATCH | `/admin/cosmetics/{id}/price` | Role `admin`; ghi audit cùng transaction |
| GET | `/admin/audit?cursor=&limit=` | Role `admin`; tối đa 100 |

WebSocket `/ws/v1?ticket=...` nhận `match.subscribe` và `match.command`. Event chính: `match.state`, `match.command_accepted`, `match.command_rejected`, `match.ended`, `match.settled`.

`match.command.payload.action` hỗ trợ `move`, `resign`, và `undo` cho bot mode. `hero_active`/`team_skill` trả `SKILL_NOT_IMPLEMENTED`; `start`, `timeout`, `cancel` chỉ server được tạo.

Replay mở sau `completed` hoặc `cancelled` để không bypass selection kín. Mỗi entry dùng frozen `stateAfter`; không chạy lại catalog hay luật hiện tại.

Purchase khóa wallet, đối chiếu key và hero, rồi cập nhật balance + ownership + ledger trong một transaction. Migration `05_app_extensions.sql` thêm key nullable/unique vào `player_hero`, nhờ vậy hero giá 0 vẫn retry được mà không ghi ledger amount 0. Admin role chỉ đến từ `player.role`; Development có thể seed email admin bằng `Onboarding:DevelopmentAdminEmails`.

## Review fixes — 23/09/2026

- `MatchRuntime:SelectionSeconds` mặc định 120 giây tính từ lúc tạo trận. Worker quét mỗi giây, hủy selection hết hạn bằng `selection_timeout`; người chơi có thể hủy sớm qua DELETE selection (`selection_cancelled`). Không cộng coin/Elo cho trận hủy.
- Command WS luôn kiểm tra participant trước subscribe; retry cùng commandId/payload nhận `match.command_accepted` với `duplicate: true`, không broadcast lại action.
- Subscribe lấy snapshot và enqueue sự kiện trong cùng gate với command để tránh bỏ lỡ version lúc reconnect.
- Socket có outbox FIFO tối đa 64 message và timeout gửi 5 giây. Socket quá chậm bị ngắt để client reconnect/resync; thao tác gửi mạng không giữ gate trận.
- Sau commit, request cancellation không hủy settlement/bot scheduling. Worker độc lập phục hồi terminal chưa settled và bot đang tới lượt từ DB; failed settlement được retry trong cùng process.
- `action` sai cấu trúc/kiểu/range trả 400 `INVALID_ACTION`, không mutate state.
- Web giữ pending command trong memory qua reconnect, retry đúng commandId sau snapshot; không tạo ID mới khi chưa rõ kết quả. Reload trang vẫn mất session/pending theo phạm vi web prototype.
- Snapshot mới dùng `stateSchemaVersion: 2`, thêm `traitKind` và `traitImplementationKey` nullable. Snapshot v1 vẫn đọc/replay được; chỉ trait `special_move` mới ghi vào `movementImplementationKey`.
- Reward và Elo K lấy đúng giá trị frozen, kể cả 0.

## Timeout/AFK được chốt — 23/09/2026

- Timeout thường mất lượt, tính một trong tổng 150 nước. `consecutiveTimeouts` trong state lưu riêng Red/Black; nước đi hợp lệ reset streak của chính người đi, không reset đối thủ.
- Lần timeout thứ hai liên tiếp trên các lượt của cùng người chơi: completed, đối thủ thắng, endReason `afk`.
- Timeout khi đang chiếu: completed, đối thủ thắng, endReason `timeout_in_check`, không còn dùng cancellation policy `unresolved_timeout_in_check`.
- Timeout gây thua trực tiếp được ưu tiên trước so SP ở nước 150; nếu không có kết quả trực tiếp, so SP quân sống như trước.
- Snapshot mới schema v3 thêm consecutiveTimeouts; snapshot cũ thiếu field mặc định 0. Skill hợp lệ sau khi có handler phải thay nước đi, tăng tổng turn/counter một lần và reset AFK của actor; skill chưa implement hiện vẫn bị reject.
