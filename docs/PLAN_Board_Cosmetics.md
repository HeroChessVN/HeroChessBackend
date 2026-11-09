# Plan tích hợp trang phục bàn cờ

Đối chiếu source ngày 09/11/2026 tại commit `0c6012b`.

## 1. Hiện trạng đã đọc

| Thành phần | Hành vi hiện có |
|---|---|
| Unity `BoardCosmeticCatalog.cs` | Bốn bàn, bốn bộ đế quân. `Select()` lưu lựa chọn bằng PlayerPrefs. |
| Unity `HeroShopScreen.ShowCosmetics/ChooseCosmetic` | Hiện tất cả mẫu local và nút CHỌN. Chưa gọi mua hoặc trang bị qua API. |
| Unity `BoardCosmeticLibraryView.Show/Rebuild` | Thư viện mẫu local, lựa chọn dùng chung catalog và PlayerPrefs với shop. |
| Unity `XiangqiBoardDirector.InstallBlenderBoard` | Đọc bàn đang chọn, tải model từ Resources, căn lại grid và tắt collider của model. |
| BE `ShopService.PurchaseHeroAsync` | Khóa ví, kiểm tra giá và sở hữu, chống xử lý lặp bằng UUID, lưu ví và ledger trong transaction. |
| BE `cosmetic/player_cosmetic` | Skin gắn hero. LineupEntry và FrozenPiece có CosmeticId, validator kiểm tra skin thuộc đúng hero và player. |
| BE `AdminService` | Đổi giá hero hoặc cosmetic và ghi audit. |

Các file Unity nằm dưới `Assets/HeroChess/Scripts/Runtime`. Các model đã có dưới `Assets/HeroChess/Resources/Board`.

## 2. Phạm vi và quyết định sản phẩm

- Đưa bốn mẫu bàn vào catalog BE, quyền mua và trang bị đồng bộ theo tài khoản.
- Bản mặc định mở sẵn cho mọi Player. Đã chốt giá ba bản còn lại là 200 gold mỗi mẫu, tương ứng coin_price=200.
- Đã chốt mỗi người nhìn toàn bộ bàn theo mẫu mình chọn. Hai client có thể hiển thị hai mẫu khác nhau trong cùng trận.
- Lựa chọn mới áp dụng khi mở bàn của trận tiếp theo. Mẫu đang hiển thị được giữ trong suốt phiên xem trận.
- Cosmetic mới tập trung vào bàn. Tab Bộ quân cần được team Unity ẩn nếu chốt bỏ phạm vi đế quân.
- Bản plan này dành cho BE và hợp đồng bàn giao Unity. Chỉnh sửa Unity là bước tích hợp của team FE.

| code khớp Unity | Tên hiện tại | modelResource | previewResource |
|---|---|---|---|
| mac-dinh | Bàn cờ mặc định | Board/XiangqiBoardHalves | UI/Cosmetics/BoardMacDinh |
| son-mai | Sơn mai | Board/XiangqiBoardSkinSonMai | UI/Cosmetics/BoardSonMai |
| son-son | Sơn son | Board/XiangqiBoardSkinSonSon | UI/Cosmetics/BoardSonSon |
| su-thanh | Sứ thanh | Board/XiangqiBoardSkinSuThanh | UI/Cosmetics/BoardSuThanh |

Giữ các mã asset đúng source. Tên hiển thị có thể đổi trên catalog sau khi chốt cách đặt tên.

## 3. Dữ liệu và migration

Thêm các bảng sau để chuyển sang tính năng bàn cờ.

- `board_cosmetic` có UUID, code unique, name, coin_price không âm, asset_key, preview_key, is_enabled.
- `player_board_cosmetic` có player_id, board_cosmetic_id, acquired_at và idempotency_key. Khóa chính gồm player và mẫu. UUID mua hàng unique theo player, tương tự player_hero.
- `player_board_loadout` có player_id làm khóa chính và board_cosmetic_id đang trang bị. FK kiểm tra danh tính Player theo pattern schema hiện hành.

Bàn mặc định là quyền do server cấp sẵn theo code, kể cả tài khoản cũ. Nếu chưa có loadout thì server trả mẫu mặc định. Tránh ghi ownership mặc định hàng loạt khi đọc catalog.

