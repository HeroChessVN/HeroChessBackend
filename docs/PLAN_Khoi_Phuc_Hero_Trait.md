# Kế hoạch khôi phục Hero Trait trong HeroChess

Ngày 08/11/2026 · Mốc đối chiếu backend `2332f90`

## Phạm vi

Khôi phục Trait cho trận **tùy chọn, xếp hạng và đấu bot cả ba cấp**. Tái sử dụng các handler của 12 biến thể trong roster đã đối chiếu. Khôi phục luật di chuyển riêng, Quang Trung và Bạch Đằng Giang qua cùng luồng kiểm tra và lưu hành động của server. Lấy phần wiring cũ trước commit `93323a7` làm tham chiếu và ghép vào pipeline hiện hành để giữ các thay đổi hiệu năng, Undo và phòng tùy chọn.

Các bước dưới đây là thứ tự triển khai và kiểm thử. Bản phát hành cần đạt tiêu chí của cả ba chế độ.

## Luật Bạch Đằng Giang đã chốt

| Hạng mục | Hành vi cần triển khai |
|---|---|
| Đặt Cọc | Chỉ Trần Hưng Đạo hệ Tượng còn sống, đúng bên và đúng lượt. Chọn một giao điểm trống ở `y=4` hoặc `y=5`. Dùng chiêu tiêu một lượt. Hai bên đều thấy Cọc và phe sở hữu. |
| Quân đi qua | Nước đúng hình học và an toàn Tướng trước khi xét Cọc vẫn có thể gửi. Quân địch không có Khiên bị loại tại Cọc đầu tiên trên đường hoặc tại ô đích. Nếu Cọc nằm giữa đường thì quân chưa chạm ô đích, nên chưa ăn quân hay phá vật cản ở đích. |
| Đường đi | Xe, Pháo và các trait đi thẳng hoặc chéo xét lần lượt các giao điểm trên đường. Mã xét ô chân rồi ô đáp. Điểm xuất phát không tính. Đường tránh được giao điểm có Cọc thì đi bình thường. |
| Tương tác | Quân đồng minh và quân đang có Khiên đi qua an toàn. Cọc là bẫy theo đường đi, không tự chặn tia di chuyển và không làm ngòi Pháo. Cọc còn tồn tại sau khi bắt quân. |
| Lộ Tướng | Sau khi loại quân dính Cọc, nếu Tướng bên vừa đi bị chiếu theo luật hiện hành, kể cả hai Tướng đối mặt, bên vừa đi thua ngay. Tướng trực tiếp bị Cọc loại cũng thua ngay. Xét kết quả này trước giới hạn SP. |
| Hồi chiêu và thời lượng | Mặc định CD 5, tồn tại 3 lượt chung, tính cả lượt đặt. Đọc `cooldownTurns` và `durationTurns` từ snapshot trait của trận. Mỗi quân Trần Hưng Đạo hệ Tượng có tối đa một Cọc đang tồn tại. Hết CD mà Cọc cũ vẫn còn thì phải chờ Cọc biến mất. |

**Quy ước triển khai cho ca trùng đích.** Nếu ô đích có Cọc địch và cũng có quân địch hoặc vật cản mà nước đi thường có thể ăn hoặc phá, xử Cọc trước. Quân đi bị loại, quân hoặc vật cản trên ô đích giữ nguyên. Khi đặt Cọc mới, từ chối ô đã có Cọc công khai.

## Thứ tự triển khai

### 1 Kiểm tra catalog và cố định luật theo trận

- Đối chiếu `hero.trait_id`, `hero_trait.implementation_key`, `kind` và `parameters` trên Supabase theo `code`. So 12 biến thể đã biết với handler thực tế. Nếu thiếu liên kết thì viết bản vá theo `code`, chạy lại được và giữ ID hiện có. Tránh chạy lại seed rộng vì có thể ghi đè catalog đã cân bằng.
- Khôi phục `hero.trait` từ `CatalogController`. Đưa liên kết, kind, key và tham số gameplay vào `CatalogVersionService`. Khôi phục metadata nullable trong `FrozenPiece` và gắn vào `PieceState` khi khởi tạo trận. Khởi tạo trạng thái sẵn sàng của Quang Trung từ snapshot.
- Lưu dấu hiệu luật Cọc công khai theo trận và truyền tới bộ luật khi tạo hoặc khôi phục trạng thái. Snapshot cũ thiếu dấu hiệu dùng hành vi tương thích cũ. Trận đang chơi, replay và Undo đọc từ snapshot của chính trận. Kiểm tra phiên bản catalog để hai bên trong một trận đang chọn đội hình không dùng dữ liệu ở hai thời điểm khác nhau.

### 2 Sửa luật Cọc và dùng lại một đường thực thi

