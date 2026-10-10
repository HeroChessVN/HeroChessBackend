import { test } from 'node:test';
import assert from 'node:assert/strict';
import { actionLabel, squareName } from './history.js';

test('replay starts at sequence zero and move labels use previous board', () => {
  const initial = { sequenceNo: 0, kind: 'start', stateAfter: { pieces: [
    { pieceId: 'p1', heroId: 'hero1', class: 'soldier', side: 'red', status: 'alive', position: { x: 0, y: 3 } }
  ] } };
  const move = { sequenceNo: 1, kind: 'move', actorSide: 'red', resolvedEvents: [
    { type: 'turn.started', side: 'red' }, { type: 'piece.moved', pieceId: 'p1', to: { x: 0, y: 4 } }
  ], stateAfter: { pieces: [{ pieceId: 'p1', status: 'alive', position: { x: 0, y: 4 } }] } };
  assert.equal(actionLabel(initial), 'Bàn cờ ban đầu · chưa đi nước nào');
  assert.equal(actionLabel(move, initial, { heroes: [{ id: 'hero1', name: 'Bùi Thị Xuân' }] }), 'Đỏ: Bùi Thị Xuân a4 → a5');
  assert.equal(squareName({ x: 8, y: 9 }), 'i10');
  assert.equal(actionLabel({ kind: 'team_skill', actorSide: 'black', resolvedEvents: [{ type: 'team_skill.activated', code: 'thanh' }] }, null,
    { teamSkills: [{ implementationKey: 'thanh', name: 'Thành' }] }), 'Đen: dùng Thành');
  assert.equal(actionLabel({ kind: 'undo', actorSide: 'red', resolvedEvents: [{ type: 'match.undone', targetSequence: 0 }] }), 'Đỏ: hoàn tác về bước 0');
});
