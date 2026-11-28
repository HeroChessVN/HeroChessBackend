# Kế hoạch tăng sức tải HeroChess trên VM 1 GB

Giữ một API process trên VM Azure 1 GB RAM. Giảm số lần client đọc bàn cờ, giảm truy vấn phụ của mỗi lần đọc và bổ sung khả năng chạy tối đa hai worker bot. Triển khai theo từng bước để đo được phần cải thiện và có thể quay về cấu hình cũ khi cần.

Đây là plan triển khai, chưa phải kết quả kiểm thử sức tải. Các mốc 20, 50 và 100 người là mức tải cần thử, không phải số người đã được bảo đảm phục vụ.

Cập nhật 27/11/2026: đã triển khai BE và web test, kiểm chứng local; mặc định giữ một worker. Còn bàn giao Unity và đo tải kéo dài trên Azure. Xem [runbook và kết quả](CAPACITY_1GB_RUNBOOK.md).

## Phạm vi và điều kiện

- Giữ VM `Standard_B2ats_v2`, 2 vCPU và 1 GB RAM; không yêu cầu thuê máy lớn hơn.
- BE thực hiện thay đổi API nội bộ, truy vấn và worker. `/frontend` trong repo là client test để kiểm chứng cùng cách đồng bộ mới.
- Unity chỉ có tài liệu bàn giao; không sửa source, scene hoặc asset Unity trong công việc BE.
- Giữ nguyên luật, sức mạnh bot, ngân sách tìm kiếm, CD, Elo, Coin và dữ liệu snapshot của trận.
- Giữ một API process; không thêm microservice, Redis, hàng đợi ngoài hoặc dependency mới.
- Không cần migration SQL trong phạm vi này. Không tăng connection pool theo số người chơi; mức đã chỉnh lên 10 là điểm xuất phát để đo.

## Hiện trạng đã xác định

| Thành phần | Hiện trạng | Thay đổi cần làm |
|---|---|---|
| Unity | `PollLoop` đọc state khoảng mỗi 1,2 giây, kể cả khi có WebSocket | WebSocket báo thay đổi; GET dự phòng thưa hơn |
| Đăng ký WebSocket | BE đã gửi `match.state`; Unity chưa dùng snapshot này mà GET lại | Dùng snapshot đã nhận, tránh lượt đọc trùng |
| State trong BE | Thường 5 truy vấn: state, participants, players, ratings, tên Hero | Mục tiêu 3 truy vấn cho snapshot mới có đủ tên |
| Tên Hero | State và replay cùng có đường đọc catalog để bù tên cũ | Chỉ bù quân thiếu tên, dùng chung lookup có giới hạn |
| Web test trước tối ưu | Poll 3 giây; refresh đọc selection rồi state | Tách selecting và active, gộp refresh; giữ legal theo version |
| Bot | Một worker, queue mặc định 128, dedup theo match/version | Cấu hình 1–2 worker, thêm bảo vệ một job đang chạy mỗi trận |
| Lam Sơn | Một phép phân tích và tối đa 4 lượt chờ | Giữ giới hạn hiện có; đo cạnh tranh CPU với bot |

Lần đọc VM ngày 26/11/2026: hệ điều hành thấy khoảng 892 MiB RAM; API khoảng 168 MiB, Caddy khoảng 33 MiB, RAM khả dụng khoảng 300 MiB. Đây là mẫu lúc tải thấp, không dùng để suy ra số người tối đa. VM không có swap, nên phải theo dõi RAM khả dụng và đỉnh dùng RAM khi thử tải.

## Thứ tự triển khai

1. Đo baseline và khóa các tiêu chí kiểm chứng.
2. Tối ưu đường đọc trong BE, giữ một worker.
3. Áp dụng cách đồng bộ mới cho web test; gửi handoff cho nhóm Unity.
4. Thêm cấu hình worker và kiểm thử đúng đắn với cả một và hai worker.
5. Đo A/B trên VM 1 GB, chọn số worker theo kết quả.

Không bật đồng thời mọi thay đổi rồi chỉ so một kết quả cuối; phải tách được lợi ích giảm request, giảm truy vấn và tăng worker.

## Phần 1 Đồng bộ bàn cờ theo sự kiện

### Luồng sử dụng API hiện có

