# Hero Chess — Task triển khai backend prototype trong 2 ngày

Ngày lập: 22/09/2026. Trạng thái: **đã bắt đầu triển khai T01–T04; xem `ImplementationStatus.md` để biết bằng chứng chạy thực tế**.

## 1. Mục tiêu và phạm vi chốt cho đợt này

Hoàn thành backend development có DB thật, hai tài khoản chơi được Ranked qua web test, server kiểm tra luật, reconnect/replay, bot cơ bản, mua hero và settlement đúng một lần. Bàn giao API để Unity tích hợp. Logic riêng của các hero sẽ được chủ dự án thêm dần; không lấy việc thiếu roster/skill đầy đủ làm blocker của nền backend.

File này dùng brief làm tài liệu phạm vi. Ngày 22/09/2026, chủ dự án đã yêu cầu bắt đầu triển khai T01–T04.

**Cách hiểu thời hạn:** kế hoạch tăng tốc 2 ngày, khoảng **24 giờ làm việc tập trung** gồm 21 giờ triển khai/kiểm tra theo mốc và 3 giờ dự phòng. Đây là timebox dự kiến cho một người triển khai có AI hỗ trợ, không phải cam kết chắc chắn từ repo hiện tại. Nếu chỉ có 8 giờ/ngày, phải đánh giá lại tại gate ngày 1; không đổi tên bản thiếu tính năng thành “full prototype”. Một ngày chỉ nhắm bản PvP xuyên suốt, hai ngày mới nhắm toàn bộ phạm vi bên dưới.

### Trong bản 2 ngày

- Auth thật; profile, catalog, ownership, lineup; PostgreSQL và migrations.
- Luật cờ tướng cơ bản đủ 7 class; cơ chế gắn handler hero/skill gọn, snapshot giữ được trait state.
- Ranked hai người: queue, selection kín, confirm/reveal, WebSocket, server clock, chống lệnh trùng và cạnh tranh.
- Kết quả, lịch sử, replay, reconnect, recovery sau restart theo policy development.
- Bot dùng cùng luật, undo; coin development, mua hero, rating/reward và admin API tối thiểu.
- Web test bằng chữ; tài liệu chạy và hợp đồng tích hợp Unity.
- Ba Tượng đã có mô tả dùng để kiểm chứng điểm mở rộng, không mở rộng sang các hero chưa đủ luật.

### Để sau đợt 2 ngày

- Full roster, skill riêng mới, shield/rào/hồi sinh/đẩy quân/cướp lượt chưa đủ tham số; balance giá và Elo chính thức.
- Guest và chuyển guest thành account: đề xuất hoãn vì đường nghiệm thu dùng hai account thật; vẫn chặn guest vào Ranked.
- Mua coin bằng tiền thật, payment/IAP, cửa hàng skin hoàn chỉnh, dashboard admin, Unity UI, deploy public và scale nhiều server.
- Không có Redis, microservices, generic scripting engine hoặc AI bot mạnh trong đợt này.

Việc hoãn guest là **đề xuất phạm vi của kế hoạch**, không phải quyết định gameplay đã được chủ dự án xác nhận. Admin sửa giá skin và validation skin vẫn nằm trong bản 2 ngày; endpoint mua skin không nằm trong đường nghiệm thu chính.

## 2. Hiện trạng đã kiểm tra

| Hạng mục | Bằng chứng trong repo | Ý nghĩa triển khai |
|---|---|---|
| Solution | `HeroChessBackend.slnx`, `HeroChess.Api/` | Giữ tên và cấu trúc hiện có, không tạo server khác thay thế |
| Stack khai báo | `HeroChess.Api.csproj`: net10.0; EF 10.0.12; Npgsql EF 10.0.3; Swagger 10.2.3 | Kiểm chứng restore/build ở T00; chưa kết luận package đã chạy được |
| HTTP host | `Program.cs` có controllers, Swagger, UseNpgsql, UseAuthorization | Chưa có đăng ký authentication hay các service gameplay |
| Database | `Data/AppDbContext.cs` chưa map entity; `TestController` chỉ gọi CanConnectAsync | Chưa có bằng chứng schema đã được áp dụng hay DB đang kết nối được |
| Nội dung | `Hero_Chess_DB_Prototype_v0_1/` có SQL, seed, API, mock và verification | 25 bảng là đầu vào thiết kế, không phải backend đã xong |
| Luật và test | Chưa thấy mã luật, socket hoặc test project trong danh sách file nguồn | Phải triển khai các luồng từ đầu |
| Git | Thư mục DB prototype đang untracked lúc khảo sát | Giữ nguyên dữ liệu nguồn, không ghi đè hoặc di chuyển tùy tiện |

