# Code review — 23/09/2026

> Báo cáo dưới đây ghi nhận trạng thái trước sửa. Các bản sửa và bằng chứng kiểm thử mới nằm trong [Review_Fix_Status_2026-09-23.md](Review_Fix_Status_2026-09-23.md).

Kết luận: chưa nên chốt backend prototype. Build và test hiện có xanh, nhưng còn lỗi quyền WebSocket, xử lý sau commit, vòng đời selection và reconnect. Không lấy việc chưa triển khai đầy đủ logic hero làm lỗi nghiệm thu.

Review này không sửa code sản phẩm và không đổi trạng thái các task đã tick. Số dòng tham chiếu tại thời điểm review.

## Kiểm chứng đã thực hiện

- `dotnet build HeroChessBackend.slnx --no-restore --verbosity quiet`: PASS, 0 warning, 0 error.
- `dotnet test HeroChessBackend.slnx --no-build --verbosity quiet`: PASS, 16 Rules tests + 5 integration tests, không skip.
- Chạy Kestrel thật trên cổng 5197, dùng database riêng `hero_chess_astra_review_20260923`; gửi REST và native Node WebSocket từ ba tài khoản riêng. Instance review đã dừng sau khi kiểm tra.
- Probe runtime xác nhận: outsider nhận snapshot sau command bị từ chối; duplicate WS không được ACK; retry confirm trả STALE_REVISION sau sửa lineup nguồn; action sai dạng trả HTTP 500.
- Probe JavaScript bằng Node VM xác nhận snapshot trận mới version 0 bị bỏ qua sau snapshot trận cũ version 10. Đây là kiểm thử hàm, chưa phải smoke test hai trình duyệt.
- Các phát hiện ghi “đọc code” bên dưới chưa được fault-inject hoặc kiểm thử đầu cuối trong review này.

## R01 — P1: Command trái phép vẫn đăng ký socket vào trận

Vị trí: `HeroChess.Api/Services/WebSocketEndpoint.cs:61`.

Nhánh `match.command` gọi `hub.Subscribe` trước khi `ExecuteAsync` kiểm tra participant. Lệnh của người ngoài trả MATCH_NOT_FOUND, nhưng subscription vẫn tồn tại. Khi người chơi hợp lệ đi nước tiếp theo, outsider nhận `match.command_accepted` chứa snapshot đầy đủ. Tái hiện trên WebSocket thật: outsider bị từ chối cả subscribe và command nhưng vẫn nhận đủ 32 quân sau một nước đi hợp lệ. Điều kiện: outsider có một matchId hợp lệ.

Sửa: kiểm tra quyền trước mọi thay đổi subscription; mọi đường đăng ký socket phải cùng áp dụng participant authorization.

Test bắt buộc: tài khoản thứ ba gửi command vào trận, sau đó hai participant đi nước/kết thúc trận; tài khoản thứ ba không được nhận state, ended hoặc settled.

## R02 — P1: Ngắt request sau commit có thể bỏ dở settlement

Vị trí: `HeroChess.Api/Services/MatchCommandService.cs:97`–108; `MatchRecoveryHostedService.cs:34`.

Đọc code: sau commit state/action, service vẫn dùng cancellation token của request để broadcast, settle và đọc kết quả. Request bị hủy tại khoảng này có thể ném OperationCanceledException trước settlement. Trận đã completed nhưng chưa có settled_at; reservation trong MatchmakingService chưa được Release nên người chơi vẫn PLAYER_BUSY. Retry command đi nhánh dedup và trả về ngay, không tiếp tục settlement. Recovery cho terminal chưa settled chỉ chạy khi startup, timeout worker chỉ quét active. Với nước đi chưa kết thúc, bot scheduling cũng có thể bị bỏ qua.

Sửa: công việc bắt buộc sau commit cần được phục hồi độc lập với request, có retry khi process vẫn chạy; broadcast không được quyết định settlement có chạy hay không. Có thể dùng worker quét terminal chưa settled và cơ chế lên lịch bot từ state đã commit.

Test bắt buộc: hủy request ngay sau commit trước broadcast; xác nhận đúng một action, settlement cuối cùng đúng một lần, người chơi vào trận mới được, bot tiếp tục. Không cần restart để test PASS.

## R03 — P1: Trận selecting không có đường thoát khi đối thủ bỏ đi

