# Kết quả sửa review R01–R11 — 23/09/2026

Đã triển khai các bản sửa trong `Astra_Code_Review_2026-09-23.md`. Không có migration database hoặc package mới; scope vận hành vẫn là một API process cho prototype.

| Review | Bản sửa | Kiểm chứng |
|---|---|---|
| R01 | Participant authorization trước subscribe ở cả hai nhánh WS | Outsider command bị chặn; broadcast không lọt qua socket outsider; test integration và Kestrel thật |
| R02 | Sau commit không dùng cancellation token của request; settlement lỗi được worker retry từ DB; worker phục hồi bot tới lượt | EF interceptor hủy request chính xác sau commit; fault-inject lỗi đọc wallet; thử mất tín hiệu schedule bot |
| R03 | DELETE selection cho participant; timeout selection mặc định 120 giây; hủy không thưởng và giải phóng người chơi | Cancel lặp, outsider cancel, expiration, requeue trong cùng process |
| R04 | Outbox FIFO riêng từng socket, tối đa 64 message; timeout gửi 5 giây; quá tải ngắt socket | Socket giả ngừng đọc không chặn recipient khác; overflow disconnect |
| R05 | Duplicate command trả ACK trực tiếp cho socket gửi, không broadcast lại | Retry cùng commandId qua socket mới; version không tăng; Kestrel thật |
| R06 | Pending giữ qua lỗi mạng/5xx; retry đúng ID sau reconnect/state; ngăn tạo command mới khi lệnh cũ chưa rõ kết quả | Node VM kiểm tra reconnect, duplicate ACK và REST network error |
| R07 | State theo matchId, lọc version trong cùng trận; hủy socket/callback cũ; tách chế độ replay khỏi live; bỏ response legal-actions cũ | Node VM kiểm tra đổi trận v10 → v0, replay và response bất đồng bộ |
| R08 | Already-confirmed participant trả selection hiện tại, không đọc/freeze lại lineup nguồn | Confirm lại sau khi xóa lineup nguồn, trước khi đối thủ confirm |
| R09 | Chỉ special_move thay movement; giữ traitKind/traitImplementationKey riêng trong frozen lineup/state/clone | Hai integration cases active/passive vẫn có base legal moves và giữ metadata |
| R10 | Validate shape, kiểu và range của action trước khi đọc/áp dụng | Null/array/type/GUID/undo sai trả 400, state version giữ nguyên; REST thật |
| R11 | Dùng trực tiếp reward/K frozen kể cả 0 | Thay snapshot policy về 0 khi config đang khác 0; worker settlement/retry giữ coin và Elo |

Subscribe cũng lấy/enqueue snapshot trong cùng gate với command để không bỏ lỡ version ở khoảng chuyển sang subscription.

Kết quả trên database riêng `hero_chess_fix_review_20260923`:

```text
.NET build: 0 warnings, 0 errors
HeroChess.Rules.Tests: 16 passed, 0 failed, 0 skipped
HeroChess.IntegrationTests: 15 passed, 0 failed, 0 skipped
Node web regression: 5 passed, 0 failed
Kestrel + PostgreSQL + native WebSocket smoke: PASS
git diff --check: PASS
```

Test suite tăng từ 21 lên 31 test .NET; thêm 5 test web. Runtime smoke tạo ba tài khoản, một trận ranked, gửi command trái quyền, đi nước hợp lệ, retry trên socket mới, resign, replay, requeue và hủy selection. Không dùng database development hiện có để ghi dữ liệu test. API smoke được dừng sau kiểm chứng.

Lệnh chạy lại:

```powershell
# HERO_CHESS_TEST_DB phải trỏ tới PostgreSQL database disposable, không dùng DB đang chạy game.
dotnet test HeroChessBackend.slnx --no-restore --verbosity quiet
node --test tests/web/app.test.mjs

# Sau khi khởi chạy API Development với DB disposable trên cổng 5197:
$env:HERO_CHESS_SMOKE_URL = 'http://127.0.0.1:5197'
node tests/live/ws-smoke.mjs
```

Giới hạn kiểm chứng: Node VM kiểm tra logic web; chưa chạy smoke trực quan hai trình duyệt. Không mô phỏng kill PostgreSQL giữa wire-level COMMIT, chưa kiểm tra tải hoặc nhiều API instance. Không đánh dấu toàn bộ T15 hoàn thành dựa trên lượt sửa này.

Quyết định vận hành: selection hết hạn sau 120 giây, cấu hình bằng `MatchRuntime:SelectionSeconds`; worker quét mỗi giây. Web prototype giữ session và pending trong memory, reload trang cần login lại. Snapshot mới dùng schema v2 với trait metadata nullable; snapshot v1 vẫn đọc được. Hero passive/active giữ base movement, hiệu ứng riêng chưa triển khai vẫn cần chủ dự án bổ sung.
