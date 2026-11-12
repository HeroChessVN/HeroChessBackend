# Hero Chess — Account Admin (Chạy Admin Console / AdminWeb)

Tài liệu này ghi lại cách chạy trang quản trị AdminWeb (React) + Backend API, tài khoản đăng nhập, đường dẫn các trang admin và toàn bộ API mà trang admin kết nối.

> Chi tiết contract DTO của từng endpoint: [`ADMIN_API_GUIDE.md`](./ADMIN_API_GUIDE.md).

---

## 1. Chạy code

### 1.1 Backend API (port 5012)

```powershell
cd C:\Users\ptien\Herochess\Code\HeroChessBackend\HeroChess.Api
dotnet run --launch-profile http
```

- Profile `http` → `http://0.0.0.0:5012`, `ASPNETCORE_ENVIRONMENT=Development`.
- Kiểm tra sống: <http://127.0.0.1:5012/health/ready> → `{"status":"ready","database":"connected"}`.
- Swagger: <http://127.0.0.1:5012/swagger>.
- Backend **không bật CORS** → AdminWeb phải gọi qua Vite proxy `/api/v1` (không gọi cross-origin).

> Lưu ý: khi chạy bằng `dotnet run` với `DatabaseBootstrap:Enabled = false` (mặc định trên production DB), backend không tự chạy migration. Migration phải áp dụng thủ công bằng `psql`.

### 1.2 AdminWeb frontend (port 5175)

```powershell
cd C:\Users\ptien\Herochess\Code\HeroChessBackend\admin-web
npm ci          # chỉ lần đầu
npm run dev     # vite --host 127.0.0.1 --port 5175
```

- Mở <http://127.0.0.1:5175/#/login>.
- Vite proxy `/api/v1` → backend. Đổi backend thì set `HERO_CHESS_API_URL=http://localhost:5012` trước khi `npm run dev`.
- `VITE_ADMIN_API_URL` (tuỳ chọn) để ghi đè base URL của client.
- Build để serve tĩnh: `npm run build` → `dist/` (base `/admin/`).

---

## 2. Tài khoản đăng nhập

| Trường | Giá trị |
|---|---|
| Email | `admin@gmail.com` |
| Mật khẩu | `Test12345` |
| Role | `admin` |
| Permissions | `["*"]` (toàn quyền) |
| User ID | `e9abb7d5-4250-4331-911a-d2101a2cd001` |

Đăng nhập lấy token (bắt buộc cho mọi API call):

```powershell
$base = 'http://127.0.0.1:5012/api/v1'
$login = Invoke-RestMethod "$base/auth/login?useCookies=false" -Method Post `
  -ContentType 'application/json' `
  -Body (@{ email = 'admin@gmail.com'; password = 'Test12345' } | ConvertTo-Json -Compress)
$h = @{ Authorization = "Bearer $($login.accessToken)" }
```

Mật khẩu phải thoả ASP.NET Core Identity: ≥ 8 ký tự, có chữ hoa, chữ thường, số (`RequireUppercase`/`RequireLowercase`/`RequireDigit` = true, `RequireNonAlphanumeric` = false).

### 2.1 Các tài khoản admin khác trên DB (không xóa)

| Email | Created | Ghi chú |
|---|---|---|
| `admin-test@hero.local` | 29/10/2026 | Tài khoản admin có sẵn từ trước |
| `auditadmin1@example.test` | 12/11/2026 | Tài khoản audit dùng cho UI audit |

> Không xóa hai tài khoản trên. Chỉ tạo thêm tài khoản khi được phê duyệt, và tạo qua `POST /api/v1/admin/accounts` (không chèn hash trực tiếp vào DB).

---

## 3. Đường dẫn trang admin (HashRouter)

Base: `http://127.0.0.1:5175/#<route>`

| Route | Trang | Quyền cần |
|---|---|---|
| `#/login` | Đăng nhập | — |
| `#/` | Dashboard — Tổng quan vận hành | `read` |
| `#/heroes` | Quản lý Hero | `hero` |
| `#/shop` | Quản lý Cửa hàng | `shop` |
| `#/players` | Quản lý Người chơi | `player` |
| `#/gift-codes` | Quản lý Gift Code | `giftcode` |
| `#/matches` | Trình xem Trận đấu | `analytics` |
| `#/analytics` | Phân tích cân bằng & bot | `analytics` |
| `#/admin` | Tài khoản Admin (phân quyền) | `admin` |
| `#/audit` | Nhật ký thao tác | `audit` |
| `#/403` | Không đủ quyền | — |
| bất kỳ route khác | 404 "Không tìm thấy trang" | — |