| Tình huống | Cách xử lý |
|---|---|
| Vào trận hoặc reconnect | Xin ticket, kết nối, gửi `match.subscribe`, nhận `match.state` và áp dụng snapshot |
| Socket đã subscribe thành công | Đọc state dự phòng mỗi khoảng 10 giây, có jitter; không GET mỗi 1,2 giây |
| `match.changed` | Lưu version mới nhất cần lấy, yêu cầu một lần refresh state |
| ACK có `snapshot` | Áp dụng snapshot, không GET thêm nếu đã đáp ứng version đang cần |
| `match.ended` hoặc `match.settled` | Ép refresh metadata/result dù version bàn cờ có thể không tăng |
| Không mở được socket hoặc socket mất | Poll khoảng 2 giây khi request thành công; lỗi thì backoff 2/4/8/15 giây, thêm jitter |
| Socket phục hồi | Subscribe lại, đồng bộ đầy đủ, chuyển về nhịp dự phòng 10 giây |
| Rời trận, logout hoặc chuyển tài khoản | Hủy request/socket/timer cũ; phản hồi cũ không được cập nhật màn mới |

Mốc 10 giây áp dụng cho GET dự phòng lúc socket khỏe. Đi quân và sự kiện vẫn cập nhật ngay; không đợi đủ 10 giây. Đồng hồ client đếm từ `serverNow` và `turnDeadlineAt`, còn hết giờ do server quyết định.

### Quy tắc tránh sai lệch và request trùng

- Socket chỉ được coi là sẵn sàng sau khi nhận snapshot hợp lệ cho subscription; `Open` chưa đủ.
- Chỉ một lần refresh active state đang chạy trên mỗi màn trận. Event đến trong lúc GET đang chạy phải được ghi nhận, không bị xóa bởi callback cũ.
- Nếu response vẫn thấp hơn version sự kiện mới nhất, làm một lượt refresh nối tiếp có giới hạn/backoff. Không mở nhiều GET song song hoặc vòng retry vô hạn.
- Snapshot trong ACK hiện có thể không kèm `sides`/hồ sơ như GET state. Chỉ dùng ACK để đáp ứng dữ liệu bàn cờ; giữ metadata hiển thị đã biết của cùng trận khi trường tùy chọn bị thiếu, hoặc refresh nếu chưa có. Không gộp lại quân/effect từ snapshot cũ. Settlement vẫn phải cập nhật từ server.
- Lọc phản hồi theo tài khoản, `matchId` và generation của màn. Bỏ snapshot có version cũ hơn; cùng version vẫn cho phép cập nhật metadata mới và thời gian server mới hơn.
- Không bỏ `match.settled` chỉ vì đã thấy version đó. Cập nhật Elo/Coin và trạng thái settlement vẫn cần hoạt động.
- `match.changed` là thông báo, không phải toàn bộ state và không phải dữ liệu đủ để tự sửa bàn cờ.
- Gộp thông báo dồn dập, chống lặp banner skill theo match/version. Giữ luồng nhận event trên main thread của Unity để cập nhật UI.
- Khi command bị lỗi mạng chưa rõ kết quả, retry phải giữ nguyên `commandId`; không phát sinh một nước mới chỉ để lấy lại trạng thái.
- Không đổi nhịp lobby, selecting, custom-room heartbeat trong bước này. Không dùng việc giảm polling để thay đổi luật logout/đóng phòng.

### Web test trong repo

- Đang selecting: tiếp tục luồng selection. Khi đã active, gọi trực tiếp `/state`, không gọi `/selection` trước mỗi refresh.
- Dùng snapshot từ `match.state`/ACK và áp dụng các quy tắc version/refresh ở trên.
- WebSocket khỏe: poll dự phòng 10 giây; mất kết nối: fallback và backoff.
- Chỉ tải `legal-actions` khi match/version/lượt hoặc trạng thái review thay đổi; không tải lại chỉ vì `serverNow` mới.
- Undo luôn làm version tăng; phải làm mới legal actions, forecast và các lựa chọn cũ sau Undo.
- Khi trận kết thúc: refresh kết quả/settlement có giới hạn, sau đó dừng polling trận đang chơi.

### Mục tiêu đo

Với 100 client trên bàn cờ và chưa có nước đi, nhịp nền 1,2 giây tương đương khoảng 83 GET/giây; nhịp 10 giây tương đương khoảng 10 GET/giây. Đây là phép tính cho polling nền của Unity, không phải cam kết giảm 88% tổng tải của hệ thống.

