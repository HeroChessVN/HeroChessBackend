# Prompt bàn giao Unity cho Lam Sơn Quân Lược

Lưu ý: phần BE bên dưới đã triển khai local, chưa deploy. Unity chưa được sửa trong đợt này. Chỉ test tích hợp sau khi API chạy bản BE tương ứng.

Sao chép phần dưới cho người phụ trách Unity:

---

Chỉ sửa luồng Lam Sơn Quân Lược và việc hiển thị kết quả authoritative của bẫy Trần. Không sửa luật cờ phía client, cấp độ bot, cooldown, dữ liệu tài khoản hoặc backend. Đọc AGENTS.md của project trước khi sửa. Không tạo nhánh/commit/push nếu chưa được yêu cầu.

## Bối cảnh và hợp đồng Lam Sơn

Kỹ năng `lam_son_quan_luoc` tốn một hành động và chuyển lượt. Sau ACK kích hoạt, gọi:

```http
GET /api/v1/matches/{matchId}/forecast?version={activationSequence}
```

`activationSequence` là `sequenceNo` trong ACK của lần dùng skill, không phải version mới nhất sau khi bot đi. Phản hồi:

```json
{
  "version": 42,
  "prediction": {
    "variation": [],
    "threats": []
  }
}
```

Mỗi mục chứa `side`, `action`, `from`, `pieceClass`, `pieceName`, `skillName`; chấp nhận null ở trường không áp dụng. Chuỗi gồm tối đa ba hành động địch → mình → địch, không phải ba phương án hay ba nước liên tiếp của mình. Nguy cơ là tối đa hai phương án ở bước địch đầu tiên, có thể không có.

BE tính từ snapshot lúc kích hoạt và trả version 42 dù bàn hiện tại đã là 43 hoặc trận đã kết thúc. Version trong phản hồi là mốc lịch sử của dự đoán. Không dùng response forecast làm snapshot bàn cờ. Cache hai phút là thời hạn giữ kết quả tính sẵn, không phải thời hạn mất quyền xem lại; khi hết cache BE có thể tính lại.

## Các vị trí cần đọc trong Unity

- `Assets/HeroChess/Scripts/Runtime/Backend/HeroChessOnlineDemo.cs`: `SubmitPending`, `AcceptSnapshot`.
- `Assets/HeroChess/Scripts/Runtime/Backend/HeroChessOnlineDemo.CommandSkills.cs`: `ReadSkillForecast`, `PresentForecast`, `ForecastActionLabel`.
- `Assets/HeroChess/Scripts/Runtime/Backend/HeroChessOnlineDemo.AuthoredUI.cs`: chỗ gọi `PresentForecast` và chọn skill.
- `Assets/HeroChess/Scripts/Runtime/Board/XiangqiPlayHud.cs`: bảng hiển thị dự đoán.

## Việc cần sửa