Chưa chạy build, test, Docker hay kết nối DB trong lượt lập kế hoạch này. Kết quả 23 PASS ở tài liệu DB là kiểm tra PGlite trước đó, không phải kết quả kiểm thử backend hiện tại.

Nguồn tham chiếu: brief tại `C:\Users\VO THANH KHOA\Downloads\Hero_Chess_Codex_Backend_Brief.md`; các file `README_VI.md`, `01_schema.sql` đến `09_constraint_tests.sql` trong thư mục DB hiện tại. Khi bắt đầu code, lưu bản brief vào docs để người triển khai sau không phụ thuộc thư mục Downloads; giữ nguyên nội dung nguồn.

## 3. Cách tổ chức mã và nguyên tắc triển khai

```text
HeroChessBackend.slnx
HeroChess.Api/
  Auth/                    Identity, mapping player, ws-ticket
  Controllers/             REST /api/v1
  Data/Configurations/     EF mappings, composite keys, JSONB
  Data/Migrations/         Gameplay bootstrap + Identity
  Services/                Profile, lineup, matchmaking, economy, settlement
  Realtime/                Socket sessions, command dispatcher, match queue
  Background/              Timeout, recovery, bot scheduling
  wwwroot/                 Web test HTML/JS/CSS
HeroChess.Contracts/       netstandard2.1; DTO, protocol, error codes
HeroChess.Rules/           netstandard2.1; pure state + rules + handlers
tests/HeroChess.Rules.Tests/
tests/HeroChess.IntegrationTests/
scripts/                   Local setup, seed, smoke test
docs/                      Decisions, Runbook, UnityIntegration, status
Hero_Chess_DB_Prototype_v0_1/  Nguồn DB hiện tại, giữ nguyên
```

- Giữ ASP.NET Core hiện có. Chọn Identity làm auth mặc định vì chưa thấy auth tích hợp; kết nối PostgreSQL/Supabase không đồng nghĩa đã có Supabase Auth.
- Chọn một luồng migration; giữ trigger, CHECK, composite FK, partial unique index. Không vừa EnsureCreated vừa migration.
- Một server, một DB. Queue trong RAM; transaction ngắn cho mỗi thao tác. Không giữ transaction trong thời gian người chơi suy nghĩ.
- Server là nguồn quyết định actor, nước hợp lệ, thời gian, kết quả, giá mua và reward. Client gửi ý định, nhận snapshot.
- Mỗi task hoàn tất phải có bằng chứng trong `docs/ImplementationStatus.md`: `implemented-tested`, `implemented-not-run`, `blocked` hoặc `not-implemented`.
- Ưu tiên code + test theo lát cắt; không dành cả ngày đầu để map mọi bảng hoặc dựng abstraction chưa dùng.

## 4. Lịch thực hiện và phụ thuộc

Giờ là thời gian làm việc cộng dồn, không tính nghỉ. Test trọng yếu nằm ngay trong từng task; T15 dùng để chạy lại luồng tích hợp và bàn giao.

| Ngày / giờ | Task | Timebox | Phụ thuộc | Đầu ra bắt buộc |
|---|---|---:|---|---|
| Ngày 1, 00:00–00:30 | T00 Khảo sát môi trường, đóng phạm vi | 0,5h | — | Chọn DB dev, cách migration, policy tạm |
| 00:30–02:00 | T01 DB, solution, DTO nền | 1,5h | T00 | DB native + health + projects build |
| 02:00–03:30 | T02 Auth và onboarding | 1,5h | T01 | Hai account thật đọc được /me |
| 03:30–05:00 | T03 Catalog và lineup | 1,5h | T02 | Lưu lineup hợp lệ, reject dữ liệu sai |
| 05:00–07:30 | T04 Luật cơ bản và extension | 2,5h | T01 | Legal actions và luật an toàn Tướng |
| 07:30–08:30 | T05 Queue, selection, reveal | 1h | T03, T04 | Hai người tạo trận active snapshot 0 |
| 08:30–10:30 | T06 WebSocket và command transaction | 2h | T02, T05 | Hai client cùng state, dedup/version |
| 10:30–11:30 | T07 Web test tối thiểu | 1h | T03, T06 | Login → lineup → PvP → đi vài nước |
| 11:30–12:00 | Gate D1 + dự phòng | 0,5h | T00–T07 | Demo xuyên suốt ngày 1 |
| Ngày 2, 00:00–01:30 | T08 Timer, reconnect, restart | 1,5h | T06 | Deadline đúng, resync và recovery |
| 01:30–03:00 | T09 Kết quả và settlement | 1,5h | T08 | Kết quả và thưởng/rating đúng một lần |
| 03:00–03:45 | T10 History/replay | 0,75h | T06, T09 | Tua snapshot bất biến |
| 03:45–05:00 | T11 Bot và undo | 1,25h | T04, T06, T09 | Bot hợp lệ, undo giữ lịch sử |
| 05:00–06:00 | T12 Shop và admin API | 1h | T02, T09 | Mua hero nguyên tử, audit giá |
| 06:00–06:45 | T13 Kiểm chứng handler hero | 0,75h | T04, T11 | Ba Tượng + quy trình thêm hero |
| 06:45–07:30 | T14 Hoàn thiện web test | 0,75h | T08–T13 | Replay, bot, kết quả, socket status |
| 07:30–09:30 | T15 Regression và bàn giao | 2h | T00–T14 | Test report + runbook + Unity contract |
| 09:30–12:00 | Dự phòng sửa lỗi nghiệm thu | 2,5h | T15 | Chỉ sửa lỗi, không mở tính năng mới |

