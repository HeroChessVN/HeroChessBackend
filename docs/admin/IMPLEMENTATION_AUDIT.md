# Hero Chess — Rà soát triển khai module Admin

Phạm vi tài liệu: **phần backend** (API, authorization, service, validation, migration) và **AdminWeb** (React + TypeScript) đã triển khai ở giai đoạn frontend.

Ngày: 2026-11-08 · Mã sự án: FA26SE307

---

## 1. Thành phần đã có sẵn (được tái sử dụng)

| Hạng mục | Thành phần | Vị trí | Ghi chú |
|---|---|---|---|
| Máy chủ web | ASP.NET Core 10, JWT bearer, Swagger | `HeroChess.Api/Program.cs` | Không tạo backend thứ hai. |
| Truy cập dữ liệu | EF Core 10 + Npgsql, schema `hero_chess` | `HeroChess.Api/Data/AppDbContext.cs` | `EnsureCreated` không được dùng; schema do SQL bootstrap quản lý. |
| Định danh | ASP.NET Core Identity + `UserStore<User>`, TPH `user_account` | `HeroChess.Api/Auth/`, `Data/UserStore.cs` | Role `player` / `admin` cột `role`. |
| Chính sách phân quyền | `AccountPolicies` + `AccountAuthorizationHandler` | `HeroChess.Api/Auth/AccountPolicies.cs` | **Đã mở rộng** trong giai đoạn này: policy read/write theo miền. |
| Nền tảng Admin | `AdminService` (đổi giá hero/cosmetic, đọc audit) | `HeroChess.Api/Services/AdminService.cs` | `admin_audit_log` đã có sẵn, tương thích dùng lại. |
| Cửa hàng người chơi | `ShopService` (mua hero), `CoinTransaction`, `player_wallet` | `HeroChess.Api/Services/ShopService.cs` | Mua hero đã có; ledger đã có `kind='admin_adjustment'`. |
| Sở hữu | `player_hero.acquired_via` đã có giá trị `'admin'` | `01_schema.sql` | Dùng để phân biệt hero admin cấp (được thu hồi) với hero mua/starter (không thu hồi). |
| Trận đấu | `game_match`, `match_participant`, `match_action`, `match_state` | `01_schema.sql` | `ruleset_snapshot` jsonb đóng băng luật tại thời điểm trận. |
| Xếp hạng | `player_rating` (Elo) | `01_schema.sql` | |
| Mã hoá con trỏ phân trang | `CursorCodec` (keyset pagination) | `HeroChess.Api/Infrastructure/` | Tái dùng cho toàn bộ danh sách admin. |
| Bộ ghi nhật ký | `AdminAuditWriter` | `HeroChess.Api/Services/` | Ghi trước/after JSON + reason. |
| Bộ luật | `ruleset`, `uq_one_active_ruleset` | `01_schema.sql` | Ruleset được đóng băng vào trận → sửa ruleset không phá lịch sử. |
| Bộ chạy bootstrap | `DatabaseBootstrapHostedService` chạy SQL theo thứ tự, advisory lock | `HeroChess.Api/Data/Bootstrap/` | Đã nối `13_admin_module.sql` vào chuỗi. |

## 2. Thành phần còn thiếu (trước giai đoạn này)

