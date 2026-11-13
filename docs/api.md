# Hero Chess API cho Frontend / Unity

Tài liệu này mô tả API của backend HeroChess cho Unity và web test. Base URL Azure:

```text
https://herochessbe.eastasia.cloudapp.azure.com
```

Mọi REST route bên dưới bắt đầu bằng `/api/v1`. Ví dụ `GET /me` gọi `GET /api/v1/me` trên origin đã cấu hình. WebSocket dùng cùng host với HTTPS và đường dẫn `/ws/v1`. Unity gửi bearer token cho REST và lấy vé riêng để nối WebSocket.

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

Gọi login sau khi đăng ký. `/catalog` trả hero đang bật kèm `id`, `name`, `classCode`, `setupPoints`, `factionIds`, `isOwned`, `coinPrice` và `trait` nếu hero có trait. Mảng `traits` tổng hợp trait của những hero đang bật. Lấy `ruleset.id`, `slots` và ID hero/skill từ catalog. Account mới cần đủ hero để xếp đội theo cấu hình hero khởi đầu trên server.

### Hero và faction trên Unity

Đọc dữ liệu từ `GET /api/v1/catalog`.

- Dùng `heroes[].id` để gửi lựa chọn đội hình và `heroes[].name` để hiển thị tên có dấu. Một nhân vật có thể có nhiều biến thể với ID và hệ quân riêng, nên đối chiếu thêm `classCode` khi hiển thị.
- Ở màn xếp đội hình, đọc `heroes[].setupPoints` cho từng quân và `ruleset.setupBudget` cho giới hạn tổng SP. Sau khi lưu, dùng `totalSp`, `budget` và `validationErrors` từ phản hồi lineup. Trong trận và replay, dùng tên và SP từ snapshot của trận.
- Ghép từng ID trong `heroes[].factionIds` với `factions[].id` để lấy `factions[].name`. Hiện có ba phe Trần, Hậu Lê và Tây Sơn. Mảng `factionIds` rỗng thì bỏ nhãn phe.
- Nếu `heroes[].trait` là `null` thì ẩn phần trait của hero. Nếu có, hiển thị `trait.name` và `trait.description` khi có mô tả. Trong trận và replay, dùng metadata trait từ snapshot.
- Có thể chọn tối đa một faction skill trong ba ô Command Skill. Để dùng một trong ba skill dưới đây, đội hình cần ít nhất 5 hero khác nhau cùng phe trong 11 ô ngoài Tốt. BE kiểm tra lúc lưu và xác nhận đội hình. Quyền dùng được chốt khi vào trận, quân bị ăn sau đó không khóa lại kỹ năng.

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

`/selection` trả **cả hai phe** kèm hồ sơ công khai và đội hình đã chọn cho trận này: mỗi side có `side`, `confirmed`, `isBot`, `player` (`playerId`, `displayName`, `elo`, `avatarName`, `frameName`, `avatarUrl`) và `selectedLineup` (`id`, `name`, `totalSp`, `entries`, `skills`). `ownLineupId`/`ownLineupRevision` chỉ có ở phe người đang gọi API. Phe bot chỉ có `isBot: true`, không có `player` hay `selectedLineup`. `elo` bằng 0 vẫn phải trả; `avatarUrl` là `null` nếu người chơi chưa tải ảnh từ máy. Ticket matchmaking nằm trong RAM backend; sau restart phải kiểm tra lịch sử trận và tạo ticket mới nếu ticket cũ mất.

## 5. Bàn cờ, nước đi và skill

| Method | Route | Dùng để |
|---|---|---|
| `GET` | `/matches/{matchId}/state` | Snapshot chuẩn để vẽ bàn cờ. |
| `GET` | `/matches/{matchId}/legal-actions` | Nước đi thường hợp lệ; dùng để tô ô đến. |
| `GET` | `/matches/{matchId}/hero-actions?pieceId={pieceId}` | Ô đích hợp lệ của kỹ năng di chuyển Quang Trung. Chỉ trả ô cho quân của phe người gọi khi đến lượt. |
| `GET` | `/matches/{matchId}/forecast?version={sequenceNo}` | Dự đoán riêng cho người đã dùng Lam Sơn Quân Lược. |
| `POST` | `/matches/{matchId}/commands` | Gửi nước đi, skill, undo hoặc đầu hàng. |

