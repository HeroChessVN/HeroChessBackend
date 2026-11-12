# Bot metrics — giai đoạn baseline

Phần này đo bot legacy `capture_sp_v1` trước khi thay thuật toán. Chưa bật các mức easy/medium/hard. Không tách MSS/service game. OpenTelemetry metrics gửi trực tiếp từ API lên Grafana Cloud, không mở cổng metrics công khai và không ghi metric vào Supabase.

## Cấu hình Grafana Cloud

Trong Grafana Cloud, mở trang OpenTelemetry của stack, lấy OTLP HTTP endpoint và tạo credential chỉ có quyền ghi metrics. Dùng metrics endpoint **đầy đủ** kết thúc bằng `/v1/metrics`. Thêm vào `/home/azureuser/herochess.env` trên VM (không đưa vào Git):

```dotenv
Observability__Enabled=true
OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=https://OTLP_HOST/otlp/v1/metrics
OTEL_EXPORTER_OTLP_METRICS_HEADERS=Authorization=Basic%20BASE64_INSTANCE_ID_AND_TOKEN
```

Lấy chuỗi headers do giao diện Grafana sinh, giữ nguyên encoding của nó. Endpoint trên là mẫu, thay bằng URL của stack. Không gửi token vào chat hoặc log. `Observability__Enabled` mặc định false; đặt false để tắt exporter khi điều tra. Không bật trace/log export ở đợt này. API gửi metrics mỗi 15 giây, timeout export 5 giây. Trước khi restart phải xử lý gate deploy: startup recovery hiện hủy trận đang chơi.

## Những gì đang đo

| Instrument (tên .NET) | Nghĩa |
| --- | --- |
| `herochess.bot.schedule` | Counter `result=accepted/deduplicated/full` |
| `herochess.bot.queue.waiting` | Số job trong channel, chưa tính in-flight |
| `herochess.bot.in_flight` | Số job worker đang xử lý |
| `herochess.bot.queue_wait` | Histogram giây, enqueue thành công → dequeue |
| `herochess.bot.state_read` | Histogram giây, đọc DB để xác nhận và lấy snapshot bot |
| `herochess.bot.search` | Histogram giây, sinh/chọn action legacy; không có trần cứng |
| `herochess.bot.command` | Histogram giây, gọi command → return, gồm gate/DB/broadcast/settlement |
| `herochess.bot.total` | Histogram giây, enqueue → kết thúc job; phân theo `outcome` |
| `herochess.bot.turns` | Counter `outcome=success/stale/expired/cancelled/failed/no_action/duplicate` |
| `herochess.match.db_commit` | Histogram giây, riêng transaction commit của command thường, gồm cả người chơi |

Outcome failed là job không hoàn tất bình thường, không khẳng định transaction chưa commit (lỗi broadcast có thể xảy ra sau commit). Queue full có thể được maintenance phục hồi; không lấy p95 của job thành công để che tỷ lệ queue-full/timeout. Tên trên Grafana có thể được chuyển dấu chấm thành gạch dưới và thêm hậu tố đơn vị; kiểm tra Explore trước khi dựng query.

Dashboard cần các panel: p50/p95/p99 total, search, command; waiting/in-flight; accepted/full; success/expired/failed; HTTP duration; process RSS/heap/GC; Npgsql pool. Dữ liệu host RAM/disk/CPU credits cần nguồn host/Azure riêng, chưa có trong Meter của API. Không gắn matchId/playerId/token vào label. Không có sample phải hiển thị No data.

Giới hạn queue mặc định 128 job, cấu hình `MatchRuntime__BotQueueCapacity` trong khoảng 1..4096; một consumer. `Schedule` không chờ channel, giữ dedup cho job in-flight và gỡ dedup khi channel từ chối để maintenance retry được. Đây chưa phải admission limit số trận đang chơi.

## Chạy kiểm tra trên PostgreSQL disposable

Không đặt `HERO_CHESS_TEST_DB` vào Supabase thật: test bootstrap tạo fixture và có thể hủy trận dở. Tạo container mới riêng (PowerShell):

```powershell
docker run -d --name hc-bot-plan-test --rm -e POSTGRES_PASSWORD=local-bot-test-only -e POSTGRES_DB=hero_chess_test -p 127.0.0.1:55439:5432 postgres:18-alpine
$env:HERO_CHESS_TEST_DB = 'Host=127.0.0.1;Port=55439;Database=hero_chess_test;Username=postgres;Password=local-bot-test-only'
$env:Logging__LogLevel__Default = 'Warning'
dotnet test tests/HeroChess.IntegrationTests --filter 'FullyQualifiedName~BotQueueTests|FullyQualifiedName~Legacy_bot_baseline' --logger 'console;verbosity=normal' -m:1
```

Harness chạy 30 lượt bot, dùng undo để lặp lại thế khởi đầu và in `BASELINE ... n=30 p50_ms=... p95_ms=...`. Đây là test assisted trên DB disposable, không đưa vào thống kê sức mạnh bot. Client polling và timestamp khác máy không được dùng làm duration BE. Để đo nhiều người dùng cần harness tải riêng có lượt đến phân tán/burst và tính cả job không được nhận.

Kết quả ngày 2026-11-03 trên máy Windows hiện tại + Docker PostgreSQL local, 1 trận, 30 thế mở đầu lặp: queue p95 1.67 ms; state-read 63.36 ms; search 46.96 ms; command 209.50 ms; total 303.41 ms. DB-commit command thường n=90, p95 20.09 ms. Không suy ra công suất/latency Azure từ số này.

Đã sửa fixture `Legacy_hero_command_retry_is_idempotent_but_new_hero_commands_are_blocked`: không đổi `kind` của sequence 0 khỏi `start` nữa (vi phạm ràng buộc DB `match_action_check`). Fixture chỉ đổi `command_id`/`actor_side`/`request_payload`; command id trùng vẫn được nhận diện là retry nên test vẫn kiểm đúng hành vi idempotent. Không nới ràng buộc production.

Đã sửa `BotWorkQueue.Stop()`: trước đây đóng channel vĩnh viễn khiến `BotTurnScheduler` singleton không đọc được nữa sau khi worker khởi động lại, làm mất lượt bot của các test chạy sau. `Stop()` nay chỉ dọn hàng đợi đang chờ.

Nguồn: [Grafana Cloud OTLP](https://grafana.com/docs/grafana-cloud/send-data/otlp/send-data-otlp/), [OpenTelemetry .NET exporter](https://opentelemetry.io/docs/languages/dotnet/exporters/).
