# Hero Chess — Chạy và test Backend API

Tài liệu này dùng để chạy và nghiệm thu backend ở máy local, với **PostgreSQL Supabase** đang là database chính. Không cần chạy Docker nếu connection string của bạn trỏ đến Supabase.

> Không gửi password Supabase, access token hay connection string đầy đủ vào Git, chat nhóm hoặc Postman collection được commit. Giữ chúng trong User Secrets của máy.

## 1. Cần có gì trước khi chạy

- .NET SDK 10 (`dotnet --version`).
- Một project Supabase PostgreSQL và connection string có quyền tạo schema/bảng, nếu database còn trống.
- Trong Visual Studio hoặc terminal, chạy API theo profile `Development`.

Backend đọc `ConnectionStrings:DefaultConnection` qua User Secrets (profile Development) hoặc biến môi trường. `appsettings.json` không chứa password. File `.env` **không được `dotnet run` tự đọc**; nó chỉ dành cho Docker Compose local. Vì vậy, khi dùng Supabase, không cần sửa hay chạy `.env`/Docker để API kết nối database. Sau khi đổi password Supabase, cập nhật lại User Secret; password cũ đã từng nằm trong Git nên cần thu hồi/đổi trên Supabase.

## 2. Cấu hình User Secrets cho Supabase

Máy hiện tại đã có `ConnectionStrings:DefaultConnection` trong User Secrets; bỏ password khỏi `appsettings.json` không làm mất cấu hình đó. Để cập nhật sau khi đổi password: trong Visual Studio nhấp phải project `HeroChess.Api` → **Manage User Secrets**, rồi giữ JSON tương tự (dán connection string mới lấy từ nút **Connect** trong Supabase):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=<host>;Port=<port>;Database=postgres;Username=<user>;Password=<new-password>;SSL Mode=Require"
  }
}
```

Giữ các User Secrets khác nếu file đã có, chỉ thay `DefaultConnection`. Nếu Supabase cung cấp connection string khác (ví dụ pooler có host/port/user riêng), dùng **nguyên connection string Supabase cung cấp**. Không tự đổi qua `127.0.0.1:55432`; đó là địa chỉ Docker local, không phải Supabase.

### Database mới hoặc chưa có schema Hero Chess

Chỉ bật bootstrap trong lần đầu tạo schema, sau khi đã xác nhận đây là đúng project Supabase cần dùng:

```powershell
dotnet user-secrets set "DatabaseBootstrap:Enabled" "true" --project HeroChess.Api
dotnet user-secrets set "DatabaseBootstrap:SeedDevelopmentFixtures" "false" --project HeroChess.Api
dotnet user-secrets set "DatabaseBootstrap:IsProductionDatabase" "true" --project HeroChess.Api
```

Chỉ dùng bước này với database mới hoặc bản sao đã backup và kiểm thử. Bootstrap tạo schema `hero_chess`, các bảng game, Identity và catalog. Không bật development fixtures trên Supabase. Nó sẽ từ chối sửa một schema `hero_chess` có số bảng không đúng để tránh ghi đè một database dở dang.

Sau khi log hiện `Database bootstrap completed.`, tắt bootstrap để các lần chạy sau không mang quyền tự sửa schema:

```powershell
dotnet user-secrets set "DatabaseBootstrap:Enabled" "false" --project HeroChess.Api
```

### Database Hero Chess đã có đủ schema

Giữ bootstrap là `false`. Nếu log báo `relation hero_chess.game_match does not exist`, connection string đang trỏ nhầm database hoặc database đó chưa được bootstrap; không phải lỗi ở MatchRecovery worker. **Đừng bật bootstrap trên Supabase đang dùng chỉ để thử skill mới**: seed/gameplay mới chưa được chạy nghiệm thu trên DB thử. Ngoài ra `MatchRecoveryHostedService` hủy trận `selecting/active` khi API khởi động lại, nên chỉ restart lúc không còn trận đang chơi.

### Thử gameplay mới trên PostgreSQL Docker riêng

Khởi động Docker Desktop, giữ mật khẩu local trong `.env` (file này đã được Git ignore), rồi tại thư mục repo chạy:

```powershell
docker compose up -d postgres
$env:ConnectionStrings__DefaultConnection = 'Host=127.0.0.1;Port=55432;Database=hero_chess;Username=hero_chess;Password=<mật-khẩu-HERO_CHESS_DB_PASSWORD-trong-.env>'
$env:DatabaseBootstrap__Enabled = 'true'
$env:DatabaseBootstrap__SeedDevelopmentFixtures = 'true'
$env:DatabaseBootstrap__IsProductionDatabase = 'false'
$env:Onboarding__GrantDevelopmentFixtureHeroes = 'true'
dotnet run --project HeroChess.Api --launch-profile http
```

Các biến `$env:` chỉ áp dụng trong cửa sổ PowerShell này và ghi đè User Secrets Supabase cho lần chạy local. Không chạy lệnh trên nếu `ConnectionStrings__DefaultConnection` còn trỏ Supabase. Bootstrap sẽ áp dụng catalog/skill mới và thêm quân DEV để đủ 16 ô đội hình; hero có tên mua được trong UI nếu đã bật và giá 0. Sau khi test local, đóng cửa sổ PowerShell này để trở về cấu hình User Secrets Supabase. Không dùng `docker compose down -v` nếu cần giữ dữ liệu local.

## 3. Chạy backend

```powershell
dotnet restore HeroChessBackend.slnx --configfile NuGet.Config
dotnet run --project HeroChess.Api --launch-profile http
```

Khi thành công, API chạy ở `http://localhost:5012`.

