# Hero Chess — AI Handoff / Project Introduction

> Mục đích: đây là điểm bắt đầu cho một AI hoặc lập trình viên mới làm việc trong repository này. Đọc file này trước, sau đó đọc tài liệu chuyên sâu được liên kết bên dưới. Không coi file này là đặc tả game mới; các quyết định đã được chốt nằm trong `docs/Decisions.md`.

## 1. Game này là gì?

**Hero Chess** là game cờ tướng PvP theo lượt có hệ thống hero. Mỗi quân cờ trên bàn đại diện cho một Hero thuộc một chess class chuẩn (Tướng, Sĩ, Tượng, Xe, Pháo, Mã, Tốt). Người chơi xây đội hình trước trận, sau đó chơi cờ tướng trên bàn 9×10 với các trait/command skill của Hero hoặc phe.

Backend hiện là prototype server-authoritative: server là nơi duy nhất quyết định nước đi hợp lệ, lượt, timeout, kết quả, Elo, coin và replay. Client chỉ gửi ý định hành động rồi hiển thị snapshot/event do server trả về.

Mục tiêu prototype đã chốt:

- Tài khoản đăng nhập; không có Guest trong scope hiện tại.
- Player xây lineup 16 quân, ghép trận PvP hoặc đấu bot cơ bản.
- Luật cờ tướng, timer, kết quả, lịch sử/replay và kinh tế cơ bản chạy ở backend.
- Có thể demo local/LAN; deploy public là giai đoạn sau.

Không tự mở rộng phạm vi bằng cách thêm bot ELO cao, guest, dashboard admin lớn, Unity client, shop/payment thật, hoặc hàng chục hero mới nếu không có yêu cầu cụ thể.

## 2. Trạng thái hiện tại: đã có gì và chưa có gì?

### Đã có trong source

- ASP.NET Core .NET 10 API, PostgreSQL/Npgsql và ASP.NET Core Identity bearer token.
- `User` là lớp cha; `Player` và `Admin` là hai loại tài khoản loại trừ nhau. Admin chỉ quản trị, không chơi game.
- Catalog, quyền sở hữu Hero, lineup CRUD + validation, shop và audit/đổi giá cho Admin.
- Matchmaking ranked/bot, chọn lineup kín, confirm, state snapshot, REST fallback và WebSocket.
- Luật cờ tướng cơ bản: nước đi hợp lệ, tự chiếu, lộ mặt hai Tướng, chiếu bí/no legal actions, trait di chuyển qua registry.
- Timer, timeout, AFK, giới hạn action, settlement, Elo/coin, lịch sử/replay, reconnect/recovery.
- Bot server cơ bản chọn nước hợp lệ, ưu tiên bắt quân có SP cao; bot này chưa phải AI có ELO được hiệu chuẩn.
- Effect model/state schema v4 và các handler command skill đang có trong `HeroChess.Rules/Skills`.

### Cần xem là **partial / phải kiểm chứng trước khi mở rộng**

- Hero active skill vẫn trả `SKILL_NOT_IMPLEMENTED`.
- Command skill đã có dispatcher và bốn handler trong source (`Vạn Cọc Trấn Giang`, `Phản Kỳ Đoạt Thế`, `Phá Trận Đoạt Phong`, `Binh Lâm Thủy Hiểm`), nhưng đây là phần mới hơn một số tài liệu T01–T14. Trước khi sửa/mở rộng, đối chiếu handler, seed catalog, API payload và test end-to-end; đừng dựa vào tài liệu cũ nói mọi skill đều là stub.
- Đặc tả effect tổng quát, thứ tự effect phức tạp, roster khoảng 20 Hero hoàn chỉnh và balance chưa chốt hết.
- Queue/lock matchmaking dùng RAM theo scope prototype; không an toàn khi scale nhiều process/server.
- Browser smoke tự động và thử nghiệm LAN thực tế chưa phải bằng chứng hoàn chỉnh.

