# Kế hoạch bot 3 cấp, thống kê và quan sát vận hành

Ngày: 2026-11-03. **Chỉ là kế hoạch; chưa triển khai.** Không tách MSS hoặc bot thành service game riêng. API .NET hiện tại tiếp tục sở hữu luật, bot, matchmaking và ghi trận. Supabase giữ dữ liệu nghiệp vụ. VM Azure 1 GiB không chạy thêm Prometheus/Grafana/database giám sát tại chỗ.

Tiến độ sau khi người dùng cho triển khai: đã bắt đầu phần metrics/baseline, xem [BOT_IMPLEMENTATION_PROGRESS.md](BOT_IMPLEMENTATION_PROGRESS.md). Câu “chưa triển khai” phía trên mô tả thời điểm lập plan; không dùng nó để suy ra trạng thái source hiện tại. Baseline local chưa thay cho gate đo trên Azure/Supabase và chưa chứng minh tải 100 trận.

Review lần 1: 2026-11-03, đối chiếu source BE và workflow deploy. Các điều kiện chống bug ở cuối tài liệu là gate bắt buộc khi triển khai. Thứ tự thực thi đã chỉnh: **Đợt 0 → 1 → 3 → 2 → 4 → 5**, để có metrics trước khi tăng chi phí search. Số thứ tự các đợt giữ nguyên để tiện đối chiếu bản đầu.

Review Astra lần 2: 2026-11-03. Đã sửa thêm ranh giới search/luật, rollout mặc định, version dùng cho thống kê, cancel nội bộ, deadline và cách tính tải thất bại. Thay đổi tài liệu không đồng nghĩa source đã có các bảo vệ này.

Điều chỉnh thực thi theo yêu cầu người dùng: ưu tiên **bot 3 cấp trước Grafana**. Đã triển khai/test phần BE bot với profile v1 nhỏ và time budget mềm; chưa chờ dashboard để làm bot. Các gate đo capacity/deploy thật vẫn phải xác nhận, xem BOT_IMPLEMENTATION_PROGRESS.md và BOT_API.md.

## Mục tiêu và ranh giới

- Người chơi chọn `easy`, `medium`, `hard`; BE xác thực, lưu cấp và phiên bản chiến thuật trong `match_participant.bot_config`, trả lại cấp để FE hiển thị sau reconnect.
- Bot chỉ dùng thông tin được phép biết theo phe, tạo nước hợp lệ qua cùng command pipeline; Command skill được xét theo luật hiện hành. Không hồi sinh Hero skill/trait.
- Đo tỷ lệ thắng theo cấp bot, phiên bản bot, Elo người chơi **lúc tạo trận**, phe và phiên bản nội dung. Dữ liệu trận là nền để phân tích khai cuộc sau này; chưa huấn luyện model hoặc tuyên bố Elo bot đã chuẩn hóa.
- Đo queue, đọc DB, chọn nước, commit, phát thông báo, tổng thời gian; nhìn được p50/p95/p99, lỗi, CPU/RAM và tải DB. Số đo vận hành không được ghi thành một row Supabase mỗi nước.
- Giữ bot match không cộng Elo/coin như hiện tại; không đổi ranked, Unity, luật thưởng hoặc dữ liệu Hero skill.

## Hiện trạng đã kiểm tra

- `MatchmakingService.CreateAsync` chỉ nhận `ranked`/`bot`; bot có `BotConfig={strategy:"capture_sp_v1"}`, `EloBefore=1000` dù đó chưa phải sức mạnh được đo. Ticket trong RAM.
- `BotTurnScheduler` có một reader và queue không giới hạn; ưu tiên ăn quân theo SP. `BotThinkMilliseconds=250` chỉ ghi warning, không cắt tính toán. `MatchMaintenanceHostedService` mỗi giây có thể schedule lại các lượt đến hạn; `_pending` gộp match/version.
- `MatchCommandService` đã ghi timing của gate/read/rules/save/commit. `match_action` lưu action, sequence, `state_after`, timestamp; `game_match` lưu kết quả; participant lưu Elo ban đầu và `bot_config`.
- `MatchRecoveryHostedService` **hủy trận selecting/active khi restart**. Workflow deploy tự động có thể restart API giữa trận. Đây là gate vận hành trước khi bật nhiều trận bot thật.
- Supabase là DB từ xa; mọi truy vấn/tổng hợp thêm cần đo thời gian và không nằm trong đường đi nước cờ.

## Luồng dữ liệu đích

