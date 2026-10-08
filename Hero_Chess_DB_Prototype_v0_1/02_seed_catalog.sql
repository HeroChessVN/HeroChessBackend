-- NOTE ve DB conceptual ERD -- Hero Chess roster seed.
-- STEP 2: Custom movement handlers wired (Le Loi, Trung Trac, Trung Nhi).
-- Run after 01_schema.sql.
-- May be re-run: existing rows use ON CONFLICT DO NOTHING / DO UPDATE.
-- Hero skill handlers are NOT in scope for this seed; trait_id = NULL for heroes
--   without a confirmed custom movement (Le Loi, Trung Trac, Trung Nhi, etc.).
-- CHECK constraint on hero(setup_points): non-GENERAL heroes require SP >= 1.
--   Tuong SP modifiers (-1d, -2d) deferred pending lineup validation design.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

-- A. chess_class: Update ELEPHANT base from 1 to 2 per design note.
UPDATE chess_class SET base_sp = 2 WHERE code = 'ELEPHANT';
INSERT INTO chess_class(code,name_vi,base_sp,required_count,base_movement_code) VALUES
 ('GENERAL','Tuong',0,1,'xiangqi.general'),('ADVISOR','Si',1,2,'xiangqi.advisor'),
 ('ELEPHANT','Tuong',2,2,'xiangqi.elephant'),('ROOK','Xe',7,2,'xiangqi.rook'),
 ('CANNON','Phao',6,2,'xiangqi.cannon'),('HORSE','Ma',4,2,'xiangqi.horse'),
 ('SOLDIER','Tot',1,5,'xiangqi.soldier') ON CONFLICT (code) DO UPDATE SET base_sp = EXCLUDED.base_sp;

-- B. historical_character: 44 roster heroes (45 minus duplicate Nguyen Trai).
--    Nguyen Nhac / Nguyen Lu appear in both Si and Tuong sections; distinct chars per variant.
INSERT INTO historical_character(id,code,name) VALUES
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
 ('50000000-0000-4000-8000-000000000045','vo-dinh-tu','Vo Dinh Tu')
 ON CONFLICT (code) DO NOTHING;

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
 ('30000000-0000-4000-8000-000000000003','elephant-river-crossing','Trung Trac / Trung Nhi di chéo qua sông','special_move','elephant.river_crossing',
 '{"minSteps":1,"maxSteps":2,"ownHalfOnly":false,"canJump":false}',
 'Đi chéo 1–2 ô; không nhảy mắt; có thể qua sông.') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000004','le-loi-king-move','Le Loi di 1 ô 8 hướng','special_move','general.king_move',
 '{"maxSteps":1,"orthogonal":true,"diagonal":true,"palaceOnly":true}',
 'Đi tối đa 1 ô theo 8 hướng; giới hạn trong cung của mình.') ON CONFLICT DO NOTHING;
-- STEP 3: Quang Trung — orthogonal 1-3, blocked like Rook, no river crossing.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000005','quang-trung-orthogonal-3','Quang Trung đi thẳng 1–3 ô','special_move','general.orthogonal_range_3',
 '{"minSteps":1,"maxSteps":3,"orthogonal":true,"diagonal":false,"riverCrossing":false}',
 'Đi thẳng 1–3 ô; bị chặn như Xe; không qua sông.') ON CONFLICT DO NOTHING;
-- STEP 3: Tran Hung Dao (GENERAL) — orthogonal 1 cell, can leave palace, cannot cross river.
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000006','tran-hung-dao-tuong-no-palace','Tran Hung Dao Tướng đi 1 ô thẳng','special_move','general.orthogonal_range_1_no_palace',
 '{"maxSteps":1,"orthogonal":true,"diagonal":false,"palaceOnly":false,"riverCrossing":false}',
 'Đi 1 ô thẳng; được ra khỏi cung; không qua sông.') ON CONFLICT DO NOTHING;

