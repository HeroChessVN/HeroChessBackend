# Plan sửa Lam Sơn Quân Lược

Trạng thái: phần BE đã triển khai local theo kế hoạch này. Chưa sửa Unity, commit, push hoặc deploy. Không cần migration DB.

Kiểm chứng ngày 14/11/2026: 392 test Rules và 45 test bot/API liên quan pass. Test API chạy trên PostgreSQL Docker tạm, không dùng Supabase. Bao gồm chuyển lượt/undo trong lúc tính, undo nhiều tầng trên cache hit và miss, quyền riêng tư, request trùng, cancellation, tham số lỗi, snapshot lỗi và rate limit. Chưa đo tải trên Azure 1 GB hoặc kiểm chứng giao diện Unity.

## 1. Mục tiêu và luật giữ nguyên

- Người dùng Lam Sơn nhận một chuỗi tối đa ba hành động: địch → mình → địch, cùng tối đa hai phương án nguy hiểm của địch ở bước đầu.
- Đây là gợi ý chiến thuật theo thế cờ lúc kích hoạt, không phải biết chắc đối phương sẽ đi đâu.
- Dùng skill vẫn tốn một hành động, chuyển lượt và tính cooldown theo snapshot trận. Không hoàn lượt, không dùng lại skill khi tải lại dự đoán.
- Giữ điều kiện đội hình và tối đa một faction skill trong ba slot. Không đổi CD, ngưỡng phe, DB hoặc Unity trong phần triển khai BE này.

## 2. Nguyên nhân lỗi đang có

Trước khi sửa, `MatchReadService.ForecastAsync` đọc `MatchAction.StateAfter` của lần kích hoạt nhưng vẫn đòi version hiện tại bằng sequence kích hoạt. Đối thủ đi nhanh làm GET trả `409 STALE_STATE`, dù snapshot cần tính vẫn còn đầy đủ. BE đã bỏ điều kiện này.

Unity cũng có ba điểm loại kết quả cũ: điều kiện trước khi gọi `ReadSkillForecast`, điều kiện khi nhận phản hồi, và xóa `forecast` khi nhận snapshot khác version. Chỉ sửa BE sẽ chưa khắc phục hết việc không hiện kết quả.

Trước khi sửa, `BotEngine.Forecast` chọn tham lam tối đa sáu ứng viên hợp lệ mỗi bước trong ngân sách mềm 50 ms, chưa đánh giá ăn lại. BE đã dùng chung đánh giá đổi quân ngắn hạn với bot khó, dành chỗ cho nước phòng thủ và thêm trần 64 lần thử mô phỏng. Không được mô tả nó là minimax sâu hoặc tiên đoán hành vi cá nhân.

## 3. Giai đoạn một sửa độ tin cậy của API

### Nguồn dữ liệu và quyền truy cập

1. Giữ route `GET /api/v1/matches/{matchId}/forecast?version={activationSequence}` và xác thực tài khoản hiện tại.
2. Kiểm tra người gọi có participant trong trận, action ở sequence đó thuộc phe người gọi, là `team_skill`, và có sự kiện `team_skill.activated` với code `lam_son_quan_luoc`.
3. Bỏ điều kiện **bàn hiện tại phải bằng version kích hoạt**. Mô phỏng từ bản sao `action.StateAfter`, dùng ruleset và lineup đã đóng băng của trận; không đọc catalog mới để thay luật trận.
4. Nâng cấp schema snapshot qua đường nâng cấp hiện có trước khi mô phỏng. Dữ liệu lịch sử lỗi phải trả lỗi có kiểm soát, không ghi đè snapshot gốc.
5. Lọc bẫy ẩn của đối thủ trước khi sinh ứng viên, chấm điểm và tạo kết quả. Chỉ trả những thông tin người dùng skill được biết. Không gửi snapshot đầy đủ có bí mật trong response.

### Undo và cache