```text
FE chọn cấp → POST /api/v1/matchmaking/tickets {mode:"bot",difficulty:"medium"}
  → BE validate → participant.bot_config {difficulty,strategyVersion}
  → queue bot → chọn action → MatchCommandService → match_action/game_match
                           ↘ .NET Meter / structured log → backend giám sát được quản lý

game_match + participant + match_action → truy vấn/batch ngoài đường chơi
  → báo cáo thắng/hòa/thua, Elo đối thủ, khai cuộc (giai đoạn sau)
```

Không có bot account hoặc bảng bot mới. Bot Elo *ước tính* là báo cáo riêng, không dùng `EloBefore=1000` hiện tại làm bằng chứng sức mạnh. Metric chỉ có nhãn ít giá trị (`difficulty`, `strategyVersion`, `result`, `stage`); `matchId` chỉ xuất hiện trong log chẩn đoán có kiểm soát, không gắn vào metric. Không xuất state ẩn, token, email, connection string hoặc raw action sang hệ giám sát.

## Đợt 0 — Baseline và cổng deploy

1. Trên DB test, thêm trước các timer tối thiểu của Đợt 3 vào bot legacy để đo 30 lượt bot hiện tại và 30 nước PvP: queue-to-start, chọn nước, command DB, end-to-end BE, CPU/RSS, DB pool wait. Dashboard/exporter hoàn thiện ở Đợt 3; không chờ có chiến thuật mới mới đo baseline. Trên FE, đo riêng click-to-display khi có người phụ trách client; không dùng đồng hồ khác máy để trừ trực tiếp.
2. Ghi commit/image, cấu hình Azure VM, Supabase region/pool, RAM còn trống; xác nhận 100 user nghĩa là bao nhiêu bot turn đến đồng thời.
3. Chốt lịch deploy không có trận active hoặc xử lý recovery/resume bằng kế hoạch riêng trước khi cho GitHub Actions deploy giữa giờ chơi. Không lặng lẽ bỏ cơ chế hủy trận hiện tại.
4. Dùng DB test/clone đã ẩn thông tin cho load test; không tạo hàng trăm trận trên Supabase production.

**Gate:** có baseline p50/p95 và biết lúc API restart chuyện gì xảy ra với trận; không triển khai bot khó trước gate này.

## Đợt 1 — Hợp đồng cấp bot và dữ liệu bền vững

**Files:** `HeroChess.Contracts/ApiContracts.cs`, `HeroChess.Api/Services/MatchmakingService.cs`, `HeroChess.Api/Services/MatchReadService.cs`, DTO selection/state/history phù hợp, `tests/HeroChess.IntegrationTests/MatchFlowTests.cs`.

1. DTO nhận `string? Difficulty = null`; chỉ áp mặc định sau khi mode được xác nhận là bot. Trong rollout, request bot thiếu/null difficulty tiếp tục dùng `capture_sp_v1`; chỉ chuyển mặc định sang `medium` sau khi medium đã qua gate và được bật. Ranked thiếu/null difficulty vẫn hợp lệ, ranked gửi một giá trị difficulty bị trả 400. Bot nhận chính xác `easy|medium|hard`, bỏ qua hoa/thường và khoảng trắng hai đầu; chuỗi rỗng/giá trị khác trả 400. Cấp hợp lệ nhưng chưa bật trả 503 `BOT_STRATEGY_UNAVAILABLE`, không lén thay bằng cấp khác. Mode null/rỗng trả 400 thay vì gọi `Trim()` gây 500. Release notes nêu lúc đổi mặc định; response legacy có difficulty null, không gắn nhãn medium cho chiến thuật cũ.
2. Lưu `{schemaVersion:1,difficulty,strategyVersion,searchProfileVersion,rulesEngineVersion}` trong `BotConfig` tại lúc tạo trận; budget/node limit/seed policy thuộc profile bất biến có phiên bản, không đổi hành vi trận cũ khi sửa global config. Đọc từ DB khi worker thực thi, không nhận các tham số search từ FE. Chỉ dạng legacy đã biết `strategy=capture_sp_v1` mới chạy hành vi cũ; config null/hỏng/version lạ không được âm thầm coi là medium hoặc legacy. Strategy/profile không hỗ trợ phải làm tạo trận trả 503 trước khi tạo reservation/ticket hoặc phải dọn sạch chúng nếu validation diễn ra sau. Kiểm tra config của trận đang tồn tại trước rollout, chặn rollout nếu còn config không đọc được. Config hỏng xuất hiện lúc runtime xử lý bằng đường cancel nội bộ tại R4, không giả lập bot resign hoặc lặp search vô hạn. Tất cả profile/version đọc từ DB phải validate phạm vi node/depth/time; config hỏng không được cấp search vô hạn.
3. Trả cấp bot trong DTO phía bot của selection/state/history để FE hiển thị sau reconnect. Không làm thay đổi dữ liệu theo phe hoặc lộ state ẩn.
4. Test: bot mặc định, 3 cấp hợp lệ, cấp sai/ranked kèm cấp, persist và đọc lại sau tạo scope mới, trận bot cũ, history; test PvP hiện có vẫn pass.

