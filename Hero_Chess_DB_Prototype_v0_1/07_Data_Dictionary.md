# HERO CHESS — Data dictionary v0.1

> Schema mới ngày 24/09: chạy migration 06_user_inheritance.sql sau baseline 01/04/05 và trước seed 03. Bảng player + identity.app_user được hợp nhất thành user_account; auth_identity.user_id và admin_audit_log.actor_user_id thay tên cũ. Sơ đồ/dictionary dưới đây là baseline lịch sử. Xem [mô hình hiện tại](/D:/Code/Github/HeroChessBackend/docs/User_Player_Admin_Refactor.md).

Sinh từ schema đã thực thi trong PostgreSQL. Khóa, CHECK và chỉ mục xem SQL; cột JSON còn được backend validate bằng DTO.

## admin_audit_log

Audit quản trị; không ghi credential/token.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| actor_player_id | uuid | NO | — |
| action | character varying | NO | — |
| entity_type | character varying | NO | — |
| entity_id | character varying | NO | — |
| before_data | jsonb | YES | — |
| after_data | jsonb | YES | — |
| reason | text | YES | — |
| created_at | timestamp with time zone | NO | now() |

## auth_identity

Liên kết identity đã xác thực.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| player_id | uuid | NO | — |
| provider | character varying | NO | — |
| subject | character varying | NO | — |
| created_at | timestamp with time zone | NO | now() |

## chess_class

Bảy class và giá cơ bản.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| code | character varying | NO | — |
| name_vi | character varying | NO | — |
| base_sp | smallint | NO | — |
| required_count | smallint | NO | — |
| base_movement_code | character varying | NO | — |

## coin_transaction

Lịch sử coin chống trùng key/thưởng; backend ghi cùng wallet.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| player_id | uuid | NO | — |
| idempotency_key | character varying | NO | — |
| kind | character varying | NO | — |
| amount | bigint | NO | — |
| balance_after | bigint | NO | — |
| match_id | uuid | YES | — |
| match_side | character varying | YES | — |
| hero_id | uuid | YES | — |
| cosmetic_id | uuid | YES | — |
| created_at | timestamp with time zone | NO | now() |

## cosmetic

Skin riêng của hero.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| code | character varying | NO | — |
| hero_id | uuid | NO | — |
| name | character varying | NO | — |
| coin_price | bigint | NO | — |
| asset_key | character varying | NO | — |
| is_enabled | boolean | NO | true |

## faction

Danh mục phe.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| code | character varying | NO | — |
| name | character varying | NO | — |
| description | text | YES | — |

## game_match

Vòng đời và kết quả trận.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| mode | character varying | NO | — |
| status | character varying | NO | 'selecting'::character varying |
| ruleset_id | uuid | NO | — |
| ruleset_snapshot | jsonb | NO | — |
| content_version | character varying | NO | — |
| result | character varying | YES | — |
| end_reason | character varying | YES | — |
| created_at | timestamp with time zone | NO | now() |
| started_at | timestamp with time zone | YES | — |
| ended_at | timestamp with time zone | YES | — |
| settled_at | timestamp with time zone | YES | — |

## hero

Phiên bản nhân vật thuộc class và tối đa một trait.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| code | character varying | NO | — |
| character_id | uuid | YES | — |
| class_code | character varying | NO | — |
| trait_id | uuid | YES | — |
| name | character varying | NO | — |
| setup_points | smallint | NO | — |
| coin_price | bigint | NO | 0 |
| is_starter | boolean | NO | false |
| is_enabled | boolean | NO | true |
| is_test_fixture | boolean | NO | false |
| asset_key | character varying | YES | — |
| description | text | YES | — |

## hero_faction

Liên kết hero với nhiều phe.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| hero_id | uuid | NO | — |
| faction_id | uuid | NO | — |

## hero_trait

Một định nghĩa trait có thể dùng chung nhiều hero.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| code | character varying | NO | — |
| name | character varying | NO | — |
| kind | character varying | NO | — |
| implementation_key | character varying | NO | — |
| parameters | jsonb | NO | '{}'::jsonb |
| description | text | YES | — |

## historical_character

Danh tính nhân vật, tách với phiên bản hero/class.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| code | character varying | NO | — |
| name | character varying | NO | — |
| description | text | YES | — |

## lineup

Đội hình đã lưu; validation aggregate ở backend.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| player_id | uuid | NO | — |
| ruleset_id | uuid | NO | — |
| name | character varying | NO | — |
| revision | integer | NO | 1 |
| created_at | timestamp with time zone | NO | now() |
| updated_at | timestamp with time zone | NO | now() |

## lineup_entry

Quân tại một slot đội hình.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| lineup_id | uuid | NO | — |
| slot_no | smallint | NO | — |
| class_code | character varying | NO | — |
| hero_id | uuid | NO | — |
| cosmetic_id | uuid | YES | — |

