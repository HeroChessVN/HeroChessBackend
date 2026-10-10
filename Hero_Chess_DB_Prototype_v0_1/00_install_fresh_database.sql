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
 kind varchar(24) NOT NULL CHECK (kind IN ('start','move','hero_active','team_skill','timeout','resign','undo','cancel')),
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
-- Hero skill handlers are NOT in scope for this seed; trait_id = NULL for heroes
--   without a confirmed custom movement (Le Loi, Trung Trac, Trung Nhi, etc.).
-- CHECK constraint on hero(setup_points): non-GENERAL heroes require SP >= 1.
--   Tuong SP modifiers (-1d, -2d) deferred pending lineup validation design.
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

-- D. hero: Full 44-entry roster (45 minus duplicate Nguyen Trai).
--    trait_id:
--      Tran Binh Trong  -> 30000000-0000-4000-8000-000000000001
--      Bui Thi Xuan    -> 327e712d-26d2-5da4-5c9a-87a1ff74c1e1
--      Da Tuong        -> 30000000-0000-4000-8000-000000000002
--      all others       -> NULL
--    is_enabled = false: not released. is_test_fixture = false. coin_price = 0.
--    CLASS CODES: Tuong/Vua entries stored as GENERAL (no VUA class code).
--    SP: Tướng variants stored as 0 (baseline); note modifiers pending lineup validation.
--    Tran Hung Dao (Tuong variant): stored 0; design note -1d requires SP modifier design.
--    Tran Hung Dao (Tuong2/ELEPHANT): design note 3d stored as 0; CHECK requires SP>=1 for non-GENERAL.
--    Nguyen Trai: two character rows, two hero rows.
--    Nguyen Nhac / Nguyen Lu: Si variant + Tuong variant preserved separately.
--    Tran Hung Dao: Tuong variant + Tuong2 (ELEPHANT) variant preserved separately.
--    Ly Thuong Kiet: Xe variant + Phao variant preserved separately.
--    Red-marked (Nguyen Trung Ngan, Dinh Liet, Tran Khat Chan, Luu Nhan Chu):
--      no gameplay meaning assigned in this step.
--    Pham Ngu Lao: note "8d( co the 9d)" stored as 8d; 9d ambiguity noted.
INSERT INTO hero(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,description)
SELECT CASE WHEN EXISTS (SELECT 1 FROM hero existing WHERE existing.id = seeded.id::uuid AND existing.code <> seeded.code)
            THEN gen_random_uuid() ELSE seeded.id::uuid END,
       seeded.code, actual_character.id, seeded.class_code, seeded.trait_id::uuid, seeded.name,
       seeded.setup_points, true, seeded.is_test_fixture, seeded.coin_price, seeded.description