- Xét nhánh lịch sử hiện hành trước khi trả kết quả: action kích hoạt đã bị undo loại khỏi nhánh thì không được dùng để yêu cầu tính mới hoặc lấy cache. Có thể đi ngược lịch sử theo các sự kiện `match.undone.targetSequence`; không chỉ so sequence lớn nhỏ. Đặt giới hạn truy vấn hợp lý, kiểm tra trường hợp undo nhiều lần rồi đi lại.
- Kiểm tra quyền/action/nhánh trước đường trả cache để cache không bỏ qua kiểm tra undo. Cache vẫn dùng `(playerId, matchId, activationSequence)`, TTL hai phút, tối đa 128 mục như hiện tại.
- Hai phút là thời hạn cache, không phải thời hạn tự động thu hồi skill. Sau khi hết cache, yêu cầu hợp lệ có thể tính lại từ cùng snapshot.
- Giữ một suất tính và hàng chờ bốn. Hợp nhất yêu cầu trùng cùng khóa để không tính lặp; không giữ khóa command của trận trong lúc search.
- Hàng chờ đầy trả `429 FORECAST_BUSY`. Dùng rate limiter nền tảng giới hạn 30 yêu cầu/phút/người, vượt giới hạn trả `429 FORECAST_RATE_LIMITED`. Global concurrency limiter không thay thế giới hạn request của một người.
- Client ngắt kết nối thì dừng công việc qua CancellationToken; không ghi cache kết quả tính dở do hủy yêu cầu.

### Hợp đồng giữ tương thích

```json
{
  "version": 42,
  "prediction": {
    "variation": [],
    "threats": []
  }
}
```

- `version` tiếp tục là sequence của **lần kích hoạt**, không đổi sang version bàn hiện tại. GET `version=42` phải trả `version=42` kể cả bàn đã lên 43 hoặc 44.
- Mỗi mục giữ `side`, `action`, `from`, `pieceClass`, `pieceName`, `skillName`. Trường không áp dụng có thể null.
- `variation` dài 0–3 và `threats` dài 0–2; không bịa cho đủ số lượng. Ghi “tối đa” trong mô tả. Mỗi bước sau được tính từ kết quả giả lập của bước trước.
- Bàn thay đổi thông thường không còn gây `409 STALE_STATE` tại endpoint này. Sai participant/action/code/nhánh dùng `404 FORECAST_NOT_FOUND` hoặc `MATCH_NOT_FOUND` theo quy ước che quyền truy cập hiện có.
- Không thay đổi quy tắc `409 STALE_STATE` của endpoint gửi command: command vẫn cần expectedVersion hiện tại.
- Nếu trận kết thúc nhưng action kích hoạt còn hợp lệ trên nhánh, vẫn cho chính người đã dùng xem lại dự đoán lịch sử. Không cho người còn lại xem vì trận đã kết thúc.

## 4. Giai đoạn hai cải thiện chất lượng dự đoán

Chỉ thực hiện sau khi giai đoạn một ổn định; không tăng độ sâu hoặc thời gian một cách mặc định.

1. Giữ giới hạn ba hành động và hai nguy cơ. Lấy thêm sự đa dạng trong sáu ứng viên: nước ăn, nước cứu quân bị dọa, nước giải chiếu và kỹ năng hợp lệ. Không để bốn nước ăn đầu lấp hết phương án phòng thủ.
2. Với từng ứng viên, chấm thế cờ sau hành động và phạt tổn thất do phản ứng ăn lại hợp lệ. Tái sử dụng logic đánh giá đổi quân của bot khó ở nơi có thể dùng chung; không viết engine thứ hai hoặc gọi search bot khó đầy đủ ba lần.
3. Mỗi ply tối ưu cho bên đang đi tại ply đó. Phương án nguy hiểm vẫn là hành động hợp lệ của địch ở bước đầu có ăn quân, chiếu hoặc thắng; không cam kết luôn có hai phương án.
4. Ngân sách thời gian và node dùng chung cho toàn bộ lượt dự đoán, bao gồm bước kiểm tra ăn lại. Kiểm tra cancellation ở các vòng tính. Không coi 50 ms là deadline cứng vì một lần sinh nước có thể vượt mức đó.
5. Không dùng thông tin ẩn để chọn một nước hoặc gắn nhãn nguy hiểm. Không thay đổi state thật, cooldown, lượt hay kết quả khi chỉ đọc dự đoán.
6. Bổ sung test thế Pháo ăn Mã rồi bị Xe bắt lại; có phương án phòng thủ tốt hơn thì không ưu tiên nước lỗ chỉ vì điểm ăn trước mắt cao.

