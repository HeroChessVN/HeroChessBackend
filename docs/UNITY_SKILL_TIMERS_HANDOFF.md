# Unity — tích hợp CD và thời hạn kỹ năng từ Backend

## 1. Phạm vi bàn giao

Nhóm Unity tự triển khai trên nhánh/project đang sử dụng. Không cần merge các file Unity local đã được sửa thử ở máy Backend. File này là yêu cầu tích hợp; phần Backend phụ trách API, luật và dữ liệu DB.

Mục tiêu: hiển thị đúng hồi chiêu (CD), thời hạn hiệu ứng và công trình theo dữ liệu server; sau này thay số trong DB không phải sửa các số hardcode ở Unity.

API vẫn là `/api/v1`. Envelope gửi command và cách gọi skill giữ nguyên. Riêng luồng Lam Sơn có bước xác nhận `end_turn`, xem mục 6 và `UNITY_LAM_SON_HANDOFF_PROMPT.md`.

## 2. Quy ước thời gian

Đọc `state.skillTimingVersion` từ snapshot trận:

- **2:** CD và thời hạn tính bằng lượt chung tiếp theo, không tính hành động kích hoạt. Timer vừa tạo giữ nguyên sau ACK. Mỗi hành động kết thúc lượt tiếp theo của bất kỳ phe nào làm timer giảm một. Timer đã tồn tại từ trước vẫn giảm khi người chơi dùng skill khác.
- **1 hoặc thiếu field:** trận cũ, giữ kết quả server đang gửi; không áp lại luật V2 cho snapshot cũ.

Ví dụ V2, Đỏ dùng Khiên Trần Thủ Độ, thời hạn 2:

| Thời điểm | Khiên còn lại |
|---|---:|
| ACK dùng skill, tới lượt Đen | 2 |
| Đen đi xong, tới lượt Đỏ | 1 |
| Đỏ đi xong, tới lượt Đen | Hiệu ứng đã bị xóa |

Hịch 6 lượt chung tiếp theo bao phủ ba lượt của phe sử dụng. CD về 0 không cho phép dùng khi chưa tới lượt mình.

**Unity không tự tick số còn lại**, không trừ 1 sau ACK và không giảm timer theo giây/animation. Lấy snapshot mới từ HTTP/WebSocket hiện có. `version` dùng chống dữ liệu cũ; `turnIndex` mới là mốc lượt, hai giá trị không luôn tăng cùng nhau. Đồng hồ lượt dùng `deadlineAt`/`serverNow`, độc lập với CD theo lượt.

## 3. Dữ liệu cần đọc

Trước trận, dùng `GET /api/v1/catalog`. Trong trận, dùng snapshot ACK, WebSocket hoặc `GET /api/v1/matches/{matchId}/state`.

Các đường `state.*` dưới đây nằm trong board state: ở response GET là `response.state`, ở ACK command là `response.snapshot.state`.

| Nội dung | Trước trận: catalog | Trong trận: cấu hình đã chốt |
|---|---|---|
| CD Command/Faction skill | `teamSkills[].cooldownTurns` | `state.skillStates.red/black[].cooldownTurns` |
| Thời hạn Command/Faction | `teamSkills[].parameters.durationTurns` | `state.skillStates.red/black[].parameters.durationTurns` |
| Khóa chân bẫy Trần | `teamSkills[].parameters.rootDurationTurns` | `state.skillStates.red/black[].parameters.rootDurationTurns` |
| CD Hero trait | `heroes[].trait.parameters.cooldownTurns` | `state.pieces[].traitParameters.cooldownTurns` |
| Thời hạn Hero trait | `heroes[].trait.parameters.durationTurns` | `state.pieces[].traitParameters.durationTurns` |

Số **đang còn** để hiển thị:

| Loại | Field trong board state |
|---|---|
| CD Command/Faction | `skillStates.red/black[].cooldownRemaining` |
| CD Quang Trung | `pieces[].traitState.quangTrungCooldownRemaining` |
| CD Lê Lai, Trần Thủ Độ, THĐ hệ Tượng | `pieces[].traitState.heroCooldownRemaining` |
| Khiên, khóa chân, Hịch trên quân | `pieces[].effects[].remainingTurns` |
| Thành, Rào, Cọc công khai | `obstacles[].remainingLifetime` |
| EffectInstance nếu giao diện đang hỗ trợ | `effectInstances[].remainingDuration` |
| Bẫy Trần của người gọi | `max(0, riverSnares[].expiresAt - turnIndex)` |