| # | Thiếu | Vì sao cần |
|---|---|---|
| M1 | Bảng quyền phân quyền chi tiết (fine-grained) cho admin | Chỉ có role `admin` là toàn quyền; không có phân tách đặc quyền tối thiểu (least-privilege). |
| M2 | Bảng gift code + lịch sử quy đổi | Yêu cầu mục 8 (Gift Code Management). |
| M3 | Cột `hero.is_in_shop` | `is_enabled` gộp cả "được chơi" và "được bán"; admin cần tắt bán mà không ảnh hưởng gameplay. |
| M4 | API liệt kê/tìm kiếm người chơi (admin) | Chỉ có API của chính người chơi. |
| M5 | API ban/mở khoá tài khoản | Cột `status` có sẵn nhưng không endpoint nào đổi. |
| M6 | API điều chỉnh coin, cấp/thu hero | Schema đã sẵn sàng (`admin_adjustment`, `acquired_via='admin'`) nhưng chưa có API. |
| M7 | API quản lý catalog (hero/skill/phe) ở góc nhìn admin | `GET /api/v1/admin/catalog` lọc `is_enabled=true`, thiếu cosmetic/trait. |
| M8 | API quản lý shop (giá hàng loạt, bật/tắt bán) | Chỉ có đổi giá đơn lẻ. |
| M9 | API trình xem trận (match inspector) | Admin không xem được trận của người khác. |
| M10 | API phân tích (cân bằng, bot) | Không có luồng dữ liệu (pipeline) số liệu. |
| M11 | API ruleset/scenario (chỉ đọc) | Không có endpoint xem bộ luật. |
| M12 | API quản lý tài khoản admin + quyền | Không có đường cấp quyền đặc quyền tối thiểu (least-privilege). |

## 3. Thay đổi đề xuất → đã triển khai

### 3.1 Lược đồ dữ liệu (migration `13_admin_module.sql`)

| Thay đổi | Loại | Ghi chú an toàn |
|---|---|---|
| `CREATE TABLE hero_chess.admin_permission` | Bổ sung | Không đụng bảng cũ. |
| `CREATE TABLE hero_chess.gift_code` | Bổ sung | `CHECK` ràng buộc loại thưởng khớp với target. |
| `CREATE TABLE hero_chess.gift_code_redemption` | Bổ sung | `UNIQUE (gift_code_id, player_id, redemption_no)` chống thưởng trùng. |
| `ALTER TABLE hero_chess.hero ADD COLUMN is_in_shop` | Bổ sung | `DEFAULT true`, `NOT NULL` → dữ liệu cũ không sai. |
| 3 index trên `gift_code` / `gift_code_redemption` | Bổ sung | Theo đúng mẫu truy vấn (query pattern) đã viết. |
| `REVOKE` cho `PUBLIC` / `anon` / `authenticated` | Bổ sung | Khớp quy ước "mọi truy cập qua ASP.NET API" của repo. |

Không có `DROP`, không có `RENAME`, không có `UPDATE` dữ liệu lịch sử. Script chạy lại được (idempotent) và dùng `pg_advisory_xact_lock(hashtext('hero_chess_admin_module_v1'))` để không tranh chấp khi nhiều instance khởi động cùng lúc.

### 3.2 Backend (file mới)

| File | Vai trò |
|---|---|
| `HeroChess.Contracts/AdminContracts.cs` | Toàn bộ DTO request/response của Admin. |
| `HeroChess.Api/Data/AdminEntities.cs` | `AdminPermission`, `GiftCode`, `GiftCodeRedemption` + ánh xạ EF. |
| `HeroChess.Api/Auth/AccountPolicies.cs` | (đã có) Mở rộng: policy `admin-read:{domain}` / `admin-write:{domain}`. |
| `HeroChess.Api/Services/AdminCatalogService.cs` | Hero/skill/phe/ruleset. |
| `HeroChess.Api/Services/AdminShopService.cs` | Cửa hàng + đổi giá hàng loạt + lịch sử mua. |
| `HeroChess.Api/Services/AdminPlayerService.cs` | Người chơi: ban/mở khoá, ví, cấp/thu hero. |
| `HeroChess.Api/Services/AdminMatchService.cs` | Trình xem trận (match inspector). |
| `HeroChess.Api/Services/AdminAnalyticsService.cs` | Số liệu cân bằng + bot. |
| `HeroChess.Api/Services/AdminGiftCodeService.cs` | Gift code + quy đổi người chơi (nguyên tử). |
| `HeroChess.Api/Services/AdminAccountService.cs` | Tài khoản admin + quyền. |
| `HeroChess.Api/Controllers/Admin*.cs` | 7 controller admin + 1 controller quy đổi gift code của người chơi. |
| `tests/HeroChess.IntegrationTests/AdminModuleTests.cs` | Bộ test tích hợp cho module admin. |

### 3.3 An toàn dữ liệu & xử lý đồng thời

