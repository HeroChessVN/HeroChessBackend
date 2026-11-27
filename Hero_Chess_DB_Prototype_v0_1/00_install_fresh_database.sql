-- Hero Chess: fresh database install only. Do not run on an existing schema.
-- Generated from the same ordered SQL files as DatabaseBootstrapHostedService.
-- Does not include 03_seed_dev_only.sql or any credentials.

-- ===== BEGIN 01_schema.sql =====
-- Vai trò file: DDL nền cho 25 bảng hero_chess, index, constraints và trigger; touch_updated_at cập nhật timestamp, validate_match_roster kiểm tra thành phần trận.
-- HERO CHESS - PostgreSQL prototype v0.1 / 2026-09-21
-- Run once in a NEW database/schema. No DROP, no production credentials.
-- Compatible with PostgreSQL 16+; intended for PostgreSQL 18 or Supabase Postgres.
-- Backend-only schema; Unity/web test clients access the ASP.NET API, never SQL.
BEGIN;
CREATE SCHEMA hero_chess;
REVOKE ALL ON SCHEMA hero_chess FROM PUBLIC;
SET LOCAL search_path = hero_chess, pg_catalog;

CREATE TABLE player (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 display_name varchar(80) NOT NULL CHECK (length(btrim(display_name)) > 0),
 is_guest boolean NOT NULL DEFAULT true,
 role varchar(16) NOT NULL DEFAULT 'player' CHECK (role IN ('player','admin')),
 status varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active','disabled')),
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now()
);
-- Credential management remains in Supabase Auth OR ASP.NET Identity, choose one.
CREATE TABLE auth_identity (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 player_id uuid NOT NULL REFERENCES player(id),
 provider varchar(40) NOT NULL CHECK (length(btrim(provider)) > 0),
 subject varchar(255) NOT NULL CHECK (length(btrim(subject)) > 0),
 created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(provider, subject), UNIQUE(player_id, provider)
);
CREATE TABLE player_wallet (
 player_id uuid PRIMARY KEY REFERENCES player(id),
 balance bigint NOT NULL DEFAULT 0 CHECK (balance >= 0),
 updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE player_rating (
 player_id uuid PRIMARY KEY REFERENCES player(id),
 elo integer NOT NULL CHECK (elo >= 0),
 games_played integer NOT NULL DEFAULT 0 CHECK (games_played >= 0),
 wins integer NOT NULL DEFAULT 0 CHECK (wins >= 0),
 draws integer NOT NULL DEFAULT 0 CHECK (draws >= 0),
 losses integer NOT NULL DEFAULT 0 CHECK (losses >= 0),
 updated_at timestamptz NOT NULL DEFAULT now(),
 CHECK (games_played = wins + draws + losses)
);
CREATE TABLE ruleset (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(60) NOT NULL UNIQUE,
 name varchar(120) NOT NULL,
 setup_budget smallint NOT NULL DEFAULT 50 CHECK (setup_budget > 0),
 turn_seconds smallint NOT NULL DEFAULT 90 CHECK (turn_seconds > 0),
 action_limit integer NOT NULL DEFAULT 150 CHECK (action_limit > 0),
 config jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(config) = 'object'),
 is_active boolean NOT NULL DEFAULT false,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX uq_one_active_ruleset ON ruleset ((true)) WHERE is_active;
CREATE TABLE chess_class (
 code varchar(16) PRIMARY KEY CHECK (code IN ('GENERAL','ADVISOR','ELEPHANT','ROOK','CANNON','HORSE','SOLDIER')),
 name_vi varchar(40) NOT NULL,
 base_sp smallint NOT NULL CHECK (base_sp >= 0),
 required_count smallint NOT NULL CHECK (required_count BETWEEN 1 AND 5),
 base_movement_code varchar(60) NOT NULL
);
-- Red's canonical starting coordinates; Black is rotated 180 degrees: (8-x,9-y).
CREATE TABLE lineup_slot (
 slot_no smallint PRIMARY KEY CHECK (slot_no BETWEEN 1 AND 16),
 class_code varchar(16) NOT NULL REFERENCES chess_class(code),
 start_x smallint NOT NULL CHECK (start_x BETWEEN 0 AND 8),
 start_y smallint NOT NULL CHECK (start_y BETWEEN 0 AND 9),
 UNIQUE(start_x,start_y), UNIQUE(slot_no,class_code)
);
CREATE TABLE historical_character (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(80) NOT NULL UNIQUE,
 name varchar(120) NOT NULL,
 description text
);
CREATE TABLE hero_trait (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(80) NOT NULL UNIQUE,
 name varchar(120) NOT NULL,
 kind varchar(20) NOT NULL CHECK (kind IN ('passive','active','special_move')),
 implementation_key varchar(100) NOT NULL,
 parameters jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(parameters) = 'object'),
 description text
);
CREATE TABLE hero (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(80) NOT NULL UNIQUE,
 character_id uuid REFERENCES historical_character(id),
 class_code varchar(16) NOT NULL REFERENCES chess_class(code),
 trait_id uuid REFERENCES hero_trait(id),
 name varchar(120) NOT NULL,
 setup_points smallint NOT NULL CHECK (setup_points >= 0),
 coin_price bigint NOT NULL DEFAULT 0 CHECK (coin_price >= 0),
 is_starter boolean NOT NULL DEFAULT false,
 is_enabled boolean NOT NULL DEFAULT true,
 is_test_fixture boolean NOT NULL DEFAULT false,
 asset_key varchar(160),
 description text,
 UNIQUE(id,class_code),
 CHECK (class_code = 'GENERAL' OR setup_points >= 1)
);
CREATE TABLE faction (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(60) NOT NULL UNIQUE,
 name varchar(120) NOT NULL,
 description text
);
CREATE TABLE hero_faction (
 hero_id uuid NOT NULL REFERENCES hero(id),
 faction_id uuid NOT NULL REFERENCES faction(id),
 PRIMARY KEY(hero_id,faction_id)
);
CREATE INDEX ix_hero_faction_faction ON hero_faction(faction_id);
CREATE TABLE team_skill (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(80) NOT NULL UNIQUE,
 name varchar(120) NOT NULL,
 faction_id uuid UNIQUE REFERENCES faction(id),
 implementation_key varchar(100) NOT NULL,
 parameters jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(parameters) = 'object'),
 eligibility jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(eligibility) = 'object'),
 max_uses smallint CHECK (max_uses >= 1),
 cooldown_turns smallint CHECK (cooldown_turns >= 0),
 is_enabled boolean NOT NULL DEFAULT false,
 is_test_fixture boolean NOT NULL DEFAULT false,
 asset_key varchar(160),
 description text
);
CREATE TABLE cosmetic (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(80) NOT NULL UNIQUE,
 hero_id uuid NOT NULL REFERENCES hero(id),
 name varchar(120) NOT NULL,
 coin_price bigint NOT NULL CHECK (coin_price >= 0),
 asset_key varchar(160) NOT NULL,
 is_enabled boolean NOT NULL DEFAULT true,
 UNIQUE(id,hero_id)
);
CREATE TABLE player_hero (
 player_id uuid NOT NULL REFERENCES player(id),
 hero_id uuid NOT NULL REFERENCES hero(id),
 acquired_via varchar(20) NOT NULL CHECK (acquired_via IN ('starter','purchase','admin','test')),
 acquired_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,hero_id)
);
CREATE TABLE player_cosmetic (
 player_id uuid NOT NULL REFERENCES player(id),
 cosmetic_id uuid NOT NULL REFERENCES cosmetic(id),
 acquired_at timestamptz NOT NULL DEFAULT now(),
 PRIMARY KEY(player_id,cosmetic_id)
);
CREATE TABLE lineup (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 player_id uuid NOT NULL REFERENCES player(id),
 ruleset_id uuid NOT NULL REFERENCES ruleset(id),
 name varchar(80) NOT NULL CHECK (length(btrim(name)) > 0),
 revision integer NOT NULL DEFAULT 1 CHECK (revision >= 1),
 created_at timestamptz NOT NULL DEFAULT now(),
 updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(id,player_id)
);
CREATE INDEX ix_lineup_player ON lineup(player_id);
CREATE TABLE lineup_entry (
 lineup_id uuid NOT NULL REFERENCES lineup(id) ON DELETE CASCADE,
 slot_no smallint NOT NULL,
 class_code varchar(16) NOT NULL,
 hero_id uuid NOT NULL,
 cosmetic_id uuid,
 PRIMARY KEY(lineup_id,slot_no),
 FOREIGN KEY(slot_no,class_code) REFERENCES lineup_slot(slot_no,class_code),
 FOREIGN KEY(hero_id,class_code) REFERENCES hero(id,class_code),
 FOREIGN KEY(cosmetic_id,hero_id) REFERENCES cosmetic(id,hero_id)
);
CREATE TABLE lineup_skill (
 lineup_id uuid NOT NULL REFERENCES lineup(id) ON DELETE CASCADE,
 slot_no smallint NOT NULL CHECK (slot_no BETWEEN 1 AND 3),
 team_skill_id uuid NOT NULL REFERENCES team_skill(id),
 PRIMARY KEY(lineup_id,slot_no),
 UNIQUE(lineup_id,team_skill_id)
);
CREATE TABLE game_match (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 mode varchar(12) NOT NULL CHECK (mode IN ('ranked','bot')),
 status varchar(24) NOT NULL DEFAULT 'selecting' CHECK (status IN ('selecting','active','completed','cancelled')),
 ruleset_id uuid NOT NULL REFERENCES ruleset(id),
 ruleset_snapshot jsonb NOT NULL CHECK (jsonb_typeof(ruleset_snapshot) = 'object'),
 content_version varchar(80) NOT NULL,
 result varchar(12) CHECK (result IN ('red_win','black_win','draw')),
 end_reason varchar(60),
 created_at timestamptz NOT NULL DEFAULT now(),
 started_at timestamptz,
 ended_at timestamptz,
 settled_at timestamptz,
 CHECK ((status = 'completed' AND result IS NOT NULL AND end_reason IS NOT NULL AND ended_at IS NOT NULL)
     OR (status = 'cancelled' AND result IS NULL AND end_reason IS NOT NULL AND ended_at IS NOT NULL)
     OR (status IN ('selecting','active') AND result IS NULL AND end_reason IS NULL AND ended_at IS NULL)),
 CHECK (status <> 'active' OR started_at IS NOT NULL),
 CHECK (ended_at IS NULL OR started_at IS NULL OR ended_at >= started_at),
 CHECK (settled_at IS NULL OR status IN ('completed','cancelled'))
);
CREATE INDEX ix_match_created ON game_match(created_at DESC);
CREATE INDEX ix_match_active ON game_match(status) WHERE status IN ('selecting','active');
CREATE TABLE match_participant (
 match_id uuid NOT NULL REFERENCES game_match(id),
 side varchar(5) NOT NULL CHECK (side IN ('red','black')),
 player_id uuid REFERENCES player(id),
 participant_type varchar(8) NOT NULL CHECK (participant_type IN ('human','bot')),
 bot_config jsonb CHECK (bot_config IS NULL OR jsonb_typeof(bot_config) = 'object'),
 -- source IDs are provenance only: intentionally no FK, so saved lineups may be deleted.
 source_lineup_id uuid,
 source_lineup_revision integer CHECK (source_lineup_revision >= 1),
 lineup_snapshot jsonb NOT NULL CHECK (jsonb_typeof(lineup_snapshot) = 'object'),
 confirmed_at timestamptz,
 elo_before integer CHECK (elo_before >= 0),
 elo_after integer CHECK (elo_after >= 0),
 coin_reward bigint NOT NULL DEFAULT 0 CHECK (coin_reward >= 0),
 stats jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(stats) = 'object'),
 PRIMARY KEY(match_id,side),
 UNIQUE(match_id,player_id),
 UNIQUE(match_id,side,player_id),
 CHECK ((participant_type = 'human' AND player_id IS NOT NULL AND bot_config IS NULL)
     OR (participant_type = 'bot' AND player_id IS NULL AND bot_config IS NOT NULL))
);
CREATE INDEX ix_participant_player ON match_participant(player_id,match_id);
CREATE TABLE match_state (
 match_id uuid PRIMARY KEY REFERENCES game_match(id),
 version integer NOT NULL DEFAULT 0 CHECK (version >= 0),
 side_to_move varchar(5) NOT NULL CHECK (side_to_move IN ('red','black')),
 turn_index integer NOT NULL DEFAULT 0 CHECK (turn_index >= 0),
 counted_actions integer NOT NULL DEFAULT 0 CHECK (counted_actions >= 0),
 turn_deadline_at timestamptz,
 state_schema_version integer NOT NULL DEFAULT 1 CHECK (state_schema_version >= 1),
 state jsonb NOT NULL CHECK (jsonb_typeof(state) = 'object'),
 updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE match_action (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 match_id uuid NOT NULL REFERENCES game_match(id),
 sequence_no integer NOT NULL CHECK (sequence_no >= 0),
 command_id uuid NOT NULL,
 actor_side varchar(5),
 kind varchar(24) NOT NULL CHECK (kind IN ('start','move','hero_active','team_skill','timeout','resign','undo','cancel','end_turn')),
 request_payload jsonb NOT NULL DEFAULT '{}' CHECK (jsonb_typeof(request_payload) = 'object'),
 resolved_events jsonb NOT NULL DEFAULT '[]' CHECK (jsonb_typeof(resolved_events) = 'array'),
 state_after jsonb NOT NULL CHECK (jsonb_typeof(state_after) = 'object'),
 state_schema_version integer NOT NULL DEFAULT 1 CHECK (state_schema_version >= 1),
 received_at timestamptz NOT NULL,
 committed_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(match_id,actor_side) REFERENCES match_participant(match_id,side),
 UNIQUE(match_id,sequence_no),
 UNIQUE(match_id,command_id),
 CHECK ((sequence_no = 0 AND kind = 'start') OR (sequence_no > 0 AND kind <> 'start'))
);
CREATE TABLE coin_transaction (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 player_id uuid NOT NULL REFERENCES player_wallet(player_id),
 idempotency_key varchar(160) NOT NULL,
 kind varchar(24) NOT NULL CHECK (kind IN ('starter','hero_purchase','cosmetic_purchase','match_reward','admin_adjustment')),
 amount bigint NOT NULL CHECK (amount <> 0),
 balance_after bigint NOT NULL CHECK (balance_after >= 0),
 match_id uuid,
 match_side varchar(5),
 hero_id uuid REFERENCES hero(id),
 cosmetic_id uuid REFERENCES cosmetic(id),
 created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(player_id,idempotency_key),
 FOREIGN KEY(match_id,match_side,player_id) REFERENCES match_participant(match_id,side,player_id),
 CHECK ((kind = 'match_reward' AND match_id IS NOT NULL AND match_side IS NOT NULL AND amount > 0
          AND hero_id IS NULL AND cosmetic_id IS NULL)
 OR (kind = 'hero_purchase' AND hero_id IS NOT NULL AND cosmetic_id IS NULL AND amount < 0 AND match_id IS NULL AND match_side IS NULL)
 OR (kind = 'cosmetic_purchase' AND cosmetic_id IS NOT NULL AND hero_id IS NULL AND amount < 0 AND match_id IS NULL AND match_side IS NULL)
 OR (kind IN ('starter','admin_adjustment') AND match_id IS NULL AND match_side IS NULL AND hero_id IS NULL AND cosmetic_id IS NULL
     AND (kind <> 'starter' OR amount > 0)))
);
CREATE UNIQUE INDEX uq_one_match_reward ON coin_transaction(player_id,match_id) WHERE kind = 'match_reward';
CREATE INDEX ix_coin_history ON coin_transaction(player_id,created_at DESC);
CREATE TABLE admin_audit_log (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 actor_player_id uuid NOT NULL REFERENCES player(id),
 action varchar(80) NOT NULL,
 entity_type varchar(60) NOT NULL,
 entity_id varchar(100) NOT NULL,
 before_data jsonb,
 after_data jsonb,
 reason text,
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_admin_audit_time ON admin_audit_log(created_at DESC);

-- Simple DB invariants only; gameplay rules remain in the C# rules engine.
-- touch_updated_at: Trigger function cập nhật updated_at khi row đổi.
CREATE FUNCTION touch_updated_at() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN NEW.updated_at := clock_timestamp(); RETURN NEW; END $$;
CREATE TRIGGER touch_player BEFORE UPDATE ON player FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
CREATE TRIGGER touch_wallet BEFORE UPDATE ON player_wallet FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
CREATE TRIGGER touch_rating BEFORE UPDATE ON player_rating FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
CREATE TRIGGER touch_lineup BEFORE UPDATE ON lineup FOR EACH ROW EXECUTE FUNCTION touch_updated_at();
CREATE TRIGGER touch_state BEFORE UPDATE ON match_state FOR EACH ROW EXECUTE FUNCTION touch_updated_at();

-- validate_match_roster: Kiểm tra ranked không có bot/guest; active/completed phải có hai bên, mode bot đúng một bot. Constraint trigger deferred kiểm tra ở cuối transaction.
CREATE FUNCTION validate_match_roster() RETURNS trigger LANGUAGE plpgsql
SET search_path = hero_chess, pg_catalog AS $$
DECLARE mid uuid; gm game_match%ROWTYPE; total_count integer; bot_count integer; guest_count integer;
BEGIN
 IF TG_TABLE_NAME = 'match_participant' AND TG_OP = 'UPDATE' THEN
  IF NEW.match_id IS DISTINCT FROM OLD.match_id OR NEW.side IS DISTINCT FROM OLD.side THEN
   RAISE EXCEPTION 'Participant match and side are immutable';
  END IF;
 END IF;
 IF TG_TABLE_NAME = 'game_match' THEN mid := NEW.id; ELSE mid := COALESCE(NEW.match_id,OLD.match_id); END IF;
 SELECT * INTO gm FROM game_match WHERE id = mid;
 IF NOT FOUND THEN RETURN NULL; END IF;
 SELECT count(*),count(*) FILTER (WHERE p.participant_type = 'bot'),
 count(*) FILTER (WHERE u.is_guest) INTO total_count,bot_count,guest_count
 FROM match_participant p LEFT JOIN player u ON u.id = p.player_id WHERE p.match_id = mid;
 IF gm.mode = 'ranked' AND (bot_count > 0 OR guest_count > 0) THEN
   RAISE EXCEPTION 'Ranked requires registered humans';
 END IF;
 IF gm.status IN ('active','completed') AND (total_count <> 2 OR (gm.mode = 'bot' AND bot_count <> 1)) THEN
   RAISE EXCEPTION 'Active/completed match requires two participants; bot mode requires one bot';
 END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER check_roster_on_match AFTER INSERT OR UPDATE ON game_match
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_match_roster();
CREATE CONSTRAINT TRIGGER check_roster_on_participant AFTER INSERT OR UPDATE OR DELETE ON match_participant
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_match_roster();

COMMENT ON SCHEMA hero_chess IS 'Hero Chess prototype; backend-only access. Do not expose through Supabase Data API.';
COMMENT ON TABLE auth_identity IS 'External identity mapping. No passwords or refresh tokens are stored here.';
COMMENT ON TABLE lineup IS 'Backend must atomically validate 16 slots, 3 skills, ownership, budget and faction before saving.';
COMMENT ON TABLE match_action IS 'Append-only accepted events and replay snapshots; start event at sequence 0. Undo appends a new event.';
COMMENT ON TABLE match_state IS 'Latest state cache persisted transactionally with match_action; version equals last sequence_no.';
COMMENT ON COLUMN match_participant.lineup_snapshot IS 'Immutable copy of hero identity/class/SP/trait, skills, faction eligibility and assets at match start.';
COMMENT ON TABLE coin_transaction IS 'Ledger must be inserted in the same transaction as locked wallet update and asset grant.';
COMMENT ON COLUMN ruleset.action_limit IS '150; exact counted events are defined by the rules engine/config, not SQL.';
COMMENT ON COLUMN team_skill.max_uses IS 'NULL means not specified yet; not an implicit unlimited-use rule.';

REVOKE ALL ON ALL TABLES IN SCHEMA hero_chess FROM PUBLIC;
REVOKE ALL ON ALL FUNCTIONS IN SCHEMA hero_chess FROM PUBLIC;
-- Supabase roles may have default grants: remove if present; harmless on vanilla Postgres.
DO $$ DECLARE r text; BEGIN
 FOREACH r IN ARRAY ARRAY['anon','authenticated'] LOOP
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r) THEN
   EXECUTE format('REVOKE ALL ON SCHEMA hero_chess FROM %I',r);
   EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA hero_chess FROM %I',r);
   EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA hero_chess FROM %I',r);
  END IF;
 END LOOP;
END $$;
COMMIT;
-- ===== END 01_schema.sql =====

-- ===== BEGIN 04_identity_schema.sql =====
-- Vai trò file: Tạo bảng app_user và index phục vụ ASP.NET Identity custom store.
CREATE SCHEMA IF NOT EXISTS identity;
REVOKE ALL ON SCHEMA identity FROM PUBLIC;

CREATE TABLE IF NOT EXISTS identity.app_user (
    id uuid PRIMARY KEY,
    user_name varchar(256),
    normalized_user_name varchar(256) UNIQUE,
    email varchar(256),
    normalized_email varchar(256) UNIQUE,
    password_hash text,
    security_stamp varchar(64) NOT NULL
);

REVOKE ALL ON identity.app_user FROM PUBLIC;
-- ===== END 04_identity_schema.sql =====

-- ===== BEGIN 05_app_extensions.sql =====
-- Vai trò file: Bổ sung idempotency_key và unique index cho player_hero để retry mua miễn phí cũng được nhận diện; script có thể chạy lại.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
ALTER TABLE player_hero ADD COLUMN IF NOT EXISTS idempotency_key varchar(160);
CREATE UNIQUE INDEX IF NOT EXISTS uq_player_hero_idempotency
    ON player_hero(player_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
COMMIT;
-- ===== END 05_app_extensions.sql =====

-- ===== BEGIN 06_user_inheritance.sql =====
-- Hợp nhất identity.app_user + hero_chess.player thành User TPH; không đổi UUID/FK hay xóa lịch sử.
-- Chạy trong lúc API đã dừng. Transaction rollback toàn bộ nếu gặp dữ liệu không tương thích.
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_user_inheritance_v1'));
DO $$
BEGIN
    IF to_regclass('hero_chess.user_account') IS NOT NULL THEN
        RETURN; -- Migration đã hoàn tất; startup tiếp theo không copy lại credential cũ.
    END IF;
    LOCK TABLE hero_chess.player, identity.app_user IN ACCESS EXCLUSIVE MODE;
    IF EXISTS (SELECT 1 FROM identity.app_user a LEFT JOIN hero_chess.player p ON p.id=a.id WHERE p.id IS NULL) THEN
        RAISE EXCEPTION 'Credential without player row: reconcile orphan accounts before migration';
    END IF;
    ALTER TABLE hero_chess.player RENAME TO user_account;
    ALTER TABLE hero_chess.user_account
        ADD COLUMN user_name varchar(256),
        ADD COLUMN normalized_user_name varchar(256),
        ADD COLUMN email varchar(256),
        ADD COLUMN normalized_email varchar(256),
        ADD COLUMN password_hash text,
        ADD COLUMN security_stamp varchar(64) NOT NULL DEFAULT gen_random_uuid()::text;
    UPDATE hero_chess.user_account u SET user_name=a.user_name, normalized_user_name=a.normalized_user_name,
        email=a.email, normalized_email=a.normalized_email, password_hash=a.password_hash, security_stamp=a.security_stamp
    FROM identity.app_user a WHERE u.id=a.id;
    CREATE UNIQUE INDEX uq_user_normalized_name ON hero_chess.user_account(normalized_user_name);
    CREATE UNIQUE INDEX uq_user_normalized_email ON hero_chess.user_account(normalized_email);
    -- Guest chỉ thuộc Player; TPH cho cột subtype null trên Admin.
    ALTER TABLE hero_chess.user_account ALTER COLUMN is_guest DROP NOT NULL;
    ALTER TABLE hero_chess.user_account ALTER COLUMN is_guest DROP DEFAULT;
    UPDATE hero_chess.user_account SET is_guest=NULL WHERE role='admin';
    ALTER TABLE hero_chess.user_account ADD CONSTRAINT ck_user_subtype
        CHECK ((role='player' AND is_guest IS NOT NULL) OR (role='admin' AND is_guest IS NULL));
    ALTER TABLE hero_chess.auth_identity RENAME COLUMN player_id TO user_id;
    ALTER TABLE hero_chess.admin_audit_log RENAME COLUMN actor_player_id TO actor_user_id;
    -- Credentials đã được copy nguyên giá trị, unique index cũng đã kiểm tra xung đột.
    DROP TABLE identity.app_user;
END $$;

-- Tên bảng trong thân PL/pgSQL không tự đổi khi RENAME TABLE.
CREATE OR REPLACE FUNCTION hero_chess.validate_match_roster() RETURNS trigger LANGUAGE plpgsql
SET search_path=hero_chess,pg_catalog AS $$
DECLARE mid uuid; gm game_match%ROWTYPE; total_count int; bot_count int; guest_count int;
BEGIN
 IF TG_TABLE_NAME='game_match' THEN mid:=NEW.id; ELSE mid:=COALESCE(NEW.match_id,OLD.match_id); END IF;
 SELECT * INTO gm FROM game_match WHERE id=mid;
 IF NOT FOUND THEN RETURN NULL; END IF;
 SELECT count(*),count(*) FILTER (WHERE p.participant_type='bot'),count(*) FILTER (WHERE u.is_guest)
 INTO total_count,bot_count,guest_count FROM match_participant p LEFT JOIN user_account u ON u.id=p.player_id WHERE p.match_id=mid;
 IF gm.mode='ranked' AND (bot_count>0 OR guest_count>0) THEN RAISE EXCEPTION 'Ranked requires registered humans'; END IF;
 IF gm.status IN ('active','completed') AND (total_count<>2 OR (gm.mode='bot' AND bot_count<>1)) THEN
   RAISE EXCEPTION 'Active/completed match requires two participants; bot mode requires one bot';
 END IF;
 RETURN NULL;
END $$;

-- FK giữ được lịch sử admin từng chơi; chặn tạo/chuyển dữ liệu gameplay mới sang tài khoản admin.
CREATE OR REPLACE FUNCTION hero_chess.require_player_owner() RETURNS trigger LANGUAGE plpgsql
SET search_path=hero_chess,pg_catalog AS $$
BEGIN
 IF NEW.player_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM user_account WHERE id=NEW.player_id AND role='player') THEN
   RAISE EXCEPTION 'Gameplay owner must be a Player' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
DO $$
DECLARE t text;
BEGIN
 FOREACH t IN ARRAY ARRAY['player_wallet','player_rating','player_hero','player_cosmetic','lineup','match_participant'] LOOP
   IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid=format('hero_chess.%I',t)::regclass AND tgname='require_player_owner') THEN
     EXECUTE format('CREATE TRIGGER require_player_owner BEFORE INSERT OR UPDATE OF player_id ON hero_chess.%I FOR EACH ROW EXECUTE FUNCTION hero_chess.require_player_owner()',t);
   END IF;
 END LOOP;
