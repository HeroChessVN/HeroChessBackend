import { test } from 'node:test';
import assert from 'node:assert/strict';
import { lineupView, opponentSideOf, ownSideOf, remainingSeconds, selectionDeadline, sideMeta, sideState, sideStateLabel, sideTitle } from './waiting.js';

test('side state follows confirm over pick over waiting', () => {
  assert.equal(sideState({ confirmed: true, selectedLineup: null }), 'confirmed');
  assert.equal(sideState({ confirmed: false, selectedLineup: { id: 'x' } }), 'picked');
  assert.equal(sideState({ confirmed: false, selectedLineup: null }), 'waiting');
});

test('side state labels cover human sides and bots', () => {
  assert.equal(sideStateLabel({ confirmed: true }), 'Đã xác nhận');
  assert.equal(sideStateLabel({ confirmed: false, selectedLineup: { id: 'x' } }), 'Đã chọn đội hình');
  assert.equal(sideStateLabel({ confirmed: false, selectedLineup: null }), 'Đang chờ chọn');
  assert.equal(sideStateLabel({ isBot: true, confirmed: false }), 'Bot đang chuẩn bị');
  assert.equal(sideStateLabel({ isBot: true, confirmed: true }), 'Bot đã sẵn sàng');
});

test('own side is found by the caller player id', () => {
  const sides = [
    { side: 'red', player: { playerId: 'p1' } },
    { side: 'black', player: { playerId: 'p2' } }
  ];
  assert.equal(ownSideOf(sides, 'p1').side, 'red');
  assert.equal(ownSideOf(sides, 'p2').side, 'black');
  assert.equal(ownSideOf(sides, 'p9', 'red').side, 'red');
  assert.equal(ownSideOf(sides, null, 'black').side, 'black');
  assert.equal(ownSideOf(sides, 'p9', null), null);
  assert.equal(ownSideOf(sides, null, null), null);
});

test('opponent side is the other side, and the bot has no player id', () => {
  const sides = [
    { side: 'red', player: { playerId: 'p2' } },
    { side: 'black', isBot: true, player: null }
  ];
  assert.equal(opponentSideOf(sides, ownSideOf(sides, 'p2')).side, 'black');
  assert.equal(opponentSideOf(sides, ownSideOf(sides, 'p2')).isBot, true);
  assert.equal(opponentSideOf(sides, ownSideOf(sides, 'p1', 'red')).side, 'black');
  assert.equal(opponentSideOf(sides, null), null);
});

test('selection deadline is the match creation plus the selection window', () => {
  const created = '2026-10-27T10:00:00Z';
  assert.equal(selectionDeadline(created), Date.parse(created) + 120000);
  assert.equal(selectionDeadline(null), null);
  assert.equal(selectionDeadline('không phải ngày'), null);
});

test('remaining seconds counts down and never goes negative', () => {
  const deadline = Date.parse('2026-10-27T10:00:30Z');
  const now = Date.parse('2026-10-27T10:00:00Z');
  assert.equal(remainingSeconds(deadline, now), 30);
  assert.equal(remainingSeconds(deadline, now + 5000), 25);
  assert.equal(remainingSeconds(deadline, now + 60000), 0);
  assert.equal(remainingSeconds(null, now), null);
});

test('lineup view joins hero and skill names from the catalog', () => {
  const catalog = {
    heroes: [{ id: 'h1', name: 'Trần Binh Trọng', classCode: 'ELEPHANT', setupPoints: 3 }],
    teamSkills: [{ id: 's1', name: 'Thành', implementationKey: 'thanh' }]
  };
  const view = lineupView({
    id: 'l1', name: 'Đội 1', totalSp: 16,
    entries: [{ slotNo: 1, heroId: 'h1' }], skills: [{ slotNo: 1, skillId: 's1' }]
  }, catalog);
  assert.equal(view.name, 'Đội 1');
  assert.equal(view.totalSp, 16);
  assert.equal(view.heroes[0].name, 'Trần Binh Trọng');
  assert.equal(view.heroes[0].classCode, 'ELEPHANT');
  assert.equal(view.heroes[0].setupPoints, 3);
  assert.equal(view.skills[0].name, 'Thành');
  assert.equal(view.skills[0].implementationKey, 'thanh');
});

test('lineup view survives a missing catalog and a missing lineup', () => {
  assert.equal(lineupView(null, null), null);
  const view = lineupView({ id: 'l1', name: 'Đội 1', totalSp: 0, entries: [{ slotNo: 1, heroId: 'h9' }], skills: [] }, null);
  assert.equal(view.heroes[0].name, 'Hero');
  assert.equal(view.heroes[0].setupPoints, null);
  assert.deepEqual(view.skills, []);
});

test('side title and meta fall back for bot and missing profile', () => {
  assert.equal(sideTitle({ isBot: true }), 'Bot');
  assert.equal(sideTitle({ player: { displayName: 'Lê Lợi' } }), 'Lê Lợi');
  assert.equal(sideTitle({}), 'Người chơi');
  assert.equal(sideMeta({ side: 'red', player: { elo: 1200, avatarName: 'leloi', frameName: 'khung' } }), 'Đỏ · Elo 1200 · Ảnh: leloi · Khung: khung');
  assert.equal(sideMeta({ side: 'black' }), 'Đen');
});
