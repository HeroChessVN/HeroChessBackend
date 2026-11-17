export async function api(token, path, options = {}) {
  const headers = { ...options.headers, ...(token ? { Authorization: `Bearer ${token}` } : {}) };
  // FormData (tải ảnh) phải để trình duyệt tự đặt Content-Type, không stringify.
  const isForm = typeof FormData !== 'undefined' && options.body instanceof FormData;
  const body = isForm || typeof options.body === 'string' ? options.body : options.body ? JSON.stringify(options.body) : options.body;
  if (body && !isForm) headers['Content-Type'] = 'application/json';
  const response = await fetch(`/api/v1${path}`, { ...options, headers, body });
  const data = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) {
    const error = new Error(data?.message || data?.title || `${response.status} ${response.statusText}`);
    error.status = response.status;
    error.code = data?.code;
    error.details = data?.details || data?.errors;
    throw error;
  }
  return data;
}

export function lineupPayload(catalog, name, selections, skillIds, expectedRevision) {
  return {
    name: name.trim(), rulesetId: catalog.ruleset.id,
    entries: catalog.slots.map(slot => ({ slotNo: slot.slotNo, heroId: selections[slot.slotNo], cosmeticId: null })),
    skills: skillIds.map((skillId, index) => ({ slotNo: index + 1, skillId })),
    ...(expectedRevision == null ? {} : { expectedRevision })
  };
}

// defaultLineup: đội hình mặc định hợp lệ theo luật server (LineupValidator):
// 16 slot đúng class, hero đã sở hữu, không lặp nhân vật lịch sử, năm Tốt dùng chung một hero,
// tổng SP trong ngân sách và ba command skill khác nhau.
// Ưu tiên skill không thuộc phe vì skill phe cần đủ 5 hero cùng phe.
export function defaultLineup(catalog) {
  const budget = catalog?.ruleset?.setupBudget ?? 0;
  const slots = [...(catalog?.slots || [])].sort((a, b) => a.slotNo - b.slotNo);
  const owned = (catalog?.heroes || []).filter(hero => hero.isOwned);
  const usedHeroIds = new Set();
  const usedCharacters = new Set();

  // take: hero rẻ nhất còn dùng được cho class này; null khi hết hero phù hợp.
  // Chọn rẻ nhất giúp tổng SP luôn nhỏ nhất trong ngân sách của ruleset.
  const take = classCode => {
    const hero = owned
      .filter(item => item.classCode === classCode && !usedHeroIds.has(item.id))
      .filter(item => !(item.characterId && usedCharacters.has(item.characterId)))
      .sort((a, b) => (a.setupPoints || 0) - (b.setupPoints || 0))[0];
    if (!hero) return null;
    usedHeroIds.add(hero.id);
    if (hero.characterId) usedCharacters.add(hero.characterId);
    return hero;
  };

  const heroes = {};
  let spent = 0;
  // Tốt: server cho phép lặp lại một hero có characterId rỗng cho cả năm ô Tốt (cùng model),
  // nhưng không cho lặp hero có characterId (mỗi nhân vật lịch sử một slot).
  const soldierSlots = slots.filter(item => item.classCode === 'SOLDIER');
  const soldierPool = owned.filter(item => item.classCode === 'SOLDIER');
  // Ưu tiên Tốt không có nhân vật lịch sử: dùng chung cho cả năm ô là cách duy nhất chắc chắn đủ năm ô.
  const sharedSoldier = soldierPool
    .filter(item => !item.characterId)
    .sort((a, b) => (a.setupPoints || 0) - (b.setupPoints || 0))[0];
  if (sharedSoldier) {
    for (const slot of soldierSlots) heroes[slot.slotNo] = sharedSoldier.id;
    spent += (sharedSoldier.setupPoints || 0) * soldierSlots.length;
  } else {
    // Không có Tốt chung: mỗi ô một Tốt khác nhau, và mỗi Tốt phải là nhân vật lịch sử khác nhau.
    for (const slot of soldierSlots) {
      const hero = take('SOLDIER');
      if (!hero) continue;
      heroes[slot.slotNo] = hero.id;
      spent += hero.setupPoints || 0;
    }
  }
  for (const slot of slots.filter(item => item.classCode !== 'SOLDIER')) {
    const hero = take(slot.classCode);
    if (!hero) continue;
    heroes[slot.slotNo] = hero.id;
    spent += hero.setupPoints || 0;
  }

  // Ưu tiên skill đã có handler thật và không thuộc phe (skill phe cần đủ 5 hero cùng phe),
  // và không bao giờ chọn hai skill cùng phe vì server chỉ cho tối đa một.
  const implemented = new Set(['thanh', 'rao', 'khien', 'van_coc_tran_giang', 'phan_ky_doat_the',
    'pha_tran_doat_phong', 'binh_lam_thuy_hien']);
  const teamSkills = catalog?.teamSkills || [];
  const picked = [];
  const hasFaction = skill => teamSkills.some(other => other.factionId && other.factionId === skill.factionId && picked.includes(other));
  for (const skill of teamSkills) {
    if (picked.length >= 3) break;
    if (picked.includes(skill)) continue;
    if (hasFaction(skill)) continue;
    if (!skill.factionId && implemented.has(skill.implementationKey)) picked.push(skill);
  }
  for (const skill of teamSkills) {
    if (picked.length >= 3) break;
    if (picked.includes(skill)) continue;
    if (hasFaction(skill)) continue;
    if (!skill.factionId) picked.push(skill);
  }
  // Chỉ dùng skill phe khi đội hình đã đủ hero cùng phe (server đòi tối thiểu 5, trừ Tốt).
  const factionMembers = new Map();
  for (const skill of teamSkills) {
    if (picked.length >= 3 || !skill.factionId || hasFaction(skill)) continue;
    const need = skill.eligibility?.minNonSoldierFactionHeroes ?? 0;
    if (!factionMembers.has(skill.factionId)) {
      const ids = new Set(catalog.heroes
        .filter(hero => hero.classCode !== 'SOLDIER' && hero.factionIds?.includes(skill.factionId)
          && Object.values(heroes).includes(hero.id))
        .map(hero => hero.id));
      factionMembers.set(skill.factionId, ids);
    }
    if (factionMembers.get(skill.factionId).size >= need) picked.push(skill);
  }
  return { heroes, skills: [1, 2, 3].map(slot => picked[slot - 1]?.id || ''), totalSp: spent, withinBudget: spent <= budget };
}

export function skillTarget(key, point, effectId, targetPieceId = null) {
  if (key === 'thanh' || key === 'rao') return point && { position: point };
  if (key === 'khien') return targetPieceId && { pieceId: targetPieceId };
  if (key === 'van_coc_tran_giang' || key === 'binh_lam_thuy_hien') return { paths: [4, 5, 6] };
  if (key === 'phan_ky_doat_the') return effectId && { effectId, ...(point ? { position: point } : {}) };
  if (key === 'pha_tran_doat_phong') return effectId && { effectId };
  return null;
}

// canPlaceStructure: ô trống cho Thành/Rào. Cùng luật với server (SkillPlacement):
// ô đang có quân, Cọc, Thành, Rào hoặc bẫy Bạch Đằng thì không đặt được — không phe nào đặt chồng.
export function canPlaceStructure(side, x, y, piece, obstacles, snares = []) {
  return (side === 'red' ? y <= 4 : side === 'black' && y >= 5) && !piece &&
    !obstacles.some(item => item.position?.x === x && item.position?.y === y) &&
    !snares.some(item => item.position?.x === x && item.position?.y === y);
}

export const pollRetryDelay = status => status >= 500 ? 10000 : 3000;
