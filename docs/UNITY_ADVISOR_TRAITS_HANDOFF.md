# Tích hợp bốn Hero Trait Sĩ và bảng SP

BE đã có code hỗ trợ; chỉ test tích hợp sau khi API tương ứng được deploy và catalog đã gắn trait. Không sửa luật cờ hoặc DB từ Unity. Dùng `docs/api.md` làm hợp đồng.

## Công việc Unity

1. Đọc lại catalog và giá `heroes[].setupPoints`, budget từ `ruleset.setupBudget`; hiển thị đội hình cũ vượt điểm để người chơi sửa. Không hardcode bảng giá hoặc tự thay hero. `CONTENT_VERSION_CHANGED` yêu cầu tải catalog và tạo lại lượt chọn trận theo luồng hiện có.
2. Nhận trait bằng `implementationKey`, không so tên hiển thị. Lê Lai: `le_lai.swap_general` — Lê Lai cứu chúa, active, CD 8. Trần Thủ Độ: `tran_thu_do.general_shield` — Trấn Quốc Hộ Quân, active, tồn tại 2 lượt chung/CD 6. Hai Sĩ Nguyễn Nhạc và Nguyễn Lữ: `advisor.king_step` — Thái Đức Linh Bộ / Đông Định Linh Bộ, special_move, không cooldown.
3. Hai active chỉ chọn Tướng đồng minh đang sống. Gửi `pieceId` của quân runtime, không dùng `heroId` catalog. Với Lê Lai, nguồn và Tướng ở trong cung, có thể cách xa nhau; không kiểm tra đường đi hoặc yêu cầu đứng kề. BE kiểm tra tự chiếu và khóa chân. Trần Thủ Độ có thể bảo vệ Tướng ngoài cung và vẫn dùng khi bản thân bị khóa chân.
4. Gửi action bằng command envelope hiện tại:

```json
{
  "commandId": "<uuid>",
  "expectedVersion": 12,
  "action": {
    "type": "hero_active",
    "pieceId": "<pieceId Lê Lai>",
    "skillCode": "le_lai.swap_general",
    "target": { "pieceId": "<pieceId Tướng đồng minh>" }
  }
}
```

5. Trần Thủ Độ đổi key và pieceId nguồn, giữ target là Tướng. Retry cùng command dùng cùng commandId; không tạo command mới chỉ vì mất ACK. Khi 409, lấy state mới và cho người chơi chọn lại.
6. Thành công đọc snapshot mới và cập nhật hai quân đồng thời. Lê Lai có hai `piece.moved`, không có bắt quân. Không xóa model Tướng ở ô đích, không tạo bản sao hoặc làm mất hiệu ứng gắn trên pieceId. `hero_skill.activated` cung cấp tên cho thông báo dùng skill. ACK duplicate không phát animation lần hai.
7. Khiên Trần Thủ Độ đọc `pieces[].effects` với `code=shield`, `sourcePieceId` và `remainingTurns`. Hai lượt chung gồm lượt kích hoạt: sau ACK còn 1; đầu lượt mình tiếp theo đã hết. CD hero đọc `traitState.heroCooldownRemaining`: thường 5 sau dùng Trần Thủ Độ, 7 sau Lê Lai. Không dùng timer theo giây hoặc tự cộng bù. Không nhầm với Command Khiên duration 4/CD 14.
8. Hai Sĩ Tây Sơn dùng `move` và `/legal-actions`; không hiện nút active chỉ vì có trait. Không áp di chuyển mới lên bản GENERAL của hai nhân vật.
9. `ALREADY_SHIELDED`, `PIECE_ROOTED`, `INVALID_TARGET`, `SKILL_ON_COOLDOWN`, `KING_IN_CHECK` và `INVALID_HERO_SKILL_CONFIG` phải hiện phản hồi rõ. Target GUI chỉ là ý định; không tự thay luật hoặc ghi đè snapshot khi BE từ chối.
10. Undo/reconnect/replay lấy vị trí, hiệu ứng, metadata và CD từ snapshot tương ứng, không lấy catalog mới để thay dữ liệu trận cũ. Luôn giữ cơ chế chặn response cũ theo version/ngữ cảnh trận.

## Kiểm tra trước bàn giao

- Lê Lai đổi với Tướng ở xa trong cung, có quân ở giữa; cả hai quân về đúng ô và không bị tính là ăn quân.
- Tướng ngoài cung, nguồn/Tướng bị khóa chân hoặc đổi xong vẫn chịu chiếu: hiển thị lỗi từ BE, bàn không đổi.
- Trần Thủ Độ chỉ target Tướng, không target Sĩ/Xe; Tướng ngoài cung vẫn được bảo vệ.
- Tướng đang có Khiên không thể nhận thêm Khiên từ nguồn khác; bảo hộ ngắn hết đúng đầu lượt mình sau một lượt địch.
- Cả Red/Black, hết giờ, retry, WS/HTTP, undo và reconnect giữ đúng trạng thái.
- Sĩ Tây Sơn có nước ngang/dọc/chéo một ô trong cung; bản Tướng không thay đổi.
- Đội hình 51 SP bị chặn xác nhận; đội 50 SP hợp lệ, giá hiển thị khớp catalog.

Danh sách file Unity cụ thể để người phụ trách xác định theo cấu trúc project hiện tại. Đợt BE này không sửa Unity hoặc hứa đã kiểm chứng giao diện.
