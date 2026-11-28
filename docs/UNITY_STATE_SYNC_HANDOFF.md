# Đồng bộ trận qua WebSocket và polling dự phòng

Handoff cho nhóm Unity để giảm request đọc bàn cờ trên VM 1 GB. Dùng WebSocket và API hiện có; không đổi luật, format command hoặc yêu cầu phiên bản snapshot mới. Đây là yêu cầu triển khai, source Unity chưa được sửa trong công việc BE.

## Các điểm cần sửa trong Unity

- `Assets/HeroChess/Scripts/Runtime/Backend/HeroChessOnlineDemo.cs`: `PollLoop`, `SocketLoop`, `ConnectMatchSocket`, `ReceiveMatchChanges`, `Update`, `RefreshBoard`, `AcceptSnapshot` và luồng lấy legal actions.
- `HeroChessOnlineDemo.Connection.cs`: trạng thái kết nối phải phù hợp nhịp polling mới, không hiện mất mạng chỉ vì không có nước đi.
- `HeroChessApi.cs` chỉ sửa nếu cần xử lý Retry-After/cancellation; giữ cách gửi command và token hiện tại.

Chỉ giảm polling của bàn cờ đang chơi. Không sửa heartbeat custom room, logout, selection và matchmaking trong cùng thay đổi này.

## Luồng vào trận

1. Đọc state ban đầu nếu cần để mở bàn cờ; không chạy hai yêu cầu vào trận trùng nhau.
2. `POST /api/v1/ws-ticket`, nhận vé dùng một lần.
3. Mở `wss://<api-host>/ws/v1?ticket=<ticket>`; localhost dùng `ws` nếu API dùng HTTP.
4. Gửi `match.subscribe` với `payload.matchId`.
5. Nhận `match.state`, áp dụng `payload` bằng luồng `AcceptSnapshot` trên main thread. Snapshot này đã được BE lọc theo người xem.
6. Sau subscription thành công, không GET lại chỉ để lấy đúng snapshot vừa nhận. Bật GET dự phòng khoảng 10 giây, thêm jitter 0–20%.

`WebSocketState.Open` chưa chứng minh đã subscribe thành công. Khi kết nối lại phải xin ticket mới và subscribe lại, không tái sử dụng vé đã tiêu thụ.

Nếu khoảng 10 giây chưa nhận snapshot subscription, đóng socket và thử lại có backoff; REST fallback vẫn tiếp tục. Message sai JSON hoặc sai match/account không được cập nhật màn hiện tại.

## Xử lý từng message

| Message | Xử lý |
|---|---|
| `match.state` | Áp dụng snapshot trong payload; không bỏ payload rồi GET lại |
| `match.changed` | Đọc `matchId`, `version`, `skillUsed`; hiển thị thông báo một lần và đánh dấu cần refresh |
| `match.command_accepted` | Áp dụng `payload.snapshot` nếu có; chỉ refresh thêm khi có thông báo mới hơn |
| `match.command_rejected` | Giữ xử lý mã lỗi; stale cần resync, không tạo command ID mới để lặp lại nước chưa rõ kết quả |
| `match.ended` | Đọc state/result chính thức một lần để kết thúc UI |
| `match.settled` | Refresh result/profile theo nhu cầu UI, kể cả khi version bàn cờ không đổi |

Không dựng state từ `match.changed`: message này không chứa toàn bộ bàn cờ. Không tự giảm timer CD/effect theo số lần GET hoặc số message nhận được.

## Gộp refresh và tránh race

- Chỉ một GET `/state` đang chạy trên màn trận. Command và API khác vẫn theo cơ chế hiện có.
- Lưu version cao nhất đã được báo và cờ cần cập nhật metadata. Event đến khi GET đang chạy không được mất.
- Sau GET, nếu state vẫn thấp hơn version cần lấy thì thực hiện một lượt nối tiếp có giới hạn/backoff. Không tự quay vòng liên tục khi server chưa trả bản mới.
- ACK có snapshot mới có thể đáp ứng yêu cầu đang chờ; không GET lại chỉ vì vừa nhận ACK.
- ACK hiện có thể thiếu `sides`/hồ sơ so với GET state. Giữ metadata hiển thị đã biết của cùng trận nếu trường tùy chọn bị thiếu; nếu chưa có thì refresh. Không gộp quân/effect cũ vào bàn cờ mới và không dùng metadata cũ để thay kết quả settlement.
- Response/socket message phải khớp tài khoản, match ID và generation của màn. Hủy việc của màn cũ khi đổi trận/logout.
- Bỏ version cũ hơn. Version bằng nhau vẫn có thể mang settlement, status hoặc `serverNow` mới hơn; không so version đơn thuần rồi bỏ mọi cập nhật.
- Skill banner chống lặp theo match/version. Giữ animation và delayed snapshot hiện có để không làm quân nhảy sai vị trí.
- Legal actions chỉ tải lại khi match/version/lượt hoặc trạng thái review thay đổi, không tải lại do heartbeat làm đổi `serverNow`.
- Sau Undo, xóa lựa chọn cũ và làm mới legal/forecast theo version mới.

## Fallback và đồng hồ

