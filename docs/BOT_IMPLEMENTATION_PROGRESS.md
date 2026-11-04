# Tiến độ — PLAN_Bot_Analytics_Observability_2026-11-03.md

2026-11-03. Nhánh `codex/bot-analytics-observability`. Người dùng chốt Grafana Cloud. Không MSS/service game riêng. Chưa deploy hoặc push.

- Đợt 0, phần code đo: đã thêm Meter, bounded queue, thời gian queue/read/search/command/total và commit; kiểm tra deadline trước search. Thuật toán chọn nước vẫn legacy.
- Đợt 3, phần exporter: đã nối OpenTelemetry HTTP metrics vào Grafana Cloud bằng env, mặc định disabled; chưa có endpoint/token của stack để xác nhận ingest từ xa. Xem BOT_METRICS_GRAFANA.md.
- Đợt 0, baseline local: 30 lượt assisted, p95 total 303.41 ms, search 46.96 ms. Không đại diện tải 100 người/Azure/Supabase thật. Rules baseline: 364 test pass. Queue/metrics và harness: 5 test pass. Regression MatchFlow trước harness: 26 pass, 1 lỗi fixture cũ vi phạm constraint sequence 0/start.
- Xác minh cuối: 8 test queue/metrics + bot/undo + maintenance recovery + post-commit cancellation pass sau cấu hình exporter cuối; build không lỗi, `git diff --check` sạch. Chưa kiểm ingest trên Grafana stack thật hoặc exporter outage 10 phút.
- Sau chỉnh ưu tiên của người dùng: đã triển khai hợp đồng difficulty, frozen config có version, worker chạy 3 cấp, shared transition move/skill/lifecycle/action limit, metadata ticket/selection/state/ACK/history, hủy nội bộ khi config không hỗ trợ. Request cũ giữ legacy; bot mới không gắn Elo giả. Xem BOT_API.md.
- Kiểm thử bot/API cuối: 17 test pass, gồm regression round-robin slot sau fix review; full BE regression trước fix slot: 86 pass (loại fixture legacy sai constraint đã ghi ở trên). Reviewer độc lập không tìm thêm blocker sau fix starvation; còn giới hạn time budget mềm cần đo trên Azure. `git diff --check` sạch.
- Chưa thực hiện: báo cáo sức mạnh; dashboard trên tài khoản Grafana; admission/maintenance gate; baseline thật và load test 100 trận. Ngân sách 50/100/200 ms của bot v1 là soft, không bảo đảm CPU cứng.

Ruling: làm phần đo trước để tuân thủ gate baseline đã được review; không tự chọn ngân sách hard trên VM khi chưa có phép đo tại VM. Điều này chưa hoàn thành toàn bộ plan.
Ruling mới theo chỉ đạo người dùng: ưu tiên bot trước Grafana. Dùng profile v1 nhỏ, có node limit và soft time budget để triển khai/test được; chưa bật Grafana trên VM hoặc khẳng định công suất. Quyết định này thay thế việc chờ Grafana/Azure trước khi viết bot.
Ruling: giữ checkout hiện tại trên nhánh feature vì sandbox ban đầu chặn tạo ref; tạo nhánh qua approval đã thành công. Không thay đổi/commit các tài liệu khác của người dùng.
Ruling: Docker/testhost/restore cần quyền ngoài sandbox để chạy named pipe và tải NuGet; đã chạy PostgreSQL riêng localhost:55439, không dùng credentials Supabase. Chỉ container hc-bot-plan-test được dùng cho lượt này.
