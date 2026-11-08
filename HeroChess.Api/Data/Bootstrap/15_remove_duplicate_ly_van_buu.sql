-- Run manually on Supabase after 13_hero_factions.sql.
BEGIN;

DO $$
DECLARE
    old_id CONSTANT uuid := '80226001-b811-40cf-b435-edf35757edb7';
    kept_id uuid;
BEGIN
    SELECT id INTO kept_id FROM hero_chess.hero
    WHERE code = 'ly-van-buu-horse' AND class_code = 'HORSE';
    IF kept_id IS NULL
        OR NOT EXISTS (SELECT FROM hero_chess.faction WHERE code = 'tay-son') THEN
        RAISE EXCEPTION 'The retained hero and the Tay Son faction must exist first.';
    END IF;
    IF NOT EXISTS (SELECT FROM hero_chess.hero WHERE id = old_id AND code = 'ly-van-bu-horse') THEN
        RETURN;
    END IF;

    IF EXISTS (
        SELECT FROM hero_chess.lineup_entry old_entry
        JOIN hero_chess.lineup_entry kept_entry USING (lineup_id)
        WHERE old_entry.hero_id = old_id AND kept_entry.hero_id = kept_id
    ) THEN
        RAISE EXCEPTION 'A lineup contains both versions. Replace one horse before retrying.';
    END IF;

    DELETE FROM hero_chess.player_hero old_owner
    USING hero_chess.player_hero kept_owner
    WHERE old_owner.hero_id = old_id AND kept_owner.hero_id = kept_id
      AND old_owner.player_id = kept_owner.player_id;

    UPDATE hero_chess.player_hero SET hero_id = kept_id WHERE hero_id = old_id;
    UPDATE hero_chess.lineup_entry SET hero_id = kept_id WHERE hero_id = old_id;
    UPDATE hero_chess.coin_transaction SET hero_id = kept_id WHERE hero_id = old_id;
END $$;

INSERT INTO hero_chess.hero_faction (hero_id, faction_id)
SELECT h.id, f.id
FROM hero_chess.hero h
CROSS JOIN hero_chess.faction f
WHERE h.code = 'ly-van-buu-horse' AND f.code = 'tay-son'
ON CONFLICT (hero_id, faction_id) DO NOTHING;

DELETE FROM hero_chess.hero_faction hf
USING hero_chess.hero h
WHERE hf.hero_id = h.id
  AND h.id = '80226001-b811-40cf-b435-edf35757edb7'
  AND h.code = 'ly-van-bu-horse';

DELETE FROM hero_chess.hero
WHERE id = '80226001-b811-40cf-b435-edf35757edb7'
  AND code = 'ly-van-bu-horse';

COMMIT;

SELECT h.id, h.code, h.name, f.name AS faction
FROM hero_chess.hero h
LEFT JOIN hero_chess.hero_faction hf ON hf.hero_id = h.id
LEFT JOIN hero_chess.faction f ON f.id = hf.faction_id
WHERE h.code IN ('ly-van-bu-horse', 'ly-van-buu-horse');
