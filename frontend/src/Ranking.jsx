import React, { useCallback, useEffect, useState } from 'react';
import { api } from './api.js';
import { query, rankBadge, recordLabel } from './records.js';

export default function Ranking({ token, onError }) {
  const [rows, setRows] = useState([]);
  const [cursor, setCursor] = useState(null);
  const [total, setTotal] = useState(0);
  const [mine, setMine] = useState(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [page, me] = await Promise.all([api(token, '/ranking'), api(token, '/ranking/me')]);
      setRows(page.items); setCursor(page.nextCursor); setTotal(page.total); setMine(me);
    } catch (failure) {
      if (failure.status === 401) onError(failure); else setError(failure);
    } finally { setLoading(false); }
  }, [token, onError]);

  useEffect(() => { load(); }, [load]);

  async function loadMore() {
    setLoadingMore(true); setError(null);
    try {
      const page = await api(token, `/ranking${query({ cursor, limit: 50 })}`);
      setRows(previous => [...previous, ...page.items]); setCursor(page.nextCursor); setTotal(page.total);
    } catch (failure) { setError(failure); } finally { setLoadingMore(false); }
  }

  return <div className="page">
    <div className="section-head"><div>
      <span className="eyebrow">BẢNG XẾP HẠNG</span><h1>Xếp hạng người chơi</h1>
      <p className="muted">{total} người chơi có rating. Xếp theo Elo giảm dần; hạng bằng nhau xếp theo ID tài khoản.</p>
    </div><button className="secondary" onClick={load} disabled={loading}>Làm mới</button></div>

    {mine && <section className="card my-rank">
      <span className="rank-badge">{rankBadge(mine.rank)}</span>
      <div><strong>{mine.displayName || 'Bạn'}</strong>
        <small>Elo {mine.rating} · {recordLabel(mine)} · {mine.gamesPlayed} trận</small></div>
      <small className="muted">{mine.total} người chơi</small>
    </section>}

    {error && <div className="error" role="alert"><strong>{error.code || 'Lỗi'}</strong>: {error.message}</div>}
    {loading ? <p className="muted">Đang tải bảng xếp hạng…</p>
      : !rows.length ? <section className="card"><p className="muted">Chưa có dữ liệu xếp hạng. Hãy chơi một trận đã xếp.</p></section>
        : <section className="card">
          <div className="table-scroll">
            <table className="rank-table">
              <thead><tr><th>Hạng</th><th>Người chơi</th><th>Elo</th><th>Trận</th><th>Thành tích</th></tr></thead>
              <tbody>{rows.map(row => <tr key={row.playerId} className={mine?.playerId === row.playerId ? 'me' : ''}>
                <td><span className="rank-badge">{rankBadge(row.rank)}</span></td>
                <td className="name">{row.displayName}</td>
                <td><strong>{row.rating}</strong></td>
                <td>{row.gamesPlayed}</td>
                <td>{recordLabel(row)}</td>
              </tr>)}</tbody>
            </table>
          </div>
          {cursor && <div className="actions more"><button className="secondary" onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Đang tải…' : 'Xem thêm'}</button></div>}
        </section>}
  </div>;
}