**Gate:** round trip API → DB → API nhất quán; chưa cần FE Unity trong PR BE. FE chỉ gửi mã cấp đã chọn và hiển thị trường BE trả.

## Đợt 2 — Ba chiến thuật có trần tài nguyên

**Files:** `HeroChess.Api/Services/BotTurnScheduler.cs`, helper chọn nước trong `HeroChess.Api/Services` nếu cần, `HeroChess.Api/Services/MatchModels.cs`, unit/integration tests của bot.

1. Tách chọn action khỏi worker để test được. `easy`: chọn nước hợp lệ có ngẫu nhiên có seed theo trận/turn để tái hiện được. `medium`: chấm điểm 1 ply theo ăn quân, mất quân, an toàn Tướng và Command skill hợp lệ. `hard`: tìm kiếm có trần số node/thời gian và fallback về nước hợp lệ đã biết; chỉ tăng depth khi đo thấy còn ngân sách.
2. Mỗi cấp xét Command skill như candidate hợp lệ khi luật cho phép, không chỉ khi hết nước thường. Candidate generator có giới hạn cho từng skill/target; `FindLegalSkillAction` hiện chỉ trả nước đầu tiên và thử một tập target hạn chế, không phải danh sách skill actions đầy đủ để search. Không dùng trạng thái ẩn của phe người chơi để chấm nước bot; tuân thủ gate thông tin và lifecycle ở phần review.
3. Search có deadline đơn điệu, node limit và cancellation được kiểm tra bên trong vòng sinh/chấm action, kể cả target skill; không dùng `Task.WhenAny` để bỏ mặc CPU search chạy nền. Budget search không được dùng để hủy transaction ghi nước, nhưng shutdown/timeout DB trước commit vẫn phải rollback như pipeline hiện tại. Có fallback hợp lệ từ root trước khi tìm sâu; nếu root generation chưa thể dừng giữa chừng thì budget chỉ là soft limit và chưa được quảng cáo là trần cứng. Queue bounded và recovery theo gate review; giữ một consumer cho tới khi có benchmark chứng minh cần tăng.
4. Test: mỗi cấp luôn gửi action hợp lệ; tình huống Command skill; hết budget vẫn đi được; stale version/restart/retry không đi hai lần; state bí mật không ảnh hưởng quyết định; cùng seed/state/version và cùng node limit cho cùng lựa chọn. Với wall-clock cutoff, dùng đồng hồ/budget giả trong test để không phụ thuộc tốc độ máy.

**Gate:** p95 bot turn và RAM trong ngân sách ở tải thấp; không gọi cấp khó là một mốc Elo cụ thể khi chưa hiệu chuẩn.

## Đợt 3 — Metrics và dashboard vận hành

**Files:** `BotTurnScheduler`, `MatchCommandService` (chỉ bổ sung mốc còn thiếu), `Program.cs`, cấu hình production và test nhỏ cho instrumentation.