## Phần 2 Làm nhẹ đường đọc trong BE

### Gộp truy vấn hồ sơ

- Sửa `PlayerProfileService.ForPlayersAsync` tại hàm dùng chung: đọc player và rating bằng một query có LEFT JOIN; player thiếu rating vẫn nhận giá trị mặc định hiện tại.
- Giữ `eloOverride`, cách dựng avatar URL, dữ liệu hồ sơ công khai và kết quả cho trường hợp danh sách rỗng.
- Rà các caller: state, lịch sử trận, kết quả, selection và custom room. Không tạo một helper join riêng chỉ cho state trong khi caller khác vẫn đọc lặp.
- Không dùng `Task.WhenAll` để chạy nhiều query trên cùng `AppDbContext`.

### Bỏ truy vấn tên Hero dư

- Snapshot đã có `heroName` thì dùng tên frozen trong snapshot; không đọc lại tên sống rồi ghi đè.
- Chỉ thu thập `heroId` của quân có tên thiếu hoặc null. Một trang replay gom các ID thiếu tên từ toàn bộ snapshot của trang rồi lookup theo lô.
- Dùng một lookup tên nhỏ chung cho state và replay, với `MemoryCache` riêng giới hạn tối đa 256 tên, hết hạn tuyệt đối 60 giây. Chỉ lưu ID và chuỗi tên; không lưu entity EF, `DbContext`, ảnh hoặc JSON bàn cờ.
- Cache miss đọc các ID thiếu bằng một query; cache hit không cần query tên. Hero đã disabled vẫn được tra tên cho trận cũ.
- Đổi tên trực tiếp trên DB có thể cần tối đa 60 giây để hiện trên snapshot cũ thiếu tên. Snapshot đã frozen tên vẫn giữ tên cũ theo thiết kế trận.
- Không dùng `CatalogVersionService.ComputeAsync` làm điều kiện vô hiệu hóa cache tên: fingerprint hiện tại không bao gồm trường tên. Cũng không sửa cơ chế kiểm tra giá/SP/trait của formation sang cache này.

Mục tiêu thường gặp sau sửa: 1 query match/state + 1 query participants + 1 query players/rating = 3 query bên trong `StateAsync` cho state đã có đủ tên, không tính query xác thực/middleware ngoài service. Snapshot cũ có thể thêm một query tên khi cache miss. Đếm bằng interceptor trong test để kiểm chứng, không chỉ nhìn thời gian tổng.

### Tránh dựng state đầy đủ chỉ để kiểm tra quyền

Trong nhánh WebSocket `match.command`, thay lần dựng `StateAsync` trước command bằng kiểm tra participant nhẹ đã có. Chỉ subscribe sau khi xác thực quyền; giữ kiểm tra tài khoản active, quyền trận và validate trong pipeline command. Nhánh `match.subscribe` vẫn trả snapshot đầy đủ đúng người xem.

Phải kiểm tra lại mã lỗi và dữ liệu trả về cho outsider, trận chưa bắt đầu, trận đã kết thúc và command trùng. Không bỏ kiểm tra quyền để đổi lấy ít query hơn.

### Dữ liệu không cache trong vòng triển khai này

- Không cache nguyên `MatchStateDto`, trạng thái mutable hoặc phản hồi `/catalog` có quyền sở hữu theo account.
- Không cache Coin, Elo hay quyền thao tác để trả kết quả cũ sau mua hàng/settlement.
- Không cache state chung giữa hai phe: bẫy Trần và forecast là dữ liệu có giới hạn người xem.
- Không cache toàn bộ lịch sử trận trong RAM. Paging replay giữ nguyên.

`serverNow` phải được tạo cho mỗi phản hồi; authorization và phép chiếu dữ liệu riêng tư vẫn chạy trước khi trả dữ liệu.

Giữ outbox WebSocket có giới hạn và timeout gửi hiện có. Không thay thông báo chung bằng full snapshot broadcast cho cả hai phe để giảm GET, vì dữ liệu riêng tư và lượng bộ nhớ chờ gửi sẽ khác.

## Phần 4 Worker bot có giới hạn trên 1 GB

### Cấu hình đề xuất

