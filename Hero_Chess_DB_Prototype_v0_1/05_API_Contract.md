# HERO CHESS — API/DTO v0.1 để chia việc

Đây là hợp đồng đề xuất cho backend và Unity/web test, không phải các endpoint đã chạy. UUID là string; coin bigint được truyền dưới dạng chuỗi thập phân để JS không mất độ chính xác; SP, version, Elo là integer. Timestamp dùng ISO 8601 UTC. Các enum là chuỗi đúng chữ hoa/thường như ví dụ.

## 1. Quy ước chung

- Base path `/api/v1`; HTTPS. WebSocket endpoint `/ws/v1` qua WSS.
- Các endpoint dữ liệu dùng identity đã xác thực; backend suy ra playerId, không tin playerId client tự gửi.
- Chọn đúng một hệ auth (Supabase Auth hoặc ASP.NET Identity) trước khi thực hiện login. `auth_identity` map subject được server xác minh sang player. Bộ DB không tự tạo tài khoản trong hệ auth.
- Token/handshake phải được server xác minh cho cả REST và WebSocket. Native Unity có thể dùng header; web test cần cơ chế cookie HttpOnly cùng origin hoặc vé WebSocket dùng một lần do API cấp. Không đặt token dài hạn trên query string hoặc bỏ qua auth cho socket.
- `requestId` để đối chiếu; `commandId` cho hành động trận; `Idempotency-Key` UUID cho mua hàng. Reconnect không đổi commandId của lệnh đang chờ phản hồi.
- Schema JSON hiển thị là hợp đồng v0.1; backend dùng DTO có kiểu và kiểm tra trường, không trả ORM entity trực tiếp.

Lỗi chung:
```json
{"code":"STALE_STATE","message":"Trạng thái đã thay đổi.","requestId":"req-01","details":{"currentVersion":12}}
```

HTTP: 400 sai định dạng; 401 thiếu phiên; 403 thiếu quyền; 404 không tìm thấy/không được xem; 409 xung đột phiên bản hoặc đã sở hữu; 422 vi phạm luật/lineup. Không chuyển nguyên SQL error ra UI.

## 2. API tối thiểu

| Method | Endpoint | Input chính | Response chính |
|---|---|---|---|
| GET | `/me` | Token | playerId, displayName, isGuest, coinBalance, elo |
| GET | `/catalog` | — | ruleset, classes, slots, heroes, traits, factions, teamSkills; chỉ nội dung được phép hiển thị |
| GET | `/me/heroes` | — | Danh sách heroId đã sở hữu |
| GET | `/lineups` | — | Đội hình của mình, SP hiện tại và validation errors |
| POST | `/lineups` | SaveLineupRequest | 201 LineupDto |
| PUT | `/lineups/{id}` | SaveLineupRequest + expectedRevision | LineupDto hoặc 409 |
| DELETE | `/lineups/{id}` | expectedRevision | 204; lịch sử trận giữ snapshot |
| POST | `/shop/heroes/{heroId}/purchase` | Idempotency-Key | heroId, acquired, coinBalance; chưa đủ coin thì từ chối |
| POST | `/matchmaking/tickets` | mode=ranked hoặc bot | ticketId, status; Ranked không ghép bot |
| DELETE | `/matchmaking/tickets/{id}` | — | 204 nếu còn được hủy theo trạng thái |
| PUT | `/matches/{id}/selection` | lineupId, expectedRevision | lựa chọn hiện tại + skill công khai; chưa lộ hero đối phương |
| POST | `/matches/{id}/confirm` | — | trạng thái khóa; gọi lại không khóa lần hai |
| GET | `/matches/{id}/state` | — | MatchStateDto; chỉ người được phép xem |
| GET | `/matches` | cursor, limit | Lịch sử trận của mình |
| GET | `/matches/{id}/replay` | afterSequence, limit | snapshot/event tăng dần sequence; chỉ khi được phép xem |

Matchmaking ticket nằm trong RAM của một backend prototype, chưa cần bảng hàng chờ. Không thêm friend invite hay ban/pick. Match ID luôn do server cấp.

Login/register/guest và API quản trị không được mô tả chi tiết trong hợp đồng giao gameplay này; phải triển khai ở module auth/admin trước khi public. Thử hai client development có thể gắn hai identity thật vào player fixture, không dùng public endpoint cho client nhận tùy ý playerId.

## 3. DTO lineup

`SaveLineupRequest`: name, rulesetId, entries[16], skills[3]; expectedRevision bắt buộc khi sửa. entries gồm slotNo, heroId, cosmeticId nullable. Không nhận SP do client khai báo. Tọa độ xuất phát lấy từ slot chuẩn, client không tự sắp xếp quân.

```json
{
  "name":"Đội hình A",
  "rulesetId":"10000000-0000-4000-8000-000000000001",
  "expectedRevision":1,
  "entries":[{"slotNo":1,"heroId":"60000000-0000-4000-8000-000000000001","cosmeticId":null}],
  "skills":[{"slotNo":1,"skillId":"70000000-0000-4000-8000-000000000001"}]
}
```

Đoạn trên chỉ minh họa một phần entry/skill; không gửi nó nguyên vẹn để lưu. `06_mock_data.json` có request đầy đủ 16+3. Validation error nên trả theo slot để Unity tô lỗi. Backend kiểm tra lại trước khóa trận; việc này không thêm bước UI.

LineupDto trả id, name, revision, rulesetId, totalSp, budget, entries, skills, isValid, validationErrors. isValid là giá trị tính tại lúc đọc, không phải cột DB đảm bảo không bao giờ lỗi sau cập nhật.