1. Dùng `System.Diagnostics.Metrics`: histogram `bot.queue_wait`, `bot.state_read`, `bot.search`, `bot.command`, `bot.total` (đơn vị giây). `total` = từ enqueue được chấp nhận đến khi `ExecuteBotAsync` trả về; `command` gồm gate/DB/rules/broadcast/settlement theo luồng hiện tại. Đo `db.commit` ngay tại `CommitAsync`, không đặt tên toàn bộ command là commit. Counter outcome chỉ `success|stale|failed|cancelled|expired`; `fallback` là thuộc tính/counter riêng vì một lượt fallback vẫn có thể thành công. Gauge waiting và in-flight tách nhau. Dùng `Stopwatch` trong một tiến trình. Job maintenance phục hồi tín hiệu bị mất trong cùng process được đánh dấu `source=maintenance`; hiện restart hủy trận nên **không có resume job sau restart**. Chỉ thêm đo resume khi có thiết kế recovery riêng.
2. Tận dụng HTTP/runtime metrics .NET; log có `matchId`, version và lỗi để điều tra một trận, giới hạn retention. Không query DB từ callback metric.
3. Xuất metrics ra dịch vụ được quản lý, ưu tiên Grafana Cloud qua OTLP **nếu chọn nhà cung cấp này**; Azure Application Insights là phương án thay thế. Không tự host collector trên VM 1 GiB ở đợt đầu. Credential chỉ ở env VM/GitHub secret; giới hạn dữ liệu gửi và ngân sách. Nếu exporter lỗi, gameplay vẫn chạy.
4. Dashboard: p50/p95/p99 tổng/queue/search/DB theo difficulty; pending, stale, failed, fallback; HTTP p95/5xx, CPU/RSS, GC, DB pool. Alert khi p95 total, queue age, lỗi hoặc RAM vượt ngưỡng liên tục; threshold lấy từ baseline, không chọn số tùy ý.
5. Test: đo một turn thành công và một turn stale có metric đúng; không có ID người chơi trong labels; exporter mất mạng không cản nước đi.

**Gate:** dashboard phân biệt được bot chờ queue, nghĩ lâu hay Supabase chậm; metric không làm tăng đáng kể p95/RAM trên VM.

## Đợt 4 — Báo cáo sức mạnh và nền telemetry khai cuộc

**Files:** SQL/report script mới dưới `docs` hoặc `tools` theo cách chạy được chọn, test truy vấn trên DB test; API admin chỉ thêm nếu thực sự cần dashboard trong app.

1. Báo cáo batch từ `game_match` + `match_participant`: chỉ trận bot `completed` và đã `settled`; nhóm theo difficulty, strategyVersion, searchProfileVersion, rulesEngineVersion, contentVersion, ruleset snapshot, Elo người chơi lúc ghép, phe bot, loại kết thúc. `CatalogVersionService` chỉ hash catalog hero/skill, không hash code luật hoặc TurnSeconds/ActionLimit: cùng contentVersion chưa đủ để gộp. Dùng ruleset snapshot JSONB đã lưu để phân biệt cấu hình luật ở bản đầu, không tạo thêm hash chưa có quy tắc canonical. Xuất games/win/draw/loss/win-rate và khoảng tin cậy; các trận `cancelled`/`server_restart` báo riêng. Hiển thị counts cả khi dưới 30 trận và gắn `chưa đủ mẫu`, không ẩn toàn bộ dữ liệu.
2. Chạy định kỳ ngoài đường request chơi và có phạm vi thời gian. Ban đầu SQL read-only từ Supabase đủ; chỉ thêm bảng aggregate theo ngày khi truy vấn đo được là quá chậm. Không ghi một row thống kê mỗi action.
3. Giai đoạn sau: trích xuất *nước có hiệu lực* từ `match_action`, xử lý undo/replay, chuẩn hóa thế cờ/nước đi theo side, ruleset/contentVersion và nhóm Elo; kiểm tra quyền riêng tư và làm sạch trước khi dùng để gợi ý nước cho bot. Chưa train model hoặc dùng dữ liệu khai cuộc để đổi bot ở đợt này.
4. Test: trận thắng/hòa/thua, hủy, `server_restart`, undo, Elo đổi sau trận, hai phiên bản chiến thuật và bản luật khác nhau cho ra nhóm đúng.

**Gate:** báo cáo có mẫu số rõ và tái tạo được từ lịch sử; không làm chậm command path.

## Đợt 5 — Capacity, rollout và rollback

- Test trên DB riêng: 2 → 10 → 20 → 40 → 100 trận bot, cả tải phân tán và burst cùng đến lượt; mỗi mức ít nhất 10 phút. Ghi p95 queue/total, lỗi, RSS/CPU, DB connection wait và số trận phục hồi. Dừng khi queue age tăng liên tục, 5xx tăng hoặc RAM sát giới hạn; không lấy số người chỉ đang mở socket làm công suất bot.
- Đặt SLO sau baseline; mục tiêu thử ban đầu: p95 bot total ≤ 2 giây ở tải đã chứng minh, không OOM, không duplicate action. Nếu 100 trận không đạt trên VM 1 GiB, giảm search budget/concurrency hoặc nâng tài nguyên sau khi biết nút thắt; **không tách MSS/service riêng** trong kế hoạch này.
- Rollout theo cấp: easy → medium → hard. Feature flag chỉ chọn strategy cho **trận tạo mới**, không lén đổi strategy đang frozen. Muốn quay lại `capture_sp_v1`, ngừng tạo trận mới rồi drain selecting/active trước; image đích phải đọc được toàn bộ config đang còn hiệu lực. Không coi schema JSON tương thích là đủ: image cũ có thể bỏ qua strategy mới và làm sai nhãn thống kê. Không chạy song song hai API dùng cùng DB.

