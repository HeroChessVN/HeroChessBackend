-- NOTE ve DB conceptual ERD -- Hero Chess roster seed.
-- STEP 2: Custom movement handlers wired (Le Loi, Trung Trac, Trung Nhi).
-- Run after 01_schema.sql.
-- May be re-run: existing rows use ON CONFLICT DO NOTHING / DO UPDATE.
-- Hero behaviour is implemented in Rules; this file supplies catalogue metadata.
-- CHECK constraint on hero(setup_points): non-GENERAL heroes require SP >= 1.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

INSERT INTO ruleset(id,code,name,setup_budget,turn_seconds,action_limit,is_active)
VALUES ('10000000-0000-4000-8000-000000000001','prototype-v0.1','Hero Chess prototype',50,90,150,true)
ON CONFLICT DO NOTHING;

-- A. chess_class: Update ELEPHANT base from 1 to 2 per design note.
UPDATE chess_class SET base_sp = 2 WHERE code = 'ELEPHANT';
INSERT INTO chess_class(code,name_vi,base_sp,required_count,base_movement_code) VALUES
 ('GENERAL','Tuong',0,1,'xiangqi.general'),('ADVISOR','Si',1,2,'xiangqi.advisor'),
 ('ELEPHANT','Tuong',2,2,'xiangqi.elephant'),('ROOK','Xe',7,2,'xiangqi.rook'),
 ('CANNON','Phao',6,2,'xiangqi.cannon'),('HORSE','Ma',4,2,'xiangqi.horse'),
 ('SOLDIER','Tot',1,5,'xiangqi.soldier') ON CONFLICT (code) DO UPDATE SET base_sp = EXCLUDED.base_sp;

INSERT INTO lineup_slot(slot_no,class_code,start_x,start_y) VALUES
 (1,'ROOK',0,0),(2,'HORSE',1,0),(3,'ELEPHANT',2,0),(4,'ADVISOR',3,0),
 (5,'GENERAL',4,0),(6,'ADVISOR',5,0),(7,'ELEPHANT',6,0),(8,'HORSE',7,0),
 (9,'ROOK',8,0),(10,'CANNON',1,2),(11,'CANNON',7,2),
 (12,'SOLDIER',0,3),(13,'SOLDIER',2,3),(14,'SOLDIER',4,3),
 (15,'SOLDIER',6,3),(16,'SOLDIER',8,3)
ON CONFLICT (slot_no) DO NOTHING;

