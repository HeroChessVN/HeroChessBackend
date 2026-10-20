# Hero Chess React – giao diện test API

Frontend dùng API ASP.NET hiện có. Server quyết định lineup hợp lệ, nước đi, thời gian và hiệu ứng skill. Console JavaScript cũ vẫn ở `/`.

## Chạy local

Mặc định FE kết nối backend Render; đăng ký, mua hero và chơi trận sẽ ghi vào database mà service Render đang dùng. Muốn thử mà không ảnh hưởng dữ liệu đó, trỏ FE sang API/DB test local như bước 3. Không bật development fixtures trên production DB.

1. Chạy `cd frontend`, `npm ci`, rồi `npm run dev`.
2. Mở `http://127.0.0.1:5173/react/`. Vite chuyển tiếp API và WebSocket đến `https://herochessbackend.onrender.com`; trình duyệt vẫn truy cập qua cổng 5173.
3. Để thử BE local thay vì Render, chạy `dotnet run --project HeroChess.Api --launch-profile http` từ thư mục gốc. Trong terminal PowerShell chạy FE, đặt `$env:HERO_CHESS_API_URL = 'http://localhost:5012'` trước `npm run dev`. Khởi động lại Vite sau khi đổi biến này.

Nếu chạy bằng Visual Studio, frontend đã được build vào `HeroChess.Api/wwwroot/react`. Mở `http://localhost:5012/react/` hoặc `https://localhost:7297/react/`. Sau khi sửa React, chạy `npm run build` để cập nhật bản Visual Studio phục vụ. Swagger vẫn ở `/swagger` trong môi trường Development.

Để thử PvP, dùng hai tài khoản trong hai phiên trình duyệt. Trận bot chỉ cần một tài khoản. Mỗi tài khoản cần đủ hero sở hữu để lưu 16 vị trí và 3 skill. Catalog chỉ trả hero đang bật; hero mới bị tắt hoặc chưa sở hữu sẽ không chọn được.

File `08_step6_skills.sql` đã được thêm vào bootstrap. Trên DB mới/test, bật `DatabaseBootstrap__Enabled=true` để nạp nó cùng catalog. Cấu hình Development hiện đang tắt bootstrap, nên DB Supabase hiện tại **không tự thay đổi** khi chỉ pull code/chạy API.

UI giữ token test trong `sessionStorage` để reload vẫn mở lại được. Đây là thiết kế dành cho môi trường test, chưa phải cơ chế phiên đăng nhập đã rà soát cho production.
