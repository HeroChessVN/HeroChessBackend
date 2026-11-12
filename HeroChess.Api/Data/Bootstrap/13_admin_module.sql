-- Vai trò file: Mở rộng schema cho module Admin (quyền fine-grained, gift code, hero trong shop).
-- Chỉ ADD bảng/cột/index; không DROP, không đổi dữ liệu lịch sử. Chạy lại được (idempotent).
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
SELECT pg_advisory_xact_lock(hashtext('hero_chess_admin_module_v1'));

-- Quyền fine-grained cho admin. Không có dòng = admin toàn quyền (tương thích ngược với admin cũ).
CREATE TABLE IF NOT EXISTS hero_chess.admin_permission (
    admin_user_id uuid PRIMARY KEY REFERENCES hero_chess.user_account(id) ON DELETE CASCADE,
    permissions text[] NOT NULL DEFAULT ARRAY[]::text[],
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid REFERENCES hero_chess.user_account(id)
);

-- Gift code: mã quy tặng admin tạo, quy đổi một lần/lần theo giới hạn.
CREATE TABLE IF NOT EXISTS hero_chess.gift_code (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code varchar(64) NOT NULL,
    reward_kind varchar(16) NOT NULL CHECK (reward_kind IN ('coin', 'hero', 'cosmetic')),
    reward_quantity integer NOT NULL CHECK (reward_quantity > 0),
    reward_hero_id uuid REFERENCES hero_chess.hero(id),
    reward_cosmetic_id uuid REFERENCES hero_chess.cosmetic(id),
    starts_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz,
    max_redemptions integer NOT NULL CHECK (max_redemptions > 0),
    max_per_player integer NOT NULL DEFAULT 1 CHECK (max_per_player > 0),
    is_active boolean NOT NULL DEFAULT true,
    created_by uuid NOT NULL REFERENCES hero_chess.user_account(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_gift_code_code UNIQUE (code),
    CONSTRAINT ck_gift_code_reward CHECK (
        (reward_kind = 'coin' AND reward_hero_id IS NULL AND reward_cosmetic_id IS NULL)
        OR (reward_kind = 'hero' AND reward_hero_id IS NOT NULL AND reward_cosmetic_id IS NULL)
        OR (reward_kind = 'cosmetic' AND reward_cosmetic_id IS NOT NULL AND reward_hero_id IS NULL))
);

-- Lịch sử quy đổi; redemption_no cho phép giới hạn nhiều lần/người chơi mà vẫn chống quy đổi trùng.
CREATE TABLE IF NOT EXISTS hero_chess.gift_code_redemption (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    gift_code_id uuid NOT NULL REFERENCES hero_chess.gift_code(id) ON DELETE CASCADE,
    player_id uuid NOT NULL REFERENCES hero_chess.user_account(id),
    redemption_no integer NOT NULL CHECK (redemption_no > 0),
    reward_kind varchar(16) NOT NULL,
    reward_quantity integer NOT NULL CHECK (reward_quantity > 0),
    coin_balance_after bigint,
    redeemed_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_gift_code_redemption UNIQUE (gift_code_id, player_id, redemption_no)
);

CREATE INDEX IF NOT EXISTS ix_gift_code_code ON hero_chess.gift_code(code);
CREATE INDEX IF NOT EXISTS ix_gift_code_redemption_code ON hero_chess.gift_code_redemption(gift_code_id, redeemed_at DESC);
CREATE INDEX IF NOT EXISTS ix_gift_code_redemption_player ON hero_chess.gift_code_redemption(player_id, redeemed_at DESC);

-- Hero có mặt trong cửa hàng: tách khỏi is_enabled (được bật để chơi) để admin tắt bán mà không ảnh hưởng gameplay.
ALTER TABLE hero_chess.hero ADD COLUMN IF NOT EXISTS is_in_shop boolean NOT NULL DEFAULT true;
UPDATE hero_chess.hero SET is_in_shop = true WHERE is_in_shop IS NULL;

REVOKE ALL ON hero_chess.admin_permission, hero_chess.gift_code, hero_chess.gift_code_redemption FROM PUBLIC;
DO $$ DECLARE role_name text;
BEGIN
    FOREACH role_name IN ARRAY ARRAY['anon', 'authenticated'] LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('REVOKE ALL ON hero_chess.admin_permission, hero_chess.gift_code, hero_chess.gift_code_redemption FROM %I', role_name);
        END IF;
    END LOOP;
END $$;
COMMIT;
