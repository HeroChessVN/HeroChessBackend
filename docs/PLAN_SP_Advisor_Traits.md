# Kế hoạch chuẩn hóa Setup Point và bổ sung trait Sĩ

Ngày lập và cập nhật: 19/11/2026. Trạng thái: BE, patch SQL và docs đã triển khai; đã gộp commit `7fb83fb` từ master. Sau khi gộp, 422 test Rules, 158 test integration trên PostgreSQL tạm và 33 test frontend đều pass. Đã sửa trước đó ba description team skill trên Supabase theo yêu cầu. Người dùng đã yêu cầu commit/push lên master; workflow deploy chạy theo push. Patch SP/trait mới chưa áp lên production; Unity chưa sửa hoặc kiểm thử.

Patch dữ liệu: `HeroChess.Api/Data/Bootstrap/19_hero_sp_and_advisor_traits.sql`, chạy thủ công sau khi deploy BE và tích hợp client tương ứng. File được copy vào output để kiểm thử/phát hành, không được đăng ký chạy tự động trong bootstrap. Hướng dẫn Unity: `docs/UNITY_ADVISOR_TRAITS_HANDOFF.md`. Hai bản Hero & Skills và hai bản Bot Algorithms đã cập nhật; 11 đoạn lịch sử có sẵn ở mỗi bản Hero & Skills giữ nguyên.

Phạm vi gồm chuẩn hóa giá SP theo class và sức ảnh hưởng của trait; thêm hai skill chủ động cho Lê Lai, Trần Thủ Độ và hai trait di chuyển cho Nguyễn Nhạc, Nguyễn Lữ hệ Sĩ. Dùng lại pipeline trận, snapshot, hiệu ứng và bot hiện có. Không tạo service riêng hoặc thêm dependency.

## 1. Quyết định và giả định

Người dùng đã chốt tên bốn trait, Trần Thủ Độ tồn tại **2 lượt chung/CD 6**, Lê Lai **CD 8**, chỉ target class Tướng. Lê Lai không còn bị giới hạn khoảng cách một ô: Tướng còn trong cung thì được xét đổi chỗ. Hai lượt chung nghĩa là mỗi bên đi tính một lượt, không phải hai lượt của đối thủ.

| Hero hệ Sĩ | Tên trait chính thức | Cơ chế | SP | Hồi chiêu |
|---|---|---|---:|---:|
| Lê Lai | Lê Lai cứu chúa | Đổi vị trí với Tướng đồng minh còn trong cung nhà, không giới hạn cự ly trong cung | 2 | 8 lượt chung |
| Trần Thủ Độ | Trấn Quốc Hộ Quân | Tạo Khiên cho Tướng đồng minh, tồn tại 2 lượt chung | 2 | 6 lượt chung |
| Nguyễn Nhạc | Thái Đức Linh Bộ | Đi một ô theo tám hướng trong cung nhà | 2 | Không có, là di chuyển thường |
| Nguyễn Lữ | Đông Định Linh Bộ | Đi một ô theo tám hướng trong cung nhà | 2 | Không có, là di chuyển thường |

Tên người dùng cung cấp “Nguyễn Lữ — Đông Định Linh Bộ” được tách thành hero Nguyễn Lữ và trait Đông Định Linh Bộ; khi trình bày có thể ghép lại. Giới hạn tám hướng một ô trong cung của hai Sĩ Tây Sơn giữ theo phương án plan trước. Những điều kiện an toàn, khóa chân và không cộng dồn ở mục 5 là đặc tả triển khai, không bỏ qua vì đã bỏ giới hạn cự ly Lê Lai.

Giữ ngân sách 50 SP, đủ 16 slot theo class, tối đa một faction skill trong ba Command Skill và ngưỡng 5 hero cùng phe ngoài Tốt. Không đổi Elo, coin, quyền sở hữu hero hoặc luật các trait cũ chỉ vì đợt cân bằng này. Tăng SP của bốn Sĩ đi cùng thời điểm phát hành trait tương ứng, không thu thêm điểm khi quân chưa có khả năng mới.

## 2. Căn cứ từ dữ liệu và code hiện tại

Đọc trực tiếp Supabase HeroChessVN ngày 19/11/2026, chỉ dùng SELECT:

- 47 hero đang bật; cả ba phe có đủ class để xây đội hình 16 quân. Năm Tốt không được tính vào ngưỡng faction skill.
- Giá class: Tướng 0, Sĩ 1, Tượng 2, Mã 4, Xe 7, Pháo 6, Tốt 1. Đội cơ bản là **45 SP**, còn 5 SP cho lựa chọn nâng cấp.
- Lê Khôi và Lê Văn Hưng đang 1 SP; Bùi Thị Xuân và Trần Bình Trọng đang 2; bốn Tượng mạnh đang 3. Đây là nhóm cần chuẩn hóa.
- Lý Thường Kiệt Pháo hiện đã 7 trên Supabase. Không giả định vẫn là 6; script cũ và DOCX còn ghi 6 cần được đồng bộ.
- Bốn Sĩ định bổ sung đang 1 SP và chưa có trait.
- Có 46 đội hình đã lưu. Tính riêng ngân sách: 1 đội đã vượt; 15 đội sẽ vượt sau thay đổi, tức **14 đội mới bị vượt SP**. Đây không phải khẳng định 14 đội đang hợp lệ ở mọi tiêu chí.
- 20 đội hình chứa hero đang bị tắt; đây là vấn đề có sẵn, có thể chồng lấp nhóm trên. Không tự bật dev-slot hay tự sửa các đội này trong đợt cân bằng.
- Các hero đang bật đều có `is_starter=true` tại thời điểm kiểm tra. Tài khoản mới theo cấu hình hiện tại có đủ lựa chọn; tài khoản cũ vẫn phải kiểm tra ownership riêng, không suy ra từ cờ starter.

