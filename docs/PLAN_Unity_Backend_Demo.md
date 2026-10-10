# Kế hoạch nối Unity với Hero Chess Backend để demo

## Kết luận sau khi đọc hai dự án

**Làm được, nhưng Unity hiện là prototype offline, chưa phải client của backend.** Có thể giữ nguyên scene, bàn cờ 3D, camera và animation. Phần phải thay là nguồn dữ liệu và nơi xử lý luật: Unity gửi ý định của người chơi, backend kiểm tra và trả state; Unity chỉ hiển thị state đó.

Đích ngắn hạn phù hợp khi thời gian ít: **đăng nhập thật → chọn đội hình đã lưu trên server → vào trận bot → đi quân → dùng skill cơ bản → kết thúc trận** trong Unity Editor hoặc Windows build. Sau khi luồng này ổn mới thêm PvP và trình sửa đội hình đầy đủ. Không cần thêm API backend mới cho luồng trên.

### Bằng chứng và khoảng cách hiện tại

| Thành phần Unity | Hiện trạng đã đọc | Việc cần làm |
|---|---|---|
| `LoginController.cs` | Chỉ kiểm tra ô nhập, lưu tên vào `PlayerPrefs`; Guest cũng vào sảnh | Gọi Identity `/api/v1/auth/login?useCookies=false`, giữ bearer token trong phiên, gọi `/api/v1/me`; ẩn/khóa Guest vì backend chưa hỗ trợ luồng này |
| `MatchmakingController.cs` | Đếm 5 giây rồi chuyển scene, không có đối thủ thật | Tạo ticket `mode=bot` hoặc `ranked`, poll ticket đến khi có `matchId`; hủy ticket khi rời hàng đợi |
| `MatchFoundController.cs`, `MockMatchSession.cs` | Đội đối thủ giả, tự xác nhận, mở bàn cờ | Lấy lineup thật từ `/lineups`, gửi chọn lineup + revision, confirm, đợi selection chuyển `active` |
| `FormationData.cs`, `FormationRepository.cs`, `FormationHeroCatalog.asset`, `CommandSkillCatalog` | Đội hình local `PlayerPrefs`, **12 slot** (chỉ 1 Tốt), ID hero/skill giả | Backend yêu cầu **16 slot** (5 Tốt), UUID catalog/lineup thật, SP và quyền sở hữu do server kiểm tra; local save không POST trực tiếp được |
| `XiangqiBoardDirector.cs`, `XiangqiRules.cs` | 32 quân mô hình đã đặt trong scene, luật và đồng hồ chạy trong Unity | Dùng snapshot + legal actions từ backend; không gọi `XiangqiRules.TryMove` hay đồng hồ local trong trận online |
| `XiangqiPieceActor.cs`, `XiangqiBoardBuilder.cs` | Có di chuyển/đánh/animation, chuyển tọa độ bàn cờ `(x,y)` sang thế giới | Giữ để render, gắn quân theo `pieceId`, cập nhật từ state server |
| `MatchReport.cs`, `MatchResultScreen.cs` | Kết quả do luật local tạo | Lấy `result`, `endReason`, trạng thái hoàn tất từ server; scene kết quả chỉ trình bày |

Unity dùng `6000.3.24f1`. `BoardScene` hiện trỏ `characterPrefab` tới `Starter Assets/.../PlayerArmature.prefab`: đã có **model nhân vật có rig dùng chung**. Một số FBX hero riêng có trong `Assets/HeroChess/Art/Models/Characters`, nhưng chưa cần dùng để vào trận. Không chặn trận chỉ vì thiếu model riêng: chọn prefab theo `hero.assetKey`/`hero.code` nếu đã cấu hình, nếu không thì dùng `PlayerArmature` như hiện tại, giữ màu phe và nhãn tên/class của hero.

## Ranh giới dữ liệu và cách kết nối

`Unity (UnityWebRequest, bearer token) → HeroChess.Api → PostgreSQL/Supabase`. Unity **không kết nối DB trực tiếp**. JSON server dùng camelCase, enum dạng chuỗi (`red`, `black`, `alive`, `rook`...). `JsonUtility` hiện chỉ dùng cho save local; response state có dictionary và cấu trúc lồng sâu, nên dùng package `com.unity.nuget.newtonsoft-json` đã xuất hiện trong `packages-lock.json` (kiểm tra/promote thành dependency trực tiếp nếu Unity không resolve) để đọc DTO. Tránh parser viết tay.

Demo trên **cùng máy**: chạy API profile `http`, base URL `http://127.0.0.1:5012`; Unity Editor/Windows build gọi URL đó. Nếu chạy Unity trên máy khác, đổi sang IP LAN của máy chạy API và bind API vào LAN; `localhost` trên máy thứ hai sẽ trỏ sai máy. Không dùng HTTPS localhost cho lần ghép đầu vì chứng chỉ dev có thể không được Unity tin. Chỉ một API process trong demo vì ticket matchmaking được giữ trong RAM process đó. Cấu hình kết nối DB vẫn ở backend, không đưa chuỗi Supabase/mật khẩu vào Unity.