| Cấu hình | Giá trị mặc định | Phạm vi |
|---|---:|---|
| `MatchRuntime__BotWorkerCount` | 1 | Chỉ nhận 1 hoặc 2 |
| `MatchRuntime__BotQueueCapacity` | 128 | Giữ giới hạn hiện có; không tăng để che việc xử lý chậm |

`BotWorkerCount` đã được bổ sung. Thay đổi giá trị cần tạo lại container; không có hot reload từ file env.

### Cách chạy

- Giữ `BotTurnScheduler` và `BotWorkQueue` hiện có. Chuyển phần xử lý một job thành hàm dùng chung rồi chạy số vòng worker cố định.
- Channel phải hỗ trợ nhiều reader khi dùng hai worker. Không `Task.Run` không giới hạn theo từng request/job.
- Mỗi job có DI scope và `AppDbContext` riêng; mỗi lần search có state clone và `BotEngine` riêng. Không chia sẻ state mutable giữa worker.
- Dedup `(matchId, version)` được giữ từ enqueue đến khi job kết thúc, kể cả thời gian đang xử lý.
- Thêm lease cho `matchId` đang xử lý để hai version của cùng trận không search đồng thời, ví dụ khi Undo xảy ra trong lúc bot đang nghĩ. Bộ lease chỉ giữ trận đang chạy và luôn giải phóng trong `finally`.
- Nếu job mới gặp trận đang có job cũ xử lý, bỏ lượt xử lý trùng đó và giải phóng pending key; sweep maintenance hiện có sẽ đọc lại version đang cần từ DB. Không tạo vòng requeue nóng. Kiểm thử cả trường hợp sweep chạy trước và sau khi job cũ kết thúc.
- Lease của worker khác với command gate. Không giữ command gate trong lúc search rồi gọi lại `ExecuteBotAsync`, vì sẽ tự chờ khóa của mình.
- Đọc lại lượt/version/deadline trước khi search; commit tiếp tục qua `ExecuteBotAsync`, transaction, khóa trận và `expectedVersion` hiện có.
- Undo, resign hoặc timeout trong lúc search phải làm kết quả cũ bị từ chối; không ghi nước bot lên trạng thái mới.
- Queue đầy hoặc tín hiệu bị mất vẫn được maintenance phục hồi. Một job lỗi không làm ngừng các worker khác.
- Khi shutdown, đợi các worker dừng và chạy cleanup queue một lần sau khi các job đã giải phóng lease; không để một worker gọi `Stop` làm mất việc của worker khác.

### Bảo vệ tài nguyên và chất lượng bot

- Giữ nguyên profile Easy/Medium/Hard và ngân sách search. Không tự hạ độ khó để đạt số người cao hơn.
- Giữ limiter Lam Sơn hiện tại là một analysis đang chạy. Khi đo hai worker phải đo cả tải có Lam Sơn; tổng CPU không chỉ có bot.
- Ngân sách search hiện là giới hạn mềm, không phải cam kết dừng cứng sau số ms. Theo dõi thời gian job thực tế.
- Không tự tăng worker dựa trên queue dài. Mặc định triển khai vẫn là 1; chỉ bật 2 khi phép đo trên chính VM chứng minh cải thiện.
- Nhiều worker có thể tăng throughput nhờ chồng thời gian chờ DB, nhưng cũng có thể tăng RAM, cạnh tranh CPU và làm chạm giới hạn CPU credits sớm hơn.

## Kiểm thử bắt buộc

| Nhóm | Trường hợp cần chứng minh |
|---|---|
| Đọc state | Snapshot mới không query tên; state thường không quá 3 query; snapshot cũ vẫn đủ tên |
| Hồ sơ chung | Thiếu rating, `eloOverride`, avatar URL, empty list và các caller lịch sử/phòng/selection giữ kết quả |
| Cache tên | Hết hạn, đủ chỗ/eviction, tên frozen không bị thay, không query lặp từng quân, không giữ resource đã dispose |
| Riêng tư | Hai phe vẫn chỉ thấy bẫy của mình; outsider không đọc/subcribe được; không lộ forecast |
| Đồng bộ client | Mất event, reconnect, event trùng, event trong lúc GET, response chậm từ trận cũ, timeout và settlement cùng version |
| Worker | Hai trận có thể xử lý song song; một trận tối đa một search; pending dedup không mất khi queue đầy |
| Race với người chơi | Undo/skill/resign/timeout trong lúc bot search; kết quả stale bị loại, trận tiếp tục hoặc kết thúc đúng |
| Vòng đời worker | Một worker lỗi, hủy reader đang đợi, shutdown hai worker, maintenance retry và startup recovery |
| Luật và settlement | Bộ test luật và bot/Undo/forecast/recovery hiện có không bị hồi quy |

