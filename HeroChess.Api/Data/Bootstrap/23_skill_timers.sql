-- Run manually on an existing DB after the API and clients support skillTimingVersion 2.
-- One-time balance operation; startup only executes it for a newly created database.
DO $skill_timers$
BEGIN
 PERFORM pg_advisory_xact_lock(hashtext('hero_chess_skill_timers_v2'));
 PERFORM set_config('lock_timeout','5s',true);
 LOCK TABLE hero_chess.team_skill,hero_chess.hero_trait,hero_chess.ruleset IN SHARE ROW EXCLUSIVE MODE;
 IF (SELECT count(*) FROM hero_chess.ruleset WHERE is_active) <> 1 THEN
  RAISE EXCEPTION 'Expected exactly one active ruleset. No timers were changed.';
 END IF;
 IF EXISTS(SELECT 1 FROM hero_chess.ruleset WHERE is_active AND config->>'skillTimingVersion'='2') THEN
  RETURN; -- Preserve deliberate tuning after this rollout, including non-default timer values.
 END IF;
 IF EXISTS(SELECT 1 FROM hero_chess.ruleset WHERE is_active AND
    config ? 'skillTimingVersion' AND config->>'skillTimingVersion' IS DISTINCT FROM '1') THEN
  RAISE EXCEPTION 'The ruleset has an unexpected skill timing version.';
 END IF;

 CREATE TEMP TABLE timer_team ON COMMIT DROP AS SELECT * FROM (VALUES
  ('thanh',14,13,6,NULL::integer),('rao',10,9,4,NULL::integer),('khien',14,13,4,NULL::integer),
  ('bach_dang_tran_phap',8,7,6,4),('lam_son_quan_luoc',16,15,NULL::integer,NULL::integer),
  ('hich_xuat_quan',12,11,6,NULL::integer)
 ) AS config(key,old_cd,new_cd,duration,root_duration);
 CREATE TEMP TABLE timer_hero ON COMMIT DROP AS SELECT * FROM (VALUES
  ('quang_trung.special_move',3,7,NULL::integer),('tran_hung_dao_tuong.coc',5,5,3),
  ('le_lai.swap_general',8,7,NULL::integer),('tran_thu_do.general_shield',6,5,2)
 ) AS config(key,old_cd,new_cd,duration);
 IF (SELECT count(*) FROM hero_chess.team_skill s JOIN timer_team t ON t.key=s.implementation_key WHERE s.is_enabled) <> 6 OR
    (SELECT count(*) FROM hero_chess.hero_trait h JOIN timer_hero t ON t.key=h.implementation_key) <> 4 THEN
  RAISE EXCEPTION 'Expected six enabled team skills and four active hero traits. Run prerequisite catalog migrations first.';
 END IF;
 IF EXISTS(SELECT 1 FROM hero_chess.team_skill s JOIN timer_team t ON t.key=s.implementation_key
    WHERE jsonb_typeof(s.parameters) <> 'object' OR s.cooldown_turns IS NULL OR s.cooldown_turns NOT IN(t.old_cd,t.new_cd) OR
      (t.duration IS NOT NULL AND s.parameters ? 'durationTurns' AND s.parameters->'durationTurns' IS DISTINCT FROM to_jsonb(t.duration)) OR
      (t.root_duration IS NOT NULL AND s.parameters ? 'rootDurationTurns' AND s.parameters->'rootDurationTurns' IS DISTINCT FROM to_jsonb(t.root_duration))) OR
    EXISTS(SELECT 1 FROM hero_chess.hero_trait h JOIN timer_hero t ON t.key=h.implementation_key
    WHERE jsonb_typeof(h.parameters) <> 'object' OR h.parameters->'cooldownTurns' IS NULL OR
      h.parameters->'cooldownTurns' NOT IN(to_jsonb(t.old_cd),to_jsonb(t.new_cd)) OR
      (t.duration IS NOT NULL AND h.parameters ? 'durationTurns' AND h.parameters->'durationTurns' IS DISTINCT FROM to_jsonb(t.duration))) THEN
  RAISE EXCEPTION 'Timers differ from the reviewed old/new values. Review the catalogue before applying this balance patch.';
 END IF;

 UPDATE hero_chess.team_skill s SET cooldown_turns=t.new_cd,
  parameters=s.parameters || CASE WHEN t.duration IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('durationTurns',t.duration) END ||
    CASE WHEN t.root_duration IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('rootDurationTurns',t.root_duration) END,
  description=regexp_replace(regexp_replace(regexp_replace(regexp_replace(s.description,
    '; tồn tại [0-9]+ lượt chung','','gi'),
    'Trong [0-9]+ lượt,','Trong thời gian hiệu lực,','g'),
    'trên sông trong [0-9]+ lượt','trên sông có thời hạn','g'),
    'khóa chân [0-9]+ lượt','khóa chân tạm thời','g')
 FROM timer_team t WHERE s.implementation_key=t.key;
 UPDATE hero_chess.hero_trait h SET parameters=h.parameters || jsonb_build_object('cooldownTurns',t.new_cd) ||
    CASE WHEN t.duration IS NULL THEN '{}'::jsonb ELSE jsonb_build_object('durationTurns',t.duration) END,
  description=btrim(regexp_replace(regexp_replace(h.description,
    'Tồn tại [0-9]+ lượt chung, hồi chiêu [0-9]+;?','','gi'),
    '; hồi chiêu [0-9]+ lượt chung','','gi'))
 FROM timer_hero t WHERE h.implementation_key=t.key;
 UPDATE hero_chess.ruleset SET config=config || '{"skillTimingVersion":2}'::jsonb WHERE is_active;
 DROP TABLE pg_temp.timer_team,pg_temp.timer_hero;
END
$skill_timers$;

SELECT code,cooldown_turns,parameters FROM hero_chess.team_skill WHERE is_enabled ORDER BY code;
