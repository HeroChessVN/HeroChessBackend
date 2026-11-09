-- Upgrade existing faction skills without changing prices, cooldowns, effects or availability.
UPDATE hero_chess.team_skill
SET eligibility = jsonb_set(eligibility, ARRAY['minNonSoldierFactionHeroes'], '5'::jsonb, true)
WHERE implementation_key IN ('bach_dang_tran_phap', 'lam_son_quan_luoc', 'hich_xuat_quan')
  AND eligibility->'minNonSoldierFactionHeroes' IS DISTINCT FROM '5'::jsonb;