`/state` trả các field ngoài cùng: `matchId`, `mode`, `status`, `version`, `sideToMove`, `turnIndex`, `countedActions`, `deadlineAt`, `serverNow`, `stateSchemaVersion`, `state`, `settledAt`, `sides`. Trong `state` có `pieces`, `obstacles`, `skillStates`, `effectInstances`, `result`, `endReason`. Mỗi quân có `pieceId`, `heroId`, `heroName`, `side`, `class`, `position`, `status`, `effects`, `traitState` và metadata hero/trait. `skillStates.red` và `.black` chứa `slotNo`, `implementationKey`, `usesRemaining`, `cooldownRemaining`, `name`, `description` để hiển thị Command skill/CD cho cả hai phe. `sides` là hồ sơ người chơi của cả hai phe, cùng shape với `sides` của `/selection`. Cọc Hero skill cũ được backend lọc khỏi snapshot/replay.

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
{ "type": "hero_active", "pieceId": "<Trần Hưng Đạo hệ Tượng>", "skillCode": "tran_hung_dao_tuong.coc", "target": { "position": { "x": 4, "y": 5 } } }
{ "type": "hero_active", "pieceId": "<Quang Trung>", "skillCode": "quang_trung.special_move", "target": { "to": { "x": 4, "y": 2 } } }
{ "type": "undo", "targetSequence": 0 }
{ "type": "resign" }
```

Với `team_skill`, chọn `slot` theo `state.skillStates.<phe>.slotNo`, rồi dựng `target` theo `implementationKey`. Thành và Rào dùng `position`, còn Khiên dùng `pieceId` của quân mình. `undo` dành cho trận bot và có thể hoàn tác `hero_active`.

### Tích hợp Hero Skill trên Unity

Catalog dùng để hiển thị tên, SP, phe và trait khi xếp đội hình. Khi đang đánh, kiểm tra `state.publicRiverStakes == true` và đọc kỹ năng từ quân tương ứng trong `state.pieces`. Chỉ cho thao tác khi quân còn sống, thuộc phe mình và tới lượt mình. Dùng `pieceId` của quân trong trận khi gửi lệnh.

| Hero | Điều kiện và cách chọn ô |
|---|---|
| Trần Hưng Đạo hệ Tượng | Kiểm tra `traitState.heroCooldownRemaining` và `state.thdTuongCocStakes[pieceId]`. Chọn ô sông trống ở hàng `y=4` hoặc `y=5`, rồi gửi `hero_active` với `target.position`. |
| Quang Trung hệ Tướng | Kiểm tra `traitState.cooldownReady == 1` và `quangTrungCooldownRemaining` bằng 0 hoặc thiếu. Gọi `/hero-actions` để tô các đích server cho phép rồi gửi `hero_active` với `target.to`. |

### Faction Skill

Unity đọc `teamSkills[].eligibility.minNonSoldierFactionHeroes` từ `GET /api/v1/catalog` để hiển thị **và kiểm tra** số tướng cần có, không ghi cố định 5 hoặc 8. BE cũng kiểm tra bằng chính trường `team_skill.eligibility` này trong DB. Ba phe có thể có ngưỡng khác nhau; giá trị hợp lệ là số nguyên từ 1 đến 11.

Ví dụ một phần tử trong `teamSkills` (chỉ trích các trường liên quan):

```json
{
  "code": "bach_dang_tran_phap",
  "factionId": "<uuid-phe-Tran>",
  "eligibility": { "minNonSoldierFactionHeroes": 5 }
}
```

FE đếm các `heroId` khác nhau trong đội hình có `classCode != "SOLDIER"` và `factionIds` chứa `skill.factionId`, rồi so với ngưỡng của skill. Năm Tốt không được tính, kể cả model cùng phe. Thiếu trường hoặc cấu hình sai thì tải lại catalog và không tự mặc định 8. Khi BE trả `FACTION_REQUIREMENT_NOT_MET`, đọc lại catalog và đội hình thay vì ép gửi lệnh. Điều kiện được kiểm tra khi lưu/chọn đội hình; mất quân trong trận không thu hồi faction skill đã chọn.

Đổi ngưỡng bằng cách sửa `team_skill.eligibility.minNonSoldierFactionHeroes` trên DB, giữ các thuộc tính eligibility khác. Trận mới dùng dữ liệu mới, trận đã bắt đầu giữ skill đã chốt. Nếu catalog đổi giữa lúc ghép trận và xác nhận, cần chọn lại theo phản hồi BE. File `18_faction_skill_requirements.sql` là bản chuyển đổi một lần từ ngưỡng cũ về 5; không chạy lại nó sau khi đã tự cân bằng ngưỡng khác. Startup không chạy lại file này để tránh ghi đè cấu hình đã sửa. Các hàm điều kiện riêng cho từng phe vẫn được giữ để mở rộng sau này.

| Phe | `implementationKey` | `target` | CD |
|---|---|---|---:|
| Trần | `bach_dang_tran_phap` | `{"positions":[{"x":2,"y":4},{"x":4,"y":4},{"x":6,"y":5}]}` | 8 |
| Hậu Lê | `lam_son_quan_luoc` | `{}` | 16 |
| Tây Sơn | `hich_xuat_quan` | `{}` | 12 |

Cả ba lệnh tiêu một lượt. `state.riverSnares` chỉ có bẫy của phe người gọi, mỗi bẫy có `owner`, `position` và `expiresAt` theo `turnIndex`. Bẫy sống 6 lượt và mất sau lần khóa đầu tiên. Hiệu ứng trên quân xuất hiện trong `pieces[].effects` với mã `bach_dang_rooted` hoặc `hich_xuat_quan` và `remainingTurns`.

Unity lấy `slot` từ `state.skillStates[ownSide]` và gửi `action.type = "team_skill"`. Với Bạch Đằng Trận Pháp, xác nhận khi đã chọn đủ ba ô sông trống khác nhau. Quân trúng bẫy đứng ở ô đích và bị khóa 4 lượt. Với Hịch Xuất Quân, tô đích của Tốt theo `/legal-actions`. Nếu server cho đi hai ô thì gửi như một nước đi thường. `riverSnares` và vật cản Cọc `thd_tuong_coc` là hai dữ liệu riêng khi vẽ bàn.

Sau khi nhận kết quả dùng Lam Sơn Quân Lược, người kích hoạt gọi `GET /api/v1/matches/{matchId}/forecast?version=<sequenceNo>`. Phản hồi có `version`, `prediction.variation` (tối đa ba hành động) và `prediction.threats` (tối đa hai phương án ở lượt địch đầu tiên). Mỗi mục có `side`, `action`, cùng `from`, `pieceClass`, `pieceName` hoặc `skillName` khi có. Máy chủ giữ kết quả riêng theo người chơi, trận và phiên bản bàn cờ trong tối đa hai phút. Chỉ hiển thị khi phiên bản phản hồi khớp bàn cờ. `409 STALE_STATE` nghĩa là bàn cờ đã đổi trước khi máy chủ tính kết quả. `429 FORECAST_BUSY` nghĩa là hàng chờ dự đoán đã đầy.

Hero Trait và ba faction skill mới mặc định tắt cho trận mới. Sau khi kiểm tra bản deploy, bật `MatchRuntime__HeroTraitsEnabled=true` hoặc `MatchRuntime__NewFactionSkillsEnabled=true` trong env của API. Faction skill còn cần các dòng catalog từ SQL `16_faction_skills.sql`. Khi cần quay về image cũ, tắt hai cờ trên bản hiện tại rồi chờ các trận đã chốt luật mới kết thúc trước khi thay image.

Trong trận có `state.publicRiverStakes`, đọc trait trên `state.pieces`, hồi chiêu từ `traitState` và Cọc Bạch Đằng Giang từ `state.obstacles` với `kind: "thd_tuong_coc"`. Phe đặt là `state.obstacleMetadata[obstacleId].placer`. Khi `thdTuongCocStakes[pieceId]` còn tồn tại, nút đặt Cọc của quân đó cần khóa kể cả hồi chiêu bằng 0. Cả hai bên thấy Cọc. Nước dính Cọc có thể hợp lệ và tạo sự kiện `river_stake.hit` với `pieceId`, `position`, `side` của Cọc và `victimSide` của quân bị loại. Kết thúc do Cọc dùng `endReason: "river_stake"`.

Command hợp lệ trả `MatchCommandResultDto`: `matchId`, `commandId`, `sequenceNo`, `duplicate`, `resolvedEvents`, `snapshot`. Render bàn cờ từ `snapshot` mới; bot có thể đi ngay sau đó nên phải nhận cập nhật tiếp. Retry **cùng một command** bằng đúng `commandId` và action cũ để tránh ghi hai lần. Khi `409 STALE_STATE`, lấy `/state` mới rồi cho người chơi chọn lại nước với `commandId` mới. Deadline dùng `deadlineAt` so với `serverNow`; server tự xử lý hết giờ, FE chỉ hiển thị đếm ngược.

### Trang phục bàn cờ

Bốn code khớp Unity là `mac-dinh`, `son-mai`, `son-son`, `su-thanh`. Bàn mặc định được sở hữu sẵn. Giá khởi tạo ba mẫu còn lại là 200 gold mỗi mẫu. Mỗi người nhìn toàn bộ bàn theo mẫu của mình, áp dụng khi mở bàn tiếp theo.

| Method và route | Request và response |
|---|---|
| GET /board-cosmetics | Trả `items` và `selectedCode` cho tài khoản Player |
| POST /shop/board-cosmetics/{id}/purchase | Header `Idempotency-Key: <UUID>`. Trả `boardCosmeticId, acquired, coinBalance, charged` |
| PUT /me/board-cosmetic | Body `{"code":"son-son"}`. Trả item đã trang bị |
| PATCH /admin/board-cosmetics/{id}/price | Admin gửi `{"coinPrice":"200","reason":"..."}`, lưu audit |

Mỗi item có `id, code, name, coinPrice, assetKey, previewKey, isOwned`. Giá và số dư là chuỗi số nguyên như shop hero. Item mặc định luôn có trong catalog. Mẫu bị tắt ẩn khỏi catalog, lựa chọn effective quay về `mac-dinh`.

Lỗi mua gồm `BOARD_NOT_FOUND`, `BOARD_DISABLED`, `BOARD_ALREADY_OWNED`, `INSUFFICIENT_COINS`, `INVALID_IDEMPOTENCY_KEY` và `IDEMPOTENCY_KEY_CONFLICT`. Lỗi trang bị mẫu chưa sở hữu là `BOARD_NOT_OWNED`. Mỗi lần mua mới tạo UUID mới. Khi retry cùng lần mua, giữ UUID và ID bàn cũ. Dùng key đã mua hero cho bàn cờ hoặc ngược lại trả 409.

Unity nối như sau.

1. Sau login và vào shop, GET catalog bàn để nhận quyền sở hữu và `selectedCode`. Tra model theo code trong BoardCosmeticCatalog hiện có.
2. Shop hiển thị MUA hoặc TRANG BỊ theo `isOwned`. Mua thành công cập nhật ví, catalog và quyền trang bị. Server không tự trang bị sau khi mua.
3. Khi bấm TRANG BỊ, PUT lựa chọn. Chỉ cập nhật cache BoardCosmeticCatalog sau response thành công.
4. Cache phải tách theo userId. Logout hoặc đổi tài khoản nạp lại lựa chọn. PlayerPrefs cũ chỉ là lựa chọn local và không cấp quyền sở hữu.
5. XiangqiBoardDirector chốt code lúc tạo bàn, tải Resources và giữ model trong phiên xem trận. Code lạ hoặc thiếu model thì dùng mặc định. Hai người có thể thấy hai mẫu khác nhau.

Giữ skin hero và `entries[].cosmeticId` cũ để đọc đội hình và replay đã lưu. Bàn mới dùng endpoint riêng. Team Unity có thể ẩn tab Bộ quân khi thu hẹp giao diện về cosmetic bàn.

Triển khai production theo thứ tự sau.

1. Chạy `HeroChess.Api/Data/Bootstrap/17_board_cosmetics.sql` trên Supabase trước khi deploy BE mới. SQL tạo bảng, mở rộng ledger và seed bốn mẫu. Chạy lại giữ giá và dữ liệu đã có.
2. Deploy backend, kiểm tra `/health/ready`. Thiếu bảng board hoặc cột ledger mới sẽ trả 503 để workflow dừng nhận bản schema chưa đủ.
3. Nối Unity bằng bốn route trên. Bản backend mặc định tắt bootstrap nên deploy riêng sẽ không tự cài SQL production.

## 6. Đồng bộ thời gian thực

REST polling đủ để làm bản demo; nếu cần cập nhật ngay giữa hai máy, dùng WebSocket:

1. `POST /ws-ticket` với bearer token. Response `{ "ticket": "...", "expiresAt": "..." }`.
2. Nối `wss://herochessbe.eastasia.cloudapp.azure.com/ws/v1?ticket=<ticket>` trong vòng 30 giây. Vé dùng **một lần**. Ngắt kết nối thì xin vé mới.
3. Gửi `{"type":"match.subscribe","requestId":"<UUID>","payload":{"matchId":"<UUID>"}}`. Server trả `match.state` với snapshot.
4. Khi nhận `match.changed`, gọi lại `GET /matches/{matchId}/state` và legal actions nếu cần. Có `match.ended`/`match.settled` thì cập nhật kết quả/số dư. Nếu `payload.skillUsed` có dữ liệu, hiện banner từ `side`, `skillName` và các trường `heroName`, `pieceClass` nếu có. Dùng `payload.matchId` và `payload.version` làm khóa chống trùng với `sequenceNo` trong phản hồi command. Replay lưu cùng sự kiện để dựng nhật ký, kể cả khi bỏ lỡ WebSocket.