## lineup_skill

Ba vị trí Team Skill của lineup.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| lineup_id | uuid | NO | — |
| slot_no | smallint | NO | — |
| team_skill_id | uuid | NO | — |

## lineup_slot

16 vị trí chuẩn theo góc nhìn Red.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| slot_no | smallint | NO | — |
| class_code | character varying | NO | — |
| start_x | smallint | NO | — |
| start_y | smallint | NO | — |

## match_action

Log action được chấp nhận và snapshot replay.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| match_id | uuid | NO | — |
| sequence_no | integer | NO | — |
| command_id | uuid | NO | — |
| actor_side | character varying | YES | — |
| kind | character varying | NO | — |
| request_payload | jsonb | NO | '{}'::jsonb |
| resolved_events | jsonb | NO | '[]'::jsonb |
| state_after | jsonb | NO | — |
| state_schema_version | integer | NO | 1 |
| received_at | timestamp with time zone | NO | — |
| committed_at | timestamp with time zone | NO | now() |

## match_participant

Hai bên và bản sao lineup lịch sử.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| match_id | uuid | NO | — |
| side | character varying | NO | — |
| player_id | uuid | YES | — |
| participant_type | character varying | NO | — |
| bot_config | jsonb | YES | — |
| source_lineup_id | uuid | YES | — |
| source_lineup_revision | integer | YES | — |
| lineup_snapshot | jsonb | NO | — |
| confirmed_at | timestamp with time zone | YES | — |
| elo_before | integer | YES | — |
| elo_after | integer | YES | — |
| coin_reward | bigint | NO | 0 |
| stats | jsonb | NO | '{}'::jsonb |

## match_state

Trạng thái hiện tại dùng reconnect; kiểm soát version.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| match_id | uuid | NO | — |
| version | integer | NO | 0 |
| side_to_move | character varying | NO | — |
| turn_index | integer | NO | 0 |
| counted_actions | integer | NO | 0 |
| turn_deadline_at | timestamp with time zone | YES | — |
| state_schema_version | integer | NO | 1 |
| state | jsonb | NO | — |
| updated_at | timestamp with time zone | NO | now() |

## player

Hồ sơ player; không lưu credential.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| display_name | character varying | NO | — |
| is_guest | boolean | NO | true |
| role | character varying | NO | 'player'::character varying |
| status | character varying | NO | 'active'::character varying |
| created_at | timestamp with time zone | NO | now() |
| updated_at | timestamp with time zone | NO | now() |

## player_cosmetic

Quyền sở hữu skin.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| player_id | uuid | NO | — |
| cosmetic_id | uuid | NO | — |
| acquired_at | timestamp with time zone | NO | now() |

## player_hero

Quyền sở hữu phiên bản hero.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| player_id | uuid | NO | — |
| hero_id | uuid | NO | — |
| acquired_via | character varying | NO | — |
| acquired_at | timestamp with time zone | NO | now() |

## player_rating

Elo và thống kê Ranked hiện tại.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| player_id | uuid | NO | — |
| elo | integer | NO | — |
| games_played | integer | NO | 0 |
| wins | integer | NO | 0 |
| draws | integer | NO | 0 |
| losses | integer | NO | 0 |
| updated_at | timestamp with time zone | NO | now() |

## player_wallet

Số dư coin hiện tại.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| player_id | uuid | NO | — |
| balance | bigint | NO | 0 |
| updated_at | timestamp with time zone | NO | now() |

## ruleset

Cấu hình phát hành; snapshot đóng băng riêng cho mỗi trận.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| code | character varying | NO | — |
| name | character varying | NO | — |
| setup_budget | smallint | NO | 50 |
| turn_seconds | smallint | NO | 90 |
| action_limit | integer | NO | 150 |
| config | jsonb | NO | '{}'::jsonb |
| is_active | boolean | NO | false |
| created_at | timestamp with time zone | NO | now() |

## team_skill

Command/faction active; max_uses/cooldown NULL là chưa thiết kế.

| Cột | Kiểu | NULL | Giá trị mặc định |
|---|---|---|---|
| id | uuid | NO | gen_random_uuid() |
| code | character varying | NO | — |
| name | character varying | NO | — |
| faction_id | uuid | YES | — |
| implementation_key | character varying | NO | — |
| parameters | jsonb | NO | '{}'::jsonb |
| eligibility | jsonb | NO | '{}'::jsonb |
| max_uses | smallint | YES | — |
| cooldown_turns | smallint | YES | — |
| is_enabled | boolean | NO | false |
| is_test_fixture | boolean | NO | false |
| asset_key | character varying | YES | — |
| description | text | YES | — |

