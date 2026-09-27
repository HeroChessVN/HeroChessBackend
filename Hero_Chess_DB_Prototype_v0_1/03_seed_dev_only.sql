-- Vai trò file: Overlay fixture Development phục vụ demo/test; không coi fixture là 20 hero sản phẩm hoàn chỉnh.
-- OPTIONAL: disposable development DB only. Not release content; no login credentials.
-- Creates 2 registered profiles, 16 distinct fixture heroes, 3 mock skill choices,
-- and valid-shaped 43 SP lineups. None of these skill choices implements gameplay.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
INSERT INTO user_account(id,display_name,is_guest) VALUES
 ('50000000-0000-4000-8000-000000000001','DEV Player A',false),
 ('50000000-0000-4000-8000-000000000002','DEV Player B',false) ON CONFLICT DO NOTHING;
INSERT INTO player_wallet(player_id) SELECT id FROM user_account WHERE id IN
 ('50000000-0000-4000-8000-000000000001','50000000-0000-4000-8000-000000000002') ON CONFLICT DO NOTHING;
-- 1000 is a technical fixture value, not a confirmed starting Elo.
INSERT INTO player_rating(player_id,elo) SELECT id,1000 FROM user_account WHERE id IN
 ('50000000-0000-4000-8000-000000000001','50000000-0000-4000-8000-000000000002') ON CONFLICT DO NOTHING;
INSERT INTO hero(id,code,class_code,name,setup_points,is_test_fixture,asset_key)
 SELECT ('60000000-0000-4000-8000-' || lpad(s.slot_no::text,12,'0'))::uuid,
 'dev-slot-' || s.slot_no,s.class_code,'DEV ' || c.name_vi || ' ' || s.slot_no,c.base_sp,true,'dev/' || lower(s.class_code)
 FROM lineup_slot s JOIN chess_class c ON c.code=s.class_code ON CONFLICT DO NOTHING;
INSERT INTO team_skill(id,code,name,implementation_key,is_enabled,is_test_fixture,description)
 SELECT ('70000000-0000-4000-8000-' || lpad(n::text,12,'0'))::uuid,
 'dev-skill-'||n,'DEV skill slot '||n,'dev.not_implemented',true,true,
 'Chỉ dùng kiểm tra builder/DTO; server phải trả SKILL_NOT_IMPLEMENTED khi dùng trong trận.'
 FROM generate_series(1,3) n ON CONFLICT DO NOTHING;
-- Development-only overlay for the three documented special-move heroes.
UPDATE hero SET is_enabled=true,is_test_fixture=true,coin_price=0,
 asset_key=COALESCE(asset_key,'dev/special-elephant')
 WHERE id IN ('40000000-0000-4000-8000-000000000001','40000000-0000-4000-8000-000000000002','40000000-0000-4000-8000-000000000003');
-- Separate shop fixtures: one purchasable and one disabled for negative tests.
INSERT INTO hero(id,code,class_code,name,setup_points,coin_price,is_enabled,is_test_fixture,asset_key,description) VALUES
 ('60000000-0000-4000-8000-000000000017','dev-shop-soldier','SOLDIER','DEV Shop Soldier',1,100,true,false,'dev/shop-soldier','Development purchase fixture.'),
 ('60000000-0000-4000-8000-000000000018','dev-disabled-soldier','SOLDIER','DEV Disabled Soldier',1,50,false,false,'dev/disabled-soldier','Development disabled purchase fixture.'),
 ('60000000-0000-4000-8000-000000000019','dev-free-soldier','SOLDIER','DEV Free Soldier',1,0,true,false,'dev/free-soldier','Development free purchase fixture.'),
 ('60000000-0000-4000-8000-000000000020','dev-expensive-soldier','SOLDIER','DEV Expensive Soldier',1,2000,true,false,'dev/expensive-soldier','Development insufficient-balance fixture.')
 ON CONFLICT (id) DO UPDATE SET coin_price=EXCLUDED.coin_price,is_enabled=EXCLUDED.is_enabled,asset_key=EXCLUDED.asset_key;
INSERT INTO player_hero(player_id,hero_id,acquired_via)
 SELECT p.id,h.id,'test' FROM user_account p CROSS JOIN hero h
 WHERE p.id IN ('50000000-0000-4000-8000-000000000001','50000000-0000-4000-8000-000000000002')
 AND h.code LIKE 'dev-slot-%' ON CONFLICT DO NOTHING;
INSERT INTO player_hero(player_id,hero_id,acquired_via)
 SELECT p.id,h.id,'test' FROM user_account p CROSS JOIN hero h
 WHERE p.id IN ('50000000-0000-4000-8000-000000000001','50000000-0000-4000-8000-000000000002')
 AND h.id IN ('40000000-0000-4000-8000-000000000001','40000000-0000-4000-8000-000000000002','40000000-0000-4000-8000-000000000003')
 ON CONFLICT DO NOTHING;
INSERT INTO lineup(id,player_id,ruleset_id,name) VALUES
 ('80000000-0000-4000-8000-000000000001','50000000-0000-4000-8000-000000000001','10000000-0000-4000-8000-000000000001','DEV 43 SP A'),
 ('80000000-0000-4000-8000-000000000002','50000000-0000-4000-8000-000000000002','10000000-0000-4000-8000-000000000001','DEV 43 SP B') ON CONFLICT DO NOTHING;
INSERT INTO lineup_entry(lineup_id,slot_no,class_code,hero_id)
 SELECT l.id,s.slot_no,s.class_code,h.id FROM lineup l CROSS JOIN lineup_slot s
 JOIN hero h ON h.code='dev-slot-'||s.slot_no
 WHERE l.id IN ('80000000-0000-4000-8000-000000000001','80000000-0000-4000-8000-000000000002') ON CONFLICT DO NOTHING;
INSERT INTO lineup_skill(lineup_id,slot_no,team_skill_id)
 SELECT l.id,n,('70000000-0000-4000-8000-'||lpad(n::text,12,'0'))::uuid FROM lineup l CROSS JOIN generate_series(1,3) n
 WHERE l.id IN ('80000000-0000-4000-8000-000000000001','80000000-0000-4000-8000-000000000002') ON CONFLICT DO NOTHING;
COMMIT;