Các điểm tái sử dụng:

- `LineupValidator`: kiểm tra đủ quân, class, ownership, trùng nhân vật, SP và điều kiện faction.
- `LineupService`: tính lại `totalSp`, `budget`, lỗi khi đọc đội hình cũ; không cần cột tổng SP mới.
- `MatchSelectionService.Freeze`: đã lưu giá SP và trait/parameters vào trận.
- `CatalogVersionService`: đã đưa SP và cấu hình trait vào fingerprint, có `CONTENT_VERSION_CHANGED` khi confirm nội dung cũ.
- `MatchTurnRules.ApplyHeroSkill`, `HeroCandidates`, `BotEngine.Simulate`: các đường thực thi và mô phỏng dùng chung.
- `EffectState`, `TurnLifecycle`, `traitState.heroCooldownRemaining`: đủ lưu Khiên và hồi chiêu, chưa cần đổi schema state.
- Một dòng `hero_trait` chỉ thuộc một hero. Nguyễn Nhạc và Nguyễn Lữ cần hai dòng trait riêng, có thể dùng chung `implementation_key` và movement handler.

## 3. Bảng giá mục tiêu

SP là giá xây đội hình. Phụ phí được gán theo tác động thực tế, không tự tính bằng số lượng dòng trait.

| Hero hoặc nhóm | Giá hiện tại | Giá mục tiêu |
|---|---:|---:|
| Lê Lai — Sĩ | 1 | 2 |
| Trần Thủ Độ — Sĩ | 1 | 2 |
| Nguyễn Nhạc — Sĩ | 1 | 2 |
| Nguyễn Lữ — Sĩ | 1 | 2 |
| Sĩ thường khác | 1 | 1 |
| Lê Khôi — Tượng | 1 | 2 |
| Lê Văn Hưng — Tượng | 1 | 2 |
| Phạm Văn Xảo — Tượng | 2 | 2 |
| Bùi Thị Xuân — Tượng | 2 | 3 |
| Trần Bình Trọng — Tượng | 2 | 3 |
| Dã Tượng — Tượng | 3 | 4 |
| Trần Hưng Đạo — Tượng | 3 | 4 |
| Trưng Trắc — Tượng | 3 | 4 |
| Trưng Nhị — Tượng | 3 | 4 |
| Lý Thường Kiệt — Pháo | 7 | 7 |
| Phạm Ngũ Lão, Lý Thường Kiệt — Xe | 8 | 8 |
| Lê Lợi, Trần Hưng Đạo — Tướng | 1 | 1 |
| Quang Trung — Tướng | 2 | 2 |
| Tướng thường | 0 | 0 |
| Mã / Pháo / Xe thường / Tốt | 4 / 6 / 7 / 1 | Giữ nguyên |

Giữ UUID, character_id, faction, asset, giá coin, starter và trạng thái bật/tắt hiện tại. Không xóa rồi tạo lại hero để thay giá.

## 4. Đội hình mẫu và khả năng lắp trong 50 SP

| Thành phần | Trần | Hậu Lê | Tây Sơn |
|---|---|---|---|
| Tướng | Trần Thánh Tông: 0 | Lê Lợi: 1 | Quang Trung: 2 |
| Sĩ | Trần Thủ Độ 2 + Trần Nhật Duật 1 | Lê Lai 2 + Nguyễn Trãi 1 | Nguyễn Nhạc 2 + Nguyễn Lữ 2 |
| Tượng | Dã Tượng 4 + Trần Hưng Đạo 4 | Lê Khôi 2 + Phạm Văn Xảo 2 | Lê Văn Hưng 2 + Bùi Thị Xuân 3 |
| Mã | Trần Quang Khải 4 + Trần Quốc Toản 4 | Lê Sát 4 + Lưu Nhân Chú 4 | Lý Văn Bưu 4 + Võ Đình Tú 4 |
| Xe | Phạm Ngũ Lão 8 + Trần Khánh Dư 7 | Đinh Liệt 7 + Trần Nguyên Hãn 7 | Võ Văn Dũng 7 + Trần Quang Diệu 7 |
| Pháo | Trần Khát Chân 6 + Yết Kiêu 6 | Đinh Lễ 6 + Nguyễn Xí 6 | Nguyễn Văn Tuyết 6 + Nguyễn Văn Lộc 6 |
| Năm Tốt | 5 | 5 | 5 |
| Tổng | **51** | **47** | **50** |

Ba phương án hợp lệ ở đúng 50 SP:

