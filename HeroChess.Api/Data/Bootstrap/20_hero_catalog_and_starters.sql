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