## 5. Kiểm chứng bắt buộc trước deploy

### API

- Kích hoạt ở 42, đối thủ đi lên 43 trước GET: trả 200 và `version=42`.
- Đối thủ đi trong lúc đang tính: kết quả vẫn dựa trên snapshot 42; không hủy vì version mới.
- Cache hit và cache miss đều kiểm tra participant và quyền kích hoạt; người còn lại/người ngoài không lấy được.
- Sai sequence, sequence của skill khác, thiếu tham số hoặc tham số không hợp lệ không khởi chạy search vô ích.
- Undo loại action kích hoạt, undo nhiều tầng, và đi tiếp ở nhánh mới: cache cũ không vượt qua kiểm tra nhánh.
- Request lặp không tiêu lượt/cooldown lần nữa. Hàng chờ đầy, cancellation và hết cache được xử lý đúng.
- Bẫy ẩn không xuất hiện trực tiếp hoặc làm thay đổi lựa chọn dựa trên thông tin bí mật; snapshot DB không bị sửa bởi GET.

### Thuật toán và tài nguyên

- Các bước trong chuỗi đều mô phỏng được nối tiếp từ snapshot gốc.
- Không trùng mục nguy cơ; 0–2 mục khi thế cờ không có đủ đe dọa.
- Ngắt budget vẫn trả danh sách hợp lệ, không sinh hành động không được kiểm tra.
- Đo số lần tính, cache hit, thời gian chờ/search và ảnh hưởng đến hàng chờ bot. Không ghi ID người/trận vào nhãn metrics.
- Chạy test Rules và nhóm bot/forecast; test DB dùng cơ sở dữ liệu tạm, không test ghi trên production.

## 6. File triển khai

- `HeroChess.Api/Services/MatchReadService.cs`: quyền đọc, nhánh undo, snapshot, cache và limiter.
- `HeroChess.Api/Services/BotEngine.cs`: đánh giá đổi quân và tập ứng viên forecast.
- `HeroChess.Api/Services/BotTelemetry.cs`: counter tính/cache và histogram queue/search.
- `HeroChess.Api/Controllers/MatchesController.cs`: kiểm tra query bắt buộc và số nguyên dương.
- `tests/HeroChess.IntegrationTests/ForecastApiTests.cs`, `ForecastAlgorithmTests.cs`: test race, phân quyền, undo, cancellation, rate limit và chất lượng; giữ các regression bot hiện có.
- `docs/api.md`: ý nghĩa version, giới hạn và lỗi của route forecast.

Deploy BE trước, xác nhận GET lịch sử trả được, rồi bên Unity áp dụng `UNITY_LAM_SON_HANDOFF_PROMPT.md`. Chưa tự commit, push hoặc deploy. Giới hạn xác minh nhánh là 256 undo; dữ liệu lỗi/vượt giới hạn trả 422 có kiểm soát, không trả cache chưa xác minh.

## 7. Luật bẫy Trần vừa được chốt riêng

- Đây là phần sửa luật đã được yêu cầu thực hiện, tách với plan Lam Sơn.
- Mọi quân dừng tại bẫy địch còn hiệu lực đầu tiên trên đường đi; Mã xét chân rồi ô đáp. Không ăn quân hoặc phá công trình ở phía sau bẫy.
- Ô bẫy đang có quân hoặc công trình chặn đứng trên đó thì bẫy tạm không kích hoạt; vẫn tồn tại đến hết hạn, có thể kích hoạt sau khi ô trống. Không xếp chồng hai quân.
- Bẫy chỉ bị tiêu khi thực sự kích hoạt. Bẫy mình và bẫy hết hạn không chặn quân. Khiên không miễn khóa chân.
- Giữ bốn lượt khóa sau lifecycle, sáu lượt tồn tại và CD theo cấu hình cũ; không sửa các số này.
- Nếu dừng sớm làm Tướng mình bị chiếu hoặc hai Tướng đối mặt: tính thua ngay với `endReason=river_snare`, theo quyết định người dùng.
- Bẫy vẫn ẩn trước khi kích hoạt: legal preview không trả sẵn tọa độ chặn. Khi commit, state và sự kiện `piece.moved.to` dùng tọa độ thực tế.
