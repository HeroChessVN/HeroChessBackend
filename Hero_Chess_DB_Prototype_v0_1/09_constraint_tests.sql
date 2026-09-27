-- Vai trò file: Kiểm tra ràng buộc SQL trong transaction rồi rollback; expect_failure kiểm tra SQLSTATE, assert_true kiểm tra điều kiện.
-- Run AFTER 03_seed_dev_only.sql in a disposable DB. All test changes roll back.
BEGIN;
SET LOCAL search_path = hero_chess, pg_catalog;
CREATE TEMP TABLE hc_test_results(name text NOT NULL, result text NOT NULL);
-- expect_failure: Chạy SQL test và assert lỗi có đúng SQLSTATE mong đợi.
CREATE FUNCTION pg_temp.expect_failure(test_name text, statement text, expected_code text)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE actual_code text;
BEGIN
 BEGIN EXECUTE statement;
 EXCEPTION WHEN OTHERS THEN GET STACKED DIAGNOSTICS actual_code = RETURNED_SQLSTATE;
 END;
 IF actual_code IS DISTINCT FROM expected_code THEN
  RAISE EXCEPTION 'FAIL %: expected %, got %', test_name, expected_code, COALESCE(actual_code,'success');
 END IF;
 INSERT INTO hc_test_results VALUES(test_name,'PASS');
END $$;
-- assert_true: Ném lỗi nếu điều kiện kiểm tra không đúng.
CREATE FUNCTION pg_temp.assert_true(test_name text, condition boolean)
RETURNS void LANGUAGE plpgsql AS $$ BEGIN
 IF condition IS DISTINCT FROM true THEN RAISE EXCEPTION 'FAIL %',test_name; END IF;
 INSERT INTO hc_test_results VALUES(test_name,'PASS');
END $$;
SELECT pg_temp.assert_true('Seed lineup: 16 slots / 43 SP',
 (SELECT count(*)=16 AND sum(h.setup_points)=43 FROM lineup_entry e JOIN hero h ON h.id=e.hero_id
 WHERE e.lineup_id='80000000-0000-4000-8000-000000000001'));
SELECT pg_temp.assert_true('Seed lineup: 3 skill slots',
 (SELECT count(*)=3 FROM lineup_skill WHERE lineup_id='80000000-0000-4000-8000-000000000001'));
SELECT pg_temp.expect_failure('Wrong hero class in rook slot',
 $$UPDATE lineup_entry SET hero_id='60000000-0000-4000-8000-000000000005' WHERE lineup_id='80000000-0000-4000-8000-000000000001' AND slot_no=1$$,'23503');
SELECT pg_temp.expect_failure('Duplicate team skill',
 $$UPDATE lineup_skill SET team_skill_id='70000000-0000-4000-8000-000000000001' WHERE lineup_id='80000000-0000-4000-8000-000000000001' AND slot_no=2$$,'23505');
SELECT pg_temp.expect_failure('Skill slot beyond three',
 $$UPDATE lineup_skill SET slot_no=4 WHERE lineup_id='80000000-0000-4000-8000-000000000001' AND slot_no=3$$,'23514');
SELECT pg_temp.expect_failure('Negative wallet',
 $$UPDATE player_wallet SET balance=-1 WHERE player_id='50000000-0000-4000-8000-000000000001'$$,'23514');
SELECT pg_temp.expect_failure('Duplicate hero ownership',
 $$INSERT INTO player_hero(player_id,hero_id,acquired_via) VALUES('50000000-0000-4000-8000-000000000001','60000000-0000-4000-8000-000000000001','test')$$,'23505');
SELECT pg_temp.expect_failure('Two active rulesets',
 $$INSERT INTO ruleset(code,name,is_active) VALUES('test-other','Test',true)$$,'23505');
INSERT INTO cosmetic(id,code,hero_id,name,coin_price,asset_key) VALUES
 ('c0000000-0000-4000-8000-000000000001','dev-skin-general','60000000-0000-4000-8000-000000000005','DEV skin',1,'dev/skin');
SELECT pg_temp.expect_failure('Skin assigned to wrong hero',
 $$UPDATE lineup_entry SET cosmetic_id='c0000000-0000-4000-8000-000000000001' WHERE lineup_id='80000000-0000-4000-8000-000000000001' AND slot_no=1$$,'23503');
INSERT INTO game_match(id,mode,ruleset_id,ruleset_snapshot,content_version) VALUES
 ('90000000-0000-4000-8000-000000000001','ranked','10000000-0000-4000-8000-000000000001','{"budget":50}','dev-test');
INSERT INTO match_participant(match_id,side,player_id,participant_type,source_lineup_id,lineup_snapshot) VALUES
 ('90000000-0000-4000-8000-000000000001','red','50000000-0000-4000-8000-000000000001','human','80000000-0000-4000-8000-000000000001','{"fixture":"frozen-lineup"}'),
 ('90000000-0000-4000-8000-000000000001','black','50000000-0000-4000-8000-000000000002','human','80000000-0000-4000-8000-000000000002','{"fixture":"frozen-lineup"}');
