-- Step 6: Thành and Rào Command Skills + Trần Hưng Đạo — Tượng Hero Skill.
-- These skills are wired to handler implementations in HeroChess.Rules.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

-- Step 6: Thành Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000010','thanh','Thành','thanh',8,true,false,
  'Tạo 1 Thành ở vị trí bất kỳ bên sông mình; tồn tại 6 lượt chung, hồi chiêu 8. Pháo phá Thành và tiến vào ô đó; Thành không làm ngòi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

-- Step 6: Rào Command Skill
INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000011','rao','Rào','rao',6,true,false,
  'Tạo 1 Rào ở vị trí bất kỳ bên sông mình; tồn tại 4 lượt chung, hồi chiêu 6. Pháo có thể dùng Rào làm ngòi.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

-- Step 6: Trần Hưng Đạo — Tượng Hero Skill (team_skill slot for the hero's own skill)
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000009','bach-dang-giang','Bạch Đằng Giang','active','tran_hung_dao_tuong.coc',
  '{"cooldownTurns":5,"durationTurns":3}',
  'Tạo 1 Cọc ẩn trên hai hàng sông; quân địch dừng ở ô Cọc bị ăn. Tồn tại 3 lượt chung, hồi chiêu 5; không thể thu hồi.')
ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,implementation_key=EXCLUDED.implementation_key,
  parameters=EXCLUDED.parameters,description=EXCLUDED.description;
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000009' WHERE code='tran-hung-dao-elephant';

INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000013','khien','Khiên','khien',8,true,false,
  'Tạo 1 Khiên cho quân đồng minh bất kỳ trên bàn cờ; tồn tại 4 lượt chung, hồi chiêu 8. Quân có Khiên không thể bị ăn.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

INSERT INTO team_skill(id,code,name,implementation_key,cooldown_turns,is_enabled,is_test_fixture,description) VALUES
 ('70000000-0000-4000-8000-000000000012','tran-hung-dao-tuong-coc','Trần Hưng Đạo — Tượng: Tạo Cọc','tran_hung_dao_tuong.coc',5,false,false,
  'Skill này đã chuyển thành Hero Skill Bạch Đằng Giang; không thể chọn làm Command Skill.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,
  cooldown_turns=EXCLUDED.cooldown_turns,name=EXCLUDED.name,description=EXCLUDED.description,is_enabled=EXCLUDED.is_enabled;

-- Lý Thường Kiệt Xe uses a movement trait to pass or break Thành/Rào.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000007','ly-thuong-kiet-xe-trait','Lý Thường Kiệt — Xe','special_move','rook.ly_thuong_kiet',
  '{}',
  'Xe có thể đi qua Thành/Rào hoặc vào ô đó để phá; quân trên đường vẫn chặn như Xe thường.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,description=EXCLUDED.description;

-- Link Lý Thường Kiệt Xe hero to the trait
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000007'
 WHERE code='ly-thuong-kiet-rook';

-- Lý Thường Kiệt Pháo uses Thành/Rào as a screen and may break Thành.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000008','ly-thuong-kiet-phao-trait','Lý Thường Kiệt — Pháo','special_move','cannon.ly_thuong_kiet',
  '{}',
  'Pháo có thể dùng cả Thành và Rào làm ngòi; cũng có thể vào ô Thành để phá.')
ON CONFLICT (id) DO UPDATE SET implementation_key=EXCLUDED.implementation_key,description=EXCLUDED.description;

-- Link Lý Thường Kiệt Pháo hero to the trait
UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000008'
 WHERE code='ly-thuong-kiet-cannon';

COMMIT;