Đường phụ thuộc quan trọng: **DB → auth → lineup → trận → pipeline command → timer/kết quả → settlement → nghiệm thu**. Rules phải xong trước khi mở trận; bot và web không được tạo đường xử lý luật riêng để vượt gate.

## 5. Checklist triển khai chi tiết

### T00 — Môi trường và quyết định ban đầu

- [ ] Đọc quy tắc repo nếu có; kiểm tra git diff và giữ mọi thay đổi hiện hữu.
- [ ] Kiểm tra SDK .NET, Docker/psql và package compatibility; đối chiếu tài liệu chính thức khi restore/cài package.
- [ ] Xác định DB dev riêng. Không chạy bootstrap vào connection cloud có sẵn trước khi xác định đúng môi trường.
- [ ] Nếu DB rỗng: initial migration sử dụng SQL nguồn có xử lý transaction wrapper phù hợp. Nếu đã có schema: inspect và baseline, không DROP/CREATE lại.
- [ ] Tạo `Decisions.md` với nhóm user-confirmed / technical choice / development assumption / unresolved; ghi phạm vi và các policy ở mục 7.
- [ ] Chuẩn bị `.env.example`, secrets local và `.gitignore`; không đưa secret vào log, web hay repo.

**Đạt:** có lựa chọn môi trường và lệnh dự kiến tái hiện; thiếu DB/Docker phải ghi blocker ngay, tiếp tục Rules/Contracts khi độc lập.

### T01 — Nền DB và hợp đồng

- [x] Giữ `HeroChess.Api`, thêm Contracts/Rules và hai test project vào solution; tham chiếu một chiều, Rules không phụ thuộc DB/web/UnityEngine.
- [x] Bootstrap đủ 25 bảng `hero_chess`; Identity schema riêng. Map EF theo từng module sẽ dùng; giữ đầy đủ invariants dù chưa dùng hết bảng qua EF.
- [x] Tách catalog seed, fixture SQL và onboarding auth. Seed idempotent; fixture chỉ bật với Development + explicit flag.
- [x] Health liveness và DB readiness; lỗi API theo envelope contract, không trả exception SQL trực tiếp.
- [x] Khóa enum/DTO theo v0.1: UUID string, coin string, UTC timestamp, tọa độ Red y=0 tiến +y; Black xoay (8-x,9-y).
- [x] Snapshot chứa pieceId riêng heroId, frozen SP, trait/effect/skill state, rules/content/schema version.
- [x] Chạy migration trên PostgreSQL native rỗng; chạy lại seed, restart; chạy SQL constraint tests trên DB disposable có dev seed.

**Đạt:** build thành công, readiness báo đúng, DB không nhân đôi seed và không mất constraint.

### T02 — Auth, hồ sơ và onboarding

- [x] Register/login/refresh bằng ASP.NET Core Identity; chốt prefix `/api/v1/auth` trong bổ sung contract. Không dùng JwtBearer để kiểm tra bearer token tích hợp của Identity.
- [x] Identity subject → `auth_identity` → player; tạo wallet/rating/starter ownership nhất quán, retry không grant lần hai.
- [x] Tạo hai Identity account từ credential local; map fixture bằng bootstrap tin cậy hoặc grant fixture cho account mới, không cho public API tự chọn playerId.
- [x] `/api/v1/me` lấy đúng coin/Elo/role từ DB; chặn disabled player và private API chưa đăng nhập.
- [x] Persist Data Protection keys qua container restart; refresh/token không ghi log.
- [x] Test hai account độc lập, password sai, thiếu token, onboarding retry và playerId giả trong payload.

**Đạt:** hai account login được; coin/hero không tăng khi login lại; A không trở thành B bằng dữ liệu request.

### T03 — Catalog, ownership, lineup