1. Trần: thay Dã Tượng 4 bằng Trần Bình Trọng 3 trong bảng trên. Giữ 11 hero Trần ngoài Tốt.
2. Hậu Lê: thay Đinh Liệt 7 bằng Lý Thường Kiệt Xe 8 và Phạm Văn Xảo 2 bằng Trưng Trắc 4. Còn 9 hero Hậu Lê ngoài Tốt, đủ ngưỡng 5.
3. Tây Sơn: giữ nguyên đội mẫu. Quang Trung làm Tướng, hai anh em làm Sĩ; không vi phạm trùng danh tính.

Không cho dùng Trần Hưng Đạo Tướng cùng Trần Hưng Đạo Tượng, hoặc Nguyễn Nhạc/Nguyễn Lữ cùng lúc ở hai class. Không ép mọi đội thuần phe phải dùng hết ngân sách. Tổng hợp lệ chỉ chứng minh lắp được, chưa chứng minh các phe có tỷ lệ thắng ngang nhau.

## 5. Luật bốn trait mới

### 5.1 Lê Lai cứu chúa của Lê Lai

Key đề xuất: `le_lai.swap_general`, kind `active`.

- Người dùng điều khiển Lê Lai Sĩ còn sống, đang trên bàn, đúng lượt và skill sẵn sàng.
- Target bắt buộc là đúng class `GENERAL`, cùng phe và còn sống. Tướng còn trong cung nhà thì có thể đổi với Lê Lai ở mọi vị trí của cung, kể cả hai ô cách xa nhau hoặc có quân/công trình ở giữa; không yêu cầu kề nhau hay đường đi trống.
- Lê Lai cũng phải đang ở trong cung nhà để Tướng không bị chuyển ra ngoài cung; đây là vị trí bình thường của Sĩ. Không dùng skill để cứu Tướng đã ra khỏi cung. Hai vị trí phải khác nhau.
- Đổi hai tọa độ trong một thao tác nguyên tử. Lê Lai đứng tại ô cũ của Tướng; đây là ngoại lệ vị trí do skill tạo ra. Sau đó Lê Lai vẫn đi theo luật Sĩ thường, không được thêm di chuyển tám hướng.
- Skill là hành động di chuyển: nếu Lê Lai hoặc Tướng bị khóa chân thì không được đổi. Không ăn quân, không loại Lê Lai, không tạo quân và không kích hoạt hiệu ứng sau ăn quân.
- Giữ nguyên pieceId, hiệu ứng và cooldown riêng của cả hai quân. Khiên đi theo quân, không nằm lại tại ô. Không reset special move của Quang Trung hoặc trait khác trên Tướng được đổi.
- Mô phỏng sau đổi rồi kiểm tra an toàn Tướng theo luật trận. Nếu vẫn chịu chiếu thì từ chối, không tiêu lượt hoặc cooldown.
- Hợp lệ tiêu một hành động, tăng version/turnIndex/count một lần và chuyển lượt một lần. Cooldown 8 lượt chung, lưu `cooldownTurns=8` từ parameters đã đóng băng.
- Phát `hero_skill.activated` có tên hero/skill và hai sự kiện `piece.moved` với vị trí thực tế, dùng snapshot làm nguồn trạng thái cuối. Không phát sự kiện bắt quân.

### 5.2 Trấn Quốc Hộ Quân của Trần Thủ Độ

Key đề xuất: `tran_thu_do.general_shield`, kind `active`. Giá 2 SP, tồn tại 2 lượt chung và cooldown 6 lượt chung theo quyết định mới nhất.

- Người dùng điều khiển Trần Thủ Độ Sĩ còn sống và đúng lượt, cooldown bằng 0.
- Chỉ target class `GENERAL` đồng minh còn sống, không yêu cầu Tướng còn trong cung; không tự chuyển sang quân khác nếu target sai. Không thêm giới hạn cự ly cho skill không di chuyển này.
- Sĩ bị khóa chân vẫn được dùng vì khóa chân hiện giới hạn di chuyển. Không biến khóa chân thành câm lặng toàn bộ skill.
- Tướng đã có Khiên còn hiệu lực thì từ chối. Dùng cùng mã `shield` để không cộng lớp, kéo dài hoặc ghi đè Khiên của Command Skill.
- Hai lượt chung tính cả lượt kích hoạt, theo quy ước hiện có của Command Khiên/Thành/Rào. Lúc áp dụng đặt `durationTurns=2`; sau ACK chuyển sang lượt địch còn 1; khi lượt mình trở lại về 0 và xóa. Như vậy bảo vệ trong lượt địch kế tiếp, không qua hai lượt địch. Địch hết giờ mất lượt cũng được tính. Không đếm bằng giây.
- Tái sử dụng `EffectState("shield", sourcePieceId, 2)` và lifecycle hiện có, không thêm timer thứ hai hoặc cộng bù một lượt sau ACK. Snapshot hiển thị số còn lại thực tế, không hứa ACK trả 2. Parameters đóng băng là `{"durationTurns":2,"cooldownTurns":6}`.
- Ghi `SourcePieceId` là Trần Thủ Độ. Nếu nguồn bị ăn trong lượt địch, hiệu ứng vẫn hết theo timer đã xác định, không tự kéo dài hay thêm lifecycle phụ.
- Giữ tính chất bảo vệ của Khiên hiện tại, bao gồm cách engine xét chiếu khi Tướng có Khiên. Không sửa ngầm luật Command Khiên trong đợt này. Test rõ khi hết Khiên mà Tướng đang bị khống chế: phải giải chiếu hoặc được xử lý kết thúc theo luật thường.
- Cooldown 6 lượt chung, độc lập CD Command Khiên. Không cho cộng dồn khi cùng tồn tại; có thể dùng Command Khiên ở một lượt sau khi Khiên cũ hết và Command đã sẵn sàng, theo luật hiện hành.
- Phát `hero_skill.activated` và `shield.applied`; thời gian còn lại đọc từ snapshot `pieces[].effects`.

