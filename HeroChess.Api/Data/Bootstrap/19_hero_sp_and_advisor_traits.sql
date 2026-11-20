-- Apply manually after the API supports the advisor traits; coordinate client rollout.
-- Do not run at every startup: catalogue balancing is an explicit operation.
-- Run the entire DO block. Its temporary tables never need a later SQL request.
DO $advisor_patch$
BEGIN
PERFORM pg_advisory_xact_lock(hashtext('hero_chess_advisor_traits_v1'));
LOCK TABLE hero_chess.hero, hero_chess.hero_trait IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE advisor_roster ON COMMIT DROP AS
SELECT * FROM (VALUES
 ('le-lai-advisor','le-lai-cuu-chua','Lê Lai cứu chúa','active','le_lai.swap_general',
  '{"cooldownTurns":8}'::jsonb,'Lê Lai hoán đổi vị trí với Tướng đồng minh còn trong cung để che chở chủ tướng. Không cần đường đi trống giữa hai quân.'),
 ('tran-thu-do-advisor','tran-thu-do-tran-quoc-ho-quan','Trấn Quốc Hộ Quân','active','tran_thu_do.general_shield',
  '{"durationTurns":2,"cooldownTurns":6}'::jsonb,'Giữ vững quốc gia, Trần Thủ Độ dựng Khiên che chở chủ tướng trong thời gian ngắn. Chỉ có thể bảo vệ Tướng đồng minh.'),
 ('nguyen-nhac-advisor','nguyen-nhac-thai-duc-linh-bo','Thái Đức Linh Bộ','special_move','advisor.king_step',
  '{"maxSteps":1,"palaceOnly":true,"orthogonal":true,"diagonal":true}'::jsonb,'Nguyễn Nhạc linh hoạt xoay chuyển thế phòng ngự, có thể tiến thoái một ô theo tám hướng trong cung nhà.'),
 ('nguyen-lu-advisor','nguyen-lu-dong-dinh-linh-bo','Đông Định Linh Bộ','special_move','advisor.king_step',
  '{"maxSteps":1,"palaceOnly":true,"orthogonal":true,"diagonal":true}'::jsonb,'Nguyễn Lữ uyển chuyển giữ thế trận, có thể di chuyển một ô theo tám hướng trong cung nhà.')
) AS roster(hero_code,trait_code,trait_name,kind,implementation_key,parameters,description);

CREATE TEMP TABLE balanced_hero_sp ON COMMIT DROP AS
SELECT * FROM (VALUES
 ('le-lai-advisor',2),('tran-thu-do-advisor',2),('nguyen-nhac-advisor',2),('nguyen-lu-advisor',2),
 ('le-khoi-elephant',2),('le-van-hung-elephant',2),('pham-van-xao-elephant',2),
 ('bui-thi-xuan-elephant',3),('tran-binh-trong-elephant',3),('da-tuong-elephant',4),
 ('tran-hung-dao-elephant',4),('trung-trac-elephant',4),('trung-nhi-elephant',4),('ly-thuong-kiet-cannon',7)
) AS prices(code,sp);

 IF (SELECT count(*) FROM hero_chess.hero h JOIN pg_temp.balanced_hero_sp p USING(code)) <> 14 THEN
  RAISE EXCEPTION 'The expected hero roster is incomplete. No balancing changes were applied.';
 END IF;
 IF EXISTS (
  SELECT 1 FROM pg_temp.advisor_roster r JOIN hero_chess.hero h ON h.code=r.hero_code
  LEFT JOIN hero_chess.hero_trait t ON t.id=h.trait_id
  WHERE h.class_code <> 'ADVISOR' OR (h.trait_id IS NOT NULL AND
    (t.code IS DISTINCT FROM r.trait_code OR t.implementation_key IS DISTINCT FROM r.implementation_key OR t.kind IS DISTINCT FROM r.kind))
 ) THEN
  RAISE EXCEPTION 'An advisor has an unexpected class or trait. Review the catalogue before applying this patch.';
 END IF;

INSERT INTO hero_chess.hero_trait(id,code,name,kind,implementation_key,parameters,description)
SELECT md5('hero_chess.hero_trait:'||trait_code)::uuid,trait_code,trait_name,kind,implementation_key,parameters,description
FROM pg_temp.advisor_roster
ON CONFLICT(code) DO UPDATE SET name=EXCLUDED.name,kind=EXCLUDED.kind,
 implementation_key=EXCLUDED.implementation_key,parameters=EXCLUDED.parameters,description=EXCLUDED.description;

UPDATE hero_chess.hero h SET trait_id=t.id
FROM pg_temp.advisor_roster r JOIN hero_chess.hero_trait t ON t.code=r.trait_code
WHERE h.code=r.hero_code AND h.trait_id IS DISTINCT FROM t.id;

UPDATE hero_chess.hero h SET setup_points=p.sp
FROM pg_temp.balanced_hero_sp p WHERE h.code=p.code AND h.setup_points IS DISTINCT FROM p.sp;

UPDATE hero_chess.team_skill
SET description=regexp_replace(description, ', hồi chiêu [0-9]+', '', 'g')
WHERE code IN ('thanh','rao','khien') AND description ~ ', hồi chiêu [0-9]+';

DROP TABLE pg_temp.advisor_roster, pg_temp.balanced_hero_sp;
END
$advisor_patch$;

-- Read-only verification: safe to run separately, even in a different session.
SELECT h.code,h.setup_points,t.name AS trait_name,t.implementation_key,t.parameters
FROM hero_chess.hero h LEFT JOIN hero_chess.hero_trait t ON t.id=h.trait_id
WHERE h.code IN (
 'le-lai-advisor','tran-thu-do-advisor','nguyen-nhac-advisor','nguyen-lu-advisor',
 'le-khoi-elephant','le-van-hung-elephant','pham-van-xao-elephant','bui-thi-xuan-elephant',
 'tran-binh-trong-elephant','da-tuong-elephant','tran-hung-dao-elephant',
 'trung-trac-elephant','trung-nhi-elephant','ly-thuong-kiet-cannon'
)
ORDER BY h.code;