Mở rộng các test xUnit và web test hiện có. Thêm một harness tải dùng Node có sẵn, không thêm framework tải mới. Harness phải có số client, mức think time, difficulty mix, thời gian chạy, timeout và cleanup rõ ràng; mô phỏng đúng số request của client, không chỉ bắn `/health`.

## Đo tải và tiêu chí chọn cấu hình

### Ma trận đo

1. Baseline client cũ + BE cũ + một worker.
2. BE tối ưu + client cũ + một worker để kiểm tra tương thích và lợi ích DB.
3. BE tối ưu + client mới + một worker để đo phần giảm polling.
4. Cùng bản tối ưu, so một worker và hai worker với cùng workload.

Mỗi bản thử PvP, bot Khó và tải hỗn hợp có Lam Sơn; có cả người nghĩ lâu và đợt nhiều người cùng đi. Tăng 10 → 20 → 30 → 50 → 100 người và dừng khi vượt ngưỡng, không bắt buộc chạy đến 100.

Test chức năng và tải thử đầu tiên dùng DB test tách biệt. Đợt đo Azure chạy trong cửa sổ riêng, chỉ dùng account test và cleanup qua API. Không tạo hàng loạt tài khoản thật hoặc thử tải cạnh người đang chơi mà không kiểm soát.

### Số liệu cần giữ

- HTTP state/command: throughput, p50/p95/p99 và tỷ lệ 5xx/timeout; tách 4xx mong đợi như stale khỏi lỗi hệ thống.
- DB: số query/state, latency và thời gian chờ connection pool; pool exhausted phải bằng 0.
- Bot: queue waiting/in-flight, p95 queue wait/search/command/total, job failed và stale.
- Forecast: p95 chờ/search, số `FORECAST_BUSY` và cache hit.
- VM: CPU, RAM khả dụng, process/container memory và đỉnh dùng RAM, restart/OOM; CPU credits trước/sau bài đo.
- Dùng meter/OpenTelemetry/Grafana hiện có; thêm số đo nhỏ khi còn thiếu, không gắn `playerId` hoặc `matchId` vào metric label.

### Ngưỡng đề xuất để đánh giá ban đầu

| Chỉ số | Mục tiêu |
|---|---|
| Độ đúng | Không mất/nhân đôi nước; không sai Undo/skill/settlement; không lộ dữ liệu |
| State p95 | ≤ 500 ms |
| Command p95 | ≤ 800 ms, không bao gồm animation phía client |
| Bot total p95 | ≤ 2 giây từ enqueue đến hoàn tất pipeline |
| Bot queue wait p95 | ≤ 1 giây, không tăng dần theo thời gian |
| 5xx và request timeout | < 1%; không có pool exhaustion |
| Bộ nhớ | RAM khả dụng không dưới 128 MiB kéo dài 30 giây; không OOM/restart |

Đây là tiêu chí chấp nhận cần đo, không phải kết quả hiện có. Khi có OOM/restart, pool exhaustion hoặc hàng đợi tăng không hồi phục thì dừng đợt tăng tải. Không chọn 2 worker nếu bot nhanh hơn nhưng state/command chậm quá ngưỡng hoặc làm RAM vượt ngưỡng.

Mỗi mức cần có warm-up và đợt chạy liên tục khoảng 30 phút. Ghi rõ think time và CPU credits; một bài ngắn lúc credits còn nhiều không chứng minh tải duy trì. Chỉ công bố sức tải cho workload đã đo. Có thể kết luận PvP đạt một mức nhưng bot chỉ đạt mức thấp hơn.

## Các file dự kiến thay đổi