## Điều kiện chống bug bổ sung sau review

### R1 — Request và config có version không được làm hỏng client/rollback

- Trước khi sửa chữ ký `MatchmakingService.CreateAsync`, tìm mọi caller/controller/test; áp default ở service theo mode, không đặt default medium lên request của mọi mode. DTO response thêm trường nullable, giữ các field cũ và cùng ý nghĩa ở `/selection`, `/state`, ACK snapshot và history/detail. Không yêu cầu FE gửi `strategyVersion` hoặc server budget.
- Test gate: `{mode:"ranked"}`, `{mode:"ranked",difficulty:null}`, bot cũ không có difficulty, mode null, difficulty rỗng/sai, config legacy/hỏng/version không hỗ trợ; test reconnect/ACK/history hiển thị cùng cấp. Trận thử tải/synthetic phải đánh dấu nguồn test trong DB test, không lọt vào báo cáo người chơi thật.

### R2 — Search phải mô phỏng đúng nước thật, không sửa state gốc

- `XiangqiRulesEngine.ApplyMove` chưa làm toàn bộ việc của `MatchCommandService`: skill phải tăng version/turn/count, đổi phe; cả move/skill phải chạy `TurnLifecycle` đúng một lần, rồi checkmate/action limit theo ruleset snapshot. `FrozenSkillSnapshotFactory.FromSkillState` mặc định cooldown bằng 0, nên search phải lấy cooldown từ frozen lineup như command thật, không dùng mặc định đó.
- Tách phần chuyển state thuần đang có thành helper dùng chung cho search và nhánh command liên quan, không gọi DB/lock/broadcast/settlement trong node search. Giữ authorize/version/dedup/transaction tại command service. Không viết lại một bộ luật cho bot. Đây là thay đổi cần regression PvP dù mục tiêu là bot.
- Mỗi nhánh dùng state clone độc lập, không chia sẻ collection mutable giữa các node. Khi hết budget chỉ dùng kết quả của depth đã hoàn tất hoặc root fallback. Terminal win/loss/draw được xét trước material; không để bot hy sinh Tướng để lấy SP hoặc bỏ qua `ActionLimit`.
- Candidate list của search có thể bị cắt vì budget/top-K; kết quả phải phân biệt `complete`, `budget_exhausted` và `no_legal_action`. **Không dùng danh sách đã cắt làm bằng chứng checkmate/no-legal-actions**, và không thay `FinishTurn` bằng generator top-K của bot. Nếu chưa kiểm đủ thì node là chưa xác định, không chấm thua/chốt trận. Logic kết thúc trận của command phải giữ hành vi chính xác hiện hành; thêm search không được vô tình thu hẹp tập Command skill cứu nguy.
- Các handler Thành/Rào/Vạn Cọc/Bình Lâm tạo `Guid.NewGuid()` trong mô phỏng. Không dùng ID mới ngẫu nhiên làm thứ tự ưu tiên, score hay khóa trạng thái cho search; dùng khóa ngữ nghĩa ổn định (side/slot/tọa độ/CreationOrder). Test so state search với state thật phải chuẩn hóa ID mới **và các tham chiếu đến chúng**; state gốc vẫn kiểm byte-equivalent. Action gửi đi chỉ được tham chiếu ID có thật ở root, không gửi ID hiệu ứng chỉ tồn tại trong nhánh giả định.
- Test gate: một move và một skill search cho state tương đương command thật (bỏ timestamp/ID event), cooldown hết hạn, Khiên/Thành/Rào, hiệu ứng đổi chủ, action limit, không còn legal action và skill cứu nguy; serialize state gốc trước/sau search phải giống nhau. Dùng test hiện có trong `tests/HeroChess.Rules.Tests` và `MatchFlowTests`, không dựng framework mới.

### R3 — Bot observation khác state có thẩm quyền; không dùng projection UI làm luật

