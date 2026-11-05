# FE tích hợp Undo và trận tùy chọn

Base API là `{BACKEND_URL}/api/v1`. Mọi request dưới đây gửi `Authorization: Bearer {accessToken}`. BE cần deploy bản hỗ trợ và chạy xong migration `12_custom_rooms.sql`.

## 1. Undo trận bot

Hiện nút **Hoàn tác** khi `snapshot.mode === "bot"` và `snapshot.status === "active"`. Khóa nút khi đang gửi lệnh. Có thể bấm lúc bot đang tính.

`POST /matches/{matchId}/commands`

```json
{
  "commandId": "<UUID mới>",
  "expectedVersion": 12,
  "action": { "type": "undo" }
}
```

- `expectedVersion` lấy từ snapshot mới nhất. BE tự chọn mốc trước hành động gần nhất của người chơi. Nếu bot đã đáp thì lùi cả nước bot.
- Response có `snapshot`. Thay toàn bộ state từ snapshot đó, gồm quân, hiệu ứng, cooldown, số lần dùng skill và đồng hồ. Xóa lựa chọn quân/skill đang mở.
- `409 STALE_STATE` hoặc `TURN_DEADLINE_PASSED` → tải lại `GET /matches/{matchId}/state`, cho người chơi bấm lại.
- `422 NO_UNDO_AVAILABLE` → báo đã về đầu ván, khóa Undo đến khi state đổi. `UNDO_NOT_ALLOWED` → chế độ này không hỗ trợ Undo.
- Mất response do mạng → thử lại đúng `commandId` và body cũ để tránh lùi thêm lần nữa. Trận đã kết thúc thì khóa Undo.

## 2. Phòng tùy chọn

Tiền tố các endpoint là `/matchmaking/rooms`.

| Thao tác | Method và đường dẫn | Body | Kết quả |
|---|---|---|---|
| Tạo phòng | `POST /matchmaking/rooms` | Không cần | 201 + room |
| Tìm lại phòng khi đăng nhập/reload | `GET /matchmaking/rooms/current` | — | 200 + room hoặc 204 |
| Đọc trạng thái phòng | `GET /matchmaking/rooms/{roomId}` | — | 200 + room |
| Nhập mã | `POST /matchmaking/rooms/join` | `{"code":"ABCD2345"}` | 200 + room |
| Rời phòng | `POST /matchmaking/rooms/{roomId}/leave` | `{"reason":"leave_room"}` | 204 |
| Thoát game/đăng xuất | Cùng endpoint leave | `{"reason":"end_session"}` | 204 |
| Báo còn online | `POST /matchmaking/rooms/{roomId}/presence` | Không cần | 204 |
| Chơi lại | `POST /matchmaking/rooms/{roomId}/rematch` | `{"previousMatchId":"<lastMatchId>","requestId":"<UUID mới>"}` | 200 + room |

Room response có dạng:

```json
{
  "roomId": "<UUID>",
  "code": "ABCD2345",
  "memberCount": 1,
  "capacity": 2,
  "status": "waiting",
  "isOwner": true,
  "matchId": null,
  "lastMatchId": null,
  "ownerOnline": true
}
```

### Luồng UI

1. Tạo phòng → hiện **Chờ · 1/2**, mã phòng, **Sao chép mã**, **Rời phòng**. Mã 8 ký tự, nhập không phân biệt hoa/thường.
2. Poll trạng thái khoảng 2–3 giây. Khách nhập mã → **2/2**. Khi có `matchId`, mở màn chọn đội hình và chơi bằng luồng trận hiện có. Chỉ tự điều hướng một lần cho mỗi `matchId`.
3. `status` gồm `waiting`, `selecting`, `active`, `ready`. `matchId` chỉ có khi đang chọn đội hình/đánh. `lastMatchId` lưu ID ván gần nhất.
4. Kết thúc ván → cho quay về phòng. Khi `status === "ready"` và `isOwner`, hiện **Chơi lại**. Gửi `previousMatchId = room.lastMatchId`. Retry cùng lần bấm phải giữ nguyên `requestId` và body.
5. Gửi presence mỗi 15 giây cả khi ở màn phòng và bàn cờ. Nếu `ownerOnline === false`, hiện **Chủ phòng mất kết nối**. Mất tín hiệu giữ mã để kết nối lại.
6. Chủ bấm **Rời phòng** khi còn khách → khách lên làm chủ, mã giữ nguyên. Chủ gửi `end_session` → đóng phòng cho cả hai. Khách rời → phòng về 1/2. Còn 0/2 → đóng phòng.
7. Khi logout, gửi `end_session` trước khi xóa token. Khi game đóng chủ động, cố gắng gửi cùng request. Với đóng tab/kill app/mất mạng, BE chỉ biết thiếu presence và giữ phòng. Reload/chuyển màn không gửi leave.

### Lỗi cần hiển thị

- `PLAYER_BUSY` → người chơi đang queue, có trận hoặc vẫn ở phòng. Đầu hàng chỉ kết thúc ván; dùng **Rời phòng** trước khi chuyển sang ranked/bot.
- `ROOM_NOT_FOUND` → mã sai/phòng đã đóng. Gọi `/current` để khôi phục sau khi mất response.
- `ROOM_FULL`, `ROOM_SELF_JOIN` → báo phòng đầy hoặc đang ở phòng của mình.
- `ROOM_MATCH_ACTIVE` → kết thúc ván trước khi bấm Rời phòng.
- `ROOM_OWNER_REQUIRED`, `ROOM_NOT_READY` → tải lại phòng để cập nhật nút Chơi lại.
- `ROOM_JOIN_LIMIT` (429) → chờ rồi thử lại. BE giới hạn join 10 lần/phút/tài khoản.
- `REMATCH_REQUEST_CONFLICT` → requestId đã dùng cho ván khác. Tải lại phòng, lần bấm mới dùng UUID mới.

Trận có `mode = "custom"`, lưu lịch sử/replay và giữ nguyên Elo, coin, thống kê ranked. Tạo trận custom qua API phòng. Trong sảnh phòng, khóa nút tìm ranked/bot đến khi rời phòng. Test bằng hai tài khoản ở hai phiên trình duyệt hoặc hai máy.
