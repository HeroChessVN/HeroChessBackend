# Thêm logic hero vào Hero Chess

Logic hero được nối bằng `implementationKey`; controller, WebSocket và bot không chứa nhánh theo tên hero.

## Quy trình

1. Khai báo `historical_character`, `hero_trait` và `hero` trong catalog. Mỗi trait chỉ gắn một hero: tạo trait ID/code/name riêng cho hero mới, không trỏ hai hero vào cùng trait ID. `hero_trait.parameters` chứa dữ liệu cân bằng; `implementation_key` là tên ổn định và có thể dùng chung, ví dụ `elephant.alternating_distance`. Hero chưa có trait vẫn dùng `trait_id=NULL`.
2. Viết `IMovementHandler` hoặc handler skill tương ứng và đăng ký key trong `MovementHandlerRegistry`. Handler chỉ nhận `GameState`/`PieceState`, không đọc database.
3. Sinh legal actions từ cùng `XiangqiRulesEngine` mà server và bot sử dụng. Với `special_move`, handler không tồn tại tạo 0 legal move; không fallback sang luật quân cơ bản. Trait `passive`/`active` giữ movement của class; hiệu ứng chưa triển khai không được giả lập, gọi skill chưa có trả `SKILL_NOT_IMPLEMENTED`.
4. Mọi dữ liệu thay đổi qua lượt phải nằm trong snapshot: `TraitState`, `Effects`, `PendingEffects` hoặc `SkillStates`. Thêm field mới thì tăng `StateSchemaVersion` và giữ khả năng đọc replay cũ.
5. Viết unit test cho Red/Black, biên bàn/cung/sông, quân cản, ăn quân, self-check và state JSON round-trip. Với stateful trait, test cả clone, replay snapshot và undo.
6. Bật hero trong `03_seed_dev_only.sql` bằng overlay Development (`is_enabled`, `is_test_fixture`) để test. Không đổi trạng thái phát hành trong catalog nguồn chỉ để chạy prototype.
7. Chạy Rules tests và PostgreSQL integration tests; xác nhận lineup snapshot chứa đúng hero/trait/key và bot chỉ chọn action hợp lệ.
8. Chỉ bật release khi ownership, giá, asset, mô tả và toàn bộ rule liên quan đã được chốt.

## Ví dụ hiện có

- Trần Bình Trọng và Bùi Thị Xuân có hai trait riêng nhưng cùng dùng `elephant.diagonal_range`: đi/ăn chéo 1–2 điểm trong sân nhà, dừng ở vật cản. Có thể đổi tên/mô tả từng trait độc lập. Migration 07 tách trait cũ nếu hai hero còn đang dùng chung.
- Dã Tượng dùng `elephant.alternating_distance`: đi/ăn chéo 1–4 điểm trong sân nhà, không nhảy và không lặp `lastMoveDistance`. Lần đầu không có khoảng cách cũ.
- `lastMoveDistance` nằm trong `PieceState.TraitState`, nên clone, JSON snapshot, replay và undo đều giữ đúng trạng thái.

## Phần chưa có luật

Ba fixture team skill dùng `dev.not_implemented`; `hero_active` và `team_skill` hiện trả `SKILL_NOT_IMPLEMENTED` mà không đổi version, lượt, counter hoặc charge. Khi bổ sung skill, cần chốt target schema, thứ tự effect, charge/cooldown và cách resolve đồng thời trước khi đăng ký handler.

Snapshot schema v2 giữ `TraitKind` và `TraitImplementationKey` riêng; `MovementImplementationKey` chỉ dành cho `special_move`. Các field trait nullable để đọc snapshot v1. Khi thêm passive/active, bổ sung dispatcher hiệu ứng tương ứng, không đăng ký key đó như movement override. Clone/undo giữ metadata trait cùng `TraitState`.