- [x] `GET /catalog`, `/me/heroes`; chỉ trả nội dung được phép hiển thị, DTO có kiểu, không serialize EF entity.
- [x] `GET/POST /lineups`, `PUT/DELETE /lineups/{id}`; chỉ thao tác dữ liệu của actor.
- [x] Validate đủ 16 slot, đúng số lượng/class, không trùng slot/nhân vật lịch sử; mẫu dev dùng 16 hero ID khác nhau.
- [x] Validate budget ≤50 SP, ownership, enabled, skin sở hữu và khớp hero, đúng 3 skill khác nhau, tối đa 1 faction active.
- [ ] Validate faction eligibility chi tiết khi catalog có điều kiện đã được chốt; hiện fixture không có faction/eligibility để suy ra luật.
- [x] Save nguyên tử; update/delete kiểm tra expectedRevision; tính lại validation khi đọc thay vì tin trạng thái đã lưu cũ.
- [x] Fixture 43 SP lưu thành công; lineup thiếu quân bị từ chối và không để lại dữ liệu nửa chừng.
- [ ] Bổ sung integration cases sai class/vượt SP/skill trùng/hero khóa/chưa sở hữu ở lượt hardening T03.
- [x] Test A không đọc/sửa/xóa lineup riêng B; hai lần update cùng revision chỉ một lần thành công.

**Đạt:** account thật dùng được fixture qua API; dữ liệu sai bị từ chối trước commit.

### T04 — Luật cơ bản và điểm mở rộng hero

- [x] GameState thuần dữ liệu, action có kiểu, `GenerateLegalActions`, validate/apply/result; cùng input/config/action cho cùng output.
- [x] Tướng trong cung + lộ mặt; Sĩ chéo trong cung; Tượng cơ bản chéo 2, cản mắt, không qua sông.
- [x] Xe đường trống; Pháo ăn đúng một ngòi; Mã có cản chân; Tốt tiến/qua sông đi ngang theo vị trí hiện tại, không lùi/phong cấp.
- [x] Loại nước làm Tướng mình không an toàn; chiếu bí/hết hành động hợp lệ kết thúc, không đợi ăn Tướng.
- [x] Handler registry theo implementationKey; base movement và special move có đường chọn rõ, không tự cộng trait bổ sung.
- [x] Chuẩn bị traitState/effects/obstacles/pendingEffects/skillStates trong snapshot nhưng không tự viết luật hiệu ứng chưa chốt.
- [x] Skill chưa code trả `SKILL_NOT_IMPLEMENTED`; không đổi lượt/version/counter/charge. Không coi null charge là vô hạn.
- [x] Legal action generation xét handler đã hỗ trợ; bot và server cùng dùng một nguồn.
- [x] Unit test từng class, biên bàn/cung/sông, quân đồng minh, cản, self-check, hết action; giữ test case hai bên Red/Black.

**Đạt:** luật di chuyển cơ bản có test PASS; thêm hero sau này không phải sửa controller/socket.

### T05 — Matchmaking và khóa đội hình

- [x] Create/cancel matchmaking ticket theo contract; queue Elo trong RAM với cửa sổ Elo cấu hình rõ; thiếu người thì chờ.
- [x] Reservation nguyên tử: một player không có hai ticket/trận selecting hoặc active; xử lý cancel cạnh tranh với match, giải phóng reservation khi kết thúc/hủy.
- [x] Ghi game_match và participants hợp lệ trong transaction; Ranked chỉ hai registered human, bot đi nhánh mode riêng.
- [x] Selection kiểm tra ownership/revision; trước reveal chỉ công khai skill và trạng thái confirm, không lộ hero qua REST/socket/error/replay.
- [x] Confirm idempotent; xác thực lại dữ liệu và đóng băng snapshot; phía đã khóa không sửa selection. Nếu cập nhật catalog làm snapshot hai bên không tương thích, từ chối start rõ ràng.
- [x] Cả hai confirm → state version 0 + start sequence 0 + active cùng transaction; snapshot không đổi khi sửa/xóa lineup nguồn.

**Đạt:** hai account vào cùng trận; hero chỉ lộ sau hai bên khóa; test queue/confirm đồng thời không tạo trùng.

### T06 — WebSocket và pipeline command

- [x] `POST /api/v1/ws-ticket`: vé ngắn hạn, một lần, gắn user; `/ws/v1` kiểm tra vé và Origin/host, không token dài hạn trên URL.
- [x] Hỗ trợ `match.subscribe`, `match.command` và các event v0.1; xác thực quyền match trước mọi lookup command/resync.
- [x] Một dispatcher tuần tự mỗi match cho command người chơi, bot và timer; nhận đủ message → gắn receivedAt + thứ tự ingress trước xử lý.
- [x] Transaction: quyền actor → dedup matchId/commandId + actor/payload → expectedVersion → luật → action/state/status → commit → broadcast.
- [x] Retry command đã commit trả kết quả cũ trước khi reject staleVersion; key cũ đổi payload/actor trả conflict, không rò kết quả riêng.
- [x] Version và log sequence cùng tiến theo schema hiện tại; turnIndex/countedActions giữ ý nghĩa riêng, không dùng một biến cho cả bốn trường.
- [x] Không dùng DbContext chung cho socket/task; serialize socket sends; heartbeat, giới hạn size/rate, cleanup và xử lý malformed message.
- [x] Từ chối client gửi start/timeout/cancel hay tự chọn actorSide. Resign đi qua pipeline chung.
- [x] Integration test hai ClientWebSocket thật: đi đúng/sai lượt, retry, hai command cùng version, người thứ ba subscribe, DB commit lỗi không broadcast success.

