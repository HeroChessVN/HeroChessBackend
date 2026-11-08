# Plan hoàn tác trận bot và phòng đấu riêng

> Khi triển khai, chạy từng nhóm test trước và sau thay đổi. Làm trên nhánh hiện tại. Tách hoàn tác và phòng riêng thành hai phần có thể nghiệm thu độc lập.

**Mục tiêu.** Người chơi có thể lùi về trước hành động gần nhất của mình khi đấu bot. Hai người chơi có thể đấu bằng mã phòng, lưu lịch sử và replay như PvP thường, với Elo và coin giữ nguyên.

**Kiến trúc.** Dùng pipeline command và lịch sử snapshot hiện có cho Undo. Lưu phòng, thành viên và mã ở PostgreSQL để phòng tồn tại độc lập với từng ván và giữ mã khi API restart. Khi người thứ hai vào, BE tạo trận `custom` rồi dùng lại luồng chọn đội hình, WebSocket, kết thúc trận và replay.

## Phần 1 — Hoàn tác trận bot

### Hành vi

- Nút **Hoàn tác** xuất hiện ở trận bot đang đánh. Một lần bấm lùi về trước hành động chơi gần nhất của người, bao gồm đi quân hoặc dùng Command Skill. Nếu bot đã đáp, lùi cả nước đáp. Nếu bot còn đang tính, lùi hành động của người.
- Snapshot phục hồi cả vị trí, quân đã mất, hiệu ứng, cooldown, lượt sử dụng skill, lượt chơi và số hành động. Theo hành vi Undo hiện có, đồng hồ bắt đầu lại với thời lượng chuẩn của lượt được khôi phục.
- Có thể hoàn tác nhiều lần tới đầu ván. Nếu đã ở đầu ván hoặc nhánh hiện tại chưa có hành động của người, trả `NO_UNDO_AVAILABLE`. Trận đã kết thúc, ranked và custom không dùng được.

### BE và web test

- [ ] Trong `MatchCommandService.cs`, mở rộng command `{ "type": "undo" }` để BE tự tìm mốc. Giữ đường gọi cũ có `targetSequence` cho client hiện hữu. Kiểm tra kiểu và phạm vi khi client truyền trường này.
- [ ] Dùng `match_action` làm nguồn sự thật. Bắt đầu từ version hiện tại, khi gặp một bản ghi Undo thì đi theo mốc đã khôi phục trong event `match.undone`, rồi tìm hành động của người gần nhất trên nhánh đó. Sau hành động số N, mốc khôi phục là snapshot N−1. Nếu đi tiếp sau Undo, mốc trước hành động mới có thể chính là snapshot do Undo tạo. Đọc metadata theo khóa trận và sequence, tránh kéo toàn bộ JSON bàn cờ để tìm mốc. Phát hiện mốc lịch sử lỗi hoặc không giảm để dừng an toàn.
- [ ] Giữ lock trận, `expectedVersion`, `commandId`, ACK và broadcast hiện có. Undo thêm một action mới và tăng version. Khôi phục snapshot cùng dấu lifecycle của lượt đó, tránh trừ cooldown hai lần hoặc gửi event lifecycle từ thế cờ vừa bỏ. Job bot tính trên version cũ sẽ bị từ chối ở bước commit. Nếu bot đã commit trước và client nhận `STALE_STATE`, tải lại snapshot rồi cho người chơi bấm lại.
- [ ] Trong `frontend/src/Match.jsx`, thay dropdown sequence bằng một nút. Khóa nút lúc đang gửi lệnh. Khi ACK về thì thay toàn bộ bàn cờ từ snapshot, dọn lựa chọn quân hoặc skill cũ và đồng bộ đồng hồ. Nếu BE báo không còn nước để lùi, khóa nút đến khi có version mới.
- [ ] Test theo `MatchFlowTests.cs`: trước và sau bot đáp, Undo sau Command Skill, Undo liên tiếp sau khi rẽ nhánh, replay vẫn chứa event Undo, lần gửi lại cùng command ID không lùi thêm, bot và Undo tranh commit, lỗi ở đầu ván và các mode PvP. Kiểm tra trạng thái sau Undo và số version tăng đúng.

## Phần 2 — Phòng đấu riêng

### Hành vi