FROM (VALUES
 -- === SI (ADVISOR) ===
 ('60000000-0000-4000-8000-000000000001','nguyen-trai-1-advisor','50000000-0000-4000-8000-000000000001','ADVISOR',NULL,'Nguyen Trai',1,false,false,0,'Si 1d; base ADVISOR movement. First Nguyen Trai entry from note.'),
 ('60000000-0000-4000-8000-000000000002','nguyen-trung-ngan-advisor','50000000-0000-4000-8000-000000000002','ADVISOR',NULL,'Nguyen Trung Ngan',1,false,false,0,'Si 1d; red-marked - no gameplay meaning assigned.'),
 ('60000000-0000-4000-8000-000000000003','nguyen-binh-khiem-advisor','50000000-0000-4000-8000-000000000003','ADVISOR',NULL,'Nguyen Binh Khiem',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000004','mac-dinh-chi-advisor','50000000-0000-4000-8000-000000000004','ADVISOR',NULL,'Mac Dinh Chi',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000005','tran-thu-do-advisor','50000000-0000-4000-8000-000000000005','ADVISOR',NULL,'Tran Thu Do',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000006','tran-nhat-duat-advisor','50000000-0000-4000-8000-000000000006','ADVISOR',NULL,'Tran Nhat Duat',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000007','le-lai-advisor','50000000-0000-4000-8000-000000000007','ADVISOR',NULL,'Le Lai',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000008','nguyen-nhac-advisor','50000000-0000-4000-8000-000000000009','ADVISOR',NULL,'Nguyen Nhac',1,false,false,0,'Si 1d; Nguyen Nhac also has Tuong variant (0d, GENERAL). No custom rule.'),
 ('60000000-0000-4000-8000-000000000010','nguyen-lu-advisor','50000000-0000-4000-8000-000000000010','ADVISOR',NULL,'Nguyen Lu',1,false,false,0,'Si 1d; Nguyen Lu also has Tuong variant (0d, GENERAL). No custom rule.'),
 -- === TUONG / VUA (GENERAL) ===
 -- Note: Tuong/Vua stored as GENERAL (no VUA class code exists).
 -- SP modifiers (-1d, -2d) stored as 0 pending lineup validation design.
 ('60000000-0000-4000-8000-000000000011','tran-hung-dao-general','50000000-0000-4000-8000-000000000011','GENERAL','30000000-0000-4000-8000-000000000006','Tran Hung Dao',0,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.orthogonal_range_1_no_palace IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000012','le-loi-general','50000000-0000-4000-8000-000000000012','GENERAL','30000000-0000-4000-8000-000000000004','Le Loi',0,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.king_move IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000013','quang-trung-general','50000000-0000-4000-8000-000000000013','GENERAL','30000000-0000-4000-8000-000000000005','Quang Trung',0,false,false,0,'Tướng đi thường 1 ô thẳng, được ra khỏi cung, không qua sông; skill đi thẳng 1–3 ô với CD 3.'),
 ('60000000-0000-4000-8000-000000000014','nguyen-nhac-general','50000000-0000-4000-8000-000000000009','GENERAL',NULL,'Nguyen Nhac',0,false,false,0,'Tuong 0d; Nguyen Nhac also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000015','nguyen-lu-general','50000000-0000-4000-8000-000000000010','GENERAL',NULL,'Nguyen Lu',0,false,false,0,'Tuong 0d; Nguyen Lu also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000016','tran-thanh-tong-general','50000000-0000-4000-8000-000000000016','GENERAL',NULL,'Tran Thanh Tong',0,false,false,0,'Tuong 0d; no custom rule noted.'),
 -- === TUONG (ELEPHANT) ===
 -- Da Tuong: trait elephant.alternating_distance (existing ID).
 -- Tran Binh Trong: trait elephant.diagonal_range (existing ID).
 -- Bui Thi Xuan: trait elephant.diagonal_range (existing BXU variant ID).
 -- Tran Hung Dao (ELEPHANT): design note 3d. SP >= 1 satisfied by value 3.
 ('60000000-0000-4000-8000-000000000017','tran-hung-dao-elephant','50000000-0000-4000-8000-000000000011','ELEPHANT',NULL,'Tran Hung Dao',3,false,false,0,'Tuong 3d; Bạch Đằng Giang active skill.'),
 ('60000000-0000-4000-8000-000000000018','da-tuong-elephant','50000000-0000-4000-8000-000000000018','ELEPHANT','30000000-0000-4000-8000-000000000002','Da Tuong',3,false,false,0,'Tuong 3d; movement: elephant.alternating_distance. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000019','tran-binh-trong-elephant','50000000-0000-4000-8000-000000000019','ELEPHANT','30000000-0000-4000-8000-000000000001','Tran Binh Trong',2,false,false,0,'Tuong 2d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000020','le-khoi-elephant','50000000-0000-4000-8000-000000000020','ELEPHANT',NULL,'Le Khoi',1,false,false,0,'Tuong 1d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000021','pham-van-xao-elephant','50000000-0000-4000-8000-000000000021','ELEPHANT',NULL,'Pham Van Xao',2,false,false,0,'Tuong 2d; no custom movement noted; base ELEPHANT movement.'),
 ('60000000-0000-4000-8000-000000000022','bui-thi-xuan-elephant','50000000-0000-4000-8000-000000000022','ELEPHANT','327e712d-26d2-5da4-5c9a-87a1ff74c1e1','Bui Thi Xuan',2,false,false,0,'Tuong 2d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000023','le-van-hung-elephant','50000000-0000-4000-8000-000000000023','ELEPHANT',NULL,'Le Van Hung',1,false,false,0,'Tuong 1d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000024','trung-trac-elephant','50000000-0000-4000-8000-000000000024','ELEPHANT','30000000-0000-4000-8000-000000000003','Trung Trac',3,false,false,0,'Tuong 3d; movement: elephant.river_crossing IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000025','trung-nhi-elephant','50000000-0000-4000-8000-000000000025','ELEPHANT','30000000-0000-4000-8000-000000000010','Trung Nhi',3,false,false,0,'Tuong 3d; movement: elephant.river_crossing IMPLEMENTED.'),
 -- === XE (ROOK) ===
 ('60000000-0000-4000-8000-000000000026','pham-ngu-lao-rook','50000000-0000-4000-8000-000000000026','ROOK','30000000-0000-4000-8000-000000000011','Pham Ngu Lao',8,false,false,0,'Xe 8 SP. Hoành Sóc Giang Sơn: sau khi ăn một quân, nước đi tiếp theo có thể xuyên một quân đồng minh.'),
 ('60000000-0000-4000-8000-000000000027','tran-khanh-du-rook','50000000-0000-4000-8000-000000000027','ROOK',NULL,'Tran Khanh Du',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000028','ly-thuong-kiet-rook','50000000-0000-4000-8000-000000000028','ROOK',NULL,'Ly Thuong Kiet',8,false,false,0,'Xe 8 SP; đi qua Thành/Rào hoặc vào ô đó để phá.'),
 ('60000000-0000-4000-8000-000000000029','dinh-liet-rook','50000000-0000-4000-8000-000000000029','ROOK',NULL,'Dinh Liet',7,false,false,0,'Xe 7d; red-marked - no gameplay meaning. Base ROOK movement.'),
 ('60000000-0000-4000-8000-000000000030','tran-nguyen-han-rook','50000000-0000-4000-8000-000000000030','ROOK',NULL,'Tran Nguyen Han',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000031','vo-van-dung-rook','50000000-0000-4000-8000-000000000031','ROOK',NULL,'Vo Van Dung',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000032','tran-quang-dieu-rook','50000000-0000-4000-8000-000000000032','ROOK',NULL,'Tran Quang Dieu',7,false,false,0,'Xe 7d; design note xe* (star meaning unclear). Assumed base ROOK movement.'),
 -- === PHAO (CANNON) ===
 ('60000000-0000-4000-8000-000000000033','tran-khat-chan-cannon','50000000-0000-4000-8000-000000000033','CANNON',NULL,'Tran Khat Chan',6,false,false,0,'Phao 6d; red-marked - no gameplay meaning. Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000034','yet-kieu-cannon','50000000-0000-4000-8000-000000000034','CANNON',NULL,'Yet Kieu',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000035','ly-thuong-kiet-cannon','50000000-0000-4000-8000-000000000028','CANNON',NULL,'Ly Thuong Kiet',6,false,false,0,'Pháo 6 SP; dùng Thành hoặc Rào làm ngòi, có thể vào ô Thành để phá.'),
 ('60000000-0000-4000-8000-000000000036','dinh-le-cannon','50000000-0000-4000-8000-000000000036','CANNON',NULL,'Dinh Le',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000037','nguyen-xi-cannon','50000000-0000-4000-8000-000000000037','CANNON',NULL,'Nguyen Xi',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000038','nguyen-van-tuyet-cannon','50000000-0000-4000-8000-000000000038','CANNON',NULL,'Nguyen Van Tuyet',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000039','nguyen-van-loc-cannon','50000000-0000-4000-8000-000000000039','CANNON',NULL,'Nguyen Van Loc',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 -- === MA (HORSE) ===
 ('60000000-0000-4000-8000-000000000040','tran-quoc-toan-horse','50000000-0000-4000-8000-000000000040','HORSE',NULL,'Tran Quoc Toan',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000041','tran-quang-khai-horse','50000000-0000-4000-8000-000000000041','HORSE',NULL,'Tran Quang Khai',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000042','luu-nhan-chu-horse','50000000-0000-4000-8000-000000000042','HORSE',NULL,'Luu Nhan Chu',4,false,false,0,'Ma 4d; red-marked - no gameplay meaning. Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000043','le-sat-horse','50000000-0000-4000-8000-000000000043','HORSE',NULL,'Le Sat',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000044','ly-van-bu-horse','50000000-0000-4000-8000-000000000044','HORSE',NULL,'Ly Van Bu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000045','vo-dinh-tu-horse','50000000-0000-4000-8000-000000000045','HORSE',NULL,'Vo Dinh Tu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'))
 AS seeded(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,description)
 JOIN seed_characters expected_character ON expected_character.seed_id::uuid = seeded.character_id::uuid
 JOIN historical_character actual_character ON actual_character.code = expected_character.code
 WHERE true
 ON CONFLICT (code) DO UPDATE SET character_id = EXCLUDED.character_id, class_code = EXCLUDED.class_code,
   trait_id = EXCLUDED.trait_id, name = EXCLUDED.name,
   description = EXCLUDED.description, is_enabled = true;
COMMIT;
-- ===== END 02_seed_catalog.sql =====

-- ===== BEGIN 08_step6_skills.sql =====
-- Step 6: Thành and Rào Command Skills + Trần Hưng Đạo — Tượng Hero Skill.
-- These skills are wired to handler implementations in HeroChess.Rules.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

-- Step 6: Thành Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000010','thanh','Thành','thanh',8,true,false,
  'Tạo 1 Thành ở vị trí bất kỳ bên sông mình; tồn tại 6 lượt chung, hồi chiêu 8. Pháo phá Thành và tiến vào ô đó; Thành không làm ngòi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

-- Step 6: Rào Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000011','rao','Rào','rao',6,true,false,
  'Tạo 1 Rào ở vị trí bất kỳ bên sông mình; tồn tại 4 lượt chung, hồi chiêu 6. Pháo có thể dùng Rào làm ngòi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

-- Step 6: Trần Hưng Đạo — Tượng Hero Skill (team_skill slot for the hero's own skill)
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000009','bach-dang-giang','Bạch Đằng Giang','active','tran_hung_dao_tuong.coc',
  '{"cooldownTurns":5,"durationTurns":3}',
  'Tạo 1 Cọc ẩn trên hai hàng sông; quân địch dừng ở ô Cọc bị ăn. Tồn tại 3 lượt chung, hồi chiêu 5; không thể thu hồi.')
ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,implementation_key=EXCLUDED.implementation_key,
  parameters=EXCLUDED.parameters,description=EXCLUDED.description;
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000009' WHERE code='tran-hung-dao-elephant';

INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000013','khien','Khiên','khien',8,true,false,
  'Tạo 1 Khiên cho quân đồng minh bất kỳ trên bàn cờ; tồn tại 4 lượt chung, hồi chiêu 8. Quân có Khiên không thể bị ăn.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

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

COMMIT;
-- ===== END 08_step6_skills.sql =====

-- ===== BEGIN 09_hero_movement_fixes.sql =====
-- Re-runnable catalog patch. Apply after 02_seed_catalog and 08_step6_skills.
-- Intentionally leaves setup_points and existing hero IDs/ownership untouched.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

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
UPDATE hero SET description='Pháo 6 SP; dùng Thành hoặc Rào làm ngòi, có thể vào ô Thành để phá.'
 WHERE code='ly-thuong-kiet-cannon';
UPDATE hero SET description='Tướng đi thường 1 ô thẳng, được ra khỏi cung, không qua sông; skill đi thẳng 1–3 ô với CD 3.'
 WHERE code='quang-trung-general';
COMMIT;
-- ===== END 09_hero_movement_fixes.sql =====
