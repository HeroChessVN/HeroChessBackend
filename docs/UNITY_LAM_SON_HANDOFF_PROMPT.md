# Unity — Lam Sơn Quân Lược và bẫy Trần

BE và Unity đã được sửa local. Phải chạy SQL `22_forecast_review.sql` trước khi deploy API rồi phát hành client tương ứng. Chưa deploy từ đợt này.

## Luồng Lam Sơn

1. Gửi command `team_skill` như cũ. ACK bắt đầu cooldown và trừ lượt dùng nhưng giữ phe đi và deadline cũ. Với `state.skillTimingVersion=2`, CD mới giữ nguyên sau cast và sau xác nhận Đi/timeout (mặc định 15 sau SQL 23); timer cũ chỉ giảm một lần khi lượt thật sự kết thúc. Đọc CD frozen, không tự trừ một hoặc hardcode 15. Snapshot V1 giữ luật cũ.
2. Đọc `snapshot.state.forecastReview`: `{ "owner": "red", "activationSequence": 42 }`. Khóa di chuyển, Hero skill, Command skill và Undo khi còn trạng thái này. Đầu hàng vẫn được phép.
3. Gọi `GET /api/v1/matches/{id}/forecast?version=42`. Dùng mốc activationSequence, không dùng version bàn sau đó.
4. Mở bàn 2D riêng: `prediction.initialBoard`; các bước `variation[].boardAfter`; các nhánh nguy hiểm `threats[].boardAfter`. Chuỗi tối đa năm hành động địch → mình → địch → mình → địch (ba địch, hai mình); tối đa hai nhánh nguy hiểm ở bước đầu. Duyệt `variation.Length`, bỏ giới hạn ba bước nếu client còn hardcode. Có thể nhận ít hơn khi hết ngân sách tính hoặc trận kết thúc; không tự bù bước còn thiếu.
5. Dùng ảnh tướng, mũi tên và nút Trước/Tiếp. Chỉ thay bàn mô phỏng, không gửi các nước này lên server.
6. Nút **Đi** gửi command với commandId mới, expectedVersion hiện tại và `action: { "type": "end_turn" }`. ACK xóa forecastReview và chuyển phe; cả lần dùng skill chỉ tính một hành động.
7. Đồng hồ lượt gốc tiếp tục chạy. Server tự chốt khi hết giờ, không phạt AFK cho thời gian xem skill. Client không tự đổi phe. Tải dự báo lỗi vẫn có nút Đi.
8. Thu gọn cửa sổ không hoàn tác skill. Reconnect đọc lại forecastReview và mở đúng lần kích hoạt; mất mạng không khôi phục quyền đi thêm nước. Khi trận kết thúc hoặc server chốt lượt, đóng cửa sổ. Có thể xem lại dự báo bằng nút riêng.

Giữ cơ chế retry commandId hiện có. Retry GET có giãn nhịp, không gửi lại lệnh dùng skill. Forecast là gợi ý chứ không bảo đảm nước tiếp theo. Bàn mô phỏng đã lọc dữ liệu bí mật. Không dùng nó làm snapshot trận thật.

## Bẫy Trần

Bẫy địch không xuất hiện trong state của người gọi và không cản đặt skill. Thành/Rào/Cọc/bẫy của mình được đặt đè lên bẫy địch ẩn nếu ô hợp lệ theo thông tin công khai. Bẫy cùng phe vẫn chặn đặt trùng. Bẫy tạm không kích hoạt khi có quân hoặc bất kỳ công trình nào trên ô, kể cả Cọc.

Khi trúng bẫy, luôn lấy vị trí thực tế từ snapshot sau command, không tự đặt quân tại đích request. Quân dừng ở bẫy đầu tiên; Mã có thể dừng tại chân. Không tự ăn quân phía sau. Lý Thường Kiệt cũng chịu quy tắc khóa chân này.

## Các file chính

- `HeroChessOnlineDemo.cs`: gửi command, nhận snapshot, khóa hành động.
- `HeroChessOnlineDemo.CommandSkills.cs`: khôi phục review, GET forecast, xác nhận end_turn.
- `HeroChessOnlineDemo.AuthoredUI.cs`: hiển thị/ẩn cửa sổ.
- `XiangqiPlayHud.Forecast.cs`: bàn dự báo 2D, dùng ảnh tướng có sẵn; không tải model 3D.

## Kiểm tra trên Unity Editor

- Dùng Lam Sơn: chưa đổi phe, không đi được quân; xem 0–5 bước và 0–2 nhánh nguy hiểm. Nếu có đủ năm bước, thứ tự là địch → mình → địch → mình → địch.
- Nhấn Đi hai lần, hoặc đúng lúc hết giờ: chỉ một lần chốt lượt.
- GET lỗi, đóng/mở cửa sổ, mất mạng, reconnect: vẫn đã dùng skill và giữ deadline gốc.
- Hết giờ tự đóng; không tăng chuỗi AFK. Đầu hàng khi đang xem đóng cửa sổ.
- Trong trận bot: sau khi chốt, bot mới đi; Undo đưa về trước lần dùng skill.
- Bẫy ẩn không làm lộ tọa độ qua việc đặt Thành/Rào/Cọc/bẫy. Bẫy dưới công trình chờ đến khi ô trống.

## Nâng chuỗi dự báo từ 3 lên 5 hành động

BE trả tối đa năm mục `variation`; schema API giữ nguyên. Đợt này chỉ sửa BE và tài liệu, chưa sửa source Unity. Các chỗ Unity hiện vẫn giới hạn ba bước:

- `HeroChessOnlineDemo.CommandSkills.cs`: `ForecastLines(prediction, "variation", 3)` trong phần mô tả sử dụng và `AddForecastMoves(f, Child(prediction, "variation"), false, 3)` trong phần tạo dữ liệu hiển thị. Cả hai cần lấy đủ số mục server trả, tối đa năm; không cắt còn ba.
- `LamSonForecastView.cs`: hiện chỉ tạo năm `RowView` (`for (int i = 0; i < 5; i++)`). Trước đây đủ cho ba bước và hai nguy cơ; nay cần hỗ trợ tối đa bảy dòng, tốt nhất tạo thêm dòng theo số mục thực tế. Kiểm tra lại chiều cao, vùng cuộn và giới hạn mép màn hình khi hiện đủ năm bước cùng hai nguy cơ.
- `XiangqiPlayHud.Forecast.cs` đã đọc `variation.Count` để điều hướng bàn dự báo; giữ cách đọc động này.

Test cả khi người dùng skill là Đỏ lẫn Đen: thứ tự tương đối luôn là địch ① → mình ① → địch ② → mình ② → địch ③. Cảnh báo vẫn thuộc lượt địch đầu tiên, không phải bước thứ sáu và bảy của chuỗi. Có thể trả 0–5 bước khi hết ngân sách hoặc trận kết thúc.