Có thể gửi `match.command` qua socket với payload `{ "matchId": "<UUID>", "commandId": "<UUID>", "expectedVersion": 0, "action": { ... } }`; server trả `match.command_accepted` hoặc `match.command_rejected`. Dùng REST `POST /commands` thường đơn giản hơn. Sau reconnect hoặc mất event, luôn GET `/state` để đồng bộ lại. WebSocket browser có kiểm tra Origin cùng host; FE chạy khác domain cần reverse proxy/same-origin hoặc cấu hình backend phù hợp. Client Unity native không cần tự gửi Origin header.

## 7. Lịch sử, replay và lỗi

| Method | Route | Kết quả |
|---|---|---|
| `GET` | `/matches?limit=20&cursor=<nextCursor>&mode=<bot\|ranked>&outcome=<win\|loss\|draw>` | `items`, `nextCursor`; mỗi item có `matchId`, `status`, `side`, `result`, `endReason`, `outcome`, `opponentDisplayName`, `eloBefore`/`eloAfter`/`eloDelta`, `ratingChange`, `bonus`, `durationSeconds`, `replayAvailable`, `sides` (hồ sơ người chơi của cả hai phe). |
| `GET` | `/matches/{matchId}` | Chi tiết trận: `sides[]` (mỗi bên có `side`, `displayName`, `outcome`, `eloBefore`/`eloAfter`, `coinReward`, `lineup`, `player`), `replayAvailable`, `actionCount`. Chỉ participant được xem. |
| `GET` | `/matches/{matchId}/lineup` | Lineup **đã đóng băng** lúc trận: `sides[]` với `entries[]` (hero, class, SP, phe, ô xuất phát) và `skills[]`. Bên đối thủ chỉ hiện khi trận đã bắt đầu. |
| `GET` | `/matches/{matchId}/replay?afterSequence=-1&limit=50` | `entries`, `nextAfterSequence`; mỗi entry có `sequenceNo`, `kind`, `actorSide`, `resolvedEvents`, `stateAfter`, `committedAt`. |
| `GET` | `/ranking?limit=50&cursor=<nextCursor>` | Bảng xếp hạng công khai: `items[]` (`rank`, `playerId`, `displayName`, `rating`, `gamesPlayed`, `wins`, `draws`, `losses`), `nextCursor`, `total`. |
| `GET` | `/ranking/me` | Hạng và rating của người chơi đang đăng nhập: `rank` (null nếu chưa có hồ sơ rating), `rating`, `gamesPlayed`, `wins`, `draws`, `losses`, `total`. |