Giữ dữ liệu skin hero cũ trong giai đoạn chuyển đổi vì lineup, cosmetic ownership và giao dịch còn tham chiếu chúng. API mới nhận loại board rõ ràng và sử dụng các bảng mới. Chỉ dọn bảng cũ sau khi kiểm kê dữ liệu, caller và phiên bản client còn dùng.

Mở rộng `coin_transaction` với `board_cosmetic_id` nullable và loại `board_cosmetic_purchase`. Sửa CHECK hiện hành để giao dịch mua bàn có amount âm, đúng board ID, các trường hero/cosmetic/match rỗng. Các nhánh giao dịch cũ phải yêu cầu board_cosmetic_id rỗng. Giữ nguyên dữ liệu giao dịch cũ.

Migration 17 chạy trong transaction và chạy lại được. Seed khớp code, giữ ID và giá đã được quản trị. Cả bốn mẫu bật khi cài, ba mẫu trả phí có giá khởi tạo 200 gold.

Migration mới cần được copy vào output qua csproj và có thứ tự rõ trong bootstrap. Production hiện tắt bootstrap, nên phải cung cấp SQL chạy thủ công và bước xác nhận schema trước khi deploy API có truy vấn bảng mới.

## 4. Hợp đồng API dự kiến

Tất cả route có prefix `/api/v1`.

| Method và route | Nội dung |
|---|---|
| GET /board-cosmetics | Catalog board đang bật cho Player, kèm isOwned và selectedCode |
| POST /shop/board-cosmetics/{id}/purchase | Mua mẫu với header Idempotency-Key là UUID |
| PUT /me/board-cosmetic | Body `{"code":"son-son"}`, trả code server xác nhận cùng metadata mẫu |
| PATCH /admin/board-cosmetics/{id}/price | Giá chuỗi số nguyên không âm và reason, ghi audit |

Item catalog có `id, code, name, coinPrice, assetKey, previewKey, isOwned`. Coin dùng chuỗi như API mua hero. Quyền trang bị và mua áp dụng policy Player, đổi giá dùng policy Admin.

Catalog trả selectedCode hợp lệ. Bản đã chọn bị tắt thì effective selection trả mac-dinh. Kết quả mua có mẫu đã sở hữu, balance và charged, tương tự PurchaseHeroDto. Mua xong người chơi chọn TRANG BỊ riêng.

Giữ profile update hiện có cho tên/avatar/frame. Endpoint trang bị riêng tránh PUT bàn cờ ghi đè hồ sơ khi hai request tới cùng lúc.

## 5. Tính nhất quán khi mua và trang bị

Tái dùng trình tự transaction của ShopService, không viết hệ thống ví thứ hai.

1. Validate UUID, khóa wallet và kiểm tra idempotency trước khi kiểm tra giá hoặc trạng thái catalog hiện tại.
2. Dùng cùng quy ước key trên các luồng mua hero và board. Key đã dùng cho loại hoặc mặt hàng khác phải trả 409, gồm cả giao dịch miễn phí.
3. Kiểm tra mẫu đang bật, ownership và giá phía server. Khóa bản ghi catalog khi xác nhận giá để đồng bộ với thao tác đổi giá admin.
4. Ghi ownership, ví và ledger trong cùng transaction. Mẫu giá 0 vẫn có ownership/key nhưng không thêm ledger amount 0.
5. Cùng mẫu mua bằng hai key đồng thời chỉ một request thành công, số dư chỉ trừ một lần. Retry key cũ trả charged gốc.

Trang bị chỉ nhận mẫu đã sở hữu đang bật hoặc mac-dinh. Khi chuyển từ chọn local cũ sang BE, PlayerPrefs không phải bằng chứng sở hữu. Tài khoản cũ được về mac-dinh cho tới khi mua hoặc được cấp mẫu hợp lệ.

## 6. Bàn giao cho Unity