HeroDto tối thiểu: id, code, name, characterId nullable, classCode, setupPoints, trait nullable, factionIds[], assetKey nullable, isOwned, coinPrice(string). TraitDto: code, kind, implementationKey, parameters, description. Phía client sử dụng thư viện C# chung hoặc danh sách legalActions do server cấp để hiển thị chính xác; không suy ra luật chỉ từ description.

## 4. Tọa độ và định danh quân

- Board x=0..8, y=0..9; Red xuất phát y=0 và Tốt tiến +y. Black xoay từ tọa độ Red theo (8-x,9-y), Tốt tiến -y.
- Camera có thể xoay cho người chơi; API vẫn dùng tọa độ server này.
- `heroId` là loại/phiên bản nhân vật. `pieceId` là UUID một quân cụ thể trong trận. Hai bên có thể dùng cùng heroId nhưng không dùng cùng pieceId.
- Bàn cờ đặt quân trên giao điểm. Không truyền tọa độ world-space Unity làm dữ liệu luật.

## 5. WebSocket

Envelope yêu cầu:
```json
{
 "type":"match.command",
 "requestId":"req-02",
 "payload":{
  "matchId":"90000000-0000-4000-8000-000000000001",
  "commandId":"b0000000-0000-4000-8000-000000000001",
  "expectedVersion":0,
  "action":{"type":"move","pieceId":"a0000000-0000-4000-8000-000000000012","to":{"x":0,"y":4}}
 }
}
```

Action có bốn dạng gameplay: move(pieceId,to), hero_active(pieceId,targets), team_skill(skillSlot,targets), resign(). Bot mode thêm undo(targetSequence). Mỗi target là object có discriminator `kind`: `piece` với pieceId hoặc `point` với x/y; số lượng/mục tiêu hợp lệ do implementation của skill quyết định. Timeout, start và cancel là sự kiện server tạo, không chấp nhận từ client.

| Event server → client | Payload |
|---|---|
| `matchmaking.matched` | matchId, mode |
| `match.selection_changed` | trạng thái xác nhận từng bên, skill được phép lộ; không gửi lineup kín |
| `match.started` | MatchStateDto version 0 |
| `match.command_accepted` | commandId, sequenceNo, resolvedEvents, snapshot |
| `match.command_rejected` | commandId, code, currentVersion, details |
| `match.state` | MatchStateDto khi subscribe/reconnect |
| `match.ended` | matchId, result, endReason, snapshot; reward/rating có thể còn pending |
| `match.settled` | matchId, coinReward, coinBalance, eloBefore, eloAfter |

Client có thể gửi `match.subscribe` với matchId để nhận snapshot sau xác thực. Không được subscribe trận người khác bằng cách đoán UUID.

Nếu mất phản hồi sau khi gửi command, kết nối lại → nhận state → tra/gửi lại đúng commandId. Server đối chiếu cả nội dung command trước khi trả kết quả cũ. Client không lấy token connection cũ làm định danh vĩnh viễn của player.

Version là số tăng sau mọi event được chấp nhận, kể cả timeout/undo; ban đầu 0. UI bỏ snapshot cũ hơn version đang giữ. command_accepted và snapshot của đối thủ đều phải đưa hai client về cùng version. Bot search nhận bản copy state; kết quả chỉ được dùng nếu version vẫn còn đúng.

## 6. MatchStateDto và replay

`06_mock_data.json` có MatchStateDto đầy đủ. Dạng tối thiểu:

- matchId, mode, status, version, stateSchemaVersion, rulesetCode.
- turn: side, turnIndex, countedActions, deadlineAt, serverNow.
- board: width=9, height=10, pieces[], obstacles[].
- pieces: pieceId, heroId, side, classCode, setupPoints, position nullable, startPosition, status, effects[], traitState.
- players: red/black public display data và skillStates[] (slot, skillId, usesRemaining nullable, cooldownRemaining).
- result nullable; endReason nullable; pendingEffects[].

Piece status: alive, captured, pending_revive. JSON snapshot phải chứa đủ trạng thái cần cho rules engine. Effects có source, target, remaining/expiry theo quy ước nhóm chốt. Không âm thầm coi null của skill fixture là số lượt vô hạn.

Snapshot trong match_action bao gồm cả turn và match result, không chỉ board. ServerNow trong response là thời điểm gửi; deadline mới là mốc gameplay. UI hiển thị đồng hồ dựa server time; server vẫn quyết định timeout.

Replay trả initial entry sequence 0 và entries sau đó theo pagination. Mỗi entry gồm sequenceNo, kind, actorSide, resolvedEvents, stateAfter. Xem tua bằng snapshot; không chạy luật mới để suy ra lịch sử cũ. PlayerId/heroId trong snapshot là dữ liệu lịch sử; không phụ thuộc lineup hiện tại.

## 7. App test nhỏ

Một trang HTML/JS hoặc Vite tối giản có: chọn phiên đăng nhập; nút tải catalog/lineup; tìm trận; xác nhận; lưới 9×10; gửi command; panel JSON/event. Hiển thị ô hợp lệ từ server hoặc chỉ gửi tọa độ để test rejection. Không cần model, animation, cửa hàng đẹp, hoặc viết lại luật JavaScript.

Mock và backend thật sử dụng cùng interface client. Fixture skills chưa có implementation nên trước tiên test trận bằng nước di chuyển cơ bản; chưa dùng fixture skill để chứng minh gameplay hoàn chỉnh.
