-- Apply before deploying the API that records Lam Son review completion.
BEGIN;
SET LOCAL lock_timeout = '5s';
ALTER TABLE hero_chess.match_action DROP CONSTRAINT IF EXISTS match_action_kind_check;
ALTER TABLE hero_chess.match_action ADD CONSTRAINT match_action_kind_check
    CHECK (kind IN ('start','move','hero_active','team_skill','timeout','resign','undo','cancel','end_turn'));
COMMIT;