- Chủ phòng bấm **Tạo phòng** thì thấy **Chờ · 1/2** cùng mã để sao chép. Bạn nhập mã thì phòng lên **2/2** và cả hai vào chọn đội hình theo luồng hiện tại. Chủ phòng cầm đỏ trong bản đầu. Đồng hồ, bộ luật và Command Skill lấy theo ruleset đang hoạt động.
- Mã gồm **8 ký tự ngẫu nhiên**, không có hạn theo thời gian chờ. Mã vẫn thuộc phòng lúc đủ 2/2, đang đánh và sau khi ván kết thúc. Khi hết ván, thành viên có thể quay về cùng phòng. Nếu cả hai còn ở lại, chủ phòng bấm **Chơi lại** để tạo ván mới với cùng mã. Trong khi một ván đang diễn ra, mã không nhận thêm người.
- Khách rời sảnh phòng thì về **1/2**, giữ mã. Nếu chủ bấm **Rời phòng** trong sảnh và vẫn còn khách, chuyển quyền chủ cho người còn lại và giữ mã. Khi còn **0/2**, hoặc chủ hiện tại **logout/thoát game**, đóng phòng và vô hiệu hóa mã.
- Client báo còn online mỗi 15 giây. Sau 90 giây thiếu tín hiệu, BE báo chủ mất kết nối và giữ phòng để chủ vào lại bằng cùng tài khoản. Tín hiệu thoát game hoặc logout chủ động đóng phòng ngay. Đóng phòng trong lúc đánh chỉ vô hiệu hóa mã, ván đã tạo đi theo luồng kết thúc trận hiện có.
- Kết quả thắng, hòa, thua, đầu hàng và replay vẫn được lưu. Trận phòng riêng không đổi Elo, coin, leaderboard hay bộ đếm ranked.

### API, matchmaking và DB

- [ ] Thêm hợp đồng dưới tiền tố `/api/v1/matchmaking`. `POST /rooms` tạo phòng. `GET /rooms/current` tìm lại phòng theo tài khoản và trả 204 nếu chưa có phòng. `GET /rooms/{roomId}` cho thành viên đọc trạng thái gồm `roomId`, `code`, `memberCount`, `capacity`, `status`, `isOwner`, `ownerOnline`, `lastMatchId` và `matchId` nếu đang có ván. `POST /rooms/join` nhận `{ "code": "..." }`. `POST /rooms/{roomId}/leave` nhận `reason: "leave_room"` khi rời sảnh hoặc `reason: "end_session"` khi thoát game/logout. `POST /rooms/{roomId}/presence` báo còn online. `POST /rooms/{roomId}/rematch` nhận `previousMatchId` và `requestId` để retry trả lại cùng ván. Client gửi `end_session` trước khi xóa token khi logout. Giới hạn thử mã sai theo tài khoản, dùng mã ngẫu nhiên và tránh đưa mã vào URL hay log.
- [ ] Mở rộng `MatchmakingService.cs` để tạo/join room dùng cùng gate và kiểm tra bận với ranked/bot. Trong DB, khóa phòng khi join hoặc đổi thành viên. Tạo trận, hai participant và liên kết trận với phòng trong cùng transaction. Nếu ghi thất bại, rollback để chủ vẫn chờ 1/2. Retry của người đã vào trả lại phòng và trận hiện có. Kiểm tra thành viên phòng từ DB khi vào ranked/bot để tránh mở hai luồng chơi, kể cả sau restart. Đóng phòng không nhả reservation của ván còn active.
- [ ] Thêm migration SQL lưu `custom_room` và `custom_room_member`, giữ mã duy nhất, tối đa hai ghế và một phòng mỗi người. Thành viên lưu thời điểm còn online gần nhất. Thêm `game_match.mode = 'custom'` và ràng buộc roster gồm hai người chơi thật. Áp dụng migration riêng trên Supabase trước khi deploy BE. Bootstrap hiện kiểm đúng 25 bảng trong khi production có 27. Cập nhật kiểm tra bootstrap theo tập bảng bắt buộc và đưa migration vào chuỗi dành cho DB mới/test.
- [ ] Tái dùng chọn đội hình, WebSocket và replay. Khi ván kết thúc hoặc hủy chọn đội hình, trả phòng về sảnh nếu còn mở. Settlement chỉ nhả reservation khớp `matchId` đã xử lý và chỉ cập nhật Elo, coin, stats cho ranked. Phân biệt recovery của phòng và recovery của ván khi API restart.

### UI và nghiệm thu

- [ ] Trong web test, thêm **Tạo phòng**, **Sao chép mã**, **Nhập mã**, **Rời phòng** và **Chơi lại**. Hiện trạng thái `Chờ · 1/2` và `2/2`. Cả hai đọc trạng thái khi ở sảnh phòng và tiếp tục báo còn online khi chuyển sang màn trận. Gắn nhãn **Phòng riêng** trong bàn cờ và lịch sử.
- [ ] Test hai tài khoản end-to-end trên PostgreSQL riêng. Chờ lâu vẫn giữ mã, khách rời thì trở về 1/2 cùng mã, chủ rời sảnh thì chuyển quyền, 0/2 hoặc chủ logout thì mã vô hiệu. Thử mất mạng rồi vào lại trong thời gian chờ, mất kết nối kéo dài, restart API, retry khi mất response và hai khách join đồng thời. Xác nhận mỗi phòng chỉ có một ván đang chọn đội hình hoặc active. Dùng tài khoản thứ ba kiểm tra quyền đọc phòng, đọc trận và xin vé WebSocket.
- [ ] Chơi hoặc đầu hàng để kết thúc. So trước/sau cho cả hai bên: Elo, ví, ledger coin và thống kê ranked giữ nguyên, lịch sử và replay có kết quả. Chạy lại regression ranked và bot. Khi BE và web test đã ổn, chuyển hợp đồng API cho bên Unity nối UI.