-- B. historical_character: 44 roster heroes (45 minus duplicate Nguyen Trai).
--    Nguyen Nhac / Nguyen Lu appear in both Si and Tuong sections; distinct chars per variant.
CREATE TEMP TABLE seed_characters ON COMMIT DROP AS SELECT * FROM (VALUES
 -- Si variants
 ('50000000-0000-4000-8000-000000000001','nguyen-trai-1','Nguyen Trai'),
 ('50000000-0000-4000-8000-000000000002','nguyen-trung-ngan','Nguyen Trung Ngan'),
 ('50000000-0000-4000-8000-000000000003','nguyen-binh-khiem','Nguyen Binh Khiem'),
 ('50000000-0000-4000-8000-000000000004','mac-dinh-chi','Mac Dinh Chi'),
 ('50000000-0000-4000-8000-000000000005','tran-thu-do','Tran Thu Do'),
 ('50000000-0000-4000-8000-000000000006','tran-nhat-duat','Tran Nhat Duat'),
 ('50000000-0000-4000-8000-000000000007','le-lai','Le Lai'),
 ('50000000-0000-4000-8000-000000000009','nguyen-nhac-si','Nguyen Nhac'),
 ('50000000-0000-4000-8000-000000000010','nguyen-lu-si','Nguyen Lu'),
 -- Tuong/Vua variants
 ('50000000-0000-4000-8000-000000000011','tran-hung-dao-tuong','Tran Hung Dao'),
 ('50000000-0000-4000-8000-000000000012','le-loi','Le Loi'),
 ('50000000-0000-4000-8000-000000000013','quang-trung','Quang Trung'),
 ('50000000-0000-4000-8000-000000000014','nguyen-nhac-tuong','Nguyen Nhac'),
 ('50000000-0000-4000-8000-000000000015','nguyen-lu-tuong','Nguyen Lu'),
 ('50000000-0000-4000-8000-000000000016','tran-thanh-tong','Tran Thanh Tong'),
 -- Tuong variants
 ('50000000-0000-4000-8000-000000000017','tran-hung-dao-tuong2','Tran Hung Dao'),
 ('50000000-0000-4000-8000-000000000018','da-tuong','Da Tuong'),
 ('50000000-0000-4000-8000-000000000019','tran-binh-trong','Tran Binh Trong'),
 ('50000000-0000-4000-8000-000000000020','le-khoi','Le Khoi'),
 ('50000000-0000-4000-8000-000000000021','pham-van-xao','Pham Van Xao'),
 ('50000000-0000-4000-8000-000000000022','bui-thi-xuan','Bui Thi Xuan'),
 ('50000000-0000-4000-8000-000000000023','le-van-hung','Le Van Hung'),
 ('50000000-0000-4000-8000-000000000024','trung-trac','Trung Trac'),
 ('50000000-0000-4000-8000-000000000025','trung-nhi','Trung Nhi'),
 -- Xe variants
 ('50000000-0000-4000-8000-000000000026','pham-ngu-lao','Pham Ngu Lao'),
 ('50000000-0000-4000-8000-000000000027','tran-khanh-du','Tran Khanh Du'),
 ('50000000-0000-4000-8000-000000000028','ly-thuong-kiet-xe','Ly Thuong Kiet'),
 ('50000000-0000-4000-8000-000000000029','dinh-liet','Dinh Liet'),
 ('50000000-0000-4000-8000-000000000030','tran-nguyen-han','Tran Nguyen Han'),
 ('50000000-0000-4000-8000-000000000031','vo-van-dung','Vo Van Dung'),
 ('50000000-0000-4000-8000-000000000032','tran-quang-dieu','Tran Quang Dieu'),
 -- Phao variants
 ('50000000-0000-4000-8000-000000000033','tran-khat-chan','Tran Khat Chan'),
 ('50000000-0000-4000-8000-000000000034','yet-kieu','Yet Kieu'),
 ('50000000-0000-4000-8000-000000000035','ly-thuong-kiet-phao','Ly Thuong Kiet'),
 ('50000000-0000-4000-8000-000000000036','dinh-le','Dinh Le'),
 ('50000000-0000-4000-8000-000000000037','nguyen-xi','Nguyen Xi'),
 ('50000000-0000-4000-8000-000000000038','nguyen-van-tuyet','Nguyen Van Tuyet'),
 ('50000000-0000-4000-8000-000000000039','nguyen-van-loc','Nguyen Van Loc'),
 -- Ma variants
 ('50000000-0000-4000-8000-000000000040','tran-quoc-toan','Tran Quoc Toan'),
 ('50000000-0000-4000-8000-000000000041','tran-quang-khai','Tran Quang Khai'),
 ('50000000-0000-4000-8000-000000000042','luu-nhan-chu','Luu Nhan Chu'),
 ('50000000-0000-4000-8000-000000000043','le-sat','Le Sat'),
 ('50000000-0000-4000-8000-000000000044','ly-van-bu','Ly Van Bu'),
 ('50000000-0000-4000-8000-000000000045','vo-dinh-tu','Vo Dinh Tu')) AS chars(seed_id,code,name);
INSERT INTO historical_character(id,code,name)
SELECT CASE WHEN EXISTS (SELECT 1 FROM historical_character existing WHERE existing.id = chars.seed_id::uuid AND existing.code <> chars.code)
            THEN gen_random_uuid() ELSE chars.seed_id::uuid END, chars.code, chars.name
FROM seed_characters chars WHERE true ON CONFLICT (code) DO NOTHING;