Router dùng `HashRouter` vì backend serve static file không có SPA fallback (deep-link kiểu `/admin/heroes` sẽ 404).

---

## 4. Các API kết nối trang admin

Base URL: `http://127.0.0.1:5012/api/v1` (AdminWeb gọi qua proxy Vite `/api/v1`).
Header bắt buộc: `Authorization: Bearer <accessToken>`.

### 4.1 Xác thực & profile

| Method | Endpoint | Method | Mục đích |
|---|---|---|---|
| POST | `/auth/login?useCookies=false` | POST | Đăng nhập → `accessToken` + `refreshToken` |
| POST | `/auth/refresh` | POST | Làm mới token (`{ refreshToken }`) |
| GET | `/me` | GET | Thông tin tài khoản hiện tại (role, status) |
| GET | `/admin/accounts` | GET | Danh sách admin + quyền (dùng để dựng `PermissionSet` ở client) |
| GET | `/admin/permissions` | GET | Catalogue quyền fine-grained |

### 4.2 Dashboard & phân tích

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| GET | `/admin/stats` | `admin-read:read` | Số liệu tổng quan (player/hero/trận/coin/admin) |
| GET | `/admin/analytics/balance?mode=ranked` | `admin-read:analytics` | Tỉ lệ chọn & tỉ lệ thắng theo hero/phe/skill |
| GET | `/admin/analytics/bot` | `admin-read:analytics` | Tỉ lệ thắng bot theo độ khó & bộ luật |

### 4.3 Hero (catalog)

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| GET | `/admin/heroes?limit=50` | `admin-read:hero` | Danh sách hero (lọc `query`, `classCode`, `factionCode`, `enabled`, `inShop`) |
| GET | `/admin/chess-classes` | `admin-read:hero` | Danh sách lớp quân lấy nguyên từ bảng `hero_chess.chess_class` |
| GET | `/admin/heroes/{id}` | `admin-read:hero` | Chi tiết hero + cosmetics + skills + lịch sử giá |
| POST | `/admin/heroes` | `admin-write:hero` | Tạo hero |
| PATCH | `/admin/heroes/{id}` | `admin-write:hero` | Sửa hero (bật/tắt, đổi giá…) |
| PUT | `/admin/heroes/{id}/faction` | `admin-write:hero` | Gán phe cho hero |
| GET | `/admin/factions` | `admin-read:hero` | Danh sách phe |
| GET | `/admin/skills` | `admin-read:hero` | Danh sách skill |
| PATCH | `/admin/skills/{id}` | `admin-write:hero` | Sửa skill |
| GET | `/admin/rulesets` | `admin-read:ruleset` | Danh sách bộ luật |

### 4.4 Cửa hàng (shop)

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| GET | `/admin/shop/heroes?limit=50` | `admin-read:shop` | Hero trong cửa hàng (lọc `query`, `inShop`) |
| PATCH | `/admin/shop/heroes/{id}` | `admin-write:shop` | Bật/tắt bán, đổi giá đơn hero |
| PATCH | `/admin/shop/prices` | `admin-write:shop` | Đổi giá hàng loạt (`{ items, reason }`) |
| GET | `/admin/shop/purchases?limit=25` | `admin-read:shop` | Lịch sử mua hero |

### 4.5 Người chơi

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| GET | `/admin/players?limit=50` | `admin-read:player` | Danh sách người chơi (lọc `query`, `status`) |
| GET | `/admin/players/{id}` | `admin-read:player` | Chi tiết người chơi |
| GET | `/admin/players/{id}/transactions` | `admin-read:player` | Lịch sử biến động ví |
| PATCH | `/admin/players/{id}/status` | `admin-write:player` | Khoá / mở khoá tài khoản |
| POST | `/admin/players/{id}/coins` | `admin-write:player` | Cộng/trừ coin |
| POST | `/admin/players/{id}/heroes` | `admin-write:player` | Cấp hero cho người chơi |
| DELETE | `/admin/players/{id}/heroes/{heroId}` | `admin-write:player` | Thu hồi hero |