**Đạt:** chỉ một command cạnh tranh được áp dụng; cả hai client nhận cùng version/snapshot sau commit.

### T07 — Web test ngày 1

- [x] Một trang `wwwroot`: login, catalog, chọn/lưu lineup, queue/cancel, confirm, bàn giao điểm 9×10, log JSON/version.
- [x] Token trong memory; hai cửa sổ/context đăng nhập hai account riêng. Reload có thể yêu cầu login lại.
- [x] Click quân và đích gửi action; hiển thị lỗi server. Có thể bổ sung legal-actions endpoint có auth và version, ghi rõ vào contract.
- [x] Dùng snapshot server để vẽ; không viết luật game hay tính tiền/rank ở JavaScript.

**Gate D1:** hai account → lineup → matched → confirm → active → mỗi bên đi vài nước; sai lượt bị chặn; retry không đi hai lần. Nếu chưa đạt, ưu tiên sửa đường này trước khi bắt đầu shop/admin.

### T08 — Đồng hồ, reconnect và restart recovery

- [x] Server clock có thể điều khiển trong test; deadline 90 giây lưu state. UI chỉ hiển thị dựa deadline/serverNow.
- [x] Timer đưa timeout vào dispatcher T06; không tự sửa state bên ngoài khóa/queue. Arbitration theo policy mục 7, timer kiểm tra version/deadline hiện hành.
- [x] Hết giờ không chiếu: append timeout, mất lượt và tăng countedActions. Đang chiếu: thua ngay; hai timeout liên tiếp trên lượt riêng của cùng người chơi: thua AFK (luật chốt 23/09/2026).
- [x] Reconnect với socket mới xác thực cùng player, subscribe nhận state/version hiện hành; client bỏ snapshot cũ, retry command pending bằng ID cũ.
- [x] Không hủy trận chỉ vì rớt socket; đồng hồ tiếp tục khi process vẫn chạy.
- [x] Startup recovery hủy selecting/active còn sót với `server_restart`, đồng bộ row/snapshot/log phù hợp trạng thái; selecting chưa có state/start không được tạo log sai sequence 0.
- [x] Test command trước/đúng/sau deadline, queue bận, timer lặp và disconnect sau commit trước broadcast; không chờ thật 90 giây.

**Đạt:** resync đúng khi process còn sống; restart có trạng thái hủy nhất quán và không phát thưởng ngoài ý muốn.

### T09 — Kết quả, giới hạn 150 và settlement

- [x] Resign, chiếu bí/hết legal action, draw/cancel; match row, state row và state_after thống nhất tại commit.
- [x] Action thứ 150: xử lý kết quả trực tiếp trước; nếu chưa kết thúc so tổng frozen SP của quân còn sống, bằng nhau hòa.
- [x] Tách RatingPolicy và RewardPolicy; config fixture Development, ghi phiên bản/giá trị dùng trong kết quả hoặc snapshot cần thiết để recovery không đổi policy.
- [x] Settlement khóa game_match, kiểm tra settled_at, khóa wallet/rating theo thứ tự player UUID; ghi ledger + stats + settled_at nguyên tử.
- [x] Draw/cancel/reward 0 vẫn settled; không ledger amount=0; bot không đổi Ranked Elo theo policy development đề xuất.
- [x] Recovery xử lý trận đã kết thúc chưa settled sau crash; phát match.ended rồi match.settled đúng dữ liệu, resync đọc được kết quả nếu mất event.
- [x] Test retry settlement, hai worker gọi cùng lúc, rollback lỗi giữa chừng và restart ở khoảng sau result/trước settlement.

**Đạt:** kết quả không đổi khi retry; coin/Elo/stats chỉ cập nhật một lần, cancelled không tăng games_played theo policy tạm.

### T10 — Lịch sử và replay

- [x] `GET /matches` theo actor + cursor/limit; `GET /matches/{id}/replay` có quyền truy cập và phân trang.
- [x] Đề xuất chỉ mở replay cho participant sau trận terminal để tránh bypass selection kín; ghi bổ sung contract.
- [x] Replay đọc start sequence 0 và state_after; không chạy lại luật/catalog hiện tại, không phụ thuộc source lineup.
- [x] Test sửa giá/SP catalog và xóa lineup sau trận: snapshot lịch sử vẫn như cũ; snapshot đầy đủ trait/effect/skill/result.