- `MatchStateProjection.State` hiện xóa cả metadata Hero cũ và cọc ẩn, không phải hàm bảo đảm tạo `GameState` để engine search. Chỉ biến đổi **bản clone dành cho quyết định** theo thông tin bot được biết; giữ state đầy đủ ở command pipeline để xác thực/resolve thật. Không sửa projection HTTP để phục vụ bot. Với legacy state có bí mật ảnh hưởng luật mà observation không thể biểu diễn đủ, chưa mở strategy mới cho loại state đó trước khi có chính sách hợp lệ.
- Mọi nơi truy cập state trong search (candidate generation, đánh giá, fallback, seed, cache) đều phải dùng observation nhất quán. Nếu action từ observation bị authoritative engine từ chối, refresh version và retry có giới hạn, không loop thử mọi ô để dò dữ liệu ẩn; log chỉ mã lỗi và version.
- Test gate: hai state có cùng observation nhưng khác bí mật đối thủ cho cùng quyết định khi cùng seed; authoritative commit vẫn xử lý đúng va chạm/hiệu ứng. Chỉ áp test bất biến lên bí mật thực sự tồn tại trong luật của state đang xét, không giả định Hero skill còn hoạt động ở trận mới.

### R4 — Queue đầy, retry và deadline không được làm mất lượt hoặc khóa worker

- Dùng bounded channel với `TryWrite` không chờ trong `Schedule`: nếu đầy/đóng, gỡ ngay key `_pending` vừa thêm; nếu không maintenance sẽ không bao giờ enqueue lại key đó. Đếm dedup, queue-full, stale và retry riêng. Key pending bao gồm in-flight để sweep không tạo duplicate; cleanup trong `finally`, kể cả cancellation/exception. Xử lý completion/shutdown trả gauges về trạng thái đúng.
- Không `await WriteAsync` khi đang giữ gate trận: command hiện gọi `Schedule` dưới gate, còn worker có thể đang chờ chính gate đó. Một worker giữ scope/DbContext riêng; không share DbContext qua task song song, không giữ row transaction hoặc gate suốt thời gian search. `expectedVersion` được kiểm lại khi commit; timeout thắng trước thì job bị bỏ, không reset deadline cho bot.
- Sweep recovery cần batch có giới hạn **và tiến qua các trận công bằng**, không lặp `Take(N)` cùng những trận đầu. Retry DB lỗi có backoff hữu hạn, không spam mỗi giây mãi. Deadline search phải chừa thời gian cho command/DB; queue đầy phải thể hiện là thiếu năng lực, không che bằng tăng turn deadline. Admission limit nếu thêm phải kiểm tra/tăng reservation nguyên tử để request đồng thời không vượt trần.
- `receivedAt` của bot là thời điểm gửi **nước đã chọn** vào command, không phải timestamp enqueue/start search. Nếu dùng enqueue timestamp, bot có thể tính quá hạn mà vẫn qua deadline guard hiện tại. Đọc deadline cùng version khi lấy job; nếu đã hết hạn thì bỏ search, để timeout worker giải quyết. `TURN_DEADLINE_PASSED` là outcome riêng `expired`, không retry lại cùng version hoặc log như lỗi engine.
- Hiện `MatchSelectionLifetime.CancelAsync` chỉ hủy selecting, còn command công khai từ chối `cancel`; chưa có API hủy active dùng được cho lỗi bot. Đợt 2 phải thêm method **nội bộ** `CancelBotFailureAsync(matchId, expectedVersion, ct)` ở `MatchCommandService`: lấy gate, khóa match rồi state, chỉ bot match đang active đúng version và vẫn có config không hỗ trợ mới hủy; append cancel action, tăng version, xóa deadline, lưu reason `bot_configuration_error`, commit rồi broadcast/settle không thưởng để release player. Nếu version/status đã đổi thì no-op. Không thêm loại cancel cho client hoặc nới authorize của HTTP/WS. Có test lặp cancel, race với move/timeout, rollback DB failure và player queue lại được sau settlement.
- Test gate: capacity=1 có queue-full rồi maintenance enqueue lại thành công; Schedule khi worker đang chờ gate không deadlock; cùng match/version không đi hai lần; undo trong lúc bot tính; timeout/đầu hàng trước commit; lỗi DB không mất pending vĩnh viễn hoặc tạo busy-loop; một trận lỗi không chặn tất cả trận còn lại vô hạn.

### R5 — Số đo phải chỉ rõ đang đo gì và không tự gây OOM