| Vấn đề | Cách xử lý |
|---|---|
| Quy đổi gift code song song | `SELECT ... FOR UPDATE` trên `gift_code` + `player_wallet`, toàn bộ trong 1 transaction; thêm ràng buộc unique `(gift_code_id, player_id, redemption_no)`. |
| Cộng/trừ coin âm | Khoá `player_wallet` bằng `FOR UPDATE`, kiểm tra `balance >= 0`, ghi ledger `admin_adjustment` cùng transaction. |
| Thu hồi hero | Chỉ thu được hero có `acquired_via='admin'`; hero mua/starter/test không đụng tới. |
| Giảm `max_redemptions` | Từ chối nếu xuống dưới số lượt đã quy đổi (`409 MAX_REDEMPTIONS_BELOW_USED`). |
| Đổi giá hero | Ghi audit trước/sau; `AdminCatalogService` đọc `admin_audit_log` để dựng lịch sử. |
| Ban tài khoản | `AccountPolicies` + handler WebSocket đã tôn trọng `status='active'` → ban có hiệu lực ngay ở request kế tiếp. |
| Tự ban chính mình | `AdminPlayerService.SetStatusAsync` từ chối với `SELF_BAN_FORBIDDEN`. |

## 4. Yêu cầu migration cơ sở dữ liệu

- File: `HeroChess.Api/Data/Bootstrap/13_admin_module.sql`.
- Đã nối vào `DatabaseBootstrapHostedService.StartAsync` sau `12_custom_rooms.sql`.
- Chạy tự động khi `DatabaseBootstrap__Enabled=true`.
- **Chưa áp dụng vào database production.** Bản production cần được review và chạy thủ công (script idempotent nên chạy lại an toàn).

```sql
-- Áp dụng thủ công cho production
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_admin_module_v1'));
\i 13_admin_module.sql   -- nội dung file
COMMIT;
```

## 5. Điểm chưa chắc chắn về quy tắc nghiệp vụ (cần team xác nhận)

| # | Câu hỏi | Trạng thái hiện tại |
|---|---|---|
| B1 | Setup Points còn áp dụng không? | `hero.setup_points` vẫn tồn tại, API admin **đọc và hiển thị** nhưng **không enforce** luật gameplay nào. Đúng theo yêu cầu "không kích hoạt lại". Tuy nhiên DB có CHECK `hero_check (class_code = 'GENERAL' OR setup_points >= 1)` → server **phải** validate trước khi ghi, nếu không sẽ thành `500 INTERNAL_ERROR`. Đã thêm `AdminValidation.RequireSetupPoints` (dùng chung cho create/update) → `400 INVALID_SETUP_POINTS`. |
| B2 | 1 hero tối đa 1 phe — có ép bằng DB không? | `hero_faction` **không** có unique theo hero. Service chỉ chặn ở tầng ứng dụng. Cần migration unique nếu xác nhận. |
| B3 | Quyền `read` = "xem toàn bộ" hay "đọc tất cả miền"? | Đang hiểu là đọc tất cả miền. Không có dòng `admin_permission` = admin toàn quyền (tương thích ngược). |
| B4 | Có cho phép admin tạo tài khoản admin qua API không? | Đã cho (`POST /api/v1/admin/accounts`) — cân nhắc thêm 2FA/audit ngoài trước production. |
| B5 | Gift code thưởng cosmetic có khớp `player_cosmetic` không? | Đã dùng `player_cosmetic`; nếu bảng này không tồn tại ở production cần kiểm tra lại. |
| B6 | Trận bất đối xứng (asymmetric) — số quân khác nhau | Trình xem trận **không** giả định 16 quân mỗi phe; hiển thị `HeroCount` thực tế. Công thức fairness chưa được định nghĩa → không implement. |
| B7 | Bot latency / invalid action có được đo không? | `match_action` không lưu latency. Phân tích bot chỉ có win-rate theo độ khó và theo ruleset. |

## 6. Thứ tự ưu tiên triển khai (đã theo đúng thứ tự này)

