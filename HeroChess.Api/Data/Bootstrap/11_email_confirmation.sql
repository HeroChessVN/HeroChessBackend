ALTER TABLE hero_chess.user_account
    ADD COLUMN IF NOT EXISTS email_confirmed boolean NOT NULL DEFAULT false;