### 5.3 Nguyễn Nhạc và Nguyễn Lữ hệ Sĩ

Thái Đức Linh Bộ của Nguyễn Nhạc và Đông Định Linh Bộ của Nguyễn Lữ là hai trait kind `special_move`, hai dòng DB riêng, dùng chung key đề xuất `advisor.king_step`.

- Đi đúng một ô theo tám hướng trong cung nhà; không ra khỏi cung, không vượt sông hoặc nhảy nhiều ô.
- Đây là thay đổi di chuyển thường: gửi `action.type=move`, không có nút kích hoạt hoặc cooldown.
- Chặn ô đồng minh, Khiên địch, vật cản và tự chiếu theo luật chung; khóa chân chặn di chuyển như quân khác.
- Tận dụng handler tám hướng trong cung của Lê Lợi nếu kiểm tra code xác nhận không phụ thuộc class General. Không sao chép thành hai handler cho hai hero.
- Chỉ gắn lên bản ADVISOR. Bản GENERAL của Nguyễn Nhạc và Nguyễn Lữ giữ nguyên 0 SP và không được nhận trait này.

### 5.4 Quy ước timer và cấu hình

Một lượt chung là một bên thực hiện hành động hoặc mất lượt do hết giờ. Giá trị cooldown khởi tạo được lifecycle giảm khi chuyển lượt, giống hero hiện tại: Lê Lai cấu hình 8 có thể trả 7 ngay sau ACK; Trần Thủ Độ cấu hình 6 có thể trả 5. Thời gian Khiên 2 có thể trả 1 ngay sau ACK vì lượt kích hoạt đã được tính. UI đọc số BE trả, không tự bù thêm.

CD và duration đọc từ `TraitParameters` đã đóng băng. Thiếu/sai kiểu/ngoài giới hạn cấu hình trả lỗi có kiểm soát lúc freeze và lúc thực thi; không mặc định thành miễn hồi chiêu. Phạm vi trong cung của Lê Lai, đơn vị lượt chung và một bước trong cung của hai Sĩ Tây Sơn là luật đã định nghĩa trong plan, không hứa mọi chỉnh sửa JSON tùy ý đều có hiệu lực.

### 5.5 Description và cooldown của team skill

Đã thực hiện trực tiếp trên Supabase ngày 19/11/2026, chỉ sửa `description` của ba dòng `thanh`, `rao`, `khien`: bỏ cụm CD ghi cũ. Đã SELECT lại xác nhận cooldown thực tế không đổi:

| Skill | `cooldown_turns` giữ nguyên | Description sau sửa |
|---|---:|---|
| Thành | 14 | Tạo 1 Thành ở vị trí bất kỳ bên sông mình; tồn tại 6 lượt chung. Pháo phá Thành và tiến vào ô đó; Thành không làm ngòi. |
| Rào | 10 | Tạo 1 Rào ở vị trí bất kỳ bên sông mình; tồn tại 4 lượt chung. Pháo có thể dùng Rào làm ngòi. |
| Khiên | 14 | Tạo 1 Khiên cho quân đồng minh bất kỳ trên bàn cờ; tồn tại 4 lượt chung. Quân có Khiên không thể bị ăn. |

Đây là Command Khiên, khác Hero Skill Trấn Quốc Hộ Quân 2/CD 6. Không đổi duration Command Khiên 4 thành 2. Client lấy CD gốc từ `teamSkills[].cooldownTurns`, trong trận lấy CD còn lại từ `skillStates[side][].cooldownRemaining`; không parse số trong description. Description không chứa số CD để lần chỉnh cột CD tiếp theo không làm nội dung mâu thuẫn.

Việc sửa DB mô tả chưa sửa nguồn bootstrap: `08_step6_skills.sql` vẫn upsert cooldown 8/6/8 và description cũ; comment `ThanhHandler`/`RaoHandler` cũng còn số cũ. Đưa sửa tận gốc vào giai đoạn code: seed mới dùng mặc định 14/10/14, không ghi đè cooldown quản trị trên dòng đã tồn tại và không khôi phục description sai. Không sửa số cooldown đang đóng băng trong trận cũ.

## 6. Thay đổi BE và các caller phải theo cùng