Kiểm tra nhanh trong cửa sổ PowerShell khác:

```powershell
Invoke-RestMethod http://localhost:5012/health/live
Invoke-RestMethod http://localhost:5012/health/ready
```

Kết quả mong đợi là `status: live` và `status: ready`, database `connected`. Swagger có tại `http://localhost:5012/swagger` khi chạy Development. Trang demo có tại `http://localhost:5012`; nó tiện để chơi thử nhưng không thay thế test API bên dưới.

### Chạy React test UI

Mở cửa sổ terminal thứ hai:

```powershell
cd frontend
npm ci
npm run dev
```

Mở `http://127.0.0.1:5173/react/`. Vite chuyển `/api/v1` và WebSocket về backend cổng `5012`. Đăng nhập, mua hero cần thử ở Catalog nếu chưa sở hữu, xếp 16 quân và 3 Command Skill, rồi chọn **Chơi với bot**. Trong bàn cờ, chọn hero để xem Hero Skill; Thành/Rào/Khiên nằm trong bảng Command Skill. `http://localhost:5012/swagger` dùng để xem request/response API; `http://localhost:5012/react/` là bản FE đã build sẵn từ lần `npm run build` gần nhất.

### Test trực tiếp bằng Swagger UI

Có thể dùng Swagger thay cho PowerShell với hầu hết API HTTP:

1. Mở `http://localhost:5012/swagger`.
2. Mở `POST /api/v1/auth/register`, chọn **Try it out**, nhập email/password rồi **Execute**.
3. Mở `POST /api/v1/auth/login`, thêm query `useCookies=false`, dùng cùng email/password và **Execute**.
4. Copy trường `accessToken` trong response.
5. Bấm nút **Authorize** ở góc trên bên phải, dán **chỉ accessToken** vào Bearer authentication rồi bấm Authorize/Close. Swagger tự thêm `Authorization: Bearer ...` vào các request sau đó.
6. Dùng **Try it out** cho `GET /api/v1/me`, `GET /api/v1/catalog`, lineup, matchmaking và các route match.