**Đạt:** tua được đầu/cuối/từng event, phân trang không thiếu/trùng sequence; người ngoài không xem được replay riêng.

### T11 — Bot và undo

- [x] Bot chọn trong legal actions, ưu tiên ăn quân theo SP và tie-break ổn định; không cần minimax sâu, không DB trong search.
- [x] Bot có giới hạn thời gian, dùng copy state, kiểm tra version trước enqueue; action vào dispatcher chung, không ghi thẳng DB.
- [x] Bot match đúng một human và một bot; không ghép vào Ranked; không còn action thì kết thúc theo Rules.
- [x] Undo chỉ active bot match: validate targetSequence là snapshot hợp lệ trước đó, khôi phục board/trait/effect/skill/counter và sideToMove.
- [x] Append undo event mới, version/sequence tăng, không DELETE log; hủy kết quả bot search cũ. Đề xuất deadline mới 90 giây từ lúc undo để tránh snapshot cũ hết hạn ngay.
- [x] Test undo sau move/timeout và trait state; Ranked/terminal match bị từ chối, bot không đi tiếp bằng version cũ.

**Đạt:** chơi được bot và undo mà state gameplay khôi phục đúng, lịch sử vẫn đầy đủ.

### T12 — Shop hero và admin tối thiểu

- [x] Mua hero theo endpoint contract và Idempotency-Key; server lấy giá/enable hiện hành, không nhận coin price từ client.
- [x] Lock wallet → kiểm tra key/payload → trừ tiền + ownership + ledger cùng transaction; thiếu tiền hoặc lỗi phải rollback.
- [x] Hero 0 coin: grant idempotent theo ownership, không ledger 0; quyết định cách giữ key/payload cho retry miễn phí bằng migration nhỏ nếu cần, ghi rõ giới hạn hợp đồng trước khi code.
- [x] Test hai lần mua đồng thời/key trùng/key đổi hero, insufficient balance, already owned và hero disabled; không âm ví.
- [x] Coin test cấp qua bootstrap/ledger có key cố định trong Development, không endpoint public nhập số dư tùy ý.
- [x] Admin policy kiểm tra role tin cậy; endpoint sửa giá hero/skin và đọc audit có pagination. Giá trước/sau + actor + reason lưu cùng transaction; không có API tự phong admin.
- [x] Test player thường nhận 403; role admin chỉ seed từ cấu hình local được cho phép; chỉnh giá không đổi SP/replay trận cũ.

**Đạt:** luồng mua và phân quyền được kiểm chứng qua API, không cần giao diện shop/admin riêng.

### T13 — Bàn giao điểm mở rộng hero

- [x] Đăng ký Trần Bình Trọng/Bùi Thị Xuân: chéo 1–2 trong sân nhà, không vượt cản.
- [x] Đăng ký Dã Tượng: chéo 1–4 trong sân nhà, không vượt cản, không lặp độ dài lần trước; first move không có khoảng cách cũ.
- [x] Overlay Development grant/enable ba hero từ catalog để test thay slot Tượng; không sửa source seed thành giá/roster phát hành chính thức.
- [x] Test legal actions, apply, snapshot round-trip và undo của lastMoveDistance; không tự gắn trait khác cho special move.
- [x] Viết `docs/AddingHeroLogic.md`: khai báo implementationKey/parameters → handler → legal actions → snapshot state → unit test → dev overlay → bật khi đủ luật.
- [x] Liệt kê skill thiếu luật và lỗi SKILL_NOT_IMPLEMENTED; không phải viết đủ 12–14 hero để đóng task nền backend.

**Đạt:** có ví dụ handler thực tế và checklist để chủ dự án bổ sung từng hero độc lập.

### T14 — Hoàn thiện web test

- [x] Hiển thị legal targets từ server, đồng hồ, reconnect/socket/version, resign, win/draw/cancel và settlement pending/completed.
- [x] History/replay với nút đầu/trước/sau/cuối; mode bot và undo chọn target hợp lệ.
- [x] Skill fixture hiển thị chưa triển khai; không nút báo thành công giả.
- [x] Nếu nhận stale state: lấy snapshot mới; không tự resend action bằng commandId mới khiến đi hai lần.
- [x] Smoke hai phiên thật; mở lại trang → login lại nếu cần → resync đúng trận.

**Đạt:** người test có thể tự chạy toàn bộ luồng mà không sửa JSON bằng tay cho thao tác cơ bản.

### T15 — Regression, tài liệu và đóng bản

