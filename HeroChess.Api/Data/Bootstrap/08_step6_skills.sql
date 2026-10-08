-- Step 6: Thành and Rào Command Skills + Trần Hưng Đạo — Tượng Hero Skill.
-- These skills are wired to handler implementations in HeroChess.Rules.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

-- Step 6: Thành Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000010','thanh','Thành','thanh',2,true,false,
  'Thành: tạo một chướng ngại tạm thời trên lãnh thổ. Tồn tại 1 lượt. Chỉ Pháo mới phá được. Không dùng làm ngòi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description;

-- Step 6: Rào Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000011','rao','Rào','rao',1,true,false,
  'Rào: tạo một rào cản tạm thời trên lãnh thổ. Tồn tại 1 lượt. Hành xử như Tốt khi bị phá.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description;

-- Step 6: Trần Hưng Đạo — Tượng Hero Skill (team_skill slot for the hero's own skill)
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000012','tran-hung-dao-tuong-coc','Trần Hưng Đạo — Tượng: Tạo Cọc','tran_hung_dao_tuong.coc',3,true,false,
  'Trần Hưng Đạo — Tượng: Tạo một Cọc trên sông phía trước. Cọc tồn tại 3 lượt. Có thể thu hồi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description;

-- Step 6: Lý Thường Kiệt — Xe: movement trait already has no override (trait_id = NULL).
-- The movement bypass is implemented purely via the MovementImplementationKey being set
-- on the piece at lineup time (e.g., via a trait or directly).
-- We mark the existing ly-thuong-kiet-rook hero with the trait so the engine can route it.
-- The trait is stored in hero_trait and linked via hero.trait_id.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000007','ly-thuong-kiet-xe-trait','Lý Thường Kiệt — Xe','special_move','rook.ly_thuong_kiet',
  '{}',
  'Xe 8d; passive: không bị cản bởi Thành / Rào / Cọc. Có thể tấn công quân địch đằng sau chướng ngại.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,description=EXCLUDED.description;

-- Link Lý Thường Kiệt Xe hero to the trait
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000007'
 WHERE code='ly-thuong-kiet-rook';

-- Step 6: Lý Thường Kiệt — Pháo: similar trait for cannon bypass
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000008','ly-thuong-kiet-phao-trait','Lý Thường Kiệt — Pháo','special_move','cannon.ly_thuong_kiet',
  '{}',
  'Pháo 6d; passive: Thành không dùng làm ngòi. Có thể phá Thành.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,description=EXCLUDED.description;

-- Link Lý Thường Kiệt Pháo hero to the trait
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000008'
 WHERE code='ly-thuong-kiet-cannon';

COMMIT;