Vị trí: `HeroChess.Api/Services/MatchmakingService.cs:91`; `MatchSelectionService.cs:61`; `MatchTimeoutHostedService.cs:20`.

Đọc code: ticket đã matched không được cancel; player trong selecting bị chặn queue; chưa có state nên không resign được; timeout worker chỉ xử lý active. Nếu đối thủ bỏ trước confirm, hai người bị giữ ở selecting cho đến restart. Trường hợp catalog đổi còn trả CONTENT_VERSION_CHANGED và yêu cầu tạo trận mới, nhưng chính selecting hiện tại ngăn tạo trận mới.

Sửa: bổ sung cancellation hoặc deadline cho selection; khi hết hạn/catalog đổi không thể tiếp tục, chuyển terminal và giải phóng reservation theo policy rõ ràng.

Test bắt buộc: bỏ selection hoặc đổi catalog giữa selection; cả hai tài khoản có đường quay lại queue trong cùng process.

## R04 — P2: Client chậm có thể giữ khóa trận và chặn timeout các trận khác

Vị trí: `HeroChess.Api/Services/MatchConnectionHub.cs:20`; `MatchCommandService.cs:99`; `MatchTimeoutHostedService.cs:25`.

Đọc code: broadcast chờ SendAsync tuần tự cho từng socket, không có timeout riêng. MatchCommandService giữ gate trận trong suốt broadcast. Khi một socket không đọc và send bị backpressure, command/timeout cùng trận phải chờ; timeout sweep lại chờ từng trận tuần tự nên các trận khác trong sweep cũng có thể bị trì hoãn.

Sửa: đưa gửi socket ra khỏi critical section, giữ thứ tự sự kiện bằng queue có giới hạn; áp dụng timeout/disconnect cho recipient chậm.

Test bắt buộc: WebSocket giả có SendAsync không hoàn thành; kiểm tra command và timeout trận khác vẫn tiến triển trong thời gian hữu hạn.

## R05 — P2: Retry command WebSocket không nhận ACK

Vị trí: `HeroChess.Api/Services/WebSocketEndpoint.cs:62`; `MatchCommandService.cs:29`–42.

Nhánh dedup trả MatchCommandResultDto mà không broadcast; endpoint bỏ qua kết quả trả về. Tái hiện: lần gửi đầu nhận command_accepted, gửi lại cùng commandId/payload không nhận sự kiện nào trong cửa sổ 700 ms, state vẫn version 2. Client mất ACK đầu tiên không thể xác nhận kết quả retry qua WS.

Sửa: trả ACK trực tiếp cho connection gửi retry; chỉ broadcast thay đổi mới cho participant khác. Giữ nguyên commandId và kết quả đã commit.

Test bắt buộc: gửi lại command đã commit qua socket mới; nhận ACK duplicate, không tăng version và không lặp side effect.

## R06 — P2: Web lưu pending command nhưng không retry khi reconnect

Vị trí: `HeroChess.Api/wwwroot/app.js:87`–90, 113–122.

Đọc code: pending Map chỉ được set/delete, không được dùng để gửi lại. onopen chỉ subscribe; mất kết nối trước ACK không có bước giải quyết các lệnh chưa rõ kết quả. REST network error còn xóa pending dù server có thể đã commit. Người dùng bấm lại sẽ tạo commandId mới.

Sửa: lưu command chưa rõ kết quả theo matchId/commandId; sau reconnect gửi lại cùng ID và đối chiếu ACK. Chỉ xóa khi có kết quả xác định. Phụ thuộc R05.

Test bắt buộc: mất kết nối trước và sau commit, reconnect rồi xác nhận chỉ một action và pending được giải quyết.

## R07 — P2: Chuyển trận giữ bàn cũ; live event có thể ghi đè replay

Vị trí: `HeroChess.Api/wwwroot/app.js:35`, 101, 144–150.

acceptSnapshot so sánh version mà không kiểm tra matchId. Probe hàm: nhận trận A v10 rồi trận B v0 vẫn giữ A v10. UI đã dùng matchId B để gửi command nhưng board/version/pieceId còn thuộc A. Replay cũng dùng chung snapshot trong khi handler socket cũ vẫn tiếp nhận sự kiện.

Sửa: quản lý match đang xem, reset state/legal/selected khi đổi trận; chỉ so version trong cùng match. Tách replay khỏi live state và bỏ qua sự kiện ngoài trận đang xem.

