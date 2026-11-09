# Kế hoạch giảm độ trễ một nước cờ Hero Chess

Cập nhật: 2026-10-19. Đây là kế hoạch cho backend và hợp đồng đồng bộ với Unity/React; **chưa chạy benchmark trên Render/Supabase**. Mốc 3–5 giây mỗi nước là trải nghiệm được báo lại, chưa được tách thành thời gian mạng, server và client. Mã nguồn BE hiện đã gộp GET `/state` thành một truy vấn, chỉ xếp lịch bot khi đến lượt bot, giữ nước bot đã chọn dù vượt budget và thêm timing log; hiệu quả thực tế chưa được đo. Unity nằm ngoài phạm vi triển khai này.

## 1. Trạng thái thực tế cần giữ đúng

- Nước đi hiện gửi bằng REST `POST /api/v1/matches/{id}/commands`. Backend kiểm tra tài khoản, participant, `commandId`, version, deadline và luật; giữ gate theo trận cùng transaction/row lock, lưu action và state rồi trả ACK **đã chứa snapshot**. Sau commit, socket phát `match.changed` gồm matchId/version.
- Unity đã có WebSocket nhận `match.changed`, nhưng `PollLoop` vẫn đọc bàn cờ mỗi 1,2 giây. Sau ACK thành công, `SubmitPending` nhận snapshot rồi gọi thêm `RefreshBoard()` → một GET `/state` nữa. Socket cũng đánh dấu để gọi GET `/state`. React vẫn poll sau mỗi 3 giây; khi nhận `match.changed`, `refresh()` đọc `/selection` rồi `/state`.
- Một GET `/state` hiện lấy participant, `match_state`, `game_match` bằng một truy vấn projection/left join; policy tài khoản còn truy vấn DB riêng. Một command thường cần truy vấn participant, kiểm tra `commandId`, khóa match/state, ghi và commit. Đây là **các lượt truy vấn thấy trong source**, chưa phải số đo thời gian thực tế.
- `ApplyMove` tạo cả danh sách legal moves để xác thực một nước; `FinishTurn` lại tạo danh sách cho phe kế tiếp để kiểm tra hết nước. Worker timeout quét 500 ms/lần, maintenance 1 giây/lần. Sau command active, bot chỉ được schedule khi trận bot chuyển tới lượt bot.
- **Hero skill đã bị vô hiệu ở đường BE đang dùng:** catalog trả `hero.trait = null`, `traits = []`; trận mới không freeze trait/movement riêng; `/hero-actions` trả rỗng; `hero_active` bị từ chối; bot/checkmate chỉ xét Command skill cứu nguy. API state/ACK/replay lọc metadata và event Hero skill cũ. Dữ liệu trait và lịch sử gốc vẫn nằm trong DB; code luật cũ vẫn còn để đọc snapshot lịch sử. Cần chạy regression trên DB test trước deploy.

Đường thời gian cần đo: **bấm đi → client gửi → server nhận → chờ gate/DB/luật/commit → ACK → Unity áp dụng snapshot → đối thủ nhận `match.changed` và đồng bộ → đối thủ hiển thị**. Nếu chỉ đo HTTP mà bỏ bước render, ta có thể sửa đúng server nhưng người chơi vẫn thấy 3–5 giây.

## 2. Đợt 0 — Lấy baseline trước khi đổi logic