WebSocket không gửi được từ Swagger UI; phần đó dùng React test UI hoặc một WebSocket client. `hero_active` đã hỗ trợ Bạch Đằng Giang và bước đặc biệt của Quang Trung; `team_skill` hỗ trợ những skill có handler, gồm Thành/Rào/Khiên. Skill trong catalog vẫn có thể chưa được triển khai nếu không có handler.

## 4. Quy ước test bằng PowerShell

Các ví dụ dùng PowerShell và API local. Chạy block này trước:

```powershell
$base = 'http://localhost:5012/api/v1'
$suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$emailA = "player-a-$suffix@example.test"
$emailB = "player-b-$suffix@example.test"
$password = 'HeroChess123'
```

JSON trả lỗi có dạng:

```json
{ "code": "...", "message": "...", "requestId": "...", "details": {} }
```

Mã HTTP thường gặp: `401` chưa đăng nhập, `403` sai role, `404` không thuộc tài nguyên, `409` version/idempotency xung đột, `422` luật game hoặc validation không hợp lệ.

## 5. Test tài khoản và catalog

### 5.1 Đăng ký hai Player

```powershell
$bodyA = @{ email = $emailA; password = $password } | ConvertTo-Json
$bodyB = @{ email = $emailB; password = $password } | ConvertTo-Json
Invoke-RestMethod "$base/auth/register" -Method Post -ContentType 'application/json' -Body $bodyA
Invoke-RestMethod "$base/auth/register" -Method Post -ContentType 'application/json' -Body $bodyB
```

Đăng ký chỉ tạo Player; client gửi `role: admin` cũng không thể tự thành Admin.

### 5.2 Login và lấy Bearer token

```powershell
$loginA = Invoke-RestMethod "$base/auth/login?useCookies=false" -Method Post -ContentType 'application/json' -Body $bodyA
$loginB = Invoke-RestMethod "$base/auth/login?useCookies=false" -Method Post -ContentType 'application/json' -Body $bodyB
$headersA = @{ Authorization = "Bearer $($loginA.accessToken)" }
$headersB = @{ Authorization = "Bearer $($loginB.accessToken)" }
```

Test profile và catalog:

```powershell
Invoke-RestMethod "$base/me" -Headers $headersA
$catalogA = Invoke-RestMethod "$base/catalog" -Headers $headersA
$catalogA.ruleset
$catalogA.heroes | Select-Object code, name, classCode, isOwned, coinPrice
```

Kết quả mong đợi: `/me` có `role: player`; catalog có ruleset, 16 slots và hero. Nếu fixture Development được bật trước lúc đăng ký, Player có hero `isOwned: true` để tạo lineup.

Test refresh token:

```powershell
Invoke-RestMethod "$base/auth/refresh" -Method Post -ContentType 'application/json' -Body (@{ refreshToken = $loginA.refreshToken } | ConvertTo-Json)
```

## 6. Tạo lineup hợp lệ

Không hard-code UUID hero trong tài liệu vì UUID đến từ catalog database của bạn. Đoạn sau tự lấy một hero đang sở hữu cho từng class theo slot, rồi lưu lineup.

```powershell
$availableByClass = @{}
foreach ($hero in ($catalogA.heroes | Where-Object isOwned)) {
  if (-not $availableByClass.ContainsKey($hero.classCode)) { $availableByClass[$hero.classCode] = @() }
  $availableByClass[$hero.classCode] += $hero
}

$entries = foreach ($slot in ($catalogA.slots | Sort-Object slotNo)) {
  $pool = $availableByClass[$slot.classCode]
  if (-not $pool -or $pool.Count -eq 0) { throw "Thiếu hero owned cho class $($slot.classCode). Bật fixture rồi đăng ký Player mới." }
  $picked = $pool[0]
  $availableByClass[$slot.classCode] = @($pool | Select-Object -Skip 1)
  @{ slotNo = $slot.slotNo; heroId = $picked.id; cosmeticId = $null }
}

$skills = @($catalogA.teamSkills | Select-Object -First 3 | ForEach-Object -Begin { $i = 0 } -Process { $i++; @{ slotNo = $i; skillId = $_.id } })
$lineupRequest = @{ name = 'API test lineup'; rulesetId = $catalogA.ruleset.id; entries = @($entries); skills = @($skills) } | ConvertTo-Json -Depth 6
$lineupA = Invoke-RestMethod "$base/lineups" -Method Post -Headers $headersA -ContentType 'application/json' -Body $lineupRequest
$lineupA
```

