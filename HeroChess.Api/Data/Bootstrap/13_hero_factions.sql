-- Re-running inserts no duplicate factions or links.
BEGIN;

INSERT INTO hero_chess.faction (code, name)
VALUES ('tran', 'Trần'), ('hau-le', 'Hậu Lê'), ('tay-son', 'Tây Sơn')
ON CONFLICT (code) DO NOTHING;

WITH roster(hero_code, faction_key) AS (
    VALUES
        ('tran-hung-dao-general', 'tran'),
        ('tran-thanh-tong-general', 'tran'),
        ('tran-thu-do-advisor', 'tran'),
        ('tran-nhat-duat-advisor', 'tran'),
        ('mac-dinh-chi-advisor', 'tran'),
        ('nguyen-trung-ngan-advisor', 'tran'),
        ('tran-hung-dao-elephant', 'tran'),
        ('da-tuong-elephant', 'tran'),
        ('tran-binh-trong-elephant', 'tran'),
        ('pham-ngu-lao-rook', 'tran'),
        ('tran-khanh-du-rook', 'tran'),
        ('tran-khat-chan-cannon', 'tran'),
        ('yet-kieu-cannon', 'tran'),
        ('tran-quoc-toan-horse', 'tran'),
        ('tran-quang-khai-horse', 'tran'),
        ('le-loi-general', 'haule'),
        ('nguyen-trai-1-advisor', 'haule'),
        ('le-lai-advisor', 'haule'),
        ('le-khoi-elephant', 'haule'),
        ('pham-van-xao-elephant', 'haule'),
        ('dinh-liet-rook', 'haule'),
        ('tran-nguyen-han-rook', 'haule'),
        ('dinh-le-cannon', 'haule'),
        ('nguyen-xi-cannon', 'haule'),
        ('luu-nhan-chu-horse', 'haule'),
        ('le-sat-horse', 'haule'),
        ('quang-trung-general', 'tayson'),
        ('nguyen-nhac-general', 'tayson'),
        ('nguyen-lu-general', 'tayson'),
        ('nguyen-nhac-advisor', 'tayson'),
        ('nguyen-lu-advisor', 'tayson'),
        ('bui-thi-xuan-elephant', 'tayson'),
        ('le-van-hung-elephant', 'tayson'),
        ('vo-van-dung-rook', 'tayson'),
        ('tran-quang-dieu-rook', 'tayson'),
        ('nguyen-van-tuyet-cannon', 'tayson'),
        ('nguyen-van-loc-cannon', 'tayson'),
        ('ly-van-buu-horse', 'tayson'),
        ('vo-dinh-tu-horse', 'tayson')
), faction_keys AS (
    SELECT id, regexp_replace(translate(lower(code), 'ầậâêơà', 'aaaeoa'), '[^a-z]', '', 'g') AS code_key,
        regexp_replace(translate(lower(name), 'ầậâêơà', 'aaaeoa'), '[^a-z]', '', 'g') AS name_key
    FROM hero_chess.faction
), resolved AS (
    SELECT DISTINCT r.hero_code, f.id
    FROM roster r
    JOIN faction_keys f ON f.code_key IN (r.faction_key, 'nha' || r.faction_key)
        OR f.name_key IN (r.faction_key, 'nha' || r.faction_key)
), unambiguous AS (
    SELECT hero_code FROM resolved GROUP BY hero_code HAVING count(*) = 1
)
INSERT INTO hero_chess.hero_faction (hero_id, faction_id)
SELECT h.id, r.id
FROM resolved r
JOIN unambiguous u USING (hero_code)
JOIN hero_chess.hero h ON h.code = r.hero_code
ON CONFLICT (hero_id, faction_id) DO NOTHING;

COMMIT;

SELECT f.code, f.name, count(hf.hero_id) AS hero_count
FROM hero_chess.faction f
LEFT JOIN hero_chess.hero_faction hf ON hf.faction_id = f.id
WHERE f.code IN ('tran', 'hau-le', 'tay-son')
GROUP BY f.id, f.code, f.name
ORDER BY f.code;