1. Sau login hoặc vào shop, GET board-cosmetics và đối chiếu theo code với bốn model đã có.
2. HeroShopScreen hiển thị MUA, TRANG BỊ hoặc ĐANG TRANG BỊ theo response. Lưu UUID mua cho tới khi lệnh được xác nhận, retry dùng cùng UUID.
3. BoardCosmeticLibraryView dùng cùng catalog và quyền sở hữu như shop. Hai màn cùng cập nhật sau khi BE chấp nhận.
4. BoardCosmeticCatalog.Selected nhận lựa chọn đã đồng bộ của tài khoản. Có thể dùng cache PlayerPrefs theo userId như pattern PlayerPortraits. Đổi tài khoản phải xóa trạng thái in-memory và nạp lựa chọn của tài khoản mới.
5. Chỉ cập nhật lựa chọn sau response trang bị thành công. API lỗi thì giữ lựa chọn trước đó và cho thử lại.
6. XiangqiBoardDirector giữ luồng tải Resources và fallback hiện có. Mẫu mới không thay đổi tọa độ hitbox hoặc ô legal của server. Giữ snapshot lựa chọn lúc khởi tạo bàn để thay cache giữa trận không tái tạo model.
7. Client cũ thiếu model hoặc nhận code lạ dùng mac-dinh. Mất kết nối khi chưa đồng bộ được catalog thì vẫn vào bàn với model mặc định.

Để thuận tiện mở rộng, dùng code mẫu bàn làm khóa ổn định giữa BE và Unity. BoardCosmeticCatalog giải quyết code thành model, XiangqiBoardDirector phụ trách lắp và căn bàn. Đặt lựa chọn tại lớp hiển thị của bàn, tránh gắn vào từng quân hoặc logic luật.

Nếu sau này chuyển sang mỗi phe một nửa bàn, bổ sung board code của từng participant vào snapshot khi bắt đầu trận. Unity dùng cùng catalog để dựng hai nửa theo phe Đỏ và Đen, kể cả khi xoay bàn. Giữ lựa chọn đã chốt khi reconnect hoặc replay. Bot dùng mac-dinh nếu chưa có lựa chọn riêng. Cách mở rộng này dùng lại bảng sở hữu, API mua, API trang bị và code asset của giai đoạn đầu. Chỉ bổ sung trường snapshot khi thực hiện chế độ hai nửa, sau khi kiểm tra pivot và khả năng tách từng model.

## 7. Kiểm thử bắt buộc

- Migration trên DB mới và DB có cosmetic hero, ownership, lineup và ledger cũ. Chạy lại migration vẫn giữ giá, ID và liên kết.
- Catalog tài khoản mới/cũ có mac-dinh. Account Admin bị chặn luồng gameplay.
- Mua đủ coin, thiếu coin, giá 0, mẫu tắt, UUID sai và cùng key khác loại mặt hàng.
- Hai request mua đồng thời và retry sau khi mất response, kiểm tra balance/ownership/ledger.
- Trang bị mẫu chưa sở hữu bị từ chối. Đổi thiết bị và đổi tài khoản lấy đúng lựa chọn.
- Admin đổi giá cùng lúc có người mua, audit lưu đúng trước/sau.
- Client cũ thiếu model vẫn chơi được. Hai phía dùng bốn mẫu đều chọn đúng ô cờ, quân và vị trí hiệu ứng.
- Giá, lựa chọn bàn và quyền sở hữu không làm thay đổi SP, luật, Elo hoặc kết quả trận.

## 8. Thứ tự thực hiện

1. Đã chốt mỗi người thấy bàn mình chọn và giá ba mẫu trả phí là 200 gold mỗi mẫu.
2. BE thêm migration, mapping, DTO, catalog và trang bị.
3. Nối mua bằng ShopService và đổi giá bằng AdminService, thêm test PostgreSQL cho tiền và quyền.
4. Chạy migration trên DB test, build image và xác nhận health. Sau đó triển khai migration production và backend.
5. Team Unity nối hai màn chọn bàn, shop và đồng bộ login theo tài liệu API.
6. Xác nhận hai client, đổi tài khoản và lỗi mạng rồi mới bật bán ba mẫu.

Các deliverable triển khai gồm một migration, API board cosmetics, test giao dịch và quyền sở hữu, cùng mục bàn giao trong docs/api.md.
