import { test } from 'node:test';
import assert from 'node:assert/strict';
import { canPlaceStructure, defaultLineup, lineupPayload, pollRetryDelay, skillTarget } from './api.js';

const catalog = {
  ruleset: { id: 'rules', setupBudget: 50 },
  slots: [
    { slotNo: 1, classCode: 'ROOK' }, { slotNo: 2, classCode: 'HORSE' }, { slotNo: 3, classCode: 'ELEPHANT' },
    { slotNo: 4, classCode: 'ADVISOR' }, { slotNo: 5, classCode: 'GENERAL' }, { slotNo: 6, classCode: 'ADVISOR' },
    { slotNo: 7, classCode: 'ELEPHANT' }, { slotNo: 8, classCode: 'HORSE' }, { slotNo: 9, classCode: 'ROOK' },
    { slotNo: 10, classCode: 'CANNON' }, { slotNo: 11, classCode: 'CANNON' },
    { slotNo: 12, classCode: 'SOLDIER' }, { slotNo: 13, classCode: 'SOLDIER' }, { slotNo: 14, classCode: 'SOLDIER' },
    { slotNo: 15, classCode: 'SOLDIER' }, { slotNo: 16, classCode: 'SOLDIER' }
  ],
  heroes: [
    { id: 'r1', name: 'Xe A', classCode: 'ROOK', setupPoints: 7, isOwned: true },
    { id: 'r2', name: 'Xe B', classCode: 'ROOK', setupPoints: 7, isOwned: true },
    { id: 'h1', name: 'Mã A', classCode: 'HORSE', setupPoints: 4, isOwned: true },
    { id: 'h2', name: 'Mã B', classCode: 'HORSE', setupPoints: 4, isOwned: true },
    { id: 'e1', name: 'Tượng A', classCode: 'ELEPHANT', setupPoints: 2, isOwned: true },
    { id: 'e2', name: 'Tượng B', classCode: 'ELEPHANT', setupPoints: 2, isOwned: true },
    { id: 'a1', name: 'Si A', classCode: 'ADVISOR', setupPoints: 1, isOwned: true },
    { id: 'a2', name: 'Si B', classCode: 'ADVISOR', setupPoints: 1, isOwned: true },
    { id: 'g1', name: 'Tướng', classCode: 'GENERAL', setupPoints: 0, isOwned: true },
    { id: 'c1', name: 'Pháo A', classCode: 'CANNON', setupPoints: 6, isOwned: true },
    { id: 'c2', name: 'Pháo B', classCode: 'CANNON', setupPoints: 6, isOwned: true },
    { id: 's1', name: 'Tốt', classCode: 'SOLDIER', setupPoints: 1, isOwned: true },
    { id: 'owned-not', name: 'Không sở hữu', classCode: 'ROOK', setupPoints: 7, isOwned: false }
  ],
  teamSkills: [
    { id: 'sk-thanh', name: 'Thành', implementationKey: 'thanh', factionId: null },
    { id: 'sk-rao', name: 'Rào', implementationKey: 'rao', factionId: null },
    { id: 'sk-khien', name: 'Khiên', implementationKey: 'khien', factionId: null },
    { id: 'sk-trap', name: 'Bạch Đằng Trận Pháp', implementationKey: 'bach_dang_tran_phap', factionId: 'f1' }
  ]
};

test('đội hình mặc định đủ 16 slot, đúng class, không lặp hero và trong ngân sách SP', () => {
  const preset = defaultLineup(catalog);
  assert.equal(Object.keys(preset.heroes).length, 16);
  assert.equal(preset.withinBudget, true);
  for (const slot of catalog.slots) {
    const hero = catalog.heroes.find(item => item.id === preset.heroes[slot.slotNo]);
    assert.ok(hero, `slot ${slot.slotNo} chưa có hero`);
    assert.equal(hero.classCode, slot.classCode);
    assert.equal(hero.isOwned, true);
  }
  // Mỗi slot khác class không dùng lại hero; năm Tốt dùng chung một hero.
  assert.equal(new Set(catalog.slots.filter(s => s.classCode !== 'SOLDIER').map(s => preset.heroes[s.slotNo])).size, 11);
  assert.equal(new Set(catalog.slots.filter(s => s.classCode === 'SOLDIER').map(s => preset.heroes[s.slotNo])).size, 1);
});

test('đội hình mặc định chọn ba skill đã có handler và không lấy skill phe', () => {
  const preset = defaultLineup(catalog);
  assert.deepEqual(preset.skills, ['sk-thanh', 'sk-rao', 'sk-khien']);
});

