# Hồ sơ và phòng chờ — 22/11/2026

`GET /api/v1/me` và phản hồi `PATCH /api/v1/me` có thêm `description`. PATCH chấp nhận tối đa 300 ký tự, cắt khoảng trắng hai đầu; `""` xóa, null hoặc không gửi giữ nguyên.

`POST /api/v1/matchmaking/rooms/join` chỉ thêm thành viên, trả phòng `ready` với `matchId: null`. Hai người ở phòng chờ.

Chủ phòng gọi `POST /api/v1/matchmaking/rooms/{roomId}/start` để tạo trận và trả phòng `selecting` kèm `matchId`. Khách gọi nhận 403; chưa đủ hai người nhận 409. Gửi lại khi trận còn mở trả cùng trận. Khách đọc `/rooms/current` để nhận trận sau khi chủ phòng bắt đầu. Luồng `/rematch` vẫn dùng cho đấu lại.

Trước khi triển khai API lên production, chạy script cộng thêm cột `HeroChess.Api/Data/Bootstrap/22_player_description.sql` trên database đích. Production đang tắt bootstrap nên deploy riêng không tự tạo cột. Script không đổi dữ liệu hồ sơ hiện có và có thể chạy lại. Khôi phục image cũ vẫn tương thích với cột mới.

Đã build API và chạy 12 test tích hợp hồ sơ/phòng trên PostgreSQL riêng tại localhost. Sau khi người dùng phê duyệt triển khai, đã áp script 22 trên database chung và xác nhận cột nullable dài tối đa 300 ký tự. API phát hành qua workflow Azure khi push master.
