-- Re-runnable catalog patch. Apply after 02_seed_catalog and 08_step6_skills.
-- Intentionally leaves setup_points and existing hero IDs/ownership untouched.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000005','quang-trung-orthogonal-3','Quang Trung — hành quân thần tốc','active','quang_trung.special_move',
  '{"minSteps":1,"maxSteps":3,"orthogonal":true,"palaceOnly":false,"riverCrossing":false,"cooldownTurns":3}',
  'Đi thường 1 ô thẳng, được ra khỏi cung nhưng không qua sông. Bấm skill để đi thẳng 1–3 ô; hồi chiêu 3 lượt chung.')
ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,
 implementation_key=EXCLUDED.implementation_key,parameters=EXCLUDED.parameters,description=EXCLUDED.description;

INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000011','pham-ngu-lao-hoanh-soc','Hoành Sóc Giang Sơn','special_move','rook.hoanh_soc',
  '{}','Sau khi Phạm Ngũ Lão ăn một quân, nước đi tiếp theo của chính quân này có thể đi xuyên tối đa một quân đồng minh.')
ON CONFLICT (id) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,
 implementation_key=EXCLUDED.implementation_key,parameters=EXCLUDED.parameters,description=EXCLUDED.description;

UPDATE hero SET trait_id='30000000-0000-4000-8000-000000000011',
 description='Xe 8 SP. Hoành Sóc Giang Sơn: sau khi ăn một quân, nước đi tiếp theo có thể xuyên một quân đồng minh.'
 WHERE code='pham-ngu-lao-rook';

UPDATE hero_trait SET description='Xe có thể đi qua Thành/Rào hoặc vào ô đó để phá; quân trên đường vẫn chặn như Xe thường.'
 WHERE id='30000000-0000-4000-8000-000000000007';
UPDATE hero SET description='Xe 8 SP; đi qua Thành/Rào hoặc vào ô đó để phá.'
 WHERE code='ly-thuong-kiet-rook';

UPDATE hero_trait SET description='Pháo có thể dùng cả Thành và Rào làm ngòi; cũng có thể vào ô Thành để phá.'
 WHERE id='30000000-0000-4000-8000-000000000008';
UPDATE hero SET description='Pháo 6 SP; dùng Thành hoặc Rào làm ngòi, có thể vào ô Thành để phá.'
 WHERE code='ly-thuong-kiet-cannon';
UPDATE hero SET description='Tướng đi thường 1 ô thẳng, được ra khỏi cung, không qua sông; skill đi thẳng 1–3 ô với CD 3.'
 WHERE code='quang-trung-general';
COMMIT;