END $$;
COMMENT ON TABLE hero_chess.user_account IS 'User TPH: Player and Admin are disjoint subtypes; admin cannot play. Legacy gameplay history is retained.';
REVOKE ALL ON FUNCTION hero_chess.require_player_owner() FROM PUBLIC;
COMMIT;
-- ===== END 06_user_inheritance.sql =====

-- ===== BEGIN 07_exclusive_hero_traits.sql =====
-- Một bản định nghĩa trait chỉ gắn tối đa một hero; handler/implementation_key vẫn có thể dùng chung.
-- Nâng cấp dữ liệu cũ: giữ trait gốc cho hero có UUID nhỏ nhất, clone cho các hero còn lại.
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_exclusive_traits_v1'));
LOCK TABLE hero_chess.hero, hero_chess.hero_trait IN SHARE ROW EXCLUSIVE MODE;
DO $$
DECLARE shared record; new_trait_id uuid;
BEGIN
 FOR shared IN
   SELECT h.id AS hero_id,h.name AS hero_name,t.*
   FROM (SELECT id,trait_id,row_number() OVER (PARTITION BY trait_id ORDER BY id) AS ordinal
         FROM hero_chess.hero WHERE trait_id IS NOT NULL) ranked
   JOIN hero_chess.hero h ON h.id=ranked.id
   JOIN hero_chess.hero_trait t ON t.id=ranked.trait_id
   WHERE ranked.ordinal>1
 LOOP
   -- UUID ổn định theo hero để seed và migration dùng cùng ID; MD5 ở đây không dùng cho bảo mật.
   new_trait_id := md5('hero_chess.hero_trait:' || shared.hero_id::text)::uuid;
   IF EXISTS (SELECT 1 FROM hero_chess.hero_trait WHERE id=new_trait_id) THEN
     RAISE EXCEPTION 'Trait clone ID already exists for hero %. Resolve conflict before upgrading.', shared.hero_id;
   END IF;
   INSERT INTO hero_chess.hero_trait(id,code,name,kind,implementation_key,parameters,description)
   VALUES(new_trait_id,left(shared.code,40)||'-'||replace(shared.hero_id::text,'-',''),
          left(shared.name||' — '||shared.hero_name,120),shared.kind,shared.implementation_key,shared.parameters,shared.description);
   UPDATE hero_chess.hero SET trait_id=new_trait_id WHERE id=shared.hero_id;
 END LOOP;
END $$;
-- PostgreSQL vẫn cho nhiều NULL: hero cơ bản chưa có trait không bị ảnh hưởng.
CREATE UNIQUE INDEX IF NOT EXISTS uq_hero_trait_id ON hero_chess.hero(trait_id);
COMMENT ON COLUMN hero_chess.hero.trait_id IS 'Optional exclusive trait definition. Different heroes may reuse implementation_key, never the same trait row.';
COMMIT;
-- ===== END 07_exclusive_hero_traits.sql =====

-- ===== BEGIN 02_seed_catalog.sql =====
-- NOTE ve DB conceptual ERD -- Hero Chess roster seed.
-- STEP 2: Custom movement handlers wired (Le Loi, Trung Trac, Trung Nhi).
-- Run after 01_schema.sql.
-- May be re-run: existing rows use ON CONFLICT DO NOTHING / DO UPDATE.
-- Hero behaviour is implemented in Rules; this file supplies catalogue metadata.
-- CHECK constraint on hero(setup_points): non-GENERAL heroes require SP >= 1.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

INSERT INTO ruleset(id,code,name,setup_budget,turn_seconds,action_limit,is_active)
VALUES ('10000000-0000-4000-8000-000000000001','prototype-v0.1','Hero Chess prototype',50,90,150,true)
ON CONFLICT DO NOTHING;

-- A. chess_class: Update ELEPHANT base from 1 to 2 per design note.
UPDATE chess_class SET base_sp = 2 WHERE code = 'ELEPHANT';
INSERT INTO chess_class(code,name_vi,base_sp,required_count,base_movement_code) VALUES
 ('GENERAL','Tuong',0,1,'xiangqi.general'),('ADVISOR','Si',1,2,'xiangqi.advisor'),
 ('ELEPHANT','Tuong',2,2,'xiangqi.elephant'),('ROOK','Xe',7,2,'xiangqi.rook'),
 ('CANNON','Phao',6,2,'xiangqi.cannon'),('HORSE','Ma',4,2,'xiangqi.horse'),
 ('SOLDIER','Tot',1,5,'xiangqi.soldier') ON CONFLICT (code) DO UPDATE SET base_sp = EXCLUDED.base_sp;

INSERT INTO lineup_slot(slot_no,class_code,start_x,start_y) VALUES
 (1,'ROOK',0,0),(2,'HORSE',1,0),(3,'ELEPHANT',2,0),(4,'ADVISOR',3,0),
 (5,'GENERAL',4,0),(6,'ADVISOR',5,0),(7,'ELEPHANT',6,0),(8,'HORSE',7,0),
 (9,'ROOK',8,0),(10,'CANNON',1,2),(11,'CANNON',7,2),
 (12,'SOLDIER',0,3),(13,'SOLDIER',2,3),(14,'SOLDIER',4,3),
 (15,'SOLDIER',6,3),(16,'SOLDIER',8,3)
ON CONFLICT (slot_no) DO NOTHING;

-- B. historical_character: 44 roster heroes (45 minus duplicate Nguyen Trai).
--    Nguyen Nhac / Nguyen Lu appear in both Si and Tuong sections; distinct chars per variant.
CREATE TEMP TABLE seed_characters ON COMMIT DROP AS SELECT * FROM (VALUES
 -- Si variants
 ('50000000-0000-4000-8000-000000000001','nguyen-trai-1','Nguyen Trai'),
 ('50000000-0000-4000-8000-000000000002','nguyen-trung-ngan','Nguyen Trung Ngan'),
 ('50000000-0000-4000-8000-000000000003','nguyen-binh-khiem','Nguyen Binh Khiem'),
 ('50000000-0000-4000-8000-000000000004','mac-dinh-chi','Mac Dinh Chi'),
 ('50000000-0000-4000-8000-000000000005','tran-thu-do','Tran Thu Do'),
 ('50000000-0000-4000-8000-000000000006','tran-nhat-duat','Tran Nhat Duat'),
 ('50000000-0000-4000-8000-000000000007','le-lai','Le Lai'),
 ('50000000-0000-4000-8000-000000000009','nguyen-nhac-si','Nguyen Nhac'),
 ('50000000-0000-4000-8000-000000000010','nguyen-lu-si','Nguyen Lu'),
 -- Tuong/Vua variants
 ('50000000-0000-4000-8000-000000000011','tran-hung-dao-tuong','Tran Hung Dao'),
 ('50000000-0000-4000-8000-000000000012','le-loi','Le Loi'),
 ('50000000-0000-4000-8000-000000000013','quang-trung','Quang Trung'),
 ('50000000-0000-4000-8000-000000000014','nguyen-nhac-tuong','Nguyen Nhac'),
 ('50000000-0000-4000-8000-000000000015','nguyen-lu-tuong','Nguyen Lu'),
 ('50000000-0000-4000-8000-000000000016','tran-thanh-tong','Tran Thanh Tong'),
 -- Tuong variants
 ('50000000-0000-4000-8000-000000000017','tran-hung-dao-tuong2','Tran Hung Dao'),
 ('50000000-0000-4000-8000-000000000018','da-tuong','Da Tuong'),
 ('50000000-0000-4000-8000-000000000019','tran-binh-trong','Tran Binh Trong'),
 ('50000000-0000-4000-8000-000000000020','le-khoi','Le Khoi'),
 ('50000000-0000-4000-8000-000000000021','pham-van-xao','Pham Van Xao'),
 ('50000000-0000-4000-8000-000000000022','bui-thi-xuan','Bui Thi Xuan'),
 ('50000000-0000-4000-8000-000000000023','le-van-hung','Le Van Hung'),
 ('50000000-0000-4000-8000-000000000024','trung-trac','Trung Trac'),
 ('50000000-0000-4000-8000-000000000025','trung-nhi','Trung Nhi'),
 -- Xe variants
 ('50000000-0000-4000-8000-000000000026','pham-ngu-lao','Pham Ngu Lao'),
 ('50000000-0000-4000-8000-000000000027','tran-khanh-du','Tran Khanh Du'),
 ('50000000-0000-4000-8000-000000000028','ly-thuong-kiet-xe','Ly Thuong Kiet'),
 ('50000000-0000-4000-8000-000000000029','dinh-liet','Dinh Liet'),
 ('50000000-0000-4000-8000-000000000030','tran-nguyen-han','Tran Nguyen Han'),
 ('50000000-0000-4000-8000-000000000031','vo-van-dung','Vo Van Dung'),
 ('50000000-0000-4000-8000-000000000032','tran-quang-dieu','Tran Quang Dieu'),
 -- Phao variants
 ('50000000-0000-4000-8000-000000000033','tran-khat-chan','Tran Khat Chan'),
 ('50000000-0000-4000-8000-000000000034','yet-kieu','Yet Kieu'),
 ('50000000-0000-4000-8000-000000000035','ly-thuong-kiet-phao','Ly Thuong Kiet'),
 ('50000000-0000-4000-8000-000000000036','dinh-le','Dinh Le'),
 ('50000000-0000-4000-8000-000000000037','nguyen-xi','Nguyen Xi'),
 ('50000000-0000-4000-8000-000000000038','nguyen-van-tuyet','Nguyen Van Tuyet'),
 ('50000000-0000-4000-8000-000000000039','nguyen-van-loc','Nguyen Van Loc'),
 -- Ma variants
 ('50000000-0000-4000-8000-000000000040','tran-quoc-toan','Tran Quoc Toan'),
 ('50000000-0000-4000-8000-000000000041','tran-quang-khai','Tran Quang Khai'),
 ('50000000-0000-4000-8000-000000000042','luu-nhan-chu','Luu Nhan Chu'),
 ('50000000-0000-4000-8000-000000000043','le-sat','Le Sat'),
 ('50000000-0000-4000-8000-000000000044','ly-van-bu','Ly Van Bu'),
 ('50000000-0000-4000-8000-000000000045','vo-dinh-tu','Vo Dinh Tu')) AS chars(seed_id,code,name);
