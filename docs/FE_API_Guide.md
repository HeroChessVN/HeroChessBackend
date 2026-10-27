# Hero Chess API cho Frontend / Unity

Tài liệu này mô tả **API đang có trong source hiện tại** để làm màn hình đăng nhập, sảnh, đội hình và trận đấu. Base URL của bản Render hiện dùng:

```text
https://herochessbackend.onrender.com
```

Mọi REST route bên dưới bắt đầu bằng `/api/v1`. Ví dụ `GET /me` nghĩa là `GET https://herochessbackend.onrender.com/api/v1/me`. WebSocket dùng `wss://herochessbackend.onrender.com/ws/v1`. FE chỉ gọi backend; tài khoản/mật khẩu PostgreSQL hoặc Supabase **không đặt trong FE**. Nếu đổi domain Render, chỉ đổi base URL.

## 1. Quy ước chung

- Body và response là JSON, tên field **camelCase**. Request có body đặt `Content-Type: application/json`.
- Trừ đăng ký/đăng nhập và health check, gửi `Authorization: Bearer <accessToken>`. Token hết hạn thì gọi refresh; refresh thất bại thì đưa người dùng về màn hình đăng nhập.
- ID là UUID dạng chuỗi. Tọa độ bàn cờ: `x = 0..8`, `y = 0..9`; phía đỏ xuất phát gần `y = 0`. Luôn lưu `pieceId` riêng của quân trong trận, không dùng `heroId` thay nó.
- `/health/live` chỉ kiểm tra process; `/health/ready` kiểm tra thêm kết nối DB. Muốn xác nhận game dùng được, xem `/health/ready` trả 200.
- Production Render không bật Swagger. Swagger `/swagger` chỉ bật khi backend chạy môi trường Development.
- Đọc `status`, `code`, `message` trong response khi lỗi; đừng coi mọi response ngoài 200 là mất kết nối. `404 favicon.ico` trong trình duyệt không liên quan API.

## 2. Tài khoản và dữ liệu sảnh

| Method | Route | Body / kết quả chính |
|---|---|---|
| `POST` | `/auth/register` | `{ "email": "...", "password": "..." }`; tạo tài khoản Player. |
| `POST` | `/auth/login?useCookies=false` | Cùng body; nhận `accessToken`, `refreshToken`, `expiresIn`, `tokenType`. |
| `POST` | `/auth/refresh` | `{ "refreshToken": "..." }`; nhận cặp token mới. |
| `GET` | `/me` | `userId`, `displayName`, `role`, `status`, `playerId`, `coinBalance`, `elo`, `avatarName`, `frameName`, `avatarUrl`. |
| `PATCH` | `/me` | Sửa `displayName`, `avatarName`, `frameName`; trả lại hồ sơ đã cập nhật. |
| `POST` | `/me/avatar` | Upload ảnh từ máy (multipart, field `file`); trả lại hồ sơ kèm `avatarUrl`. |
| `GET` | `/catalog` | `ruleset`, `classes`, `slots`, `heroes`, `traits`, `factions`, `teamSkills`. |
| `GET` | `/me/heroes` | Mảng UUID hero đang sở hữu. Trong `/catalog`, mỗi hero cũng có `isOwned`. |
| `POST` | `/shop/heroes/{heroId}/purchase` | Gửi header `Idempotency-Key: <UUID>`; nhận `heroId`, `acquired`, `coinBalance`, `charged`. |

### Hồ sơ tài khoản

`GET /me` luôn trả `elo` kể cả khi bằng 0 — không được bỏ field. `avatarUrl` chỉ có giá trị khi người chơi **đã tải ảnh từ máy**; đó là URL tuyệt đối (`https://…` qua proxy Render, `http://…` khi chạy local dev). Chưa có ảnh thì `avatarUrl` là `null`, còn `avatarName`/`frameName` vẫn trả mã ảnh/khung đã chọn.

`PATCH /me` nhận JSON, field nào `null` thì giữ nguyên giá trị đang lưu:

```json
{ "displayName": "Lê Lợi", "avatarName": "leloi", "frameName": "khung-vang" }
```

