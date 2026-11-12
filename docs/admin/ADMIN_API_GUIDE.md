# Hero Chess — Hướng dẫn tích hợp API Admin (cho React AdminWeb)

Tài liệu này mô tả contract của toàn bộ endpoint Admin đã hiện thực ở backend, nhằm hỗ trợ frontend AdminWeb (React + TypeScript) tích hợp.

- Base URL: `https://<host>/api/v1`
- Đường dẫn Admin: `/api/v1/admin/**`
- Đường dẫn người chơi: `/api/v1/gift-codes/redeem`
- Kiểu JSON: camelCase. Số tiền (`coinPrice`, `amount`, `balance`) là **chuỗi** để không mất độ chính xác của `bigint`.

---

## 1. Xác thực

### 1.1 Đăng nhập

```http
POST /api/v1/auth/login?useCookies=false
Content-Type: application/json

{ "email": "admin@example.com", "password": "••••" }
```

Response:

```json
{
  "tokenType": "Bearer",
  "accessToken": "eyJhbGciOi...",
  "expiresIn": 3600,
  "refreshToken": "..."
}
```

Dùng `Authorization: Bearer <accessToken>` cho mọi request sau. `POST /api/v1/auth/refresh` với body `{ "refreshToken": "..." }` để làm mới token.

### 1.2 Kiểm tra quyền ở client (chỉ để ẩn/hiện nút — **không phải** bảo mật)

```http
GET /api/v1/admin/permissions
```

```json
[
  { "key": "read",      "description": "Đọc dashboard, danh sách và chi tiết trận đấu." },
  { "key": "hero",      "description": "Tạo, sửa, bật/tắt hero, trait, phe và skill." },
  { "key": "shop",      "description": "Quản lý giá và tính khả dụng trong cửa hàng." },
  { "key": "player",    "description": "Xem, ban/mở khoá, điều chỉnh ví và bộ sưu tập của người chơi." },
  { "key": "giftcode",  "description": "Quản lý gift code và lịch sử quy đổi." },
  { "key": "analytics", "description": "Đọc số liệu cân bằng và bot." },
  { "key": "ruleset",   "description": "Xem bộ luật và kịch bản." },
  { "key": "audit",     "description": "Đọc nhật ký thao tác admin." },
  { "key": "admin",     "description": "Quản lý tài khoản admin và quyền của họ." }
]
```

Ngoài ra `GET /api/v1/admin/accounts` trả về danh sách admin kèm `permissions` của từng tài khoản (dùng để biết quyền của chính mình).

**Quy tắc server-side (không nới được):**

| Key | Quyền |
|---|---|
| `*` | Toàn quyền. |
| Không có dòng trong `admin_permission` | Toàn quyền (tương thích ngược với admin tạo trước migration). |
| `read` | Đọc được mọi miền. |
| `hero` / `shop` / `player` / `giftcode` / `analytics` / `ruleset` / `audit` / `admin` | Miền tương ứng. Quyền ghi của một miền **cũng cho đọc** miền đó. |

Mọi request đều đối chiếu `role` và `status` với DB ở thời điểm gọi — không tin claim cũ.

### 1.3 Mã lỗi

| HTTP | Ý nghĩa |
|---|---|
| 401 | Chưa đăng nhập / token hết hạn → chuyển hướng về trang đăng nhập. |
| 403 | Đã đăng nhập nhưng thiếu quyền miền, hoặc tài khoản không phải admin, hoặc đã bị ban. |
| 400 | Payload sai → hiển thị `message`. |
| 404 | Không tìm thấy entity. |
| 409 | Xung đột nghiệp vụ (trùng mã, hết hạn, đã quy đổi). |
| 422 | Dữ liệu tham chiếu không tồn tại (hero/cosmetic). |

Response lỗi: `{ "code": "HERO_NOT_FOUND", "message": "The hero was not found." }`

---

## 2. Phân trang

Mọi danh sách dùng **keyset cursor**:

```json
{ "items": [ ... ], "nextCursor": "eyJ...", "total": 123 }
```