| File hoặc nhóm file | Nội dung |
|---|---|
| `HeroChess.Api/Services/MatchReadService.cs` | Tránh query tên dư; dùng lookup tên chung; giữ authorization/projection |
| `HeroChess.Api/Services/PlayerProfileService.cs` | Gộp player/rating trong helper dùng chung |
| `HeroChess.Api/Services/MatchHistoryService.cs` | Chỉ bù tên thiếu, lookup theo lô của trang replay |
| `HeroChess.Api/Services/HeroNameLookup.cs` nếu cần | Helper nhỏ cho state/replay, cache tên có giới hạn |
| `HeroChess.Api/Services/WebSocketEndpoint.cs` | Authorize nhẹ cho command; subscription vẫn trả state đúng người xem |
| `HeroChess.Api/Services/BotTurnScheduler.cs` | Vòng worker cố định, lease theo trận, scope và shutdown |
| `HeroChess.Api/Services/BotWorkQueue.cs` | Multi-reader, dedup và cleanup đúng khi chạy nhiều worker |
| `HeroChess.Api/Services/MatchModels.cs`, `Program.cs`, `appsettings.json` | Option worker, validate 1–2, đăng ký lookup/config |
| `HeroChess.Api/Services/BotTelemetry.cs` nếu cần | Chỉ bổ sung số đo chưa có |
| `frontend/src/Match.jsx` | Refresh theo sự kiện, polling dự phòng, legal theo version |
| `tests/HeroChess.IntegrationTests` và test web hiện có | Query count, quyền riêng tư, queue/concurrency/Undo/reconnect |
| `tests/live/capacity-smoke.mjs` dự kiến | Harness tải có giới hạn, workload và cleanup |
| `docs/api.md`, `docs/UNITY_STATE_SYNC_HANDOFF.md` | Quy trình WS/polling, tương thích và checklist FE |

Không phải mọi file đều bắt buộc sửa: dùng helper hiện có nếu đủ và giữ diff nhỏ. Không sửa `BotEngine`, Rules hoặc snapshot schema chỉ để thêm worker. `MatchCommandService` và maintenance là đường cần kiểm chứng tương thích; không viết lại nếu test cho thấy đã đáp ứng.

## Triển khai và quay lại cấu hình cũ

1. Deploy BE tối ưu với một worker; client cũ tiếp tục dùng API cũ.
2. Kiểm tra state, command, bot, Undo, skill và settlement; không chạy SQL mới.
3. Cập nhật web test, bàn giao Unity và xác nhận nhóm FE đã kiểm chứng cách đồng bộ mới.
4. Deploy khả năng chạy hai worker nhưng giữ `BotWorkerCount=1` cho đến khi đo đạt.
5. Nếu hai worker đạt tiêu chí, đổi `MatchRuntime__BotWorkerCount=2` trong `~/herochess.env` và tạo lại container trong cửa sổ bảo trì; trận pending tiếp tục được recovery/maintenance xử lý.
6. Nếu có vấn đề, trả về `1` và tạo lại container; nếu lỗi ở code thì quay lại image đã biết tốt. Không hứa rollback không gián đoạn kết nối.

Handoff Unity dùng API/event hiện có và có thể làm độc lập. Chỉ deploy BE sẽ chưa đạt phần giảm polling của Unity; thay đổi cache/worker vẫn có hiệu lực với client cũ.

## Kết quả bàn giao khi triển khai xong

- BE và web test đã sửa, kèm các kiểm chứng liên quan.
- Hướng dẫn Unity có luồng, trường hợp lỗi và tiêu chí nghiệm thu; không kèm thay đổi source Unity.
- Bảng so sánh query count, request rate, latency, queue và bộ nhớ trước/sau.
- Giá trị worker được chọn cho VM 1 GB và sức tải theo từng workload đã đo.

BE và web test đã triển khai theo phạm vi trên. Source Unity, cấu hình Azure và Supabase production chưa thay đổi trong đợt này; không có migration SQL mới.

Đã bổ sung lớp bảo vệ lưu lượng: quota theo account/IP dùng chung REST và WebSocket, giới hạn socket/ticket/subscription, slot xử lý không xếp hàng chờ, proxy tin cậy và phản hồi `429`/`Retry-After`. Bootstrap dùng connection riêng để không giữ khóa session trong pool; lỗi HTTP quá kích thước giữ đúng `413`. Cấu hình, kiểm chứng và bàn giao Unity ghi trong [runbook](CAPACITY_1GB_RUNBOOK.md) và [API mục 6.1](api.md#61-giới-hạn-tài-nguyên-và-retry). Đây là bảo vệ ở một API process; sức tải tối đa vẫn cần đo trên VM.