`avatarName` và `frameName` là **mã** dạng chuỗi không dấu cách, không chứa `/` hoặc `\`; sai thì trả `422 INVALID_AVATAR_NAME` / `422 INVALID_FRAME_NAME`. `displayName` dài 1–80 ký tự, sai thì trả `422 INVALID_DISPLAY_NAME`.

`POST /me/avatar` nhận `multipart/form-data` với field `file`; chỉ nhận `image/png`, `image/jpeg`, `image/webp`, tối đa 2 MB (sai thì `415 AVATAR_TYPE_UNSUPPORTED` / `413 AVATAR_TOO_LARGE`). Tên file do server sinh, không dùng tên file từ client. Đổi tên, ảnh hoặc khung xong thì **lần ghép trận sau** trả giá trị mới — không cần reload app.

Đăng ký **không tự đăng nhập**; gọi login ngay sau đó. `/catalog` chỉ trả hero đang bật, kèm `id`, `name`, `classCode`, `setupPoints`, `trait`, `factionIds`, `isOwned`, `coinPrice`. Lấy `ruleset.id`, `slots` và ID hero/skill từ catalog; đừng hard-code UUID. Account mới có thể chưa đủ hero để xếp đội nếu cấu hình hero khởi đầu trên server chưa hoàn tất.

## 3. Đội hình

| Method | Route | Ý nghĩa |
|---|---|---|
| `GET` | `/lineups` | Danh sách đội hình của Player. |
| `POST` | `/lineups` | Tạo đội hình; trả `201` và `LineupDto`. |
| `PUT` | `/lineups/{id}` | Sửa đội hình, cần `expectedRevision`. |
| `DELETE` | `/lineups/{id}?expectedRevision=N` | Xóa; thành công trả `204`. |

Body tạo/sửa:

```json
{
  "name": "Đội hình 1",
  "rulesetId": "<catalog.ruleset.id>",
  "entries": [
    { "slotNo": 1, "heroId": "<hero UUID đúng class của slot>", "cosmeticId": null }
  ],
  "skills": [
    { "slotNo": 1, "skillId": "<team skill UUID>" }
  ]
}
```

`entries` phải điền đủ các slot mà `catalog.slots` yêu cầu và hero phải thuộc sở hữu Player. `skills` là các command skill chọn cho đội hình (slot skill 1..3), khác với `entries` của quân cờ. Khi sửa, thêm `"expectedRevision": <lineup.revision>`. Response có `id`, `revision`, `totalSp`, `budget`, `isValid`, `validationErrors`. Chỉ dùng lineup hợp lệ để vào trận.

## 4. Vào trận với bot / PvP

1. `POST /matchmaking/tickets` với `{ "mode": "bot" }` hoặc `{ "mode": "ranked" }`. Nhận `ticketId`, `status`, `matchId` (có thể `null`). Chế độ `ranked` ghép hai Player; chế độ `bot` tạo đối thủ bot. Player đấu bot ở phe đỏ; PvP phe phụ thuộc thứ tự ghép, đọc `side` từ `/selection` hoặc lịch sử trận.
2. Nếu `matchId` còn `null`, poll `GET /matchmaking/tickets/{ticketId}` cho đến khi có `matchId`. Có thể `DELETE` ticket khi còn đang chờ. Đừng tạo ticket mới ở mỗi lần poll.
3. `GET /matches/{matchId}/selection` để xem hai bên đã confirm chưa. Mỗi người `PUT /matches/{matchId}/selection` với `{ "lineupId": "<UUID>", "expectedRevision": <lineup.revision> }`.
4. Mỗi người `POST /matches/{matchId}/confirm` (body rỗng). Trận bot cần Player confirm; PvP cần cả hai confirm. Trong lúc chọn đội, `DELETE /matches/{matchId}/selection` để hủy. Khi trận đã active, muốn rời trận phải gửi lệnh `resign`.
5. Khi active, `GET /matches/{matchId}/state`. Trước đó route này có thể trả `409 MATCH_NOT_STARTED`.

`/selection` trả **cả hai phe** với hồ sơ công khai và đội hình đang dùng, dùng cho màn ghép trận. `ownLineupId`/`ownLineupRevision` chỉ có ở phe người đang gọi API; phe đối thủ vẫn thấy `player` và `selectedLineup` (đội hình họ đã chọn cho trận này). Phe bot chỉ có `isBot: true`, không có `player` hay `selectedLineup`.

```json
{
  "matchId": "<UUID>",
  "mode": "ranked",
  "status": "selecting",
  "sides": [
    {
      "side": "red",
      "confirmed": true,
      "isBot": false,
      "ownLineupId": "<UUID đội hình của tôi>",
      "ownLineupRevision": 3,
      "player": {
        "playerId": "<UUID>",
        "displayName": "Lê Lợi",
        "elo": 1200,
        "avatarName": "leloi",
        "frameName": null,
        "avatarUrl": "https://herochessbackend.onrender.com/avatars/<id>.png"
      },
      "selectedLineup": {
        "id": "<UUID>",
        "name": "Đội hình 1",
        "totalSp": 16,
        "entries": [ { "slotNo": 1, "heroId": "<UUID hero>" } ],
        "skills": [ { "slotNo": 1, "skillId": "<UUID skill>" } ]
      }
    },
    {
      "side": "black",
      "confirmed": false,
      "isBot": true,
      "ownLineupId": null,
      "ownLineupRevision": null,
      "player": null,
      "selectedLineup": null
    }
  ]
}
```

`player.elo` là 0 vẫn phải trả, không được bỏ field. `avatarUrl` là `null` nếu người chơi chưa tải ảnh từ máy. `selectedLineup` là `null` khi phe đó chưa chọn đội hình (kèm `confirmed: false`).

**Thời điểm lộ đội hình:** `selectedLineup` xuất hiện **ngay khi phe đó `PUT /selection`**, chưa cần `POST /confirm`. Đối thủ poll `GET /selection` sẽ thấy đội hình của bạn trong lúc bạn còn đang chọn; `confirmed` chỉ chuyển `true` sau `POST /confirm`. Vì vậy màn chờ có thể hiện đội hình đối thủ theo thời gian thực, còn nút "Xác nhận" chỉ khóa lựa chọn và bắt đầu trận khi cả hai đã xác nhận.

`GET /matches/{matchId}/selection`, response của `PUT /matches/{matchId}/selection` và `POST /matches/{matchId}/confirm` đều dùng chung shape trên. Ticket matchmaking nằm trong RAM backend; sau restart phải kiểm tra lịch sử trận và tạo ticket mới nếu ticket cũ mất.

## 5. Xếp hạng

| Method | Route | Kết quả |
|---|---|---|
| `GET` | `/ranking?limit=50&cursor=<nextCursor>` | `items`, `nextCursor`, `total`; mỗi item có `rank`, `playerId`, `displayName`, `rating`, `gamesPlayed`, `wins`, `draws`, `losses`. |
| `GET` | `/ranking/me` | `rank`, `playerId`, `displayName`, `rating`, `gamesPlayed`, `wins`, `draws`, `losses`, `total`. |

```json
{
  "items": [
    { "rank": 1, "playerId": "<UUID>", "displayName": "Player", "rating": 1000,
      "gamesPlayed": 0, "wins": 0, "draws": 0, "losses": 0 }
  ],
  "nextCursor": "<base64>",
  "total": 2
}
```

`/ranking` là bảng xếp hạng công khai: `rating` (Elo) giảm dần, `playerId` tăng dần làm tie-break; `rank` là hạng tuyệt đối trong toàn bảng, không phải thứ tự trong trang. `limit` mặc định 50, server giới hạn 1..100. Phân trang bằng cursor keyset (không phải offset): lấy `nextCursor` của trang trước rồi truyền nguyên vẹn vào `cursor`; `nextCursor` là `null` khi hết trang, `total` là tổng số người có hồ sơ rating. Cursor sai định dạng trả `400 INVALID_CURSOR`.

`/ranking/me` lấy danh tính từ token, không nhận `playerId` trong request; `rank` là `null` khi người chơi chưa có hồ sơ rating. Cả hai route yêu cầu tài khoản Player: thiếu token trả `401`, tài khoản admin hoặc bị disable trả `403`. Endpoint này không lộ email hay thông tin nhạy cảm. Elo chỉ đổi sau khi trận ranked settle, nên đọc lại `/ranking/me` (và `/ranking`) sau mỗi ván.

## 6. Bàn cờ, nước đi và skill

| Method | Route | Dùng để |
|---|---|---|
| `GET` | `/matches/{matchId}/state` | Snapshot chuẩn để vẽ bàn cờ. |
| `GET` | `/matches/{matchId}/legal-actions` | Nước đi thường hợp lệ; dùng để tô ô đến. |
| `GET` | `/matches/{matchId}/hero-actions?pieceId={pieceId}` | Ô đích hợp lệ của skill đi đặc biệt Quang Trung. |
| `POST` | `/matches/{matchId}/commands` | Gửi nước đi, skill, undo hoặc đầu hàng. |

`/state` trả các field ngoài cùng: `matchId`, `mode`, `status`, `version`, `sideToMove`, `turnIndex`, `countedActions`, `deadlineAt`, `serverNow`, `stateSchemaVersion`, `state`, `settledAt`, `sides`. Trong `state` có `pieces`, `obstacles`, `skillStates`, `effectInstances`, `result`, `endReason`. Mỗi quân có `pieceId`, `heroId`, `heroName`, `side`, `class`, `position`, `status`, `effects`, `traitState` và metadata hero/trait. `skillStates.red` và `.black` chứa `slotNo`, `implementationKey`, `usesRemaining`, `cooldownRemaining`, `name` để hiển thị skill/CD cho cả hai phe. Cọc ẩn của đối thủ được backend lọc khỏi snapshot/replay.

`sides` ở cấp ngoài cùng của `/state` là hồ sơ người chơi của **cả hai phe**, dùng để vẽ tên/điểm/ảnh/khung trên bàn cờ. Cùng shape với `sides` của `/selection`:

```json
"sides": [
  { "side": "red",  "isBot": false, "player": { "playerId": "<UUID>", "displayName": "Lê Lợi", "elo": 1200, "avatarName": "leloi", "frameName": null, "avatarUrl": null } },
  { "side": "black", "isBot": false, "player": { "playerId": "<UUID>", "displayName": "Nguyễn Huy", "elo": 1180, "avatarName": null, "frameName": null, "avatarUrl": null } }
]
```

`heroName` có cho cả quân đồng minh lẫn quân đối thủ, và `heroId` luôn trỏ đúng hero trong `/catalog`. `player.elo` là 0 vẫn phải trả.

`/legal-actions` trả mảng `{ pieceId, fromX, fromY, toX, toY, capturedPieceId }`. Chỉ gửi nước trong danh sách server. Mỗi command gửi UUID mới ở `commandId`, `expectedVersion` bằng `version` mới nhất:

```json
{
  "commandId": "<UUID mới>",
  "expectedVersion": 0,
  "action": { "type": "move", "pieceId": "<pieceId>", "to": { "x": 0, "y": 1 } }
}
```

Các `action` khác đặt vào cùng phong bì trên:

```json
{ "type": "team_skill", "slot": 1, "target": { "position": { "x": 3, "y": 3 } } }
{ "type": "team_skill", "slot": 2, "target": { "pieceId": "<quân đồng minh>" } }
{ "type": "hero_active", "pieceId": "<pieceId>", "skillCode": "tran_hung_dao_tuong.coc", "target": { "position": { "x": 3, "y": 4 } } }
{ "type": "hero_active", "pieceId": "<pieceId>", "skillCode": "quang_trung.special_move", "target": { "to": { "x": 4, "y": 2 } } }
{ "type": "undo", "targetSequence": 0 }
{ "type": "resign" }
```

Với `team_skill`, chọn `slot` theo `state.skillStates.<phe>.slotNo`, rồi dựng `target` theo `implementationKey`: `thanh`/`rao` dùng `position`; `khien` dùng `pieceId` của quân mình. Những command skill khác có target riêng, xem `implementationKey`/`parameters` từ catalog và [mã FE mẫu](../frontend/src/api.js) trước khi dựng UI. Thành/Rào chỉ đặt trên nửa sân mình và ô trống; nếu đặt lên ô có quân, backend trả `422 CELL_OCCUPIED`. FE nên khóa các ô này trước khi bấm và vẫn hiện thông báo lỗi từ backend nếu bị từ chối. `hero_active` là một lượt thay cho đi thường; skill Quang Trung dùng `/hero-actions` để lấy ô đến. `undo` chỉ dành cho trận bot.

Command hợp lệ trả `MatchCommandResultDto`: `matchId`, `commandId`, `sequenceNo`, `duplicate`, `resolvedEvents`, `snapshot`. Render bàn cờ từ `snapshot` mới; bot có thể đi ngay sau đó nên phải nhận cập nhật tiếp. Retry **cùng một command** bằng đúng `commandId` và action cũ để tránh ghi hai lần. Khi `409 STALE_STATE`, lấy `/state` mới rồi cho người chơi chọn lại nước với `commandId` mới. Deadline dùng `deadlineAt` so với `serverNow`; server tự xử lý hết giờ, FE chỉ hiển thị đếm ngược.

## 7. Đồng bộ thời gian thực

REST polling đủ để làm bản demo; nếu cần cập nhật ngay giữa hai máy, dùng WebSocket:

1. `POST /ws-ticket` với bearer token. Response `{ "ticket": "...", "expiresAt": "..." }`.
2. Nối `wss://herochessbackend.onrender.com/ws/v1?ticket=<ticket>` trong vòng 30 giây. Vé dùng **một lần**; ngắt kết nối thì xin vé mới.
3. Gửi `{"type":"match.subscribe","requestId":"<UUID>","payload":{"matchId":"<UUID>"}}`. Server trả `match.state` với snapshot.
4. Khi nhận `match.changed`, gọi lại `GET /matches/{matchId}/state` và legal actions nếu cần. Có `match.ended`/`match.settled` thì cập nhật kết quả/số dư.