-- C. hero_trait: Heroes with confirmed custom movement.
--    Existing traits (from prior seed):
--      elephant-diagonal-1-2              -> 30000000-0000-4000-8000-000000000001 (Tran Binh Trong)
--      elephant-diagonal-1-2 (BXU variant) -> 327e712d-26d2-5da4-5c9a-87a1ff74c1e1 (Bui Thi Xuan)
--      da-tuong-diagonal                  -> 30000000-0000-4000-8000-000000000002 (Da Tuong)
--    NEW traits added in STEP 2:
--      elephant.river_crossing (Trung Trac, Trung Nhi): diagonal 1-2, no home-half restriction
--      general.king_move       (Le Loi): 1-step, 8 directions, palace-confined
--    NEW traits added in STEP 3:
--      general.orthogonal_range_3     (Quang Trung): orthogonal 1-3 cells, blocked like Rook
--      general.orthogonal_range_1_no_palace (Tran Hung Dao Tướng): orthogonal 1 cell, no palace, no river
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000001','elephant-diagonal-1-2','Trần Bình Trọng đi chéo 1–2 ô','special_move','elephant.diagonal_range',
  '{"minSteps":1,"maxSteps":2,"ownHalfOnly":true,"canJump":false}', 'Đi chéo 1–2 ô trên phần sân nhà.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000002','da-tuong-diagonal','Dã Tượng luân phiên tầm đi','special_move','elephant.alternating_distance',
  '{}', 'Tầm đi chéo thay đổi sau mỗi nước.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('327e712d-26d2-5da4-5c9a-87a1ff74c1e1','bui-thi-xuan-diagonal-1-2','Bùi Thị Xuân đi chéo 1–2 ô','special_move','elephant.diagonal_range',
  '{"minSteps":1,"maxSteps":2,"ownHalfOnly":true,"canJump":false}', 'Đi chéo 1–2 ô trên phần sân nhà.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000003','elephant-river-crossing','Trung Trac / Trung Nhi di chéo qua sông','special_move','elephant.river_crossing',
 '{"minSteps":1,"maxSteps":2,"ownHalfOnly":false,"canJump":false}',
 'Đi chéo 1–2 ô; không nhảy mắt; có thể qua sông.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000010','trung-nhi-river-crossing','Trưng Nhị đi chéo qua sông','special_move','elephant.river_crossing',
 '{"minSteps":1,"maxSteps":2,"ownHalfOnly":false,"canJump":false}',
 'Đi chéo 1–2 ô; không nhảy mắt; có thể qua sông.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000004','le-loi-king-move','Le Loi di 1 ô 8 hướng','special_move','general.king_move',
 '{"maxSteps":1,"orthogonal":true,"diagonal":true,"palaceOnly":true}',
 'Đi tối đa 1 ô theo 8 hướng; giới hạn trong cung của mình.') ON CONFLICT DO NOTHING;
-- Quang Trung: one-square normal movement; active skill travels up to three squares.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000005','quang-trung-orthogonal-3','Quang Trung — hành quân thần tốc','active','quang_trung.special_move',
 '{"minSteps":1,"maxSteps":3,"orthogonal":true,"palaceOnly":false,"riverCrossing":false,"cooldownTurns":3}',
 'Đi thường 1 ô thẳng, được ra khỏi cung nhưng không qua sông. Bấm skill để đi thẳng 1–3 ô; hồi chiêu 3 lượt chung.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000011','pham-ngu-lao-hoanh-soc','Hoành Sóc Giang Sơn','special_move','rook.hoanh_soc',
 '{}','Sau khi Phạm Ngũ Lão ăn một quân, nước đi tiếp theo của chính quân này có thể đi xuyên tối đa một quân đồng minh.') ON CONFLICT DO NOTHING;
-- STEP 3: Tran Hung Dao (GENERAL) — orthogonal 1 cell, can leave palace, cannot cross river.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000006','tran-hung-dao-tuong-no-palace','Tran Hung Dao Tướng đi 1 ô thẳng','special_move','general.orthogonal_range_1_no_palace',
 '{"maxSteps":1,"orthogonal":true,"diagonal":false,"palaceOnly":false,"riverCrossing":false}',
 'Đi 1 ô thẳng; được ra khỏi cung; không qua sông.') ON CONFLICT DO NOTHING;

-- Independent trait definitions; existing catalogue configuration is preserved.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description)
SELECT md5('hero_chess.hero_trait:'||code)::uuid,code,name,kind,implementation_key,parameters::jsonb,description
FROM (VALUES
 ('le-lai-cuu-chua','Lê Lai cứu chúa','active','le_lai.swap_general','{"cooldownTurns":8}',
  'Lê Lai hoán đổi vị trí với Tướng đồng minh còn trong cung để che chở chủ tướng. Không cần đường đi trống giữa hai quân.'),
 ('tran-thu-do-tran-quoc-ho-quan','Trấn Quốc Hộ Quân','active','tran_thu_do.general_shield','{"durationTurns":2,"cooldownTurns":6}',
  'Giữ vững quốc gia, Trần Thủ Độ dựng Khiên che chở chủ tướng trong thời gian ngắn. Chỉ có thể bảo vệ Tướng đồng minh.'),
 ('nguyen-nhac-thai-duc-linh-bo','Thái Đức Linh Bộ','special_move','advisor.king_step','{"maxSteps":1,"palaceOnly":true,"orthogonal":true,"diagonal":true}',
  'Nguyễn Nhạc linh hoạt xoay chuyển thế phòng ngự, có thể tiến thoái một ô theo tám hướng trong cung nhà.'),
 ('nguyen-lu-dong-dinh-linh-bo','Đông Định Linh Bộ','special_move','advisor.king_step','{"maxSteps":1,"palaceOnly":true,"orthogonal":true,"diagonal":true}',
  'Nguyễn Lữ uyển chuyển giữ thế trận, có thể di chuyển một ô theo tám hướng trong cung nhà.')
) AS traits(code,name,kind,implementation_key,parameters,description)
ON CONFLICT(code) DO NOTHING;

