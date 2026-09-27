# HERO CHESS — Kết quả kiểm tra

Ngày: 21/09/2026.

## Môi trường

PostgreSQL 18.3 (PGlite 0.5.8) on wasm32-unknown-emscripten, compiled by emcc (Emscripten gcc/clang-like replacement + linker emulating GNU ld) 3.1.74 (1092ec30a3fb1d46b1782ff1b4db5094d3d06ae5), 32-bit

Đây là PostgreSQL chạy qua PGlite/WebAssembly cục bộ, không phải PostgreSQL native hoặc project Supabase đang host. Chưa đo tải, concurrency nhiều connection, kết nối Npgsql, quyền trên Supabase hay gameplay C#.

## Kết quả

- Schema tạo thành công: 25 bảng.
- Catalog seed và dev seed chạy thành công; chạy lại không nhân đôi dữ liệu.
- Hai lineup dev đều có 16 slot, ba skill và 43 SP.
- File mock có 32 quân không trùng vị trí; heroId khớp DB seed.
- Transaction kiểm tra rollback thành công; không để lại match/ledger test.
- 23 kiểm tra ràng buộc dưới đây đều PASS.

| Kiểm tra | Kết quả |
|---|---|
| Active match cannot lose a participant | PASS |
| Completed match needs result and reason | PASS |
| Deleting source lineup preserves historical snapshot | PASS |
| Duplicate hero ownership | PASS |
| Duplicate log sequence | PASS |
| Duplicate match command | PASS |
| Duplicate reward with different request key | PASS |
| Duplicate team skill | PASS |
| Duplicate wallet idempotency key | PASS |
| Negative wallet | PASS |
| Participant side cannot be reassigned | PASS |
| Ranked accepts two registered humans | PASS |
| Ranked rejects bot | PASS |
| Ranked rejects guest | PASS |
| Reward must reference correct participant | PASS |
| Same player on both sides | PASS |
| Seed lineup: 16 slots / 43 SP | PASS |
| Seed lineup: 3 skill slots | PASS |
| Skill slot beyond three | PASS |
| Skin assigned to wrong hero | PASS |
| Snapshot must be JSON object | PASS |
| Two active rulesets | PASS |
| Wrong hero class in rook slot | PASS |

## Giới hạn kiểm tra

Không xác nhận rằng DB tự kiểm tra toàn bộ luật game. Đủ quân/SP/ownership/faction và JSON schema cần backend như README. Ledger/wallet atomicity, request retries, xử lý timer cạnh tranh với nước đi và settlement cần integration test sau khi backend được viết. Migration cần chạy thử trong project Supabase development trước khi dùng dữ liệu thật.