- Timer enqueue được ghi khi nhận job thành công, không reset vì tín hiệu schedule trùng. Queue waiting giảm khi dequeue, in-flight tăng/giảm một lần. Đếm success khi action mới commit, không đếm retry dedup là nước mới; lỗi sau commit/broadcast phải phân biệt với commit thất bại.
- `MatchAction.CommittedAt` hiện được gán **trước** SaveChanges/Commit, nên không lấy chênh timestamp đó làm thời gian commit chính xác. `bot.total` không bao gồm client fetch/render, và queue wait không bao gồm khoảng tín hiệu schedule bị mất. Báo cáo client cần đo riêng; không cộng p95 của từng đoạn để suy ra p95 tổng.
- `bot.total` chỉ có cho job hoàn tất nên không đủ chứng minh SLO: khi channel đầy, nhiều lượt không được enqueue và sẽ không xuất hiện trong histogram này. Harness test phải theo dõi từ mỗi lượt bot trở nên cần xử lý đến commit/timeout/cancel, kể cả job bị từ chối; dashboard kèm queue-full, oldest due age, expired, count completed/expected. Dùng một tập theo match/version trong harness để không đếm mỗi lần sweep thành một lượt mới. Timestamp `state.UpdatedAt` chỉ cho số đo due-age gần đúng (ghi trước commit); công bố rõ sai số, không đổi tên thành thời gian chuẩn. N=0 hoặc toàn lỗi phải báo thiếu dữ liệu/thất bại, không coi là đạt latency target.
- Histogram labels và bucket có giới hạn; exporter có buffer/batch hữu hạn, timeout gửi, drop counter và circuit/backoff khi mất mạng. Log không chứa query params chứa credential hay state ẩn; xoay log Docker để disk không đầy. Bắt đầu bằng metrics và error log, chỉ mở trace sampling khi cần.
- .NET RSS/heap không thay cho RAM toàn VM: lấy CPU host, disk, RAM khả dụng, restart/OOM và CPU credits của VM burstable từ công cụ host/Azure phù hợp. Đo trên tải lâu để không kết luận dựa vào CPU burst ban đầu. Exporter tắt hoặc mất mạng không được tăng queue/RAM không giới hạn.
- Test gate: success/stale/cancel/queue-full/fallback+success đếm đúng; mất endpoint metrics trong 10 phút không gây memory growth liên tục; kiểm tra dashboard có data, không coi không có dữ liệu là latency=0.

### R6 — Thống kê không được nhầm luyện tập với sức mạnh thật

- Mỗi trận được tính đúng một lần từ kết quả đã settle; dùng khoảng UTC `[from,to)`, không đếm join từng action thành nhiều trận. Elo bucket cố định 200 điểm (ví dụ `[800,1000)`), null/unknown và legacy strategy để nhóm riêng. Mẫu gồm win/draw/loss; `winRate=W/N`, `scoreRate=(W+0.5D)/N`; khoảng tin cậy win rate dùng Wilson 95%. Mốc 30 chỉ là cờ ít mẫu, không phải chứng nhận Elo chính xác.
- Wilson ở đây là khoảng mô tả theo trận với giả định mẫu độc lập; nếu một người chơi lặp nhiều trận thì không diễn giải thành độ tin cậy hiệu chuẩn Elo. Khi hiệu chuẩn thật, kiểm tra trên người chơi/phiên độc lập, hoặc bootstrap theo player; dữ liệu chọn độ khó tự nguyện không phải thử nghiệm so sánh ngẫu nhiên giữa các bot. Tách rulesEngineVersion/searchProfileVersion và ruleset snapshot như Đợt 4 để không trộn sức mạnh sau đổi budget/luật.
- Bot hiện luôn đen và sao chép lineup người chơi (`MatchSelectionService`); báo cáo cần ghi rõ giới hạn này, không suy ra sức mạnh đối xứng hai phe/đội hình. Hiển thị số người chơi riêng biệt, distribution Elo, reason timeout/resign và tỷ lệ hủy cùng tỷ lệ thắng. Trận có undo tách nhóm `assisted`, không đưa vào nhóm chính dùng hiệu chuẩn/học khai cuộc. Elo khởi đầu chung 1000 không chứng minh mọi người ở nhóm đó có sức mạnh tương đương; chưa tự động map mức khó theo số Elo này.
- Batch có read-only credential, statement timeout và phạm vi ngày; thiếu quyền/timeout báo lỗi rõ. API thống kê/admin hoặc export sau này phải kiểm tra role và không gửi raw lịch sử/state của người khác ra FE. Aggregate nếu thêm phải upsert/recompute idempotent và đọc bù trận settle muộn, không chỉ dùng cursor endedAt làm mất kết quả.
- `match_action.state_after` là snapshot lớn trên mỗi action: đo tốc độ tăng dung lượng, quota Supabase và chính sách backup/retention trước tải 100 trận. Không xóa action để tiết kiệm khi chưa có archive và kiểm chứng replay/undo; bản aggregate không thay thế dữ liệu gốc.
- Test gate: nhiều action không tăng số trận; range cận UTC/Elo, settle muộn, batch chạy hai lần, mixed strategy/lineup/undo và số người chơi lặp lại được báo đúng. Đối chiếu tổng W+D+L=N bằng một fixture SQL nhỏ.