-- D. hero: Full 44-entry roster (45 minus duplicate Nguyen Trai).
--    trait_id:
--      Tran Binh Trong  -> 30000000-0000-4000-8000-000000000001
--      Bui Thi Xuan    -> 327e712d-26d2-5da4-5c9a-87a1ff74c1e1
--      Da Tuong        -> 30000000-0000-4000-8000-000000000002
--      all others       -> NULL
--    is_enabled = false: not released. is_test_fixture = false. coin_price = 0.
--    CLASS CODES: Tuong/Vua entries stored as GENERAL (no VUA class code).
--    SP: General baseline 0, special Generals 1–2; Elephants 2–4; Advisors 1–2.
--    Nguyen Trai: two character rows, two hero rows.
--    Nguyen Nhac / Nguyen Lu: Si variant + Tuong variant preserved separately.
--    Tran Hung Dao: Tuong variant + Tuong2 (ELEPHANT) variant preserved separately.
--    Ly Thuong Kiet: Xe variant + Phao variant preserved separately.
--    Red-marked (Nguyen Trung Ngan, Dinh Liet, Tran Khat Chan, Luu Nhan Chu):
--      no gameplay meaning assigned in this step.
--    Pham Ngu Lao: note "8d( co the 9d)" stored as 8d; 9d ambiguity noted.
WITH release_prices(code,model_key,price,starter) AS (VALUES
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
 ('nguyen-van-loc-cannon',NULL,100,false),('bui-thi-xuan-elephant','buithixuan',200,false),
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
)
INSERT INTO hero(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,is_starter,asset_key,description)
SELECT CASE WHEN EXISTS (SELECT 1 FROM hero existing WHERE existing.id = seeded.id::uuid AND existing.code <> seeded.code)
            THEN gen_random_uuid() ELSE seeded.id::uuid END,
       seeded.code, actual_character.id, seeded.class_code, seeded.trait_id::uuid, seeded.name,
       seeded.setup_points, true, seeded.is_test_fixture, release.price, release.starter, release.model_key, seeded.description