1. Ghi commit deploy, gói và region của Render, region của Supabase, loại kết nối/pool, số instance, client đang dùng. **Xác nhận trên cấu hình thật**, không suy từ tài liệu hoặc URL. Tách lần đánh thức Render khỏi server đã nóng. Render Free có thể ngủ sau 15 phút không có inbound traffic; cold start là vấn đề riêng với nước đi đang diễn ra. [Render Free](https://render.com/docs/free).
2. Đo ít nhất 30 nước thường và 10 Command skill ở một trận PvP nóng; đo riêng bot. Ghi p50/p95 cho `click→ACK`, `click→hiển thị bên mình`, `commit→đối thủ thấy`, GET `/state`, GET `/legal-actions`; ghi lỗi/timeout và số lần client gọi mỗi endpoint sau một nước. Đồng hồ client và server khác nhau: đo các đoạn bằng đồng hồ đơn điệu trên từng máy, chỉ ghép dấu mốc bằng requestId/commandId/version, không lấy hiệu hai timestamp chưa đồng bộ.
3. Thêm timing gọn vào backend: request vào/ra; chờ gate; truy vấn participant/dedup; chờ và đọc row lock; tính luật; `SaveChanges`; commit; tạo snapshot/ACK. Ghi thời gian và version, không ghi token, payload action, full state hoặc vị trí cọc ẩn. Dùng timing để tìm nhánh tốn nhất; chỉ bật log SQL chi tiết tạm thời trên DB test.
4. Đo pool wait/connection, DB CPU và query duration, API CPU/GC/RSS, payload state, số socket, queue bot; so sánh giờ rảnh với lúc có 2 trận đồng thời. **Không chạy load test tạo/sửa nhiều trận trên Supabase đang demo.** Dùng DB test riêng hoặc bản sao dữ liệu đã ẩn thông tin nhạy cảm.

**Quyết định sau baseline:** nếu server ACK đã nhanh nhưng Unity vẫn chậm, ưu tiên đợt 2 (client). Nếu DB/commit chiếm đa số, làm đợt 1 và kiểm tra region/pool. Nếu luật hoặc bot chiếm đa số, làm nhánh tương ứng ở đợt 3. Không tăng cấu hình hoặc thêm cache trước khi biết nút thắt.

## 3. Đợt 1 — Backend: giảm round-trip trên đường đang dùng

Làm từng thay đổi nhỏ, đo lại cùng kịch bản sau mỗi thay đổi.

1. **GET `/state` — đã sửa trong source, chưa đo:** `MatchReadService.StateAsync` dùng một projection/left join để giữ phân biệt `MATCH_NOT_FOUND` với `MATCH_NOT_STARTED`; truy vấn kiểm tra tài khoản active của policy vẫn giữ. State vẫn đi qua `MatchStateProjection`. Kiểm tra SQL thực tế và p95 trên DB test/Render trước khi kết luận nhanh hơn.
2. **Bot queue — đã sửa trong source, chưa đo:** `MatchCommandService` chỉ enqueue sau commit khi trận bot chuyển tới lượt bot; timeout theo cùng nguyên tắc. Worker vẫn tự xác nhận match/version/side. Bot không bỏ nước hợp lệ đã chọn khi tính quá `BotThinkMilliseconds=250` ms; tiếp tục qua command pipeline kiểm tra version. Đo số job/query và độ trễ bot trước/sau.
3. **Command write path:** nếu log cho thấy DB RTT chiếm chính, xem xét gộp các lượt đọc không phụ thuộc nhau của participant/dedup/match-state, nhưng giữ thứ tự khóa `game_match` → `match_state`, transaction, idempotency và deadline. Không bỏ `FOR UPDATE`, không bỏ action history hoặc snapshot chỉ để giảm số query. Đo riêng commit và lượng JSON lưu ở `match_state.state`/`match_action.state_after` trước khi tối ưu serialization.
4. Nhánh socket `match.command` hiện gọi `StateAsync` rồi command service lại đọc participant/state. Chỉ tối ưu nhánh này nếu client thực sự chuyển sang gửi command qua socket; Unity và React hiện gửi REST. Nếu bỏ pre-read, phải giữ kiểm tra player active, participant và chỉ subscribe sau khi được authorize; command sai không được cấp quyền nghe trận.

**Nghiệm thu:** `/state` ít query hơn và p95 thấp hơn trên cùng DB; ACK move/Command skill không chậm đi; account bị khóa và người ngoài trận vẫn bị chặn; version, retry `commandId` và kết quả trận giữ nguyên. Ghi kết quả đo, không khẳng định lợi ích từ số dòng code đã sửa.

## 4. Đợt 2 — Bỏ lần chờ thừa ở client; cần phối hợp Unity/React

Đây là phần bắt buộc nếu backend đã nhanh mà cảm giác chơi vẫn chậm. Không sửa client trong PR backend đầu tiên.

1. **Unity ưu tiên:** với ACK mới, áp dụng `reply.snapshot` và không GET `/state` ngay sau đó. Chỉ resync nếu ACK là bản lịch sử do retry, version thiếu/cũ, hoặc có tín hiệu mới hơn (ví dụ bot đã đi). Khi socket subscribe khỏe, dừng poll `/state` mỗi 1,2 giây; `match.changed` chỉ kích hoạt **một** GET nếu version mới hơn đang hiển thị. Gộp thông báo đến trong lúc GET đang chạy và đọc bù version cuối. Khi socket mất, bật poll fallback 2–3 giây và resync lúc reconnect/app trở lại foreground.
2. **React test client:** khi active và socket khỏe, dừng poll 3 giây; `match.changed` đọc trực tiếp `/state` thay vì `/selection` trước. ACK đã có snapshot thì áp dụng ngay. Poll `/selection` chỉ ở giai đoạn chọn đội hình; terminal/replay dừng poll live. Chống response version cũ ghi đè bản mới.
3. Chỉ gọi `/legal-actions` khi đến lượt mình và version đổi; không để nhiều yêu cầu cùng version chạy song song. Đồng hồ hiển thị chạy từ `serverNow`/`deadlineAt` ở client, không poll server mỗi giây. Vẫn giữ nút đồng bộ thủ công và GET `/state` để phục hồi sau mất socket.

**Nghiệm thu:** hai máy thấy cùng version sau move/Command skill/timeout; socket mất rồi nối lại bắt kịp; ACK thành công không phát sinh GET `/state` vô điều kiện; số GET định kỳ khi socket khỏe giảm ít nhất 80% so với baseline; pending UI được xóa ở cả ACK, lỗi và timeout. `match.changed` chỉ chứa version nên client vẫn cần GET cho đối thủ; chưa phát raw state qua broadcast vì state có dữ liệu theo phe.

## 5. Đợt 3 — Chỉ làm nhánh được profile xác nhận nóng

- **CPU luật:** nếu `ApplyMove`/`FinishTurn` tốn nhiều thời gian, cho việc xác thực một đích và kiểm tra “còn ít nhất một nước” dừng sớm trên cùng bộ luật sinh nước. `/legal-actions` vẫn trả đủ. Giữ kiểm tra Tướng bị chiếu, Thành/Rào/Khiên, hiệu ứng và Command skill cứu nguy. Hero skill đã bị vô hiệu trong trận mới. So legal sets/kết quả với regression hiện có.
- **Worker/DB nền:** nếu sweep chiếm pool/CPU, thử timeout poll 1000 ms và maintenance 5 giây, batch có giới hạn; vẫn kiểm deadline theo thời điểm server nhận command, không nới 90 giây luật. Bot schedule sau commit là đường chính, sweep là phục hồi. Đo độ trễ timeout/settlement trước khi giữ cấu hình mới. Sau baseline có thể hạ log SQL thành Warning ở Production; việc đó không giảm số query.
- **Index/cache:** chỉ thêm index khi `EXPLAIN (ANALYZE, BUFFERS)` trên DB test chỉ ra scan đắt. Chỉ cache read state/legal khi nhiều lượt đọc cùng version vẫn là nút thắt; key phải gồm match, version và phe xem. Cache invalidation sau commit, giữ DB là nguồn phục hồi. Không cache quyền từ bearer claim, không broadcast state chung cho hai phe, không thêm Redis chỉ vì “có cache sẽ nhanh”.
- **Hosting:** nếu Render API và Supabase ở xa nhau, đo tuyến client→API và API→DB rồi cân nhắc service mới gần DB/người chơi. Render hiện có Singapore và không đổi region service tại chỗ. [Render regions](https://render.com/docs/regions). Thử gói compute/pool theo số đo, không mặc định tăng pool sẽ nhanh. Một instance là giới hạn kiến trúc hiện tại: lock, ticket, matchmaking, broadcast và bot queue đều trong RAM; chưa scale ngang khi chưa thiết kế đồng bộ giữa các instance. [Supabase connection management](https://supabase.com/docs/guides/database/connection-management).

## 6. Bảo mật và tính đúng không được đánh đổi

- Giữ kiểm tra tài khoản active/role từ DB và participant cho HTTP/socket; không tin `matchId`, `side`, legal moves hay snapshot do client gửi. Rate limit và outbox socket có giới hạn vẫn giữ.
- Giữ transaction, thứ tự row lock, version/deadline, idempotency `commandId`; hai lệnh cùng version chỉ một được nhận, retry cùng command không đi hai lần.
- State/event/replay vẫn lọc cọc ẩn theo phe. ACK và cache là bản theo đúng viewer; không chia sẻ object `GameState` mutable hoặc gửi snapshot đầy đủ qua `match.changed`.
- Thay đổi Hero skill là thay đổi gameplay riêng với các bước chặn `hero_active`, không freeze trait cho trận mới và lọc dữ liệu trả về. Giữ dữ liệu lịch sử gốc trong DB; regression phải xác nhận Command skill, bot, checkmate và replay cũ.
- Không deploy giữa trận để đo: `MatchRecoveryHostedService` hiện hủy trận selecting/active khi restart. Render Free có filesystem tạm thời, nên keys lưu local cũng cần kế hoạch vận hành riêng nếu muốn phiên đăng nhập sống qua restart. [Render Free](https://render.com/docs/free).

## 7. Thứ tự làm và ngưỡng dừng

| Bước | Việc giao được | Điều kiện chuyển bước |
|---|---|---|
| 0 | Timing BE đã thêm; còn baseline 2 client thật + DB và xác nhận region/gói | Biết phần nào của 3–5 giây nằm ở client, DB, luật, bot |
| 1 | `/state` + bot queue đã sửa trong source; command write path chỉ tối ưu nếu số đo yêu cầu | So sánh cùng workload; auth/version/hidden state pass |
| 2 | Một PR Unity bỏ GET sau ACK và poll nhanh khi WS khỏe; React làm tương tự nếu còn dùng | Own board và đối thủ cập nhật nhanh, reconnect đúng |
| 3 | CPU/worker/index/cache/hosting theo kết quả profile | Chỉ giữ thay đổi có cải thiện p95 mà không tăng lỗi |

Mục tiêu thử trên **service đã nóng**, cùng tuyến mạng nhóm dùng: p95 từ bấm tới bàn mình cập nhật ≤1 giây; đối thủ thấy nước ≤1,5 giây; ACK server p95 ≤800 ms. Đây là ngưỡng để đánh giá, không phải cam kết với Render Free. Nếu mạng/region không cho phép đạt, ghi breakdown và điều chỉnh vị trí/compute thay vì làm yếu luật hay phân quyền.

Sau khi hết độ trễ một nước, mới thử tải 2 → 10 → 20 → 40 người trên DB test, giữ mỗi mức ít nhất 10 phút với **nước đi thật**, theo dõi p95, 5xx, queue và pool wait; dừng nếu chúng tăng kéo dài. Không dùng số socket mở để kết luận công suất. Mỗi đợt có commit/image rollback; build và regression hiện có chạy trước smoke test PvP/bot trên DB disposable. Plan này chưa thay đổi gameplay hay deploy.