- `nextCursor` = `null` → hết trang. `total` là tổng số bản ghi (để hiển thị "x trong y").
- Request trang sau: `?cursor=<nextCursor>&limit=50`.
- `limit` mặc định 50 (audit: 20), tối đa 200.
- Cursor **không** phải offset → chèn/xoá bản ghi giữa chừng không làm lệch trang.

---

## 3. Danh mục endpoint

### 3.1 Hero

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/admin/heroes` | `hero` (đọc) | Danh sách hero, gồm cả hero đang tắt. |
| GET | `/api/v1/admin/heroes/{id}` | `hero` (đọc) | Chi tiết hero. |
| POST | `/api/v1/admin/heroes` | `hero` (ghi) | Tạo hero. |
| PATCH | `/api/v1/admin/heroes/{id}` | `hero` (ghi) | Sửa hero. |
| PUT | `/api/v1/admin/heroes/{id}/faction` | `hero` (ghi) | Gán phe. |

Tham số truy vấn của `GET /api/v1/admin/heroes`: `query`, `classCode`, `factionCode`, `enabled`, `inShop`, `cursor`, `limit`.

Response của `GET /api/v1/admin/heroes`:

```json
{
  "items": [{
    "id": "…uuid…",
    "code": "GUAN_YU",
    "name": "Quan Vũ",
    "classCode": "general",
    "characterId": null,
    "setupPoints": 4,
    "coinPrice": "1200",
    "isEnabled": true,
    "isInShop": true,
    "isStarter": false,
    "isTestFixture": false,
    "assetKey": "hero_guan_yu",
    "description": null,
    "factionCodes": ["wei"],
    "traitId": null,
    "traitCode": null
  }],
  "nextCursor": null,
  "total": 42
}
```

> `setupPoints` là **legacy read-only** (dữ liệu cũ chỉ đọc). API admin trả về giá trị để hiển thị nhưng không có luật gameplay nào enforce nó. Không cho sửa trong UI nếu team chưa xác nhận.

Response của `GET /api/v1/admin/heroes/{id}`:

```json
{
  "hero": { "…": "AdminHeroDto" },
  "cosmetics": [{ "id": "…", "code": "…", "name": "…", "heroId": "…", "coinPrice": "300", "isEnabled": true, "assetKey": "…" }],
  "skills": [{ "id": "…", "code": "…", "name": "…", "factionId": null, "factionCode": null,
               "implementationKey": "…", "isEnabled": true, "maxUses": 3, "cooldownTurns": 2,
               "description": null, "status": "LIVE" }],
  "ownerCount": 128,
  "totalRevenue": 153600,
  "priceHistory": [{ "id": "…", "actorUserId": "…", "coinPriceBefore": "1000", "coinPriceAfter": "1200",
                     "reason": "balance pass 2026-11", "changedAt": "2026-11-08T…Z" }]
}
```

**Tạo hero** — `POST /api/v1/admin/heroes`:

```json
{
  "code": "GUAN_YU",
  "name": "Quan Vũ",
  "classCode": "general",
  "setupPoints": 4,
  "coinPrice": "1200",
  "isEnabled": true,
  "isInShop": true,
  "isStarter": false,
  "assetKey": "hero_guan_yu",
  "description": "Tướng Võ.",
  "traitId": null,
  "reason": "thêm hero mở mùa 3"
}
```

`code` và `name` bắt buộc và phải unique; `classCode` phải tồn tại; `coinPrice` phải là số nguyên ≥ 0. `reason` nên có — nó được ghi vào audit log.

> **Luật `setupPoints`**: chỉ hero lớp `GENERAL` được có `setupPoints = 0`; các lớp khác cần tối thiểu `1` (khớp CHECK `hero_check` của DB). Vi phạm → `400 INVALID_SETUP_POINTS` (server validate trước khi ghi DB).

**Sửa hero** — `PATCH /api/v1/admin/heroes/{id}` (chỉ gửi field muốn đổi, `null` = giữ nguyên):

```json
{ "name": "Quan Vũ", "setupPoints": 4, "isEnabled": false, "assetKey": null, "description": null, "traitId": null, "reason": "tắt tạm" }
```

> Tắt hero (`isEnabled=false`) là **soft disable** (vô hiệu hoá mềm), không xoá. Hero đã được sở hữu vẫn dùng được; lịch sử trận không bị ảnh hưởng.

**Gán phe** — `PUT /api/v1/admin/heroes/{id}/faction`:

```json
{ "factionId": "…uuid…", "reason": "chuyển phe" }
```

`factionId: null` = bỏ phe. Thiết kế là **tối đa 1 phe mỗi hero**; service chặn vi phạm (DB chưa có unique constraint — xem audit B2).

### 3.2 Skill & Phe

| Method | Path | Quyền |
|---|---|---|
| GET | `/api/v1/admin/skills` | `hero` (đọc) |
| PATCH | `/api/v1/admin/skills/{id}` | `hero` (ghi) |
| GET | `/api/v1/admin/factions` | `hero` (đọc) |
| POST | `/api/v1/admin/factions` | `hero` (ghi) |
| PATCH | `/api/v1/admin/factions/{id}` | `hero` (ghi) |

`PATCH /api/v1/admin/skills/{id}`:

```json
{ "name": "Thiên Quang", "description": null, "isEnabled": true, "maxUses": 3, "cooldownTurns": 2, "reason": "…" }
```

> **Không** cho sửa hiệu ứng skill (damage, điều kiện kích hoạt, target restriction) — các luật đó chưa được đặc tả. Chỉ sửa metadata. `status` là `DRAFT` hoặc `LIVE`; UI nên hiển thị `DRAFT` rõ ràng và không cho bật skill DRAFT vào ruleset đang active (chưa có API sửa ruleset → xem mục 6).

### 3.3 Ruleset (chỉ đọc)

| Method | Path | Quyền |
|---|---|---|
| GET | `/api/v1/admin/rulesets` | `ruleset` (đọc) |

```json
[{
  "id": "…uuid…",
  "code": "ranked_v1",
  "name": "Ranked V1",
  "setupBudget": 32,
  "turnSeconds": 60,
  "actionLimit": 120,
  "isActive": true,
  "config": { "…": "ruleset config jsonb" },
  "matchCount": 812,
  "lastUsedAt": "2026-11-08T…Z"
}]
```

Không có endpoint sửa ruleset/scenario. Ruleset được **đóng băng** vào `game_match.ruleset_snapshot` nên sửa ruleset không phá lịch sử trận.

### 3.4 Cửa hàng

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/admin/shop/heroes` | `shop` (đọc) | Hero trong cửa hàng kèm số lượt đã mua. |
| PATCH | `/api/v1/admin/shop/heroes/{id}` | `shop` (ghi) | Đổi giá và/hoặc bật/tắt bán. |
| PATCH | `/api/v1/admin/shop/prices` | `shop` (ghi) | Đổi giá hàng loạt. |
| GET | `/api/v1/admin/shop/purchases` | `shop` (đọc) | Lịch sử mua. |
| PATCH | `/api/v1/admin/heroes/{id}/price` | `shop` (ghi) | Đổi giá hero (endpoint cũ, giữ tương thích). |
| PATCH | `/api/v1/admin/cosmetics/{id}/price` | `shop` (ghi) | Đổi giá cosmetic. |

