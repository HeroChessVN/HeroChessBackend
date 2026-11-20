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