Xếp hạng sắp theo `rating` (Elo) giảm dần; khi bằng nhau thì xếp theo `playerId` tăng dần — quy tắc tie-break duy nhất, `rank` là hạng tuyệt đối. Bảng xếp hạng **không** trả email hay thông tin nhạy cảm; chỉ `displayName`, rating và thống kê. Lịch sử trận chỉ trả trận của chính người gọi (`/me`); người khác gọi `/matches/{matchId}` hoặc `/lineup` nhận `404 MATCH_NOT_FOUND`.

`outcome` là `win` / `loss` / `draw` tính theo phe của người xem; trận chưa có `result` thì `outcome` là `null`, không suy đoán. `result` là `red_win` / `black_win` / `draw`; `endReason` là `checkmate` / `resign` / `timeout` / `stalemate` / `draw` / `agreement`. `ratingChange` là điểm Elo người gọi API được/mất (0 = hòa hoặc không đổi), `bonus` là điểm thưởng thêm của trận (0 nếu không có). `replayAvailable` cho biết trận đã có action lưu để xem replay; mỗi entry replay có `committedAt` để tính thời gian suy nghĩ.

Replay bắt đầu bằng action `start` ở `sequenceNo = 0`: đó là bàn cờ ban đầu **chưa ai đi**. Các nước tiếp theo là sequence 1..N. Phân trang tiếp bằng `afterSequence=nextAfterSequence`. `resolvedEvents` là sự kiện máy đọc như `piece.moved`, `turn.started`, `shield.applied`; UI có thể chuyển thành câu tiếng Việt. `stateAfter` là snapshot đã lưu sau action, không cần tính lại luật trong FE.