UPDATE game_match SET status='active',started_at=now() WHERE id='90000000-0000-4000-8000-000000000001';
SET CONSTRAINTS ALL IMMEDIATE;
SELECT pg_temp.assert_true('Ranked accepts two registered humans',true);
SELECT pg_temp.expect_failure('Same player on both sides',
 $$UPDATE match_participant SET player_id='50000000-0000-4000-8000-000000000001' WHERE match_id='90000000-0000-4000-8000-000000000001' AND side='black'$$,'23505');
SELECT pg_temp.expect_failure('Ranked rejects bot',
 $$UPDATE match_participant SET participant_type='bot',player_id=NULL,bot_config='{}' WHERE match_id='90000000-0000-4000-8000-000000000001' AND side='black'$$,'P0001');
INSERT INTO user_account(id,display_name,is_guest) VALUES('50000000-0000-4000-8000-000000000003','DEV guest',true);
SELECT pg_temp.expect_failure('Ranked rejects guest',
 $$UPDATE match_participant SET player_id='50000000-0000-4000-8000-000000000003' WHERE match_id='90000000-0000-4000-8000-000000000001' AND side='black'$$,'P0001');
SELECT pg_temp.expect_failure('Active match cannot lose a participant',
 $$DELETE FROM match_participant WHERE match_id='90000000-0000-4000-8000-000000000001' AND side='black'$$,'P0001');
SELECT pg_temp.expect_failure('Participant side cannot be reassigned',
 $$UPDATE match_participant SET side='black' WHERE match_id='90000000-0000-4000-8000-000000000001' AND side='red'$$,'23505');
SELECT pg_temp.expect_failure('Completed match needs result and reason',
 $$UPDATE game_match SET status='completed' WHERE id='90000000-0000-4000-8000-000000000001'$$,'23514');
INSERT INTO match_state(match_id,side_to_move,state) VALUES('90000000-0000-4000-8000-000000000001','red','{"pieces":[]}');
INSERT INTO match_action(match_id,sequence_no,command_id,kind,state_after,received_at) VALUES
 ('90000000-0000-4000-8000-000000000001',0,'b0000000-0000-4000-8000-000000000000','start','{"pieces":[]}',now());
SELECT pg_temp.expect_failure('Duplicate match command',
 $$INSERT INTO match_action(match_id,sequence_no,command_id,kind,state_after,received_at) VALUES('90000000-0000-4000-8000-000000000001',1,'b0000000-0000-4000-8000-000000000000','move','{}',now())$$,'23505');
SELECT pg_temp.expect_failure('Duplicate log sequence',
 $$INSERT INTO match_action(match_id,sequence_no,command_id,kind,state_after,received_at) VALUES('90000000-0000-4000-8000-000000000001',0,'b0000000-0000-4000-8000-000000000001','start','{}',now())$$,'23505');
SELECT pg_temp.expect_failure('Snapshot must be JSON object',
 $$UPDATE match_state SET state='[]' WHERE match_id='90000000-0000-4000-8000-000000000001'$$,'23514');
UPDATE game_match SET status='completed',result='red_win',end_reason='checkmate',ended_at=now() WHERE id='90000000-0000-4000-8000-000000000001';
UPDATE player_wallet SET balance=10 WHERE player_id='50000000-0000-4000-8000-000000000001';
INSERT INTO coin_transaction(player_id,idempotency_key,kind,amount,balance_after,match_id,match_side) VALUES
 ('50000000-0000-4000-8000-000000000001','reward-test-1','match_reward',10,10,'90000000-0000-4000-8000-000000000001','red');
SELECT pg_temp.expect_failure('Duplicate wallet idempotency key',
 $$INSERT INTO coin_transaction(player_id,idempotency_key,kind,amount,balance_after) VALUES('50000000-0000-4000-8000-000000000001','reward-test-1','starter',10,20)$$,'23505');
SELECT pg_temp.expect_failure('Duplicate reward with different request key',
 $$INSERT INTO coin_transaction(player_id,idempotency_key,kind,amount,balance_after,match_id,match_side) VALUES('50000000-0000-4000-8000-000000000001','reward-test-2','match_reward',10,20,'90000000-0000-4000-8000-000000000001','red')$$,'23505');
SELECT pg_temp.expect_failure('Reward must reference correct participant',
 $$INSERT INTO coin_transaction(player_id,idempotency_key,kind,amount,balance_after,match_id,match_side) VALUES('50000000-0000-4000-8000-000000000002','reward-test-3','match_reward',10,10,'90000000-0000-4000-8000-000000000001','red')$$,'23503');
DELETE FROM lineup WHERE id='80000000-0000-4000-8000-000000000001';
SELECT pg_temp.assert_true('Deleting source lineup preserves historical snapshot',
 (SELECT lineup_snapshot->>'fixture'='frozen-lineup' FROM match_participant WHERE match_id='90000000-0000-4000-8000-000000000001' AND side='red'));
SELECT * FROM hc_test_results ORDER BY name;
ROLLBACK;
