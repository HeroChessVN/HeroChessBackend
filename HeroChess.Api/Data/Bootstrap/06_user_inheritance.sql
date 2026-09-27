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