Có thể gửi `match.command` qua socket với payload `{ "matchId": "<UUID>", "commandId": "<UUID>", "expectedVersion": 0, "action": { ... } }`; server trả `match.command_accepted` hoặc `match.command_rejected`. Dùng REST `POST /commands` thường đơn giản hơn. Sau reconnect hoặc mất event, luôn GET `/state` để đồng bộ lại. WebSocket browser có kiểm tra Origin cùng host; FE chạy khác domain cần reverse proxy/same-origin hoặc cấu hình backend phù hợp. Client Unity native không cần tự gửi Origin header.

## 8. Lịch sử, replay và lỗi

| Method | Route | Kết quả |
|---|---|---|
| `GET` | `/matches?limit=20&cursor=<nextCursor>` | `items`, `nextCursor`; mỗi item có `matchId`, `status`, `side`, `result`, `endReason`, `ratingChange`, `bonus`, `sides`. |
| `GET` | `/matches/{matchId}` | Chi tiết trận: `sides` (kèm `player` và `lineup` đã đóng băng), `result`, `endReason`, `actionCount`. |
| `GET` | `/matches/{matchId}/replay?afterSequence=-1&limit=50` | `entries`, `nextAfterSequence`; mỗi entry có `sequenceNo`, `kind`, `actorSide`, `resolvedEvents`, `stateAfter`, `committedAt`. |

