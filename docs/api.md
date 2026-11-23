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
| `GET` | `/me` | `userId`, `displayName`, `role`, `status`, `playerId`, `coinBalance`, `elo`, `avatarName`, `frameName`, `avatarUrl`, `description`. |
| `PATCH` | `/me` | Sửa `displayName`, `avatarName`, `frameName`, `description`; trả lại hồ sơ đã cập nhật. `description` tối đa 300 ký tự, chuỗi rỗng để xóa, null/bỏ qua để giữ nguyên. |
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
- Đợt chuẩn hóa SP: Sĩ có trait 2; Tượng cơ bản 2, Tượng linh hoạt 3, Tượng mạnh 4; Lý Thường Kiệt Pháo 7. Budget vẫn 50. Luôn đọc giá thực tế từ catalog. Đội hình đã lưu có thể phát sinh `SP_BUDGET_EXCEEDED`; giữ đội hình để sửa, không tự thay quân. Hai biến thể cùng nhân vật không được chọn chung.
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
{ "type": "undo" }
{ "type": "resign" }
```

Với `team_skill`, chọn `slot` theo `state.skillStates.<phe>.slotNo`, rồi dựng `target` theo `implementationKey`. Thành và Rào dùng `position`, còn Khiên dùng `pieceId` của quân mình. `undo` dành cho trận bot và có thể hoàn tác `hero_active`.

### Undo trong trận với bot

Nút **Hoàn tác** gửi `POST /api/v1/matches/{matchId}/commands` với command envelope hiện có:

```json
{
  "commandId": "<UUID mới>",
  "expectedVersion": 12,
  "action": { "type": "undo" }
}
```

Không gửi `targetSequence` cho nút Undo thông thường. BE tự tìm hành động gần nhất của người chơi trên nhánh hiện hành rồi đưa bàn về **trước hành động đó**. Nếu bot đã trả nước thì cả nước bot sau đó cũng được hoàn tác. Có thể bấm trong lúc chờ bot; nếu bot vừa đi trước khi lệnh đến server thì xử lý `409 STALE_STATE` bên dưới.

`targetSequence` là tùy chọn nâng cao để về một snapshot cụ thể, không phải số lượt lùi. Ví dụ `targetSequence: 0` nghĩa là **về đầu trận**, không phải hoàn tác một nước. Mục tiêu phải tồn tại, nhỏ hơn version hiện tại và không nằm giữa giai đoạn xem Lam Sơn.

**Điều kiện và giao diện:**

- Chỉ hiện nút cho `mode == "bot"` và `status == "active"`; không dùng trong Ranked/Custom PvP hoặc sau khi trận đã kết thúc, kể cả sau đầu hàng/chiếu bí.
- Khóa khi `state.forecastReview` còn tồn tại, khi đang gửi command khác hoặc đang xem replay. Lam Sơn phải bấm kết thúc review/đợi timeout trước khi Undo.
- Chưa có hành động của mình thì không có gì để hoàn tác. Có thể khóa nút sau lỗi `NO_UNDO_AVAILABLE` ở version đó và mở lại khi bàn có version mới.
- Không tự đảo tọa độ, cộng CD hoặc hồi sinh model bằng suy đoán. Thay toàn bộ bàn và UI theo `snapshot` trả về: quân sống/bị loại, vị trí hai quân khi Lê Lai đổi chỗ, Khiên, khóa chân, buff Tốt, Thành/Rào/Cọc/bẫy, CD/lượt dùng và trạng thái nội tại đều được khôi phục từ dữ liệu trận.
- Sau Undo thành công, lấy lại legal actions nếu tới lượt mình. Đồng hồ dùng `snapshot.deadlineAt` và `serverNow` mới; không giữ deadline trước Undo.

**ACK và đồng bộ:** Undo tạo thêm một bản ghi lịch sử, không xóa các nước cũ. `sequenceNo`/`snapshot.version` tiếp tục tăng, dù `turnIndex` và `countedActions` có thể lùi. Không dùng hai số lượt đó để bỏ snapshot mới. Sự kiện trả về:

```json
{ "type": "match.undone", "targetSequence": 8 }
```

Server gửi `match.changed` cho các kết nối; client gọi lại `/state` theo cơ chế hiện có. Retry vì mất ACK phải dùng lại đúng `commandId` và action, không tạo UUID khác cho cùng lần bấm. ACK duplicate có thể chứa snapshot lịch sử: không ghi đè bàn mới hơn bằng response có version thấp hơn. Sau Undo, xóa phần dự báo Lam Sơn thuộc nhánh đã bị bỏ; endpoint forecast vẫn kiểm tra nhánh ngay cả khi có cache.

| HTTP / mã lỗi | Cách xử lý |
|---|---|
| `409 STALE_STATE` | Tải `/state` mới; chưa coi Undo là thành công. Nếu người chơi vẫn muốn Undo thì gửi command mới theo version mới. |
| `409 TURN_DEADLINE_PASSED` | Tải state mới sau khi server xử lý hết giờ, không tự lùi bàn. |
| `409 MATCH_NOT_ACTIVE` / `MATCH_NOT_STARTED` | Cập nhật trạng thái trận và khóa nút. |
| `422 NO_UNDO_AVAILABLE` | Báo không còn nước của mình để hoàn tác. |
| `422 UNDO_NOT_ALLOWED` | Chế độ hiện tại không hỗ trợ Undo. |
| `422 TURN_ALREADY_COMMITTED` | Lam Sơn còn đang chờ kết thúc review. |
| `422 INVALID_UNDO_TARGET` | Snapshot đích không hợp lệ; nút thông thường không nên tự chọn target. |
| `409 COMMAND_ID_CONFLICT` | Không tái sử dụng commandId cho nội dung khác. |

Kiểm tra Unity: Undo trước/sau khi bot trả nước; bấm lặp/mất ACK; dùng skill rồi Undo; ăn quân hoặc trúng Cọc/bẫy rồi Undo; CD/hiệu lực không giảm thêm khi dựng lại bàn; Lam Sơn bị khóa trong review và hoàn tác cả lần dùng sau khi đã chốt. Xem lại replay/reconnect phải giữ đúng nhánh và version mới.

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
| Trần | `bach_dang_tran_phap` | `{"positions":[{"x":2,"y":4},{"x":4,"y":4},{"x":6,"y":5}]}` | 7 |
| Hậu Lê | `lam_son_quan_luoc` | `{}` | 15 |
| Tây Sơn | `hich_xuat_quan` | `{}` | 11 |

Cả ba lệnh tiêu một lượt. Riêng Lam Sơn giữ lượt ở giai đoạn xem dự báo cho đến khi xác nhận hoặc hết giờ, nhưng không được thực hiện thêm hành động. `state.riverSnares` chỉ có bẫy của phe người gọi, mỗi bẫy có `owner`, `position` và `expiresAt` theo `turnIndex`. Bẫy sống 6 lượt và mất sau lần khóa đầu tiên. Hiệu ứng trên quân xuất hiện trong `pieces[].effects` với mã `bach_dang_rooted` hoặc `hich_xuat_quan` và `remainingTurns`.

Bẫy địch ẩn không chặn đặt Thành/Rào/Cọc/bẫy phe mình, không làm thiếu ô trong danh sách gợi ý. Các điều kiện công khai như quân, vật cản, nửa bàn vẫn áp dụng. Hai phe có thể đặt bẫy trùng ô; mỗi phe chỉ thấy bẫy của mình. Bẫy tạm ngưng kích hoạt dưới quân hoặc bất kỳ công trình nào, kể cả Cọc công khai.

Unity lấy `slot` từ `state.skillStates[ownSide]` và gửi `action.type = "team_skill"`. Với Bạch Đằng Trận Pháp, xác nhận khi đã chọn đủ ba ô sông trống khác nhau. Quân đi qua bẫy địch còn hiệu lực đầu tiên dừng ngay tại ô bẫy rồi bị khóa 4 lượt, không ăn quân hay phá công trình phía sau. Mã xét chân trước ô đáp và có thể dừng tại chân; áp dụng cả Pháo thường và Lý Thường Kiệt. Ô đang có quân hoặc công trình chặn thì bẫy tạm không kích hoạt, không bị tiêu và vẫn có thể kích hoạt khi ô trống trước lúc hết hạn. Khiên không miễn khóa chân. Bẫy vẫn ẩn nên legal preview giữ đích dự định; khi xử lý xong, `pieces[].position` và sự kiện `piece.moved.to` trả đích thực tế. Không tự xóa quân tại đích request. Nếu dừng sớm làm Tướng mình còn bị chiếu hoặc lộ mặt hai Tướng thì thua ngay với `endReason: "river_snare"`. Với Hịch Xuất Quân, tô đích của Tốt theo `/legal-actions`. Nếu server cho đi hai ô thì gửi như một nước đi thường. `riverSnares` và vật cản Cọc `thd_tuong_coc` là hai dữ liệu riêng khi vẽ bàn.

Sau khi nhận ACK dùng Lam Sơn Quân Lược, người kích hoạt gọi `GET /api/v1/matches/{matchId}/forecast?version=<sequenceNo của ACK>`. Phản hồi có `version`, `prediction.variation` (tối đa năm hành động địch → mình → địch → mình → địch, gồm ba hành động địch và hai hành động mình) và `prediction.threats` (tối đa hai phương án ở lượt địch đầu tiên). Mỗi mục có `side`, `action`, cùng `from`, `pieceClass`, `pieceName` hoặc `skillName` khi có; trường không áp dụng có thể null. Client duyệt toàn bộ mảng, không giới hạn ở ba bước. Ngân sách tìm kiếm vẫn là 64 lần thử mô phỏng và ngưỡng mềm 50 ms; chuỗi có thể ngắn hơn năm bước khi hết ngân sách hoặc trận kết thúc. Kết quả là gợi ý, không bảo đảm đối thủ sẽ đi theo chuỗi đó.

ACK kích hoạt có `state.forecastReview: {"owner":"red","activationSequence":42}`. `sideToMove`, `turnIndex`, `countedActions` và `deadlineAt` chưa đổi; cooldown và lượt dùng đã bị trừ. Đọc trường này từ `/state` để khôi phục sau reconnect. Khi còn review, legal/hero-actions trả mảng rỗng và các hành động khác bị từ chối `422 TURN_ALREADY_COMMITTED`, trừ đầu hàng. `prediction.initialBoard` là bàn mô phỏng đầu lượt địch; mỗi mục variation/threat có `boardAfter` tương ứng. Các bàn này đã lọc dữ liệu bí mật, chỉ dùng cho cửa sổ dự báo riêng.

Nút **Đi** gửi command bình thường với `expectedVersion` hiện tại và `action: {"type":"end_turn"}`. Không gửi nước dự đoán. ACK chốt lượt tăng version thêm một, tăng `countedActions`/`turnIndex` đúng một, xóa `forecastReview` và chuyển phe. Sự kiện `forecast.completed` có `activationSequence`, `automatic`. Retry cùng commandId; request khác dùng version cũ trả 409. Không có review trả `422 NO_FORECAST_REVIEW`. Server tự chốt khi deadline gốc hết, không phạt AFK cho thời gian xem skill. Đóng cửa sổ không hủy skill hay dừng đồng hồ. Bot dùng Lam Sơn tự chốt ngay. Undo chỉ cho sau khi review hoàn tất và đưa về trước lần dùng skill.

Triển khai: chạy `HeroChess.Api/Data/Bootstrap/22_forecast_review.sql` trên DB hiện hữu **trước khi deploy API**, để bảng `match_action` chấp nhận loại `end_turn`. Bootstrap tự chạy file này nếu được bật; Azure đang tắt bootstrap thì chạy thủ công. Cập nhật client cùng đợt; client cũ không có nút Đi sẽ phải đợi hết giờ.

`version` là mốc kích hoạt, không phải version bàn hiện tại. Kích hoạt ở 42 thì phản hồi vẫn là 42 dù bàn đã lên 43 hoặc trận đã kết thúc. Unity đối chiếu với mốc yêu cầu, giữ kết quả để xem lại và ghi rõ khi bàn đã thay đổi; không áp chuỗi dự đoán lên bàn thật. GET không tiêu thêm lượt hoặc cooldown. Cache riêng theo người chơi/trận/mốc, tối đa 128 mục, TTL hai phút; hết cache vẫn có thể tính lại từ snapshot nếu lần kích hoạt còn trên nhánh hiện hành. Mỗi lần đọc, kể cả cache hit, đều kiểm tra quyền và nhánh undo. Không trả bẫy ẩn của địch hoặc dùng chúng để tính dự đoán.

Thiếu/sai `version` trả 400. Sai người, sai action hoặc lần kích hoạt đã bị undo trả 404 (`MATCH_NOT_FOUND` / `FORECAST_NOT_FOUND`). Snapshot không phân tích được trả 422 `INVALID_FORECAST_SNAPSHOT`; lịch sử undo lỗi hoặc vượt 256 mục cần xác minh trả 422 `INVALID_FORECAST_HISTORY` / `FORECAST_HISTORY_TOO_LONG`. `429 FORECAST_BUSY` là hàng chờ tính đã đầy; `429 FORECAST_RATE_LIMITED` là quá 30 yêu cầu/phút/người. Cho tải lại GET có giãn nhịp, không gửi lại command kích hoạt. Endpoint forecast không còn trả `409 STALE_STATE` vì một nước đi mới; quy tắc 409 của endpoint command vẫn giữ nguyên.

Forecast dùng một suất tính, hàng chờ bốn; yêu cầu trùng được dùng chung kết quả. Mỗi lần phân tích có ngân sách mềm 50 ms, tối đa 64 lần thử mô phỏng, sáu ứng viên hợp lệ mỗi bước và đánh giá ăn lại ngắn hạn. Một lần sinh nước có thể vượt ngân sách mềm; đây không phải cam kết thời gian phản hồi trên Azure. Metrics trong meter `HeroChess.Bot`: `herochess.forecast.requests` (nhãn `outcome=computed|cache_hit`), `herochess.forecast.queue_wait` và `herochess.forecast.search` (giây).

Hero Trait và ba faction skill mới mặc định tắt cho trận mới. Sau khi kiểm tra bản deploy, bật `MatchRuntime__HeroTraitsEnabled=true` hoặc `MatchRuntime__NewFactionSkillsEnabled=true` trong env của API. Faction skill còn cần các dòng catalog từ SQL `16_faction_skills.sql`. Khi cần quay về image cũ, tắt hai cờ trên bản hiện tại rồi chờ các trận đã chốt luật mới kết thúc trước khi thay image.

Trong trận có `state.publicRiverStakes`, đọc trait trên `state.pieces`, hồi chiêu từ `traitState` và Cọc Bạch Đằng Giang từ `state.obstacles` với `kind: "thd_tuong_coc"`. Phe đặt là `state.obstacleMetadata[obstacleId].placer`. Khi `thdTuongCocStakes[pieceId]` còn tồn tại, nút đặt Cọc của quân đó cần khóa kể cả hồi chiêu bằng 0. Cả hai bên thấy Cọc. Nước dính Cọc có thể hợp lệ và tạo sự kiện `river_stake.hit` với `pieceId`, `position`, `side` của Cọc và `victimSide` của quân bị loại. Kết thúc do Cọc dùng `endReason: "river_stake"`.

Command hợp lệ trả `MatchCommandResultDto`: `matchId`, `commandId`, `sequenceNo`, `duplicate`, `resolvedEvents`, `snapshot`. Render bàn cờ từ `snapshot` mới; bot có thể đi ngay sau đó nên phải nhận cập nhật tiếp. Retry **cùng một command** bằng đúng `commandId` và action cũ để tránh ghi hai lần. Khi `409 STALE_STATE`, lấy `/state` mới rồi cho người chơi chọn lại nước với `commandId` mới. Deadline dùng `deadlineAt` so với `serverNow`; server tự xử lý hết giờ, FE chỉ hiển thị đếm ngược.

### Trait Sĩ

| Hero | Tên trait | `implementationKey` | `kind` | SP | CD |
|---|---|---|---|---:|---:|
| Lê Lai — Sĩ | Lê Lai cứu chúa | `le_lai.swap_general` | `active` | 2 | 7 |
| Trần Thủ Độ — Sĩ | Trấn Quốc Hộ Quân | `tran_thu_do.general_shield` | `active` | 2 | 5 |
| Nguyễn Nhạc — Sĩ | Thái Đức Linh Bộ | `advisor.king_step` | `special_move` | 2 | — |
| Nguyễn Lữ — Sĩ | Đông Định Linh Bộ | `advisor.king_step` | `special_move` | 2 | — |

Hai active chỉ target Tướng đồng minh còn sống. Gửi trong command envelope hiện tại, giữ `commandId` khi retry:

```json
{
  "commandId": "<uuid>",
  "expectedVersion": 12,
  "action": {
    "type": "hero_active",
    "pieceId": "<pieceId Sĩ trong trận>",
    "skillCode": "le_lai.swap_general",
    "target": { "pieceId": "<pieceId Tướng đồng minh trong trận>" }
  }
}
```

Lê Lai đổi hai vị trí trong cung nhà, không giới hạn khoảng cách hoặc yêu cầu đường trống. Lê Lai và Tướng phải còn sống, ở trong cung, không bị khóa chân; đổi xong Tướng phải an toàn theo luật trận. Thành công tiêu một lượt và phát `hero_skill.activated` cùng hai `piece.moved`. Luôn cập nhật cả hai quân từ snapshot; không xử lý như ăn quân.

Trần Thủ Độ dùng shape trên với `skillCode="tran_thu_do.general_shield"`. Tướng được bảo vệ có thể đã rời cung. Trần Thủ Độ bị khóa chân vẫn dùng được vì đây không phải di chuyển. Khiên dùng chung `code="shield"`, có `sourcePieceId`; không được cộng dồn hoặc ghi đè Khiên còn hiệu lực của Command Skill. Nguồn bị ăn không kéo dài hoặc xóa sớm timer.

Với `state.skillTimingVersion=2`, Trấn Quốc Hộ Quân tồn tại **2 lượt chung tiếp theo, không tính lượt kích hoạt**: ACK còn `remainingTurns=2`, bảo vệ trong lượt địch và lượt mình kế tiếp, rồi hết trước lượt địch sau đó. ACK CD Trần Thủ Độ còn 5, Lê Lai còn 7. Đọc `traitParameters` của quân và `traitState.heroCooldownRemaining` trong trận, không tự bù số. Command Khiên tồn tại 4 lượt, CD 13; đây là hai kỹ năng khác nhau. Snapshot cũ thiếu version hoặc version 1 giữ cách tính cũ.

Hai Sĩ Tây Sơn đi một ô theo tám hướng trong cung, dùng `/legal-actions` và action `move`; không có nút kích hoạt hoặc CD. Bản GENERAL của Nguyễn Nhạc/Nguyễn Lữ không nhận trait này. `classCode` ở catalog là `GENERAL`/`ADVISOR`, còn enum `class` trong snapshot là `general`/`advisor`.

Sai JSON target trả 400 `INVALID_ACTION`; target sai phe/class/vị trí trả 422 `INVALID_TARGET`; đổi chỗ khi khóa chân trả `PIECE_ROOTED`; skill chưa sẵn sàng trả `SKILL_ON_COOLDOWN`; Tướng đã có Khiên trả `ALREADY_SHIELDED`; cấu hình sai trả `INVALID_HERO_SKILL_CONFIG`. Command version cũ vẫn 409. Đọc lỗi và giữ snapshot cũ khi bị từ chối; không tiêu lượt ở client trước ACK.

Phát hành trait ban đầu: deploy BE hỗ trợ key và tích hợp Unity trước, rồi mới chạy thủ công `19_hero_sp_and_advisor_traits.sql` trên DB đích. Deploy code không tự áp patch 19. Metadata/SP trong trận cũ giữ nguyên theo snapshot. Description Thành/Rào/Khiên không nhúng CD; UI lấy số từ `teamSkills[].cooldownTurns`, số đang hồi lấy từ `skillStates`. Sau khi đã áp patch timer 23, không chạy lại 19 để tránh đưa tham số về mức cũ.

### Cấu hình timer bằng DB — skillTimingVersion 2

Các bảng trên ghi giá trị sau patch `23_skill_timers.sql`. Khi chưa áp patch, catalog và trận V1 vẫn dùng cấu hình trước đó. Client luôn đọc số thực tế, không hardcode theo bảng tài liệu.

| Skill | CD | Thời hạn (`parameters`) |
|---|---:|---|
| Thành / Rào / Khiên | 13 / 9 / 13 | `durationTurns`: 6 / 4 / 4 |
| Bạch Đằng Trận Pháp | 7 | `durationTurns:6`, `rootDurationTurns:4` |
| Lam Sơn Quân Lược | 15 | Không có duration; review giữ deadline lượt gốc |
| Hịch Xuất Quân | 11 | `durationTurns:6` — ba lượt mình hưởng buff |
| Quang Trung / Lê Lai | 7 / 7 | Tức thời |
| Cọc THĐ / Khiên Trần Thủ Độ | 5 / 5 | `durationTurns`: 3 / 2 |

Team CD lưu ở `team_skill.cooldown_turns`; thời hạn lưu JSONB `team_skill.parameters`. Hero CD/thời hạn lưu JSONB `hero_trait.parameters.cooldownTurns` / `durationTurns`. Giá trị phải là số nguyên dương; giữ các khóa JSON khác khi chỉnh. Không đổi gameplay bằng description.

Catalog cung cấp `teamSkills[].cooldownTurns`, `parameters`; Hero có `trait.parameters`. Trong trận đọc cấu hình **frozen** từ `state.skillStates[side][].cooldownTurns` / `parameters` và `pieces[].traitParameters`. Đọc số còn lại từ `cooldownRemaining`, trait state, effect/obstacle; không tự trừ một. Bẫy đã đặt giữ riêng thời hạn khóa chân của nó. DB thay đổi chỉ áp dụng trận mới.

`state.skillTimingVersion=2`: timer mới giữ nguyên sau ACK; giảm sau mỗi lượt chung tiếp theo. Timer cũ vẫn giảm, cả hai phe dùng cùng cách tính. Lam Sơn cast chưa kết thúc lượt; `end_turn` hoặc timeout chốt đúng một lượt, không giảm CD vừa tạo của Lam Sơn. Version 1 hoặc thiếu field giữ luật cũ khi reconnect/replay/undo. Dùng `turnIndex`, không dùng `version` để đếm thời gian.

Rollout: áp SQL 22 theo hướng dẫn Lam Sơn trước API; deploy BE và client hỗ trợ V1/V2; sau đó mới chạy thủ công SQL 23. SQL 23 cập nhật catalog và `ruleset.config.skillTimingVersion` trong cùng transaction, kiểm tra drift và không ghi đè giá trị tự cân bằng khi chạy lại. Existing DB không tự chạy patch 23 lúc startup. Trận đang đánh giữ snapshot cũ; trận đang chọn đội hình có thể nhận `CONTENT_VERSION_CHANGED` và cần tạo/chọn trận mới.

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

Giữ skin hero và `entries[].cosmeticId` cũ để đọc đội hình và replay đã lưu. Bàn cờ và bộ quân dùng endpoint riêng, không truyền chúng trong `entries[].cosmeticId`.

Triển khai production theo thứ tự sau.

1. Chạy `HeroChess.Api/Data/Bootstrap/17_board_cosmetics.sql` trên Supabase trước khi deploy BE mới. SQL tạo bảng, mở rộng ledger và seed bốn mẫu. Chạy lại giữ giá và dữ liệu đã có.
2. Deploy backend, kiểm tra `/health/ready`. Thiếu bảng board hoặc cột ledger mới sẽ trả 503 để workflow dừng nhận bản schema chưa đủ.
3. Nối Unity bằng bốn route trên. Bản backend mặc định tắt bootstrap nên deploy riêng sẽ không tự cài SQL production.

### Bộ quân cờ

`GET /piece-cosmetics` trả `items, selectedCode`, item có cùng shape với bàn cờ: `id, code, name, coinPrice, assetKey, previewKey, isOwned`.

| Method và route | Request và response |
|---|---|
| POST /shop/piece-cosmetics/{id}/purchase | Header `Idempotency-Key: <UUID>`. Trả `pieceCosmeticId, acquired, coinBalance, charged` |
| PUT /me/piece-cosmetic | Body `{"code":"son-son"}`. Trả item đã trang bị |

Bốn code là `mac-dinh`, `son-mai`, `son-son`, `su-thanh`; mặc định được sở hữu sẵn, ba mẫu còn lại khởi tạo 200 vàng/mẫu. Model key tương ứng là `Board/XiangqiPieces`, `Board/XiangqiPiecesSkinSonMai`, `Board/XiangqiPiecesSkinSonSon`, `Board/XiangqiPiecesSkinSuThanh`; preview key là `UI/Cosmetics/PiecesMacDinh`, `PiecesSonMai`, `PiecesSonSon`, `PiecesSuThanh` dưới cùng thư mục. Đây là ngoại hình đế/quân cờ, không thay model hero hoặc luật đi.

Chỉ Player truy cập được. Mua và trang bị là hai thao tác riêng. Lỗi gồm `PIECE_NOT_FOUND`, `PIECE_DISABLED`, `PIECE_ALREADY_OWNED`, `PIECE_NOT_OWNED`, `INVALID_PIECE_CODE` cùng lỗi thiếu tiền/key của shop. Key mua phải giữ nguyên khi retry; không dùng lại giữa hero, bàn cờ và bộ quân, kể cả món có giá 0.

Unity dùng `isOwned` và `selectedCode` từ BE; không nhận `PlayerPrefs` cũ làm bằng chứng mua. Đăng xuất/đổi tài khoản xóa trạng thái và tải lại. Phản hồi của phiên cũ bị bỏ qua. Mỗi người nhìn bộ quân theo lựa chọn của tài khoản mình; thiếu model hoặc lựa chọn bị tắt thì dùng mặc định. Không tự trang bị sau khi mua.

### Starter, giá hero và rollout danh mục

Sau patch 20, tài khoản mới nhận 14 hero: Trần Thánh Tông, Nguyễn Nhạc (Tướng); Mạc Đĩnh Chi, Nguyễn Trãi, Nguyễn Bỉnh Khiêm (Sĩ); Lê Khôi, Lê Văn Hưng (Tượng); Trần Khánh Dư, Trần Quang Diệu (Xe); Trần Quốc Toản, Lê Sát (Mã); Nguyễn Xí, Nguyễn Văn Tuyết (Pháo); Lính nhà Trần (Tốt). Một hero Tốt dùng được ở năm slot. Đội cơ bản 45 SP; mỗi phe có tối đa bốn hero ngoài Tốt trong bộ starter nên cần mua thêm để đạt ngưỡng năm.

`Onboarding:StartingCoins` mặc định 1000; `DevelopmentStartingCoins` chỉ ghi đè trong Development. Tiền và hero được cấp trong transaction tạo Player, có ledger `starter`; Admin không được cấp và tài khoản cũ không được cộng lại. `hero.is_starter` là nguồn đọc của onboarding. Bảng `starter_hero_config` legacy nếu tồn tại được patch 20 đồng bộ; lịch sử cũ được giữ nguyên.

Giá ngoài starter theo catalog: quân thường 100, trait vừa 200, trait mạnh 300. Giá/SP phải đọc từ API, không tính giá vàng từ SP trong client. Nguyễn Văn Lộc hệ Pháo có model key `nguyenvanloc`, giá 100 vàng, 6 SP và được mở bán; không thuộc starter. Đinh Liệt, Lưu Nhân Chú, Trần Khát Chân và Nguyễn Trung Ngạn được loại khỏi catalog; đội hình có tham chiếu được thay quân cơ bản cùng class, ưu tiên cùng phe, không tăng SP và không lặp nhân vật. Cấp bù khi cần, tăng revision đội hình bị đổi; snapshot trận cũ không bị viết lại.

Rollout production:

1. Backup hero/config starter/ownership/đội hình bị ảnh hưởng. Chạy bản cập nhật `17_board_cosmetics.sql` rồi `21_piece_cosmetics.sql` trước khi deploy BE; đây là schema cộng thêm tương thích với API cũ. Giá và quyền sở hữu bàn cờ đang có được giữ nguyên.
2. Deploy API tương thích. `/health/ready` kiểm tra đủ ba bảng bộ quân và cột ledger mới; thiếu schema trả 503 để deployment rollback.
3. Khi không còn trận/lượt chọn đang dở, chạy **toàn bộ** `20_hero_catalog_and_starters.sql`. Script một transaction, không chạy lại mỗi startup. Tham chiếu mua bằng tiền/quà/cosmetic hoặc việc thay quân làm mất điều kiện phe sẽ dừng và rollback để xử lý rõ ràng.
4. Kiểm tra 43 hero còn lại, 14 starter, 28 hero đang bán, 42 model key và bốn bộ quân. Unity mới tải lại catalog/ownership; người chơi sửa các đội hình cũ khác đang vượt SP hoặc chứa dev-slot đã tắt.

BE chỉ áp patch 20 tự động khi chính nó vừa tạo schema mới; DB đã có dữ liệu phải rollout thủ công. Bộ SQL cài mới tổng đã đồng bộ cùng danh mục. Không chạy installer cài mới vào production và không dùng seed để reset ví/quyền sở hữu.

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
{ "code": "CELL_OCCUPIED", "message": "Cannot place Rào on an occupied cell.", "requestId": "...", "details": null }
```