Kết quả mong đợi: `isValid: true`, có `id`, `revision`, `totalSp` và `budget`. Lưu `$lineupA.id`/`$lineupA.revision`; chúng được dùng ở bước chọn đội hình.

Test đọc, sửa revision và xóa:

```powershell
Invoke-RestMethod "$base/lineups" -Headers $headersA
# Gửi lại PUT với expectedRevision cũ sau một lần update phải trả 409 STALE_REVISION.
```

> Không xóa lineup đang cần cho các bước bot/PvP phía dưới.

## 7. Luồng test nhanh nhất: Player đấu Bot

Bot match cần một Player và một lineup hợp lệ.

```powershell
$ticket = Invoke-RestMethod "$base/matchmaking/tickets" -Method Post -Headers $headersA -ContentType 'application/json' -Body (@{ mode = 'bot' } | ConvertTo-Json)
$ticket
```

Nếu `matchId` chưa xuất hiện, poll ticket vài lần:

```powershell
do {
  Start-Sleep -Milliseconds 500
  $ticket = Invoke-RestMethod "$base/matchmaking/tickets/$($ticket.ticketId)" -Headers $headersA
} while (-not $ticket.matchId)
$matchId = $ticket.matchId
```

Chọn lineup, confirm và đọc state:

```powershell
$selection = @{ lineupId = $lineupA.id; expectedRevision = $lineupA.revision } | ConvertTo-Json
Invoke-RestMethod "$base/matches/$matchId/selection" -Method Put -Headers $headersA -ContentType 'application/json' -Body $selection
Invoke-RestMethod "$base/matches/$matchId/confirm" -Method Post -Headers $headersA
$state = Invoke-RestMethod "$base/matches/$matchId/state" -Headers $headersA
$state
```

Kết quả mong đợi: `status: active`, `version: 0`, `deadlineAt` có giá trị, `countedActions: 0`.

### Gửi một nước đi hợp lệ

Không tự đoán nước đi. Luôn lấy legal actions từ server, chọn nước đầu tiên rồi gửi với đúng `expectedVersion`:

```powershell
$legal = Invoke-RestMethod "$base/matches/$matchId/legal-actions" -Headers $headersA
$move = $legal | Select-Object -First 1
$command = @{
  commandId = [guid]::NewGuid()
  expectedVersion = $state.version
  action = @{ type = 'move'; pieceId = $move.pieceId; to = @{ x = $move.toX; y = $move.toY } }
} | ConvertTo-Json -Depth 6
$result = Invoke-RestMethod "$base/matches/$matchId/commands" -Method Post -Headers $headersA -ContentType 'application/json' -Body $command
$result.snapshot
```

Kết quả mong đợi: response có `duplicate: false`, `sequenceNo` tăng, snapshot mới có version mới. Bot sẽ tự đi sau đó; gọi lại `/state` khi đến lượt Player.

### Test lệnh bị stale và resign

Gửi lại command cũ nhưng đổi `commandId` trong khi `expectedVersion` cũ: mong đợi `409 STALE_STATE`. Để kết thúc nhanh một trận test:

```powershell
$fresh = Invoke-RestMethod "$base/matches/$matchId/state" -Headers $headersA
$resign = @{ commandId = [guid]::NewGuid(); expectedVersion = $fresh.version; action = @{ type = 'resign' } } | ConvertTo-Json -Depth 4
Invoke-RestMethod "$base/matches/$matchId/commands" -Method Post -Headers $headersA -ContentType 'application/json' -Body $resign
```

