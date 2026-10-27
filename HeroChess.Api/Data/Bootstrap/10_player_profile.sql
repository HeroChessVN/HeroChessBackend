-- Vai trò file: Thêm cột hồ sơ hiển thị (avatar/khung) cho user_account; script idempotent, chạy lại được.
-- avatar_name/frame_name là chuỗi không dấu cách, không chứa '/' hoặc '\'; avatar_path lưu tên file ảnh đã tải lên.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;

ALTER TABLE user_account ADD COLUMN IF NOT EXISTS avatar_name varchar(80);
ALTER TABLE user_account ADD COLUMN IF NOT EXISTS frame_name varchar(80);
-- avatar_path chỉ chứa tên file đã lưu trong thư mục avatar; dựng URL tuyệt đối ở tầng API.
ALTER TABLE user_account ADD COLUMN IF NOT EXISTS avatar_path varchar(160);

-- Tên/khung phải là chuỗi không dấu cách, không có dấu gạch chéo để ghép URL an toàn.
ALTER TABLE user_account DROP CONSTRAINT IF EXISTS ck_user_avatar_name;
ALTER TABLE user_account ADD CONSTRAINT ck_user_avatar_name
    CHECK (avatar_name IS NULL OR avatar_name !~ '[\s/\\]');
ALTER TABLE user_account DROP CONSTRAINT IF EXISTS ck_user_frame_name;
ALTER TABLE user_account ADD CONSTRAINT ck_user_frame_name
    CHECK (frame_name IS NULL OR frame_name !~ '[\s/\\]');
-- avatar_path chỉ là tên file: không dấu cách, không dấu gạch chéo, không dấu chấm kép.
ALTER TABLE user_account DROP CONSTRAINT IF EXISTS ck_user_avatar_path;
ALTER TABLE user_account ADD CONSTRAINT ck_user_avatar_path
    CHECK (avatar_path IS NULL OR avatar_path !~ '[\s/\\]' AND avatar_path !~ '\.\.');

COMMENT ON COLUMN user_account.avatar_name IS 'Mã ảnh đại diện dạng không dấu cách, không chứa / hoặc \.';
COMMENT ON COLUMN user_account.frame_name IS 'Mã khung đại diện dạng không dấu cách, không chứa / hoặc \.';
COMMENT ON COLUMN user_account.avatar_path IS 'Tên file ảnh đã tải lên; API dựng URL tuyệt đối từ request host.';
COMMIT;