-- D. hero: Full 44-entry roster (45 minus duplicate Nguyen Trai).
--    trait_id:
--      Tran Binh Trong  -> 30000000-0000-4000-8000-000000000001
--      Bui Thi Xuan    -> 327e712d-26d2-5da4-5c9a-87a1ff74c1e1
--      Da Tuong        -> 30000000-0000-4000-8000-000000000002
--      all others       -> NULL
--    is_enabled = false: not released. is_test_fixture = false. coin_price = 0.
--    CLASS CODES: Tuong/Vua entries stored as GENERAL (no VUA class code).
--    SP: Tướng variants stored as 0 (baseline); note modifiers pending lineup validation.
--    Tran Hung Dao (Tuong variant): stored 0; design note -1d requires SP modifier design.
--    Tran Hung Dao (Tuong2/ELEPHANT): design note 3d stored as 0; CHECK requires SP>=1 for non-GENERAL.
--    Nguyen Trai: two character rows, two hero rows.
--    Nguyen Nhac / Nguyen Lu: Si variant + Tuong variant preserved separately.
--    Tran Hung Dao: Tuong variant + Tuong2 (ELEPHANT) variant preserved separately.
--    Ly Thuong Kiet: Xe variant + Phao variant preserved separately.
--    Red-marked (Nguyen Trung Ngan, Dinh Liet, Tran Khat Chan, Luu Nhan Chu):
--      no gameplay meaning assigned in this step.
--    Pham Ngu Lao: note "8d( co the 9d)" stored as 8d; 9d ambiguity noted.
INSERT INTO hero(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,is_test_fixture,coin_price,description) VALUES
 -- === SI (ADVISOR) ===
 ('60000000-0000-4000-8000-000000000001','nguyen-trai-1-advisor','50000000-0000-4000-8000-000000000001','ADVISOR',NULL,'Nguyen Trai',1,false,false,0,'Si 1d; base ADVISOR movement. First Nguyen Trai entry from note.'),
 ('60000000-0000-4000-8000-000000000002','nguyen-trung-ngan-advisor','50000000-0000-4000-8000-000000000002','ADVISOR',NULL,'Nguyen Trung Ngan',1,false,false,0,'Si 1d; red-marked - no gameplay meaning assigned.'),
 ('60000000-0000-4000-8000-000000000003','nguyen-binh-khiem-advisor','50000000-0000-4000-8000-000000000003','ADVISOR',NULL,'Nguyen Binh Khiem',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000004','mac-dinh-chi-advisor','50000000-0000-4000-8000-000000000004','ADVISOR',NULL,'Mac Dinh Chi',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000005','tran-thu-do-advisor','50000000-0000-4000-8000-000000000005','ADVISOR',NULL,'Tran Thu Do',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000006','tran-nhat-duat-advisor','50000000-0000-4000-8000-000000000006','ADVISOR',NULL,'Tran Nhat Duat',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000007','le-lai-advisor','50000000-0000-4000-8000-000000000007','ADVISOR',NULL,'Le Lai',1,false,false,0,'Si 1d; base ADVISOR movement.'),
 ('60000000-0000-4000-8000-000000000008','nguyen-nhac-advisor','50000000-0000-4000-8000-000000000009','ADVISOR',NULL,'Nguyen Nhac',1,false,false,0,'Si 1d; Nguyen Nhac also has Tuong variant (0d, GENERAL). No custom rule.'),
 ('60000000-0000-4000-8000-000000000010','nguyen-lu-advisor','50000000-0000-4000-8000-000000000010','ADVISOR',NULL,'Nguyen Lu',1,false,false,0,'Si 1d; Nguyen Lu also has Tuong variant (0d, GENERAL). No custom rule.'),
 -- === TUONG / VUA (GENERAL) ===
 -- Note: Tuong/Vua stored as GENERAL (no VUA class code exists).
 -- SP modifiers (-1d, -2d) stored as 0 pending lineup validation design.
 ('60000000-0000-4000-8000-000000000011','tran-hung-dao-general','50000000-0000-4000-8000-000000000011','GENERAL','30000000-0000-4000-8000-000000000006','Tran Hung Dao',0,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.orthogonal_range_1_no_palace IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000012','le-loi-general','50000000-0000-4000-8000-000000000012','GENERAL','30000000-0000-4000-8000-000000000004','Le Loi',0,false,false,0,'Tuong 0d; design note -1d modifier pending design. Movement: general.king_move IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000013','quang-trung-general','50000000-0000-4000-8000-000000000013','GENERAL','30000000-0000-4000-8000-000000000005','Quang Trung',0,false,false,0,'Vua 0d; design note -2d modifier pending design. Movement: general.orthogonal_range_3 IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000014','nguyen-nhac-general','50000000-0000-4000-8000-000000000014','GENERAL',NULL,'Nguyen Nhac',0,false,false,0,'Tuong 0d; Nguyen Nhac also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000015','nguyen-lu-general','50000000-0000-4000-8000-000000000015','GENERAL',NULL,'Nguyen Lu',0,false,false,0,'Tuong 0d; Nguyen Lu also has Si variant (1d). No custom rule.'),
 ('60000000-0000-4000-8000-000000000016','tran-thanh-tong-general','50000000-0000-4000-8000-000000000016','GENERAL',NULL,'Tran Thanh Tong',0,false,false,0,'Tuong 0d; no custom rule noted.'),
 -- === TUONG (ELEPHANT) ===
 -- Da Tuong: trait elephant.alternating_distance (existing ID).
 -- Tran Binh Trong: trait elephant.diagonal_range (existing ID).
 -- Bui Thi Xuan: trait elephant.diagonal_range (existing BXU variant ID).
 -- Tran Hung Dao (ELEPHANT): design note 3d. SP >= 1 satisfied by value 3.
 ('60000000-0000-4000-8000-000000000017','tran-hung-dao-elephant','50000000-0000-4000-8000-000000000017','ELEPHANT',NULL,'Tran Hung Dao',3,false,false,0,'Tuong 3d; design note 3d. Skill tao coc an tren song chua implement.'),
 ('60000000-0000-4000-8000-000000000018','da-tuong-elephant','50000000-0000-4000-8000-000000000018','ELEPHANT','30000000-0000-4000-8000-000000000002','Da Tuong',3,false,false,0,'Tuong 3d; movement: elephant.alternating_distance. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000019','tran-binh-trong-elephant','50000000-0000-4000-8000-000000000019','ELEPHANT','30000000-0000-4000-8000-000000000001','Tran Binh Trong',2,false,false,0,'Tuong 2d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000020','le-khoi-elephant','50000000-0000-4000-8000-000000000020','ELEPHANT',NULL,'Le Khoi',1,false,false,0,'Tuong 1d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000021','pham-van-xao-elephant','50000000-0000-4000-8000-000000000021','ELEPHANT',NULL,'Pham Van Xao',2,false,false,0,'Tuong 2d; no custom movement noted; base ELEPHANT movement.'),
 ('60000000-0000-4000-8000-000000000022','bui-thi-xuan-elephant','50000000-0000-4000-8000-000000000022','ELEPHANT','327e712d-26d2-5da4-5c9a-87a1ff74c1e1','Bui Thi Xuan',2,false,false,0,'Tuong 2d; movement: elephant.diagonal_range. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000023','le-van-hung-elephant','50000000-0000-4000-8000-000000000023','ELEPHANT',NULL,'Le Van Hung',1,false,false,0,'Tuong 1d; standard ELEPHANT movement. COMPLETE (base).'),
 ('60000000-0000-4000-8000-000000000024','trung-trac-elephant','50000000-0000-4000-8000-000000000024','ELEPHANT','30000000-0000-4000-8000-000000000003','Trung Trac',3,false,false,0,'Tuong 3d; movement: elephant.river_crossing IMPLEMENTED.'),
 ('60000000-0000-4000-8000-000000000025','trung-nhi-elephant','50000000-0000-4000-8000-000000000025','ELEPHANT','30000000-0000-4000-8000-000000000003','Trung Nhi',3,false,false,0,'Tuong 3d; movement: elephant.river_crossing IMPLEMENTED.'),
 -- === XE (ROOK) ===
 ('60000000-0000-4000-8000-000000000026','pham-ngu-lao-rook','50000000-0000-4000-8000-000000000026','ROOK',NULL,'Pham Ngu Lao',8,false,false,0,'Xe 8d; design note 8d( co the 9d) stored as 8d; 9d ambiguity noted. Skill hoanh soc giang son chua implement.'),
 ('60000000-0000-4000-8000-000000000027','tran-khanh-du-rook','50000000-0000-4000-8000-000000000027','ROOK',NULL,'Tran Khanh Du',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000028','ly-thuong-kiet-rook','50000000-0000-4000-8000-000000000028','ROOK',NULL,'Ly Thuong Kiet',8,false,false,0,'Xe 8d; passive ko bi can boi thanh rao coc chua implement.'),
 ('60000000-0000-4000-8000-000000000029','dinh-liet-rook','50000000-0000-4000-8000-000000000029','ROOK',NULL,'Dinh Liet',7,false,false,0,'Xe 7d; red-marked - no gameplay meaning. Base ROOK movement.'),
 ('60000000-0000-4000-8000-000000000030','tran-nguyen-han-rook','50000000-0000-4000-8000-000000000030','ROOK',NULL,'Tran Nguyen Han',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000031','vo-van-dung-rook','50000000-0000-4000-8000-000000000031','ROOK',NULL,'Vo Van Dung',7,false,false,0,'Xe 7d; base ROOK movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000032','tran-quang-dieu-rook','50000000-0000-4000-8000-000000000032','ROOK',NULL,'Tran Quang Dieu',7,false,false,0,'Xe 7d; design note xe* (star meaning unclear). Assumed base ROOK movement.'),
 -- === PHAO (CANNON) ===
 ('60000000-0000-4000-8000-000000000033','tran-khat-chan-cannon','50000000-0000-4000-8000-000000000033','CANNON',NULL,'Tran Khat Chan',6,false,false,0,'Phao 6d; red-marked - no gameplay meaning. Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000034','yet-kieu-cannon','50000000-0000-4000-8000-000000000034','CANNON',NULL,'Yet Kieu',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000035','ly-thuong-kiet-cannon','50000000-0000-4000-8000-000000000035','CANNON',NULL,'Ly Thuong Kiet',6,false,false,0,'Phao 6d; passive same as Xe variant: ko bi can boi thanh rao coc chua implement.'),
 ('60000000-0000-4000-8000-000000000036','dinh-le-cannon','50000000-0000-4000-8000-000000000036','CANNON',NULL,'Dinh Le',6,false,false,0,'Phao 6d; design note phao* (star meaning unclear). Base CANNON movement.'),
 ('60000000-0000-4000-8000-000000000037','nguyen-xi-cannon','50000000-0000-4000-8000-000000000037','CANNON',NULL,'Nguyen Xi',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000038','nguyen-van-tuyet-cannon','50000000-0000-4000-8000-000000000038','CANNON',NULL,'Nguyen Van Tuyet',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000039','nguyen-van-loc-cannon','50000000-0000-4000-8000-000000000039','CANNON',NULL,'Nguyen Van Loc',6,false,false,0,'Phao 6d; base CANNON movement. COMPLETE.'),
 -- === MA (HORSE) ===
 ('60000000-0000-4000-8000-000000000040','tran-quoc-toan-horse','50000000-0000-4000-8000-000000000040','HORSE',NULL,'Tran Quoc Toan',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000041','tran-quang-khai-horse','50000000-0000-4000-8000-000000000041','HORSE',NULL,'Tran Quang Khai',4,false,false,0,'Ma 4d; design note ma* (star meaning unclear). Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000042','luu-nhan-chu-horse','50000000-0000-4000-8000-000000000042','HORSE',NULL,'Luu Nhan Chu',4,false,false,0,'Ma 4d; red-marked - no gameplay meaning. Base HORSE movement.'),
 ('60000000-0000-4000-8000-000000000043','le-sat-horse','50000000-0000-4000-8000-000000000043','HORSE',NULL,'Le Sat',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000044','ly-van-bu-horse','50000000-0000-4000-8000-000000000044','HORSE',NULL,'Ly Van Bu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.'),
 ('60000000-0000-4000-8000-000000000045','vo-dinh-tu-horse','50000000-0000-4000-8000-000000000045','HORSE',NULL,'Vo Dinh Tu',4,false,false,0,'Ma 4d; base HORSE movement. COMPLETE.')
 ON CONFLICT (code) DO UPDATE SET character_id = EXCLUDED.character_id, class_code = EXCLUDED.class_code,
   trait_id = EXCLUDED.trait_id, name = EXCLUDED.name, setup_points = EXCLUDED.setup_points,
   description = EXCLUDED.description;
COMMIT;