### R7 — Gate deploy phải chặn request mới, không chỉ nhìn một lần rồi restart

- Trước rollout/rollback: bật maintenance ngăn tạo ticket/chọn trận mới, drain cả `selecting` và `active`, rồi mới dừng API; nếu chưa có maintenance gate thật thì deploy thủ công trong khung đóng game, không hứa zero-downtime. Một truy vấn đếm trận bằng 0 trước deploy không loại được race người chơi vừa tạo trận.
- Workflow hiện tự deploy mỗi push master; phải chuyển sang deploy có gate/manual theo kế hoạch vận hành trước khi mở cho nhiều người. Readiness DB khỏe không chứng minh strategy/config mới tương thích và không khôi phục các trận đã bị startup recovery hủy. Rollback cũng chạy startup recovery; ưu tiên canary trên DB test, không chạy hai instance chung DB để thử image.
- Seed tái hiện dùng thuật toán hash/PRNG có version, input gồm matchId + state version + strategy version, thứ tự candidate ổn định; không dùng `string.GetHashCode`/global Random làm cam kết tái hiện qua process. Trận undo tăng version nên lịch sử mới là một lần quyết định mới.
- Test gate: maintenance chặn ticket mới, không restart khi còn selecting/active, strategy lạ bị phát hiện trước public traffic; rollback không đưa trận mới sang engine legacy mà vẫn giữ nhãn hard. Test cùng state/seed qua hai process cho cùng kết quả khi profile dùng node limit cố định; profile bị cắt bởi wall-clock có thể khác độ sâu và phải ghi depth/nodes/fallback, không cam kết action luôn giống nhau.

## Quyết định trước khi code

Các regression bổ sung từ review Astra phải nằm trong test của đợt sở hữu logic:

- Đợt 1: rollout chỉ bật easy vẫn phục vụ request legacy không difficulty; medium/hard disabled trả 503 và không để lại ticket/reservation khiến player bị `PLAYER_BUSY`.
- Đợt 2: cutoff trước khi tìm thấy skill cứu nguy trả `budget_exhausted`, không chốt thua; root action không tham chiếu Guid mới ở node con; đổi Guid hiệu ứng giả định không đổi thứ tự/score ngữ nghĩa. Test deadline: enqueue trước hạn nhưng hoàn tất search sau hạn phải bị từ chối; không backdate `receivedAt`.
- Đợt 2: cancel bot configuration error đúng một lần, giữ action log, không thưởng/đổi Elo và giải phóng player; client thường vẫn không gửi được cancel cho trận active.
- Đợt 3/5: burst vượt capacity tạo queue-full/expired trong báo cáo; không thể pass capacity gate bằng p95 của vài lượt thành công. Test không có sample phải hiển thị unknown/fail.
- Đợt 4: cùng catalog nhưng khác ActionLimit hoặc searchProfileVersion thành hai nhóm báo cáo; số trận cùng một player lớn không được mô tả như hàng trăm người độc lập.

1. Chốt bên nhận metrics (Grafana Cloud hay Azure Application Insights), quota/retention và ngân sách. Chuẩn `Meter` trong BE cho phép đổi nơi nhận sau này.
2. Chốt mức ngân sách search và tiêu chuẩn chấm medium/hard sau baseline; không gắn nhãn Elo bot khi chưa có dữ liệu hiệu chuẩn.
3. Chốt chính sách deploy khi còn trận active, vì recovery hiện hủy trận. Đây là điều kiện vận hành, không phải hạng mục MSS.

Nguồn tham khảo: [.NET metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation), [Grafana Cloud OTLP](https://grafana.com/docs/grafana-cloud/send-data/otlp/send-data-otlp/), [Azure Monitor OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-applicationinsights).