### API cần dùng theo thứ tự

1. `POST /api/v1/auth/login?useCookies=false` với `{email,password}` → `accessToken`; sau đó `GET /api/v1/me`, `GET /api/v1/catalog`, `GET /api/v1/lineups`. `GET /api/v1/matches?limit=20` để khôi phục trận `selecting`/`active` khi mở lại game.
2. `POST /api/v1/matchmaking/tickets` với `{mode:"bot"}` (sau đó mới `ranked`) → ticket. Nếu chưa có `matchId`, `GET /api/v1/matchmaking/tickets/{ticketId}` mỗi 1–2 giây. `DELETE` chỉ khi ticket còn `waiting`.
3. `GET /api/v1/matches/{matchId}/selection`; `PUT` cùng đường dẫn với `{lineupId,expectedRevision}`; `POST /api/v1/matches/{matchId}/confirm`. Chỉ mở BoardScene khi status `active`.
4. `GET /api/v1/matches/{matchId}/state` và `/legal-actions`. Chọn quân bằng `pieceId`; đi bằng `POST /api/v1/matches/{matchId}/commands` với `{commandId,expectedVersion,action:{type:"move",pieceId,to:{x,y}}}`. Sau ACK dùng `snapshot` trả về, không tự di chuyển quân trước khi server nhận.
5. Skill đội dùng action `{type:"team_skill",slot,target}`. Skill hero dùng `{type:"hero_active",pieceId,skillCode,target}`; Quang Trung lấy ô hợp lệ từ `/hero-actions?pieceId=...`. Target của `thanh`/`rao` là `{position:{x,y}}`, `khien` là `{pieceId}`; Cọc chọn ô sông và gửi `{position:{x,y}}`. Lấy CD/số lượt tồn tại và cả skill đối phương từ `state`, không đếm cục bộ.
6. `resign` cũng đi qua `/commands`. Kết thúc và lý do lấy từ state. Replay/lịch sử có API sẵn nhưng không bắt buộc cho luồng demo đầu tiên.

Lấy đúng `ownSide` từ selection, không giả định người chơi luôn đỏ (bot hiện tạo người chơi đỏ, PvP hai bên khác nhau). Dùng `version` để bỏ snapshot cũ. Khi lỗi 409/stale version hoặc mất mạng, tải state mới rồi cho người chơi thao tác lại. Khi request chưa có ACK, khóa nút gửi và giữ nguyên `commandId` nếu cần thử lại, tránh đi hai nước. Đồng hồ tính từ `deadlineAt` và `serverNow`; Unity không tự xử hết giờ.

## Các task triển khai theo thứ tự, tối đa 2 ngày cho bản demo