Lỗi nghiệp vụ thường có dạng:

```json
{ "code": "CELL_OCCUPIED", "message": "Cannot place Rào on a cell occupied by a piece.", "requestId": "...", "details": null }
```

`400`: body/action sai; `401`: chưa đăng nhập/token hết hạn; `403`: sai quyền; `404`: không có tài nguyên hoặc không phải người trong trận; `409`: version/deadline/trạng thái đã đổi; `422`: nước/skill không hợp lệ; `500`: lỗi backend/DB, nên hiện thông báo và cho thử lại, không khóa bàn cờ vĩnh viễn. Nếu nhiều route cùng trả 500, kiểm tra `/health/ready` và log backend. Đừng gửi liên tục request 500 mỗi frame; polling nên giãn nhịp và báo lỗi rõ ràng.

## 8. Cấu hình Unity hiện tại

Trong `HeroChessApi.cs`, đặt `BaseUrl` thành origin Azure ở đầu tài liệu. Client tự nối prefix `/api/v1`. Nếu scene có ô Endpoint thì điền origin vào ô đó. Unity có thể dùng giá trị đã lưu trong `PlayerPrefs` với key `HeroChess.ApiUrl`, nên kiểm tra ô Endpoint khi máy từng kết nối một backend khác.

Nguồn chuẩn để đối chiếu khi backend thay đổi: [DTO](../HeroChess.Contracts/ApiContracts.cs), [controller](../HeroChess.Api/Controllers/MatchesController.cs), [pipeline command](../HeroChess.Api/Services/MatchCommandService.cs), [WebSocket](../HeroChess.Api/Services/WebSocketEndpoint.cs). Tài liệu này mô tả source, không xác nhận Render đã deploy đúng commit mới nhất; nếu behavior khác, kiểm tra commit đang chạy trên Render.