## 3. Luật gameplay đã chốt

Những luật dưới đây là yêu cầu sản phẩm, không tự đổi khi code:

| Chủ đề | Luật đã chốt |
|---|---|
| Bàn cờ | 9 cột × 10 hàng; `x=0..8`, `y=0..9`. Red ở phía `y` nhỏ, Black được xoay khi tạo bàn. |
| Lượt | Mỗi lượt có 90 giây. Server, không phải client, quyết định deadline. |
| Hết giờ thường | Mất lượt và tăng action count. Hai lượt timeout liên tiếp của **cùng người chơi** thì thua AFK, dù có lượt đối thủ xen giữa. Một action hợp lệ của chính người đó reset chuỗi timeout của họ. |
| Hết giờ khi bị chiếu | Thua ngay, `endReason = timeout_in_check`. |
| Giới hạn trận | 150 action tổng của Red + Black. Move, skill hợp lệ và timeout đều tính một action. Kết quả trực tiếp như checkmate/AFK/timeout-in-check được ưu tiên. Nếu vẫn chưa kết thúc ở action 150, so tổng Setup Points của quân sống; bằng nhau hòa. |
| Skill | Skill hợp lệ thay nước đi. Cooldown được thiết kế theo lượt chung của bàn; chi tiết effect/cooldown chỉ được thêm khi đặc tả skill tương ứng rõ ràng. |
| Faction command skill | Hero chỉ thuộc một faction theo hướng thiết kế. Điều kiện thường là 2–3 hero cùng phe có mặt lúc bắt đầu; ngưỡng cụ thể theo từng skill. Quyền đã mở ở đầu trận giữ nguyên khi hero bị bắt. |
| Trait | Một `hero_trait` chỉ thuộc một Hero, nhưng nhiều Hero có thể dùng cùng `implementationKey`/handler. |
| Bot | Bot chỉ cần chọn nước đi hợp lệ cho prototype. Không hứa hẹn độ khó/ELO như engine cờ vua. |

Đọc đầy đủ: [Decisions.md](Decisions.md).

## 4. Kiến trúc và luồng dữ liệu

```text
Browser / Unity client
   ├─ HTTP REST ───────────────────────────────┐
   └─ WebSocket (vé một lần, 30 giây) ────────┤
                                                v
                                   HeroChess.Api (ASP.NET Core)
                                   ├─ Auth / policies / controllers
                                   ├─ Services: lineup, match, settlement, shop
                                   ├─ Background workers: timeout, bot, recovery
                                   └─ MatchCommandService (server authority)
                                                v
                            HeroChess.Rules + HeroChess.Contracts
                            ├─ XiangqiRulesEngine / movement registry
                            ├─ game state, lifecycle, effects, skill handlers
                            └─ request/response DTOs
                                                v
                                    PostgreSQL (Supabase or local Docker)
                                    ├─ hero_chess schema: game data
                                    └─ identity schema: authentication data
```

Nguyên tắc quan trọng:

1. Client không được tự tính nước đi/kết quả rồi gửi state lên server.
2. `GameState` trong `match_state` là snapshot authoritative; `match_action.state_after` là lịch sử immutable/replay.
3. Mọi command player, WebSocket, bot và timeout đi qua pipeline command chung để có lock, version check, transaction, dedup và broadcast nhất quán.
4. Catalog/lineup/ruleset được freeze khi trận start. Sửa catalog sau đó không được sửa replay hoặc trận đang chơi.
5. Chỉ code ở `HeroChess.Rules` mới nên hiểu luật game thuần; không đưa EF/HTTP/Unity vào project này.

## 5. Cấu trúc repository

