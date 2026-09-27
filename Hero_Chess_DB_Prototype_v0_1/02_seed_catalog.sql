-- Vai trò file: Seed ruleset, class, slot, hero, phe, skill và cosmetic; dữ liệu catalog không đồng nghĩa handler gameplay đã triển khai.
-- Minimal reference data; not the final 12-14 hero roster or a balance decision.
-- Run after 01_schema.sql. May be re-run: existing rows are not overwritten.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
INSERT INTO chess_class(code,name_vi,base_sp,required_count,base_movement_code) VALUES
 ('GENERAL','Tướng',0,1,'xiangqi.general'),('ADVISOR','Sĩ',1,2,'xiangqi.advisor'),
 ('ELEPHANT','Tượng',1,2,'xiangqi.elephant'),('ROOK','Xe',7,2,'xiangqi.rook'),
 ('CANNON','Pháo',6,2,'xiangqi.cannon'),('HORSE','Mã',4,2,'xiangqi.horse'),
 ('SOLDIER','Tốt',1,5,'xiangqi.soldier') ON CONFLICT DO NOTHING;
INSERT INTO lineup_slot(slot_no,class_code,start_x,start_y) VALUES
 (1,'ROOK',0,0),(2,'HORSE',1,0),(3,'ELEPHANT',2,0),(4,'ADVISOR',3,0),
 (5,'GENERAL',4,0),(6,'ADVISOR',5,0),(7,'ELEPHANT',6,0),(8,'HORSE',7,0),(9,'ROOK',8,0),
 (10,'CANNON',1,2),(11,'CANNON',7,2),
 (12,'SOLDIER',0,3),(13,'SOLDIER',2,3),(14,'SOLDIER',4,3),(15,'SOLDIER',6,3),(16,'SOLDIER',8,3)
 ON CONFLICT DO NOTHING;
INSERT INTO ruleset(id,code,name,config,is_active) VALUES
 ('10000000-0000-4000-8000-000000000001','prototype-v0.1','Hero Chess prototype',
 '{"timeoutPolicy":"skip_turn","limitResolution":"remaining_sp_then_draw","rankedBotsAllowed":false,"teamSkillCount":3,"maxFactionSkills":1,"factionEligibilityAt":"match_start","pendingDecisions":["timeout_while_in_check","action_limit_counting","effect_resolution_order","revive_tie_break"]}',
 true) ON CONFLICT DO NOTHING;
INSERT INTO historical_character(id,code,name) VALUES
 ('20000000-0000-4000-8000-000000000001','tran-binh-trong','Trần Bình Trọng'),
 ('20000000-0000-4000-8000-000000000002','bui-thi-xuan','Bùi Thị Xuân'),
 ('20000000-0000-4000-8000-000000000003','da-tuong','Dã Tượng') ON CONFLICT DO NOTHING;
INSERT INTO hero_trait(id,code,name,kind,implementation_key,parameters,description) VALUES
 ('30000000-0000-4000-8000-000000000001','elephant-diagonal-1-2','Tượng đi chéo 1–2 bước','special_move','elephant.diagonal_range',
 '{"minSteps":1,"maxSteps":2,"ownHalfOnly":true,"canJump":false}', 'Nước đi và nước ăn theo đường chéo; không vượt vật cản, không qua sông.'),
 ('327e712d-26d2-5da4-5c9a-87a1ff74c1e1','elephant-diagonal-1-2-40000000000040008000000000000002','Tượng đi chéo 1–2 bước — Bùi Thị Xuân','special_move','elephant.diagonal_range',
 '{"minSteps":1,"maxSteps":2,"ownHalfOnly":true,"canJump":false}', 'Nước đi và nước ăn theo đường chéo; không vượt vật cản, không qua sông.'),
 ('30000000-0000-4000-8000-000000000002','da-tuong-diagonal','Dã Tượng đi chéo đổi độ dài','special_move','elephant.alternating_distance',
 '{"minSteps":1,"maxSteps":4,"ownHalfOnly":true,"canJump":false,"cannotRepeatLastDistance":true,"resetDistanceOnRevive":true}',
 'Lưu lastMoveDistance trong trạng thái trận; lần đi tiếp theo không trùng độ dài lần trước.') ON CONFLICT DO NOTHING;
-- Coin prices, ownership grants and release activation have NOT been decided.
-- Named heroes remain disabled until the team configures their store/release policy.
INSERT INTO hero(id,code,character_id,class_code,trait_id,name,setup_points,is_enabled,description) VALUES
 ('40000000-0000-4000-8000-000000000001','tran-binh-trong-elephant','20000000-0000-4000-8000-000000000001','ELEPHANT','30000000-0000-4000-8000-000000000001','Trần Bình Trọng',2,false,'Cấu hình gameplay; chưa bật phát hành.'),
 ('40000000-0000-4000-8000-000000000002','bui-thi-xuan-elephant','20000000-0000-4000-8000-000000000002','ELEPHANT','327e712d-26d2-5da4-5c9a-87a1ff74c1e1','Bùi Thị Xuân',2,false,'Cấu hình gameplay; chưa bật phát hành.'),
 ('40000000-0000-4000-8000-000000000003','da-tuong-elephant','20000000-0000-4000-8000-000000000003','ELEPHANT','30000000-0000-4000-8000-000000000002','Dã Tượng',3,false,'Cấu hình gameplay; chưa bật phát hành.') ON CONFLICT DO NOTHING;
COMMIT;
