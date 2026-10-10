# Hero Chess React – giao diện test API

Frontend dùng API ASP.NET hiện có. Server quyết định lineup hợp lệ, nước đi, thời gian và hiệu ứng skill. Console JavaScript cũ vẫn ở `/`.

## Chạy local

Hãy cấu hình API trỏ tới PostgreSQL **test/local riêng** trước khi đăng ký, mua hero hoặc chơi trận; các thao tác này ghi DB. Không bật development fixtures trên production DB.

1. Từ thư mục gốc repo, chạy API: `dotnet run --project HeroChess.Api --launch-profile http`.
2. Trong terminal khác: `cd frontend`, `npm ci`, rồi `npm run dev`.
3. Mở `http://127.0.0.1:5173/react/`. Vite chuyển tiếp API và WebSocket đến `http://localhost:5012`. Nếu API chạy cổng khác, đặt biến `HERO_CHESS_API_URL` trước khi chạy Vite.

Nếu chạy bằng Visual Studio, frontend đã được build vào `HeroChess.Api/wwwroot/react`. Mở `http://localhost:5012/react/` hoặc `https://localhost:7297/react/`. Sau khi sửa React, chạy `npm run build` để cập nhật bản Visual Studio phục vụ. Swagger vẫn ở `/swagger` trong môi trường Development.

Để thử PvP, dùng hai tài khoản trong hai phiên trình duyệt. Trận bot chỉ cần một tài khoản. Mỗi tài khoản cần đủ hero sở hữu để lưu 16 vị trí và 3 skill. Catalog chỉ trả hero đang bật; hero mới bị tắt hoặc chưa sở hữu sẽ không chọn được.

File `08_step6_skills.sql` đã được thêm vào bootstrap. Trên DB mới/test, bật `DatabaseBootstrap__Enabled=true` để nạp nó cùng catalog. Cấu hình Development hiện đang tắt bootstrap, nên DB Supabase hiện tại **không tự thay đổi** khi chỉ pull code/chạy API.

UI giữ token test trong `sessionStorage` để reload vẫn mở lại được. Đây là thiết kế dành cho môi trường test, chưa phải cơ chế phiên đăng nhập đã rà soát cho production.