`GET /api/v1/admin/shop/heroes` — query `query`, `inShop`, `cursor`, `limit`:

```json
{
  "items": [{
    "hero": { "…": "AdminHeroDto" },
    "purchasable": true,
    "purchaseCount": 37,
    "lastPurchasedAt": "2026-11-08T…Z"
  }],
  "nextCursor": null,
  "total": 18
}
```

`PATCH /api/v1/admin/shop/heroes/{id}`:

```json
{ "coinPrice": "1500", "isInShop": false, "reason": "giảm giá tháng 11" }
```

`PATCH /api/v1/admin/shop/prices` (hàng loạt — lỗi từng dòng không làm hỏng các dòng khác):

```json
{
  "items": [
    { "heroId": "…uuid…", "coinPrice": "1500" },
    { "heroId": "…uuid…", "coinPrice": "1800" }
  ],
  "reason": "đợt giảm giá tháng 11"
}
```

```json
{
  "results": [
    { "heroId": "…", "succeeded": true,  "coinPrice": "1500", "errorCode": null,    "errorMessage": null },
    { "heroId": "…", "succeeded": false, "coinPrice": null,    "errorCode": "HERO_NOT_FOUND", "errorMessage": "The hero was not found." }
  ]
}
```

> Đổi giá ảnh hưởng `CatalogVersionService` fingerprint → trận đang chọn lineup có thể nhận `CONTENT_VERSION_CHANGED`. UI nên cảnh báo admin.

