import React, { useEffect, useState } from 'react';
import { api } from './api.js';
import { durationLabel, eloClass, eloLabel, modeLabel, outcomeClass, outcomeLabel, statusLabel } from './records.js';

const classes = { general: 'TƯỚNG', advisor: 'SĨ', elephant: 'TƯỢNG', rook: 'XE', cannon: 'PHÁO', horse: 'MÃ', soldier: 'TỐT' };

export default function MatchDetail({ token, matchId, onOpenReplay, onBack, onError }) {
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    let live = true;
    setLoading(true); setError(null);
    api(token, `/matches/${matchId}`)
      .then(value => { if (live) setDetail(value); })
      .catch(failure => { if (live) { if (failure.status === 401) onError(failure); else setError(failure); } })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [token, matchId, onError]);

  if (loading) return <div className="page"><p className="muted">Đang tải chi tiết trận…</p></div>;
  if (error) return <div className="page"><div className="error" role="alert"><strong>{error.code || 'Lỗi'}</strong>: {error.message}</div>
    <button className="secondary" onClick={onBack}>← Lịch sử</button></div>;
  if (!detail) return <div className="page"><p className="muted">Không tìm thấy trận.</p></div>;

  return <div className="page">
    <div className="section-head"><div>
      <span className="eyebrow">CHI TIẾT TRẬN</span>
      <h1>{modeLabel(detail.mode)} · {statusLabel(detail.status)}</h1>
      <p className="match-id">{detail.matchId}</p>
    </div><div className="match-controls"><button className="secondary" onClick={onBack}>← Lịch sử</button>
      {detail.replayAvailable && <button onClick={() => onOpenReplay(detail.matchId)}>Xem replay</button>}</div></div>

    <div className="match-meta">
      <span className="pill">{detail.result || statusLabel(detail.status)}</span>
      {detail.endReason && <span>Lý do: {detail.endReason}</span>}
      <span>Bắt đầu: {detail.startedAt ? new Date(detail.startedAt).toLocaleString('vi-VN') : '—'}</span>
      <span>Kết thúc: {detail.endedAt ? new Date(detail.endedAt).toLocaleString('vi-VN') : '—'}</span>
      <span>Thời lượng: {durationLabel(detail.endedAt && detail.startedAt ? (new Date(detail.endedAt) - new Date(detail.startedAt)) / 1000 : null)}</span>
      <span>Số bước: {detail.actionCount}</span>
    </div>

    <div className="split">
      {detail.sides.map(side => <section className="card" key={side.side}>
        <div className="section-head"><h2>{side.side === 'red' ? 'Đỏ' : 'Đen'}</h2>
          <span className={`result-pill ${outcomeClass(side.outcome)}`}>{outcomeLabel(side.outcome)}</span></div>
        <p>{side.displayName || (side.participantType === 'bot' ? 'Bot' : '—')}</p>
        <small>Elo {side.eloBefore ?? '—'} → {side.eloAfter ?? '—'}
          {side.eloBefore != null && side.eloAfter != null && <> (<span className={`elo ${eloClass(side.eloAfter - side.eloBefore)}`}>{eloLabel(side.eloAfter - side.eloBefore)}</span>)</>}
          {' · '}thưởng {side.coinReward} coin</small>
        <h3>Đội hình đã dùng</h3>
        {!side.lineup?.entries?.length ? <p className="muted">Không có dữ liệu đội hình.</p>
          : <div className="lineup-grid">{side.lineup.entries.map(entry => <div className="lineup-cell" key={entry.slotNo}>
            <span className="slot-no">#{entry.slotNo}</span>
            <strong>{entry.heroName || entry.heroCode || 'Hero'}</strong>
            <small>{classes[entry.classCode] || entry.classCode} · {entry.setupPoints} SP{entry.factionCode ? ` · ${entry.factionCode}` : ''}</small>
          </div>)}</div>}
        {!!side.lineup?.skills?.length && <>
          <h3>Command skill</h3>
          <ul className="skill-list">{side.lineup.skills.map(skill => <li key={skill.slotNo}>
            <strong>#{skill.slotNo} · {skill.name || skill.code || skill.implementationKey}</strong>
            {skill.implementationKey && <small>{skill.implementationKey}</small>}
          </li>)}</ul>
        </>}
      </section>)}
    </div>
  </div>;
}