Một item của `/matches` có đủ dữ liệu cho màn tổng kết:

```json
{
  "matchId": "<UUID>",
  "mode": "ranked",
  "status": "completed",
  "side": "red",
  "result": "red_win",
  "endReason": "resign",
  "createdAt": "2026-10-27T08:00:00+00:00",
  "endedAt": "2026-10-27T08:12:30+00:00",
  "outcome": "win",
  "eloBefore": 1200,
  "eloAfter": 1214,
  "eloDelta": 14,
  "ratingChange": 14,
  "bonus": 0,
  "durationSeconds": 750,
  "replayAvailable": true,
  "sides": [
    { "side": "red",  "isBot": false, "player": { "playerId": "<UUID>", "displayName": "Lê Lợi", "elo": 1214, "avatarName": "leloi", "frameName": null, "avatarUrl": null } },
    { "side": "black", "isBot": false, "player": { "playerId": "<UUID>", "displayName": "Nguyễn Huy", "elo": 1186, "avatarName": null, "frameName": null, "avatarUrl": null } }
  ]
}
```

- `result` là `red_win`, `black_win` hoặc `draw` — so sánh với `side` của người gọi API để biết thắng/thua.
- `endReason` là `checkmate`, `resign`, `timeout`, `stalemate`, `draw` hoặc `agreement`.
- `ratingChange` là điểm Elo người gọi API được/mất (âm = mất, 0 = hòa hoặc không đổi).
- `bonus` là điểm thưởng thêm của trận, 0 nếu không có.
- `sides[].player.elo` là điểm **sau trận**; phe bot có `isBot: true` và `player: null`.
- `createdAt`/`endedAt` là ISO-8601; `durationSeconds` là số giây.

