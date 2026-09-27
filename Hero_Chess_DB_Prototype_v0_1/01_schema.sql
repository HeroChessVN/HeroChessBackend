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