Ô đã có quân, Cọc, Thành, Rào hoặc Bẫy thì không skill nào đặt được lên đó: server trả `422 CELL_OCCUPIED` cho cả hai phe, không ngoại lệ Cọc. Client tô/nhận ô theo cùng luật nhưng server vẫn là nơi phán quyết — một client khác gửi lệnh đặt chồng vẫn bị từ chối.

`400`: body/action sai; `401`: chưa đăng nhập/token hết hạn; `403`: sai quyền; `404`: không có tài nguyên hoặc không phải người trong trận; `409`: version/deadline/trạng thái đã đổi; `422`: nước/skill không hợp lệ; `500`: lỗi backend/DB, nên hiện thông báo và cho thử lại, không khóa bàn cờ vĩnh viễn. Nếu nhiều route cùng trả 500, kiểm tra `/health/ready` và log backend. Đừng gửi liên tục request 500 mỗi frame; polling nên giãn nhịp và báo lỗi rõ ràng.

## 8. Cấu hình Unity hiện tại

Trong `HeroChessApi.cs`, đặt `BaseUrl` thành origin Azure ở đầu tài liệu. Client tự nối prefix `/api/v1`. Nếu scene có ô Endpoint thì điền origin vào ô đó. Unity có thể dùng giá trị đã lưu trong `PlayerPrefs` với key `HeroChess.ApiUrl`, nên kiểm tra ô Endpoint khi máy từng kết nối một backend khác.

Nguồn chuẩn để đối chiếu khi backend thay đổi: [DTO](../HeroChess.Contracts/ApiContracts.cs), [controller](../HeroChess.Api/Controllers/MatchesController.cs), [pipeline command](../HeroChess.Api/Services/MatchCommandService.cs), [WebSocket](../HeroChess.Api/Services/WebSocketEndpoint.cs). Tài liệu này mô tả source, không xác nhận Render đã deploy đúng commit mới nhất; nếu behavior khác, kiểm tra commit đang chạy trên Render.