1. **Quyền + audit nền tảng** — `admin_permission`, `AccountPolicies` read/write theo miền. Mọi thứ sau đều dựa vào đây.
2. **Gift code** — làm trước vì rủi ro kinh tế cao nhất, cần transaction + ràng buộc unique.
3. **Quản lý người chơi** — ban/mở khoá, ví, cấp/thu hero.
4. **Catalog + cửa hàng** — hero/skill/phe, giá, bật/tắt bán.
5. **Trình xem trận + phân tích** — chỉ đọc.
6. **Tài khoản admin + quyền** — đường cấp quyền đặc quyền tối thiểu (least-privilege).

## 7. Độ phủ kiểm thử

`tests/HeroChess.IntegrationTests/AdminModuleTests.cs` — 8 test, 9 tình huống nghiệp vụ:

| Test | Phủ |
|---|---|
| `Admin_api_requires_admin_role_and_player_cannot_reach_admin_endpoints` | 401 cho ẩn danh, 403 cho người chơi. |
| `Admin_can_list_players_ban_and_unban_with_audit_reason` | Ban → login bị chặn → mở khoá → audit có lý do. |
| `Admin_can_adjust_wallet_and_grant_then_revoke_only_admin_granted_heroes` | Điều chỉnh coin, cấp hero, không thu được hero mua/starter. |
| `Admin_shop_update_changes_price_and_availability_and_batch_reports_errors` | Đổi giá, bật/tắt bán, báo lỗi từng dòng trong batch, giá không hợp lệ. |
| `Gift_code_redemption_is_atomic_and_rejects_duplicate_and_expired` | Quy đổi chạy song song → đúng 1 lần thành công; trùng → 409; hết hạn → 409. |
| `Restricted_admin_cannot_access_unpermitted_domains` | Admin thiếu quyền bị 403. |
| `Admin_can_read_catalog_matches_rulesets_and_analytics` | Endpoint đọc trả dữ liệu thật, không mock. |
| `Admin_hero_setup_points_follows_db_rule_non_general_requires_one` | Chặn regression lỗi 500 khi `setupPoints = 0` với lớp khác `GENERAL`. |

## 8. Lỗi phát hiện khi chạy toàn bộ bộ test (đã sửa)

Hai lỗi dưới đây không liên quan Admin nhưng chặn việc xác nhận "toàn bộ bộ test xanh", nên đã sửa trong giai đoạn backend:

| Lỗi | Nguyên nhân gốc | Sửa |
|---|---|---|
| `BotWorkQueue.Stop()` đóng channel vĩnh viễn | `BotTurnScheduler` là singleton; `Stop()` gọi `Writer.TryComplete()`. Worker khởi động lại (`BotUndoFlowTests` stop/start) không đọc được nữa → mọi lượt bot sau đó mất tín hiệu, version trận đứng ở 1 thay vì 2. | `Stop()` chỉ dọn hàng đợi đang chờ, **không** đóng channel. Worker dừng hẳn vẫn hợp lệ vì `ReadAsync` nhận `CancellationToken`. |
| Fixture `Legacy_hero_command_retry...` vi phạm ràng buộc production | Test đổi `kind` của action `sequence_no = 0` từ `start` sang `hero_active`, vi phạm `match_action_check` (`sequence_no = 0 ⇒ kind = 'start'`). | Fixture không đổi `kind` nữa — chỉ đổi `command_id`/`actor_side`/`request_payload`. Command id trùng vẫn được nhận diện là retry, nên test vẫn kiểm đúng hành vi idempotent. **Không** nới ràng buộc production. |

Cả hai lỗi chỉ lộ ra khi chạy **toàn bộ** bộ test (test chạy tuần tự, dùng chung DB). Chạy lẻ từng test đều xanh — đây là lý do phải chạy full suite thay vì chỉ các test Admin.

### Kết quả chạy thực tế

| Bộ test | Lệnh | Kết quả |
|---|---|---|
| `HeroChess.Rules.Tests` | `dotnet test tests/HeroChess.Rules.Tests` | **364/364 đạt** (268 ms) |
| `HeroChess.IntegrationTests` | `dotnet test tests/HeroChess.IntegrationTests` | **111/111 đạt** (45 s), chạy 2 lần liên tiếp trên DB sạch |
| Build | `dotnet build HeroChess.Api/HeroChess.Api.csproj` | 0 lỗi, 0 cảnh báo |