INSERT INTO historical_character(id,code,name)
SELECT CASE WHEN EXISTS (SELECT 1 FROM historical_character existing WHERE existing.id = chars.seed_id::uuid AND existing.code <> chars.code)
            THEN gen_random_uuid() ELSE chars.seed_id::uuid END, chars.code, chars.name
FROM seed_characters chars WHERE true ON CONFLICT (code) DO NOTHING;

-- C. hero_trait: Heroes with confirmed custom movement.
--    Existing traits (from prior seed):
--      elephant-diagonal-1-2              -> 30000000-0000-4000-8000-000000000001 (Tran Binh Trong)
--      elephant-diagonal-1-2 (BXU variant) -> 327e712d-26d2-5da4-5c9a-87a1ff74c1e1 (Bui Thi Xuan)
--      da-tuong-diagonal                  -> 30000000-0000-4000-8000-000000000002 (Da Tuong)
--    NEW traits added in STEP 2:
--      elephant.river_crossing (Trung Trac, Trung Nhi): diagonal 1-2, no home-half restriction
--      general.king_move       (Le Loi): 1-step, 8 directions, palace-confined
--    NEW traits added in STEP 3:
--      general.orthogonal_range_3     (Quang Trung): orthogonal 1-3 cells, blocked like Rook
--      general.orthogonal_range_1_no_palace (Tran Hung Dao Tướng): orthogonal 1 cell, no palace, no river
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000001','elephant-diagonal-1-2','Trần Bình Trọng đi chéo 1–2 ô','special_move','elephant.diagonal_range',
  '{"minSteps":1,"maxSteps":2,"ownHalfOnly":true,"canJump":false}', 'Đi chéo 1–2 ô trên phần sân nhà.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000002','da-tuong-diagonal','Dã Tượng luân phiên tầm đi','special_move','elephant.alternating_distance',
  '{}', 'Tầm đi chéo thay đổi sau mỗi nước.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('327e712d-26d2-5da4-5c9a-87a1ff74c1e1','bui-thi-xuan-diagonal-1-2','Bùi Thị Xuân đi chéo 1–2 ô','special_move','elephant.diagonal_range',
  '{"minSteps":1,"maxSteps":2,"ownHalfOnly":true,"canJump":false}', 'Đi chéo 1–2 ô trên phần sân nhà.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000003','elephant-river-crossing','Trung Trac / Trung Nhi di chéo qua sông','special_move','elephant.river_crossing',
 '{"minSteps":1,"maxSteps":2,"ownHalfOnly":false,"canJump":false}',
 'Đi chéo 1–2 ô; không nhảy mắt; có thể qua sông.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000010','trung-nhi-river-crossing','Trưng Nhị đi chéo qua sông','special_move','elephant.river_crossing',
 '{"minSteps":1,"maxSteps":2,"ownHalfOnly":false,"canJump":false}',
 'Đi chéo 1–2 ô; không nhảy mắt; có thể qua sông.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000004','le-loi-king-move','Le Loi di 1 ô 8 hướng','special_move','general.king_move',
 '{"maxSteps":1,"orthogonal":true,"diagonal":true,"palaceOnly":true}',
 'Đi tối đa 1 ô theo 8 hướng; giới hạn trong cung của mình.') ON CONFLICT DO NOTHING;
-- Quang Trung: one-square normal movement; active skill travels up to three squares.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000005','quang-trung-orthogonal-3','Quang Trung — hành quân thần tốc','active','quang_trung.special_move',
 '{"minSteps":1,"maxSteps":3,"orthogonal":true,"palaceOnly":false,"riverCrossing":false,"cooldownTurns":3}',
 'Đi thường 1 ô thẳng, được ra khỏi cung nhưng không qua sông. Bấm skill để đi thẳng 1–3 ô; hồi chiêu 3 lượt chung.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000011','pham-ngu-lao-hoanh-soc','Hoành Sóc Giang Sơn','special_move','rook.hoanh_soc',
 '{}','Sau khi Phạm Ngũ Lão ăn một quân, nước đi tiếp theo của chính quân này có thể đi xuyên tối đa một quân đồng minh.') ON CONFLICT DO NOTHING;
-- STEP 3: Tran Hung Dao (GENERAL) — orthogonal 1 cell, can leave palace, cannot cross river.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000006','tran-hung-dao-tuong-no-palace','Tran Hung Dao Tướng đi 1 ô thẳng','special_move','general.orthogonal_range_1_no_palace',
 '{"maxSteps":1,"orthogonal":true,"diagonal":false,"palaceOnly":false,"riverCrossing":false}',
 'Đi 1 ô thẳng; được ra khỏi cung; không qua sông.') ON CONFLICT DO NOTHING;

-- Independent trait definitions; existing catalogue configuration is preserved.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description)
SELECT md5('hero_chess.hero_trait:'||code)::uuid,code,name,kind,implementation_key,parameters::jsonb,description
FROM (VALUES
 ('le-lai-cuu-chua','Lê Lai cứu chúa','active','le_lai.swap_general','{"cooldownTurns":8}',
  'Lê Lai hoán đổi vị trí với Tướng đồng minh còn trong cung để che chở chủ tướng. Không cần đường đi trống giữa hai quân.'),
 ('tran-thu-do-tran-quoc-ho-quan','Trấn Quốc Hộ Quân','active','tran_thu_do.general_shield','{"durationTurns":2,"cooldownTurns":6}',
  'Giữ vững quốc gia, Trần Thủ Độ dựng Khiên che chở chủ tướng trong thời gian ngắn. Chỉ có thể bảo vệ Tướng đồng minh.'),
 ('nguyen-nhac-thai-duc-linh-bo','Thái Đức Linh Bộ','special_move','advisor.king_step','{"maxSteps":1,"palaceOnly":true,"orthogonal":true,"diagonal":true}',
  'Nguyễn Nhạc linh hoạt xoay chuyển thế phòng ngự, có thể tiến thoái một ô theo tám hướng trong cung nhà.'),
 ('nguyen-lu-dong-dinh-linh-bo','Đông Định Linh Bộ','special_move','advisor.king_step','{"maxSteps":1,"palaceOnly":true,"orthogonal":true,"diagonal":true}',
  'Nguyễn Lữ uyển chuyển giữ thế trận, có thể di chuyển một ô theo tám hướng trong cung nhà.')
) AS traits(code,name,kind,implementation_key,parameters,description)
ON CONFLICT(code) DO NOTHING;

-- D. hero: Full 44-entry roster (45 minus duplicate Nguyen Trai).
--    trait_id:
--      Tran Binh Trong  -> 30000000-0000-4000-8000-000000000001
--      Bui Thi Xuan    -> 327e712d-26d2-5da4-5c9a-87a1ff74c1e1
--      Da Tuong        -> 30000000-0000-4000-8000-000000000002
--      all others       -> NULL
--    is_enabled = false: not released. is_test_fixture = false. coin_price = 0.
--    CLASS CODES: Tuong/Vua entries stored as GENERAL (no VUA class code).
--    SP: General baseline 0, special Generals 1–2; Elephants 2–4; Advisors 1–2.
--    Nguyen Trai: two character rows, two hero rows.
--    Nguyen Nhac / Nguyen Lu: Si variant + Tuong variant preserved separately.
--    Tran Hung Dao: Tuong variant + Tuong2 (ELEPHANT) variant preserved separately.
--    Ly Thuong Kiet: Xe variant + Phao variant preserved separately.
--    Red-marked (Nguyen Trung Ngan, Dinh Liet, Tran Khat Chan, Luu Nhan Chu):
--      no gameplay meaning assigned in this step.
--    Pham Ngu Lao: note "8d( co the 9d)" stored as 8d; 9d ambiguity noted.
WITH release_prices(code,model_key,price,starter) AS (VALUES
 ('tran-thanh-tong-general','tranthanhtong',0,true),('nguyen-nhac-general','nguyennhac',0,true),
 ('mac-dinh-chi-advisor','macdinhchi',0,true),('nguyen-trai-1-advisor','nguyentrai',0,true),
 ('nguyen-binh-khiem-advisor','nguyenbinhkhiem',0,true),('le-khoi-elephant','lekhoi',0,true),
 ('le-van-hung-elephant','levanhung',0,true),('tran-khanh-du-rook','trankhanhdu',0,true),
 ('tran-quang-dieu-rook','tranquangdieu',0,true),('tran-quoc-toan-horse','tranquoctoan',0,true),
 ('le-sat-horse','lesat',0,true),('nguyen-xi-cannon','nguyenxi',0,true),
 ('nguyen-van-tuyet-cannon','nguyenvantuyet',0,true),('linh-nha-tran-soldier','linhnhatran',0,true),
 ('tran-nhat-duat-advisor','trannhatduat',100,false),('le-lai-advisor','lelai',200,false),
 ('tran-thu-do-advisor','tranthudo',200,false),('nguyen-nhac-advisor','nguyennhac',200,false),
 ('nguyen-lu-advisor','nguyenlu_rig',200,false),('dinh-le-cannon','dinhle',100,false),
 ('ly-thuong-kiet-cannon','lythuongkiet',200,false),('yet-kieu-cannon','yetkeu',100,false),
 ('nguyen-van-loc-cannon','nguyenvanloc',100,false),('bui-thi-xuan-elephant','buithixuan',200,false),
 ('da-tuong-elephant','datuong',300,false),('pham-van-xao-elephant','phamvanxao',100,false),
 ('tran-binh-trong-elephant','tranbinhtrong',200,false),('tran-hung-dao-elephant','tranhungdao',300,false),
 ('trung-trac-elephant','trungtrac',300,false),('trung-nhi-elephant','trungnhi',300,false),
 ('le-loi-general','leloi',200,false),('nguyen-lu-general','nguyenlu_rig',100,false),
 ('quang-trung-general','quangtrung',300,false),('tran-hung-dao-general','tranhungdao',200,false),
 ('ly-van-buu-horse','lyvanbuu',100,false),('tran-quang-khai-horse','tranquangkhai',100,false),
 ('vo-dinh-tu-horse','vodinhtu',100,false),('ly-thuong-kiet-rook','lythuongkiet',200,false),
 ('pham-ngu-lao-rook','phamngulao',200,false),('tran-nguyen-han-rook','trannguyenhan',100,false),
 ('vo-van-dung-rook','vovandung',100,false),('linh-nha-le-soldier','linhnhale',100,false),
 ('linh-nha-tay-son-soldier','linhnhatayson',100,false)
)
INSERT INTO hero(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,is_starter,asset_key,description)
SELECT CASE WHEN EXISTS (SELECT 1 FROM hero existing WHERE existing.id = seeded.id::uuid AND existing.code <> seeded.code)
            THEN gen_random_uuid() ELSE seeded.id::uuid END,
       seeded.code, actual_character.id, seeded.class_code, seeded.trait_id::uuid, seeded.name,
       seeded.setup_points, true, seeded.is_test_fixture, release.price, release.starter, release.model_key, seeded.description
