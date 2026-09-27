# HERO CHESS — ERD vật lý prototype

> Schema mới ngày 24/09: chạy migration 06_user_inheritance.sql sau baseline 01/04/05 và trước seed 03. Bảng player + identity.app_user được hợp nhất thành user_account; auth_identity.user_id và admin_audit_log.actor_user_id thay tên cũ. Sơ đồ/dictionary dưới đây là baseline lịch sử. Xem [mô hình hiện tại](/D:/Code/Github/HeroChessBackend/docs/User_Player_Admin_Refactor.md).

Tên entity dưới đây trùng tên bảng trong schema `hero_chess`. PK/FK và đầy đủ cột nằm trong SQL/data dictionary. Sơ đồ chia ba nhóm để đọc thuận tiện. Đường quan hệ chỉ biểu diễn FK thật; snapshot JSON không tạo FK tới catalog/lineup.

## 1. Nội dung và đội hình

```mermaid
erDiagram
    historical_character o|--o{ hero : identifies
    chess_class ||--o{ hero : classifies
    hero_trait o|--o{ hero : defines
    hero ||--o{ hero_faction : joins
    faction ||--o{ hero_faction : includes
    faction o|--o| team_skill : provides
    chess_class ||--o{ lineup_slot : defines
    player ||--o{ lineup : creates
    ruleset ||--o{ lineup : validates_against
    lineup ||--o{ lineup_entry : contains
    lineup_slot ||--o{ lineup_entry : assigns
    hero ||--o{ lineup_entry : selects
    cosmetic o|--o{ lineup_entry : decorates
    lineup ||--o{ lineup_skill : equips
    team_skill ||--o{ lineup_skill : selects
```

`lineup_entry` có composite FK để class hero đúng class slot. Đủ 16 entry và đúng ba skill là invariant của API lưu lineup, nên cardinality vật lý vẫn 0..n. Một faction có tối đa một skill; muốn bắt buộc có đúng một khi phát hành, backend kiểm tra catalog.

## 2. Tài khoản, tài sản và coin

```mermaid
erDiagram
    player ||--o{ auth_identity : authenticates_as
    player ||--o| player_wallet : holds
    player ||--o| player_rating : has
    player ||--o{ player_hero : owns
    hero ||--o{ player_hero : unlocks
    hero ||--o{ cosmetic : supports
    player ||--o{ player_cosmetic : owns
    cosmetic ||--o{ player_cosmetic : unlocks
    player_wallet ||--o{ coin_transaction : records
    hero o|--o{ coin_transaction : purchased_in
    cosmetic o|--o{ coin_transaction : purchased_in
    match_participant o|--o{ coin_transaction : rewards
    player ||--o{ admin_audit_log : performs
```

Tạo player + wallet + rating trong cùng transaction khi onboarding. DB cho phép chưa có wallet/rating trước khi onboarding xong. Ledger reward tham chiếu đúng player/side thuộc trận bằng composite FK.

## 3. Trận và replay

```mermaid
erDiagram
    ruleset ||--o{ game_match : governs
    game_match ||--o{ match_participant : includes
    player o|--o{ match_participant : participates
    game_match ||--o| match_state : stores_current
    game_match ||--o{ match_action : records
    match_participant o|--o{ match_action : acts
    match_participant o|--o{ coin_transaction : earns

    game_match {
        uuid id PK
        string mode
        string status
        uuid ruleset_id FK
        jsonb ruleset_snapshot
        string content_version
        string result
        timestamptz settled_at
    }
    match_participant {
        uuid match_id PK,FK
        string side PK
        uuid player_id FK
        string participant_type
        uuid source_lineup_id
        jsonb lineup_snapshot
    }
    match_state {
        uuid match_id PK,FK
        int version
        jsonb state
        timestamptz turn_deadline_at
    }
    match_action {
        uuid id PK
        uuid match_id FK
        int sequence_no
        uuid command_id
        string kind
        jsonb resolved_events
        jsonb state_after
    }
```

Trận selecting có thể chưa đủ hai participant; active/completed bắt buộc đủ hai. Bot participant không có playerId. Ranked không cho bot/guest. `source_lineup_id` chỉ là dấu vết nguồn, không có FK vì lineup có thể bị xóa sau trận. Replay là dữ liệu match_action, không có bảng replay trùng lặp.