Test bắt buộc: chơi trận thứ hai không reload trang; mở replay trong lúc socket trận cũ vẫn có event.

## R08 — P2: Retry confirm phụ thuộc lineup nguồn dù đã khóa

Vị trí: `HeroChess.Api/Services/MatchSelectionService.cs:58`–72.

Confirm chỉ early-return khi toàn trận active, không khi participant đã confirmed và đang đợi đối thủ. Nó tiếp tục đọc/validate/freeze lineup nguồn. Tái hiện: red confirm thành công, sửa tên lineup nguồn làm revision tăng, retry confirm trả 409 STALE_REVISION. Trong khi đó black vẫn có thể confirm và bắt đầu trận với snapshot red đã khóa. Xóa lineup nguồn cũng làm retry không còn idempotent.

Sửa: participant đã confirmed phải trả trạng thái selection hiện tại mà không đọc lại lineup nguồn hay ghi đè snapshot.

Test bắt buộc: confirm hai lần trước khi đối thủ confirm, có sửa/xóa lineup nguồn ở giữa; kết quả thành công và snapshot frozen giữ nguyên.

## R09 — P2: Trait passive/active bị dùng làm movement override

Vị trí: `HeroChess.Api/Services/MatchSelectionService.cs:114`; `HeroChess.Rules/XiangqiRulesEngine.cs:73`–77.

Đọc code: Freeze gán mọi Trait.ImplementationKey vào MovementImplementationKey mà không xét loại trait. Rules engine thấy key không có movement handler thì yield break, không sinh nước đi cơ bản. Thêm hero passive/active có key riêng có thể làm quân mất toàn bộ khả năng di chuyển. Đây là lỗi nền extension, không phải yêu cầu triển khai thêm logic hero ngay.

Sửa: chỉ special_move thay movement; giữ loại/key của passive/active riêng và giữ movement class cơ bản cho chúng.

Test bắt buộc: hero có passive/active chưa implement vẫn đi theo class; special_move chưa implement được xử lý theo policy rõ ràng.

## R10 — P2: Payload action sai dạng trả lỗi server

Vị trí: `HeroChess.Api/Services/MatchCommandService.cs:54`, 60–64, 73.

Tái hiện REST command của participant hợp lệ với action là []: HTTP 500 INTERNAL_ERROR. TryGetProperty/GetString/GetGuid/GetInt32 không kiểm tra ValueKind/định dạng trước khi đọc; input sai lọt thành exception không được map sang lỗi validation.

Sửa: validate cấu trúc action và kiểu/range các trường trước khi xử lý; trả lỗi 400 ổn định cho payload sai, không thay state/action log.

Test bắt buộc: action null/array, type không phải string, GUID sai, tọa độ không phải số nguyên, undo target sai kiểu.

## R11 — P2: Frozen reward bằng 0 bị thay bằng config hiện tại

Vị trí: `HeroChess.Api/Services/SettlementService.cs:30`–31, 45.

Đọc code: các giá trị frozen chỉ được dùng khi > 0; 0 bị chuyển thành null để policy lấy options hiện tại. Trận tạo khi reward bằng 0 nhưng recovery settlement sau khi config tăng reward sẽ nhận mức thưởng mới. Elo K=0 có vấn đề tương tự. Snapshot đã chứa số 0 hợp lệ nhưng không được tôn trọng.

Sửa: sử dụng trực tiếp giá trị frozen; nếu cần hỗ trợ snapshot cũ thiếu trường, phân biệt thiếu với 0 bằng schema/version hoặc nullable field.

Test bắt buộc: freeze reward/K=0, thay runtime config rồi settle/recover; coin và Elo vẫn theo snapshot.

## Thứ tự xử lý đề xuất

1. R01: quyền subscribe; R02 + R04: tách broadcast và phục hồi công việc sau commit.
2. R03: đường thoát selecting; R05 + R06: ACK và retry qua reconnect.
3. R07 + R08: state web và idempotent confirm.
4. R09–R11: extension trait, validation và frozen policy.
5. Chạy regression mới cùng 21 test hiện có, sau đó smoke hai trình duyệt: ranked → disconnect/reconnect → kết thúc → trận mới → replay.

Các test xanh hiện tại chưa đủ để chốt các mục reconnect, lỗi sau commit và smoke browser của roadmap. Chưa kiểm tra tải hoặc mô hình chạy nhiều API instance trong lượt review này.