FROM (VALUES
 -- === SI (ADVISOR) ===
 ('60000000-0000-4000-8000-000000000001','nguyen-trai-1-advisor','50000000-0000-4000-8000-000000000001','ADVISOR',NULL,'Nguyen Trai',1,false,false,0,'Si 1d; base ADVISOR movement. First Nguyen Trai entry from note.'),
 ('60000000-0000-4000-8000-000000000003','nguyen-binh-khiem-advisor','50000000-0000-4000-8000-000000000003','ADVISOR',NULL,'Nguyen Binh Khiem',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000004','mac-dinh-chi-advisor','50000000-0000-4000-8000-000000000004','ADVISOR',NULL,'Mac Dinh Chi',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000005','tran-thu-do-advisor','50000000-0000-4000-8000-000000000005','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='tran-thu-do-tran-quoc-ho-quan'),'Tran Thu Do',2,false,false,0,'Sĩ có skill Trấn Quốc Hộ Quân.'),
 ('60000000-0000-4000-8000-000000000006','tran-nhat-duat-advisor','50000000-0000-4000-8000-000000000006','ADVISOR',NULL,'Tran Nhat Duat',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000007','le-lai-advisor','50000000-0000-4000-8000-000000000007','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='le-lai-cuu-chua'),'Le Lai',2,false,false,0,'Sĩ có skill Lê Lai cứu chúa.'),
 ('60000000-0000-4000-8000-000000000008','nguyen-nhac-advisor','50000000-0000-4000-8000-000000000009','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='nguyen-nhac-thai-duc-linh-bo'),'Nguyen Nhac',2,false,false,0,'Sĩ có trait Thái Đức Linh Bộ; bản Tướng giữ di chuyển cơ bản.'),
 ('60000000-0000-4000-8000-000000000010','nguyen-lu-advisor','50000000-0000-4000-8000-000000000010','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='nguyen-lu-dong-dinh-linh-bo'),'Nguyen Lu',2,false,false,0,'Sĩ có trait Đông Định Linh Bộ; bản Tướng giữ di chuyển cơ bản.'),
 -- === TUONG / VUA (GENERAL) ===
 -- Note: Tuong/Vua stored as GENERAL (no VUA class code exists).
 -- Special General costs include their movement/skill premium.
 ('60000000-0000-4000-8000-000000000011','tran-hung-dao-general','50000000-0000-4000-8000-000000000011','GENERAL','30000000-0000-4000-8000-000000000006','Tran Hung Dao',1,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.orthogonal_range_1_no_palace IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000012','le-loi-general','50000000-0000-4000-8000-000000000012','GENERAL','30000000-0000-4000-8000-000000000004','Le Loi',1,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.king_move IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000013','quang-trung-general','50000000-0000-4000-8000-000000000013','GENERAL','30000000-0000-4000-8000-000000000005','Quang Trung',2,false,false,0,'Tướng đi thường 1 ô thẳng, được ra khỏi cung, không qua sông; skill đi thẳng 1–3 ô với CD 3.'),
 ('60000000-0000-4000-8000-000000000014','nguyen-nhac-general','50000000-0000-4000-8000-000000000009','GENERAL',NULL,'Nguyen Nhac',0,false,false,0,'Tuong 0d; Nguyen Nhac also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000015','nguyen-lu-general','50000000-0000-4000-8000-000000000010','GENERAL',NULL,'Nguyen Lu',0,false,false,0,'Tuong 0d; Nguyen Lu also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000016','tran-thanh-tong-general','50000000-0000-4000-8000-000000000016','GENERAL',NULL,'Tran Thanh Tong',0,false,false,0,'Tuong 0d; no custom rule noted.'),
 -- === TUONG (ELEPHANT) ===
 -- Da Tuong: trait elephant.alternating_distance (existing ID).
 -- Tran Binh Trong: trait elephant.diagonal_range (existing ID).
 -- Bui Thi Xuan: trait elephant.diagonal_range (existing BXU variant ID).
 -- Trần Hưng Đạo (ELEPHANT): 4 SP includes the Bạch Đằng Giang premium.
 ('60000000-0000-4000-8000-000000000017','tran-hung-dao-elephant','50000000-0000-4000-8000-000000000011','ELEPHANT',NULL,'Tran Hung Dao',4,false,false,0,'Tượng 4 SP; Bạch Đằng Giang active skill.'),
 ('60000000-0000-4000-8000-000000000018','da-tuong-elephant','50000000-0000-4000-8000-000000000018','ELEPHANT','30000000-0000-4000-8000-000000000002','Da Tuong',4,false,false,0,'Tuong 4d; movement: elephant.alternating_distance. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000019','tran-binh-trong-elephant','50000000-0000-4000-8000-000000000019','ELEPHANT','30000000-0000-4000-8000-000000000001','Tran Binh Trong',3,false,false,0,'Tuong 3d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000020','le-khoi-elephant','50000000-0000-4000-8000-000000000020','ELEPHANT',NULL,'Le Khoi',2,false,false,0,'Tuong 2d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000021','pham-van-xao-elephant','50000000-0000-4000-8000-000000000021','ELEPHANT',NULL,'Pham Van Xao',2,false,false,0,'Tuong 2d; no custom movement noted; base ELEPHANT movement.'),
 ('60000000-0000-4000-8000-000000000022','bui-thi-xuan-elephant','50000000-0000-4000-8000-000000000022','ELEPHANT','327e712d-26d2-5da4-5c9a-87a1ff74c1e1','Bui Thi Xuan',3,false,false,0,'Tuong 3d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000023','le-van-hung-elephant','50000000-0000-4000-8000-000000000023','ELEPHANT',NULL,'Le Van Hung',2,false,false,0,'Tuong 2d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000024','trung-trac-elephant','50000000-0000-4000-8000-000000000024','ELEPHANT','30000000-0000-4000-8000-000000000003','Trung Trac',4,false,false,0,'Tuong 4d; movement: elephant.river_crossing IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000025','trung-nhi-elephant','50000000-0000-4000-8000-000000000025','ELEPHANT','30000000-0000-4000-8000-000000000010','Trung Nhi',4,false,false,0,'Tuong 4d; movement: elephant.river_crossing IMPLEMENTED.'),
 -- === XE (ROOK) ===
 ('60000000-0000-4000-8000-000000000026','pham-ngu-lao-rook','50000000-0000-4000-8000-000000000026','ROOK','30000000-0000-4000-8000-000000000011','Pham Ngu Lao',8,false,false,0,'Xe 8 SP. Hoành Sóc Giang Sơn: sau khi ăn một quân, nước đi tiếp theo có thể xuyên một quân đồng minh.'),
 ('60000000-0000-4000-8000-000000000027','tran-khanh-du-rook','50000000-0000-4000-8000-000000000027','ROOK',NULL,'Tran Khanh Du',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000028','ly-thuong-kiet-rook','50000000-0000-4000-8000-000000000028','ROOK',NULL,'Ly Thuong Kiet',8,false,false,0,'Xe 8 SP; đi qua Thành/Rào hoặc vào ô đó để phá.'),
 ('60000000-0000-4000-8000-000000000030','tran-nguyen-han-rook','50000000-0000-4000-8000-000000000030','ROOK',NULL,'Tran Nguyen Han',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000031','vo-van-dung-rook','50000000-0000-4000-8000-000000000031','ROOK',NULL,'Vo Van Dung',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000032','tran-quang-dieu-rook','50000000-0000-4000-8000-000000000032','ROOK',NULL,'Tran Quang Dieu',7,false,false,0,'Xe 7d; design note xe* (star meaning unclear). Assumed base ROOK movement.'),
 -- === PHAO (CANNON) ===
 ('60000000-0000-4000-8000-000000000034','yet-kieu-cannon','50000000-0000-4000-8000-000000000034','CANNON',NULL,'Yet Kieu',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000035','ly-thuong-kiet-cannon','50000000-0000-4000-8000-000000000028','CANNON',NULL,'Ly Thuong Kiet',7,false,false,0,'Pháo 7 SP; dùng Thành hoặc Rào làm ngòi, có thể vào ô Thành để phá.'),
 ('60000000-0000-4000-8000-000000000036','dinh-le-cannon','50000000-0000-4000-8000-000000000036','CANNON',NULL,'Dinh Le',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000037','nguyen-xi-cannon','50000000-0000-4000-8000-000000000037','CANNON',NULL,'Nguyen Xi',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000038','nguyen-van-tuyet-cannon','50000000-0000-4000-8000-000000000038','CANNON',NULL,'Nguyen Van Tuyet',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000039','nguyen-van-loc-cannon','50000000-0000-4000-8000-000000000039','CANNON',NULL,'Nguyen Van Loc',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 -- === MA (HORSE) ===
 ('60000000-0000-4000-8000-000000000040','tran-quoc-toan-horse','50000000-0000-4000-8000-000000000040','HORSE',NULL,'Tran Quoc Toan',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000041','tran-quang-khai-horse','50000000-0000-4000-8000-000000000041','HORSE',NULL,'Tran Quang Khai',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000043','le-sat-horse','50000000-0000-4000-8000-000000000043','HORSE',NULL,'Le Sat',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000044','ly-van-buu-horse','50000000-0000-4000-8000-000000000044','HORSE',NULL,'Ly Van Bu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000045','vo-dinh-tu-horse','50000000-0000-4000-8000-000000000045','HORSE',NULL,'Vo Dinh Tu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'))
 AS seeded(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,description)
 JOIN seed_characters expected_character ON expected_character.seed_id::uuid = seeded.character_id::uuid
 JOIN historical_character actual_character ON actual_character.code = expected_character.code
 JOIN release_prices release ON release.code=seeded.code
 ON CONFLICT (code) DO NOTHING;
INSERT INTO hero(code,class_code,name,setup_points,coin_price,is_starter,is_enabled,asset_key)
VALUES ('linh-nha-tran-soldier','SOLDIER','Lính nhà Trần',1,0,true,true,'linhnhatran'),
 ('linh-nha-le-soldier','SOLDIER','Lính nhà Lê',1,100,false,true,'linhnhale'),
 ('linh-nha-tay-son-soldier','SOLDIER','Lính nhà Tây Sơn',1,100,false,true,'linhnhatayson')
ON CONFLICT(code) DO NOTHING;
COMMIT;
-- ===== END 02_seed_catalog.sql =====

-- ===== BEGIN 24_hero_biographies.sql =====
-- Fill missing Vietnamese biographies after the hero catalog has been seeded.
BEGIN;

WITH biographies(code, biography) AS (
    VALUES
    -- https://baotanglichsu.vn/vi/Articles/3098/13485/cuoc-khoi-nghia-hai-ba-trung-nam-40-43-sau-cong-nguyen.html
    -- https://baotanglichsu.vn/vi/Articles/2001/65263/quoc-hieu-viet-nam-thoi-trung-vuong.html
    ('trung-trac', $bio$Trưng Trắc xuất thân từ gia đình Lạc tướng ở Mê Linh. Năm 40, bà cùng em gái Trưng Nhị phát động khởi nghĩa chống chính quyền đô hộ Đông Hán do Tô Định đứng đầu. Sau khi nghĩa quân giành được nhiều thành trì, bà lên ngôi, xưng là Trưng Vương và đóng đô ở Mê Linh.

Nhà Đông Hán sai Mã Viện đem quân sang đàn áp. Trưng Trắc tiếp tục lãnh đạo cuộc kháng cự cùng Trưng Nhị cho đến năm 43. Các đền thờ ở Mê Linh và Hát Môn là những nơi tưởng niệm Hai Bà Trưng.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13485/cuoc-khoi-nghia-hai-ba-trung-nam-40-43-sau-cong-nguyen.html
    -- https://sknc.qdnd.vn/chuyen-xua-nay/toa-sang-khi-tiet-hai-ba-trung-502900
    ('trung-nhi', $bio$Trưng Nhị là em gái của Trưng Trắc. Năm 40, bà cùng chị tập hợp lực lượng ở vùng Mê Linh và lãnh đạo cuộc khởi nghĩa chống Đông Hán. Nghĩa quân tiến đánh Luy Lâu, trung tâm cai trị của nhà Đông Hán ở Giao Chỉ, rồi giành quyền tự chủ.

Khi quân của Mã Viện tiến đến Lãng Bạc, Trưng Nhị cùng Trưng Vương đem quân từ Mê Linh ra giao chiến. Cuộc kháng cự kéo dài đến năm 43. Hậu thế thờ Trưng Nhị cùng chị trong các đền thờ Hai Bà Trưng.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/16192/ly-thuong-kiet-nguoi-chi-huy-kiet-xuat-cua-cuoc-khang-chien-chong-quan-tong-lan-thu-hai.html
    ('ly-thuong-kiet-xe', $bio$Lý Thường Kiệt là danh tướng triều Lý, từng nắm giữ những trọng trách về quân sự và chính trị của Đại Việt thế kỷ XI. Ông tham gia chiến dịch đánh Chiêm Thành năm 1069 và là người chỉ huy chủ chốt trong cuộc kháng chiến chống quân Tống lần thứ hai.

Năm 1075, ông chỉ huy quân Đại Việt tiến công các căn cứ của nhà Tống ở Ung, Khâm và Liêm. Khi quân Tống tiến vào Đại Việt, ông tổ chức phòng tuyến trên sông Như Nguyệt. Cuộc kháng chiến kết thúc thắng lợi năm 1077.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/14318/thai-su-tran-thu-djo.html
    -- https://baotanglichsu.vn/VI/Articles/3096/12006/tran-djanh-kinh-djong-ca-the-gioi-cua-nguoi-viet.html
    ('tran-thu-do', $bio$Trần Thủ Độ là một trong những nhân vật có ảnh hưởng lớn trong giai đoạn chuyển giao quyền lực từ nhà Lý sang nhà Trần. Năm 1224, ông giữ chức Điện tiền Chỉ huy sứ. Sau khi nhà Trần được thành lập, ông trở thành Thái sư và nắm vai trò lãnh đạo quan trọng trong triều đình trong nhiều thập niên. Ông tham gia tổ chức lại bộ máy chính quyền và củng cố vương triều Trần trong giai đoạn đầu.

Trần Thủ Độ cũng có vai trò trong cuộc kháng chiến chống quân Mông Cổ lần thứ nhất năm 1258. Khi tình hình chiến sự khó khăn, sử liệu chép rằng ông khuyên vua Trần Thái Tông giữ vững ý chí kháng chiến.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/2001/65033/mot-so-vi-vua-chua-cam-tinh-con-chuot-trong-lich-su-viet-nam.html
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xiv.-nha-tran-175-nam-1225-1400-quoc-hieu-dai-viet-kinh-do-thang-long-phan-1-.html
    -- https://baotanglichsu.vn/VI/Articles/3096/12006/tran-djanh-kinh-djong-ca-the-gioi-cua-nguoi-viet.html
    ('tran-thanh-tong', $bio$Trần Thánh Tông (1240–1290), tên húy Trần Hoảng, là con của Trần Thái Tông và là vị vua thứ hai của nhà Trần. Khi còn là thái tử, ông cùng vua cha phản công quân Mông Cổ ở Đông Bộ Đầu năm 1258. Ông lên ngôi trong năm đó và trị vì đến năm 1278.

Sau khi nhường ngôi cho Trần Nhân Tông, ông làm Thái thượng hoàng và cùng vua con lãnh đạo hai cuộc kháng chiến chống quân Nguyên năm 1285 và 1287–1288. Thơ của ông còn được lưu lại trong Đại Việt sử ký toàn thư và Việt âm thi tập.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xiv.-nha-tran-175-nam-1225-1400-quoc-hieu-dai-viet-kinh-do-thang-long-phan-1-.html
    -- https://baotanglichsu.vn/VI/Articles/3096/71341/tran-thuy-chien-bach-djang-nhung-manh-ghep.html
    ('tran-hung-dao-tuong', $bio$Trần Hưng Đạo, tên thật Trần Quốc Tuấn, là một trong những tướng lĩnh quan trọng của nhà Trần trong các cuộc kháng chiến chống quân Mông – Nguyên thế kỷ XIII. Ông là con của An Sinh Vương Trần Liễu và thuộc hoàng tộc nhà Trần. Đặc biệt trong hai cuộc kháng chiến năm 1285 và 1287–1288, ông giữ vai trò Quốc công Tiết chế, thống lĩnh các lực lượng quân sự của Đại Việt. Chiến thắng Bạch Đằng năm 1288 là một trong những dấu ấn lớn trong sự nghiệp cầm quân của ông.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/18629/chieu-van-vuong-tran-nhat-duat.html
    ('tran-nhat-duat', $bio$Trần Nhật Duật là con thứ sáu của vua Trần Thái Tông và Hoàng hậu Thuận Thiên, đồng thời là em của vua Trần Thánh Tông và Trần Quang Khải. Ông nổi tiếng là người có học vấn rộng, am hiểu chính trị, quân sự, ngôn ngữ và phong tục của nhiều dân tộc. Bảo tàng Lịch sử Quốc gia ghi nhận ông biết tiếng Hoa, tiếng Chiêm Thành và tiếng của người Sách Mã Tích.

Một sự kiện tiêu biểu thể hiện khả năng ngoại giao của Trần Nhật Duật diễn ra năm 1280, khi ông được giao giải quyết việc Trịnh Giác Mật chống lại triều đình tại Đà Giang. Nhờ hiểu phong tục và cách giao tiếp của địa phương, ông thuyết phục được Trịnh Giác Mật quy thuận mà không phải tiến hành một cuộc giao tranh lớn.

Trong cuộc kháng chiến chống Nguyên–Mông lần thứ hai năm 1285, Trần Nhật Duật là một trong những tướng lĩnh quan trọng của nhà Trần. Ông trực tiếp chỉ huy lực lượng trong trận Hàm Tử, góp phần vào cuộc phản công và thắng lợi chung của quân dân Đại Việt. Ông cũng tiếp tục tham gia cuộc kháng chiến lần thứ ba năm 1288.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/17651/chieu-minh-vuong-tran-quang-khai.html
    ('tran-quang-khai', $bio$Trần Quang Khải là con vua Trần Thái Tông và là một trong những nhân vật quan trọng của triều Trần thế kỷ XIII. Ông mang tước Chiêu Minh Vương, giữ chức Tướng quốc Thái úy và về sau làm Thượng tướng. Ông được ghi nhận là người có kiến thức rộng, giỏi cả văn lẫn võ và am hiểu một số ngôn ngữ của các dân tộc khác.

Trong cuộc kháng chiến chống Nguyên – Mông lần thứ hai năm 1285, Trần Quang Khải giữ vai trò quan trọng trong cuộc phản công của quân Trần. Ông cùng các tướng lĩnh chỉ huy quân đánh thắng tại Chương Dương, góp phần tạo điều kiện giải phóng kinh thành Thăng Long.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/2001/66032/cay-duoi-djai-thu-va-chung-tich-hoi-nghi-binh-than.html
    -- https://baotanglichsu.vn/VI/Articles/3098/17651/chieu-minh-vuong-tran-quang-khai.html
    ('tran-quoc-toan', $bio$Hoài Văn hầu Trần Quốc Toản là một người trẻ thuộc tôn thất nhà Trần trong giai đoạn chuẩn bị và tiến hành cuộc kháng chiến chống quân Nguyên – Mông lần thứ hai. Đại Việt sử ký toàn thư ghi chuyện năm 1282, khi triều đình tổ chức hội nghị bàn kế chống giặc. Vì còn trẻ nên Trần Quốc Toản không được tham dự, từ đó gắn với câu chuyện bóp nát quả cam vì phẫn chí.

Sau sự kiện này, sử liệu ghi rằng Trần Quốc Toản tự tập hợp hơn một nghìn gia nô và thân thuộc, chuẩn bị vũ khí, đóng chiến thuyền và dựng lá cờ mang sáu chữ “Phá cường địch, báo hoàng ân”, thể hiện quyết tâm chống quân xâm lược và báo đáp triều đình. Năm 1285, ông cùng các tướng nhà Trần tham gia trận Chương Dương.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3096/6830/tran-binh-trong-danh-tuong-khi-tiet.html
    ('tran-binh-trong', $bio$Trần Bình Trọng là một danh tướng thời Trần, nổi bật trong cuộc kháng chiến chống quân Nguyên – Mông năm 1285. Theo Bảo tàng Lịch sử Quốc gia, ông có nguồn gốc từ dòng họ Lê và được cho là con của Lê Tần, một danh tướng có công dưới triều Trần. Về sau ông mang họ Trần.

Trong giai đoạn quân Nguyên tiến sâu vào Đại Việt, Trần Bình Trọng được giao nhiệm vụ chặn quân địch tại khu vực Đà Mạc – Thiên Mạc, tạo thời gian để vua và bộ chỉ huy quân Trần rút lui an toàn. Do lực lượng chênh lệch, ông bị quân Nguyên bắt sau khi chiến đấu chống lại lực lượng truy kích. Ông khước từ lời dụ hàng và bị giết.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/16587/danh-tuong-tran-hai-chien-van-djon.html
    ('tran-khanh-du', $bio$Nhân Huệ vương Trần Khánh Dư là tướng nhà Trần từng trấn giữ vùng biển Vân Đồn. Trong cuộc kháng chiến chống quân Nguyên lần thứ ba, ông cho quân đón đánh đoàn thuyền chở lương do Trương Văn Hổ chỉ huy.

Chiến thắng Vân Đồn khiến quân Nguyên mất nguồn tiếp tế quan trọng và góp phần tạo điều kiện cho thắng lợi Bạch Đằng năm 1288.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/16591/danh-tuong-pham-ngu-lao-1255-1320.html
    ('pham-ngu-lao', $bio$Phạm Ngũ Lão quê ở làng Phù Ủng, vùng Hưng Yên. Ông xuất thân từ tầng lớp bình dân và về sau trở thành gia tướng của Hưng Đạo Vương Trần Quốc Tuấn. Dưới sự dìu dắt của Trần Quốc Tuấn, Phạm Ngũ Lão trở thành một trong những tướng lĩnh tiêu biểu của triều Trần.

Ông có công trong hai cuộc kháng chiến chống quân Nguyên – Mông vào các năm 1285 và 1288. Trong cuộc kháng chiến năm 1285, Phạm Ngũ Lão tham gia các hoạt động quân sự tại Chương Dương và truy kích quân Nguyên rút chạy. Đến cuộc kháng chiến năm 1288, ông tiếp tục tham gia đánh chặn và truy kích quân địch. Sau chiến tranh, ông còn nhiều lần tham gia các chiến dịch quân sự khác và được triều đình giao nhiều chức vụ quan trọng.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/19035/hai-danh-tuong-djuoc-khac-trong-di-san-tu-lieu-the-gioi-ve-su-trung-thanh.html
    ('da-tuong', $bio$Dã Tượng là một trong những gia tướng thân cận của Hưng Đạo Vương Trần Quốc Tuấn, thường được nhắc đến cùng Yết Kiêu. Sử liệu về thân thế và cuộc đời của Dã Tượng khá ít, vì vậy những thông tin như năm sinh, năm mất hay diện mạo của ông hiện không có cơ sở chắc chắn để xác định.

Trong cuộc kháng chiến năm 1285, khi Trần Hưng Đạo định rút theo đường núi, Dã Tượng khuyên ông trở lại Bãi Tân vì tin Yết Kiêu vẫn giữ thuyền chờ chủ tướng. Đại Việt sử ký toàn thư chép rằng quả nhiên Yết Kiêu vẫn ở đó, giúp Trần Hưng Đạo rút lui bằng đường thủy.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/19035/hai-danh-tuong-djuoc-khac-trong-di-san-tu-lieu-the-gioi-ve-su-trung-thanh.html
    -- https://baotanglichsu.vn/vi/Articles/2001/66862/djen-son-hai.html
    ('yet-kieu', $bio$Yết Kiêu là một trong những gia tướng thân cận của Trần Hưng Đạo, cùng với Dã Tượng được sử liệu nhắc đến về lòng trung thành và tài năng quân sự. Ông đặc biệt nổi tiếng với khả năng bơi lặn và hoạt động dưới nước, qua đó trở thành một hình tượng tiêu biểu của lực lượng thủy quân thời Trần. Bảo tàng Lịch sử Quốc gia ghi nhận Yết Kiêu có biệt tài thủy chiến và có công trong cuộc kháng chiến chống quân Nguyên – Mông thế kỷ XIII.

Trong cuộc kháng chiến năm 1285, ông giữ thuyền ở Bãi Tân để đợi Trần Hưng Đạo dù quân phòng thủ đã rút. Tư liệu thờ tự ở làng Hạ Bì lưu truyền tên ông là Phạm Hữu Thế.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/con-nguoi-viet-nam/luong-quoc-trang-nguyen-mac-dinh-chi.html
    -- https://sovhtt.hanoi.gov.vn/ky-niem-680-nam-ngay-mat-trang-nguyen-mac-dinh-chi-ton-vinh-truyen-thong-hieu-hoc-viet-nam/
    ('mac-dinh-chi', $bio$Mạc Đĩnh Chi là một danh thần và nhà ngoại giao nổi tiếng dưới triều Trần. Ông quê ở làng Lũng Động, vùng Nam Sách xưa. Năm 1304, dưới triều vua Trần Anh Tông, ông đỗ đầu kỳ thi và được xác định là Trạng nguyên.

Ông nổi tiếng về học vấn và khả năng ứng đối. Năm 1308, Mạc Đĩnh Chi được cử đi sứ nhà Nguyên. Các câu chuyện về tài ngoại giao và ứng đối của ông về sau trở thành một phần nổi bật trong hình tượng Mạc Đĩnh Chi. Danh xưng “Lưỡng quốc Trạng nguyên” cũng gắn liền với hình tượng này. Ông làm quan qua ba đời vua Trần Anh Tông, Trần Minh Tông và Trần Hiến Tông.$bio$),

    -- https://vnmh.com.vn/vi/Articles/3098/14470/tai-tri-cua-vua-le-thai-to-trong-muoi-nam-khang-chien-chong-quan-minh-1418-1427.html
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xviii.-trieu-le-so-99-nam-1428-1527-quoc-hieu-dai-viet-kinh-do-dong-do-ha-noi-.html
    ('le-loi', $bio$Lê Lợi sinh năm 1385 tại vùng Lam Sơn, Thanh Hóa, xuất thân từ một gia đình hào trưởng. Sau khi nhà Minh thiết lập ách cai trị ở Đại Việt, năm 1418, ông cùng các hào kiệt dựng cờ khởi nghĩa tại Lam Sơn, mở đầu cuộc kháng chiến kéo dài khoảng mười năm chống quân Minh.

Trong những năm đầu, nghĩa quân Lam Sơn gặp nhiều khó khăn và nhiều lần phải rút về vùng rừng núi. Từ năm 1424, lực lượng nghĩa quân phát triển mạnh hơn, mở rộng hoạt động từ Nghệ An ra Bắc. Đến năm 1427, các chiến thắng lớn, đặc biệt tại Chi Lăng – Xương Giang, góp phần đánh bại lực lượng viện binh nhà Minh và đưa cuộc chiến đến hồi kết.

Năm 1428, Lê Lợi lên ngôi Hoàng đế, lấy niên hiệu Thuận Thiên, khôi phục quốc hiệu Đại Việt và trở thành vị vua đầu tiên của triều Hậu Lê. Ông mất năm 1433 và được an táng tại Vĩnh Lăng, Lam Sơn.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/18122/nguyen-trai-anh-hung-dan-toc-danh-nhan-van-hoa-1380-1442.html
    ('nguyen-trai-1', $bio$Nguyễn Trãi sinh năm 1380. Cha ông là Nguyễn Phi Khanh, trước tên là Nguyễn Ứng Long. Mẹ ông là Trần Thị Thái, con gái của quan Tư đồ Trần Nguyên Đán. Năm 1400, Nguyễn Trãi đỗ Thái học sinh và từng làm quan dưới triều Hồ.

Sau khi Đại Việt rơi vào sự cai trị của nhà Minh, Nguyễn Trãi tham gia khởi nghĩa Lam Sơn do Lê Lợi lãnh đạo. Ông giữ vai trò quan trọng về mưu lược, chính trị và ngoại giao, đồng thời thay mặt Lê Lợi soạn nhiều văn thư gửi các tướng nhà Minh. Sau thắng lợi của khởi nghĩa Lam Sơn, Nguyễn Trãi soạn Bình Ngô đại cáo, một trong những tác phẩm tiêu biểu nhất trong sự nghiệp của ông.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/14509/thai-uy-phuc-quoc-cong-le-lai-dje-nhat-khai-quoc-cong-than-trieu-le.html
    -- https://baotanglichsu.vn/VI/Articles/3091/14734/hoi-thao-khoa-hoc-hoi-the-lung-nhai-trong-khoi-nghia-lam-son.html
    ('le-lai', $bio$Lê Lai là một trong những tướng lĩnh tham gia phong trào Lam Sơn từ giai đoạn đầu. Ông quê ở vùng Dựng Tú, Thanh Hóa, thuộc một gia đình có thế lực tại địa phương. Ông cùng Lê Lợi và các tướng lĩnh tham gia Hội thề Lũng Nhai, đặt nền móng cho lực lượng khởi nghĩa Lam Sơn. Khi khởi nghĩa chính thức bùng nổ năm 1418, Lê Lai được phong Quan Nội Hầu, giữ chức Tổng quản phủ Đô Tổng quản.

Theo ghi chép về giai đoạn đầu của khởi nghĩa, khi nghĩa quân bị vây ở vùng núi Chí Linh, Lê Lai khoác áo của Lê Lợi và dẫn quân ra đánh để đánh lạc hướng quân Minh. Ông hy sinh, giúp Lê Lợi cùng lực lượng chủ chốt thoát vây.$bio$),

    -- https://scov.gov.vn/ban-sac-van-hoa/viet-su-giai-thoai/vu-an-le-sat.html
    -- https://chauson.langson.gov.vn/thong-tin-tuyen-truyen/dinh-huong-tuyen-truyen/ky-niem-599-nam-ngay-chien-thang-chi-lang-10-10-1427-10-10-2026-.html
    ('le-sat', $bio$Lê Sát quê ở làng Bỉ Ngũ, vùng Lam Sơn, là một trong những tướng tham gia cuộc khởi nghĩa Lam Sơn từ giai đoạn đầu. Ông được sử liệu mô tả là người có tài trí và dũng lược, được Lê Lợi tin cậy giao quyền chỉ huy quân đội. Trong quá trình chống quân Minh, Lê Sát lập nhiều chiến công và trở thành một trong những võ tướng quan trọng của nghĩa quân.

Đến năm 1427, khi cuộc khởi nghĩa bước vào giai đoạn quyết định, Lê Sát cùng các tướng Lam Sơn chặn đánh đạo viện binh do Liễu Thăng chỉ huy tại Chi Lăng. Sau khi Lê Lợi lên ngôi năm 1428, ông tiếp tục được trao những chức vụ cao trong triều đình. Năm 1429, khi triều đình khắc tên 93 khai quốc công thần, Lê Sát được xếp ở hàng thứ hai trong danh sách được ghi nhận.$bio$),

    -- https://tapchiqptd.vn/vi/lich-su-quan-su-viet-nam/nghe-thuat-nghi-binh-tao-the-trong-tran-tot-dong-chuc-dong-nam-1426/26264.html
    -- https://scov.gov.vn/ban-sac-van-hoa/viet-su-giai-thoai/dinh-le-va-nguyen-xi-voi-cai-gia-cua-mot-bai-hoc.html
    ('dinh-le', $bio$Đinh Lễ là một trong những võ tướng nổi bật của nghĩa quân Lam Sơn. Ông là anh ruột của Đinh Liệt. Các tư liệu về Đinh Liệt cũng ghi nhận hai anh em sớm tham gia lực lượng của Lê Lợi trong cuộc khởi nghĩa chống quân Minh.

Trong giai đoạn nghĩa quân tiến vào Nghệ An, Đinh Lễ có vai trò đáng chú ý tại trận Khả Lưu năm 1424–1425. Ông được giao bố trí quân mai phục tại khu vực hiểm yếu, phối hợp với lực lượng của Đinh Liệt đánh quân Minh, góp phần vào quá trình mở rộng địa bàn hoạt động của nghĩa quân Lam Sơn ở Nghệ An.

Năm 1426, ông cùng Nguyễn Xí đem quân tiếp ứng ở Tốt Động – Chúc Động, góp phần đánh bại đạo quân do Vương Thông chỉ huy. Năm 1427, ông bị quân Minh bắt và giết sau trận đánh ở vùng My Động.$bio$),

    -- https://hannom.vass.gov.vn/tong-muc-luc/10-tim-lai-dau-xua-mot-danh-nhan-lich-su-tbhnh-1998-505635
    -- https://tapchiqptd.vn/vi/lich-su-quan-su-viet-nam/nghe-thuat-nghi-binh-tao-the-trong-tran-tot-dong-chuc-dong-nam-1426/26264.html
    ('pham-van-xao', $bio$Phạm Văn Xảo là tướng của nghĩa quân Lam Sơn dưới quyền Lê Lợi. Năm 1426, ông cùng Lý Triện chỉ huy một cánh quân tiến ra Bắc. Ông góp công trong chiến dịch Tốt Động – Chúc Động, nơi nghĩa quân đánh bại cuộc phản công do tướng Minh Vương Thông chỉ huy.

Sau khi nhà Lê thành lập năm 1428, ông được ban họ Lê và thăng hàm Thái bảo. Ông được ghi tên trong hàng ngũ khai quốc công thần của triều Lê sơ.$bio$),

    -- https://dulichphutho.gov.vn/den-tho-ta-tuong-quoc-tran-nguyen-han-dau-an-vi-danh-tuong-lam-son-tren-dat-son-dong.html
    -- https://vov.gov.vn/ta-tuong-quoc-tran-nguyen-han-nha-quan-su-dai-tai-dtnew-302445
    ('tran-nguyen-han', $bio$Trần Nguyên Hãn là một trong những tướng lĩnh quan trọng của khởi nghĩa Lam Sơn. Ông thuộc dòng dõi nhà Trần, là hậu duệ của Trần Quang Khải và cháu nội của Đại Tư đồ Trần Nguyên Đán. Năm sinh của ông chưa được xác định chắc chắn.

Sau khi theo Lê Lợi, Trần Nguyên Hãn được giao nhiều nhiệm vụ quân sự quan trọng. Năm 1425, ông tham gia chỉ huy lực lượng tiến vào Tân Bình và Thuận Hóa. Đến giai đoạn cuối của cuộc chiến, ông tham gia vây Đông Quan và chiến dịch tiêu diệt viện binh nhà Minh tại Xương Giang năm 1427. Sau thắng lợi của khởi nghĩa Lam Sơn, năm 1428 ông được phong Tả Tướng quốc, trở thành một trong những công thần có địa vị cao của triều Lê sơ.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/19477/danh-tuong-le-khoi-va-djen-tho-569-nam-tuoi.html
    ('le-khoi', $bio$Lê Khôi là người vùng Lam Sơn, Thanh Hóa, con của Lê Trừ và là cháu ruột của Lê Lợi. Cha mẹ mất sớm nên ông sống cùng Lê Lợi và tham gia lực lượng Lam Sơn từ giai đoạn đầu. Bảo tàng Lịch sử Quốc gia ghi nhận ông có tên trong những người tham gia Hội thề Lũng Nhai và lập nhiều chiến công trong cuộc khởi nghĩa chống quân Minh.

Trong giai đoạn cuối của khởi nghĩa, Lê Khôi giữ vai trò đáng kể trên chiến trường. Năm 1427, tại chiến dịch Xương Giang, ông cùng Phạm Vấn đem quân chi viện cho lực lượng của Lê Sát và Trần Nguyên Hãn, góp phần đánh bại quân Minh và bắt nhiều tướng địch. Sau khi nhà Lê được thành lập, ông tiếp tục làm quan qua ba triều Lê Thái Tổ, Lê Thái Tông và Lê Nhân Tông.$bio$),

    -- https://dsvh.gov.vn/lang-mo-va-den-tho-nguyen-xi-tinh-nghe-an-3309
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/con-nguoi-viet-nam/huu-tuong-quoc-nguyen-xi.html
    ('nguyen-xi', $bio$Nguyễn Xí quê ở vùng Thượng Xá, Chân Phúc, Nghệ An. Từ khi còn trẻ, ông cùng anh là Nguyễn Biện đến Lam Sơn và trở thành người thân cận của Lê Lợi. Khi khởi nghĩa Lam Sơn bùng nổ năm 1418, Nguyễn Xí được giao cùng Đinh Lễ chỉ huy đội quân Thiết đột, lực lượng quan trọng bảo vệ bộ chỉ huy nghĩa quân.

Trong gần mười năm khởi nghĩa, Nguyễn Xí tham gia nhiều chiến dịch lớn, từ những năm đầu gian khó ở vùng Thanh Hóa – Nghệ An đến Tốt Động – Chúc Động và bao vây Đông Quan. Sau khi cuộc khởi nghĩa thắng lợi, ông trở thành một trong những công thần quan trọng của triều Lê sơ.

Điểm đặc biệt của Nguyễn Xí là sự nghiệp của ông tiếp tục kéo dài nhiều thập niên sau chiến tranh. Ông phục vụ qua các triều Lê Thái Tổ, Lê Thái Tông, Lê Nhân Tông và Lê Thánh Tông. Năm 1460, Nguyễn Xí cùng một số đại thần tham gia phế truất Lê Nghi Dân và đưa Lê Tư Thành lên ngôi, tức Lê Thánh Tông. Sau đó ông được trao nhiều chức tước cao trong triều đình.$bio$),

    -- https://dsvh.gov.vn/di-tich-lich-su-de%CC%80n-tho%CC%80-nguye%CC%83n-bi%CC%89nh-khiem-2993
    ('nguyen-binh-khiem', $bio$Nguyễn Bỉnh Khiêm quê tại làng Trung Am, huyện Vĩnh Lại, trấn Hải Dương, nay thuộc khu vực Vĩnh Bảo, Hải Phòng. Năm 1535, dưới triều Mạc Thái Tông, ông dự thi và đỗ Trạng nguyên. Sau đó, ông làm quan cho triều Mạc và đảm nhiệm nhiều chức vụ quan trọng.

Sau khoảng tám năm làm quan, Nguyễn Bỉnh Khiêm rời quan trường và trở về quê, dựng am Bạch Vân và mở trường dạy học. Ông có ảnh hưởng lớn trong đời sống văn hóa và tư tưởng Việt Nam thế kỷ XVI, được hậu thế thường gọi là Trạng Trình. Ông mất năm 1585.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3096/6119/vua-quang-trung-nhung-djinh-menh-lich-su.html
    -- https://viensuhoc.vass.gov.vn/bao-ve-nen-tang-tu-tuong-cua-dang/chien-thang-ngoc-hoi-dong-da-mua-xuan-nam-ky-dau-1789-dau-an-lich-su-va-van-hoa-560605
    ('quang-trung', $bio$Nguyễn Huệ là một trong ba anh em lãnh đạo phong trào Tây Sơn, cùng với Nguyễn Nhạc và Nguyễn Lữ. Từ phong trào khởi nghĩa bắt đầu năm 1771, lực lượng Tây Sơn từng bước đánh bại các thế lực Nguyễn ở phía Nam và Trịnh ở phía Bắc. Bảo tàng Lịch sử Quốc gia ghi nhận Nguyễn Huệ là vị hoàng đế thứ hai của triều Tây Sơn và trị vì với niên hiệu Quang Trung từ 1788 đến 1792.

Một trong những chiến công quân sự nổi bật của Nguyễn Huệ là trận Rạch Gầm – Xoài Mút năm 1785, nơi quân Tây Sơn đánh bại lực lượng Xiêm tại khu vực sông Tiền. Sau đó, khi quân Thanh tiến vào Đại Việt cuối năm 1788, Nguyễn Huệ lên ngôi hoàng đế tại Phú Xuân rồi nhanh chóng đưa quân ra Bắc. Đầu xuân Kỷ Dậu 1789, quân Tây Sơn tiến công Hà Hồi, Ngọc Hồi và Đống Đa, sau đó tiến vào Thăng Long.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3096/12481/nguyen-nhac-voi-vuong-trieu-tay-son-mot-trieu-djai-toa-sang.html
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xxi.-nha-tay-son-24-nam-1778-1802-kinh-do-phu-xuan-hue-.html
    ('nguyen-nhac-si', $bio$Nguyễn Nhạc là anh cả trong ba anh em Tây Sơn và được xem là người khởi xướng phong trào. Trước cuộc khởi nghĩa, ông từng làm nghề buôn bán trầu, nhờ đó có điều kiện đi lại giữa vùng đồng bằng và Tây Sơn Thượng đạo, xây dựng quan hệ và tập hợp lực lượng. Khoảng năm 1771, phong trào Tây Sơn bắt đầu phát triển dưới sự lãnh đạo của Nguyễn Nhạc cùng Nguyễn Huệ và Nguyễn Lữ.

Năm 1773, lực lượng Tây Sơn từ căn cứ vùng thượng đạo tiến xuống đồng bằng và chiếm được phần lớn khu vực Quy Nhơn. Đến năm 1778, Nguyễn Nhạc lên ngôi Hoàng đế, lấy niên hiệu Thái Đức, đặt trung tâm chính quyền tại Thành Hoàng Đế. Di tích này từng là đại bản doanh của quân Tây Sơn và sau đó trở thành kinh đô của chính quyền Nguyễn Nhạc.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xxi.-nha-tay-son-24-nam-1778-1802-kinh-do-phu-xuan-hue-.html
    -- https://baotanglichsu.vn/VI/Articles/3096/12503/nguyen-anh-voi-vung-djat-gia-djinh.html
    ('nguyen-lu-si', $bio$Nguyễn Lữ là một trong ba anh em Tây Sơn cùng Nguyễn Nhạc và Nguyễn Huệ lãnh đạo phong trào khởi nghĩa bùng nổ năm 1771. Bảo tàng Lịch sử Quốc gia ghi nhận ông là một thành viên quan trọng của phong trào từ giai đoạn đầu và được giao nhiều trọng trách quân sự.

Năm 1776, Nguyễn Lữ được phong Thiếu phó và tham gia điều hành vùng đất do Tây Sơn kiểm soát. Năm 1777, ông cùng Nguyễn Huệ tiếp tục tiến quân vào khu vực Nam Bộ chống lực lượng của chúa Nguyễn. Khi anh em Tây Sơn phân chia địa bàn cai quản năm 1787, Nguyễn Lữ được phong Đông Định vương, phụ trách vùng Gia Định.$bio$),

    -- https://svhttdl.gialai.gov.vn/van-hoa-nghe-thuat/ky-niem-219-nam-ngay-mat-do-doc-bui-thi-xuan-1802-2021-tu-hao-nho-ve-nu-tuong-148.html
    -- https://baotanglichsu.vn/VI/Articles/3098/14999/su-thuc-cai-chet-cua-nu-tuong-bui-thi-xuan-va-chong-2.html
    ('bui-thi-xuan', $bio$Đô đốc Bùi Thị Xuân là nữ tướng của nhà Tây Sơn, quê ở vùng Tây Sơn, Bình Định. Sau khi Quang Trung mất, bà tiếp tục phục vụ triều Cảnh Thịnh và chỉ huy lực lượng bảo vệ kinh đô Phú Xuân.

Năm 1802, bà chỉ huy quân Tây Sơn chiến đấu tại lũy Trấn Ninh trong cuộc chiến với quân Nguyễn Ánh. Bà qua đời cùng năm. Đền thờ Đô đốc Bùi Thị Xuân ở quê hương là nơi tưởng niệm bà.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/14973/thai-pho-tran-quang-dieu-danh-tuong-nhan-djuc-cua-trieu-tay-son.html
    ('tran-quang-dieu', $bio$Trần Quang Diệu là một trong những tướng lĩnh quan trọng của nhà Tây Sơn. Bảo tàng Lịch sử Quốc gia ghi ông mất năm 1802, là chồng của nữ tướng Bùi Thị Xuân và thuộc nhóm các tướng nổi bật thường được hậu thế gọi là Tây Sơn Thất hổ tướng.

Ông tham gia phong trào Tây Sơn từ giai đoạn sớm và trở thành một trong những tướng lĩnh trụ cột. Sau chiến thắng Kỷ Dậu 1789, Trần Quang Diệu được giao làm Đốc trấn Nghệ An và phụ trách việc xây dựng Phượng Hoàng Trung Đô, công trình gắn với kế hoạch của Quang Trung tại Nghệ An.

Sau khi Quang Trung mất năm 1792, Trần Quang Diệu tiếp tục phục vụ triều Cảnh Thịnh. Trong giai đoạn cuối của Tây Sơn, ông chỉ huy nhiều chiến dịch chống lực lượng Nguyễn Ánh. Trong chiến dịch vây thành Quy Nhơn, ông tiếp nhận quân đối phương sau khi Võ Tánh mất và cho an táng vị tướng giữ thành.$bio$),

    -- https://svhttdl.gialai.gov.vn/hoat-dong-nganh/le-khanh-thanh-den-tho-dai-tu-do-vo-van-dung-244.html
    -- https://baotanglichsu.vn/vi/Articles/3098/13755/tay-son-that-ho-tuong-ky-2-trung-than-khong-tho-hai-vua.html
    ('vo-van-dung', $bio$Võ Văn Dũng quê ở vùng Tuy Viễn, phủ Quy Nhơn, thuộc khu vực Tây Sơn – Bình Định trước đây. Tư liệu giới thiệu tại Đền thờ Đại tư đồ Võ Văn Dũng cho biết ông tham gia phong trào Tây Sơn từ giai đoạn đầu và dần trở thành một võ tướng quan trọng của lực lượng này.

Trong quá trình hoạt động, Võ Văn Dũng tham gia nhiều chiến dịch của quân Tây Sơn. Nguồn giới thiệu di tích ghi nhận ông tham gia các hoạt động quân sự tại Gia Định, chiến dịch Rạch Gầm – Xoài Mút, cuộc tiến quân ra Bắc năm 1786 và các hoạt động quân sự về sau của triều Tây Sơn.

Võ Văn Dũng cũng đảm nhiệm công việc ngoại giao. Sau chiến thắng năm 1789, ông được Quang Trung giao nhiệm vụ đi sứ nhằm thiết lập quan hệ với nhà Thanh. Năm 1791, ông giữ chức Chiêu Viễn Đại Đô đốc, tước Võ Quốc công. Đến thời Cảnh Thịnh, ông được phong Đại Tư đồ.$bio$),

    -- https://giaoducthoidai.vn/tay-son-that-ho-tuong-le-van-hung-tu-tuong-cuop-thanh-danh-tuong-post580235.html
    -- https://baotanglichsu.vn/vi/Articles/3098/13757/tay-son-that-ho-tuong-ky-4-vi-dan-vi-nuoc-gac-chuyen-phuc-thu.html
    ('le-van-hung', $bio$Lê Văn Hưng được ghi nhận là người vùng Kiên Dõng, Bình Khê, thuộc khu vực Tây Sơn – Bình Định ngày nay. Ông xuất thân bình dân và gia nhập nghĩa quân Tây Sơn từ cấp binh lính, sau đó dần trở thành một võ tướng. Nhiều câu chuyện về thời trẻ của ông được lưu truyền trong võ sử Bình Định.

Trong quân đội Tây Sơn, Lê Văn Hưng được nhắc đến như một tướng có võ nghệ cao. Truyền thống võ học Bình Định đặc biệt gắn ông với côn, còn gọi là roi trường. Các tài liệu về “Tây Sơn Thất hổ tướng” xếp ông cùng Võ Văn Dũng, Trần Quang Diệu, Võ Đình Tú, Nguyễn Văn Tuyết, Lý Văn Bưu và Nguyễn Văn Lộc.

Trong quá trình chiến đấu giữa Tây Sơn và lực lượng Nguyễn, Lê Văn Hưng từng hoạt động tại Diên Khánh và Bình Thuận. Một số nguồn ghi nhận ông lần lượt giữ các chức Đề đốc, Đô đốc, Đại Đô đốc, Thái úy và về sau giữ chức Binh bộ Thượng thư dưới triều Cảnh Thịnh.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13757/tay-son-that-ho-tuong-ky-4-vi-dan-vi-nuoc-gac-chuyen-phuc-thu.html
    ('nguyen-van-loc', $bio$Nguyễn Văn Lộc được Địa chí Bình Định ghi là người làng Kỳ Sơn, huyện Tuy Viễn. Ông thuộc nhóm bảy võ tướng Bình Định được hậu thế gọi là Tây Sơn Thất hổ tướng, cùng Võ Văn Dũng, Trần Quang Diệu, Võ Đình Tú, Nguyễn Văn Tuyết, Lý Văn Bưu và Lê Văn Hưng.

Trong giai đoạn đầu của phong trào Tây Sơn, Nguyễn Văn Lộc được phong Hữu Đô đốc. Cuối năm 1773, ông cùng Lê Văn Hưng tham gia lực lượng tiến xuống phía Nam, hoạt động tại Phú Yên, Diên Khánh và Bình Thuận.

Năm 1786, Nguyễn Văn Lộc giữ vai trò Thủy quân Đô đốc, tham gia chiến dịch của Nguyễn Huệ đánh Thuận Hóa.

Trong chiến dịch chống quân Thanh 1788–1789, Nguyễn Văn Lộc được phong Đại Đô đốc, chỉ huy một cánh quân tiến theo hướng Lạng Sơn – Phượng Nhãn và bố trí lực lượng nhằm chặn đường rút của quân Thanh. Đây là một trong những hoạt động quân sự nổi bật nhất gắn với tên tuổi ông.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13757/tay-son-that-ho-tuong-ky-4-vi-dan-vi-nuoc-gac-chuyen-phuc-thu.html
    -- https://baotanglichsu.vn/DataFiles/2024/03/News/Tieng%20Viet/4.3.2024/Thong%20bao%20Khoa%20hoc%20so%202.2023/005.pdf
    ('nguyen-van-tuyet', $bio$Nguyễn Văn Tuyết là một tướng lĩnh của nhà Tây Sơn, thường được xếp cùng Võ Văn Dũng, Võ Đình Tú, Trần Quang Diệu, Lê Văn Hưng, Lý Văn Bưu và Nguyễn Văn Lộc trong nhóm được hậu thế gọi là Tây Sơn Thất hổ tướng. Tài liệu địa phương và các công trình viết về Tây Sơn thường ghi ông quê ở An Nhơn, huyện Tuy Viễn, phủ Quy Nhơn. Các tư liệu gia phả còn ghi những thông tin khác nhau về thân thế của ông.

Nguyễn Văn Tuyết nổi bật trong chiến dịch chống quân Thanh cuối năm 1788 – đầu năm 1789. Khi quân Thanh tiến vào Thăng Long, ông được giao nhiệm vụ nhanh chóng về Phú Xuân báo tin cho Nguyễn Huệ. Sau khi Quang Trung tiến quân ra Bắc, Nguyễn Văn Tuyết được giao chỉ huy một cánh quân hoạt động theo hướng Hải Dương, phối hợp với các đạo quân khác trong chiến dịch.$bio$),

    -- https://tayson.gialai.gov.vn/tin-tuc/tin-van-hoa-xa-hoi/khoi-dong-du-an-phim-da-su-hanh-dong-tay-son-that-ho-tuong-ky-nam-cung-thu-ly-van-buu2.html
    -- https://baotanglichsu.vn/VI/Articles/3098/13754/tay-son-that-ho-tuong-ky-1-vi-tuong-diet-ac-tru-gian.html
    ('ly-van-bu', $bio$Lý Văn Bưu là một trong bảy võ tướng thường được hậu thế gọi là Tây Sơn Thất hổ tướng, cùng Võ Văn Dũng, Trần Quang Diệu, Võ Đình Tú, Nguyễn Văn Tuyết, Lê Văn Hưng và Nguyễn Văn Lộc.

Các tài liệu địa phương ghi ông quê làng Đại Khoang, huyện Phù Cát, Bình Định, xuất thân trong gia đình có nghề nuôi và buôn bán ngựa. Nhờ môi trường này, hình tượng Lý Văn Bưu về sau đặc biệt gắn với cưỡi ngựa, huấn luyện chiến mã và bắn cung. Năm sinh và năm mất của ông hiện chưa được xác định rõ.

Khi tham gia phong trào Tây Sơn, Lý Văn Bưu được phong Đô đốc và được ghi nhận có vai trò trong việc tổ chức, huấn luyện lực lượng chiến mã. Tư liệu địa phương cũng ghi ông tham gia chiến dịch chống quân Xiêm ở phía Nam và sau đó theo Quang Trung tiến ra Bắc chống quân Thanh.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13756/tay-son-that-ho-tuong-ky-3-thiet-con-vo-djich.html
    ('vo-dinh-tu', $bio$Võ Đình Tú là người thôn Phú Phong, huyện Tuy Viễn, phủ Quy Nhơn, thuộc vùng Tây Sơn, Bình Định. Ông là một trong bảy võ tướng được hậu thế gọi là Tây Sơn Thất hổ tướng. Bảo tàng Lịch sử Quốc gia cho biết tư liệu về ông không nhiều, nhưng Địa chí Bình Định và các tài liệu địa phương đều nhấn mạnh tài võ nghệ và mối quan hệ của ông với lực lượng Tây Sơn.

Theo các tài liệu này, Võ Đình Tú được Võ Văn Dũng giới thiệu với Nguyễn Nhạc khi phong trào Tây Sơn phát triển. Ông được Nguyễn Huệ tin cậy và tham gia bàn bạc việc quân. Trong giai đoạn đầu, Võ Đình Tú cùng Bùi Thị Xuân được giao quản lý và phòng thủ khu vực Tây Sơn.

Đặc điểm nổi tiếng nhất của Võ Đình Tú là võ côn. Các tài liệu về Tây Sơn kể rằng ông thông thạo côn, kiếm, thương, quyền, cưỡi ngựa và bắn cung, nhưng đặc biệt nổi danh với thiết côn. Danh xưng “Thiết côn vô địch” cũng gắn chặt với hình tượng của ông trong truyền thống võ học Tây Sơn.$bio$)
)
UPDATE hero_chess.historical_character AS c
SET description = b.biography
FROM biographies AS b
WHERE c.code = b.code
  AND COALESCE(c.description, '') ~ '^[[:space:]]*$';

-- Soldier archetypes have no historical character record.
WITH biographies(code, biography) AS (
    VALUES
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xiv.-nha-tran-175-nam-1225-1400-quoc-hieu-dai-viet-kinh-do-thang-long-phan-1-.html
    -- https://tulieuvankien.dangcongsan.vn/van-kien-tu-lieu-ve-dang/book/sach-chinh-tri/lich-su-tu-tuong-quan-su-viet-nam-tap-2-tu-nam-1428-den-nam-1858-326
    -- https://baotanglichsu.vn/vi/Articles/3097/15641/ky-niem-726-nam-1288-2014-chien-thang-quan-nguyen-mong-lan-thu-3.html
    ('linh-nha-tran-soldier', $bio$Lính nhà Trần đại diện cho quân sĩ Đại Việt thế kỷ XIII. Triều đình duy trì quân thường trực và thực hiện chính sách ngụ binh ư nông, theo đó binh lính thay phiên tại ngũ và trở về sản xuất.

Quân dân nhà Trần đã tham gia ba cuộc kháng chiến chống quân Mông Cổ và nhà Nguyên vào các năm 1258, 1285 và 1287–1288. Trong cuộc kháng chiến thứ ba, thắng lợi ở Bạch Đằng năm 1288 góp phần kết thúc cuộc xâm lược.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xviii.-trieu-le-so-99-nam-1428-1527-quoc-hieu-dai-viet-kinh-do-dong-do-ha-noi-.html
    -- https://tulieuvankien.dangcongsan.vn/van-kien-tu-lieu-ve-dang/book/sach-chinh-tri/lich-su-tu-tuong-quan-su-viet-nam-tap-2-tu-nam-1428-den-nam-1858-326
    ('linh-nha-le-soldier', $bio$Lính nhà Lê đại diện cho quân sĩ Đại Việt buổi đầu thời Lê sơ. Lực lượng này kế thừa đội ngũ nghĩa quân Lam Sơn từng theo Lê Lợi kháng chiến chống quân Minh.

Sau khi lên ngôi năm 1428, Lê Lợi tổ chức lại quân đội. Năm 1429, ông cho các vệ và quân năm đạo tập trận thủy bộ rồi chia phiên. Một phiên ở lại quân ngũ, các phiên còn lại về làm ruộng theo chính sách ngụ binh ư nông.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xxi.-nha-tay-son-24-nam-1778-1802-kinh-do-phu-xuan-hue-.html
    -- https://viensuhoc.vass.gov.vn/bao-ve-nen-tang-tu-tuong-cua-dang/chien-thang-ngoc-hoi-dong-da-mua-xuan-nam-ky-dau-1789-dau-an-lich-su-va-van-hoa-560605
    ('linh-nha-tay-son-soldier', $bio$Lính nhà Tây Sơn đại diện cho quân sĩ của phong trào khởi nghĩa bắt đầu năm 1771 ở vùng Tây Sơn, Bình Định. Lực lượng này chiến đấu ở cả Đàng Trong và Đàng Ngoài vào những thập niên cuối thế kỷ XVIII.

Dưới quyền Nguyễn Huệ, quân Tây Sơn đánh bại quân Xiêm ở Rạch Gầm – Xoài Mút năm 1785. Mùa xuân Kỷ Dậu 1789, quân Tây Sơn do Hoàng đế Quang Trung chỉ huy đánh bại quân Thanh trong chiến dịch Ngọc Hồi – Đống Đa và tiến vào Thăng Long.$bio$)
)
UPDATE hero_chess.hero AS h
SET description = b.biography
FROM biographies AS b
WHERE h.code = b.code
  AND h.class_code = 'SOLDIER'
  AND h.character_id IS NULL
  AND COALESCE(h.description, '') ~ '^[[:space:]]*$';

COMMIT;
-- ===== END 24_hero_biographies.sql =====

-- ===== BEGIN 08_step6_skills.sql =====
-- Step 6: Thành and Rào Command Skills + Trần Hưng Đạo — Tượng Hero Skill.
-- These skills are wired to handler implementations in HeroChess.Rules.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
DO $legacy_catalog$
BEGIN
 IF EXISTS (SELECT 1 FROM ruleset WHERE is_active AND config->>'skillTimingVersion'='2') THEN RETURN; END IF;

-- Step 6: Thành Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000010','thanh','Thành','thanh',14,true,false,
  'Tạo 1 Thành ở vị trí bất kỳ bên sông mình; tồn tại 6 lượt chung. Pháo phá Thành và tiến vào ô đó; Thành không làm ngòi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  name=EXCLUDED.name,is_enabled=EXCLUDED.is_enabled;

-- Step 6: Rào Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000011','rao','Rào','rao',10,true,false,
  'Tạo 1 Rào ở vị trí bất kỳ bên sông mình; tồn tại 4 lượt chung. Pháo có thể dùng Rào làm ngòi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  name=EXCLUDED.name,is_enabled=EXCLUDED.is_enabled;

-- Step 6: Trần Hưng Đạo — Tượng Hero Skill (team_skill slot for the hero's own skill)
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000009','bach-dang-giang','Bạch Đằng Giang','active','tran_hung_dao_tuong.coc',
  '{"cooldownTurns":5,"durationTurns":3}',
  'Tạo 1 Cọc ẩn trên hai hàng sông; quân địch dừng ở ô Cọc bị ăn. Tồn tại 3 lượt chung, hồi chiêu 5; không thể thu hồi.')
ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,implementation_key=EXCLUDED.implementation_key,
  parameters=EXCLUDED.parameters,description=EXCLUDED.description;
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000009' WHERE code='tran-hung-dao-elephant';

INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000013','khien','Khiên','khien',14,true,false,
  'Tạo 1 Khiên cho quân đồng minh bất kỳ trên bàn cờ; tồn tại 4 lượt chung. Quân có Khiên không thể bị ăn.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  name=EXCLUDED.name,is_enabled=EXCLUDED.is_enabled;

INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000012','tran-hung-dao-tuong-coc','Trần Hưng Đạo — Tượng: Tạo Cọc','tran_hung_dao_tuong.coc',5,false,false,
  'Skill này đã chuyển thành Hero Skill Bạch Đằng Giang; không thể chọn làm Command Skill.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

-- Lý Thường Kiệt Xe uses a movement trait to pass or break Thành/Rào.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000007','ly-thuong-kiet-xe-trait','Lý Thường Kiệt — Xe','special_move','rook.ly_thuong_kiet',
  '{}',
  'Xe có thể đi qua Thành/Rào hoặc vào ô đó để phá; quân trên đường vẫn chặn như Xe thường.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,description=EXCLUDED.description;

-- Link Lý Thường Kiệt Xe hero to the trait
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000007'
 WHERE code='ly-thuong-kiet-rook';

-- Lý Thường Kiệt Pháo uses Thành/Rào as a screen and may break Thành.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000008','ly-thuong-kiet-phao-trait','Lý Thường Kiệt — Pháo','special_move','cannon.ly_thuong_kiet',
  '{}',
  'Pháo có thể dùng cả Thành và Rào làm ngòi; cũng có thể vào ô Thành để phá.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,description=EXCLUDED.description;

-- Link Lý Thường Kiệt Pháo hero to the trait
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000008'
 WHERE code='ly-thuong-kiet-cannon';

END
$legacy_catalog$;
COMMIT;
-- ===== END 08_step6_skills.sql =====

-- ===== BEGIN 09_hero_movement_fixes.sql =====
-- Re-runnable catalog patch. Apply after 02_seed_catalog and 08_step6_skills.
-- Intentionally leaves setup_points and existing hero IDs/ownership untouched.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
DO $legacy_catalog$
BEGIN
 IF EXISTS (SELECT 1 FROM ruleset WHERE is_active AND config->>'skillTimingVersion'='2') THEN RETURN; END IF;

INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000005','quang-trung-orthogonal-3','Quang Trung — hành quân thần tốc','active','quang_trung.special_move',
  '{"minSteps":1,"maxSteps":3,"orthogonal":true,"palaceOnly":false,"riverCrossing":false,"cooldownTurns":3}',
  'Đi thường 1 ô thẳng, được ra khỏi cung nhưng không qua sông. Bấm skill để đi thẳng 1–3 ô; hồi chiêu 3 lượt chung.')
ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,
 implementation_key=EXCLUDED.implementation_key,parameters=EXCLUDED.parameters,description=EXCLUDED.description;

INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000011','pham-ngu-lao-hoanh-soc','Hoành Sóc Giang Sơn','special_move','rook.hoanh_soc',
  '{}','Sau khi Phạm Ngũ Lão ăn một quân, nước đi tiếp theo của chính quân này có thể đi xuyên tối đa một quân đồng minh.')
ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,
 implementation_key=EXCLUDED.implementation_key,parameters=EXCLUDED.parameters,description=EXCLUDED.description;

UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000011',
 description='Xe 8 SP. Hoành Sóc Giang Sơn: sau khi ăn một quân, nước đi tiếp theo có thể xuyên một quân đồng minh.'
 WHERE code='pham-ngu-lao-rook';

UPDATE hero_trait SET description='Xe có thể đi qua Thành/Rào hoặc vào ô đó để phá; quân trên đường vẫn chặn như Xe thường.'
 WHERE id='30000000-0000-4000-8000-000000000007';
UPDATE hero SET description='Xe 8 SP; đi qua Thành/Rào hoặc vào ô đó để phá.'
 WHERE code='ly-thuong-kiet-rook';

UPDATE hero_trait SET description='Pháo có thể dùng cả Thành và Rào làm ngòi; cũng có thể vào ô Thành để phá.'
 WHERE id='30000000-0000-4000-8000-000000000008';
UPDATE hero SET description='Pháo 7 SP; dùng Thành hoặc Rào làm ngòi, có thể vào ô Thành để phá.'
 WHERE code='ly-thuong-kiet-cannon';
UPDATE hero SET description='Tướng đi thường 1 ô thẳng, được ra khỏi cung, không qua sông; skill đi thẳng 1–3 ô với CD 3.'
 WHERE code='quang-trung-general';
END
$legacy_catalog$;
COMMIT;
-- ===== END 09_hero_movement_fixes.sql =====

-- ===== BEGIN 13_admin_module.sql =====
-- Vai trò file: Mở rộng schema cho module Admin (quyền fine-grained, gift code, hero trong shop).
-- Chỉ ADD bảng/cột/index; không DROP, không đổi dữ liệu lịch sử. Chạy lại được (idempotent).
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_admin_module_v1'));

-- Quyền fine-grained cho admin. Không có dòng = admin toàn quyền (tương thích ngược với admin cũ).
CREATE TABLE IF NOT EXISTS hero_chess.admin_permission (
    admin_user_id uuid PRIMARY KEY REFERENCES hero_chess.user_account(id) ON DELETE CASCADE,
    permissions text[] NOT NULL DEFAULT ARRAY[]::text[],
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid REFERENCES hero_chess.user_account(id)
);

-- Gift code: mã quy tặng admin tạo, quy đổi một lần/lần theo giới hạn.
CREATE TABLE IF NOT EXISTS hero_chess.gift_code (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code varchar(64) NOT NULL,
    reward_kind varchar(16) NOT NULL CHECK (reward_kind IN ('coin', 'hero', 'cosmetic')),
    reward_quantity integer NOT NULL CHECK (reward_quantity > 0),
    reward_hero_id uuid REFERENCES hero_chess.hero(id),
    reward_cosmetic_id uuid REFERENCES hero_chess.cosmetic(id),
    starts_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz,
    max_redemptions integer NOT NULL CHECK (max_redemptions > 0),
    max_per_player integer NOT NULL DEFAULT 1 CHECK (max_per_player > 0),
    is_active boolean NOT NULL DEFAULT true,
    created_by uuid NOT NULL REFERENCES hero_chess.user_account(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_gift_code_code UNIQUE (code),
    CONSTRAINT ck_gift_code_reward CHECK (
        (reward_kind = 'coin' AND reward_hero_id IS NULL AND reward_cosmetic_id IS NULL)
        OR (reward_kind = 'hero' AND reward_hero_id IS NOT NULL AND reward_cosmetic_id IS NULL)
        OR (reward_kind = 'cosmetic' AND reward_cosmetic_id IS NOT NULL AND reward_hero_id IS NULL))
);

-- Lịch sử quy đổi; redemption_no cho phép giới hạn nhiều lần/người chơi mà vẫn chống quy đổi trùng.
CREATE TABLE IF NOT EXISTS hero_chess.gift_code_redemption (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    gift_code_id uuid NOT NULL REFERENCES hero_chess.gift_code(id) ON DELETE CASCADE,
    player_id uuid NOT NULL REFERENCES hero_chess.user_account(id),
    redemption_no integer NOT NULL CHECK (redemption_no > 0),
    reward_kind varchar(16) NOT NULL,
    reward_quantity integer NOT NULL CHECK (reward_quantity > 0),
    coin_balance_after bigint,
    redeemed_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_gift_code_redemption UNIQUE (gift_code_id, player_id, redemption_no)
);

CREATE INDEX IF NOT EXISTS ix_gift_code_code ON hero_chess.gift_code(code);
CREATE INDEX IF NOT EXISTS ix_gift_code_redemption_code ON hero_chess.gift_code_redemption(gift_code_id, redeemed_at DESC);
CREATE INDEX IF NOT EXISTS ix_gift_code_redemption_player ON hero_chess.gift_code_redemption(player_id, redeemed_at DESC);

-- Hero có mặt trong cửa hàng: tách khỏi is_enabled (được bật để chơi) để admin tắt bán mà không ảnh hưởng gameplay.
ALTER TABLE hero_chess.hero ADD COLUMN IF NOT EXISTS is_in_shop boolean NOT NULL DEFAULT true;
UPDATE hero_chess.hero SET is_in_shop = true WHERE is_in_shop IS NULL;

REVOKE ALL ON hero_chess.admin_permission, hero_chess.gift_code, hero_chess.gift_code_redemption FROM PUBLIC;
DO $$ DECLARE role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY['anon', 'authenticated'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('REVOKE ALL ON hero_chess.admin_permission, hero_chess.gift_code, hero_chess.gift_code_redemption FROM %I', role_name);
        END IF;
    END LOOP;
END $$;
COMMIT;
-- ===== END 13_admin_module.sql =====

-- ===== BEGIN 13_hero_factions.sql =====
-- Re-running inserts no duplicate factions or links.
BEGIN;

INSERT INTO hero_chess.faction (code, name)
VALUES ('tran', 'Trần'), ('hau-le', 'Hậu Lê'), ('tay-son', 'Tây Sơn')
ON CONFLICT (code) DO NOTHING;

WITH roster(hero_code, faction_key) AS (
    VALUES
        ('tran-hung-dao-general', 'tran'),
        ('tran-thanh-tong-general', 'tran'),
        ('tran-thu-do-advisor', 'tran'),
        ('tran-nhat-duat-advisor', 'tran'),
        ('mac-dinh-chi-advisor', 'tran'),
        ('tran-hung-dao-elephant', 'tran'),
        ('da-tuong-elephant', 'tran'),
        ('tran-binh-trong-elephant', 'tran'),
        ('pham-ngu-lao-rook', 'tran'),
        ('tran-khanh-du-rook', 'tran'),
        ('yet-kieu-cannon', 'tran'),
        ('tran-quoc-toan-horse', 'tran'),
        ('tran-quang-khai-horse', 'tran'),
        ('le-loi-general', 'haule'),
        ('nguyen-trai-1-advisor', 'haule'),
        ('le-lai-advisor', 'haule'),
        ('le-khoi-elephant', 'haule'),
        ('pham-van-xao-elephant', 'haule'),
        ('tran-nguyen-han-rook', 'haule'),
        ('dinh-le-cannon', 'haule'),
        ('nguyen-xi-cannon', 'haule'),
        ('le-sat-horse', 'haule'),
        ('quang-trung-general', 'tayson'),
        ('nguyen-nhac-general', 'tayson'),
        ('nguyen-lu-general', 'tayson'),
        ('nguyen-nhac-advisor', 'tayson'),
        ('nguyen-lu-advisor', 'tayson'),
        ('bui-thi-xuan-elephant', 'tayson'),
        ('le-van-hung-elephant', 'tayson'),
        ('vo-van-dung-rook', 'tayson'),
        ('tran-quang-dieu-rook', 'tayson'),
        ('nguyen-van-tuyet-cannon', 'tayson'),
        ('nguyen-van-loc-cannon', 'tayson'),
        ('ly-van-buu-horse', 'tayson'),
        ('vo-dinh-tu-horse', 'tayson')
), faction_keys AS (
    SELECT id, regexp_replace(translate(lower(code), 'ầậâêơà', 'aaaeoa'), '[^a-z]', '', 'g') AS code_key,
        regexp_replace(translate(lower(name), 'ầậâêơà', 'aaaeoa'), '[^a-z]', '', 'g') AS name_key
    FROM hero_chess.faction
), resolved AS (
    SELECT DISTINCT r.hero_code, f.id
    FROM roster r
    JOIN faction_keys f ON f.code_key IN (r.faction_key, 'nha' || r.faction_key)
        OR f.name_key IN (r.faction_key, 'nha' || r.faction_key)
), unambiguous AS (
    SELECT hero_code FROM resolved GROUP BY hero_code HAVING count(*) = 1
)
INSERT INTO hero_chess.hero_faction (hero_id, faction_id)
SELECT h.id, r.id
FROM resolved r
JOIN unambiguous u USING (hero_code)
JOIN hero_chess.hero h ON h.code = r.hero_code
ON CONFLICT (hero_id, faction_id) DO NOTHING;

COMMIT;

SELECT f.code, f.name, count(hf.hero_id) AS hero_count
FROM hero_chess.faction f
LEFT JOIN hero_chess.hero_faction hf ON hf.faction_id = f.id
WHERE f.code IN ('tran', 'hau-le', 'tay-son')
GROUP BY f.id, f.code, f.name
ORDER BY f.code;
-- ===== END 13_hero_factions.sql =====

-- ===== BEGIN 17_board_cosmetics.sql =====
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_board_cosmetics'));

CREATE TABLE IF NOT EXISTS hero_chess.board_cosmetic (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(60) NOT NULL UNIQUE,
 name varchar(120) NOT NULL,
 coin_price bigint NOT NULL CHECK (coin_price >= 0),
 asset_key varchar(160) NOT NULL,
 preview_key varchar(160) NOT NULL,
 is_enabled boolean NOT NULL DEFAULT true
);
CREATE TABLE IF NOT EXISTS hero_chess.player_board_cosmetic (
 player_id uuid NOT NULL REFERENCES hero_chess.user_account(id),
 board_cosmetic_id uuid NOT NULL REFERENCES hero_chess.board_cosmetic(id),
 acquired_at timestamptz NOT NULL DEFAULT now(),
 idempotency_key varchar(160) NOT NULL,
 PRIMARY KEY (player_id, board_cosmetic_id),
 UNIQUE (player_id, idempotency_key)
);
CREATE TABLE IF NOT EXISTS hero_chess.player_board_loadout (
 player_id uuid PRIMARY KEY REFERENCES hero_chess.user_account(id),
 board_cosmetic_id uuid NOT NULL REFERENCES hero_chess.board_cosmetic(id)
);
DO $$
DECLARE t text;
BEGIN
 FOREACH t IN ARRAY ARRAY['player_board_cosmetic','player_board_loadout'] LOOP
  IF NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid=format('hero_chess.%I',t)::regclass AND tgname='require_player_owner') THEN
   EXECUTE format('CREATE TRIGGER require_player_owner BEFORE INSERT OR UPDATE OF player_id ON hero_chess.%I FOR EACH ROW EXECUTE FUNCTION hero_chess.require_player_owner()',t);
  END IF;
 END LOOP;
END $$;

ALTER TABLE hero_chess.coin_transaction ADD COLUMN IF NOT EXISTS board_cosmetic_id uuid REFERENCES hero_chess.board_cosmetic(id);
-- Keep the ledger constraint compatible when this older bootstrap runs again.
ALTER TABLE hero_chess.coin_transaction ADD COLUMN IF NOT EXISTS piece_cosmetic_id uuid;
DO $$
DECLARE c record;
BEGIN
 FOR c IN SELECT conname FROM pg_constraint
  WHERE conrelid='hero_chess.coin_transaction'::regclass AND contype='c'
   AND pg_get_constraintdef(oid) LIKE '%kind%'
 LOOP
  EXECUTE format('ALTER TABLE hero_chess.coin_transaction DROP CONSTRAINT %I', c.conname);
 END LOOP;
END $$;
ALTER TABLE hero_chess.coin_transaction ADD CONSTRAINT ck_coin_transaction_kind_payload CHECK (
 (kind='piece_cosmetic_purchase' AND piece_cosmetic_id IS NOT NULL AND board_cosmetic_id IS NULL AND amount<0
   AND hero_id IS NULL AND cosmetic_id IS NULL AND match_id IS NULL AND match_side IS NULL)
 OR (piece_cosmetic_id IS NULL AND (
 (kind='board_cosmetic_purchase' AND board_cosmetic_id IS NOT NULL AND amount<0
   AND hero_id IS NULL AND cosmetic_id IS NULL AND match_id IS NULL AND match_side IS NULL)
 OR (board_cosmetic_id IS NULL AND (
  (kind='match_reward' AND match_id IS NOT NULL AND match_side IS NOT NULL AND amount>0 AND hero_id IS NULL AND cosmetic_id IS NULL)
  OR (kind='hero_purchase' AND hero_id IS NOT NULL AND cosmetic_id IS NULL AND amount<0 AND match_id IS NULL AND match_side IS NULL)
  OR (kind='cosmetic_purchase' AND cosmetic_id IS NOT NULL AND hero_id IS NULL AND amount<0 AND match_id IS NULL AND match_side IS NULL)
  OR (kind IN ('starter','admin_adjustment') AND match_id IS NULL AND match_side IS NULL AND hero_id IS NULL AND cosmetic_id IS NULL
      AND (kind<>'starter' OR amount>0))
 ))))
);

INSERT INTO hero_chess.board_cosmetic(code,name,coin_price,asset_key,preview_key)
VALUES
 ('mac-dinh','Bàn cờ mặc định',0,'Board/XiangqiBoardHalves','UI/Cosmetics/BoardMacDinh'),
 ('son-mai','Sơn mai',200,'Board/XiangqiBoardSkinSonMai','UI/Cosmetics/BoardSonMai'),
 ('son-son','Sơn son',200,'Board/XiangqiBoardSkinSonSon','UI/Cosmetics/BoardSonSon'),
 ('su-thanh','Sứ thanh',200,'Board/XiangqiBoardSkinSuThanh','UI/Cosmetics/BoardSuThanh')
ON CONFLICT (code) DO NOTHING;

ALTER TABLE hero_chess.board_cosmetic ENABLE ROW LEVEL SECURITY;
ALTER TABLE hero_chess.player_board_cosmetic ENABLE ROW LEVEL SECURITY;
ALTER TABLE hero_chess.player_board_loadout ENABLE ROW LEVEL SECURITY;
COMMIT;
-- ===== END 17_board_cosmetics.sql =====

-- ===== BEGIN 21_piece_cosmetics.sql =====
BEGIN;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_piece_cosmetics'));

CREATE TABLE IF NOT EXISTS hero_chess.piece_cosmetic (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
 code varchar(60) NOT NULL UNIQUE,
 name varchar(120) NOT NULL,
 coin_price bigint NOT NULL CHECK (coin_price >= 0),
 asset_key varchar(160) NOT NULL,
 preview_key varchar(160) NOT NULL,
 is_enabled boolean NOT NULL DEFAULT true
);
CREATE TABLE IF NOT EXISTS hero_chess.player_piece_cosmetic (
 player_id uuid NOT NULL REFERENCES hero_chess.user_account(id),
 piece_cosmetic_id uuid NOT NULL REFERENCES hero_chess.piece_cosmetic(id),
 acquired_at timestamptz NOT NULL DEFAULT now(),
 idempotency_key varchar(160) NOT NULL,
 PRIMARY KEY (player_id, piece_cosmetic_id),
 UNIQUE (player_id, idempotency_key)
);
CREATE TABLE IF NOT EXISTS hero_chess.player_piece_loadout (
 player_id uuid PRIMARY KEY REFERENCES hero_chess.user_account(id),
 piece_cosmetic_id uuid NOT NULL REFERENCES hero_chess.piece_cosmetic(id)
);
ALTER TABLE hero_chess.coin_transaction ADD COLUMN IF NOT EXISTS piece_cosmetic_id uuid;
DO $$
DECLARE t text;
BEGIN
 IF NOT EXISTS (SELECT FROM pg_constraint WHERE conrelid='hero_chess.coin_transaction'::regclass AND conname='coin_transaction_piece_cosmetic_id_fkey') THEN
  ALTER TABLE hero_chess.coin_transaction ADD CONSTRAINT coin_transaction_piece_cosmetic_id_fkey
   FOREIGN KEY (piece_cosmetic_id) REFERENCES hero_chess.piece_cosmetic(id);
 END IF;
 FOREACH t IN ARRAY ARRAY['player_piece_cosmetic','player_piece_loadout'] LOOP
  IF NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid=format('hero_chess.%I',t)::regclass AND tgname='require_player_owner') THEN
   EXECUTE format('CREATE TRIGGER require_player_owner BEFORE INSERT OR UPDATE OF player_id ON hero_chess.%I FOR EACH ROW EXECUTE FUNCTION hero_chess.require_player_owner()',t);
  END IF;
 END LOOP;
END $$;

INSERT INTO hero_chess.piece_cosmetic(code,name,coin_price,asset_key,preview_key)
VALUES
 ('mac-dinh','Bộ quân cờ mặc định',0,'Board/XiangqiPieces','UI/Cosmetics/PiecesMacDinh'),
 ('son-mai','Sơn mai',200,'Board/XiangqiPiecesSkinSonMai','UI/Cosmetics/PiecesSonMai'),
 ('son-son','Sơn son',200,'Board/XiangqiPiecesSkinSonSon','UI/Cosmetics/PiecesSonSon'),
 ('su-thanh','Sứ thanh',200,'Board/XiangqiPiecesSkinSuThanh','UI/Cosmetics/PiecesSuThanh')
ON CONFLICT (code) DO NOTHING;

ALTER TABLE hero_chess.piece_cosmetic ENABLE ROW LEVEL SECURITY;
ALTER TABLE hero_chess.player_piece_cosmetic ENABLE ROW LEVEL SECURITY;
ALTER TABLE hero_chess.player_piece_loadout ENABLE ROW LEVEL SECURITY;
COMMIT;
-- ===== END 21_piece_cosmetics.sql =====

-- ===== BEGIN 20_hero_catalog_and_starters.sql =====
-- One-time data rollout, after deploying the compatible API. Back up affected rows first.
-- Existing accounts keep other heroes and coins; 2000 starting coins are granted by onboarding only.
DO $catalog_rollout$
DECLARE entry record; replacement uuid;
BEGIN
 PERFORM pg_advisory_xact_lock(hashtext('hero_chess_catalog_starters_v1'));
 LOCK TABLE hero_chess.game_match, hero_chess.hero, hero_chess.lineup, hero_chess.lineup_entry, hero_chess.player_hero IN SHARE ROW EXCLUSIVE MODE;
 IF EXISTS (SELECT FROM hero_chess.game_match WHERE status NOT IN ('completed','cancelled')) THEN
  RAISE EXCEPTION 'Finish ongoing matches and selections before changing the catalogue.';
 END IF;

 CREATE TEMP TABLE rollout_catalog ON COMMIT DROP AS
 SELECT * FROM (VALUES
 ('tran-thanh-tong-general','tranthanhtong',0,true),('nguyen-nhac-general','nguyennhac',0,true),
 ('mac-dinh-chi-advisor','macdinhchi',0,true),('nguyen-trai-1-advisor','nguyentrai',0,true),
 ('nguyen-binh-khiem-advisor','nguyenbinhkhiem',0,true),('le-khoi-elephant','lekhoi',0,true),
 ('le-van-hung-elephant','levanhung',0,true),('tran-khanh-du-rook','trankhanhdu',0,true),
 ('tran-quang-dieu-rook','tranquangdieu',0,true),('tran-quoc-toan-horse','tranquoctoan',0,true),
 ('le-sat-horse','lesat',0,true),('nguyen-xi-cannon','nguyenxi',0,true),
 ('nguyen-van-tuyet-cannon','nguyenvantuyet',0,true),('linh-nha-tran-soldier','linhnhatran',0,true),
 ('tran-nhat-duat-advisor','trannhatduat',100,false),('le-lai-advisor','lelai',200,false),
 ('tran-thu-do-advisor','tranthudo',200,false),('nguyen-nhac-advisor','nguyennhac',200,false),
 ('nguyen-lu-advisor','nguyenlu_rig',200,false),('dinh-le-cannon','dinhle',100,false),
 ('ly-thuong-kiet-cannon','lythuongkiet',200,false),('yet-kieu-cannon','yetkeu',100,false),
 ('nguyen-van-loc-cannon','nguyenvanloc',100,false),('bui-thi-xuan-elephant','buithixuan',200,false),
 ('da-tuong-elephant','datuong',300,false),('pham-van-xao-elephant','phamvanxao',100,false),
 ('tran-binh-trong-elephant','tranbinhtrong',200,false),('tran-hung-dao-elephant','tranhungdao',300,false),
 ('trung-trac-elephant','trungtrac',300,false),('trung-nhi-elephant','trungnhi',300,false),
 ('le-loi-general','leloi',200,false),('nguyen-lu-general','nguyenlu_rig',100,false),
 ('quang-trung-general','quangtrung',300,false),('tran-hung-dao-general','tranhungdao',200,false),
 ('ly-van-buu-horse','lyvanbuu',100,false),('tran-quang-khai-horse','tranquangkhai',100,false),
 ('vo-dinh-tu-horse','vodinhtu',100,false),('ly-thuong-kiet-rook','lythuongkiet',200,false),
 ('pham-ngu-lao-rook','phamngulao',200,false),('tran-nguyen-han-rook','trannguyenhan',100,false),
 ('vo-van-dung-rook','vovandung',100,false),('linh-nha-le-soldier','linhnhale',100,false),
 ('linh-nha-tay-son-soldier','linhnhatayson',100,false)
 ) AS catalogue(code,model_key,price,starter);
 IF (SELECT count(*) FROM pg_temp.rollout_catalog c JOIN hero_chess.hero h USING(code))<>43 THEN
  RAISE EXCEPTION 'Expected 43 retained heroes; no catalogue changes applied.';
 END IF;

 CREATE TEMP TABLE rollout_removed ON COMMIT DROP AS SELECT * FROM hero_chess.hero
 WHERE code IN ('dinh-liet-rook','luu-nhan-chu-horse','tran-khat-chan-cannon','nguyen-trung-ngan-advisor');
 IF EXISTS (SELECT FROM hero_chess.coin_transaction t JOIN pg_temp.rollout_removed r ON r.id=t.hero_id)
 OR EXISTS (SELECT FROM hero_chess.gift_code g JOIN pg_temp.rollout_removed r ON r.id=g.reward_hero_id)
 OR EXISTS (SELECT FROM hero_chess.cosmetic c JOIN pg_temp.rollout_removed r ON r.id=c.hero_id) THEN
  RAISE EXCEPTION 'Removed heroes have purchase, gift or cosmetic references; review them before deletion.';
 END IF;

 CREATE TEMP TABLE rollout_lineups ON COMMIT DROP AS
 SELECT DISTINCT e.lineup_id FROM hero_chess.lineup_entry e JOIN pg_temp.rollout_removed r ON r.id=e.hero_id;
 FOR entry IN
  SELECT e.lineup_id,e.slot_no,e.hero_id,l.player_id,h.class_code,h.setup_points
  FROM hero_chess.lineup_entry e JOIN pg_temp.rollout_removed h ON h.id=e.hero_id
  JOIN hero_chess.lineup l ON l.id=e.lineup_id ORDER BY e.lineup_id,e.slot_no
 LOOP
  SELECT h.id INTO replacement FROM hero_chess.hero h JOIN pg_temp.rollout_catalog c ON c.code=h.code
  WHERE h.is_enabled AND c.model_key IS NOT NULL AND h.class_code=entry.class_code
   AND h.setup_points<=entry.setup_points AND h.trait_id IS NULL
   AND NOT EXISTS (SELECT FROM hero_chess.lineup_entry e JOIN hero_chess.hero other ON other.id=e.hero_id
     WHERE e.lineup_id=entry.lineup_id AND e.slot_no<>entry.slot_no
      AND (other.id=h.id OR other.character_id=h.character_id))
  ORDER BY EXISTS(SELECT FROM hero_chess.hero_faction old_f JOIN hero_chess.hero_faction new_f ON new_f.faction_id=old_f.faction_id
    WHERE old_f.hero_id=entry.hero_id AND new_f.hero_id=h.id) DESC,
   EXISTS(SELECT FROM hero_chess.player_hero p WHERE p.player_id=entry.player_id AND p.hero_id=h.id) DESC,h.code
  LIMIT 1;
  IF replacement IS NULL THEN RAISE EXCEPTION 'No safe replacement for hero % in lineup %',entry.hero_id,entry.lineup_id; END IF;
  INSERT INTO hero_chess.player_hero(player_id,hero_id,acquired_via)
  VALUES(entry.player_id,replacement,'admin') ON CONFLICT(player_id,hero_id) DO NOTHING;
  UPDATE hero_chess.lineup_entry SET hero_id=replacement WHERE lineup_id=entry.lineup_id AND slot_no=entry.slot_no;
 END LOOP;
 UPDATE hero_chess.lineup l SET revision=revision+1
 WHERE EXISTS(SELECT FROM pg_temp.rollout_lineups x WHERE x.lineup_id=l.id);
 -- Saved faction choices must still be eligible after replacements; never silently change a skill.
 IF EXISTS (
  SELECT FROM pg_temp.rollout_lineups x JOIN hero_chess.lineup_skill ls ON ls.lineup_id=x.lineup_id
  JOIN hero_chess.team_skill s ON s.id=ls.team_skill_id WHERE s.faction_id IS NOT NULL
  AND (SELECT count(DISTINCT h.id) FROM hero_chess.lineup_entry e JOIN hero_chess.hero h ON h.id=e.hero_id
   JOIN hero_chess.hero_faction f ON f.hero_id=h.id AND f.faction_id=s.faction_id
   WHERE e.lineup_id=x.lineup_id AND h.class_code<>'SOLDIER') < (s.eligibility->>'minNonSoldierFactionHeroes')::int
 ) THEN RAISE EXCEPTION 'Replacement would invalidate a saved faction skill; review affected lineups.'; END IF;

 -- Also protect owners who have not saved a lineup yet from losing their last playable class.
 FOR entry IN SELECT DISTINCT p.player_id,r.class_code
  FROM hero_chess.player_hero p JOIN pg_temp.rollout_removed r ON r.id=p.hero_id
 LOOP
  WHILE (SELECT count(*) FROM hero_chess.player_hero p JOIN hero_chess.hero h ON h.id=p.hero_id
    JOIN pg_temp.rollout_catalog c ON c.code=h.code
    WHERE p.player_id=entry.player_id AND h.class_code=entry.class_code AND h.is_enabled AND c.model_key IS NOT NULL)
   < (SELECT required_count FROM hero_chess.chess_class WHERE code=entry.class_code)
  LOOP
   SELECT h.id INTO replacement FROM hero_chess.hero h JOIN pg_temp.rollout_catalog c ON c.code=h.code
   WHERE h.is_enabled AND h.class_code=entry.class_code AND h.trait_id IS NULL AND c.model_key IS NOT NULL
    AND NOT EXISTS(SELECT FROM hero_chess.player_hero p WHERE p.player_id=entry.player_id AND p.hero_id=h.id)
   ORDER BY c.starter DESC,h.code LIMIT 1;
   IF replacement IS NULL THEN RAISE EXCEPTION 'Cannot preserve class % for player %',entry.class_code,entry.player_id; END IF;
   INSERT INTO hero_chess.player_hero(player_id,hero_id,acquired_via) VALUES(entry.player_id,replacement,'admin');
  END LOOP;
 END LOOP;

 IF to_regclass('hero_chess.starter_hero_config') IS NOT NULL THEN
  EXECUTE 'DELETE FROM hero_chess.starter_hero_config';
 END IF;
 DELETE FROM hero_chess.player_hero p USING pg_temp.rollout_removed r WHERE p.hero_id=r.id;
 DELETE FROM hero_chess.hero_faction f USING pg_temp.rollout_removed r WHERE f.hero_id=r.id;
 DELETE FROM hero_chess.hero h USING pg_temp.rollout_removed r WHERE h.id=r.id;

 UPDATE hero_chess.hero SET is_starter=false WHERE is_starter;
 UPDATE hero_chess.hero h SET coin_price=c.price,is_starter=c.starter,
  is_in_shop=NOT c.starter AND c.model_key IS NOT NULL,asset_key=c.model_key
 FROM pg_temp.rollout_catalog c WHERE h.code=c.code;
 IF to_regclass('hero_chess.starter_hero_config') IS NOT NULL THEN
  EXECUTE 'INSERT INTO hero_chess.starter_hero_config(hero_id,position)
   SELECT id,row_number() OVER(ORDER BY code)::int FROM hero_chess.hero WHERE is_starter';
 END IF;
 DROP TABLE pg_temp.rollout_lineups,pg_temp.rollout_removed,pg_temp.rollout_catalog;
END
$catalog_rollout$;

-- Independent read-only postflight; can be run again without reapplying prices.
SELECT code,class_code,setup_points,coin_price,is_starter,is_in_shop,asset_key
FROM hero_chess.hero WHERE is_enabled AND NOT is_test_fixture ORDER BY class_code,code;
-- ===== END 20_hero_catalog_and_starters.sql =====

-- ===== BEGIN 16_faction_skills.sql =====
-- Run after 13_hero_factions.sql and deploy a backend with the three handlers first.
BEGIN;

DO $$
BEGIN
    IF (SELECT count(*) FROM hero_chess.faction WHERE code IN ('tran', 'hau-le', 'tay-son')) <> 3 THEN
        RAISE EXCEPTION 'Run 13_hero_factions.sql before inserting faction skills.';
    END IF;
END $$;

INSERT INTO hero_chess.team_skill
    (code, name, faction_id, implementation_key, cooldown_turns, is_enabled, eligibility, description)
SELECT skill.code, skill.name, faction.id, skill.code, skill.cooldown, true, '{"minNonSoldierFactionHeroes":5}'::jsonb, skill.description
FROM (VALUES
    ('tran', 'bach_dang_tran_phap', 'Bạch Đằng Trận Pháp', 8,
        'Đặt ba bẫy ẩn trên sông trong 6 lượt. Quân địch đi qua bị khóa chân 4 lượt. Mỗi bẫy dùng một lần.'),
    ('hau-le', 'lam_son_quan_luoc', 'Lam Sơn Quân Lược', 16,
        'Xem riêng tối đa ba hành động dự đoán và hai phương án nguy hiểm của địch ở lượt kế tiếp.'),
    ('tay-son', 'hich_xuat_quan', 'Hịch Xuất Quân', 12,
        'Trong 6 lượt, Tốt phe mình có thể đi thêm một ô theo hướng hợp lệ nếu ô trung gian trống.')
) AS skill(faction_code, code, name, cooldown, description)
JOIN hero_chess.faction faction ON faction.code = skill.faction_code
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name, faction_id = EXCLUDED.faction_id,
    implementation_key = EXCLUDED.implementation_key, cooldown_turns = EXCLUDED.cooldown_turns,
    eligibility = EXCLUDED.eligibility, description = EXCLUDED.description
WHERE NOT EXISTS (SELECT 1 FROM hero_chess.ruleset WHERE is_active AND config->>'skillTimingVersion'='2');

COMMIT;
-- ===== END 16_faction_skills.sql =====

-- ===== BEGIN 23_skill_timers.sql =====
-- Run manually on an existing DB after the API and clients support skillTimingVersion 2.
-- One-time balance operation; startup only executes it for a newly created database.
DO $skill_timers$
BEGIN
 PERFORM pg_advisory_xact_lock(hashtext('hero_chess_skill_timers_v2'));
 PERFORM set_config('lock_timeout','5s',true);
 LOCK TABLE hero_chess.team_skill,hero_chess.hero_trait,hero_chess.ruleset IN SHARE ROW EXCLUSIVE MODE;
 IF (SELECT count(*) FROM hero_chess.ruleset WHERE is_active) <> 1 THEN
  RAISE EXCEPTION 'Expected exactly one active ruleset. No timers were changed.';
 END IF;
 IF EXISTS(SELECT 1 FROM hero_chess.ruleset WHERE is_active AND config->>'skillTimingVersion'='2') THEN
  RETURN; -- Preserve deliberate tuning after this rollout, including non-default timer values.
 END IF;
 IF EXISTS(SELECT 1 FROM hero_chess.ruleset WHERE is_active AND
    config ? 'skillTimingVersion' AND config->>'skillTimingVersion' IS DISTINCT FROM '1') THEN
  RAISE EXCEPTION 'The ruleset has an unexpected skill timing version.';
 END IF;

 CREATE TEMP TABLE timer_team ON COMMIT DROP AS SELECT * FROM (VALUES
  ('thanh',14,13,6,NULL::integer),('rao',10,9,4,NULL::integer),('khien',14,13,4,NULL::integer),
  ('bach_dang_tran_phap',8,7,6,4),('lam_son_quan_luoc',16,15,NULL::integer,NULL::integer),
  ('hich_xuat_quan',12,11,6,NULL::integer)
 ) AS config(key,old_cd,new_cd,duration,root_duration);
 CREATE TEMP TABLE timer_hero ON COMMIT DROP AS SELECT * FROM (VALUES
  ('quang_trung.special_move',3,7,NULL::integer),('tran_hung_dao_tuong.coc',5,5,3),
  ('le_lai.swap_general',8,7,NULL::integer),('tran_thu_do.general_shield',6,5,2)
 ) AS config(key,old_cd,new_cd,duration);
 IF (SELECT count(*) FROM hero_chess.team_skill s JOIN timer_team t ON t.key=s.implementation_key WHERE s.is_enabled) <> 6 OR
    (SELECT count(*) FROM hero_chess.hero_trait h JOIN timer_hero t ON t.key=h.implementation_key) <> 4 THEN
  RAISE EXCEPTION 'Expected six enabled team skills and four active hero traits. Run prerequisite catalog migrations first.';
 END IF;
 IF EXISTS(SELECT 1 FROM hero_chess.team_skill s JOIN timer_team t ON t.key=s.implementation_key
    WHERE jsonb_typeof(s.parameters) <> 'object' OR s.cooldown_turns IS NULL OR s.cooldown_turns NOT IN(t.old_cd,t.new_cd) OR
      (t.duration IS NOT NULL AND s.parameters ? 'durationTurns' AND s.parameters->'durationTurns' IS DISTINCT FROM to_jsonb(t.duration)) OR
      (t.root_duration IS NOT NULL AND s.parameters ? 'rootDurationTurns' AND s.parameters->'rootDurationTurns' IS DISTINCT FROM to_jsonb(t.root_duration))) OR
    EXISTS(SELECT 1 FROM hero_chess.hero_trait h JOIN timer_hero t ON t.key=h.implementation_key
    WHERE jsonb_typeof(h.parameters) <> 'object' OR h.parameters->'cooldownTurns' IS NULL OR
      h.parameters->'cooldownTurns' NOT IN(to_jsonb(t.old_cd),to_jsonb(t.new_cd)) OR
      (t.duration IS NOT NULL AND h.parameters ? 'durationTurns' AND h.parameters->'durationTurns' IS DISTINCT FROM to_jsonb(t.duration))) THEN
  RAISE EXCEPTION 'Timers differ from the reviewed old/new values. Review the catalogue before applying this balance patch.';
 END IF;

 UPDATE hero_chess.team_skill s SET cooldown_turns=t.new_cd,
  parameters=s.parameters || CASE WHEN t.duration IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('durationTurns',t.duration) END ||
    CASE WHEN t.root_duration IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('rootDurationTurns',t.root_duration) END,
  description=regexp_replace(regexp_replace(regexp_replace(regexp_replace(s.description,
    '; tồn tại [0-9]+ lượt chung','','gi'),
    'Trong [0-9]+ lượt,','Trong thời gian hiệu lực,','g'),
    'trên sông trong [0-9]+ lượt','trên sông có thời hạn','g'),
    'khóa chân [0-9]+ lượt','khóa chân tạm thời','g')
 FROM timer_team t WHERE s.implementation_key=t.key;
 UPDATE hero_chess.hero_trait h SET parameters=h.parameters || jsonb_build_object('cooldownTurns',t.new_cd) ||
    CASE WHEN t.duration IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('durationTurns',t.duration) END,
  description=btrim(regexp_replace(regexp_replace(h.description,
    'Tồn tại [0-9]+ lượt chung, hồi chiêu [0-9]+;?','','gi'),
    '; hồi chiêu [0-9]+ lượt chung','','gi'))
 FROM timer_hero t WHERE h.implementation_key=t.key;
 UPDATE hero_chess.ruleset SET config=config || '{"skillTimingVersion":2}'::jsonb WHERE is_active;
 DROP TABLE pg_temp.timer_team,pg_temp.timer_hero;
END
$skill_timers$;

SELECT code,cooldown_turns,parameters FROM hero_chess.team_skill WHERE is_enabled ORDER BY code;
-- ===== END 23_skill_timers.sql =====
