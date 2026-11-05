-- Apply after 06_user_inheritance.sql. Rooms and rematch records outlive individual matches.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_custom_rooms_v1'));

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'game_match'::regclass AND conname = 'ck_game_match_mode_custom') THEN
        ALTER TABLE game_match DROP CONSTRAINT game_match_mode_check,
            ADD CONSTRAINT ck_game_match_mode_custom CHECK (mode IN ('ranked', 'bot', 'custom'));
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS custom_room (
    id uuid PRIMARY KEY,
    code varchar(8) COLLATE "C" NOT NULL CHECK (code ~ '^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{8}$'),
    owner_id uuid NOT NULL REFERENCES user_account(id),
    current_match_id uuid REFERENCES game_match(id),
    created_at timestamptz NOT NULL,
    closed_at timestamptz,
    CONSTRAINT uq_custom_room_code UNIQUE (code)
);

CREATE TABLE IF NOT EXISTS custom_room_member (
    room_id uuid NOT NULL REFERENCES custom_room(id) ON DELETE CASCADE,
    player_id uuid NOT NULL REFERENCES user_account(id),
    seat smallint NOT NULL CHECK (seat IN (1, 2)),
    last_seen_at timestamptz NOT NULL,
    CONSTRAINT pk_custom_room_member PRIMARY KEY (player_id),
    CONSTRAINT uq_custom_room_member_seat UNIQUE (room_id, seat)
);

CREATE TABLE IF NOT EXISTS custom_room_rematch (
    room_id uuid NOT NULL REFERENCES custom_room(id) ON DELETE CASCADE,
    request_id uuid NOT NULL,
    previous_match_id uuid NOT NULL REFERENCES game_match(id),
    match_id uuid NOT NULL REFERENCES game_match(id),
    CONSTRAINT pk_custom_room_rematch PRIMARY KEY (room_id, request_id),
    CONSTRAINT uq_custom_room_rematch_previous UNIQUE (room_id, previous_match_id)
);

-- Keep this check independent of the ranked/bot trigger recreated by migration 06 on startup.
CREATE OR REPLACE FUNCTION validate_custom_match_roster() RETURNS trigger LANGUAGE plpgsql
SET search_path = hero_chess, pg_catalog AS $$
DECLARE match_ids uuid[];
BEGIN
    IF TG_TABLE_NAME = 'game_match' THEN
        match_ids := ARRAY[NEW.id];
    ELSE
        match_ids := ARRAY[OLD.match_id, NEW.match_id];
    END IF;

    IF EXISTS (
        SELECT 1 FROM game_match m WHERE m.id = ANY(match_ids) AND m.mode = 'custom'
            AND (SELECT count(*) FROM match_participant p WHERE p.match_id = m.id AND p.participant_type = 'human') <> 2
    ) THEN
        RAISE EXCEPTION 'Custom matches require two human players' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = 'game_match'::regclass AND tgname = 'check_custom_roster_on_match') THEN
        CREATE CONSTRAINT TRIGGER check_custom_roster_on_match AFTER INSERT OR UPDATE OF mode ON game_match
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW.mode = 'custom') EXECUTE FUNCTION validate_custom_match_roster();
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = 'match_participant'::regclass AND tgname = 'check_custom_roster_on_participant') THEN
        CREATE CONSTRAINT TRIGGER check_custom_roster_on_participant
            AFTER INSERT OR UPDATE OF match_id, participant_type OR DELETE ON match_participant
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_custom_match_roster();
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = 'custom_room_member'::regclass AND tgname = 'require_player_owner') THEN
        CREATE TRIGGER require_player_owner BEFORE INSERT OR UPDATE OF player_id ON custom_room_member
            FOR EACH ROW EXECUTE FUNCTION require_player_owner();
    END IF;
END $$;

REVOKE ALL ON custom_room, custom_room_member, custom_room_rematch FROM PUBLIC;
REVOKE ALL ON FUNCTION validate_custom_match_roster() FROM PUBLIC;
DO $$ DECLARE role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY['anon', 'authenticated'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('REVOKE ALL ON custom_room, custom_room_member, custom_room_rematch FROM %I', role_name);
            EXECUTE format('REVOKE ALL ON FUNCTION validate_custom_match_roster() FROM %I', role_name);
        END IF;
    END LOOP;
END $$;
COMMIT;