### 3.5 Người chơi

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/admin/players` | `player` (đọc) | Danh sách, tìm kiếm. |
| GET | `/api/v1/admin/players/{id}` | `player` (đọc) | Hồ sơ đầy đủ. |
| PATCH | `/api/v1/admin/players/{id}/status` | `player` (ghi) | Ban / mở khoá. |
| POST | `/api/v1/admin/players/{id}/coins` | `player` (ghi) | Cộng/trừ coin. |
| POST | `/api/v1/admin/players/{id}/heroes` | `player` (ghi) | Cấp hero. |
| DELETE | `/api/v1/admin/players/{id}/heroes/{heroId}` | `player` (ghi) | Thu hero do admin cấp. |
| GET | `/api/v1/admin/players/{id}/transactions` | `player` (đọc) | Lịch sử biến động ví. |

`GET /api/v1/admin/players` — query `query` (email hoặc display name), `status` (`active`/`disabled`), `cursor`, `limit`:

```json
{
  "items": [{
    "id": "…uuid…",
    "displayName": "player1",
    "status": "active",
    "email": "player1@example.com",
    "elo": 1024,
    "gamesPlayed": 40,
    "wins": 21, "draws": 3, "losses": 16,
    "coinBalance": "3400",
    "ownedHeroes": 12,
    "createdAt": "2026-09-01T…Z"
  }],
  "nextCursor": null,
  "total": 128
}
```

`GET /api/v1/admin/players/{id}`:

```json
{
  "player": { "…": "AdminPlayerDto" },
  "heroes":   [{ "heroId": "…", "heroCode": "…", "heroName": "…", "acquiredVia": "purchase", "acquiredAt": "…" }],
  "lineups":  [{ "id": "…", "name": "Bộ 1", "revision": 3, "entryCount": 16, "updatedAt": "…" }],
  "moderation": [{ "id": "…", "actorUserId": "…", "action": "player.status.update", "reason": "cheating", "createdAt": "…" }],
  "matches":  [{ "matchId": "…", "mode": "ranked", "status": "completed", "result": "…", "createdAt": "…" }]
}
```

**Ban / mở khoá** — `PATCH /api/v1/admin/players/{id}/status`:

```json
{ "status": "disabled", "reason": "phát hiện gian lận rank 12/11" }
```

- `status` ∈ {`active`, `disabled`}. `reason` **bắt buộc**, ghi vào audit log.
- Tài khoản bị ban không đăng nhập được (kiểm tra `status='active'` ở mỗi request) → client hiện tại cần đăng xuất (logout).
- Admin **không tự ban chính mình** → `400 SELF_BAN_FORBIDDEN`.

**Điều chỉnh coin** — `POST /api/v1/admin/players/{id}/coins`:

```json
{ "amount": "500", "reason": "bù lỗi server sập lúc 16/11" }
```

`amount` là chuỗi số; âm = trừ. Số dư sau không được âm (`400 INSUFFICIENT_BALANCE`). Response:

```json
{ "playerId": "…", "balanceBefore": "2900", "balanceAfter": "3400", "amount": "500" }
```

**Cấp hero** — `POST /api/v1/admin/players/{id}/heroes`:

```json
{ "heroId": "…uuid…", "reason": "bù lỗi thiếu hero do bug 16/11" }
```

**Thu hero** — `DELETE /api/v1/admin/players/{id}/heroes/{heroId}?reason=…`

Chỉ thu được hero có `acquiredVia == "admin"`. Hero mua (`purchase`), starter (`starter`) hoặc test (`test`) **không** bị thu → `409 HERO_NOT_ADMIN_GRANTED`.

**Lịch sử ví** — `GET /api/v1/admin/players/{id}/transactions`:

```json
[{ "id": "…", "kind": "admin_adjustment", "amount": 500, "balanceAfter": "3400",
   "heroId": null, "matchId": null, "createdAt": "…" }]
