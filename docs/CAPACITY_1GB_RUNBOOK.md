# Vận hành tối ưu sức tải trên VM 1 GB

BE đã tối ưu đường đọc state và hỗ trợ một hoặc hai worker bot trong cùng process. Web test trong `/frontend` đã dùng WebSocket kết hợp polling dự phòng. Unity cần nhóm FE áp dụng [handoff đồng bộ](UNITY_STATE_SYNC_HANDOFF.md); chỉ deploy BE không đổi nhịp polling của bản Unity đang cài trên máy người chơi.

## Cấu hình sau khi deploy

Mặc định giữ **một worker** cho VM 1 GB. Không cần thêm biến env nếu dùng mặc định. Không có migration SQL mới cho đợt tối ưu này.

```ini
MatchRuntime__BotWorkerCount=1
MatchRuntime__BotQueueCapacity=128
```

`BotWorkerCount` chỉ nhận 1 hoặc 2; cấu hình khác bị từ chối khi khởi động. Queue có giới hạn, job trùng match/version được gộp. Một trận tối đa một job bot đang xử lý; các trận khác nhau có thể chạy đồng thời với hai worker. Undo, resign và timeout vẫn qua pipeline kiểm tra version/khóa trận hiện có.

Không tăng queue hoặc connection pool theo số người chơi. Mức pool 10 hiện có là điểm bắt đầu để đo. Profile bot và giới hạn tìm kiếm không thay đổi; nhiều worker không làm bot thông minh hơn.

## Giới hạn bảo vệ tài nguyên