- [ ] Restore/build/test bằng phiên bản đã ghi; Rules unit tests và integration tests chạy với PostgreSQL thật. Không thay EF InMemory để chứng minh transaction/FK.
- [ ] Có script setup DB mới, migration, catalog/dev seed, smoke hai account và cleanup chỉ dữ liệu test được xác định.
- [ ] Xuất OpenAPI từ code và cập nhật protocol WebSocket; ghi mọi endpoint bổ sung, error/status, nullable và coin string.
- [ ] `Runbook.md`: setup, cấu hình secret local, launch, dev users, mở hai phiên, restart, xem log và lỗi thường gặp; chỉ đánh dấu lệnh đã kiểm chứng đúng môi trường.
- [ ] `UnityIntegration.md`: auth/refresh/ws-ticket, DTO/toạ độ, connect/subscribe/move, reconnect/retry/version, đọc settlement; không cần làm Unity client.
- [ ] `ImplementationStatus.md`: từng module, test/lệnh thực tế, PASS/FAIL/NOT RUN, limitation và skill chưa triển khai.
- [ ] Hoàn thành ma trận nghiệm thu mục 8; ghi rõ bất kỳ task còn thiếu thay vì tick theo số file tạo ra.

**Đạt:** backend chạy lại từ hướng dẫn trên DB dev mới; có bằng chứng luồng hai account, atomicity/retry, bot/economy và bàn giao Unity.

## 6. Bảng API cần bám khi code

Các đường dưới đây có prefix `/api/v1`, ngoại trừ WebSocket.

| Nhóm | Hợp đồng có sẵn / phần cần bổ sung | Task |
|---|---|---|
| Auth | Bổ sung `/auth/register`, `/auth/login`, `/auth/refresh`; giữ GET `/me` | T02 |
| Content | GET `/catalog`, GET `/me/heroes` | T03 |
| Lineup | GET/POST `/lineups`, PUT/DELETE `/lineups/{id}` | T03 |
| Matchmaking | POST `/matchmaking/tickets`, DELETE `/matchmaking/tickets/{id}` | T05 |
| Selection | PUT `/matches/{id}/selection`, POST `/matches/{id}/confirm` | T05 |
| Socket | POST `/ws-ticket`; WS `/ws/v1`, match.subscribe, match.command | T06 |
| State | GET `/matches/{id}/state`; đề xuất GET `/matches/{id}/legal-actions` có version | T06–T08 |
| History | GET `/matches`, GET `/matches/{id}/replay` | T10 |
| Economy | POST `/shop/heroes/{heroId}/purchase` | T12 |
| Admin | Đề xuất PATCH `/admin/heroes/{id}/price`, PATCH `/admin/cosmetics/{id}/price`, GET `/admin/audit` | T12 |

Resign/undo/skill là action qua match.command, không tạo service xử lý thứ hai qua REST. Health endpoints và route bổ sung phải được ghi vào tài liệu thực tế. Không đổi ID seed hoặc enum casing để tiện implementation.

## 7. Quyết định development cần ghi trước khi áp dụng

Một số policy ban đầu là đề xuất/tạm. Luật timeout/AFK, đếm 150 và quyền skill phe đã được chủ dự án chốt ngày 23/09/2026; xem `Decisions.md`. Các giá trị kinh tế và chi tiết skill còn mở.

| Điểm mở | Đề xuất để chạy prototype | Task xác nhận bằng test |
|---|---|---|
| Đếm 150 | Tổng nước của hai bên: move/skill hợp lệ/timeout đều tăng 1; skill thay nước đi. Start/selection/reconnect không tăng; undo phục hồi counter. Luật chốt 23/09/2026 | T08–T09 |
| Hết giờ / AFK | Hết giờ thường mất lượt; hai lần liên tiếp trên lượt của chính người chơi thua AFK; hết giờ trong chiếu thua ngay. Luật chốt 23/09/2026 | T08–T09 |
| Deadline cùng lúc command | Command nhận đầy đủ tại hoặc sau deadline thua timeout; trước deadline được ưu tiên; tie có thứ tự ingress server, không dựa thời gian client | T06, T08 |
| Restart server | Hủy selecting/active còn sót bằng `server_restart`; giữ history, settled không thưởng | T08–T09 |
| Elo/reward chưa chốt | Config fixture riêng cho Development; không tự nhận là balance chính thức; bot không đổi Ranked Elo, cancel không tăng games_played | T09 |
| Undo deadline | Khôi phục gameplay snapshot, cấp deadline mới theo turnSeconds tại thời điểm undo | T11 |
| Replay quyền truy cập | Participant đọc sau terminal; chọn policy này để tránh rò selection qua history | T10 |
| Nội dung thiếu handler | Fixture được chọn ở dev để đủ 3 slot; dùng skill phải bị từ chối, không consume gì | T03–T04 |
| Faction chưa đủ điều kiện | Không tự suy faction từ tên hero; chỉ bật rule eligibility đã có cấu hình rõ, chốt eligibility tại start | T03, T05 |

