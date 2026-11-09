# Astra review và kế hoạch sửa BE — 20/10/2026

Trạng thái (cập nhật 20/10): đã sửa R01–R04 và C01–C03 trong source BE. Build và test không cần DB đã qua; HTTP/WebSocket và transaction regression mới chưa chạy vì `HERO_CHESS_TEST_DB` chưa được cấu hình, Docker daemon cũng chưa chạy. Chưa deploy. Review do `gpt-6-astra` thực hiện trên thay đổi BE ẩn Hero skill và performance, sau đó rà thêm các đường Command skill, HTTP/WebSocket, lịch sử và khóa trận. Unity và các thay đổi frontend có sẵn nằm ngoài phạm vi.

Có 4 lỗi trên luồng hiện dùng. R03 là regression của thay đổi mới; R01, R02, R04 đã tồn tại trước đó. Chưa xác định được lỗi vượt quyền mới. Kết luận này không thay thế kiểm thử tích hợp PostgreSQL hoặc kiểm tra hệ thống đã deploy.

## 1. Các lỗi cần sửa trước

### R01 — P2: Match ID bị từ chối vẫn làm tăng RAM của registry khóa

Vị trí: `HeroChess.Api/Services/MatchLockRegistry.cs:11`; `MatchCommandService.cs:30`. Các đường WebSocket subscribe, chọn đội, confirm và hủy selection cũng lấy gate trước khi kiểm tra participant.

`For(matchId)` thêm mọi ID vào `ConcurrentDictionary` singleton và không thu hồi. Một player đã đăng nhập có thể gửi nhiều UUID ngẫu nhiên: request trả 404 nhưng mỗi ID để lại một gate trong RAM. Đây là đường tăng bộ nhớ không giới hạn; cấu hình API được rà chưa có rate limiter cho REST. Chưa chạy thử gây cạn RAM.

Kế hoạch sửa:

- Dùng một pool gate có kích thước cố định, ánh xạ match ID vào cùng một gate bằng hash. Giữ nguyên FIFO, cancellation và `Release` của gate hiện có.
- Ghi rõ giới hạn bằng comment theo AGENTS.md: hai match trùng bucket sẽ chờ nhau; đo mức tranh chấp ở tải mục tiêu trước khi chọn số bucket cuối cùng.
- Không xóa dictionary entry đơn giản sau `Release`: caller còn giữ gate cũ có thể chạy đồng thời với gate mới của cùng trận.
- Không coi rate limit là bản sửa thay thế cho cấu trúc bộ nhớ không giới hạn.

Nghiệm thu: nhiều ID không hợp lệ không làm storage tăng theo số ID; các lệnh cùng match vẫn tuần tự; hủy waiter không làm kẹt gate; hai command cùng version vẫn chỉ một được ghi. Đo chờ gate khi nhiều match hoạt động để kiểm soát ảnh hưởng do trùng bucket.

### R02 — P2: `team_skill.slot` sai kiểu làm REST trả 500

Vị trí: `HeroChess.Api/Services/MatchCommandService.cs:140`, hàm validation chung ở dòng 323.

Code gọi `TryGetInt32` mà chưa kiểm tra `ValueKind`. Giá trị như `"slot":"1"`, `null`, object hoặc array ném `InvalidOperationException`. Player hợp lệ gửi command tới trận active có thể gặp REST 500; WS lại chuyển cùng lỗi thành `MALFORMED_MESSAGE`. Astra đã tái hiện exception của JsonElement bằng lời gọi chỉ đọc, chưa gửi request tích hợp tới DB.

Kế hoạch sửa:

- Kiểm tra `slot` tồn tại, là JSON Number và đọc được số nguyên trong `ValidateAction` dùng chung.
- Trả `400 INVALID_ACTION` khi shape sai. Slot số nguyên không có trong đội hình tiếp tục dùng lỗi nghiệp vụ hiện có.
- Giữ validation trước khi áp dụng lifecycle/skill; không sửa riêng ở controller HTTP.

Nghiệm thu: missing/string/null/object/array/fractional/overflow bị từ chối qua REST và WS; version, số action và số lượt dùng skill không đổi. Command hợp lệ tiếp theo vẫn chạy được.

### R03 — P2, regression mới: Retry Hero command đã commit mất duplicate ACK

Vị trí: `HeroChess.Api/Services/MatchCommandService.cs:328`; thứ tự gọi validation và tra command cũ ở dòng 39–40.

Chặn `hero_active` trong `ValidateAction` xảy ra trước tra lịch sử command. Vì vậy retry cùng ID và payload của một Hero command đã commit trước đây trả 422 thay vì `duplicate=true`. Nhánh dedup vốn hỗ trợ trả ACK lịch sử ngay cả khi trận đã completed/cancelled. Đây là lỗi tương thích retry; không có nghĩa cần bật lại Hero skill.

Kế hoạch sửa:

- Giữ kiểm tra quyền và JSON shape trước xử lý command.
- Giữ nhánh dedup cùng kiểm tra actor/payload; trả ACK lịch sử qua projection đã lọc Hero metadata.
- Đặt chính sách từ chối Hero command mới sau nhánh dedup, trước thực thi action.
- Không gọi engine Hero skill, không thêm action, không thay đổi state khi trả duplicate.

Nghiệm thu trên DB test có action legacy: retry giống hệt trả duplicate ACK sạch metadata; cùng ID nhưng khác nội dung hoặc actor trả conflict; outsider vẫn bị chặn; Hero command mới vẫn trả 422. Kiểm tra cả HTTP và WS.

### R04 — P2: Cursor lịch sử không hợp lệ làm REST trả 500