| Vị trí | Công việc |
|---|---|
| `HeroChess.Rules/Skills/SkillKeys.cs` | Bổ sung ba key; đặt parser cấu hình tối thiểu ở helper luật mới, tránh đọc JSON trùng ở các caller |
| `HeroChess.Rules/MovementHandlers.cs` | Đăng ký key Sĩ tám hướng, tái sử dụng handler phù hợp |
| Helper thuần trong `HeroChess.Rules/Skills` | Hai phép áp dụng skill Sĩ: kiểm tra, clone, đổi vị trí/tạo Khiên; không DB/HTTP, không tự advance lượt |
| `MatchTurnRules.ApplyHeroSkill` | Nhận hai key chủ động mới, gọi helper và dùng chung advance/AfterAction, tránh tick hai lần |
| `MatchTurnRules.HeroCandidates` | Sinh tối đa một target Tướng cho mỗi Sĩ chủ động; dùng chung cho bot và phát hiện còn hành động hợp lệ |
| `MatchCommandService.ValidateAction` | Giữ kiểm tra JSON chung; kiểm tra `target.pieceId` đúng kiểu cho hai key mới, không để lỗi JSON thành 500 |
| `MatchSelectionService.Freeze` | Kiểm tra parameters mới; lưu toàn bộ cấu hình vào snapshot |
| `BotEngine.Simulate`, `Choose`, `Forecast` | Dùng đường thực thi chung; cả ba cấp bot và Lam Sơn đều nhận được các hành động mới |
| `FindLegalSkillAction` / `MatchTurnRules.Finish` | Cứu chiếu bằng skill hợp lệ không bị kết luận chiếu hết sớm; không dùng tập ứng viên đã cắt của bot để quyết định kết thúc |
| `MatchStateProjection` / `MatchHistoryService` | Đảm bảo metadata/effect/event mới ra đúng người xem, replay và undo giữ đúng snapshot |

Nguyên tắc root cause: hai skill mới chỉ có một đường xử lý luật dùng chung cho command thật, bot và forecast. Không viết một bản cho HTTP và một bản cho mô phỏng. Không tăng độ sâu bot, thời gian tìm kiếm hoặc thêm machine learning trong đợt này.

SP mới tự tác động đến chấm vật chất của bot và so SP quân còn sống khi đạt giới hạn hành động. Giữ công thức hiện tại, thêm regression và mô tả đúng tác động; không lén thêm cột giá trị chiến đấu hoặc đổi luật phân thắng thua.

## 7. API và bàn giao Unity

Giữ `/api/v1`, `/catalog`, endpoint command, WS và shape snapshot hiện có. Không cần endpoint riêng để mua điểm hoặc đọc SP khi bấm quân.

Ví dụ action Lê Lai, nằm trong envelope command hiện tại với `commandId` và `expectedVersion`:

```json
{
  "type": "hero_active",
  "pieceId": "<pieceId Lê Lai>",
  "skillCode": "le_lai.swap_general",
  "target": { "pieceId": "<pieceId Tướng đồng minh>" }
}
```

Trần Thủ Độ dùng cùng shape, đổi `pieceId` nguồn và `skillCode` thành `tran_thu_do.general_shield`. Tên key ở đây là đề xuất kỹ thuật, chốt vào docs khi triển khai.

- Hai target đều là Tướng đồng minh duy nhất. Giai đoạn này không cần endpoint chọn mục tiêu mới; Unity lấy pieceId từ snapshot, gửi ý định và xử lý lỗi BE. Không tuyên bố nút bật nghĩa là server chắc chắn chấp nhận; tự chiếu và điều kiện cuối cùng do BE kiểm tra.
- `/hero-actions` hiện dành cho nước đặc biệt Quang Trung, giữ shape cũ. Không nhét đổi chỗ vào `LegalMoveDto` rồi khiến client hiểu là đi ăn Tướng mình.
- Hai Sĩ Tây Sơn dùng `/legal-actions` và command `move` như thường. Unity không tự suy ra tám hướng từ class Sĩ hoặc tự mô phỏng luật thay BE.
- Màn xếp quân đọc `heroes[].setupPoints`, `ruleset.setupBudget`; đọc lại catalog khi đăng nhập hoặc gặp lỗi nội dung thay đổi. Không giữ bảng giá hardcode.
- Đội hình vượt SP vẫn hiện để sửa, ghi tổng và số điểm vượt. Chặn xác nhận theo kết quả validation BE. Không tự xóa đội hình hoặc thay hero.
- Nút skill chủ động đọc `traitKind`, key, tên, parameters và `traitState.heroCooldownRemaining`. Đọc trạng thái đang sống, lượt và quyền điều khiển từ snapshot. Hiển thị đúng Trấn Quốc Hộ Quân, Lê Lai cứu chúa, Thái Đức Linh Bộ và Đông Định Linh Bộ; không nhận dạng luật bằng tên hiển thị.
- Đổi chỗ cập nhật hai quân cùng một snapshot; không chạy animation bắt quân hoặc nhân bản model. Khiên ngắn hiện icon và số lượt từ effect thực tế. Reconnect/undo khôi phục theo snapshot, không theo timer local.
- ACK trùng commandId không được phát animation hoặc trừ lượt hai lần. Có lỗi 400/409/422 thì hiện thông báo rõ, không bỏ qua rồi tự sửa bàn local.

Đầu ra bàn giao khi code xong: cập nhật `docs/api.md` và một prompt tích hợp Unity ngắn có JSON, key, cooldown/effect, thứ tự triển khai và ca test. Unity chưa nằm trong thay đổi code BE tự động của plan này.

## 8. Patch DB, seed và dữ liệu cũ

Không cần bảng mới hoặc đổi schema cho các thông tin trên. Cần patch dữ liệu có kiểm soát:

1. Preflight SELECT: xác nhận roster, trait hiện gắn, giá trước/sau; số đội vượt SP, đủ quân theo class và hero starter. Chạy lại ngay trước rollout vì số liệu 46/14/20 chỉ đúng thời điểm kiểm tra.
2. Xuất bản sao các dòng sẽ đổi: hero id/code/setup_points/trait_id và các dòng trait liên quan. Không xuất credential hoặc toàn bộ dữ liệu tài khoản.
3. Viết một script áp dụng thủ công trong transaction, xác định bằng code và xác minh UUID/character đúng. Upsert bốn trait riêng, gắn đúng ADVISOR; update SP theo bảng mục tiêu. Nếu hero đang có trait ngoài dự kiến thì dừng và báo, không ghi đè âm thầm.
4. Giữ giá và attach trait của mỗi Sĩ trong cùng transaction. Chạy lại không sinh thêm trait, không sửa ownership/faction/coin/asset.
5. Không tự xóa hoặc thay entries của đội hình cũ. Dùng validation hiện tại cho người chơi sửa. Không sửa SP trong match_participant, match_state hoặc match_action đã đóng băng.
6. Seed cài mới phải có cùng giá/cấu hình. Đọc hết đường bootstrap: `02_seed_catalog.sql` hiện có thể ghi lại `trait_id` từ seed, gồm NULL ở Sĩ; phải bảo đảm restart/bootstrap không làm mất bốn trait vừa gắn.
7. Đồng bộ giá trong script `14_hero_names_and_setup_points.sql` và mô tả cũ ghi Pháo 6 SP ở seed/`09_hero_movement_fixes.sql`. Sửa nguồn `08_step6_skills.sql` theo mục 5.5: không tự trả CD về 8/6/8 hoặc tái ghi description sai sau restart. Không thêm lệnh reset giá mỗi lần API chạy; bootstrap không được hoàn tác thay đổi catalog quản trị.
8. Kiểm tra các entrypoint cài mới, gồm script tổng nếu có dữ liệu nhúng, để không có hai bộ giá khác nhau. Giữ báo cáo kiểm chứng lịch sử cũ như lịch sử, không sửa số test quá khứ thành kết quả mới.
9. Postflight SELECT đối chiếu tất cả mục tiêu, unique trait_id, class và faction; chạy báo cáo đội hình vượt ngân sách lần nữa.

Ngoài ba description đã sửa theo yêu cầu tại mục 5.5, không áp patch SP/trait production trong bước cập nhật plan. Script rollout thủ công tách khỏi deploy; production đang tắt bootstrap thì deploy code không tự thêm trait hoặc đổi SP.

## 9. Kiểm thử bắt buộc

| Nhóm | Kiểm chứng |
|---|---|
| Giá và đội hình | Ba ví dụ 50 SP ở mục 4 pass; đội Trần 51 bị từ chối; 16 slot và trùng nhân vật được kiểm tra |
| Migration | Chạy hai lần không trùng trait; không thay UUID/ownership; gặp dữ liệu ngoài dự kiến dừng; restart bootstrap không reset trait/giá/CD; CD tùy chỉnh vẫn giữ, description không tái chứa CD cũ |
| Snapshot | Catalog đổi giữa select/confirm báo content changed; trận đã active giữ SP/trait cũ; replay không đọc giá mới thay giá cũ |
| Lê Lai hợp lệ | Red/Black, các vị trí xa nhau trong cung và có quân/vật cản ở giữa vẫn đổi được; đúng hai quân đổi tọa độ; version/count/cooldown 8 chỉ tăng/tick một lần |
| Lê Lai bị chặn | Tướng ngoài cung, nguồn ngoài cung, sai phe/class/target, quân chết, nguồn/đích bị khóa chân, còn cooldown, đổi xong vẫn tự chiếu |
| Tương tác đổi chỗ | Hiệu ứng đi theo pieceId, Khiên giữ timer; không reset cooldown Tướng; không sinh ăn quân; không sửa input state khi mô phỏng |
| Trấn Quốc Hộ Quân | Duration 2/CD 6 lúc áp dụng; sau ACK còn 1/CD 5; sau địch đi hoặc hết giờ sang lượt mình hết Khiên; chỉ target GENERAL đồng minh, trong/ngoài cung đều được; giống nhau cho Red/Black |
| Chồng Khiên | Command Khiên đang có thì Sĩ bị từ chối; Khiên Sĩ đang có thì Command Khiên bị từ chối; request lỗi không tiêu lượt/CD |
| Hết bảo hộ | Sau khi hết Khiên Tướng chịu chiếu thì phải giải; kỹ năng cứu hợp lệ được xét trước khi kết luận mate; nguồn bị ăn không làm timer tick sai |
| Sĩ tám hướng | Tám hướng một ô trong cả hai cung; không đi quá một ô/ra cung; bị chặn bởi đồng minh, vật cản, Khiên địch, khóa chân và tự chiếu |
| Bot và Lam Sơn | Sinh/áp dụng được skill Sĩ; mô phỏng không sửa state thật; giữ ngân sách cũ; không bỏ sót hành động cứu chiếu vì top-K |
| API | JSON lỗi trả 400, sai quyền/lượt/target trả lỗi quy ước hiện tại, expectedVersion cũ 409; retry commandId không dùng skill hai lần |
| Undo, reconnect, replay | Đúng vị trí cả hai quân, hiệu ứng, cooldown và SP snapshot; nguồn sự kiện đủ cho UI cập nhật |
| UI tích hợp | SP từ catalog; đội cũ vượt điểm sửa được; bốn hero hiển thị đúng trait; animation đổi chỗ và icon Khiên cập nhật theo snapshot |

