# Hero Chess — Decisions

> Cập nhật 24/09/2026: User → Player/Admin loại trừ nhau; Admin không chơi. Xem [refactor tài khoản](/D:/Code/Github/HeroChessBackend/docs/User_Player_Admin_Refactor.md) cho contract /me, audit và migration mới. Các kết quả ngày 23/09 phía dưới là lịch sử.

**Quyết định 24/09:** Admin chỉ quản trị. `Player : User` và `Admin : User` là hai lớp con riêng; không dùng Admin như Player được cấp thêm quyền. Chọn TPH một bảng `user_account`; Bot nằm ngoài hierarchy User. Giữ dữ liệu game lịch sử của admin cũ để không phá tham chiếu, nhưng không cho admin tạo hoặc thao tác gameplay mới.

## User-confirmed

- 24/09/2026: một trait riêng cho một hero, để đặt tên và mở rộng độc lập. Cấm dùng cùng trait ID cho hai hero; vẫn được dùng chung implementation key/handler. Hero chưa có trait và trait nháp chưa gán vẫn được phép.

- Backend prototype phải hoàn thành trong hai ngày; logic riêng từng hero sẽ được bổ sung dần.
- Các phần T01–T14 và bản sửa review đã có mã; nghiệm thu cuối T15 vẫn cần đối chiếu bằng chứng thực tế.

## Technical choices

- ASP.NET Core và .NET 10; PostgreSQL qua Npgsql.
- `01_schema.sql` là bootstrap chuẩn cho 25 bảng gameplay. EF chỉ map bảng cần cho lát cắt hiện tại và không dùng `EnsureCreated`.
- ASP.NET Core Identity dùng bearer token tích hợp. Identity nằm trong schema `identity`; `auth_identity` nối Identity user với player.
- Contracts và Rules target .NET Standard 2.1, không phụ thuộc ASP.NET, EF hoặc Unity.
- Hero movement mở rộng được chọn bằng `implementationKey` trong registry của Rules.
- Matchmaking prototype giữ queue trong RAM; PostgreSQL giữ match, immutable snapshot, action log và settlement.
- Mỗi match có FIFO gate dùng chung cho player command, bot và timeout. WebSocket dùng ticket ngắn hạn một lần.
- Catalog gameplay được fingerprint SHA-256 khi tạo match và kiểm tra lại lúc confirm.
- Development policy: ranked thắng 10 coin, hòa 5 coin, Elo K=32; bot/cancel không thưởng và không đổi Elo.
- Development account nhận 1000 test coin qua ledger `starter`; production mặc định 0. Không có public endpoint cộng coin.
- Idempotency mua hero được giữ trên entitlement `player_hero`; purchase giá 0 không tạo ledger. Admin role chỉ seed từ danh sách email cấu hình ở Development.

## Development assumptions

- Tài khoản đăng ký ở Development được grant hero fixture khi `Onboarding:GrantDevelopmentFixtureHeroes=true`.
- Display name ban đầu lấy phần trước `@` của email; endpoint đổi tên thuộc phạm vi sau.
- Team skill fixture được chọn để kiểm tra lineup nhưng gọi gameplay phải trả `SKILL_NOT_IMPLEMENTED`.

## Unresolved

- Guest không bắt buộc trong prototype, có thể hoãn hoặc bỏ. Production reward/rating policy và danh sách hero/skill chi tiết chưa chốt.
- Data Protection key hiện persist trong thư mục local gitignored; production cần chọn cơ chế mã hóa/quản lý key theo môi trường deploy.

## Gameplay do chủ dự án xác nhận — 23/09/2026

Các quyết định dưới đây thay thế quy ước development cũ nếu có mâu thuẫn.

- Mỗi lượt 90 giây. Hết giờ khi không bị chiếu: mất lượt. Hai lượt của cùng một người liên tiếp đều hết giờ: người đó thua AFK, bất kể có lượt đối thủ xen giữa. Hành động gameplay hợp lệ của chính người đó xóa chuỗi; lệnh bị từ chối/reconnect không xóa. Đây là cách diễn giải triển khai của “2 lần liên tục” đã thông báo trong task.
- Hết giờ khi đang bị chiếu: thua ngay, không hủy trận. Ranked áp dụng settlement thắng/thua bình thường theo policy reward/Elo đang cấu hình.
- Giới hạn 150 tính tổng số nước của cả hai bên cộng lại. Move, skill hợp lệ và timeout đều tiêu thụ một lượt, tăng counter một lần. Khi có kết quả trực tiếp (chiếu bí, AFK, timeout trong chiếu), ưu tiên kết quả đó trước so SP. Nếu chưa kết thúc ở nước 150: so SP frozen của quân còn sống, bằng nhau hòa.
- Dùng skill thay thế nước đi, kể cả skill di chuyển quân. Cooldown tính theo lượt chung của bàn cờ, không chỉ lượt của chủ skill. Dispatcher skill và mốc giảm cooldown chi tiết sẽ hoàn thiện cùng đặc tả skill; stub hiện tại vẫn trả SKILL_NOT_IMPLEMENTED và không tính là một lượt đã dùng.
- Hướng ưu tiên hiệu ứng: phòng thủ trước tấn công, ví dụ khiên trước chiêu công. Danh mục effect, cách khiên chặn/tiêu hao và xử lý các hiệu ứng đồng hạng chưa chốt, không tự triển khai suy đoán.
- Mỗi tướng không thuộc nhiều phe. Command skill phe dự kiến cần 2–3 tướng của phe đó trong đội hình trên bàn lúc bắt đầu; ngưỡng chính xác sẽ cấu hình theo từng skill sau khi thiết kế.
- Đã xác nhận riêng: quyền skill phe xét/mở lúc bắt đầu trận và giữ nguyên dù tướng sau đó bị ăn. Không kiểm tra lại ngưỡng quân sống để thu hồi quyền.
- Prototype có tài khoản đăng nhập, xây đội hình, ghép trận PvP và bot chọn nước hợp lệ. Guest không bắt buộc, có thể làm sau hoặc bỏ.
- Mục tiêu nội dung khoảng tối thiểu 20 hero; các hero fixture dùng test không thay thế 20 hero có thiết kế gameplay hoàn chỉnh. Không chốt số lượng skill tối thiểu; chủ dự án sẽ thiết kế dần.
- Prototype có thể demo qua LAN/Wi-Fi chung; sản phẩm hoàn thiện phải chơi online. Chưa yêu cầu deploy public trong quyết định này.

## Trạng thái triển khai liên quan

- Đã có bot server: sinh legal actions từ Rules chung, ưu tiên ăn quân theo SP, tie-break xác định, hỗ trợ undo trong bot match. Chưa có mức khó/Elo được hiệu chuẩn như bot cờ vua.
- Timeout đã đổi sang thua AFK hoặc timeout_in_check; snapshot schema v3 lưu consecutiveTimeouts từng bên, clone/replay/undo giữ dữ liệu này. Snapshot cũ thiếu field mặc định streak = 0.
- Skill vẫn đang là extension/stub; chưa triển khai hiệu ứng, cooldown dispatcher hay kiểm tra ngưỡng phe cụ thể. Việc xác nhận luật không đồng nghĩa các skill đã có gameplay.
- Kết nối hai máy qua LAN chưa được kiểm chứng trong phiên này.