```

### 3.6 Gift Code

| Method | Path | Quyền | Mô tả |
|---|---|---|---|
| GET | `/api/v1/admin/gift-codes` | `giftcode` (đọc) | Danh sách + số lượt đã quy đổi. |
| GET | `/api/v1/admin/gift-codes/{id}` | `giftcode` (đọc) | Chi tiết. |
| POST | `/api/v1/admin/gift-codes` | `giftcode` (ghi) | Tạo. |
| PATCH | `/api/v1/admin/gift-codes/{id}` | `giftcode` (ghi) | Sửa cấu hình. |
| GET | `/api/v1/admin/gift-codes/{id}/redemptions` | `giftcode` (đọc) | Lịch sử quy đổi. |
| POST | `/api/v1/gift-codes/redeem` | người chơi | **Người chơi** quy đổi. |

`GET /api/v1/admin/gift-codes` — query `query`, `active`, `cursor`, `limit`:

```json
{
  "items": [{
    "id": "…uuid…",
    "code": "ABCDEF234567",
    "rewardKind": "coin",
    "rewardQuantity": 500,
    "rewardHeroId": null,
    "rewardCosmeticId": null,
    "startsAt": "2026-11-01T…Z",
    "expiresAt": "2026-12-01T…Z",
    "maxRedemptions": 1000,
    "maxPerPlayer": 1,
    "isActive": true,
    "redemptionCount": 42,
    "createdAt": "…", "updatedAt": "…"
  }],
  "nextCursor": null,
  "total": 12
}
```

**Tạo** — `POST /api/v1/admin/gift-codes`:

```json
{
  "code": null,
  "rewardKind": "coin",
  "rewardQuantity": 500,
  "rewardHeroId": null,
  "rewardCosmeticId": null,
  "startsAt": "2026-11-08T00:00:00Z",
  "expiresAt": "2026-12-31T00:00:00Z",
  "maxRedemptions": 1000,
  "maxPerPlayer": 1,
  "isActive": true,
  "reason": "sự kiện tháng 11"
}
```

- `code: null` → **server tự sinh** mã 12 ký tự (chữ hoa + số, bỏ ký tự dễ nhầm). Trả về mã trong response.
- `rewardKind` ∈ {`coin`, `hero`, `cosmetic`}.
  - `coin` → `rewardHeroId`/`rewardCosmeticId` phải null.
  - `hero` → `rewardHeroId` bắt buộc, phải tồn tại.
  - `cosmetic` → `rewardCosmeticId` bắt buộc, phải tồn tại.
- `startsAt`/`expiresAt` null → mặc định `startsAt = now`, `expiresAt = null` (không hết hạn).
- `expiresAt` phải sau `startsAt` → `400 INVALID_EXPIRY`.

**Sửa** — `PATCH /api/v1/admin/gift-codes/{id}`:

```json
{ "isActive": false, "expiresAt": null, "maxRedemptions": 500, "maxPerPlayer": 1, "reason": "dừng phát" }
```

Không cho giảm `maxRedemptions` xuống dưới số lượt đã quy đổi → `409 MAX_REDEMPTIONS_BELOW_USED`.

**Lịch sử quy đổi** — `GET /api/v1/admin/gift-codes/{id}/redemptions`:

```json
{
  "items": [{ "id": "…", "playerId": "…", "playerDisplayName": "player1", "rewardKind": "coin",
              "rewardQuantity": 500, "coinBalanceAfter": "3400", "redeemedAt": "…" }],
  "nextCursor": null,
  "total": 42
}
```

**Quy đổi (người chơi)** — `POST /api/v1/gift-codes/redeem`:

```json
{ "code": "ABCDEF234567" }
```

```json
{ "code": "ABCDEF234567", "rewardKind": "coin", "rewardQuantity": 500, "heroId": null, "cosmeticId": null, "coinBalance": "3400" }
```

Mã lỗi nghiệp vụ (đều là `409` trừ khi nêu khác):

| Code | Nghĩa |
|---|---|
| `GIFT_CODE_INACTIVE` | Mã đã bị tắt. |
| `GIFT_CODE_NOT_STARTED` | Chưa tới `startsAt`. |
| `GIFT_CODE_EXPIRED` | Đã quá `expiresAt`. |
| `GIFT_CODE_EXHAUSTED` | Hết tổng lượt quy đổi. |
| `GIFT_CODE_ALREADY_REDEEMED` | Đã đạt giới hạn `maxPerPlayer`. |
| `HERO_ALREADY_OWNED` / `COSMETIC_ALREADY_OWNED` | Đã sở hữu phần thưởng đó. |

`404 GIFT_CODE_NOT_FOUND` khi mã không tồn tại. Quy đổi là **atomic**: dưới tải đồng thời, chỉ đúng số lượt được phép thành công; phần còn lại nhận `409`.

### 3.7 Trình xem trận (Match Inspector)

| Method | Path | Quyền |
|---|---|---|
| GET | `/api/v1/admin/matches` | `analytics` (đọc) |
| GET | `/api/v1/admin/matches/{id}` | `analytics` (đọc) |
| GET | `/api/v1/admin/matches/{id}/actions` | `analytics` (đọc) |

`GET /api/v1/admin/matches` — query `mode` (`ranked`/`bot`), `status`, `from`, `to`, `playerId`, `cursor`, `limit`.

```json
{
  "items": [{
    "id": "…uuid…",
    "mode": "ranked",
    "status": "completed",
    "result": "…",
    "endReason": "checkmate",
    "rulesetId": "…uuid…",
    "rulesetCode": "ranked_v1",
    "createdAt": "…", "startedAt": "…", "endedAt": "…", "settledAt": "…",
    "actionCount": 87,
    "sides": [{
      "side": "red", "playerId": "…uuid…", "displayName": "player1",
      "participantType": "player", "isBot": false, "difficulty": null,
      "outcome": "win", "eloBefore": 1000, "eloAfter": 1024, "coinReward": 10, "heroCount": 16
    }]
  }],
  "nextCursor": null,
  "total": 812
}
```

`GET /api/v1/admin/matches/{id}`:

```json
{
  "match": { "…": "AdminMatchDto" },
  "rulesetSnapshot": { "…": "jsonb snapshot lúc bắt đầu trận" },
  "contentVersion": "…",
  "lineups": [{
    "side": "red",
    "heroes": [{ "slotNo": 1, "heroId": "…", "heroCode": "…", "heroName": "…", "classCode": "general",
                 "setupPoints": 4, "factionCode": "wei", "startX": 0, "startY": 0 }],
    "skills":  [{ "slotNo": 1, "skillId": "…", "code": "…", "name": "…" }]
  }],
  "hasReplay": false,
  "firstActionAt": "…"
}
```

> `hasReplay` là `false` — **không** có deterministic replay. UI không được hiển thị nút "replay". `heroCount` là số quân thực tế → hỗ trợ kịch bản bất đối xứng, không giả định 16 quân mỗi phe.

`GET /api/v1/admin/matches/{id}/actions` — query `afterSequence` (mặc định `-1` = từ đầu), `limit` (mặc định 100):

```json
[{ "sequenceNo": 1, "kind": "move", "actorSide": "red",
   "request": { "…": "jsonb" }, "events": { "…": "jsonb" }, "committedAt": "…" }]