Vị trí: `HeroChess.Api/Services/MatchHistoryService.cs:23` và dòng 25.

`Convert.FromBase64String` chưa được xử lý lỗi, còn ticks được đưa thẳng vào `DateTimeOffset`. `cursor=!` ném `FormatException`; Base64 hợp lệ chứa `-1|<UUID hợp lệ>` ném `ArgumentOutOfRangeException`. Astra đã tái hiện hai exception bằng lời gọi chỉ đọc.

Kế hoạch sửa: xử lý lỗi Base64 và kiểm tra khoảng ticks trước khi dựng timestamp; dùng lỗi sẵn có `400 INVALID_CURSOR`. Giữ participant filter và thứ tự keyset hiện tại.

Nghiệm thu: Base64 hỏng, thiếu trường, UUID sai, ticks âm/vượt giới hạn đều trả 400; cursor hợp lệ vẫn phân trang đúng, không lặp/mất bản ghi trong bộ dữ liệu test cố định.

## 2. Lỗi có điều kiện: sửa trước khi bật các Command handler mở rộng

Seed trong repo chưa chứng minh Vạn Cọc/Binh Lâm/Phản Kỳ/Phá Trận được bật trên DB deploy. Vì thế các mục sau là lỗi code đã xác định nhưng chưa được coi là outage của catalog hiện tại. Chỉ đọc catalog/DB để xác nhận trạng thái khi triển khai; không tự bật skill hoặc thêm dữ liệu giả.

| Mục | Bằng chứng | Bản sửa nhỏ nhất và kiểm thử |
|---|---|---|
| C01: Không serialize được controller theo ô | `HeroChess.Rules/Effects/EffectInstance.cs:42` dùng `Dictionary<BoardPoint, Side>`; `HeroChess.Api/Infrastructure/GameJson.cs:11` chưa có converter cho key đó. Map có phần tử ném `NotSupportedException`, Astra đã tái hiện với Rules DLL hiện tại. | Định nghĩa biểu diễn JSON key có thể đọc ngược; bảo đảm mọi đường persist/projection dùng cùng cách. Test output thực của handler qua lưu JSON → đọc lại → projection, kiểm tra vị trí và phe giữ nguyên. |
| C02: Payload đổi kiểu sau lưu/đọc JSON | `EffectInstance.cs:84` dùng giá trị `object`; sau đọc JSON trở thành `JsonElement`. Cast `as string` tại `PhanKyDoatTheHandler.cs:67`, `XiangqiRulesEngine.cs:127/179` và kiểm tra GUID tại `PhaTranDoatPhongHandler.cs:68` không còn đúng. Astra đã xác nhận giá trị `terrain` đổi kiểu sau roundtrip. | Dùng dữ liệu có kiểu rõ ràng hoặc đọc cả JsonElement cho đúng các field đang dùng; sửa tất cả caller cùng field. So sánh hiệu ứng, quyền điều khiển địa hình và hành động trước/sau persist. |
| C03: Tọa độ Phản Kỳ sai kiểu ném exception | `HeroChess.Rules/Skills/RiverGeometry.cs:123/124` gọi `TryGetInt32` thiếu ValueKind guard; `position.x="4"` đã tái hiện exception. | Validate kiểu/range của tọa độ được cung cấp. Phân biệt position bị bỏ qua với position sai: không biến input sai thành nhắm toàn effect. Test chuỗi/null/object/ngoài bàn và cả trường hợp position được phép bỏ qua. |

## 3. Thứ tự triển khai và điều kiện hoàn tất

1. Sửa R01 và test tính tuần tự/cancellation; đo tranh chấp để không làm xấu độ trễ giữa các trận.
2. Sửa R02 và R04 tại validation chung tương ứng; thêm test input sai không ghi state.
3. Sửa R03 và kiểm tra duplicate lịch sử, conflict, outsider, Hero command mới bị chặn.
4. Xác nhận catalog thực tế. Nếu handler mở rộng đang bật thì nâng C01–C03 thành việc bắt buộc trước deploy; nếu chưa bật, giữ thành điều kiện trước khi bật.
5. Chạy integration trên PostgreSQL disposable qua `HERO_CHESS_TEST_DB`: `/state` một query service, outsider 404, selecting 409, active state đúng; catalog/ACK/replay không chứa Hero trait; Command skill và bot vẫn tiến triển, bot vượt budget không bỏ nước; hai lệnh cạnh tranh không ghi hai lần.
6. Chỉnh mục 1 của `PLAN_Server_Performance.md`: các câu “3 SELECT” và “PvP vẫn enqueue bot” phải ghi là trạng thái trước sửa hoặc đổi sang mô tả source hiện tại.

Giữ nguyên transaction, row-lock order, participant/account checks, deadline và version trong các bản sửa. Không cần migration xóa trait, thay đổi Unity hay thêm cache/dependency để xử lý R01–R04.

## 4. Giới hạn bằng chứng

- Review này gồm đọc code/caller và các probe chỉ đọc cho JSON/Base64/timestamp/serialization; chưa chạy PostgreSQL integration, concurrent HTTP thực tế hoặc load test.
- Lượt triển khai trước đã báo build thành công, 364 Rules tests và 4 test BE không cần DB qua. Những test đó không chứng minh EF join, transaction và các case legacy retry mới nêu đã đúng.
- Chưa xác định thêm regression cụ thể ở participant-filtered state join, subscribe authorization, lịch bot hoặc version/deadline của bot qua đọc code.
- Chưa đo p50/p95 trên Render/Supabase; không kết luận đã giải quyết độ trễ 3–5 giây.