Sau vài giây, kiểm tra history/replay:

```powershell
Invoke-RestMethod "$base/matches?limit=10" -Headers $headersA
Invoke-RestMethod "$base/matches/$matchId/replay?afterSequence=-1&limit=50" -Headers $headersA
```

`undo` chỉ hợp lệ trong bot match. `hero_active` dùng cho Bạch Đằng Giang/Quang Trung; `team_skill` dùng slot skill đã chọn trong lineup. Skill chưa có handler mới trả lỗi không hỗ trợ.

## 8. Test PvP hai Player

Lặp lại bước tạo lineup cho Player B bằng `$headersB` và `$catalogB`, tạo `$lineupB`. Sau đó tạo ticket ranked cho cả hai:

```powershell
$ticketA = Invoke-RestMethod "$base/matchmaking/tickets" -Method Post -Headers $headersA -ContentType 'application/json' -Body '{"mode":"ranked"}'
$ticketB = Invoke-RestMethod "$base/matchmaking/tickets" -Method Post -Headers $headersB -ContentType 'application/json' -Body '{"mode":"ranked"}'
```

Poll ticket A/B tới khi đều nhận cùng `matchId`. Mỗi Player gửi `PUT /matches/{matchId}/selection` bằng lineup của chính họ, sau đó mỗi bên `POST /confirm`. Khi cả hai confirm, state thành `active`.

Nguyên tắc test PvP:

- Mỗi nước lấy state mới trước, dùng `expectedVersion` đúng bằng `state.version`.
- Player chỉ gửi được khi `sideToMove` là phe của mình; sai lượt trả `422 WRONG_TURN`.
- Timeout là server-side: mỗi lượt 90 giây. Timeout thường mất lượt; timeout lần thứ hai liên tiếp của cùng người chơi thua `afk`; timeout khi đang bị chiếu thua ngay.
- Tổng 150 action (nước đi/timeout; skill sau này) sẽ so SP quân còn sống nếu chưa có kết quả trực tiếp.

## 9. Test WebSocket (tùy chọn)

REST ở trên đủ test backend. Khi cần realtime, lấy vé một lần rồi mở WebSocket:

```powershell
Invoke-RestMethod "$base/ws-ticket" -Method Post -Headers $headersA
```

Nối tới `ws://localhost:5012/ws/v1?ticket=<ticket>` (hoặc `wss` nếu chạy HTTPS), rồi gửi:

```json
{
  "type": "match.subscribe",
  "requestId": "<uuid>",
  "payload": { "matchId": "<uuid>" }
}
```

Command qua socket dùng:

```json
{
  "type": "match.command",
  "requestId": "<uuid>",
  "payload": {
    "matchId": "<uuid>",
    "commandId": "<uuid>",
    "expectedVersion": 0,
    "action": { "type": "resign" }
  }
}
```

Vé hết hạn sau 30 giây và chỉ dùng một lần. Dùng lại phải xin vé mới.

## 10. Test Shop và Admin

### Shop

Lấy `id` của hero `isOwned: false` từ catalog. Gửi header `Idempotency-Key` UUID; dùng lại đúng key cùng hero phải trả lại kết quả cũ, không trừ coin lần hai.

```powershell
$hero = $catalogA.heroes | Where-Object { -not $_.isOwned } | Select-Object -First 1
$purchaseHeaders = @{ Authorization = $headersA.Authorization; 'Idempotency-Key' = [guid]::NewGuid().ToString() }
Invoke-RestMethod "$base/shop/heroes/$($hero.id)/purchase" -Method Post -Headers $purchaseHeaders
```

### Admin

Admin là subtype riêng, không thể tạo bằng API đăng ký public. Trong Development, đặt email admin trước khi đăng ký account đó:

