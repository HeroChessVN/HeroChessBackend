// Smoke test: chạy API thật, xác minh ranking / lịch sử / chi tiết / lineup / replay trả đúng.
const base = process.env.SMOKE_BASE || 'http://127.0.0.1:5012/api/v1';

async function call(path, { token, method = 'GET', body } = {}) {
  const headers = token ? { Authorization: `Bearer ${token}` } : {};
  if (body) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${base}${path}`, { method, headers, body: body ? JSON.stringify(body) : undefined });
  const text = await response.text();
  const data = text ? JSON.parse(text) : null;
  return { status: response.status, data };
}

function check(label, condition, extra = '') {
  console.log(`${condition ? 'PASS' : 'FAIL'}  ${label}${extra ? ` :: ${extra}` : ''}`);
  if (!condition) process.exitCode = 1;
}

async function newPlayer() {
  const email = `smoke-${crypto.randomUUID().replace(/-/g, '')}@hero.local`;
  const password = `A1!${crypto.randomUUID().replace(/-/g, '')}`;
  await call('/auth/register', { method: 'POST', body: { email, password } });
  const login = await call('/auth/login?useCookies=false', { method: 'POST', body: { email, password } });
  const token = login.data.accessToken;
  const me = await call('/me', { token });
  return { token, playerId: me.data.playerId, displayName: me.data.displayName, email, password };
}

async function makeLineup(player) {
  const catalog = await call('/catalog', { token: player.token });
  // Mỗi slot cần một hero khác nhau: cùng nhân vật lịch sử không được lặp trong một đội hình.
  const byClass = new Map();
  for (const hero of catalog.data.heroes.filter(x => x.isOwned)) {
    if (!byClass.has(hero.classCode)) byClass.set(hero.classCode, []);
    byClass.get(hero.classCode).push(hero);
  }
  const entries = catalog.data.slots.slice().sort((a, b) => a.slotNo - b.slotNo).map(slot => {
    const pool = byClass.get(slot.classCode) || [];
    const hero = pool.shift();
    return { slotNo: slot.slotNo, heroId: hero?.id, cosmeticId: null };
  });
  const skills = catalog.data.teamSkills.slice(0, 3).map((skill, index) => ({ slotNo: index + 1, skillId: skill.id }));
  const created = await call('/lineups', { token: player.token, method: 'POST', body: { name: 'Smoke', rulesetId: catalog.data.ruleset.id, entries, skills } });
  if (created.status !== 200 && created.status !== 201) throw new Error(`POST /lineups failed: ${created.status} ${JSON.stringify(created.data)}`);
  return created.data;
}

async function main() {
  const a = await newPlayer();
  const b = await newPlayer();
  check('register + login hai player', Boolean(a.playerId && b.playerId));

  const ranking = await call('/ranking?limit=100', { token: a.token });
  check('GET /ranking trả 200', ranking.status === 200, `status=${ranking.status}`);
  check('/ranking có items và total', Array.isArray(ranking.data.items) && ranking.data.total >= 2, `total=${ranking.data.total}`);
  const ratings = ranking.data.items.map(x => x.rating);
  check('/ranking sắp theo Elo giảm dần', JSON.stringify(ratings) === JSON.stringify(ratings.slice().sort((x, y) => y - x)));
  const ranks = ranking.data.items.map(x => x.rank);
  check('/ranking có hạng tăng dần từ 1', JSON.stringify(ranks) === JSON.stringify(ranks.slice().sort((x, y) => x - y)));
  check('/ranking không lộ email', !JSON.stringify(ranking.data).includes('@'));

  const myRank = await call('/ranking/me', { token: a.token });
  check('GET /ranking/me trả hạng của chính mình', myRank.status === 200 && myRank.data.playerId === a.playerId, `rank=${myRank.data?.rank}`);

  const anonymous = await call('/ranking');
  check('/ranking chặn khách chưa đăng nhập', anonymous.status === 401, `status=${anonymous.status}`);

  // Ghép trận ranked giữa hai player.
  const lineupA = await makeLineup(a);
  const lineupB = await makeLineup(b);
  check('tạo đội hình hợp lệ', lineupA.isValid && lineupB.isValid, `A=${lineupA.isValid} B=${lineupB.isValid}`);

  const ticketA = await call('/matchmaking/tickets', { token: a.token, method: 'POST', body: { mode: 'ranked' } });
  const ticketB = await call('/matchmaking/tickets', { token: b.token, method: 'POST', body: { mode: 'ranked' } });
  const matchId = ticketB.data.matchId;
  check('ghép được trận ranked', Boolean(matchId), `matchId=${matchId}`);
  // Ticket của người vào trước được trả về lúc còn "waiting", nên phải hỏi lại để lấy matchId.
  const ticketAStatus = await call(`/matchmaking/tickets/${ticketA.data.ticketId}`, { token: a.token });
  check('cả hai cùng vào một trận', ticketAStatus.data.matchId === matchId, `A=${ticketAStatus.data.matchId} B=${matchId}`);

  const selA = await call(`/matches/${matchId}/selection`, { token: a.token, method: 'PUT', body: { lineupId: lineupA.id, expectedRevision: lineupA.revision } });
  const selB = await call(`/matches/${matchId}/selection`, { token: b.token, method: 'PUT', body: { lineupId: lineupB.id, expectedRevision: lineupB.revision } });
  check('chọn đội hình thành công', selA.status === 200 && selB.status === 200, `A=${selA.status} B=${selB.status} ${JSON.stringify(selA.data?.code ?? selB.data?.code)}`);
  const confA = await call(`/matches/${matchId}/confirm`, { token: a.token, method: 'POST' });
  const confB = await call(`/matches/${matchId}/confirm`, { token: b.token, method: 'POST' });
  check('confirm hai bên thành công', confA.status === 200 && confB.status === 200, `A=${confA.status} B=${confB.status} ${JSON.stringify(confA.data?.code ?? confB.data?.code)}`);

  const outsider = await newPlayer();
  const foreignDetail = await call(`/matches/${matchId}`, { token: outsider.token });
  check('người ngoài không xem được chi tiết trận', foreignDetail.status === 404, `status=${foreignDetail.status}`);
  const foreignLineup = await call(`/matches/${matchId}/lineup`, { token: outsider.token });
  check('người ngoài không xem được lineup', foreignLineup.status === 404, `status=${foreignLineup.status}`);

  const lineup = await call(`/matches/${matchId}/lineup`, { token: a.token });
  check('GET /lineup trả lineup đã đóng băng', lineup.status === 200 && lineup.data.sides.length === 2, `sides=${lineup.data?.sides?.length}`);
  const mySide = lineup.data.sides.find(side => side.side === 'red');
  check('lineup của tôi khớp đội hình đã chọn', mySide.entries.length === lineupA.entries.length);
  check('lineup giữ đúng hero đã chọn',
    JSON.stringify(mySide.entries.map(x => x.heroId).sort()) === JSON.stringify(lineupA.entries.map(x => x.heroId).sort()));
  // GENERAL có base_sp = 0 nên SP hợp lệ có thể bằng 0; chỉ cần class có mặt và SP không âm.
  check('lineup có class và SP', mySide.entries.every(x => x.classCode && x.setupPoints >= 0),
    JSON.stringify(mySide.entries.map(x => `${x.slotNo}:${x.classCode}:${x.setupPoints}`)));

  const detail = await call(`/matches/${matchId}`, { token: a.token });
  check('GET /matches/{id} trả chi tiết', detail.status === 200 && detail.data.sides.length === 2, `status=${detail.status}`);
  check('chi tiết có Elo trước/sau', detail.data.sides.every(side => side.eloBefore !== undefined));

  const replay = await call(`/matches/${matchId}/replay?afterSequence=-1&limit=50`, { token: a.token });
  check('GET /replay trả bước 0', replay.status === 200 && replay.data.entries.some(x => x.sequenceNo === 0), `entries=${replay.data?.entries?.length}`);

  // Đầu hàng để có kết quả và Elo đã settle.
  const state = await call(`/matches/${matchId}/state`, { token: b.token });
  await call(`/matches/${matchId}/commands`, {
    token: b.token, method: 'POST',
    body: { commandId: crypto.randomUUID(), expectedVersion: state.data.version, action: { type: 'resign' } }
  });
  await new Promise(resolve => setTimeout(resolve, 2500));

  const history = await call('/matches?limit=20', { token: a.token });
  const item = history.data.items.find(x => x.matchId === matchId);
  check('GET /matches trả trận của tôi', Boolean(item), `items=${history.data.items.length}`);
  check('lịch sử có outcome và đối thủ', item?.outcome === 'win' && item?.opponentDisplayName === b.displayName, `outcome=${item?.outcome} opponent=${item?.opponentDisplayName}`);
  check('lịch sử có delta Elo', typeof item?.eloDelta === 'number', `delta=${item?.eloDelta}`);
  check('lịch sử có thời lượng', typeof item?.durationSeconds === 'number', `seconds=${item?.durationSeconds}`);
  check('lịch sử báo có replay', item?.replayAvailable === true);

  const wins = await call('/matches?outcome=win&limit=20', { token: a.token });
  check('lọc outcome=win chỉ trả trận thắng', wins.data.items.every(x => x.outcome === 'win'));
  const botFilter = await call('/matches?mode=bot&limit=20', { token: a.token });
  check('lọc mode=bot chỉ trả trận bot', botFilter.data.items.every(x => x.mode === 'bot'));

  const after = await call('/ranking/me', { token: a.token });
  check('Elo người thắng tăng', after.data.rating > myRank.data.rating, `${myRank.data.rating} -> ${after.data.rating}`);
  check('hạng người thắng không tệ hơn trước', after.data.rank <= myRank.data.rank, `${myRank.data.rank} -> ${after.data.rank}`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
