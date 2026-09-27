// Vai trò file: Client demo thuần JavaScript: auth, lineup, matchmaking, bàn cờ, socket/retry, lịch sử và replay. State giao diện chỉ nằm trong bộ nhớ tab.
// $: Tìm DOM element theo ID.
const $ = id => document.getElementById(id);
// log: Hiển thị JSON mới nhất lên vùng log.
const log = value => { $('log').textContent = JSON.stringify(value, null, 2) + '\n' + $('log').textContent; };
let token, catalog, lineup, ticket, snapshot, ws, selected;
let legal = [], serverClockOffset = 0, reconnectTimer, manualClose = false;
let historyCursor = null, replayEntries = [], replayIndex = -1;
const pending = new Map();
let viewMatchId = null, replayMode = false, viewEpoch = 0, socketEpoch = 0;

// api: Gọi fetch với bearer token, đọc JSON và chuyển HTTP lỗi thành exception.
async function api(path, options = {}) {
  options.headers = { ...(options.headers || {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) };
  if (options.body && typeof options.body !== 'string') {
    options.headers['Content-Type'] = 'application/json'; options.body = JSON.stringify(options.body);
  }
  const response = await fetch('/api/v1' + path, options);
  const body = response.status === 204 ? null : await response.json().catch(() => null);
  if (!response.ok) throw Object.assign(new Error(body?.message || response.statusText), { body, status: response.status });
  return body;
}

// auth: Gọi đăng ký hoặc đăng nhập; khi nhận token thì giữ trong RAM, reset view/socket và hiển thị email.
async function auth(path) {
  const credentials = { email: $('email').value, password: $('password').value };
  const data = await api(`/auth/${path}${path === 'login' ? '?useCookies=false' : ''}`, { method: 'POST', body: credentials });
  if (data?.accessToken) { setView(''); closeSocket(); pending.clear(); token = data.accessToken; $('identity').textContent = credentials.email; }
  log({ ok: true, next: data?.accessToken ? 'Đã đăng nhập.' : 'Login để lấy access token.' });
}

// closeSocket: Hủy socket/timer cũ và tăng thế hệ để bỏ callback đến trễ.
function closeSocket() {
  socketEpoch++; clearTimeout(reconnectTimer);
  const old = ws; ws = null;
  if (old) { old.onclose = null; old.onmessage = null; old.close(); }
  $('socketStatus').textContent = 'socket: offline';
}
// setView: Đổi trận/chế độ replay, dọn state và pending command của view cũ.
function setView(matchId, replay = false) {
  if (viewMatchId === matchId && replayMode === replay) return;
  closeSocket(); viewEpoch++; viewMatchId = matchId; replayMode = replay;
  snapshot = null; legal = []; selected = null; replayEntries = []; replayIndex = -1;
  $('matchId').value = matchId;
  $('matchStatus').textContent = 'match: -'; $('settlement').textContent = 'settlement: -';
  $('turn').textContent = ''; $('replayPosition').textContent = 'Replay: -';
  buildUndoTargets(); renderBoard(); renderSkills(); tickClock();
}
// loadState: Tải snapshot server, chỉ nhận nếu vẫn ở đúng view.
async function loadState() {
  setView($('matchId').value);
  const epoch = viewEpoch, matchId = viewMatchId;
  const current = await api(`/matches/${matchId}/state`);
  if (epoch !== viewEpoch) return;
  acceptSnapshot(current); log(current);
  if (current.status === 'active') await loadLegal();
}
// loadLegal: Tải nước hợp lệ để tô ô, bỏ response đã lỗi thời.
async function loadLegal() {
  if (replayMode || !snapshot || snapshot.matchId !== $('matchId').value) return;
  const epoch = viewEpoch, version = snapshot.version;
  const moves = await api(`/matches/${viewMatchId}/legal-actions`);
  if (epoch !== viewEpoch || snapshot?.version !== version || replayMode) return;
  legal = moves; renderBoard();
}
// acceptSnapshot: Chấp nhận state đúng trận và version phù hợp; cập nhật UI/deadline.
function acceptSnapshot(next) {
  if (replayMode || !next?.state || next.matchId !== viewMatchId || next.matchId !== $('matchId').value) return;
  if (snapshot?.matchId === next.matchId && snapshot.version > next.version) return;
  if (snapshot?.version !== next.version) { legal = []; selected = null; }
  snapshot = next; serverClockOffset = Date.now() - Date.parse(next.serverNow);
  $('matchStatus').textContent = `match: ${next.mode}/${next.status} v${next.version}`;
  if (next.status === 'completed' || next.status === 'cancelled') $('settlement').textContent = next.settledAt ? 'settlement: completed' : 'settlement: pending';
  buildUndoTargets(); renderBoard(); renderSkills(); tickClock();
}
// tickClock: Tính thời gian hiển thị từ deadline/serverNow; server mới quyết định timeout.
function tickClock() {
  if (!snapshot?.deadlineAt || snapshot.status !== 'active') { $('timer').textContent = 'timer: -'; return; }
  const serverNow = Date.now() - serverClockOffset;
  const seconds = Math.max(0, Math.ceil((Date.parse(snapshot.deadlineAt) - serverNow) / 1000));
  $('timer').textContent = `timer: ${seconds}s`;
}
setInterval(tickClock, 250);

// renderBoard: Vẽ lưới 9x10, quân cờ và ô đích hợp lệ; gắn xử lý click.
function renderBoard() {
  const board = $('board'); board.innerHTML = ''; if (!snapshot) return;
  $('turn').textContent = `v${snapshot.version} · ${snapshot.sideToMove} · turn ${snapshot.turnIndex} · counted ${snapshot.countedActions}`;
  const pieces = snapshot.state.pieces || [];
  for (let y = 9; y >= 0; y--) for (let x = 0; x < 9; x++) {
    const cell = document.createElement('div'); cell.className = 'cell';
    const piece = pieces.find(p => p.position?.x === x && p.position?.y === y && p.status === 'alive');
    if (piece) {
      const node = document.createElement('div'); node.className = `piece ${piece.side}${selected === piece.pieceId ? ' selected' : ''}`;
      node.textContent = piece.class.slice(0, 2); node.title = `${piece.side} ${piece.class} · ${piece.heroId}`;
      node.onclick = () => { selected = piece.pieceId; renderBoard(); }; cell.append(node);
    }
    const move = legal.find(m => m.pieceId === selected && m.toX === x && m.toY === y);
    if (move) { cell.classList.add('legal'); cell.onclick = () => send({ type: 'move', pieceId: selected, to: { x, y } }); }
    board.append(cell);
  }
}
// renderSkills: Hiển thị skill metadata; gameplay skill vẫn chưa triển khai.
function renderSkills() {
  const states = snapshot?.state?.skillStates;
  const text = states ? Object.entries(states).map(([side, skills]) => `${side}: ${skills.map(x => `slot ${x.slotNo} ${x.skillId}`).join(', ')}`).join(' | ') : 'chưa tải';
  $('skills').textContent = `Team skills (${text}). Fixture hiện trả SKILL_NOT_IMPLEMENTED; UI không gửi lệnh skill giả.`;
}
// buildUndoTargets: Chuẩn bị các mốc undo để gửi command cho bot match.
function buildUndoTargets() {
  const select = $('undoTarget'), current = Number(select.value); select.innerHTML = '';
  for (let sequence = 0; sequence < (snapshot?.version || 0); sequence++) {
    const option = document.createElement('option'); option.value = sequence; option.textContent = `undo → sequence ${sequence}`; select.append(option);
  }
  if ([...select.options].some(x => Number(x.value) === current)) select.value = current;
  $('undo').disabled = snapshot?.mode !== 'bot' || snapshot?.status !== 'active' || select.options.length === 0;
}

// connect: Xin vé và mở WS; guard callback cũ, subscribe lại, resend pending cùng commandId, nhận ACK/broadcast.
async function connect(manual = true) {
  if (!token || !$('matchId').value) return;
  setView($('matchId').value);
  if (manual) manualClose = false;
  closeSocket();
  const generation = socketEpoch, matchId = viewMatchId;
  $('socketStatus').textContent = 'socket: connecting';
  try {
    const issued = await api('/ws-ticket', { method: 'POST' });
    if (generation !== socketEpoch || replayMode) return;
    const socket = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws/v1?ticket=${encodeURIComponent(issued.ticket)}`);
    ws = socket;
    const current = () => ws === socket && generation === socketEpoch && viewMatchId === matchId && !replayMode;
    socket.onopen = () => {
      if (!current()) return;
      $('socketStatus').textContent = 'socket: connected';
      socket.send(JSON.stringify({ type: 'match.subscribe', requestId: crypto.randomUUID(), payload: { matchId } }));
    };
    socket.onmessage = async event => {
      if (!current()) return;
      const message = JSON.parse(event.data), payload = message.payload;
      if (message.type === 'match.command_accepted') pending.delete(payload.commandId);
      if (message.type === 'match.command_rejected') {
        pending.delete(payload.commandId); log(message);
        if (payload.code === 'STALE_STATE' || payload.code === 'TURN_DEADLINE_PASSED') await loadState().catch(error => log(error.body || error.message));
        return;
      }
      const eventMatchId = payload?.matchId || payload?.snapshot?.matchId;
      if (eventMatchId && eventMatchId !== matchId) return;
      acceptSnapshot(payload?.snapshot || (message.type === 'match.state' ? payload : null)); log(message);
      if (message.type === 'match.settled') $('settlement').textContent = 'settlement: completed';
      if (message.type === 'match.ended') $('settlement').textContent = 'settlement: pending';
      if (message.type === 'match.state') {
        for (const command of pending.values()) if (command.matchId === matchId)
          socket.send(JSON.stringify({ type: 'match.command', requestId: crypto.randomUUID(), payload: command }));
      }
      if (snapshot?.status === 'active') loadLegal().catch(error => log(error.body || error.message));
    };
    socket.onclose = event => {
      if (!current()) return;
      $('socketStatus').textContent = `socket: disconnected (${event.code})`;
      if (!manualClose && token) reconnectTimer = setTimeout(() => connect(false), 1000);
    };
  } catch (error) {
    if (generation !== socketEpoch) return;
    $('socketStatus').textContent = 'socket: reconnect waiting'; log(error.body || error.message);
    if (!manualClose) reconnectTimer = setTimeout(() => connect(false), 1500);
  }
}
// send: Gửi action với commandId/version; khi chưa có ACK giữ nguyên ID để retry, có REST fallback.
function send(action) {
  if (replayMode || !snapshot || snapshot.matchId !== $('matchId').value || snapshot.status !== 'active')
    return log({ code: 'STATE_REQUIRED', message: 'Load/subscribe an active match first.' });
  if ([...pending.values()].some(x => x.matchId === snapshot.matchId)) {
    log({ code: 'COMMAND_PENDING', message: 'Đang xác nhận lệnh trước. Kết nối lại để nhận kết quả.' });
    return connect(false);
  }
  const payload = { matchId: snapshot.matchId, commandId: crypto.randomUUID(), expectedVersion: snapshot.version, action };
  pending.set(payload.commandId, payload);
  if (ws?.readyState === WebSocket.OPEN) ws.send(JSON.stringify({ type: 'match.command', requestId: crypto.randomUUID(), payload }));
  else api(`/matches/${payload.matchId}/commands`, { method: 'POST', body: payload }).then(result => {
    pending.delete(payload.commandId); acceptSnapshot(result.snapshot); log(result); return loadLegal();
  }).catch(async error => {
    // A network/5xx failure does not prove that the command was rolled back.
    if (error.status >= 400 && error.status < 500) pending.delete(payload.commandId);
    log(error.body || error.message);
    if (payload.matchId !== viewMatchId || replayMode) return;
    if (pending.has(payload.commandId)) await connect(false);
    else if (error.body?.code === 'STALE_STATE' || error.body?.code === 'TURN_DEADLINE_PASSED') await loadState().catch(inner => log(inner.body || inner.message));
  });
}
// loadHistory: Tải/thêm trang lịch sử bằng cursor.
async function loadHistory(reset) {
  if (reset) { historyCursor = null; $('historyList').innerHTML = ''; }
  const page = await api(`/matches?limit=10${historyCursor ? `&cursor=${encodeURIComponent(historyCursor)}` : ''}`);
  for (const item of page.items) {
    const button = document.createElement('button'); button.className = 'historyItem';
    button.textContent = `${item.mode} · ${item.status} · ${item.result || '-'} · ${item.matchId}`;
    button.onclick = async () => { $('matchId').value = item.matchId; await loadReplay(item.matchId); }; $('historyList').append(button);
  }
  historyCursor = page.nextCursor; $('historyMore').disabled = !historyCursor; log(page);
}
// loadReplay: Tải các trang replay, chuyển view sang lịch sử.
async function loadReplay(matchId) {
  setView(matchId, true);
  const epoch = viewEpoch, entries = []; let after = -1;
  do {
    const page = await api(`/matches/${matchId}/replay?afterSequence=${after}&limit=100`);
    if (epoch !== viewEpoch) return;
    entries.push(...page.entries); after = page.nextAfterSequence ?? -1;
  } while (after >= 0);
  replayEntries = entries; replayIndex = entries.length ? 0 : -1; showReplay();
}
// showReplay: Vẽ snapshot của bước replay đang chọn.
function showReplay() {
  if (replayIndex < 0 || !replayEntries.length) { $('replayPosition').textContent = 'Replay: -'; return; }
  const entry = replayEntries[replayIndex];
  snapshot = { matchId: $('matchId').value, mode: 'replay', status: 'terminal', version: entry.sequenceNo,
    sideToMove: entry.stateAfter.sideToMove, turnIndex: entry.stateAfter.turnIndex, countedActions: entry.stateAfter.countedActions,
    deadlineAt: null, serverNow: new Date().toISOString(), state: entry.stateAfter };
  legal = []; selected = null; renderBoard(); $('replayPosition').textContent = `Replay ${replayIndex + 1}/${replayEntries.length} · ${entry.kind} · seq ${entry.sequenceNo}`; log(entry);
}

// Các callback UI phía dưới nối nút với API/helper; luật và quyền vẫn do server kiểm tra.
$('register').onclick = () => auth('register').catch(error => log(error.body || error.message));
$('login').onclick = () => auth('login').catch(error => log(error.body || error.message));
$('catalog').onclick = async () => { try { catalog = await api('/catalog'); log(catalog); } catch (error) { log(error.body || error.message); } };
// Tạo lineup fixture cho demo từ hero đã sở hữu; không phải màn hình build đội hình sản phẩm.
$('lineup').onclick = async () => { try {
  if (!catalog) catalog = await api('/catalog'); const byClass = {};
  catalog.heroes.filter(x => x.isOwned).sort((a, b) => Number(!a.code.startsWith('dev-slot-')) - Number(!b.code.startsWith('dev-slot-')) || a.code.localeCompare(b.code)).forEach(hero => (byClass[hero.classCode] ??= []).push(hero));
  const entries = [...catalog.slots].sort((a, b) => a.slotNo - b.slotNo).map(slot => ({ slotNo: slot.slotNo, heroId: byClass[slot.classCode].shift().id, cosmeticId: null }));
  lineup = await api('/lineups', { method: 'POST', body: { name: 'Web fixture', rulesetId: catalog.ruleset.id, entries, skills: catalog.teamSkills.slice(0, 3).map((x, index) => ({ slotNo: index + 1, skillId: x.id })) } }); log(lineup);
} catch (error) { log(error.body || error.message); } };
// Nhóm queue/poll/select/confirm điều khiển giai đoạn trước khi bàn cờ active.
$('queue').onclick = async () => { try { ticket = await api('/matchmaking/tickets', { method: 'POST', body: { mode: $('mode').value } }); if (ticket.matchId) setView(ticket.matchId); log(ticket); } catch (error) { log(error.body || error.message); } };
$('poll').onclick = async () => { try { ticket = await api(`/matchmaking/tickets/${ticket.ticketId}`); if (ticket.matchId) setView(ticket.matchId); log(ticket); } catch (error) { log(error.body || error.message); } };
$('select').onclick = async () => { try { log(await api(`/matches/${$('matchId').value}/selection`, { method: 'PUT', body: { lineupId: lineup.id, expectedRevision: lineup.revision } })); } catch (error) { log(error.body || error.message); } };
$('confirm').onclick = async () => { try { log(await api(`/matches/${$('matchId').value}/confirm`, { method: 'POST' })); await loadState(); } catch (error) { log(error.body || error.message); } };
$('state').onclick = () => loadState().catch(error => log(error.body || error.message));
$('legal').onclick = () => loadLegal().then(() => log(legal)).catch(error => log(error.body || error.message));
$('connect').onclick = () => connect(true); $('resign').onclick = () => send({ type: 'resign' });
$('undo').onclick = () => send({ type: 'undo', targetSequence: Number($('undoTarget').value) });
$('history').onclick = () => loadHistory(true).catch(error => log(error.body || error.message));
$('historyMore').onclick = () => loadHistory(false).catch(error => log(error.body || error.message));
// Nhóm điều hướng replay chỉ đổi snapshot hiển thị, không gửi nước đi vào trận.
$('replayFirst').onclick = () => { replayIndex = 0; showReplay(); }; $('replayLast').onclick = () => { replayIndex = replayEntries.length - 1; showReplay(); };
$('replayPrev').onclick = () => { replayIndex = Math.max(0, replayIndex - 1); showReplay(); }; $('replayNext').onclick = () => { replayIndex = Math.min(replayEntries.length - 1, replayIndex + 1); showReplay(); };
// Đóng socket khi rời trang; token/state UI hiện chỉ ở RAM, reload sẽ mất phiên phía client.
window.addEventListener('beforeunload', () => { manualClose = true; ws?.close(); });

$('cancelSelection').onclick = async () => { try { await api(`/matches/${$('matchId').value}/selection`, { method: 'DELETE' }); log({ status: 'cancelled' }); } catch (error) { log(error.body || error.message); } };
$('matchId').onchange = () => setView($('matchId').value);