```powershell
dotnet user-secrets set "Onboarding:DevelopmentAdminEmails:0" "admin-test@example.test" --project HeroChess.Api
```

Restart API, đăng ký/login email này, rồi dùng token của Admin:

```powershell
$adminHeaders = @{ Authorization = 'Bearer <access-token-admin>' }
Invoke-RestMethod "$base/admin/heroes/<hero-uuid>/price" -Method Patch -Headers $adminHeaders -ContentType 'application/json' -Body '{"coinPrice":"125","reason":"API manual test"}'
Invoke-RestMethod "$base/admin/audit?limit=20" -Headers $adminHeaders
```

Player gọi `/admin/...` phải nhận `403`; Admin gọi lineup, shop, matchmaking hay gameplay cũng phải nhận `403`.

## 11. Checklist nghiệm thu tối thiểu

- [ ] `/health/live` và `/health/ready` đều OK.
- [ ] Player đăng ký, login, refresh, `/me`, `/catalog` thành công.
- [ ] Tạo lineup hợp lệ; test dữ liệu thiếu/sai revision trả lỗi mà không ghi dở.
- [ ] Bot match: queue → select → confirm → active → legal move → resign → history/replay.
- [ ] PvP: hai Player được ghép, không lộ lineup đối phương trước confirm, state đồng bộ sau nước đi.
- [ ] Sai token/sai role/sai owner bị 401/403/404 phù hợp.
- [ ] Lệnh stale trả 409; lệnh không hợp lệ trả 400/422; retry đúng `commandId` không áp dụng nước hai lần.
- [ ] Shop không trừ coin hai lần với cùng `Idempotency-Key`; admin price change tạo audit.

## 12. Docker dùng khi nào?

Nếu dùng Supabase, bỏ qua Docker. `docker compose up -d postgres` chỉ dùng khi muốn có PostgreSQL local riêng tại `127.0.0.1:55432`. Để test local, ưu tiên biến môi trường chỉ trong cửa sổ PowerShell đang chạy API như mục 2; không cần thay User Secrets Supabase. Password local phải khớp `HERO_CHESS_DB_PASSWORD` trong `.env`.

## 13. Gỡ lỗi nhanh

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| `relation hero_chess.game_match does not exist` | Trỏ nhầm DB hoặc DB chưa có schema | Kiểm tra User Secrets; chỉ đúng DB mới bật bootstrap một lần. |
| `Database bootstrap is disabled` | Cờ bootstrap đang false | Bình thường khi schema đã tồn tại; không bình thường nếu DB trống. |
| `/health/ready` 503 | Connection string/Supabase network sai | Kiểm tra host, port, password, SSL và firewall/network. |
| `401` | Thiếu/sai bearer token | Login lại, dùng `Authorization: Bearer <accessToken>`. |
| `403` | Account sai role hoặc disabled | Player chỉ vào game; Admin chỉ vào route admin. |
| `422 SKILL_NOT_IMPLEMENTED` | Skill cụ thể chưa có gameplay handler | Kiểm tra `implementationKey` trong catalog và handler đã đăng ký. |
| `409 STALE_STATE` | State đổi sau khi client đọc | GET state mới, gửi lại với version mới và commandId mới. |

## 14. Test tự động của source

Manual API test không thay thế test tự động. Không chạy integration test vào Supabase production/development đang dùng, vì test có thể tạo/sửa dữ liệu. Muốn chạy integration test, cấu hình `HERO_CHESS_TEST_DB` tới database test riêng rồi chạy:

```powershell
dotnet test tests/HeroChess.Rules.Tests/HeroChess.Rules.Tests.csproj --no-restore
dotnet test tests/HeroChess.IntegrationTests/HeroChess.IntegrationTests.csproj --no-restore
node --test tests/web/app.test.mjs
npm test --prefix frontend
```