### 4.6 Gift code

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| GET | `/admin/gift-codes?limit=50` | `admin-read:giftcode` | Danh sách gift code (lọc `query`, `active`) |
| GET | `/admin/gift-codes/{id}` | `admin-read:giftcode` | Chi tiết gift code |
| GET | `/admin/gift-codes/{id}/redemptions` | `admin-read:giftcode` | Lịch sử quy đổi |
| POST | `/admin/gift-codes` | `admin-write:giftcode` | Tạo gift code (để trống `code` → backend tự sinh 12 ký tự) |
| PATCH | `/admin/gift-codes/{id}` | `admin-write:giftcode` | Sửa/bật-tắt gift code |

### 4.7 Trận đấu

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| GET | `/admin/matches?limit=50` | `admin-read:analytics` | Danh sách trận (lọc `mode`, `status`, `from`, `to`, `playerId`) |
| GET | `/admin/matches/{id}` | `admin-read:analytics` | Chi tiết trận |
| GET | `/admin/matches/{id}/actions?limit=200` | `admin-read:analytics` | Chuỗi action đã ghi của trận |

### 4.8 Nhật ký & quản trị tài khoản

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| GET | `/admin/audit?limit=25` | `admin-read:audit` | Nhật ký thao tác admin (lọc `action`, `entityType`, `entityId`, `actorUserId`, `from`, `to`) |
| POST | `/admin/accounts` | `admin-write:admin` | Tạo tài khoản admin (`{ email, displayName, password, permissions }`) |
| PUT | `/admin/accounts/{id}/permissions` | `admin-write:admin` | Đặt lại quyền của admin |

### 4.9 Giá (legacy, vẫn dùng được)

| Method | Endpoint | Policy | Mục đích |
|---|---|---|---|
| PATCH | `/admin/heroes/{id}/price` | `admin-write:shop` | Đổi giá hero |
| PATCH | `/admin/cosmetics/{id}/price` | `admin-write:shop` | Đổi giá cosmetic |
| PATCH | `/admin/board-cosmetics/{id}/price` | `admin-write:shop` | Đổi giá board cosmetic |

---

## 5. Quyền fine-grained

Cấu hình nằm trong `hero_chess.admin_permission` (`admin_user_id`, `permissions text[]`).

- Không có dòng quyền → toàn quyền (tương thích ngược).
- Có dòng → theo danh sách; `["*"]` = toàn quyền.
- Quyền đọc: `admin-read:{miền}`. Quyền ghi: `admin-write:{miền}` (ghi bao hàm đọc cùng miền).
- Miền: `read`, `hero`, `shop`, `player`, `giftcode`, `analytics`, `ruleset`, `audit`, `admin`.

Mọi thao tác **ghi** đều được ghi vào `hero_chess.admin_audit_log` (actor, before/after, reason).

### 5.1 Lớp quân — nguồn duy nhất là DB

Bảng `hero_chess.chess_class` (7 dòng) là nguồn duy nhất. AdminWeb **không** hardcode danh sách lớp:

- Backend: `GET /api/v1/admin/chess-classes` đọc thẳng bảng `chess_class`, trả `ChessClassDto` (`code`, `nameVi`, `baseSp`, `requiredCount`, `baseMovementCode`).
- Form tạo hero: trường "Lớp quân" là **text read-only**, hiển thị nhãn lấy từ DB. Hero mới luôn thuộc lớp mặc định `ADVISOR` (quân Sĩ) — không có dropdown chọn.
- Form sửa hero: lớp hiển thị **read-only** (`disabled`) — không đổi được sau khi tạo; API `PATCH /admin/heroes/{id}` cũng không nhận `classCode`.
- `admin-web/src/lib/chessClasses.ts` có fallback `FALLBACK_CLASSES` (ảnh chụp DB) để console vẫn chạy được khi backend chưa có endpoint mới.

---

## 6. Kiểm tra nhanh bằng PowerShell

```powershell
$base = 'http://127.0.0.1:5012/api/v1'
$login = Invoke-RestMethod "$base/auth/login?useCookies=false" -Method Post `
  -ContentType 'application/json' `
  -Body (@{ email = 'admin@gmail.com'; password = 'Test12345' } | ConvertTo-Json -Compress)
$h = @{ Authorization = "Bearer $($login.accessToken)" }

(Invoke-WebRequest "$base/me"          -Headers $h -UseBasicParsing).Content
(Invoke-WebRequest "$base/admin/accounts" -Headers $h -UseBasicParsing).Content
(Invoke-WebRequest "$base/admin/stats"   -Headers $h -UseBasicParsing).Content
```

Bỏ `-UseBasicParsing` sẽ lỗi `Windows PowerShell is in NonInteractive mode` khi server trả 401.