DB test: PostgreSQL 17.4 portable, cổng `55439`, database `hero_chess_test`, `HERO_CHESS_TEST_DB` trỏ vào đó. Không dùng Supabase production.

---

## 9. AdminWeb (React + TypeScript) — giai đoạn frontend

> **Lưu ý (cập nhật):** AdminWeb đã được tách thành repository độc lập:
> <https://github.com/HeroChessVN/HeroChessAdminWeb>.
> Thư mục `admin-web/` **không còn nằm trong repository Backend này** — mục này giữ lại
> lịch sử triển khai và các quy ước kỹ thuật đã áp dụng.
> Tài liệu vận hành hiện hành của AdminWeb nằm ở repository AdminWeb (`docs/`).
> Backend vẫn sở hữu Admin API và contract của nó.

Thư mục `admin-web/` (nay đã tách riêng) — package riêng, không đụng `frontend/` (frontend đang dùng để test backend).

| Hạng mục | Lựa chọn | Vì sao |
|---|---|---|
| Build | Vite 7 + React 19 + TypeScript 5.9 | Nhanh, độc lập với backend, build tĩnh. |
| CSS | Tailwind v4 (`@tailwindcss/vite`, CSS-first `@theme`) | Token màu tập trung trong `src/index.css`, không cấu hình lẫn nhầm. |
| Định tuyến | `react-router-dom` v7 với `HashRouter` | Host tĩnh của ASP.NET không bảo đảm URL rewrite → deep-link kiểu `/admin/heroes` sẽ 404. Hash (`/#/heroes`) không cần sửa backend. |
| Icon | `lucide-react` | Bộ icon nhất quán, không dùng ảnh stock. |
| Biểu đồ | `recharts` | Chỉ dùng ở màn Analytics (bước 5). |

### 9.1 Cấu trúc

> Ảnh chụp cấu trúc tại thời điểm giai đoạn frontend. Cấu trúc hiện hành (12 page,
> 11 component UI, 3 feature drawer, `ui-audit/`, `docs/`) nằm ở repository AdminWeb.

```
admin-web/
├── src/
│   ├── auth/            AuthContext, ProtectedRoute
│   ├── components/
│   │   ├── layout/      AppLayout, Sidebar, Header
│   │   ├── ui/          Button, Card, Badge, Skeleton, Field, ConfirmDialog, Drawer, Toast, JsonViewer, EmptyState, ErrorState
│   │   └── heroes/      HeroForm, HeroDetailDrawer
│   ├── lib/             api, session, permissions, types, format, classLabel, useResource
│   └── pages/           LoginPage, DashboardPage, HeroesPage (+ 6 module còn lại ở bước 5)
```

### 9.2 Quy ước kỹ thuật đã áp dụng

| Quy ước | Cách làm |
|---|---|
| `bigint` (coin/balance/amount) | Backend trả **chuỗi**; `formatCoin` nhóm nghìn bằng regex, **không** ép `Number()` — giữ đúng cả giá trị trên 2^53. |
| Phân trang | `nextCursor` (keyset) qua `usePagedResource`; không dùng offset giả. |
| Token | `sessionStorage` (mất khi đóng tab). 401 → refresh một lần rồi thử lại; refresh hỏng → về trang login. 403 → báo "không đủ quyền" khác với lỗi mạng. |
| Quyền | `GET /admin/accounts` cần policy `admin-read:admin` → admin bị giới hạn **không lấy được danh sách quyền**; client coi `permissions = null` là "chưa xác định" và hiện đầy đủ, **server vẫn là nơi quyết định**. |
| Thao tác ghi | `reason` bắt buộc (validate ở client, backend `AdminValidation.RequireReason` cho các API nhạy cảm), có `ConfirmDialog`. |
| Không giả lập dữ liệu | Mọi biểu đồ/số liệu lấy từ API; `hasReplay == false` thì không hiển thị nút replay; analytics thiếu dữ liệu hiện empty state kèm sample size. |
| Không gọi Supabase từ browser | Toàn bộ đi qua `/api/v1` của ASP.NET. Base URL là đường dẫn tương đối; dev server proxy `/api/v1` → `HERO_CHESS_API_URL`. |
| Secret | Không có service-role key hay secret nào trong frontend. |

