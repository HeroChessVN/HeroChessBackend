import { test } from 'node:test';
import assert from 'node:assert/strict';
import { lineupPayload, skillTarget } from './api.js';

test('lineup uses server slot numbers and revision', () => {
  const result = lineupPayload({ ruleset: { id: 'rules' }, slots: [{ slotNo: 7 }] }, ' Test ', { 7: 'hero' }, ['skill'], 2);
  assert.deepEqual(result, { name: 'Test', rulesetId: 'rules', entries: [{ slotNo: 7, heroId: 'hero', cosmeticId: null }], skills: [{ slotNo: 1, skillId: 'skill' }], expectedRevision: 2 });
});

test('skill targets match handler shapes', () => {
  assert.deepEqual(skillTarget('thanh', { x: 2, y: 3 }), { position: { x: 2, y: 3 } });
  assert.deepEqual(skillTarget('rao', { x: 2, y: 3 }), { position: { x: 2, y: 3 } });
  assert.equal(skillTarget('tran_hung_dao_tuong.coc', { x: 4, y: 4 }), null);
  assert.deepEqual(skillTarget('khien', null, null, 'piece-id'), { pieceId: 'piece-id' });
  assert.deepEqual(skillTarget('van_coc_tran_giang'), { paths: [4, 5, 6] });
  assert.deepEqual(skillTarget('binh_lam_thuy_hien'), { paths: [4, 5, 6] });
  assert.deepEqual(skillTarget('phan_ky_doat_the', null, 'effect'), { effectId: 'effect' });
  assert.deepEqual(skillTarget('pha_tran_doat_phong', null, 'effect'), { effectId: 'effect' });
  assert.equal(skillTarget('dev.not_implemented'), null);
});