| Path | Vai trò |
|---|---|
| `HeroChess.Api/` | Web API, auth, EF Core entities, controllers, services, workers và web demo. Điểm vào là `Program.cs`. |
| `HeroChess.Contracts/` | DTO request/response dùng chung API-client; không chứa EF hay luật game. |
| `HeroChess.Rules/` | Game model, luật cờ tướng, trait movement, turn lifecycle, effects và command-skill handlers. |
| `Hero_Chess_DB_Prototype_v0_1/` | SQL baseline: schema, catalog seed, development fixture và SQL constraint tests. |
| `tests/HeroChess.Rules.Tests/` | Unit tests cho luật/effect/skill. |
| `tests/HeroChess.IntegrationTests/` | API/PostgreSQL/WebSocket/inheritance/migration tests. |
| `tests/web/` | Test logic JavaScript web demo. |
| `docs/` | Handoff, quyết định, ERD, API guide, task plan và codebase guide. |
| `docker-compose.yml` | PostgreSQL local tùy chọn; không bắt buộc khi dùng Supabase. |
| `.env.example` | Mẫu Docker local; không phải nơi API .NET tự đọc secret. |

### Các file cần mở theo việc

| Việc | Mở từ đây |
|---|---|
| Luật gameplay/đi cờ | `HeroChess.Rules/XiangqiRulesEngine.cs`, `GameModels.cs`, `MovementHandlers.cs` |
| Thêm/sửa command skill | `HeroChess.Rules/Skills/`, `Effects/`, `TurnLifecycle.cs`, rồi `HeroChess.Api/Services/MatchCommandService.cs` |
| Thêm Hero/trait | [AddingHeroLogic.md](AddingHeroLogic.md), SQL catalog seed, `MovementHandlers.cs` |
| Sửa action/trận/replay | `MatchCommandService.cs`, `MatchReadService.cs`, `MatchHistoryService.cs`, `WebSocketEndpoint.cs` |
| Matchmaking/selection | `MatchmakingService.cs`, `MatchSelectionService.cs`, `MatchSelectionLifetime.cs` |
| User/Admin/quyền | `Auth/`, `User_Player_Admin_Refactor.md`, `Data/Entities.cs` |
| Database/bootstrap | `Data/Bootstrap/`, `AppDbContext.cs`, SQL baseline |
| API contract/client | `Contracts/ApiContracts.cs`, `Controllers/`, `wwwroot/app.js` |
| Test endpoint thủ công | [API_Testing_Guide.md](API_Testing_Guide.md) |

## 6. Tài khoản, auth và quyền

- Public register tạo **Player**, không nhận role do client tự gửi.
- `Admin` phải được seed/cấp theo môi trường; Admin chỉ dùng endpoint `/api/v1/admin/...`.
- `Player` dùng lineup, shop, matchmaking, WebSocket, match/replay.
- `/api/v1/me` trả `userId`, `role`, `status`; các trường game (`playerId`, coin, elo) chỉ có với Player.
- Token dùng bearer authentication tích hợp của ASP.NET Identity. Policy kiểm tra lại role/status hiện tại trong database, không chỉ tin role claim cũ.
- API public auth nằm dưới `/api/v1/auth`: register, login, refresh.

Chi tiết refactor: [User_Player_Admin_Refactor.md](User_Player_Admin_Refactor.md).

## 7. Database và cấu hình local

Application dùng PostgreSQL nói chung, có thể kết nối Supabase hoặc PostgreSQL Docker local. Connection string được đọc từ `ConnectionStrings:DefaultConnection`.

### Supabase (flow đang ưu tiên)

- Lưu connection string thật trong **.NET User Secrets**, không commit vào Git.
- `dotnet run` không tự đọc `.env`.
- `DatabaseBootstrap:Enabled=true` chỉ dùng khi đúng database cần tạo/nâng schema. Bootstrap dùng advisory lock, tạo/chạy SQL theo thứ tự và từ chối một `hero_chess` schema không đúng số bảng dự kiến.
- Sau khi bootstrap/migration thành công, tắt lại `DatabaseBootstrap:Enabled` để không tự động thay đổi DB mỗi lần chạy.