Replay bắt đầu bằng action `start` ở `sequenceNo = 0`: đó là bàn cờ ban đầu **chưa ai đi**. Các nước tiếp theo là sequence 1..N. Phân trang tiếp bằng `afterSequence=nextAfterSequence`. `resolvedEvents` là sự kiện máy đọc như `piece.moved`, `turn.started`, `shield.applied`; UI có thể chuyển thành câu tiếng Việt. `stateAfter` là snapshot đã lưu sau action, không cần tính lại luật trong FE. Mỗi entry có `committedAt` (ISO-8601) để tính thời gian suy nghĩ của mỗi nước; `stateAfter.pieces` có `heroId` và `heroName` cho cả hai phe.

Lỗi nghiệp vụ thường có dạng:

```json
{ "code": "CELL_OCCUPIED", "message": "Cannot place Rào on a cell occupied by a piece.", "requestId": "...", "details": null }
```

`400`: body/action sai; `401`: chưa đăng nhập/token hết hạn; `403`: sai quyền; `404`: không có tài nguyên hoặc không phải người trong trận; `409`: version/deadline/trạng thái đã đổi; `422`: nước/skill không hợp lệ; `500`: lỗi backend/DB, nên hiện thông báo và cho thử lại, không khóa bàn cờ vĩnh viễn. Nếu nhiều route cùng trả 500, kiểm tra `/health/ready` và log backend. Đừng gửi liên tục request 500 mỗi frame; polling nên giãn nhịp và báo lỗi rõ ràng.