Ngoài Development, kiểm tra startup/config và từ chối bật nội dung/policy chưa đủ để phát hành. Không coi phần này là yêu cầu triển khai production trong 2 ngày.

## 8. Ma trận nghiệm thu cuối ngày 2

Mỗi dòng cần ghi test name hoặc bước smoke và kết quả thực tế trong ImplementationStatus; hiện tại tất cả là chưa chạy.

| ID | Tình huống | Kết quả mong đợi |
|---|---|---|
| A01 | DB rỗng → migrate → seed → restart → seed lại | Đủ schema, không seed lặp, constraints giữ nguyên |
| A02 | Hai account + login sai + private API không token | Hai identity khác nhau; sai auth bị chặn |
| A03 | A truy cập lineup/match riêng B hoặc giả actor | Không đọc/sửa dữ liệu trái quyền |
| A04 | Lineup thiếu slot/sai class/SP/skill/ownership | Reject, không ghi một phần |
| A05 | Bộ luật 7 class, cản quân, cung/sông, chiếu | Legal action đúng, không tự để Tướng bị chiếu |
| A06 | Queue hai account + confirm + soi payload trước reveal | Một trận, kín hero đến khi cả hai khóa |
| A07 | Hai lệnh cùng expectedVersion | Tối đa một thay đổi state |
| A08 | Retry command/purchase và key cũ đổi payload | Không áp dụng lần hai; payload khác bị conflict |
| A09 | Timeout cạnh tranh command, clock giả | Kết quả ổn định theo policy, không timeout đôi |
| A10 | Disconnect sau commit, reconnect socket khác | Đúng snapshot/version, retry không nhân đôi |
| A11 | Restart lúc active / sau ended trước settled | Hủy active đúng policy / tiếp tục settlement đúng một lần |
| A12 | Action limit, resign, mate, draw/cancel | State/log/result khớp; reward 0 vẫn settled |
| A13 | Replay sau sửa catalog/xóa lineup | Bàn, SP, trait và kết quả lịch sử giữ nguyên |
| A14 | Hai purchase/settlement đồng thời | Ví không âm, ledger/ownership/Elo không nhân đôi |
| A15 | Bot + undo + bot search đang chạy | Nước hợp lệ, state khôi phục, version/log vẫn tăng |
| A16 | Ba Tượng và Dã Tượng undo | Khoảng cách/cản/sân nhà và traitState đúng |
| A17 | Player thường gọi admin; admin sửa giá | 403 / cập nhật kèm audit nguyên tử |
| A18 | Web hai phiên chơi, resign, replay, bot | Demo xuyên suốt bằng backend/DB thật |
| A19 | Build và runbook từ môi trường dev sạch | Lệnh tái hiện được; bước chưa chạy ghi NOT RUN |

**Định nghĩa hoàn thành:** các task trong phạm vi 2 ngày và các kiểm tra tương ứng đã đạt; handler thiếu đặc tả được công khai là chưa triển khai. Chỉ build PASS hoặc seed SQL PASS chưa đủ gọi backend prototype hoàn thành.

## 9. Khi có nguy cơ trễ

- Nếu môi trường DB mất quá 30 phút xử lý: ghi blocker, tiếp tục Contracts/Rules, chuẩn bị setup DB local; không lấy DB giả làm bằng chứng persistence.
- Nếu Gate D1 chưa đạt: dùng dự phòng sửa auth/lineup/PvP trước; chưa mở rộng hero, làm đẹp web hay tối ưu bot.
- Khi cần giảm thời gian: giảm độ sâu bot về chọn legal action tốt nhất một bước, dùng API để test shop/admin, giữ web tối thiểu. Không cắt auth, validation, transaction, dedup hoặc test concurrency.
- Ba Tượng đã mô tả là mục tiêu kiểm chứng extension; nếu còn lỗi ở nền PvP, ưu tiên nền và ghi rõ T13 còn thiếu, không âm thầm bỏ khỏi checklist nghiệm thu.
- Nếu hết ngày 2 vẫn còn T08–T15 chưa đạt: bàn giao chính xác bản đã chạy, task còn lại và blocker. Không tuyên bố “full prototype” bằng cách dời bot/economy/replay ra ngoài phạm vi sau khi code.

## 10. Cách dùng file task trong lúc triển khai

Làm theo thứ tự T00 → T15. Sau mỗi task: ghi đầu ra, lệnh/test đã chạy, kết quả và quyết định tạm; chỉ tick checkbox khi đã hoàn thành phần mô tả. Không cần hỏi lại để tiếp tục các task khi chủ dự án đã giao triển khai toàn lộ trình.

Chủ dự án thêm hero theo `AddingHeroLogic.md` sau này; phần backend vẫn dùng chung auth, lineup, pipeline command, snapshot, replay, bot action generator và settlement. Mỗi hero mới chỉ được bật khi handler cùng test của nó đã sẵn sàng.