BE dùng limiter có sẵn của .NET, không cài thêm service hoặc dependency. Mặc định: 2 socket/account, 256 socket/process; 32 slot xử lý chung cho REST/readiness/handshake/message socket; 512 vé chưa dùng toàn process và 4/account. State/đọc trận có burst 12 và nạp 4/giây/account, dùng chung với subscription. Tổng request/message có burst 400 và nạp 200/giây; auth có quota IP riêng. Bảng đầy đủ và mã lỗi nằm trong [API, mục 6.1](api.md#61-giới-hạn-tài-nguyên-và-retry).

Quota bảo vệ bộ nhớ và giảm request vào DB; không thay authorization, kiểm tra participant, bẫy ẩn hoặc tính hợp lệ của command. Không có hàng đợi HTTP chờ trong limiter. Quá tải trả `429`/`Retry-After`; client chậm hoặc quá payload bị ngắt socket, slot được giải phóng. Giới hạn 256 socket là trần bảo vệ, **không phải cam kết VM chịu được 256 người chơi**.

Mặc định được áp dụng sau khi deploy BE, không chạy SQL, không cần thêm env. Nếu cần điều chỉnh sau khi đo thực tế, mở file trên VM:

```bash
cp -p ~/herochess.env ~/herochess.env.before-traffic-limits
nano ~/herochess.env
```

Ví dụ cấu hình tương ứng mặc định:

```ini
TrafficLimits__MaxSocketsPerAccount=2
TrafficLimits__MaxSockets=256
TrafficLimits__MaxHttpInFlight=32
TrafficLimits__MaxPendingTickets=512
TrafficLimits__Reads__TokenLimit=12
TrafficLimits__Reads__TokensPerPeriod=4
TrafficLimits__Reads__ReplenishmentPeriod=00:00:01
```

Các bucket khác: `Global`, `Ip`, `Authentication`, `Account`, `Tickets`, `Messages`; dùng cùng ba thuộc tính `TokenLimit`, `TokensPerPeriod`, `ReplenishmentPeriod`. Giữ `QueueLimit=0`, `AutoReplenishment=true`. Cấu hình không hợp lệ bị từ chối khi startup. Không tăng quota chỉ để che lỗi client gửi lặp. Lưu nano bằng `Ctrl+O`, `Enter`, `Ctrl+X`, rồi tạo lại container qua workflow deploy; restart không nạp env mới.

### IP thật sau Caddy

Đã kiểm tra chỉ đọc trên Azure: Caddy dùng host network, API dùng Docker bridge với gateway `172.17.0.1`. `ReverseProxy:KnownProxies` trong appsettings tin đúng gateway này; middleware chỉ đọc một hop `X-Forwarded-For`/`X-Forwarded-Proto` từ proxy tin cậy. Các địa chỉ loopback mặc định của .NET vẫn được tin cho chạy local. Không tin header IP do client gửi trực tiếp từ nguồn khác.

Giữ API chỉ publish `127.0.0.1:10000` như workflow hiện tại; Internet đi qua Caddy. Nếu đổi Docker network/proxy, kiểm tra gateway rồi cấu hình `ReverseProxy__KnownProxies__0=<IP proxy thực tế>` trước khi deploy. Không bật `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` để bỏ qua danh sách proxy tin cậy. Không thêm toàn dải Internet vào KnownProxies/KnownNetworks.

### Kiểm tra sau deploy

1. Health/readiness thành công; login, vào trận và chơi bình thường.
2. Một account mở 2 socket; socket thứ 3 nhận `429`. Đóng một socket rồi reconnect bằng vé mới.
3. Spam GET state/ticket trong môi trường test: nhận `429` có `Retry-After`; account khác vẫn có quota riêng. Nhiều người chung IP vẫn dùng chung quota IP.
4. Ngắt mạng: client chuyển REST fallback, reconnect có backoff, không gửi lại command bằng ID mới. Thư viện WebSocket cần hỗ trợ pong chuẩn để vượt kiểm tra keepalive.
5. Quan sát tỷ lệ `429`, CPU/RAM và queue bot; điều chỉnh dựa trên workload thật. Các limiter nằm trong một process và reset khi process restart; nếu sau này chạy nhiều API replica thì cần thiết kế quota dùng chung.

Lớp này giảm spam ở tầng ứng dụng; không thay bảo vệ DDoS ở hạ tầng hoặc một đợt pentest.

Bootstrap dùng connection riêng không pooling để khóa session không bị giữ lại trong pool sau startup thành công hoặc thất bại. Pool xử lý trận không thay đổi.

## Thử hai worker trên Azure

Chỉ áp dụng sau khi code đã deploy và đã chốt cửa sổ thử tải riêng. Trong terminal SSH của VM:

```bash
cp -p ~/herochess.env ~/herochess.env.before-worker2
nano ~/herochess.env
```

Tìm biến worker nếu đã có, sửa thành dòng dưới; nếu chưa có thì thêm một dòng. Không để nhiều dòng cùng tên:

```ini
MatchRuntime__BotWorkerCount=2
```

Trong nano: `Ctrl+O`, `Enter`, rồi `Ctrl+X`. Chạy lại workflow **Deploy backend to Azure VM** của bản code đã kiểm chứng để tạo lại container. `docker restart` không nạp lại biến từ file env.

Kiểm tra từ SSH sau khi workflow hoàn tất:

```bash
sudo docker inspect --format '{{.State.Running}}' hc-api
curl -fsS http://127.0.0.1:10000/health/ready
sudo docker stats --no-stream hc-api hc-caddy
```

Theo dõi state/command p95, queue wait, bot total, RAM khả dụng, restart/OOM, DB pool và CPU credits. So một và hai worker với cùng loại trận, think time và mức tải. Lam Sơn cũng cần có mặt trong bài thử tải hỗn hợp vì dùng chung CPU của VM.

Nếu hai worker làm tăng RAM/độ trễ API hoặc không giảm thời gian chờ bot, đổi biến về `1` rồi tạo lại container qua workflow. Nếu lỗi thuộc code thì quay lại image đã biết tốt. Việc tạo lại container sẽ làm mất kết nối tạm thời; client phải reconnect và resync.

## Thay đổi ở đường đọc

- `StateAsync` thông thường còn **3 query nội bộ service**: match/state, participants, players nối rating. Không tính query xác thực bên ngoài service.
- Quân đã có tên frozen không tra catalog lại. Snapshot cũ thiếu tên được bù theo lô; cache tối đa 256 tên và hết hạn sau 60 giây.
- Cache chỉ lưu ID/tên. Không cache chung state hai phe, quyền sở hữu, số dư hoặc forecast riêng tư.
- Nhánh WebSocket command chỉ kiểm tra participant trước subscribe, không dựng đầy đủ state chỉ để kiểm tra quyền; command pipeline vẫn xác thực lại.
- Không có thay đổi snapshot schema, luật skill, CD, Elo, Coin hoặc cơ chế ghi trận.

## Chạy kiểm chứng local

Chạy tại thư mục repo trong PowerShell, với Docker Desktop đang hoạt động. Container dưới đây chỉ chứa dữ liệu test, bind localhost và không dùng volume production:

```powershell
docker run -d --name hc-capacity-local -p 127.0.0.1:55439:5432 -e POSTGRES_USER=hero_chess -e POSTGRES_PASSWORD=capacity-local-test-only -e POSTGRES_DB=capacity_load postgres:18-alpine
```

Đợi `pg_isready` báo nhận kết nối rồi tạo DB riêng cho integration:

```powershell
docker exec hc-capacity-local pg_isready -U hero_chess -d capacity_load
docker exec hc-capacity-local psql -U hero_chess -d postgres -c 'CREATE DATABASE capacity_test'
$env:HERO_CHESS_TEST_DB='Host=127.0.0.1;Port=55439;Database=capacity_test;Username=hero_chess;Password=capacity-local-test-only;Maximum Pool Size=10'
dotnet test tests/HeroChess.IntegrationTests/HeroChess.IntegrationTests.csproj
dotnet test tests/HeroChess.Rules.Tests/HeroChess.Rules.Tests.csproj
npm test --prefix frontend
node --test tests/web/app.test.mjs
```

Mật khẩu trong ví dụ chỉ dùng cho container test cục bộ. Nếu tên container hoặc cổng đã được dùng, kiểm tra môi trường hiện có hoặc chọn tên/cổng khác; không xóa một database chưa xác định.

Sau khi bộ test kết thúc, mở API load test trong terminal PowerShell riêng, cũng tại repo:

```powershell
$env:ConnectionStrings__DefaultConnection='Host=127.0.0.1;Port=55439;Database=capacity_load;Username=hero_chess;Password=capacity-local-test-only;Maximum Pool Size=10'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:DatabaseBootstrap__Enabled='true'
$env:DatabaseBootstrap__SeedDevelopmentFixtures='true'
$env:DatabaseBootstrap__IsProductionDatabase='false'
$env:Onboarding__GrantDevelopmentFixtureHeroes='true'
$env:MatchRuntime__NewFactionSkillsEnabled='true'
$env:MatchRuntime__BotWorkerCount='1'
dotnet run --project HeroChess.Api --no-launch-profile --urls http://127.0.0.1:5197
```

Trong terminal khác, chạy harness:

```powershell
$env:HERO_CHESS_SMOKE_URL='http://127.0.0.1:5197'
$env:LOAD_USERS='4'
$env:LOAD_SECONDS='30'
$env:LOAD_MODE='bot'
$env:LOAD_DIFFICULTY='hard'
$env:LOAD_THINK_MS='5000'
$env:LOAD_POLLING='optimized'
node tests/live/capacity-smoke.mjs
```

Kiểm tra biên HTTP trên Kestrel (API local/test đang chạy, không dùng TestServer):

```powershell
node tests/live/traffic-smoke.mjs
```

Script này chỉ chấp nhận localhost, tạo một account test, xác nhận command quá 64 KiB nhận `413`, spam state nhận `429` kèm `Retry-After` và profile vẫn đọc được. Không dùng vào API trỏ DB production.

- `LOAD_MODE`: `bot`, `pvp`, `mixed`; PvP cần số người chẵn.
- `LOAD_POLLING`: `legacy` mô phỏng polling bàn cờ khoảng 1,2 giây, `optimized` dùng snapshot subscription và nhịp dự phòng 10 giây.
- Tối đa 100 client và 1.800 giây cho một lần chạy; tăng tải từng nấc, có thời gian nghĩ thực tế. Mỗi client dùng account/lineup riêng.
- Harness dùng HTTP/WebSocket thật, lấy nước hợp lệ và gửi command có version. Trận kết thúc được tạo lượt mới để tiếp tục workload, dùng lại account/lineup.
- Khởi tạo account thực hiện trước khi đo; auth nhận `429` sẽ chờ `Retry-After` với số lần thử hữu hạn. Không tắt limiter để tạo tải. Báo cáo route có `rateLimited`; GET đồng bộ giữ backoff theo header, command không bị tự gửi lại.
- Harness dọn trận qua API khi kết thúc bình thường; account/lineup test vẫn ở DB riêng. Khi bị kill cưỡng bức, kiểm tra dữ liệu test trước lần đo sau.
- Target ngoài localhost bị chặn mặc định. Biến `HERO_CHESS_ALLOW_REMOTE_LOAD=YES` chỉ dành cho môi trường test riêng đã được cho phép; không dùng để bắn tải vào production có người đang chơi.
- Dừng API bằng `Ctrl+C`, đổi worker env thành `2`, mở lại rồi chạy cùng workload để so sánh. Không chạy hai API cùng DB trong phép so worker, vì worker của cả hai process sẽ tham gia xử lý.
- Không chạy integration tests hoặc workload khác cùng lúc khi cần so latency. Query count và số request vẫn có thể kiểm chứng riêng, nhưng latency phụ thuộc tài nguyên dùng chung.

Sau khi dùng xong:

```powershell
docker stop hc-capacity-local
```

Container được giữ để kiểm tra dữ liệu test; không có lệnh xóa dữ liệu trong quy trình này.

## Kết quả đã kiểm chứng

Ngày 26–27/11/2026, trên máy local với PostgreSQL test riêng:

| Kiểm chứng | Kết quả |
|---|---|
| Toàn bộ luật | 457 test pass |
| Toàn bộ integration | 226 test pass, gồm cấu hình hai worker và bảo vệ lưu lượng |
| Bảo vệ tài nguyên | Quota chung REST/WS, proxy tin cậy, ticket dùng một lần, socket/reconnect, 413 và hủy request đã được kiểm tra |
| Khóa bootstrap | Connection riêng giải phóng khóa sau cả startup thành công và thất bại |
| Đường đọc state | 3 query trong service cho snapshot đủ tên |
| Cache tên | Bù theo lô, dùng lại cache, hết hạn và giữ tên frozen đã được kiểm tra |
| Worker | Test một và hai worker đạt đúng đỉnh in-flight tương ứng; cùng trận không chạy hai version song song |
| Web | Test gộp refresh, ACK thiếu hồ sơ, stale, event trong lúc GET, abort và Retry-After pass |

Các mẫu smoke HTTP/WebSocket, 4 người, khoảng 15 giây mỗi lần:

| Workload | Kết quả |
|---|---|
| PvP không có nước đi, polling cũ | 43 GET state, không lỗi |
| PvP không có nước đi, polling tối ưu | 4 GET state, không lỗi |
| 4 trận bot Khó, 1 worker, think time khoảng 3 giây | 16 lượt trả bot quan sát; p95 khoảng 473 ms; không lỗi/cleanup lỗi |
| 4 trận bot Khó, 2 worker, think time khoảng 3 giây | 17 lượt trả bot quan sát; p95 khoảng 355 ms; không lỗi/cleanup lỗi |
| PvP 4 người, 45 giây, limiter bật | 16 GET state, không lỗi/cleanup lỗi; auth setup kiểm tra với quota thấp 1 token/giây và retry theo header |
| Kestrel kiểm tra biên HTTP | Command >64 KiB trả `413 REQUEST_TOO_LARGE`; spam state trả `429` kèm `Retry-After`, profile vẫn truy cập được |

Độ trễ trả bot quan sát từ client bao gồm gửi command, chờ bot, truyền và đồng bộ state; khác metric search riêng. Các mẫu ngắn chạy ở những thời điểm khác nhau, không phải benchmark đủ để khẳng định hai worker tốt hơn trên Azure. Vì vậy cấu hình mặc định vẫn là một worker.

Chưa đo tải kéo dài trên VM Azure 1 GB, chưa chọn ngưỡng người chơi tối đa và chưa kiểm chứng bản Unity sau handoff. Khi đo Azure cần ghi RAM, CPU credits và queue; số liệu local không thay thế phép đo đó.