```

> `request`/`events` là `jsonb` thô của backend. UI nên hiển thị an toàn (trình xem JSON), không tự diễn giải nội dung hành động.

### 3.8 Phân tích (Analytics)

| Method | Path | Quyền |
|---|---|---|
| GET | `/api/v1/admin/stats` | `analytics` (đọc) |
| GET | `/api/v1/admin/analytics/balance` | `analytics` (đọc) |
| GET | `/api/v1/admin/analytics/bot` | `analytics` (đọc) |

`GET /api/v1/admin/stats`:

```json
{
  "totalPlayers": 128, "activePlayers": 120, "disabledPlayers": 8,
  "totalHeroes": 42, "enabledHeroes": 38, "inShopHeroes": 18,
  "totalMatches": 812, "completedMatches": 790, "rankedMatches": 500, "botMatches": 290, "activeMatches": 5,
  "totalCoinBalance": 432000,
  "adminCount": 3,
  "generatedAt": "…"
}
```

`GET /api/v1/admin/analytics/balance?mode=ranked&from=&to=`:

```json
{
  "mode": "ranked",
  "matchCount": 500,
  "playerMatchCount": 1000,
  "firstMoveAdvantage": { "value": 52.0, "sampleSize": 1000 },
  "averageDurationSeconds": 214.3,
  "heroes": [{ "heroId": "…", "heroCode": "…", "heroName": "…", "factionCode": "wei",
               "picks": 320, "matches": 290, "wins": 150, "draws": 20, "losses": 120,
               "winRate": 51.72, "pickRate": 32.0 }],
  "factions": [{ "factionCode": "wei", "matches": 210, "wins": 110, "draws": 15, "losses": 85, "winRate": 52.38 }],
  "skills":  [{ "skillId": "…", "skillCode": "…", "skillName": "…", "matches": 90, "uses": 140 }]
}
```

> `winRate` / `pickRate` / `value` là `null` khi **chưa đủ dữ liệu**. UI phải hiển thị "chưa đủ dữ liệu" chứ không hiển thị `0%`. `sampleSize` là cỡ mẫu — **luôn** hiển thị cạnh mọi tỉ lệ thắng.
>
> **Đơn vị phần trăm**: backend trả tỉ lệ ở thang **0–100** (đã nhân 100, ví dụ `51.72` nghĩa là 51.72%), không phải 0–1. Client hiển thị thẳng giá trị + `%`, không nhân thêm lần nữa.

`GET /api/v1/admin/analytics/bot?from=&to=`:

```json
{
  "botMatches": 290, "botWins": 120, "playerWins": 160, "draws": 10, "botWinRate": 41.38,
  "byDifficulty": [{ "difficulty": "easy", "matches": 100, "wins": 25, "draws": 5, "losses": 70, "winRate": 25.0 }],
  "byRuleset":   [{ "rulesetId": "…", "rulesetCode": "ranked_v1", "matches": 290, "wins": 120, "draws": 10, "losses": 160, "winRate": 41.38 }]
}
```

> Số liệu bot **tách riêng** khỏi cân bằng PvP. Không có latency / invalid-action telemetry (xem audit B7) — UI không hiển thị biểu đồ đó.

### 3.9 Nhật ký thao tác (Audit Log)

| Method | Path | Quyền |
|---|---|---|
| GET | `/api/v1/admin/audit` | `audit` (đọc) |

Tham số truy vấn: `cursor`, `limit` (mặc định 20), `action`, `entityType`, `entityId`, `actorUserId`, `from`, `to`.

```json
{
  "items": [{
    "id": "…uuid…",
    "actorUserId": "…uuid…",
    "action": "player.status.update",
    "entityType": "player",
    "entityId": "…uuid…",
    "before": { "…": "json" },
    "after":  { "…": "json" },
    "reason": "gian lận rank",
    "result": "success",
    "createdAt": "…"
  }],
  "nextCursor": null,
  "total": 342
}
```

### 3.10 Tài khoản admin

| Method | Path | Quyền |
|---|---|---|
| GET | `/api/v1/admin/accounts` | `admin` (đọc) |
| POST | `/api/v1/admin/accounts` | `admin` (ghi) |
| PUT | `/api/v1/admin/accounts/{id}/permissions` | `admin` (ghi) |
| GET | `/api/v1/admin/permissions` | `admin` (đọc) |

`GET /api/v1/admin/accounts`:

```json
{ "items": [{ "id": "…", "displayName": "ops1", "email": "ops1@example.com", "status": "active",
              "permissions": ["read", "player", "giftcode"], "createdAt": "…" }] }
