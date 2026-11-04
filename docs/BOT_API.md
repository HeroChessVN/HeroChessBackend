# Bot 3 cấp — hợp đồng BE

Tạo trận qua endpoint hiện có, dùng bearer token của Player:

```http
POST /api/v1/matchmaking/tickets
Content-Type: application/json

{"mode":"bot","difficulty":"hard"}
```

`difficulty`: `easy`, `medium`, `hard` (bỏ qua khoảng trắng đầu/cuối và hoa/thường). Giá trị khác/rỗng hoặc gửi difficulty cho mode ranked trả 400 `INVALID_BOT_DIFFICULTY`. Request cũ `{"mode":"bot"}` tiếp tục dùng bot legacy `capture_sp_v1`; không tự đổi độ khó của client cũ. FE có thể đặt ba lựa chọn cố định với ba mã này; không cần bot account/id mới.

Ticket trả thêm:

```json
{"bot":{"difficulty":"hard","strategyVersion":"tactical-v1","searchProfileVersion":"bounded-v1"}}
```

Đây là một phần của response, không thay thế các trường ticketId/matchId/status hiện có. Dữ liệu tương tự có tại `state.bot`, `state.sides[].bot` của phe bot, `selection.sides[].bot`, `ACK.snapshot.bot`, lịch sử list/detail của phe bot. Bot legacy trả bot null. Config thật được lưu trong `match_participant.bot_config` JSONB, không cần migration; không nhận budget/strategy do FE gửi. Bot mới không tự nhận Elo 1000: chưa hiệu chuẩn nên Elo của bot để null.

## Cách đánh phiên bản đầu

| Cấp | Cách chọn | Trần tìm kiếm có chủ đích |
| --- | --- | --- |
| easy | Chọn ngẫu nhiên có seed từ nước hợp lệ và một số Command skill hợp lệ | 32 lần thử skill, 50 ms soft budget |
| medium | Chấm điểm sau một nước: SP quân còn sống, tiến quân Tốt, chiếu Tướng | beam 12 nước + tối đa 8 target skill; 64 node, 100 ms soft budget |
| hard | Xét thêm phản ứng mạnh của đối thủ trong tập nước được chọn; nếu chưa hoàn tất thì dùng kết quả một nước | beam 8 nước + tối đa 8 target skill mỗi vị trí; 256 node, 200 ms soft budget |

**Time budget là mềm:** một lần sinh nước/mô phỏng/kết thúc lượt chưa có cancellation bên trong, nên thực tế có thể vượt mốc trên. Node limit và cutoff được kiểm giữa các lần mô phỏng. Không có tác vụ search nền bị bỏ mặc; quá budget vẫn gửi fallback hợp lệ qua cùng command pipeline. Không cam kết hard có Elo cố định hoặc luôn thắng medium. Có test chiến thuật quân mồi cho thấy tìm phản ứng giúp hard tránh nước medium chọn.

Các Command skill được xét luân phiên slot để các target của Thành/Rào không che skill khác. Search dùng cùng phần advance skill, turn lifecycle, cooldown và action-limit với command thật; tập candidate giới hạn của bot không được dùng để tuyên bố hết nước/checkmate. Search không sửa state gốc. Engine mới chỉ dùng state trận mới không có Hero trait/hidden cọc cũ; state không hỗ trợ hoặc config hỏng được worker xử lý qua hủy trận nội bộ, không biến thành người chơi thua.

Trận bot giữ chính sách không cộng Elo/coin. Bot luôn cầm đen, dùng bản sao đội hình người chơi như trước. Hero skill/trait không được bật lại; chỉ Command skill đang có trong lineup được thực thi. Chưa sửa Unity hoặc UI web.

## Kiểm thử và triển khai

Test thuần: `dotnet test tests/HeroChess.IntegrationTests --filter FullyQualifiedName~BotDifficultyTests -m:1` (không cần DB cho nhóm này). Test HTTP/worker cần `HERO_CHESS_TEST_DB` trỏ PostgreSQL disposable, xem BOT_METRICS_GRAFANA.md về cách tạo DB local; không chạy fixture lên Supabase thật.

Nhóm test bao phủ chuẩn hóa config, config lạ, state không mutate, quân mồi, cứu bằng Khiên, cooldown frozen, action limit, cancellation, phân bổ target theo slot; integration bao phủ 3 cấp đi qua pipeline, metadata ticket/state/ACK/history, validation không kẹt reservation và cancel nội bộ idempotent.

Chưa có số đo tải 100 trận trên Azure 1 GiB/Supabase. Deploy/restart hiện vẫn hủy trận dở qua startup recovery: cần khung bảo trì/drain trước khi rollout hoặc rollback. Không scale ngang API do lock/ticket/queue đang nằm trong RAM.