Dùng framework kiểm thử hiện có và PostgreSQL tạm cho ca DB; không tạo tài khoản/trận thử ghi vào Supabase production. Chạy full Rules và nhóm integration liên quan. Đo thêm so với baseline bot/forecast chỉ khi thay đổi làm tăng chi phí hoặc có dấu hiệu timeout; không suy ra tải Azure từ test local.

## 10. Docs cần cập nhật sau khi triển khai

- `docs/defense/01_Hero_and_Skills_VI.docx` và `_EN.docx`: bảng SP, các mức phụ phí, ví dụ đội hình, hai skill chủ động và hai di chuyển đặc biệt; giữ phần lịch sử không bị thay đổi, bổ sung nguồn lịch sử cho nội dung mới sau khi xác minh.
- `docs/defense/03_Bot_Algorithms_VI.docx` và `_EN.docx`: giữ công thức; bổ sung ứng viên hai skill Sĩ, ảnh hưởng SP mới và ví dụ số nào thực sự không còn đúng. Ví dụ Mã thường 4/Pháo thường 6 vẫn đúng.
- Hai file Elo & Coin: không cần sửa khi công thức/reward không đổi.
- `docs/api.md`: bảng key, action JSON, dữ liệu CD/effect, snapshot và xử lý catalog/lineup cũ. Không ghi kỹ năng là đã có trước khi code/test xong.
- Docs seed/README có giá hiện hành phải đồng bộ. Không viết lại báo cáo test cũ hoặc thư mục `docs/defense - Copy` của người dùng.
- DOCX sửa tối thiểu, giữ nội dung người dùng đã thêm; render và kiểm tra bản Anh/Việt trước khi giao.

## 11. Thứ tự triển khai và rollout

**Bước 1 — khóa đặc tả:** dùng bốn tên đã chốt, Trấn Quốc Hộ Quân 2 lượt chung/CD 6, Lê Lai cứu chúa CD 8 và không giới hạn khoảng cách trong cung. Hai active chỉ target GENERAL đồng minh. Chuẩn bị bảng giá và danh sách dữ liệu tác động; giữ Sĩ Tây Sơn tám hướng một ô trong cung theo plan.

**Bước 2 — code BE tương thích:** thêm luật và candidate/simulation, test với catalog tạm có bốn trait. Deploy hỗ trợ key trước khi gắn key vào catalog production. Không tăng giá Sĩ sớm. Không tạo nhánh mới nếu người dùng chưa yêu cầu.

**Bước 3 — Unity sẵn sàng:** tích hợp JSON và hiển thị từ metadata trên môi trường test; xác nhận đổi chỗ cả hai quân, Khiên ngắn, giá động và lỗi đội hình cũ.

**Bước 4 — phát hành dữ liệu:** chọn thời điểm ít người chơi; để các trận và lượt chọn đội hình đang dở kết thúc/thoát trước khi dừng API cho cửa sổ cập nhật. Recovery hiện tại có thể hủy trận dở khi API khởi động lại, nên không hứa deploy không ảnh hưởng trận đang chạy. Không xây thêm hệ thống maintenance chỉ cho lần này. Chạy preflight, backup dòng thay đổi, transaction patch, postflight rồi mở API.

**Bước 5 — smoke test bản phát hành:** health/ready, catalog, đội hình chuẩn 50, một skill mỗi loại, snapshot/WS và một lượt bot; test ghi chỉ trên môi trường test hoặc tài khoản/trận được cho phép riêng. Production mặc định kiểm tra đọc. Cập nhật thông báo cho người chơi về việc đội hình cũ có thể cần chỉnh SP.

**Bước 6 — cân bằng sau phát hành:** thử cùng ngân sách và đổi màu Red/Black, đội thuần lẫn đội mượn hero. Quan sát Trần giữ Tướng bằng Khiên/Cọc, Tây Sơn với hai Sĩ và buff Tốt, Hậu Lê với cứu Tướng/Lam Sơn. Dùng dữ liệu trận hiện có và checklist có kết quả; không dựng hệ telemetry mới trong đợt này. Điều chỉnh SP/CD sau quan sát, không gọi bảng giá ban đầu là cân bằng đã được chứng minh.

Rollback dữ liệu bằng bản sao chính xác trước patch, không reset toàn catalog bằng seed cũ. Nếu đã có trận dùng key mới, giữ image BE có hỗ trợ key đến khi các trận đó kết thúc; hạ image quá sớm có thể làm command/bot không hiểu snapshot. Giá và trait của trận đang chạy vẫn theo snapshot, không chỉnh ngược lịch sử để rollback.

## 12. Tiêu chí hoàn thành

