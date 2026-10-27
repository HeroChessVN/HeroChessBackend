// Vai trò file: Màn chờ trận — hiển thị hồ sơ và đội hình của cả hai phe, chờ hai bên xác nhận.
import React, { useEffect, useState } from 'react';
import { playerAvatar, sideLabel } from './records.js';
import { lineupView, opponentSideOf, ownSideOf, remainingSeconds, selectionDeadline, sideMeta, sideStateLabel, sideTitle } from './waiting.js';

export default function MatchSelection({ selection, catalog, lineups, ownSide, playerId, matchCreatedAt, selectedLineupId, onPickLineup, onSelect, onConfirm, onCancel, busy }) {
  const sides = selection?.sides || [];
  const own = ownSideOf(sides, playerId, ownSide);
  const opponent = opponentSideOf(sides, own);
  const [now, setNow] = useState(() => Date.now());

  // Đếm ngược hạn chọn đội hình; mốc tạo trận do server trả qua /matches.
  const deadline = selectionDeadline(matchCreatedAt);
  useEffect(() => {
    if (deadline == null) return;
    const timer = setInterval(() => setNow(Date.now()), 500);
    return () => clearInterval(timer);
  }, [deadline]);

  const left = remainingSeconds(deadline, now);
  const ready = sides.length > 0 && sides.every(side => side.confirmed);
  const ownLineup = own?.selectedLineup ? lineupView(own.selectedLineup, catalog) : null;
  const enemyLineup = opponent?.selectedLineup ? lineupView(opponent.selectedLineup, catalog) : null;

  function renderSide(side, isOwn) {
    if (!side) return <section className="card wait-side"><p className="muted">Chưa có đối thủ.</p></section>;
    const avatar = playerAvatar(side.player);
    const lineup = isOwn ? ownLineup : enemyLineup;
    return <section className={`card wait-side ${isOwn ? 'own' : ''} ${side.confirmed ? 'ready' : ''}`}>
      <header className="wait-side-head">
        {avatar ? <img className="avatar" src={avatar} alt="" /> : <span className="avatar placeholder">{sideLabel(side.side).slice(0, 1)}</span>}
        <div>
          <strong>{sideTitle(side)}</strong>
          <small>{sideMeta(side)}</small>
        </div>
        <span className={`wait-state ${side.confirmed ? 'ready' : ''}`}>{sideStateLabel(side)}</span>
      </header>
      {isOwn && <div className="wait-picker">
        <label>Đội hình của bạn
          <select value={selectedLineupId} onChange={event => onPickLineup(event.target.value)} disabled={busy}>
            {(lineups || []).length === 0 && <option value="">Chưa có đội hình</option>}
            {(lineups || []).map(lineup => <option key={lineup.id} value={lineup.id}>{lineup.name} · {lineup.totalSp} SP</option>)}
          </select>
        </label>
        <div className="actions">
          <button disabled={busy || !selectedLineupId || side.confirmed} onClick={onSelect}>Chọn đội hình</button>
          <button disabled={busy || !own?.ownLineupId || side.confirmed} onClick={onConfirm}>Xác nhận</button>
          <button className="danger" disabled={busy} onClick={onCancel}>Hủy tham gia</button>
        </div>
      </div>}
      {isOwn
        ? (lineup ? renderLineup(lineup) : <p className="muted">Bạn chưa chọn đội hình cho trận này.</p>)
        : (lineup ? renderLineup(lineup) : <p className="muted">Đối thủ chưa chọn đội hình.</p>)}
    </section>;
  }

  // renderLineup: Lưới hero theo slot và danh sách command skill của đội hình đã chọn.
  function renderLineup(lineup) {
    return <div className="wait-lineup">
      <h3>{lineup.name} <small>· {lineup.totalSp} SP</small></h3>
      <div className="lineup-grid">{lineup.heroes.map(hero => <div className="lineup-cell" key={hero.slotNo}>
        <span className="slot-no">#{hero.slotNo}</span>
        <strong>{hero.name}</strong>
        <small>{hero.classCode || '—'}{hero.setupPoints != null ? ` · ${hero.setupPoints} SP` : ''}</small>
      </div>)}</div>
      {lineup.skills.length > 0 && <ul className="skill-list">{lineup.skills.map(skill => <li key={skill.slotNo}>
        <strong>#{skill.slotNo} · {skill.name}</strong>
        {skill.implementationKey && <small>{skill.implementationKey}</small>}
      </li>)}</ul>}
    </div>;
  }

  return <section className="waiting-room">
    <div className="match-meta">
      <span className="pill">{selection?.status === 'selecting' ? 'Đang chọn đội hình' : selection?.status}</span>
      {left != null && <span className="timer">Còn {left}s</span>}
      <span>{ready ? 'Hai bên đã xác nhận · trận sẽ bắt đầu' : 'Chờ hai bên xác nhận đội hình'}</span>
    </div>
    <div className="wait-versus">
      {renderSide(own, true)}
      <div className="wait-vs">VS</div>
      {renderSide(opponent, false)}
    </div>
  </section>;
}