- Sửa `BachDangGiang` để đọc tham số số nguyên hợp lệ từ `PieceState.TraitParameters`. Dùng 5 và 3 khi snapshot cũ thiếu trường. Chặn cấu hình sai kiểu, bằng 0 hoặc âm khi duyệt catalog để tránh Cọc không hết hạn. Dùng `ThdTuongCocStakes` và vòng đời hiện có để khóa đặt tiếp trong lúc Cọc cũ còn.
- Trong `XiangqiRulesEngine`, xác định đường đi và Cọc địch đầu tiên trước khi ăn quân hoặc phá vật cản ở đích. Áp dụng cho nước thường và di chuyển riêng. Giữ phép kiểm tra an toàn Tướng trước Cọc cho bước duyệt lệnh, rồi kiểm tra lại sau khi Cọc thực sự loại quân để xử thua đúng bên. Xét đường chiếu với Cọc công khai để nước tấn công bị Cọc chặn trước khi đến Tướng không sinh chiếu giả. Giữ tương tác Khiên và tránh gọi kiểm tra chiếu đệ quy từ chính logic tính đường đi.
- Xuất kết quả hành động từ dữ liệu đã giải quyết. Sự kiện Cọc trúng ghi quân bị loại và giao điểm trúng Cọc. Nếu bị bắt giữa đường, sự kiện không được ghi như thể quân đã tới ô đích. Nếu trận kết thúc do Cọc, dùng lý do nhất quán cho state, replay và client.

### 3 Khôi phục kỹ năng chủ động và dữ liệu trả client

- Nối lại `hero_active` trong `MatchCommandService` qua handler chung cho Quang Trung và Bạch Đằng Giang. Kiểm tra đúng bên, quyền sở hữu quân, key trait trong snapshot, cooldown, mục tiêu, version và deadline. Cho hành động thành công chạy cùng `MatchTurnRules.AfterAction` để đổi lượt và tick đúng một lần. Dùng lại kiểm tra kỹ năng này khi xét hết hành động sau nước đi và timeout.
- Khôi phục `/hero-actions` cho Quang Trung. Chỉ trả mục tiêu của quân thuộc người gọi ở đúng lượt. Trả metadata trait, cooldown và Cọc công khai qua `MatchStateProjection` cho cả hai bên trong trận mới. Giữ phân loại và cách che Cọc ẩn cho snapshot/replay cũ. Phân biệt Bạch Đằng Giang với Cọc của Command Skill để FE vẽ và phát hiệu ứng đúng loại.
- Dùng web test để thử lệnh và hiển thị cho cả hai phe. Bàn giao hợp đồng `hero_active`, `hero-actions`, trường state và sự kiện Cọc cho team Unity kiểm tra trên client của họ.

### 4 Tương thích bot và Undo

- Cho `BotEngine` và nhánh bot Legacy sinh được nước theo trait và ứng viên `hero_active`. Dùng chung luật Cọc và mô phỏng hậu quả thực tế trước khi chấm điểm. Điểm của nước bị loại giữa đường dựa vào trạng thái sau Cọc, gồm khả năng thua ngay vì lộ Tướng. Cọc công khai là dữ liệu hợp lệ cho bot. Trạng thái Cọc ẩn cũ vẫn đi qua kiểm tra hỗ trợ riêng.
- Tính cả hai kỹ năng tướng khi bot không còn nước thường. Giữ giới hạn ứng viên và thời gian, rồi đo lại số mô phỏng, thời gian search và thời gian đợi hàng. Giữ đường commit hiện tại để xử lý bot đang suy nghĩ khi người chơi Undo hoặc phiên bản trận đổi.
- Thêm `hero_active` vào nhóm hành động người chơi mà Undo có thể quay về trước đó. Kiểm tra phục hồi Cọc, cooldown và quân bị loại từ snapshot. Kiểm tra cả trường hợp bot đã đáp và trường hợp bot vẫn đang tính.

### 5 Hiện thông báo dùng kỹ năng và nhật ký trận

