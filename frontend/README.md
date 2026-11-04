# Hero Chess React – giao diện test API

Frontend dùng API ASP.NET hiện có. Server quyết định lineup hợp lệ, nước đi, thời gian và hiệu ứng skill. Console JavaScript cũ vẫn ở `/`.

## Chạy local

Mặc định FE kết nối backend Azure. Đăng ký, mua hero và chơi trận sẽ ghi vào DB mà backend Azure đang dùng. Muốn dùng DB test, trỏ FE sang API local như bước 3.

1. Chạy `cd frontend`, `npm ci`, rồi `npm run dev`.
2. Mở `http://127.0.0.1:5173/react/`. Vite chuyển tiếp API và WebSocket đến `https://herochessbe.eastasia.cloudapp.azure.com`.
3. Để thử BE local với DB test, chạy `dotnet run --project HeroChess.Api --launch-profile http` từ thư mục gốc. Trong terminal PowerShell chạy FE, đặt `$env:HERO_CHESS_API_URL = 'http://localhost:5012'` trước `npm run dev`. Khởi động lại Vite sau khi đổi biến này.

Để thử bot ba cấp, đăng nhập, lưu đội hình, vào **Chơi với bot**, chọn **Dễ**, **Trung bình** hoặc **Khó**, rồi bấm **Bắt đầu trận bot**. Màn trận hiển thị cấp do API xác nhận. Nếu FE báo backend chưa xác nhận cấp, hủy trận ở màn chọn đội hình và kiểm tra BE đang chạy bản có hỗ trợ `difficulty`.

Nếu chạy bằng Visual Studio, frontend đã được build vào `HeroChess.Api/wwwroot/react`. Mở `http://localhost:5012/react/` hoặc `https://localhost:7297/react/`. Sau khi sửa React, chạy `npm run build` để cập nhật bản Visual Studio phục vụ. Swagger vẫn ở `/swagger` trong môi trường Development.

Để thử PvP, dùng hai tài khoản trong hai phiên trình duyệt. Trận bot chỉ cần một tài khoản. Mỗi tài khoản cần đủ hero sở hữu để lưu 16 vị trí và 3 skill. Catalog chỉ trả hero đang bật; hero mới bị tắt hoặc chưa sở hữu sẽ không chọn được.

File `08_step6_skills.sql` đã được thêm vào bootstrap. Trên DB mới/test, bật `DatabaseBootstrap__Enabled=true` để nạp nó cùng catalog. Cấu hình Development hiện đang tắt bootstrap, nên DB Supabase hiện tại **không tự thay đổi** khi chỉ pull code/chạy API.

UI giữ token test trong `sessionStorage` để reload vẫn mở lại được. Đây là thiết kế dành cho môi trường test, chưa phải cơ chế phiên đăng nhập đã rà soát cho production.