```

**Tạo admin** — `POST /api/v1/admin/accounts`:

```json
{ "email": "ops2@example.com", "displayName": "ops2", "password": "…", "permissions": ["read", "analytics"] }
```

Mật khẩu phải đạt luật Identity. `permissions` null = toàn quyền (tương thích ngược).

**Đổi quyền** — `PUT /api/v1/admin/accounts/{id}/permissions`:

```json
{ "permissions": ["read", "shop", "giftcode"] }
```

Thay **toàn bộ** danh sách quyền. Gửi `["*"]` = toàn quyền. Gửi `[]` = không có quyền miền nào (nhưng vẫn là admin → vẫn đọc được `/api/v1/admin/permissions`? Không — `admin` read cần key `admin`; với `[]` sẽ bị 403 trên mọi endpoint admin).

---

## 4. Bảng phân quyền endpoint

| Endpoint | Policy |
|---|---|
| `GET /admin/stats`, `/admin/analytics/*`, `/admin/matches*` | `admin-read:analytics` |
| `GET /admin/heroes`, `/admin/heroes/{id}`, `/admin/skills`, `/admin/factions` | `admin-read:hero` |
| `GET /admin/rulesets` | `admin-read:ruleset` |
| `GET /admin/shop/heroes`, `/admin/shop/purchases` | `admin-read:shop` |
| `GET /admin/players*` | `admin-read:player` |
| `GET /admin/gift-codes*` | `admin-read:giftcode` |
| `GET /admin/audit` | `admin-read:audit` |
| `GET /admin/accounts`, `/admin/permissions` | `admin-read:admin` |
| `POST/PATCH/PUT/DELETE` tương ứng miền | `admin-write:{domain}` |

---

## 5. Gợi ý tích hợp React

```ts
const API = import.meta.env.VITE_ADMIN_API_URL ?? '/api/v1';

export async function adminFetch<T>(path: string, init: RequestInit = {}, token: string): Promise<T> {
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      ...init.headers,
    },
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({ code: 'UNKNOWN', message: res.statusText }));
    if (res.status === 401) { /* chuyển hướng về trang đăng nhập */ }
    throw Object.assign(new Error(body.message), { code: body.code, status: res.status });
  }
  return res.status === 204 ? (undefined as T) : res.json();
}
```

- **Tự động 401** → xoá token, chuyển trang đăng nhập.
- **403** → hiển thị "Bạn không có quyền X" (không phải lỗi hệ thống).
- **Số tiền**: định dạng từ chuỗi, không ép `Number` trước khi hiển thị (tránh mất độ chính xác với `bigint`).
- **Cursor**: giữ `nextCursor` trong state; nút "Tải thêm" chỉ hiện khi `nextCursor !== null`.
- **Hộp thoại xác nhận (confirm dialog)** bắt buộc cho: ban tài khoản, điều chỉnh coin, thu hero, tắt gift code, đổi giá hàng loạt, cấp/thu quyền admin.
- **Reason bắt buộc**: ban, điều chỉnh coin, cấp/thu hero, tạo/sửa gift code, đổi quyền admin → form phải kiểm tra hợp lệ (validate) trước khi gửi.

---

## 6. Những gì AdminWeb **không** được làm

| Không được | Vì sao |
|---|---|
| Gọi trực tiếp Supabase / Data API | Schema đã `REVOKE` cho `anon`/`authenticated`. Mọi truy cập phải qua API có authorization. |
| Đưa service-role key vào browser | Rò rỉ toàn quyền DB. |
| Tự sửa coin / hero ownership phía client | Phải qua endpoint admin có transaction + audit. |
| Xoá hero / xoá tài khoản người chơi | Phá lịch sử trận. Chỉ soft-disable. |
| Hiển thị deterministic replay | `hasReplay` luôn `false`. |
| Hiển thị win-rate khi `sampleSize` = 0 | `value` là `null`; phải hiện "chưa đủ dữ liệu". |
| Áp dụng luật Setup Points | Legacy read-only, chưa được duyệt. |
| Tự tính số liệu thay vì gọi API | Analytics đã tính sẵn từ dữ liệu thật. |