- Socket khỏe, đã subscribe: GET dự phòng khoảng 10 giây. Sự kiện vẫn cập nhật ngay.
- Socket không có hoặc không được nền tảng hỗ trợ: poll khoảng 2 giây khi thành công.
- Lỗi GET: backoff 2/4/8/15 giây, có jitter; nếu server trả Retry-After thì tôn trọng giá trị đó.
- Giữ thời điểm được thử lại trong bộ gộp refresh; heartbeat hoặc bấm Đồng bộ không được vượt qua Retry-After của một request trước đó.
- Hủy request do rời màn/logout không phải lỗi kết nối cần hiển thị lên màn mới.
- Không dùng một vòng retry socket cố định 1,5 giây cho mọi client khi server vừa restart; giữ backoff tới khoảng 30 giây và thêm jitter.
- Kết nối lại thành công: resubscribe và nhận snapshot; quay lại nhịp dự phòng.
- Đồng hồ đếm theo `serverNow` và `turnDeadlineAt`, không cần GET mỗi giây. Server vẫn quyết định timeout.
- Lam Sơn đang review: hết giờ, chuyển lượt và kết quả vẫn phải được nhận qua event hoặc fallback. Không kéo dài deadline do reconnect.
- Sau terminal state: cập nhật settlement có giới hạn rồi dừng polling trận đang chơi. Khi người chơi mở lại kết quả vẫn có thể refresh một lần.
- Không coi socket im lặng là mất mạng: một người có thể suy nghĩ gần hết 90 giây. GET dự phòng và trạng thái transport giúp kiểm tra kết nối.

## Giới hạn bảo vệ server

- Account dùng chung quota đọc trận: burst 12, nạp 4/giây, tính cả `match.subscribe`. Không chia timer độc lập cho mỗi màn rồi cộng dồn request.
- Mỗi account tối đa 2 socket. Đóng socket cũ khi đổi màn/logout; khi mất mạng có thể dùng slot thứ hai để reconnect. Socket chết được dọn bằng ping/pong chuẩn (20 giây interval, 20 giây timeout); thư viện native phải trả pong tự động.
- `POST /ws-ticket`: burst 4, nạp 1 mỗi 5 giây; tối đa 4 vé chưa dùng/account. Không xin ticket theo mỗi heartbeat. Vé đã thử handshake không tái sử dụng.
- HTTP `429`: đọc `Retry-After` (giây), kết hợp backoff/jitter. Nếu thư viện không đọc được header của handshake thất bại, dùng backoff reconnect và xin vé mới; REST fallback vẫn phải tôn trọng quota.
- Sau upgrade, `match.command_rejected` có thể mang `RATE_LIMITED`/`SERVER_BUSY`/`SUBSCRIPTION_LIMIT` với `details.retryAfterSeconds`. Chờ trước khi thử lại; không coi đây là nước đi đã thành công hoặc tự chuyển lượt.
- Một socket tối đa 8 subscription; gửi lại cùng match không chiếm thêm. Với kết nối dùng lâu qua nhiều trận, mở lại socket trước subscription thứ 9.
- Mặc định auth theo IP có burst 12, nạp 2/giây. Nhiều máy dùng chung Wi-Fi có thể phải đợi khi đăng nhập đồng thời; hiển thị thời gian retry thay vì gửi liên tục.

Các thay đổi này chỉ ở BE và tài liệu. Nhóm Unity cần áp dụng xử lý retry/đồng bộ trong client; không có source Unity được chỉnh trong đợt này.

## Prompt giao cho nhóm Unity

> Tối ưu đồng bộ bàn cờ HeroChess theo tài liệu này, dùng WebSocket hiện có làm đường thông báo chính. Áp dụng snapshot của match.state và command ACK, chỉ giữ polling dự phòng khoảng 10 giây khi socket đã subscribe; fallback khoảng 2 giây và backoff khi lỗi. Gộp request, không mất event đến giữa lúc GET, không nhận response của trận/account cũ. Giữ đúng bẫy ẩn, Lam Sơn, Undo, animation, timeout và settlement cùng version. Không thay luật hoặc API contract, không sửa heartbeat custom room/logout/selection. Báo lại các file thay đổi và kiểm chứng mất mạng/reconnect/event trùng, rồi xác nhận nhịp GET đã giảm bằng Network/log đo đạc.

## Kiểm chứng trước khi bàn giao

1. Hai người chơi PvP nhận nước đi ngay, không phải chờ heartbeat 10 giây.
2. Lượt bot, active Hero Skill, Khiên, bẫy Trần và Lam Sơn hoạt động như trước.
3. Ngắt socket nhưng REST còn hoạt động: vẫn cập nhật; nối lại không nhân đôi nước hoặc banner.
4. Event tới trong lúc GET đang chờ: cuối cùng nhận được version mới nhất, không bị đứng bàn cờ.
5. Undo hoặc đổi trận khi request đang chạy: phản hồi cũ không ghi đè màn mới.
6. Kết thúc trận rồi settlement tới sau: Coin/Elo/UI kết quả cập nhật được dù version không tăng.
   Snapshot ACK thiếu hồ sơ không làm mất tên/avatar trên HUD.
7. Không có nước đi trong 30 giây với socket khỏe: chỉ khoảng 3 GET dự phòng/client thay vì khoảng 25; không tính request vào trận ban đầu, command và sự kiện thật.
8. Clock, timeout và modal mất mạng vẫn đúng; rời phòng/logout giữ nguyên nghiệp vụ.

Không giảm tần suất bằng cách bỏ đồng bộ bắt buộc hoặc bỏ kiểm tra server. Chỉ deploy BE không tự sửa nhịp polling của bản Unity đã phát hành.