Tên mã hiệu ứng: Khiên `shield`, khóa chân `bach_dang_rooted`, buff Tốt `hich_xuat_quan`. Khi hiệu ứng/công trình không còn trong snapshot thì gỡ biểu tượng và phần hiển thị tương ứng. Không tạo lại hiệu ứng chỉ vì catalog còn định nghĩa nó.

Các field nullable/thiếu phải đọc an toàn. Thiếu cấu hình thì ghi “Chưa có thông số” hoặc ẩn nhãn cấu hình, không tự hiểu là CD 0/sẵn sàng. Skill tức thời không có `durationTurns` thì không hiển thị thời hạn giả. Hành động hợp lệ cuối cùng vẫn do BE xác nhận.

**Trong trận luôn ưu tiên dữ liệu đã chốt trong state.** Tải catalog mới không được thay thông số của trận đang chơi, replay hoặc trận cũ sau reconnect. Các field `createdAtTurn`/`cooldownStartedAtTurn` là dữ liệu nội bộ hỗ trợ timer; Unity không cần tính lại từ chúng.

Ví dụ minh họa một slot Command skill V2, các số không được hardcode:

```json
{
  "slotNo": 1,
  "implementationKey": "khien",
  "cooldownTurns": 13,
  "cooldownRemaining": 13,
  "parameters": { "durationTurns": 4 }
}
```

Sau ACK này có thể hiện “Hồi chiêu: 13 lượt” trên nút và “Hiệu lực: 4 lượt chung tiếp theo” trong chi tiết. Hiệu lực **thực tế còn lại** trên quân lấy từ effect, không lấy lại số 4 trong parameters.

## 4. Công việc UI

1. Bổ sung đọc `skillTimingVersion`, `cooldownTurns`, `parameters`, `traitParameters` nếu DTO/parser chưa có. Giữ các field cũ và xử lý field mới bị thiếu.
2. Tách hai nhãn **Hồi chiêu** và **Hiệu lực**. Bỏ số cố định trong tooltip và logic tự trừ một CD sau khi kích hoạt.
3. Nút Command/Faction hiển thị `cooldownRemaining`; khóa nút khi còn CD, hết lượt dùng hoặc không đúng lượt/trạng thái. Hero skill dùng trait state tương ứng; không suy ra luật chỉ bằng tên tiếng Việt.
4. Bấm quân: hiện Khiên, khóa chân, buff Hịch và số lượt còn. Bấm Thành/Rào/Cọc công khai: hiện thời hạn còn lại. Đây là hiển thị state, không phải lệnh mới.
5. Nhận response lỗi: giữ bàn theo state server, không tự tiêu lượt/CD. Retry cùng hành động giữ `commandId`; ACK duplicate không chạy animation hoặc giảm số lần hai.
6. Reconnect/Undo/replay: thay bằng snapshot tương ứng, không tự cộng/trừ bù. Giữ cơ chế bỏ response cũ theo version và đúng ngữ cảnh trận.
7. Giữ bí mật bẫy Trần: chỉ hiển thị bẫy server trả cho người gọi. Không tạo marker bẫy địch từ suy đoán, log hoặc forecast. Khi trúng bẫy, dùng vị trí và effect sau ACK, không tự đưa quân đến đích request.

Các vị trí nên tìm trong project Unity: parser/DTO board state, UI Command skill, chi tiết Hero trait, chi tiết hiệu ứng/công trình và luồng Lam Sơn. Nếu vẫn dùng cấu trúc demo hiện tại, tìm `SkillCooldown`, `DurationCopy`, `HeroChessOnlineDemo.CommandSkills.cs`, `HeroChessOnlineDemo.HeroSkills.cs`; nhóm tự sửa trên code của mình, không cần chép cả file từ máy BE.

## 5. Bảng đối chiếu sau SQL 23

Đây là mức cân bằng ban đầu để test, **không phải hằng số dùng trong Unity**:

| Kỹ năng | Implementation key | CD | Thời hạn |
|---|---|---:|---|
| Thành | `thanh` | 13 | 6 |
| Rào | `rao` | 9 | 4 |
| Khiên Command | `khien` | 13 | 4 |
| Bạch Đằng Trận Pháp | `bach_dang_tran_phap` | 7 | Bẫy 6; khóa chân 4 |
| Lam Sơn Quân Lược | `lam_son_quan_luoc` | 15 | Review dùng deadline lượt gốc |
| Hịch Xuất Quân | `hich_xuat_quan` | 11 | 6 |
| Hành Quân Thần Tốc — Quang Trung | `quang_trung.special_move` | 7 | Tức thời |
| Bạch Đằng Giang — THĐ hệ Tượng | `tran_hung_dao_tuong.coc` | 5 | Cọc 3 |
| Lê Lai cứu chúa | `le_lai.swap_general` | 7 | Tức thời |
| Trấn Quốc Hộ Quân | `tran_thu_do.general_shield` | 5 | Khiên 2 |

## 6. Lam Sơn: một lượt dù có hai ACK

1. Dùng skill: ACK bắt đầu CD/trừ lượt dùng, giữ phe đi và deadline gốc; `state.forecastReview` xuất hiện. Khóa hành động khác, trừ đầu hàng và xác nhận kết thúc review.
2. Lấy `forecastReview.activationSequence` để gọi `GET /api/v1/matches/{id}/forecast?version={activationSequence}`. Mở bàn mô phỏng riêng; không áp nước dự báo vào bàn thật.
3. Nút **Đi** gửi command mới với `action: { "type": "end_turn" }`, `commandId` mới và `expectedVersion` hiện tại. Server chuyển lượt và xóa review; timeout cũng tự chốt.
4. V2: CD vừa tạo còn nguyên sau cả ACK cast và ACK chốt. Timer đã có trước đó chỉ giảm một lần lúc chốt; không tick theo hai lần tăng version. Nếu chưa nhận ACK chốt thì không tự đổi phe.

Luồng hiển thị dự báo đầy đủ nằm trong `UNITY_LAM_SON_HANDOFF_PROMPT.md` và `api.md`.

## 7. Checklist nghiệm thu của nhóm Unity

- [ ] Khiên Command V2: sau dùng còn CD 13, hiệu lực 4; đối phương đi xong còn 12/3.
- [ ] Khiên Trần Thủ Độ: sau dùng CD 5/hiệu lực 2; lượt địch xong còn 4/1; lượt mình tiếp theo xong thì Khiên biến mất.
- [ ] Hịch 6: có đủ ba lượt mình tận dụng buff; sau đó biểu tượng mất theo state.
- [ ] Bẫy Trần: chỉ thấy bẫy mình, khóa chân đọc đúng 4 ngay sau lần trúng; vị trí quân lấy từ ACK.
- [ ] Lam Sơn: xem dự báo không chuyển lượt; Đi/timeout chỉ chốt một lượt, không tự giảm CD hai lần.
- [ ] Red/Black, retry/ACK duplicate, HTTP/WebSocket không làm số nhảy hai lần.
- [ ] Reconnect, Undo, replay giữ đúng số của snapshot; trận V1 vẫn hiển thị được.
- [ ] Trên môi trường test, BE đổi một giá trị DB rồi tạo trận mới: Unity hiện số mới mà không sửa hardcode; trận cũ vẫn giữ số cũ.

## 8. Phối hợp phát hành

BE deploy bản tương thích V1/V2 trước. Nhóm Unity hoàn thiện và kiểm tra client đọc metadata theo file này; sau đó BE mới áp thủ công SQL 23 để mở timing V2 cho trận mới. SQL 22 của luồng Lam Sơn là prerequisite riêng trước API tương ứng. Nhóm Unity không chạy SQL và không cần đưa Unity lên VM Azure.

Nếu hiện chỉ test bằng Unity Editor, nhóm mở project đã sửa và Play. Bản game đã phát cho người chơi cần nhóm Unity build/phát hành bản client tương ứng. Không yêu cầu người phụ trách BE merge hoặc build project Unity.