| Ưu tiên | Task và chỗ sửa trong Unity | Điều kiện xong |
|---|---|---|
| D1-1 | Tạo `HeroChessApi` nhỏ dùng `UnityWebRequest`, cấu hình base URL một nơi, deserialize response/lỗi, thêm bearer token; tạo session giữ token, `matchId` và ticket khi chuyển scene. Đổi `LoginController` sang login thật; hiện lỗi API, bỏ đường Guest giả. | Login sai ở lại scene và báo lỗi; login đúng `/me` hiện đúng tài khoản; không lưu mật khẩu vào `PlayerPrefs`. |
| D1-2 | Sảnh lấy `/me`, `/catalog`, `/lineups`, `/matches`; bỏ chỉ số vàng/level giả; nút “Tiếp tục trận” từ history. `MatchmakingController` dùng ticket thật, không còn timer 5 giây. | Bấm Bot có `matchId` thật; tắt/mở scene vẫn tìm lại trận; API lỗi thì không tự mở bàn cờ. |
| D1-3 | `MatchFoundController` hiển thị tối đa 3 lineup **server đã lưu và `isValid=true`**, dùng `id`/`revision` thật, gửi selection/confirm và đợi `active`. Không hiển thị lineup đối thủ giả; chỉ dùng public skill info mà backend cho xem. | Từ sảnh vào `BoardScene` với bot và state `active`. Nếu chưa có lineup hợp lệ, báo rõ và mở đường chuẩn bị đội hình. |
| D1-4 | Để kịp demo: dùng **một tài khoản đã có lineup 16 slot trên backend** (có thể tạo trước bằng React API Test hiện có). Không chuyển 12 slot local thành 16 bằng cách tự thêm 4 quân giả. Nếu bắt buộc tạo ngay trong Unity, bổ sung màn “đội hình nhanh” lấy `catalog.slots`, `heroes.isOwned`, chọn 16 UUID và 3 skill rồi `POST /lineups`; đây là task riêng, cần thêm thời gian. | Có ít nhất một lineup server hợp lệ trước khi bấm Bot; Unity không dùng local fake IDs. |
| D2-1 | Tách đường online của `XiangqiBoardDirector`: giữ camera, raycast, `SquareWorld`, hint và actor animation; lấy `pieces` từ snapshot, map `pieceId → XiangqiPieceActor`, cập nhật vị trí/sống-chết/màu/tên theo server. Khi khởi tạo, bind 32 actor scene theo phe + tọa độ khởi đầu, không suy ra hero từ tên GameObject. | Hai bên hiển thị đúng 32 quân và hero; quân bị ăn biến mất; refresh không tạo trùng actor. |
| D2-2 | Chọn quân chỉ từ phe mình trong đúng lượt; tô `/legal-actions`; click đích gửi `move` với `expectedVersion`. Cập nhật từ ACK và poll `/state` khoảng 1 giây (poll cả selection); tránh chạy luật, clock, restart/flip-phe, tự xử hòa/đầu hàng của offline mode trong online. Nút xoay chỉ xoay camera/bàn, không đổi `ownSide`. | Chơi liên tiếp vài nước với bot; bot đi thì Unity tự cập nhật; hết giờ/kết thúc hiện đúng server state. |
| D2-3 | UI nhỏ cạnh HUD cho 3 command skill, CD của cả hai bên; khi chọn hero hiện tên/trait, nút hero active nếu backend hỗ trợ. Chọn target theo loại skill; dùng `/hero-actions` cho skill di chuyển; hiện `obstacles`, khiên/effects từ snapshot. **Không vẽ Cọc ẩn của đối thủ** và không hiển thị thông báo làm lộ ô ẩn. | Dùng ít nhất một command skill và một hero skill trên tài khoản demo; skill/CD/hiệu ứng cập nhật sau action. |
| D2-4 | Chuyển kết quả server sang `MatchReport`/`MatchResultScene`; nút resign gửi API. Smoke test Unity Editor/Windows build với API + DB thật: login → bot → chọn đội → 3 nước mỗi bên → 1 skill → resign/kết quả → quay sảnh/tiếp tục trận. | Không có exception Unity Console; trạng thái trận trên Unity khớp `/state`/React; không có nước nào tự hợp lệ theo Unity mà backend từ chối. |

**Nếu còn thời gian:** dùng `ranked` ticket cho hai tài khoản, vào cùng match, poll selection/state ở cả hai cửa sổ. WebSocket `/api/v1/ws-ticket` + `/ws/v1` có sẵn và có thể giảm độ trễ; thêm sau khi đường REST chạy chắc. Trình biên tập đội hình Unity 16 slot đầy đủ, mua hero, replay, skin, animation skill riêng cũng để sau bản demo bot. Không làm luật chơi/bot thứ hai ở Unity.

## Rủi ro cần xử lý trước buổi demo

- **Dữ liệu đội hình:** Unity local 12 slot không tương thích 16 slot backend. Chuẩn bị tài khoản đã có lineup hợp lệ trên server và đăng nhập chính tài khoản đó. Nếu DB Supabase khác DB backend đang dùng, Unity sẽ không thấy lineup; kiểm tra `/me`, `/lineups` trên đúng API trước demo.
- **Độ ổn định kết nối:** ticket RAM mất khi restart API; sau khi khởi động lại client phải bỏ ticket cũ và đọc `/matches` để tìm trận còn mở. Không chạy 2 instance API phía trước cùng một DB cho demo PvP.
- **Model:** `PlayerArmature` đã là fallback dùng chung. Hero nào có FBX riêng cũng chỉ gắn khi prefab/animator đã kiểm tra hoạt động; thiếu model riêng vẫn có actor, tên hero và class.
- **Trạng thái kiểm chứng (10/10/2026):** đã mở Unity Editor qua Unity CLI, compile script online thành công và chạy Play Mode với API giả localhost: login → sảnh → ghép bot → nhận state → gắn 32 actor → gửi một nước đi → nhận snapshot mới. Chưa chạy một trận bot với backend và Supabase thật; xem `Unity_Backend_Demo_Run.md` để làm bước cuối trước khi trình thầy.

## Cập nhật sau khi khôi phục UI gốc (10/10/2026)

Đã bỏ lớp menu IMGUI, dùng controller của các scene gốc cho API, mở rộng đội hình online lên 16 ô, nối 15 model + avatar humanoid. Giữ dữ liệu offline riêng. Xem `Unity_Backend_Demo_Run.md` để biết thao tác và phạm vi đã kiểm chứng; các mục D2 chưa test DB thật không được coi là đã nghiệm thu.
