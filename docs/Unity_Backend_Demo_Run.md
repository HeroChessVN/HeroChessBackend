# Chạy demo Unity với HeroChess Backend

## Giao diện hiện tại

Đã bỏ menu IMGUI phủ toàn màn hình. Unity dùng lại **LoginScene, MainMenuScene, FormationList, FormationEditor, MatchFoundScene và BoardScene của nhóm**, giữ nền, font, bố cục, prefab và hiệu ứng nút. `MainMenuVietnameseSDF.asset` là font; `LoginBackground.renderTexture` là texture nền, không phải controller đăng nhập.

Các controller sẵn có gọi `HeroChessOnlineDemo` → `HeroChessApi` → backend → PostgreSQL/Supabase. Không có mật khẩu DB trong Unity. Backend quyết định nước đi, skill, CD và kết quả.

- Login/register dùng tài khoản API. Nút tạo tài khoản dùng cùng phong cách nút đăng nhập trong khung gốc. Guest/provider vẫn chưa có API.
- Sảnh: chọn **RANKED PvP** hoặc **VS BOT**, bấm **VÀO TRẬN**; có hủy tìm trận. Không còn tự ghép đối thủ giả sau 5 giây.
- Đội hình: dữ liệu tài khoản từ `/lineups` và hero sở hữu từ `/catalog`; 16 ô + 3 command skill. Giữ 12 ô của luồng offline riêng, không ghi đè PlayerPrefs đội hình offline bằng dữ liệu server.
- Lưu/xóa đội hình gọi API, dùng revision. Chỉ cập nhật cache sau ACK. Server kiểm tra SP, sở hữu, trùng nhân vật và điều kiện skill.
- Chọn đội hình/xác nhận dựa trên trạng thái server; chỉ vào bàn khi trận đã active.
- BoardScene giữ bàn 3D, camera, HUD, nút đầu hàng/thoát/đổi góc nhìn. Nút cầu hòa chưa có API được đổi thành **ĐỒNG BỘ**. Bảng skill bổ sung bên trái dùng cùng kiểu panel/nút của HUD; hiển thị command CD hai phe, chọn quân để xem hero skill/CD, chọn mục tiêu rồi xác nhận.
- Trạng thái được poll mỗi 1,2 giây; Unity chạy nền khi chuyển cửa sổ. Không chạy luật cờ offline song song với server.
- 15 model trong `Art/Models/Characters` được tham chiếu từ `Resources/HeroChessModels.asset`. Tên hero được bỏ dấu để đối chiếu với tên model; các class cùng nhân vật dùng chung model. Avatar humanoid được tạo riêng để dùng controller có sẵn, không sửa FBX nguồn. Hero thiếu model dùng `PlayerArmature` dự phòng.

## Chạy cùng máy

1. Mở PowerShell tại `D:\Code\Github\HeroChessBackend`:

   ```powershell
   dotnet run --project HeroChess.Api --launch-profile http
   ```

   Giữ API chạy. `http://127.0.0.1:5012/health/ready` phải trả ready. Kết nối Supabase hay Docker vẫn do cấu hình backend quyết định.

2. Mở Unity project `D:\Unity\Capstone-Hero-Chess` bằng Editor `6000.3.25f1`, mở `Assets/HeroChess/Scenes/Menu/LoginScene.unity`, bấm Play. Nhập email/mật khẩu tài khoản Player trên DB backend đang sử dụng.
3. Mở **ĐỘI HÌNH**, chọn hoặc tạo đội; chọn từng ô rồi chọn hero phù hợp ở thư viện. Tab **DANH SÁCH SKILL** chọn 3 command. Lưu để gửi lên server. Không còn nút tự điền của giao diện debug cũ.
4. Về sảnh → **VS BOT** → **VÀO TRẬN** → chọn đội → xác nhận. PvP cần hai tài khoản và cùng một backend process.
5. Chọn quân rồi chọn ô hợp lệ. Skill ở bảng bên trái: chọn skill, chọn ô/quân mục tiêu rồi xác nhận. Chọn quân đối thủ để đọc nội tại/hero CD; không được gửi lệnh cho quân đối thủ.

Mặc định API URL là `http://127.0.0.1:5012`. Đổi khi cần: trong Play Mode, chọn `DontDestroyOnLoad / HeroChess Online API Client` ở Hierarchy, sửa **Endpoint** trong Inspector trước khi đăng nhập. URL được lưu trong PlayerPrefs khi gửi đăng nhập; không thêm `/api/v1`. Nếu Unity ở máy khác, dùng IP LAN máy chạy backend và cấu hình backend nghe LAN.

## Kiểm tra đã chạy

- Unity CLI compile thành công.
- API giả trên cổng riêng **5013**, không ghi DB: register trả body rỗng → login → sảnh gốc → mở/lưu đội hình 16 ô → nút VS BOT → chọn/xác nhận → BoardScene 32 actor → move nhận ACK/version/vị trí mới.
- Mock kiểm tra request lưu có đủ 16 slot duy nhất, đúng hero và expectedRevision; DELETE có expectedRevision trả 204 và cache được cập nhật.
- Đã kiểm tra ảnh Game View của login, sảnh, đội hình, bàn cờ. Trong trận thử, 10 quân có model hero tương ứng được thay đúng; quân khác dùng fallback.
- 15/15 avatar humanoid hợp lệ; kiểm tra idle trong Play Mode.
- Self-check có thể chạy lại sau đăng nhập: menu Unity **Hero Chess → Backend → Validate authored UI (after login)**. Kiểm tra 16 slot, ánh xạ slot API không trùng, lineup round trip, model/rig và raycaster UI.
- Khi thêm model cùng bộ xương CC_Base, Stop Play rồi chạy **Hero Chess → Backend → Rebuild hero model bindings**. Model rig khác cần bổ sung mapping, không đoán tên xương.

## Giới hạn cần biết

Chưa kiểm tra lại trận với Supabase thật, hai máy PvP hoặc bản Windows build. Trước buổi demo cần thử một trận bot thật: đi vài nước → command skill → hero skill → đầu hàng → về sảnh. Poll có độ trễ khoảng 1,2 giây cộng thời gian mạng.

Replay hoàn chỉnh, shop và VFX skill riêng chưa tích hợp trong Unity. Nút Replay hiện thông báo lịch sử gần đây; chưa có điều khiển tua trận. Log nước đi chỉ ghi thay đổi snapshot từ khi mở bàn, chưa tải đầy đủ lịch sử khi reconnect. Hai command có target đường vẫn dùng lựa chọn cố định của demo trước. Animation hiện dùng lại controller chung, chưa phải bộ chiêu riêng cho từng hero.

Script trước lần sửa giao diện được sao lưu tại `D:\Unity\Capstone-Hero-Chess\Backups\BeforeAuthoredUI_20261010` (ngoài Assets). Các scene/prefab/nền gốc không bị ghi đè. Project Unity này không phải Git checkout: cần đưa các script sửa, file mới và `Resources/HeroChessModels.asset` cùng `.meta` vào repo Unity của nhóm, không chỉ copy thư mục Backend.