- Khi server chấp nhận kỹ năng của bất kỳ phe nào, hiện một banner nhỏ ở mép trên bàn cờ cho cả hai bên trong khoảng 2–3 giây. Không che ô cờ hoặc khóa thao tác. Ví dụ **Đỏ · Trần Hưng Đạo (Tượng) dùng Bạch Đằng Giang** hoặc **Đen · Quang Trung dùng Hành Quân Thần Tốc**. Với Command Skill, hiện **Đen dùng Khiên** vì kỹ năng thuộc đội hình. Bot dùng cùng mẫu, có thể thêm nhãn Bot cạnh tên phe.
- Dùng cùng một sự kiện để hiện banner và ghi một dòng vào mục **Lịch sử nước đi** theo thứ tự lượt. Banner báo lúc kích hoạt thành công. Trait đổi cách đi thụ động không báo ở mọi nước. Khi Cọc thực sự bắt quân, ghi riêng kết quả như **Cọc của Đỏ bắt Mã Đen tại e5**, tránh hiển thị hai lần thông báo kích hoạt cho cùng một lần đặt.
- Tái sử dụng `hero_skill.activated` và `team_skill.activated` trong `match_action.ResolvedEvents`. Đưa tên kỹ năng và, nếu có, tên tướng cùng loại quân từ snapshot trận vào sự kiện đã lưu. Client định dạng câu từ `side`, `skillName`, `heroName` và `pieceClass`. Tên trong replay giữ theo lúc đánh. Lỗi dùng skill chỉ hiển thị cho người gửi lệnh và không tạo dòng nhật ký kích hoạt.
- Gửi bản tóm tắt công khai của sự kiện kỹ năng qua trường tùy chọn trong `match.changed` **sau khi commit**, để cả bên còn lại và người đánh với bot nhận được ngay. Dùng `matchId` và `sequenceNo` của action để chống hiện lặp giữa xác nhận lệnh, broadcast và retry. Chỉ truyền dữ liệu cần hiển thị và vẫn áp dụng quy tắc chiếu dữ liệu của trận. Khi mất kết nối, tải phần lịch sử còn thiếu qua `/replay?afterSequence=...` và cập nhật nhật ký. Khi vào lại trận hoặc mở replay, không phát lại hàng loạt banner cũ. Undo thêm một dòng hoàn tác, không phát lại banner của nước đã hoàn tác.
- Web test tái sử dụng `frontend/src/history.js` và bảng lịch sử đang có trong `Match.jsx`. Team Unity đọc cùng dữ liệu để hiện banner và nhật ký theo bố cục của game.

## Kiểm thử và tiêu chí phát hành

| Phạm vi | Ca kiểm thử bắt buộc |
|---|---|
| Di chuyển và Cọc | Cả Đỏ lẫn Đen, đi thẳng, đi chéo, chân Mã, ô đáp, nhiều Cọc trên một đường, né Cọc, Cọc của mình, Khiên, ăn đích và phá vật cản. |
| Chiếu và kết quả | Nước vốn tự chiếu bị từ chối. Nước đúng luật nhưng mất quân che Tướng vì Cọc thua ngay, kể cả hai Tướng đối mặt. Cọc ngắt đường tấn công đúng chỗ. Kiểm tra còn hero skill giải nguy thì trận tiếp tục. |
| Cấu hình và lượt | Giá trị mặc định, giá trị từ DB được chốt lúc vào trận, dữ liệu cũ thiếu trường, tham số lỗi, tuổi Cọc dài hơn CD, Cọc hết hạn, lệnh gửi lại và timeout. |
| API và replay | Hai bên thấy đúng Cọc công khai và kết quả quân bị loại. Replay cũ vẫn đọc được. Lệnh sai chủ, sai lượt, sai key, sai version hoặc hết hạn không đổi state. |
| Thông báo kỹ năng | Hai phe thấy đúng tên tướng và kỹ năng sau khi server chấp nhận. Command Skill chỉ ghi tên phe và kỹ năng. Bot cũng phát thông báo. Retry không tạo banner lặp. Khi reconnect, nhật ký bù mục còn thiếu. Undo và replay không làm hiện lại banner live. |
| Ba chế độ | Đấu hết một trận tùy chọn và một trận ranked. Cả ba cấp bot chơi được khi có trait, biết xử lý Cọc và chạy được Undo. Đo độ trễ bot theo cấp trên môi trường kiểm thử trước khi mở production. |

Chạy bộ test Rules và các test PostgreSQL liên quan đến trait, kết quả lượt, replay, bot, Undo và phòng tùy chọn. Smoke test backend với web test và xác nhận hợp đồng với team Unity. Khi đã đạt các mục trên, mở cả ba chế độ cho trận mới. Đặt `MatchRuntime__HeroTraitsEnabled=false` để dừng tạo trận mới theo luật mới trong khi tiếp tục phục vụ các trận đã lưu cấu hình đó.

## Lệnh kiểm thử khi triển khai

Chạy tại thư mục gốc repo. `HERO_CHESS_TEST_DB` cần trỏ đến một PostgreSQL riêng có thể xóa sau khi test. Bộ integration test khởi tạo schema và fixture, vì vậy dùng DB dành riêng cho test.

```powershell
dotnet test tests/HeroChess.Rules.Tests/HeroChess.Rules.Tests.csproj
dotnet test tests/HeroChess.IntegrationTests/HeroChess.IntegrationTests.csproj
npm --prefix frontend test
npm --prefix frontend run build
```

Với web test, mở hai phiên đăng nhập cho trận tùy chọn và kiểm tra cùng một lần dùng kỹ năng xuất hiện đúng một lần trên mỗi màn hình. Ngắt kết nối một phiên trong lúc bên kia dùng kỹ năng, sau đó nối lại để kiểm tra nhật ký cập nhật và banner cũ không phát lại. Lặp lại với bot và trận ranked trước khi phát hành.
