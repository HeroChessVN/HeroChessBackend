-- Run after 13_hero_factions.sql and deploy a backend with the three handlers first.
BEGIN;

DO $$
BEGIN
    IF (SELECT count(*) FROM hero_chess.faction WHERE code IN ('tran', 'hau-le', 'tay-son')) <> 3 THEN
        RAISE EXCEPTION 'Run 13_hero_factions.sql before inserting faction skills.';
    END IF;
END $$;

INSERT INTO hero_chess.team_skill
    (code, name, faction_id, implementation_key, cooldown_turns, is_enabled, eligibility, description)
SELECT skill.code, skill.name, faction.id, skill.code, skill.cooldown, true, '{"minNonSoldierFactionHeroes":5}'::jsonb, skill.description
FROM (VALUES
    ('tran', 'bach_dang_tran_phap', 'Bạch Đằng Trận Pháp', 8,
        'Đặt ba bẫy ẩn trên sông trong 6 lượt. Quân địch đi qua bị khóa chân 4 lượt. Mỗi bẫy dùng một lần.'),
    ('hau-le', 'lam_son_quan_luoc', 'Lam Sơn Quân Lược', 16,
        'Xem riêng chuỗi dự đoán tối đa năm hành động: địch, ta, địch, ta, địch; gồm ba hành động của địch và hai hành động của ta. Hiển thị thêm tối đa hai phương án nguy hiểm của địch ở lượt dự đoán đầu tiên. Dự báo có thể ngắn hơn khi hết ngân sách phân tích hoặc trận kết thúc.'),
    ('tay-son', 'hich_xuat_quan', 'Hịch Xuất Quân', 12,
        'Trong 6 lượt, Tốt phe mình có thể đi thêm một ô theo hướng hợp lệ nếu ô trung gian trống.')
) AS skill(faction_code, code, name, cooldown, description)
JOIN hero_chess.faction faction ON faction.code = skill.faction_code
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name, faction_id = EXCLUDED.faction_id,
    implementation_key = EXCLUDED.implementation_key, cooldown_turns = EXCLUDED.cooldown_turns,
    eligibility = EXCLUDED.eligibility, description = EXCLUDED.description
WHERE NOT EXISTS (SELECT 1 FROM hero_chess.ruleset WHERE is_active AND config->>'skillTimingVersion'='2');

COMMIT;