### Docker local (tùy chọn)

`docker compose up -d` tạo PostgreSQL tại `127.0.0.1:55432`. File `.env` chỉ giúp Docker Compose lấy `HERO_CHESS_DB_PASSWORD`. Nếu dùng Docker, connection string User Secrets phải khớp password/port của Docker.

### Bootstrap/migration

Đừng dùng `EnsureCreated` hoặc tự tạo vài bảng lẻ. Bootstrap là thứ tự chuẩn:

1. `01_schema.sql` khi schema mới.
2. `04_identity_schema.sql` nếu database cũ chưa có `user_account`.
3. `05_app_extensions.sql`.
4. `06_user_inheritance.sql`.
5. `07_exclusive_hero_traits.sql`.
6. `02_seed_catalog.sql`.
7. `03_seed_dev_only.sql` chỉ khi Development + fixture bật.

Khi nâng database đã có dữ liệu: sao lưu, dừng binary cũ, chạy binary/schema mới, kiểm tra login và `/health/ready`. Không chạy binary cũ song song với schema sau migration.

## 8. API và test nhanh

Chạy Development:

```powershell
dotnet restore HeroChessBackend.slnx --configfile NuGet.Config
dotnet run --project HeroChess.Api --launch-profile http
```

- API local: `http://localhost:5012`.
- Swagger: `http://localhost:5012/swagger`.
- Web demo: `http://localhost:5012`.
- Health: `/health/live`, `/health/ready`.

Swagger đã có Bearer authentication: register → login với `useCookies=false` → copy `accessToken` → bấm **Authorize** và dán token → gọi API protected. WebSocket cần dùng web demo hoặc WebSocket client, Swagger không gửi WebSocket message.

Đọc [API_Testing_Guide.md](API_Testing_Guide.md) để có request mẫu cho register, lineup, bot match, PvP, shop, admin và gỡ lỗi DB.

## 9. Match lifecycle cần giữ nguyên

```text
Player tạo queue ticket
  → match tạo ở selecting
  → mỗi participant chọn snapshot lineup của mình
  → cả hai confirm
  → active: state/action sequence bắt đầu ở server
  → move / team_skill / timeout / resign / bot command
  → completed hoặc cancelled
  → settlement idempotent
  → participant đọc history/replay
```

- `expectedVersion` bảo vệ request stale.
- `commandId` bảo vệ retry/dedup. Retry cùng ID và payload phải trả command cũ, không áp dụng hai lần.
- Không xóa/sửa action log để làm undo. Undo bot tạo action mới, ranked không cho undo.
- Selection có deadline; startup recovery hủy trận dở theo policy prototype.
- WebSocket ticket dùng một lần, hạn ngắn; socket chỉ là transport, REST command dùng pipeline giống nhau.

## 10. Command skill/effect state hiện có

GameState schema hiện là v4 và có:

- `SkillStates` per side: slot, skill ID, uses/cooldown, implementation key.
- `EffectInstances`: ID, creator, controller theo vị trí, duration, remaining duration, state Active/Disabled/Ended, payload.
- `Obstacles` + `StakeMetadata` cho vật cản vật lý.
- `ProcessedTurns` để lifecycle chạy exactly-once cho mỗi turn.

`TurnLifecycle` chạy lúc bắt đầu turn authoritative để giảm cooldown/duration/lifetime, xử lý effect hết hạn và resolution Phản Kỳ. `MatchCommandService` nâng snapshot v3 cũ lên v4 trước khi dùng lifecycle.

Khi thêm skill mới:

1. Chốt rule/target/timing/priority bằng tài liệu trước.
2. Thêm key cố định vào `SkillKeys` và handler thuần trong `HeroChess.Rules/Skills`.
3. Đăng ký handler trong `Program.cs` qua `CommandSkillRegistry`.
4. Bảo đảm catalog `team_skill.implementation_key` khớp key đó.
5. Chỉ mutate clone state; failure phải không tiêu cooldown/không ghi state.
6. Thêm unit test handler/lifecycle và integration test command/replay.