test('đội hình mặc định không lặp nhân vật lịch sử và vẫn đủ 16 slot', () => {
  const withCharacter = {
    ...catalog,
    heroes: [
      ...catalog.heroes.map(hero => hero.id === 'h1' ? { ...hero, characterId: 'c-1' } : hero),
      { id: 'h3', name: 'Mã C', classCode: 'HORSE', setupPoints: 4, isOwned: true },
      { id: 'h4', name: 'Mã D', classCode: 'HORSE', setupPoints: 4, isOwned: true, characterId: 'c-1' }
    ]
  };
  const preset = defaultLineup(withCharacter);
  assert.equal(Object.keys(preset.heroes).length, 16);
  const chosen = catalog.slots.map(slot => withCharacter.heroes.find(hero => hero.id === preset.heroes[slot.slotNo]));
  const characters = chosen.filter(hero => hero?.characterId).map(hero => hero.characterId);
  assert.equal(new Set(characters).size, characters.length);
  // Hai Mã cùng nhân vật lịch sử thì chỉ một hero được dùng, slot còn lại lấy Mã khác.
  const horses = chosen.filter(hero => hero?.classCode === 'HORSE');
  assert.equal(horses.length, 2);
  assert.notEqual(horses[0].characterId, horses[1].characterId);
});

test('đội hình mặc định báo thiếu hero khi catalog không đủ quân cho mọi slot', () => {
  const thin = { ...catalog, heroes: catalog.heroes.filter(hero => hero.classCode !== 'ROOK') };
  const preset = defaultLineup(thin);
  assert.equal(Object.keys(preset.heroes).length, 14);
  assert.equal(preset.heroes[1], undefined);
});

test('đội hình mặc định dùng chung Tốt không có nhân vật lịch sử cho cả năm ô', () => {
  // Tốt có nhân vật lịch sử rẻ hơn vẫn không được dùng chung: server chặn DUPLICATE_CHARACTER.
  const withSoldierCharacter = {
    ...catalog,
    heroes: [
      ...catalog.heroes.map(hero => hero.id === 's1' ? { ...hero, characterId: 'c-soldier' } : hero),
      { id: 's2', name: 'Tốt chung', classCode: 'SOLDIER', setupPoints: 1, isOwned: true }
    ]
  };
  const preset = defaultLineup(withSoldierCharacter);
  const soldiers = catalog.slots.filter(slot => slot.classCode === 'SOLDIER');
  assert.equal(new Set(soldiers.map(slot => preset.heroes[slot.slotNo])).size, 1);
  assert.equal(preset.heroes[soldiers[0].slotNo], 's2');
});

test('đội hình mặc định chỉ điền số ô Tốt có thể dùng khi mọi Tốt đều là nhân vật lịch sử', () => {
  const allCharacters = {
    ...catalog,
    heroes: catalog.heroes.map(hero => hero.classCode === 'SOLDIER' ? { ...hero, characterId: `c-${hero.id}` } : hero)
  };
  const preset = defaultLineup(allCharacters);
  const soldiers = catalog.slots.filter(slot => slot.classCode === 'SOLDIER');
  // Một Tốt chỉ được dùng một lần nên chỉ một ô Tốt có hero.
  assert.equal(soldiers.filter(slot => preset.heroes[slot.slotNo]).length, 1);
});

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

test('Thành and Rào targets must be empty cells on the caster side', () => {
  const barrier = [{ kind: 'rao', position: { x: 2, y: 3 } }];
  assert.equal(canPlaceStructure('red', 2, 3, null, []), true);
  assert.equal(canPlaceStructure('red', 2, 3, { side: 'black' }, []), false);
  assert.equal(canPlaceStructure('red', 2, 5, null, []), false);
  assert.equal(canPlaceStructure('black', 2, 5, null, []), true);
  assert.equal(canPlaceStructure('red', 2, 3, null, barrier), false);
});

test('No stacking: a cell with a Cọc, a Thành, a Rào or a Bẫy is closed for both sides', () => {
  for (const kind of ['thd_tuong_coc', 'than_h', 'rao']) {
    assert.equal(canPlaceStructure('red', 2, 3, null, [{ kind, position: { x: 2, y: 3 } }]), false);
    assert.equal(canPlaceStructure('black', 2, 5, null, [{ kind, position: { x: 2, y: 5 } }]), false);
  }
  assert.equal(canPlaceStructure('red', 2, 3, null, [], [{ position: { x: 2, y: 3 } }]), false);
  assert.equal(canPlaceStructure('black', 2, 5, null, [], [{ position: { x: 2, y: 5 } }]), false);
});

test('polling backs off while the backend returns 500', () => {
  assert.equal(pollRetryDelay(500), 10000);
  assert.equal(pollRetryDelay(422), 3000);
});