1. Khi ACK xác nhận Lam Sơn, chụp `matchId`, `activationSequence`, ngữ cảnh tài khoản/trận và một mã yêu cầu forecast cục bộ. Hiện “Đang phân tích…”. Gọi GET dù snapshot hiện tại đã mới hơn ACK.
2. Khi nhận dữ liệu, kiểm tra còn cùng tài khoản/trận và đây vẫn là yêu cầu Lam Sơn mới nhất. Kiểm tra `reply.version == activationSequence` đã chụp; **không** bắt nó bằng version bàn hiện tại.
3. Không xóa forecast chỉ vì `AcceptSnapshot` nhận một nước đi mới. Giữ kết quả để người chơi đọc/đóng/mở lại trong cùng trận. Xóa khi thoát trận, logout, chuyển tài khoản hoặc thay bằng lần kích hoạt mới. Undo phải xóa/hủy kết quả đang chờ; không nhận lại response của nhánh cũ.
4. Hiện tiêu đề “Lam Sơn Quân Lược”, mốc “Dự đoán tại lượt …”, chuỗi và nguy cơ. Khi bàn đã khác mốc, ghi “Bàn cờ đã thay đổi — đây là dự đoán tại thời điểm kích hoạt”. Không giữ mũi tên cũ trên bàn hiện tại khiến người chơi hiểu nhầm là nước hợp lệ mới.
5. Nếu có minh họa mũi tên, chỉ vẽ ở snapshot đúng mốc hoặc khung xem lại riêng. Không di chuyển/xóa quân thật hay áp hành động trong chuỗi dự đoán lên trận đang chơi.
6. Danh sách ngắn hơn ba hoặc hai vẫn hợp lệ. Nếu cả hai rỗng, hiện “Chưa có phương án dự đoán phù hợp”, không báo chắc chắn đủ ba nước.
7. Nút thử lại chỉ gọi GET với cùng activationSequence. Không gửi lại `team_skill` bằng commandId mới, không tiêu thêm lượt hoặc reset cooldown.
8. `429 FORECAST_BUSY`: báo máy chủ bận và cho tải lại có giãn nhịp. `429 FORECAST_RATE_LIMITED`: đã quá 30 yêu cầu/phút/người, chờ trước khi thử lại, không polling liên tục. `404`: báo kết quả không còn khả dụng hoặc lần kích hoạt không hợp lệ. `422`: dữ liệu lịch sử không phân tích được, hiện không có dự đoán, không tự kích hoạt lại skill. Lỗi mạng: báo rõ và cho GET lại. `401`: xử lý đăng nhập như các API khác. Khi gặp `409` với BE cũ, báo chưa lấy được dự đoán thay vì im lặng.
9. Chống response đến sai thứ tự: kết quả của trận cũ, tài khoản cũ hoặc lần kích hoạt trước không được đè kết quả mới.

## Bẫy Trần đã đổi luật ở BE

`bach_dang_tran_phap`: mọi quân dừng ở bẫy đầu tiên trên đường; Mã có thể đứng ở ô chân nếu dính bẫy tại chân. Không được suy diễn rằng Mã luôn phải xuất hiện ở ô đáp đã gửi.

- Khi nhận ACK/WS, vị trí và quân bị ăn phải lấy từ snapshot mới. Sự kiện `piece.moved.to` cũng trả tọa độ dừng thực tế; đích trong request chỉ là đích dự định.
- Không tự xóa quân ở đích dự định trước khi có xác nhận; nếu có animation lạc quan thì phải sửa/hoàn tác khi đích thực tế khác.
- Vẽ khóa chân tại vị trí thực tế trong `pieces[].position`, đọc `bach_dang_rooted` và `remainingTurns` từ effects.
- Không vẽ bẫy địch chưa biết, không tự tính lại ngòi Pháo hay đặc quyền Lý Thường Kiệt để ghi đè state server.
- `endReason=river_snare` là thua do dừng sớm khiến Tướng không còn an toàn. Hiển thị kết quả server, không tiếp tục cho đi.

## Test cần báo kết quả

- Bot đi trước lúc GET bắt đầu và trước lúc GET kết thúc: dự đoán vẫn hiện, có nhãn cũ khi cần.
- Dự đoán đã hiện, đối thủ đi tiếp: bảng không tự biến mất.
- Bấm tải lại không tiêu lượt; không gửi thêm command kích hoạt.
- Logout/đổi trận/undo trong lúc chờ: response cũ không xuất hiện.
- Loading, lỗi mạng, 404, 429 và danh sách rỗng có phản hồi rõ ràng.
- Pháo thường và Pháo Lý Thường Kiệt cùng dừng ở bẫy; quân ở đích dự định vẫn còn. Mã dừng ở chân đúng state, không bị client kéo về ô đáp.

Khi bàn giao, liệt kê file đã sửa, kết quả test thực tế và điểm chưa kiểm chứng. Không tự thay đổi contract API.
