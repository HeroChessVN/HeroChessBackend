import React, { useEffect, useState } from 'react';
import { api } from './api.js';
import LineupEditor from './Lineup.jsx';
import MatchView from './Match.jsx';

const savedSession = () => {
  try { return JSON.parse(sessionStorage.getItem('heroChessSession') || 'null'); }
  catch { return null; }
};
const savedTicket = () => {
  try { return JSON.parse(sessionStorage.getItem('heroChessTicket') || 'null'); }
  catch { return null; }
};

function ErrorBox({ error, onClose }) {
  if (!error) return null;
  return <div className="error" role="alert"><button className="close" onClick={onClose}>×</button>
    <strong>{error.code || 'Lỗi'}</strong>: {error.message}
    {error.details && <pre>{JSON.stringify(error.details, null, 2)}</pre>}
  </div>;
}

export default function App() {
  const [session, setSession] = useState(savedSession);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [page, setPage] = useState('lobby');
  const [me, setMe] = useState(null);
  const [catalog, setCatalog] = useState(null);
  const [lineups, setLineups] = useState([]);
  const [matches, setMatches] = useState([]);
  const [ticket, setTicket] = useState(savedTicket);
  const [matchId, setMatchId] = useState(() => sessionStorage.getItem('heroChessMatchId'));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const token = session?.accessToken;

  function logout() {
    sessionStorage.removeItem('heroChessSession');
    sessionStorage.removeItem('heroChessMatchId');
    sessionStorage.removeItem('heroChessTicket');
    setSession(null); setMe(null); setCatalog(null); setTicket(null); setMatchId(null); setPage('lobby');
  }
  function rememberTicket(next) {
    if (next?.status === 'waiting' || next?.status === 'matching') sessionStorage.setItem('heroChessTicket', JSON.stringify(next));
    else sessionStorage.removeItem('heroChessTicket');
    setTicket(next);
  }

  async function refresh() {
    if (!token) return;
    try {
      const [profile, book, teams, history] = await Promise.all([
        api(token, '/me'), api(token, '/catalog'), api(token, '/lineups'), api(token, '/matches?limit=20')
      ]);
      setMe(profile); setCatalog(book); setLineups(teams); setMatches(history.items);
    } catch (failure) {
      if (failure.status === 401) logout();
      setError(failure);
    }
  }
  useEffect(() => { refresh(); }, [token]);

  async function run(work) {
    setBusy(true); setError(null);
    try { await work(); }
    catch (failure) { setError(failure); }
    finally { setBusy(false); }
  }

  async function authenticate(register) {
    await run(async () => {
      const body = { email: email.trim(), password };
      if (register) await api(null, '/auth/register', { method: 'POST', body });
      const login = await api(null, '/auth/login?useCookies=false', { method: 'POST', body });
      const next = { accessToken: login.accessToken, refreshToken: login.refreshToken };
      sessionStorage.setItem('heroChessSession', JSON.stringify(next));
      setSession(next); setPassword('');
    });
  }

  async function queue(mode) {
    await run(async () => {
      const created = await api(token, '/matchmaking/tickets', { method: 'POST', body: { mode } });
      rememberTicket(created);
      if (created.matchId) openMatch(created.matchId);
    });
  }
  function openMatch(id, status = 'selecting') {
    rememberTicket(null);
    if (status === 'selecting' || status === 'active') sessionStorage.setItem('heroChessMatchId', id);
    else sessionStorage.removeItem('heroChessMatchId');
    setMatchId(id); setPage('match');
  }
  const currentMatch = matches.find(match => match.matchId === matchId);
  const ongoingMatch = currentMatch && (currentMatch.status === 'selecting' || currentMatch.status === 'active');
  useEffect(() => {
    if (page !== 'match' && currentMatch && !ongoingMatch) {
      sessionStorage.removeItem('heroChessMatchId');
      setMatchId(null);
    }
  }, [page, currentMatch?.status, matchId]);
  useEffect(() => {
    if (!ticket || ticket.matchId || !['waiting', 'matching'].includes(ticket.status) || !token) return;
    let polling = false;
    const timer = setInterval(async () => {
      if (polling) return;
      polling = true;
      try {
        const current = await api(token, `/matchmaking/tickets/${ticket.ticketId}`);
        rememberTicket(current);
        if (current.matchId) openMatch(current.matchId);
      } catch (failure) {
        if (failure.status === 404) {
          rememberTicket(null);
          try {
            const history = await api(token, '/matches?limit=20');
            setMatches(history.items);
            const ongoing = history.items.find(match => match.status === 'selecting' || match.status === 'active');
            if (ongoing) openMatch(ongoing.matchId, ongoing.status);
          } catch (historyError) { setError(historyError); }
        } else setError(failure);
      } finally { polling = false; }
    }, 2000);
    return () => clearInterval(timer);
  }, [ticket?.ticketId, ticket?.status, token]);

  if (!token) return <main className="auth-shell"><section className="auth-card">
    <div className="eyebrow">HERO CHESS · API TEST</div><h1>Vào trận</h1>
    <p>Đăng nhập để thử đội hình, ghép trận, bot và bàn cờ trực tiếp.</p>
    <form onSubmit={event => { event.preventDefault(); authenticate(false); }}>
      <label>Email<input autoComplete="email" type="email" value={email} onChange={event => setEmail(event.target.value)} /></label>
      <label>Mật khẩu<input autoComplete="current-password" type="password" value={password} onChange={event => setPassword(event.target.value)} /></label>
      <div className="actions"><button type="submit" disabled={busy || !email || !password}>Đăng nhập</button>
        <button type="button" className="secondary" disabled={busy || !email || !password} onClick={() => authenticate(true)}>Tạo tài khoản</button></div>
    </form>
    <small>Mật khẩu cần ít nhất 8 ký tự, chữ hoa và chữ thường.</small>
    <ErrorBox error={error} onClose={() => setError(null)} />
  </section></main>;

  return <div className="shell">
    <header className="topbar"><div><span className="brand-mark">♜</span><strong>HERO CHESS</strong><span className="subbrand">API test console</span></div>
      <nav><button className={page === 'lobby' ? 'active' : ''} onClick={() => { setPage('lobby'); refresh(); }}>Sảnh</button>
        <button className={page === 'lineup' ? 'active' : ''} onClick={() => { setPage('lineup'); refresh(); }}>Đội hình</button>
        {matchId && (page === 'match' || ongoingMatch) && <button className={page === 'match' ? 'active' : ''} onClick={() => setPage('match')}>{currentMatch?.status === 'completed' ? 'Xem replay' : 'Trận đang chơi'}</button>}</nav>
      <button className="secondary" onClick={logout}>Đăng xuất</button>
    </header>
    <div className="content"><ErrorBox error={error} onClose={() => setError(null)} />
      {page === 'lobby' && <>
        <div className="hero-banner"><div><span className="eyebrow">SẢNH CHỜ</span><h1>Chọn trận đấu của bạn.</h1><p>Đội hình và luật được kiểm tra trên server. Bàn cờ chỉ gửi nước hợp lệ và lệnh skill thật.</p></div>
          <div className="profile"><span>{me?.displayName || email || 'Player'}</span><strong>Elo {me?.elo ?? '—'}</strong><small>{me?.coinBalance ?? '—'} coin · {me?.role || '...'}</small></div></div>
        <div className="cards"><section className="card"><h2>Chơi với bot</h2><p>Vào trận ngay, chọn đội hình và đấu với bot.</p><button disabled={busy || !lineups.length} onClick={() => queue('bot')}>Bắt đầu trận bot</button></section>
          <section className="card"><h2>Ghép trận PvP</h2><p>Hai tài khoản cùng vào hàng đợi để ghép trận.</p><button disabled={busy || !lineups.length || ['waiting', 'matching'].includes(ticket?.status)} onClick={() => queue('ranked')}>Tìm đối thủ</button></section>
          <section className="card"><h2>Đội hình</h2><p>{lineups.length} đội hình đã lưu · {catalog?.heroes?.filter(x => x.isOwned).length ?? 0} hero sở hữu.</p><button className="secondary" onClick={() => setPage('lineup')}>Mở trình xếp đội</button></section></div>
        {!lineups.length && <p className="hint">Hãy tạo đội hình trước khi tìm trận. Tài khoản test cần có đủ hero để điền 16 vị trí.</p>}
        {['waiting', 'matching'].includes(ticket?.status) && <div className="status-row">{ticket.status === 'matching' ? 'Đang tạo trận…' : 'Đang chờ ghép trận…'} <code>{ticket.ticketId}</code>
          {ticket.status === 'waiting' && <button className="secondary" onClick={() => run(async () => { await api(token, `/matchmaking/tickets/${ticket.ticketId}`, { method: 'DELETE' }); rememberTicket(null); })}>Hủy tìm trận</button>}</div>}
        {ongoingMatch && <div className="status-row">Trận đang chơi: <code>{matchId}</code><button onClick={() => setPage('match')}>Tiếp tục</button></div>}
        <div className="split"><section className="card"><div className="section-head"><h2>Lịch sử trận</h2><button className="secondary" onClick={() => run(refresh)}>Làm mới</button></div>
          {matches.length ? matches.map(match => <button className="list-row" key={match.matchId} onClick={() => openMatch(match.matchId, match.status)}><span>{match.mode === 'bot' ? 'BOT' : 'PVP'} · {match.status}</span><small>{match.result || match.endReason || new Date(match.createdAt).toLocaleString('vi-VN')}</small></button>) : <p className="muted">Chưa có trận.</p>}</section>
          <section className="card"><h2>Hero trong catalog</h2><div className="catalog-list">{catalog?.heroes?.map(hero => <div className="catalog-row" key={hero.id}><div><strong>{hero.name}</strong><small>{hero.classCode} · {hero.setupPoints} SP{hero.trait ? ` · ${hero.trait.name}` : ''}</small></div>
            {hero.isOwned ? <span className="owned">Đã sở hữu</span> : <button disabled={busy} className="secondary" onClick={() => run(async () => { await api(token, `/shop/heroes/${hero.id}/purchase`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() } }); await refresh(); })}>Mua {hero.coinPrice}</button>}</div>)}</div></section></div>
      </>}
      {page === 'lineup' && catalog && <LineupEditor token={token} catalog={catalog} lineups={lineups} onChanged={refresh} onError={setError} />}
      {page === 'match' && matchId && <MatchView key={matchId} token={token} matchId={matchId} catalog={catalog} lineups={lineups} onBack={ended => { if (ended) { sessionStorage.removeItem('heroChessMatchId'); setMatchId(null); } setPage('lobby'); refresh(); }} onCancel={() => { sessionStorage.removeItem('heroChessMatchId'); setMatchId(null); setPage('lobby'); refresh(); }} onError={setError} />}
    </div>
  </div>;
}