FROM (VALUES
 -- === SI (ADVISOR) ===
 ('60000000-0000-4000-8000-000000000001','nguyen-trai-1-advisor','50000000-0000-4000-8000-000000000001','ADVISOR',NULL,'Nguyen Trai',1,false,false,0,'Si 1d; base ADVISOR movement. First Nguyen Trai entry from note.'),
 ('60000000-0000-4000-8000-000000000003','nguyen-binh-khiem-advisor','50000000-0000-4000-8000-000000000003','ADVISOR',NULL,'Nguyen Binh Khiem',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000004','mac-dinh-chi-advisor','50000000-0000-4000-8000-000000000004','ADVISOR',NULL,'Mac Dinh Chi',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000005','tran-thu-do-advisor','50000000-0000-4000-8000-000000000005','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='tran-thu-do-tran-quoc-ho-quan'),'Tran Thu Do',2,false,false,0,'Sĩ có skill Trấn Quốc Hộ Quân.'),
 ('60000000-0000-4000-8000-000000000006','tran-nhat-duat-advisor','50000000-0000-4000-8000-000000000006','ADVISOR',NULL,'Tran Nhat Duat',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000007','le-lai-advisor','50000000-0000-4000-8000-000000000007','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='le-lai-cuu-chua'),'Le Lai',2,false,false,0,'Sĩ có skill Lê Lai cứu chúa.'),
 ('60000000-0000-4000-8000-000000000008','nguyen-nhac-advisor','50000000-0000-4000-8000-000000000009','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='nguyen-nhac-thai-duc-linh-bo'),'Nguyen Nhac',2,false,false,0,'Sĩ có trait Thái Đức Linh Bộ; bản Tướng giữ di chuyển cơ bản.'),
 ('60000000-0000-4000-8000-000000000010','nguyen-lu-advisor','50000000-0000-4000-8000-000000000010','ADVISOR',(SELECT id::text FROM hero_trait WHERE code='nguyen-lu-dong-dinh-linh-bo'),'Nguyen Lu',2,false,false,0,'Sĩ có trait Đông Định Linh Bộ; bản Tướng giữ di chuyển cơ bản.'),
 -- === TUONG / VUA (GENERAL) ===
 -- Note: Tuong/Vua stored as GENERAL (no VUA class code exists).
 -- Special General costs include their movement/skill premium.
 ('60000000-0000-4000-8000-000000000011','tran-hung-dao-general','50000000-0000-4000-8000-000000000011','GENERAL','30000000-0000-4000-8000-000000000006','Tran Hung Dao',1,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.orthogonal_range_1_no_palace IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000012','le-loi-general','50000000-0000-4000-8000-000000000012','GENERAL','30000000-0000-4000-8000-000000000004','Le Loi',1,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.king_move IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000013','quang-trung-general','50000000-0000-4000-8000-000000000013','GENERAL','30000000-0000-4000-8000-000000000005','Quang Trung',2,false,false,0,'Tướng đi thường 1 ô thẳng, được ra khỏi cung, không qua sông; skill đi thẳng 1–3 ô với CD 3.'),
 ('60000000-0000-4000-8000-000000000014','nguyen-nhac-general','50000000-0000-4000-8000-000000000009','GENERAL',NULL,'Nguyen Nhac',0,false,false,0,'Tuong 0d; Nguyen Nhac also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000015','nguyen-lu-general','50000000-0000-4000-8000-000000000010','GENERAL',NULL,'Nguyen Lu',0,false,false,0,'Tuong 0d; Nguyen Lu also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000016','tran-thanh-tong-general','50000000-0000-4000-8000-000000000016','GENERAL',NULL,'Tran Thanh Tong',0,false,false,0,'Tuong 0d; no custom rule noted.'),
 -- === TUONG (ELEPHANT) ===
 -- Da Tuong: trait elephant.alternating_distance (existing ID).
 -- Tran Binh Trong: trait elephant.diagonal_range (existing ID).
 -- Bui Thi Xuan: trait elephant.diagonal_range (existing BXU variant ID).
 -- Trần Hưng Đạo (ELEPHANT): 4 SP includes the Bạch Đằng Giang premium.
 ('60000000-0000-4000-8000-000000000017','tran-hung-dao-elephant','50000000-0000-4000-8000-000000000011','ELEPHANT',NULL,'Tran Hung Dao',4,false,false,0,'Tượng 4 SP; Bạch Đằng Giang active skill.'),
 ('60000000-0000-4000-8000-000000000018','da-tuong-elephant','50000000-0000-4000-8000-000000000018','ELEPHANT','30000000-0000-4000-8000-000000000002','Da Tuong',4,false,false,0,'Tuong 4d; movement: elephant.alternating_distance. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000019','tran-binh-trong-elephant','50000000-0000-4000-8000-000000000019','ELEPHANT','30000000-0000-4000-8000-000000000001','Tran Binh Trong',3,false,false,0,'Tuong 3d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000020','le-khoi-elephant','50000000-0000-4000-8000-000000000020','ELEPHANT',NULL,'Le Khoi',2,false,false,0,'Tuong 2d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000021','pham-van-xao-elephant','50000000-0000-4000-8000-000000000021','ELEPHANT',NULL,'Pham Van Xao',2,false,false,0,'Tuong 2d; no custom movement noted; base ELEPHANT movement.'),
 ('60000000-0000-4000-8000-000000000022','bui-thi-xuan-elephant','50000000-0000-4000-8000-000000000022','ELEPHANT','327e712d-26d2-5da4-5c9a-87a1ff74c1e1','Bui Thi Xuan',3,false,false,0,'Tuong 3d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000023','le-van-hung-elephant','50000000-0000-4000-8000-000000000023','ELEPHANT',NULL,'Le Van Hung',2,false,false,0,'Tuong 2d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000024','trung-trac-elephant','50000000-0000-4000-8000-000000000024','ELEPHANT','30000000-0000-4000-8000-000000000003','Trung Trac',4,false,false,0,'Tuong 4d; movement: elephant.river_crossing IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000025','trung-nhi-elephant','50000000-0000-4000-8000-000000000025','ELEPHANT','30000000-0000-4000-8000-000000000010','Trung Nhi',4,false,false,0,'Tuong 4d; movement: elephant.river_crossing IMPLEMENTED.'),
 -- === XE (ROOK) ===
 ('60000000-0000-4000-8000-000000000026','pham-ngu-lao-rook','50000000-0000-4000-8000-000000000026','ROOK','30000000-0000-4000-8000-000000000011','Pham Ngu Lao',8,false,false,0,'Xe 8 SP. Hoành Sóc Giang Sơn: sau khi ăn một quân, nước đi tiếp theo có thể xuyên một quân đồng minh.'),
 ('60000000-0000-4000-8000-000000000027','tran-khanh-du-rook','50000000-0000-4000-8000-000000000027','ROOK',NULL,'Tran Khanh Du',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000028','ly-thuong-kiet-rook','50000000-0000-4000-8000-000000000028','ROOK',NULL,'Ly Thuong Kiet',8,false,false,0,'Xe 8 SP; đi qua Thành/Rào hoặc vào ô đó để phá.'),
 ('60000000-0000-4000-8000-000000000030','tran-nguyen-han-rook','50000000-0000-4000-8000-000000000030','ROOK',NULL,'Tran Nguyen Han',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000031','vo-van-dung-rook','50000000-0000-4000-8000-000000000031','ROOK',NULL,'Vo Van Dung',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000032','tran-quang-dieu-rook','50000000-0000-4000-8000-000000000032','ROOK',NULL,'Tran Quang Dieu',7,false,false,0,'Xe 7d; design note xe* (star meaning unclear). Assumed base ROOK movement.'),
 -- === PHAO (CANNON) ===
 ('60000000-0000-4000-8000-000000000034','yet-kieu-cannon','50000000-0000-4000-8000-000000000034','CANNON',NULL,'Yet Kieu',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000035','ly-thuong-kiet-cannon','50000000-0000-4000-8000-000000000028','CANNON',NULL,'Ly Thuong Kiet',7,false,false,0,'Pháo 7 SP; dùng Thành hoặc Rào làm ngòi, có thể vào ô Thành để phá.'),
 ('60000000-0000-4000-8000-000000000036','dinh-le-cannon','50000000-0000-4000-8000-000000000036','CANNON',NULL,'Dinh Le',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000037','nguyen-xi-cannon','50000000-0000-4000-8000-000000000037','CANNON',NULL,'Nguyen Xi',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000038','nguyen-van-tuyet-cannon','50000000-0000-4000-8000-000000000038','CANNON',NULL,'Nguyen Van Tuyet',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000039','nguyen-van-loc-cannon','50000000-0000-4000-8000-000000000039','CANNON',NULL,'Nguyen Van Loc',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 -- === MA (HORSE) ===
 ('60000000-0000-4000-8000-000000000040','tran-quoc-toan-horse','50000000-0000-4000-8000-000000000040','HORSE',NULL,'Tran Quoc Toan',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000041','tran-quang-khai-horse','50000000-0000-4000-8000-000000000041','HORSE',NULL,'Tran Quang Khai',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000043','le-sat-horse','50000000-0000-4000-8000-000000000043','HORSE',NULL,'Le Sat',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000044','ly-van-buu-horse','50000000-0000-4000-8000-000000000044','HORSE',NULL,'Ly Van Bu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000045','vo-dinh-tu-horse','50000000-0000-4000-8000-000000000045','HORSE',NULL,'Vo Dinh Tu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'))
 AS seeded(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,description)
 JOIN seed_characters expected_character ON expected_character.seed_id::uuid = seeded.character_id::uuid
 JOIN historical_character actual_character ON actual_character.code = expected_character.code
 JOIN release_prices release ON release.code=seeded.code
 ON CONFLICT (code) DO NOTHING;
INSERT INTO hero(code,class_code,name,setup_points,coin_price,is_starter,is_enabled,asset_key)
VALUES ('linh-nha-tran-soldier','SOLDIER','Lính nhà Trần',1,0,true,true,'linhnhatran'),
 ('linh-nha-le-soldier','SOLDIER','Lính nhà Lê',1,100,false,true,'linhnhale'),
 ('linh-nha-tay-son-soldier','SOLDIER','Lính nhà Tây Sơn',1,100,false,true,'linhnhatayson')
ON CONFLICT(code) DO NOTHING;
COMMIT;
