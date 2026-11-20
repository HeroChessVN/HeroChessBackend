-- Apply manually after the advisor-capable API is deployed.
-- For the combined SP/trait rollout prefer 19_hero_sp_and_advisor_traits.sql.
WITH roster(code, name, setup_points) AS (
    VALUES
        ('le-lai-advisor', 'Lê Lai', 2),
        ('mac-dinh-chi-advisor', 'Mạc Đĩnh Chi', NULL),
        ('nguyen-binh-khiem-advisor', 'Nguyễn Bỉnh Khiêm', NULL),
        ('nguyen-lu-advisor', 'Nguyễn Lữ', 2),
        ('nguyen-nhac-advisor', 'Nguyễn Nhạc', 2),
        ('nguyen-trai-1-advisor', 'Nguyễn Trãi', NULL),
        ('tran-nhat-duat-advisor', 'Trần Nhật Duật', NULL),
        ('tran-thu-do-advisor', 'Trần Thủ Độ', 2),
        ('dinh-le-cannon', 'Đinh Lễ', NULL),
        ('ly-thuong-kiet-cannon', 'Lý Thường Kiệt', 7),
        ('nguyen-van-loc-cannon', 'Nguyễn Văn Lộc', NULL),
        ('nguyen-van-tuyet-cannon', 'Nguyễn Văn Tuyết', NULL),
        ('nguyen-xi-cannon', 'Nguyễn Xí', NULL),
        ('yet-kieu-cannon', 'Yết Kiêu', NULL),
        ('bui-thi-xuan-elephant', 'Bùi Thị Xuân', 3),
        ('da-tuong-elephant', 'Dã Tượng', 4),
        ('le-khoi-elephant', 'Lê Khôi', 2),
        ('le-van-hung-elephant', 'Lê Văn Hưng', 2),
        ('pham-van-xao-elephant', 'Phạm Văn Xảo', NULL),
        ('tran-binh-trong-elephant', 'Trần Bình Trọng', 3),
        ('tran-hung-dao-elephant', 'Trần Hưng Đạo', 4),
        ('trung-nhi-elephant', 'Trưng Nhị', 4),
        ('trung-trac-elephant', 'Trưng Trắc', 4),
        ('le-loi-general', 'Lê Lợi', 1),
        ('nguyen-lu-general', 'Nguyễn Lữ', NULL),
        ('nguyen-nhac-general', 'Nguyễn Nhạc', NULL),
        ('quang-trung-general', 'Quang Trung', 2),
        ('tran-hung-dao-general', 'Trần Hưng Đạo', 1),
        ('tran-thanh-tong-general', 'Trần Thánh Tông', NULL),
        ('le-sat-horse', 'Lê Sát', NULL),
        ('ly-van-bu-horse', 'Lý Văn Bưu', NULL),
        ('ly-van-buu-horse', 'Lý Văn Bưu', NULL),
        ('tran-quang-khai-horse', 'Trần Quang Khải', NULL),
        ('tran-quoc-toan-horse', 'Trần Quốc Toản', NULL),
        ('vo-dinh-tu-horse', 'Võ Đình Tú', NULL),
        ('ly-thuong-kiet-rook', 'Lý Thường Kiệt', 8),
        ('pham-ngu-lao-rook', 'Phạm Ngũ Lão', NULL),
        ('tran-khanh-du-rook', 'Trần Khánh Dư', NULL),
        ('tran-nguyen-han-rook', 'Trần Nguyên Hãn', NULL),
        ('tran-quang-dieu-rook', 'Trần Quang Diệu', NULL),
        ('vo-van-dung-rook', 'Võ Văn Dũng', NULL),
        ('linh-nha-le-soldier', 'Lính nhà Lê', NULL),
        ('linh-nha-tay-son-soldier', 'Lính nhà Tây Sơn', NULL),
        ('linh-nha-tran-soldier', 'Lính nhà Trần', NULL)
)
UPDATE hero_chess.hero AS h
SET name = r.name,
    setup_points = COALESCE(r.setup_points, h.setup_points)
FROM roster AS r
WHERE h.code = r.code
  AND (h.name IS DISTINCT FROM r.name
       OR h.setup_points IS DISTINCT FROM COALESCE(r.setup_points, h.setup_points))
RETURNING h.code, h.name, h.class_code, h.setup_points;
