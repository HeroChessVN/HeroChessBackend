import React, { useCallback, useEffect, useRef, useState } from 'react';
import { api, skillTarget } from './api.js';
import { actionLabel } from './history.js';

const classes = { general: 'TƯỚNG', advisor: 'SĨ', elephant: 'TƯỢNG', rook: 'XE', cannon: 'PHÁO', horse: 'MÃ', soldier: 'TỐT' };
const pointSkills = new Set(['thanh', 'rao']);
const effectSkills = new Set(['phan_ky_doat_the', 'pha_tran_doat_phong']);
const knownSkills = new Set([...pointSkills, ...effectSkills, 'khien', 'van_coc_tran_giang', 'binh_lam_thuy_hien']);

export default function MatchView({ token, matchId, catalog, lineups, onBack, onCancel, onError }) {
  const [selection, setSelection] = useState(null);
  const [snapshot, setSnapshot] = useState(null);
  const [legal, setLegal] = useState([]);
  const [selectedPiece, setSelectedPiece] = useState(null);
  const [heroTargetMode, setHeroTargetMode] = useState(null);
  const [heroMoves, setHeroMoves] = useState([]);
  const [targetPieceId, setTargetPieceId] = useState(null);
  const [selectedLineupId, setSelectedLineupId] = useState(lineups[0]?.id || '');
  const [skillSlot, setSkillSlot] = useState(null);
  const [targetPoint, setTargetPoint] = useState(null);
  const [effectId, setEffectId] = useState('');
  const [pending, setPending] = useState(null);
  const [socketStatus, setSocketStatus] = useState('đang kết nối');
  const [timeLeft, setTimeLeft] = useState(null);
  const [undoTarget, setUndoTarget] = useState(0);
  const [events, setEvents] = useState([]);
  const [history, setHistory] = useState([]);
  const [historyError, setHistoryError] = useState(false);
  const [historyRetry, setHistoryRetry] = useState(0);
  const [replay, setReplay] = useState(null);
  const [replayIndex, setReplayIndex] = useState(0);
  const [flipped, setFlipped] = useState(false);
  const [busy, setBusy] = useState(false);
  const clockOffset = useRef(0);

  const accept = useCallback(next => {
    if (!next?.state || next.matchId !== matchId) return;
    setSnapshot(previous => !previous || next.version >= previous.version ? next : previous);
    clockOffset.current = Date.now() - Date.parse(next.serverNow);
  }, [matchId]);

  const refresh = useCallback(async () => {
    const stage = await api(token, `/matches/${matchId}/selection`);
    setSelection(stage);
    if (stage.status === 'active' || stage.status === 'completed') {
      const state = await api(token, `/matches/${matchId}/state`);
      accept(state);
    }
  }, [token, matchId, accept]);

  useEffect(() => {
    refresh().catch(onError);
    const interval = setInterval(() => refresh().catch(onError), 3000);
    return () => clearInterval(interval);
  }, [refresh]);

  useEffect(() => {
    if (snapshot?.status !== 'active') { setSocketStatus('chờ bắt đầu trận'); return; }
    let stopped = false; let socket; let retry;
    async function connect() {
      try {
        const issued = await api(token, '/ws-ticket', { method: 'POST' });
        if (stopped) return;
        socket = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws/v1?ticket=${encodeURIComponent(issued.ticket)}`);
        socket.onopen = () => {
          setSocketStatus('trực tuyến');
          socket.send(JSON.stringify({ type: 'match.subscribe', requestId: crypto.randomUUID(), payload: { matchId } }));
        };
        socket.onmessage = event => {
          const message = JSON.parse(event.data);
          if (message.type === 'match.state') accept(message.payload);
          if (message.type === 'match.changed') refresh().catch(onError);
          if (message.payload?.snapshot) accept(message.payload.snapshot);
          if (message.type === 'match.command_accepted') setPending(previous => previous?.commandId === message.payload?.commandId ? null : previous);
          if (message.type === 'match.command_rejected') onError(new Error(`${message.payload?.code}: ${message.payload?.message}`));
          if (message.payload?.resolvedEvents) setEvents(previous => [...message.payload.resolvedEvents, ...previous].slice(0, 12));
          if (message.type === 'match.ended' || message.type === 'match.settled') refresh().catch(onError);
        };
        socket.onclose = () => { if (!stopped) { setSocketStatus('mất kết nối · đang thử lại'); retry = setTimeout(connect, 1500); } };
        socket.onerror = () => setSocketStatus('mất kết nối');
      } catch (error) {
        if (!stopped) { setSocketStatus('REST polling'); retry = setTimeout(connect, 2000); }
      }
    }
    connect();
    return () => { stopped = true; clearTimeout(retry); socket?.close(); };
  }, [token, matchId, accept, refresh, snapshot?.status]);

  useEffect(() => {
    setSelectedPiece(null); setSkillSlot(null); setTargetPoint(null); setHeroTargetMode(null); setTargetPieceId(null);
    if (!snapshot || snapshot.status !== 'active' || replay) { setLegal([]); return; }
    let live = true;
    api(token, `/matches/${matchId}/legal-actions`).then(value => { if (live) setLegal(value); }).catch(onError);
    return () => { live = false; };
  }, [token, matchId, snapshot?.version, snapshot?.status, replay]);

  useEffect(() => {
    const timer = setInterval(() => {
      if (!snapshot?.deadlineAt || snapshot.status !== 'active') { setTimeLeft(null); return; }
      setTimeLeft(Math.max(0, Math.ceil((Date.parse(snapshot.deadlineAt) - (Date.now() - clockOffset.current)) / 1000)));
    }, 250);
    return () => clearInterval(timer);
  }, [snapshot?.deadlineAt, snapshot?.status]);

  async function selectLineup() {
    const lineup = lineups.find(item => item.id === selectedLineupId);
    if (!lineup) return onError(new Error('Chưa chọn đội hình.'));
    setBusy(true);
    try {
      setSelection(await api(token, `/matches/${matchId}/selection`, { method: 'PUT', body: { lineupId: lineup.id, expectedRevision: lineup.revision } }));
    } catch (error) { onError(error); } finally { setBusy(false); }
  }
  async function confirm() {
    setBusy(true);
    try { setSelection(await api(token, `/matches/${matchId}/confirm`, { method: 'POST' })); await refresh(); }
    catch (error) { onError(error); } finally { setBusy(false); }
  }
  async function cancel() {
    setBusy(true);
    try { await api(token, `/matches/${matchId}/selection`, { method: 'DELETE' }); onCancel(); }
    catch (error) { onError(error); } finally { setBusy(false); }
  }

  async function submit(payload) {
    setBusy(true);
    try {
      const result = await api(token, `/matches/${matchId}/commands`, { method: 'POST', body: payload });
      setPending(null); accept(result.snapshot);
      setEvents(previous => [...(result.resolvedEvents || []), ...previous].slice(0, 12));
    } catch (error) {
      onError(error);
      if (error.status >= 400 && error.status < 500) setPending(null);
      await refresh().catch(onError);
    } finally { setBusy(false); }
  }
  function command(action) {
    if (pending || !snapshot || snapshot.status !== 'active') return;
    const payload = { commandId: crypto.randomUUID(), expectedVersion: snapshot.version, action };
    setPending(payload); submit(payload);
  }

  useEffect(() => {
    if (snapshot?.status !== 'active' && snapshot?.status !== 'completed') return;
    let current = true;
    setHistoryError(false);
    async function loadHistory() {
      // shortcut: Tải lại tối đa 151 bước khi version đổi; dùng cursor tăng dần nếu giới hạn action tăng.
      const all = []; let after = -1;
      do {
        const page = await api(token, `/matches/${matchId}/replay?afterSequence=${after}&limit=100`);
        all.push(...page.entries); after = page.nextAfterSequence ?? -1;
      } while (after >= 0);
      if (snapshot.status === 'completed' && !all.length) throw new Error('Replay không có bàn cờ ban đầu.');
      if (current) {
        setHistory(all);
        if (snapshot.status === 'completed' && all.length) { setReplay(all); setReplayIndex(0); }
      }
    }
    loadHistory().catch(error => { if (current) { setHistoryError(true); onError(error); } });
    return () => { current = false; };
  }, [token, matchId, snapshot?.version, snapshot?.status, historyRetry]);

  const ownSide = selection?.sides?.find(side => side.ownLineupId)?.side;
  const ourTurn = ownSide && snapshot?.sideToMove === ownSide;
  useEffect(() => {
    if (heroTargetMode !== 'quangtrung' || !selectedPiece || !ourTurn) { setHeroMoves([]); return; }
    let live = true;
    api(token, `/matches/${matchId}/hero-actions?pieceId=${selectedPiece}`).then(value => { if (live) setHeroMoves(value); }).catch(onError);
    return () => { live = false; };
  }, [token, matchId, selectedPiece, heroTargetMode, snapshot?.version, ourTurn]);
  const view = replay ? replay[replayIndex]?.stateAfter : snapshot?.state;
  const status = replay ? 'replay' : snapshot?.status || selection?.status || 'đang tải';
  const replayStep = replay?.[replayIndex];
  const replayEnd = replay?.at(-1)?.sequenceNo ?? 0;
  const pieces = (view?.pieces || []).filter(piece => piece.status === 'alive' && piece.position);
  const obstacles = view?.obstacles || [];
  const effects = (view?.effectInstances || []).filter(effect => effect.state !== 'ended');
  const sideSkills = ownSide ? view?.skillStates?.[ownSide] || [] : [];
  const enemySide = ownSide === 'red' ? 'black' : 'red';
  const enemySkills = view?.skillStates?.[enemySide] || [];
  const inspectedPiece = pieces.find(piece => piece.pieceId === selectedPiece);
  const inspectedHero = catalog?.heroes?.find(hero => hero.id === inspectedPiece?.heroId);
  const heroCooldown = inspectedPiece?.traitState?.heroCooldownRemaining || 0;
  const quangTrungCooldown = inspectedPiece?.traitState?.quangTrungCooldownRemaining || 0;
  const isQuangTrung = ['quang_trung.special_move', 'general.orthogonal_range_3'].includes(inspectedPiece?.traitImplementationKey);
  const activeState = sideSkills.find(skill => skill.slotNo === skillSlot);
  const activeCatalog = catalog?.teamSkills?.find(skill => skill.id === activeState?.skillId);
  const activeInfo = activeState && { ...activeCatalog, ...activeState, name: activeState.name || activeCatalog?.name };
  const target = activeInfo && skillTarget(activeInfo.implementationKey, targetPoint, effectId, targetPieceId);
  const ys = flipped ? Array.from({ length: 10 }, (_, y) => y) : Array.from({ length: 10 }, (_, y) => 9 - y);
  const xs = flipped ? Array.from({ length: 9 }, (_, x) => 8 - x) : Array.from({ length: 9 }, (_, x) => x);
  const moves = legal.filter(move => move.pieceId === selectedPiece);
  const displayMoves = heroTargetMode === 'quangtrung' ? heroMoves : moves;

  function clickCell(x, y, piece) {
    if (replay || snapshot?.status !== 'active') return;
    if (heroTargetMode === 'stake') { if ((y === 4 || y === 5) && !piece && !obstacles.some(item => item.position?.x === x && item.position?.y === y)) setTargetPoint({ x, y }); return; }
    if (heroTargetMode === 'quangtrung') { if (heroMoves.some(move => move.toX === x && move.toY === y)) setTargetPoint({ x, y }); return; }
    if (activeInfo?.implementationKey === 'khien') { setTargetPieceId(piece?.side === ownSide ? piece.pieceId : null); return; }
    if (activeInfo && pointSkills.has(activeInfo.implementationKey)) { setTargetPoint({ x, y }); return; }
    const move = moves.find(item => item.toX === x && item.toY === y);
    if (move && ourTurn) { command({ type: 'move', pieceId: selectedPiece, to: { x, y } }); return; }
    setSelectedPiece(piece?.pieceId || null);
  }

  return <div className="match-page"><div className="section-head"><div><span className="eyebrow">{snapshot?.mode === 'bot' ? 'CHƠI VỚI BOT' : 'TRẬN ĐẤU'}</span><h1>{replay || snapshot?.status === 'completed' ? 'Xem lại trận' : 'Bàn cờ'}</h1><p className="match-id">{matchId}</p></div>
    <div className="match-controls"><button className="secondary" onClick={() => onBack(snapshot?.status === 'completed' || selection?.status === 'cancelled')}>← Sảnh</button><button className="secondary" onClick={() => refresh().catch(onError)}>Đồng bộ</button><button className="secondary" onClick={() => setFlipped(!flipped)}>Xoay bàn</button></div></div>
    <div className="match-meta"><span className="pill">{replay ? 'replay' : status}</span>{replay ? <><span>Bước {replayStep?.sequenceNo ?? 0}/{replayEnd}</span><span>{actionLabel(replayStep, replay[replayIndex - 1], catalog)}</span></> : <><span>Lượt: <strong>{snapshot?.sideToMove || '—'}</strong>{ownSide ? ` · bạn: ${ownSide}` : ''}</span><span>#{snapshot?.countedActions ?? 0}/{catalog?.ruleset?.actionLimit ?? 150}</span><span className="timer">{timeLeft == null ? '—' : `${timeLeft}s`}</span><span>Socket: {socketStatus}</span></>}</div>
    {status === 'selecting' && <section className="card selection"><h2>Chọn đội hình</h2><p>Hai bên có 120 giây để chọn và xác nhận.</p>
      <div className="actions"><select value={selectedLineupId} onChange={event => setSelectedLineupId(event.target.value)}>{lineups.map(lineup => <option key={lineup.id} value={lineup.id}>{lineup.name} · {lineup.totalSp} SP</option>)}</select>
        <button disabled={busy || !selectedLineupId} onClick={selectLineup}>Chọn</button><button disabled={busy || !selection?.sides?.some(side => side.ownLineupId)} onClick={confirm}>Xác nhận</button><button className="danger" disabled={busy} onClick={cancel}>Hủy tham gia</button></div>
      <div className="side-status">{selection?.sides?.map(side => <span key={side.side}>{side.side}: {side.confirmed ? 'đã xác nhận' : 'đang chọn'} · {side.publicSkillIds?.length || 0} skill</span>)}</div></section>}
    {snapshot?.status === 'completed' && !replay ? <div className="card"><p className="hint">{historyError ? 'Không tải được replay.' : 'Đang tải replay từ bước 0…'}</p>{historyError && <button onClick={() => setHistoryRetry(value => value + 1)}>Thử lại</button>}</div> : <div className="battle-layout"><section className="board-wrap"><div className="board" role="grid" aria-label="Bàn cờ Hero Chess">{ys.flatMap(y => xs.map(x => {
      const piece = pieces.find(item => item.position.x === x && item.position.y === y);
      const obstacle = obstacles.find(item => item.position?.x === x && item.position?.y === y);
      const legalMove = displayMoves.some(move => move.toX === x && move.toY === y);
      const inEffect = effects.some(effect => effect.targetPositions?.some(position => position.x === x && position.y === y));
      return <button type="button" role="gridcell" key={`${x}-${y}`} className={`cell ${legalMove ? 'legal' : ''} ${inEffect ? 'effect-cell' : ''} ${heroTargetMode === 'stake' && (y === 4 || y === 5) && !piece && !obstacle ? 'hero-target' : ''} ${targetPoint?.x === x && targetPoint?.y === y ? 'target-cell' : ''}`} title={`(${x},${y})${piece ? ` · ${piece.side} ${piece.class}` : ''}${obstacle ? ` · ${obstacle.kind}` : ''}`} onClick={() => clickCell(x, y, piece)}>
        <span className="coord">{x},{y}</span>{obstacle && <span className="obstacle" title={obstacle.kind}>▣</span>}{piece && <span className={`piece ${piece.side} ${selectedPiece === piece.pieceId ? 'selected' : ''}`}><span>{classes[piece.class] || piece.class}{piece.effects?.some(effect => effect.code === 'shield') ? ' 🛡' : ''}</span><small>{piece.heroName || catalog?.heroes?.find(hero => hero.id === piece.heroId)?.name || ''}</small></span>}
        {legalMove && <span className="move-dot" />}</button>;
    }))}</div><p className="board-note">Chọn quân của bạn rồi chọn ô sáng. Viền vàng: ô có hiệu ứng; ▣: vật cản. Nước hợp lệ do API tính.</p>
      {inspectedPiece && <div className="card hero-card"><h2>{inspectedPiece.heroName || inspectedHero?.name || classes[inspectedPiece.class]}</h2><p>{inspectedPiece.side} · {classes[inspectedPiece.class]} · {inspectedPiece.traitDescription || inspectedHero?.trait?.description || 'Nước đi cơ bản'}</p>
        {inspectedPiece.effects?.some(effect => effect.code === 'shield') && <p>🛡 Khiên: còn {inspectedPiece.effects.find(effect => effect.code === 'shield')?.remainingTurns} lượt</p>}
        {inspectedPiece.traitImplementationKey === 'tran_hung_dao_tuong.coc' && <><p>{inspectedPiece.traitName || inspectedHero?.trait?.name} · CD {heroCooldown}</p><button disabled={!ourTurn || inspectedPiece.side !== ownSide || heroCooldown > 0 || !!pending || busy} onClick={() => { setHeroTargetMode('stake'); setSkillSlot(null); setTargetPoint(null); }}>Dùng {inspectedPiece.traitName || inspectedHero?.trait?.name}</button></>}
        {isQuangTrung && <><p>Di chuyển đặc biệt 1–3 ô · CD {quangTrungCooldown}</p><button disabled={!ourTurn || inspectedPiece.side !== ownSide || quangTrungCooldown > 0 || inspectedPiece.traitState?.cooldownReady !== 1 || !!pending || busy} onClick={() => { setHeroTargetMode('quangtrung'); setSkillSlot(null); setTargetPoint(null); }}>Dùng di chuyển đặc biệt</button></>}
        {inspectedPiece.traitImplementationKey === 'rook.hoanh_soc' && <p>Hoành Sóc Giang Sơn: {inspectedPiece.traitState?.hoanhSocCharged === 1 ? 'sẵn sàng xuyên 1 quân đồng minh ở nước đi kế tiếp' : 'chưa tích điện'}</p>}
        {heroTargetMode && <div className="target-panel"><p>{heroTargetMode === 'stake' ? 'Chọn ô trống ở hàng sông y=4 hoặc y=5.' : 'Chọn ô sáng cho nước đi kỹ năng.'}</p><button disabled={!targetPoint || !!pending || busy} onClick={() => command({ type: 'hero_active', pieceId: inspectedPiece.pieceId, skillCode: heroTargetMode === 'stake' ? inspectedPiece.traitImplementationKey : 'quang_trung.special_move', target: heroTargetMode === 'stake' ? { position: targetPoint } : { to: targetPoint } })}>Xác nhận {targetPoint && `(${targetPoint.x},${targetPoint.y})`}</button><button className="secondary" onClick={() => { setHeroTargetMode(null); setTargetPoint(null); }}>Hủy</button></div>}
      </div>}</section>
    <aside className="battle-side"><section className="card"><h2>Command skill</h2>{!ownSide && <p className="muted">Chọn đội hình để hiện skill của bạn.</p>}
      {sideSkills.map(skill => {
        const info = catalog?.teamSkills?.find(item => item.id === skill.skillId);
        const turnAlreadyProcessed = view?.processedTurns?.[ownSide]?.includes(snapshot?.turnIndex);
        const shownCooldown = ourTurn && !turnAlreadyProcessed ? Math.max(0, skill.cooldownRemaining - 1) : skill.cooldownRemaining;
        const allowed = ourTurn && status === 'active' && !pending && shownCooldown === 0 && skill.usesRemaining !== 0 && knownSkills.has(skill.implementationKey);
        return <button key={skill.slotNo} className={`skill-button ${skillSlot === skill.slotNo ? 'active' : ''}`} disabled={!allowed} onClick={() => { setSkillSlot(skill.slotNo); setTargetPoint(null); setTargetPieceId(null); setEffectId(''); setHeroTargetMode(null); setSelectedPiece(null); }}><strong>#{skill.slotNo} · {skill.name || info?.name || skill.implementationKey}</strong><small>{shownCooldown ? `Hồi chiêu: ${shownCooldown}` : `Lượt dùng: ${skill.usesRemaining ?? '∞'}`} · {skill.implementationKey}</small></button>;
      })}
      {activeInfo && <div className="target-panel"><strong>{activeInfo.name}</strong><p>{pointSkills.has(activeInfo.implementationKey) ? 'Chọn ô mục tiêu trên bàn cờ.' : activeInfo.implementationKey === 'khien' ? 'Chọn một quân đồng minh trên bàn cờ.' : effectSkills.has(activeInfo.implementationKey) ? 'Chọn hiệu ứng cần tác động.' : 'Tác động ba đường sông 4, 5, 6.'}</p>
        {effectSkills.has(activeInfo.implementationKey) && <select value={effectId} onChange={event => setEffectId(event.target.value)}><option value="">Chọn hiệu ứng</option>{effects.map(effect => <option key={effect.effectId} value={effect.effectId}>{effect.code || effect.effectId} · {effect.creator} · {effect.remainingDuration} lượt</option>)}</select>}
        {targetPoint && <small>Ô: ({targetPoint.x}, {targetPoint.y})</small>}
        <button disabled={!target || pending || busy} onClick={() => command({ type: 'team_skill', slot: skillSlot, target })}>Dùng skill</button></div>}
      <h3>Skill đối thủ</h3>{enemySkills.map(skill => { const info = catalog?.teamSkills?.find(item => item.id === skill.skillId); return <div className="skill-button" key={skill.slotNo}><strong>#{skill.slotNo} · {skill.name || info?.name || skill.implementationKey}</strong><small>CD {skill.cooldownRemaining} · lượt dùng {skill.usesRemaining ?? '∞'}</small></div>; })}
      <h3>Hero skill hai bên</h3><div className="hero-skill-list">{pieces.filter(piece => piece.traitKind === 'active' || piece.traitKind === 'special_move').map(piece => { const hero = catalog?.heroes?.find(item => item.id === piece.heroId); const cd = piece.traitState?.heroCooldownRemaining ?? piece.traitState?.quangTrungCooldownRemaining ?? 0; return <button className="skill-button" key={piece.pieceId} onClick={() => setSelectedPiece(piece.pieceId)}><strong>{piece.side}: {piece.heroName || hero?.name} · {piece.traitName || hero?.trait?.name}</strong><small>{piece.traitImplementationKey === 'rook.hoanh_soc' ? (piece.traitState?.hoanhSocCharged === 1 ? 'Hoành Sóc sẵn sàng' : 'Hoành Sóc chưa tích điện') : piece.traitKind === 'active' || piece.traitImplementationKey === 'general.orthogonal_range_3' ? `CD ${cd}` : 'Nội tại'}</small></button>; })}</div></section>
      <section className="card"><h2>Điều khiển trận</h2><div className="actions"><button className="danger" disabled={status !== 'active' || !!pending || busy} onClick={() => window.confirm('Đầu hàng trận này?') && command({ type: 'resign' })}>Đầu hàng</button>
        {snapshot?.mode === 'bot' && <><select value={undoTarget} onChange={event => setUndoTarget(Number(event.target.value))}>{Array.from({ length: snapshot.version }, (_, index) => <option key={index} value={index}>Về action #{index}</option>)}</select><button className="secondary" disabled={status !== 'active' || !snapshot.version || !!pending || busy} onClick={() => command({ type: 'undo', targetSequence: undoTarget })}>Undo</button></>}</div>
        {pending && <div className="pending"><small>Lệnh #{pending.commandId} chưa xác nhận.</small><button disabled={busy} onClick={() => submit(pending)}>Thử lại cùng mã lệnh</button></div>}
        {snapshot?.state?.result && <p>Kết quả: <strong>{snapshot.state.result}</strong> · {snapshot.state.endReason}</p>}
        {replay && <div className="replay-controls"><button className="secondary" disabled={replayIndex === 0} onClick={() => setReplayIndex(replayIndex - 1)}>←</button><span>Bước {replayStep?.sequenceNo ?? 0}/{replayEnd}</span><button className="secondary" disabled={replayIndex === replay.length - 1} onClick={() => setReplayIndex(replayIndex + 1)}>→</button></div>}</section>
      <section className="card"><h2>Lịch sử nước đi</h2><p className="muted">Bước 0 là bàn cờ ban đầu. Skill, hết giờ và hoàn tác cũng là một bước.</p><div className="history-list">{history.length > 1 ? history.slice(1).map((entry, index) => <button key={entry.sequenceNo} type="button" className={`history-row ${replayStep?.sequenceNo === entry.sequenceNo ? 'active' : ''}`} onClick={() => replay && setReplayIndex(index + 1)} disabled={!replay}><strong>#{entry.sequenceNo}</strong><span>{actionLabel(entry, history[index], catalog)}</span></button>) : <p className="muted">Chưa có nước đi.</p>}</div></section>
      <section className="card"><h2>Hiệu ứng trên bàn</h2><p className="muted">{effects.length} hiệu ứng · {obstacles.length} vật cản</p>{effects.map(effect => <p key={effect.effectId}>{effect.code || effect.effectId} · còn {effect.remainingDuration} lượt</p>)}{obstacles.map(obstacle => <p key={obstacle.obstacleId || `${obstacle.position?.x}-${obstacle.position?.y}`}>{obstacle.kind} · ô {obstacle.position?.x},{obstacle.position?.y}</p>)}<details className="event-debug"><summary>Sự kiện kỹ thuật (API)</summary><p>turn.started: bắt đầu lượt; piece.moved: quân đã di chuyển. Đây là log server, không phải ký hiệu nước cờ.</p><div className="event-list">{events.map((event, index) => <div key={index}>{event.type || JSON.stringify(event)}</div>)}</div></details></section>
    </aside></div>}
  </div>;
}