Không thêm effect chung chung chỉ vì cần “một chỗ để lưu”; dùng `EffectInstance` hiện có nếu rule thực sự cần effect.

## 11. Quy tắc làm việc cho AI tiếp theo

1. Đọc `AGENTS.md`, file này và [Decisions.md](Decisions.md) trước khi code.
2. So sánh source hiện tại với docs. Một số docs cũ mô tả trước command-skill/effect work; source và test hiện tại là bằng chứng implementation mới hơn.
3. Trước khi sửa gameplay, trace full flow: catalog/seed → frozen lineup → GameState → command/lifecycle → snapshot/replay → test.
4. Không tự thay đổi luật đã chốt, role model, timeout/action-limit, hay semantic User/Admin.
5. Giữ server authoritative; mọi boundary input phải validate phía server.
6. Giữ action/idempotency/version semantics. Không “fix” lỗi stale bằng việc bỏ version check.
7. Không commit `.env`, User Secrets, Data Protection keys, Supabase URL/password hoặc bearer token.
8. Không chạy integration test trên Supabase database đang dùng cho demo/production. `HERO_CHESS_TEST_DB` phải là database test riêng có thể bị sửa/xóa.
9. Với logic không tầm thường, thêm/sửa test nhỏ nhất chứng minh behavior. Với thay đổi docs/config đơn giản, không cần bịa test.
10. Không dùng destructive Git commands hoặc reset database nếu người dùng chưa yêu cầu rõ.

## 12. Kiểm chứng và các giới hạn đã biết

Theo kết quả ghi nhận gần nhất trong repository, build/test trước đó đã pass 42 .NET tests sau migration trait; web tests không được chạy lại khi code web không đổi. Khi thay đổi code, chạy lại check phù hợp thay vì tin số liệu lịch sử:

```powershell
dotnet build HeroChessBackend.slnx --no-restore --verbosity quiet
dotnet test HeroChessBackend.slnx --no-restore --verbosity quiet
node --test tests/web/app.test.mjs
```

Nếu API đang chạy trong Visual Studio, DLL Debug có thể bị lock; dừng API trước khi build Debug, hoặc dùng build Release để kiểm tra compile.

Các giới hạn quan trọng:

- Queue/match locks là in-memory prototype; cần distributed coordination khi deploy nhiều instance.
- Bot là heuristic hợp lệ, chưa phải bot Elo.
- Hero active skill chưa làm.
- Skill/effect mới cần test end-to-end với catalog seed và state/replay, không chỉ unit test handler.
- Production cần quản lý Data Protection keys/secrets đúng môi trường và deploy/monitoring thật.

## 13. Tài liệu tham chiếu

- [Decisions.md](Decisions.md): luật và quyết định đã chốt.
- [ImplementationStatus.md](ImplementationStatus.md): trạng thái T01–T14 và bằng chứng lịch sử.
- [Codebase_Guide_Java_to_CSharp.md](Codebase_Guide_Java_to_CSharp.md): giải thích cấu trúc C# cho người quen Java.
- [API_Testing_Guide.md](API_Testing_Guide.md): chạy, Swagger/PowerShell test, Supabase/Docker troubleshooting.
- [AddingHeroLogic.md](AddingHeroLogic.md): mở rộng Hero/trait.
- [User_Player_Admin_Refactor.md](User_Player_Admin_Refactor.md): account hierarchy + migration.
- [Conceptual_ERD_Admin_Bot.md](Conceptual_ERD_Admin_Bot.md): ERD conceptual.
- [GameplayApiImplemented.md](GameplayApiImplemented.md): endpoint gameplay; kiểm tra source khi đọc phần skill vì tài liệu này có thể cũ hơn implementation hiện tại.
