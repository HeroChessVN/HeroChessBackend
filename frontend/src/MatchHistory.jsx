import React, { useCallback, useEffect, useState } from 'react';
import { api } from './api.js';
import { query, durationLabel, eloClass, eloLabel, modeLabel, outcomeClass, outcomeLabel, statusLabel } from './records.js';

export default function MatchHistory({ token, onOpenMatch, onError }) {
  const [items, setItems] = useState([]);
  const [cursor, setCursor] = useState(null);
  const [mode, setMode] = useState('');
  const [outcome, setOutcome] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const page = await api(token, `/matches${query({ limit: 20, mode, outcome })}`);
      setItems(page.items); setCursor(page.nextCursor);
    } catch (failure) {
      if (failure.status === 401) onError(failure); else setError(failure);
    } finally { setLoading(false); }
  }, [token, mode, outcome, onError]);

  useEffect(() => { load(); }, [load]);

  async function loadMore() {
    setLoadingMore(true); setError(null);
    try {
      const page = await api(token, `/matches${query({ limit: 20, cursor, mode, outcome })}`);
      setItems(previous => [...previous, ...page.items]); setCursor(page.nextCursor);
    } catch (failure) { setError(failure); } finally { setLoadingMore(false); }
  }

  return <div className="page">
    <div className="section-head"><div>
      <span className="eyebrow">LỊCH SỬ</span><h1>Trận đấu của bạn</h1>
      <p className="muted">Chỉ hiển thị các trận bạn đã tham gia, mới nhất trước.</p>
    </div><button className="secondary" onClick={load} disabled={loading}>Làm mới</button></div>

    <div className="filters">
      <label>Chế độ<select value={mode} onChange={event => setMode(event.target.value)}>
        <option value="">Tất cả</option><option value="ranked">PvP</option><option value="bot">Bot</option></select></label>
      <label>Kết quả<select value={outcome} onChange={event => setOutcome(event.target.value)}>
        <option value="">Tất cả</option><option value="win">Thắng</option><option value="draw">Hòa</option><option value="loss">Thua</option></select></label>
    </div>

    {error && <div className="error" role="alert"><strong>{error.code || 'Lỗi'}</strong>: {error.message}</div>}
    {loading ? <p className="muted">Đang tải lịch sử…</p>
      : !items.length ? <section className="card"><p className="muted">Chưa có trận nào. Hãy vào sảnh để tìm đối thủ.</p></section>
        : <section className="card">
          <div className="match-list">{items.map(item => <button key={item.matchId} className="match-row" onClick={() => onOpenMatch(item.matchId, item.status)}>
            <span className={`result-pill ${outcomeClass(item.outcome)}`}>{outcomeLabel(item.outcome)}</span>
            <div className="match-row-main">
              <strong>{modeLabel(item.mode)} · {statusLabel(item.status)}</strong>
              <small>{item.opponentDisplayName ? `vs ${item.opponentDisplayName}` : 'vs Bot'} · {new Date(item.createdAt).toLocaleString('vi-VN')} · {durationLabel(item.durationSeconds)}</small>
            </div>
            <div className="match-row-meta">
              <span className={`elo ${eloClass(item.eloDelta)}`}>{eloLabel(item.eloDelta)} Elo</span>
              <small>{item.replayAvailable ? 'Có replay' : item.status === 'completed' ? 'Không có replay' : 'Chưa kết thúc'}</small>
            </div>
          </button>)}</div>
          {cursor && <div className="actions more"><button className="secondary" onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Đang tải…' : 'Xem thêm'}</button></div>}
        </section>}
  </div>;
}
