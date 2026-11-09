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
 (kind='board_cosmetic_purchase' AND board_cosmetic_id IS NOT NULL AND amount<0
   AND hero_id IS NULL AND cosmetic_id IS NULL AND match_id IS NULL AND match_side IS NULL)
 OR (board_cosmetic_id IS NULL AND (
  (kind='match_reward' AND match_id IS NOT NULL AND match_side IS NOT NULL AND amount>0 AND hero_id IS NULL AND cosmetic_id IS NULL)
  OR (kind='hero_purchase' AND hero_id IS NOT NULL AND cosmetic_id IS NULL AND amount<0 AND match_id IS NULL AND match_side IS NULL)
  OR (kind='cosmetic_purchase' AND cosmetic_id IS NOT NULL AND hero_id IS NULL AND amount<0 AND match_id IS NULL AND match_side IS NULL)
  OR (kind IN ('starter','admin_adjustment') AND match_id IS NULL AND match_side IS NULL AND hero_id IS NULL AND cosmetic_id IS NULL
      AND (kind<>'starter' OR amount>0))
 ))
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
