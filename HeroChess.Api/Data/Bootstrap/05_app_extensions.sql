-- Vai trò file: Bổ sung idempotency_key và unique index cho player_hero để retry mua miễn phí cũng được nhận diện; script có thể chạy lại.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
ALTER TABLE player_hero ADD COLUMN IF NOT EXISTS idempotency_key varchar(160);
CREATE UNIQUE INDEX IF NOT EXISTS uq_player_hero_idempotency
    ON player_hero(player_id,idempotency_key) WHERE idempotency_key IS NOT NULL;
COMMIT;