- Bốn hero có đúng trait và SP; các hero khác theo bảng, budget vẫn 50.
- Ba phương án mẫu 50 SP hợp lệ; đội vượt điểm được giải thích và sửa được.
- Hai active skill, hai movement trait hoạt động trên cùng Rules cho người chơi, bot và Lam Sơn.
- Trấn Quốc Hộ Quân giá 2 SP, duration 2 lượt chung/CD 6; Khiên không stack với Command Khiên. Lê Lai CD 8, đổi được với Tướng ở mọi ô trong cung khi các điều kiện hợp lệ khác thỏa mãn.
- SP/trait đã đóng băng không bị đổi bởi catalog, undo hoặc replay; bootstrap không xóa cấu hình mới.
- Test phù hợp pass; docs và hướng dẫn Unity khớp code. Không tự commit, push, deploy hoặc sửa SP/trait production khi mới được yêu cầu viết plan. Ba description team skill là thay đổi DB riêng đã được yêu cầu và hoàn tất.

## 13. Danh sách file dự kiến khi triển khai

Tên file mới ở đây là dự kiến, kiểm tra trùng tên lúc bắt đầu code. SDK đã tự include `.cs`; SQL mới dùng thủ công nên không cần thêm vào startup hoặc csproj chỉ để auto chạy production.

### File cần thêm

| File dự kiến | Nội dung |
|---|---|
| `HeroChess.Rules/Skills/AdvisorHeroSkills.cs` | Hai active, parser parameters và kiểm tra dùng chung cho command/bot/forecast; không thêm framework skill mới |
| `HeroChess.Api/Data/Bootstrap/19_hero_sp_and_advisor_traits.sql` | Patch thủ công SP và bốn dòng trait riêng theo tên chính thức; chuẩn hóa description team skill, chạy lại an toàn |
| `tests/HeroChess.Rules.Tests/AdvisorHeroSkillsTests.cs` | Swap trong toàn cung, shield 2/CD 6, Sĩ tám hướng, sai target và tương tác hiệu ứng |
| `tests/HeroChess.IntegrationTests/AdvisorTraitFlowTests.cs` | API/DB/snapshot/bot/undo/replay và seed không reset dữ liệu |
| `docs/UNITY_ADVISOR_TRAITS_HANDOFF.md` | Prompt/API tích hợp bốn trait, metadata, CD và cập nhật SP; Unity tự triển khai theo bàn giao |

### File hiện có cần sửa

| File | Phạm vi |
|---|---|
| `HeroChess.Rules/Skills/SkillKeys.cs` | Hai active key và một movement key |
| `HeroChess.Rules/MovementHandlers.cs` | Đăng ký handler tám hướng trong cung cho Sĩ |
| `HeroChess.Api/Services/MatchTurnRules.cs` | Dispatch active, tên sự kiện, candidate và chuyển lượt đúng một lần |
| `HeroChess.Api/Services/MatchCommandService.cs` | Validate shape target mới, giữ dedup/expectedVersion và xét hành động cứu chiếu |
| `HeroChess.Api/Services/MatchSelectionService.cs` | Validate/freeze CD 8, duration 2/CD 6 và metadata tên mới |
| `Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql` | Giá cài mới, bốn trait và không reset trait_id đã cấu hình về NULL |
| `HeroChess.Api/Data/Bootstrap/08_step6_skills.sql` | Mặc định CD Thành/Rào/Khiên 14/10/14, description không nhúng CD, bỏ ghi đè CD quản trị khi conflict |
| `HeroChess.Api/Data/Bootstrap/09_hero_movement_fixes.sql` | Bỏ mô tả Pháo 6 SP cũ hoặc cập nhật theo giá 7, tránh văn bản tái mâu thuẫn |
| `HeroChess.Api/Data/Bootstrap/14_hero_names_and_setup_points.sql` | Đồng bộ bảng SP mục tiêu, không kéo Lý Thường Kiệt Pháo về 6 |
| `HeroChess.Rules/Skills/ThanhHandler.cs`, `RaoHandler.cs` | Chỉ sửa comment CD hardcode cũ, không đổi thời gian tồn tại 6/4 |
| `docs/api.md` | Action/target, tên trait, timer, CD động và hướng dẫn đội hình cũ vượt SP |
| `docs/defense/01_Hero_and_Skills_VI.docx`, `_EN.docx` | Giá, tên, luật bốn trait và ví dụ đội hình |
| `docs/defense/03_Bot_Algorithms_VI.docx`, `_EN.docx` | Ứng viên skill Sĩ và tác động SP; giữ công thức bot |

### Chỉ kiểm tra, không mặc định sửa

`TurnLifecycle.cs` đã giảm `shield` và hero cooldown; `GameModels.cs` đã đủ fields; `BotEngine.cs` đã gọi `HeroCandidates` và `ApplyHeroSkill`; `LineupValidator`, `LineupService`, `CatalogVersionService`, `MatchStateProjection` và `MatchHistoryService` đã có đường dữ liệu cần thiết. Chỉ sửa nếu test phát hiện thiếu cụ thể, không thêm field hoặc tầng trung gian để phục vụ khả năng giả định.

Kiểm tra script cài mới tổng và các test fixture liên quan; cập nhật nếu chúng nhúng giá cũ. Không sửa hai DOCX Elo/Coin, thư mục backup của người dùng hoặc project Unity trong đợt BE nếu chưa được yêu cầu triển khai Unity.