## 9. Cấu hình Unity hiện tại

Trong [HeroChessApi.cs](../../../../Unity/Capstone-Hero-Chess/Assets/HeroChess/Scripts/Runtime/Backend/HeroChessApi.cs), `BaseUrl` là **origin**, không kèm `/api/v1` vì client tự nối prefix đó. Giá trị cần đặt cho bản Render là `https://herochessbackend.onrender.com` (không thêm `/` cuối). Nếu scene có ô Endpoint thì điền URL này; bản Unity đang lưu vào `PlayerPrefs` key `HeroChess.ApiUrl`. Để FE hoạt động trên máy khác, máy đó chỉ cần internet và đúng base URL; không cần IP LAN, port 5012, Supabase URL hay DB password. Trên Render gói free, lần gọi đầu sau thời gian ngủ có thể chậm hơn timeout client Unity hiện tại (15 giây); khi đó thử lại sau khi `/health/ready` đã lên 200.

Nguồn chuẩn để đối chiếu khi backend thay đổi: [DTO](../HeroChess.Contracts/ApiContracts.cs), [controller](../HeroChess.Api/Controllers/MatchesController.cs), [ranking](../HeroChess.Api/Controllers/RankingController.cs), [pipeline command](../HeroChess.Api/Services/MatchCommandService.cs), [WebSocket](../HeroChess.Api/Services/WebSocketEndpoint.cs). Tài liệu này mô tả source, không xác nhận Render đã deploy đúng commit mới nhất; nếu behavior khác, kiểm tra commit đang chạy trên Render.