### 9.3 Cấu hình chạy

> Thư mục `admin-web/` nay độc lập — clone từ
> <https://github.com/HeroChessVN/HeroChessAdminWeb> vào một thư mục riêng, ví dụ
> `C:\Users\ptien\Herochess\Code\HeroChessAdminWeb`.

```bash
cd C:\Users\ptien\Herochess\Code\HeroChessAdminWeb
npm install
npm run dev          # http://127.0.0.1:5175  (proxy /api/v1 -> HERO_CHESS_API_URL, mặc định http://127.0.0.1:5012)
npm run build        # tsc -b && vite build -> dist/ (base '/admin/' khi build)
npm test             # node --test "src/lib/*.test.ts"
```

Biến môi trường: `HERO_CHESS_API_URL` (chỉ dùng cho proxy ở dev).

### 9.4 Kết quả kiểm thử thực tế (giai đoạn frontend, 2026-11-08)

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Build backend | `dotnet build HeroChess.Api/HeroChess.Api.csproj` | **0 lỗi, 0 cảnh báo** |
| Test module Admin | `dotnet test ... --filter FullyQualifiedName~AdminModuleTests` | **8/8 đạt** |
| Build AdminWeb | `npm run build` | **thành công** — `dist/assets/index-*.js` 329,97 kB (gzip 102,20 kB), CSS 24,58 kB |
| Test AdminWeb | `npm test` | **7/7 đạt** (`src/lib/format.test.ts`: formatCoin bigint, formatPercent thang 0–100, initials, ma trận quyền) |
| Kiểm thử trình duyệt | `http://127.0.0.1:5175/#/heroes` | Danh sách 64 hero, tìm kiếm, lọc, phân trang cursor, Drawer chi tiết (giá, skin, skill chỉ đọc, lịch sử đổi giá), tạo hero, dialog bật/tắt — tất cả với dữ liệu thật từ API |

### 9.5 Lỗi phát hiện khi kiểm thử AdminWeb (đã sửa)

| Lỗi | Nguyên nhân gốc | Sửa |
|---|---|---|
| Tạo hero lớp khác `GENERAL` với `setupPoints = 0` → **500 INTERNAL_ERROR** | DB có CHECK `hero_check (class_code = 'GENERAL' OR setup_points >= 1)`; cả client lẫn service đều cho phép 0 nên lỗi Postgres trào ra ngoài thành 500. | `AdminValidation.RequireSetupPoints` dùng chung cho create + update → `400 INVALID_SETUP_POINTS`; client validate trước và tự nâng lên 1 khi đổi lớp; thêm test chặn regression. |
| Bật/tắt hero ghi audit log với `reason` rỗng | `toggleEnabled` không kiểm tra reason trước khi gọi API. | Chặn ngay trên client kèm thông báo; `ConfirmDialog` giữ nguyên luồng xác nhận. |
| `npm test` báo `MODULE_NOT_FOUND` | Script là `node --test src/lib/` (Node 24 cần mẫu file) và chưa có file test. | Sửa script thành `node --test "src/lib/*.test.ts"`, thêm `src/lib/format.test.ts`, loại file test khỏi `tsconfig.app.json` (Node tự strip type). |
| Bộ lọc "Phe" không có lựa chọn nào | Bảng `faction` trong DB dev rỗng → `GET /admin/factions` trả `[]`. | Không phải lỗi client. Ghi nhận là hạn chế dữ liệu môi trường dev. |

### 9.6 Chưa làm (bước 5)

Shop, Player, Gift Code, Match Inspector, Analytics, Admin & Audit — chỉ triển khai sau khi Dashboard và Hero Management đã xác nhận hoạt động đúng (đã đạt, xem 9.4).
