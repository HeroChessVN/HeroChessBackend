import React, { useState } from 'react';
import { statusLabel } from './records.js';

export default function CustomRoom({ room, busy, canStart, onCreate, onJoin, onLeave, onRematch, onContinue, onError }) {
  const [code, setCode] = useState('');
  const [copiedCode, setCopiedCode] = useState(null);
  const joinCode = code.trim().toUpperCase();

  async function copyCode() {
    try {
      await navigator.clipboard.writeText(room.code);
      setCopiedCode(room.code);
    } catch {
      onError(new Error('Không thể sao chép tự động. Hãy chọn và sao chép mã phòng.'));
    }
  }

  return <section className="card">
    <h1>Phòng riêng</h1>
    {room ? <>
      <p role="status">
        {room.status === 'waiting' ? 'Chờ' : room.status === 'ready' ? 'Sẵn sàng' : statusLabel(room.status)}
        {' · '}{room.memberCount}/{room.capacity}{room.isOwner && ' · Chủ phòng'}
        {room.ownerOnline === false && ' · Chủ phòng mất kết nối'}
      </p>
      <div className="status-row"><span>Mã phòng <code>{room.code}</code></span>
        <button type="button" className="secondary" onClick={copyCode}>{copiedCode === room.code ? 'Đã sao chép' : 'Sao chép mã'}</button>
      </div>
      <div className="actions">
        {room.matchId && <button type="button" onClick={onContinue}>Tiếp tục trận</button>}
        {room.status === 'ready' && room.isOwner && room.lastMatchId && <button type="button" disabled={busy} onClick={onRematch}>Chơi lại</button>}
        {!room.matchId && <button type="button" className="secondary" disabled={busy} onClick={onLeave}>Rời phòng</button>}
      </div>
    </> : <>
      <p>Tạo phòng để mời bạn hoặc nhập mã để tham gia.</p>
      <button type="button" disabled={!canStart} onClick={onCreate}>Tạo phòng</button>
      <form className="status-row" onSubmit={event => { event.preventDefault(); onJoin(joinCode); }}>
        <label>Nhập mã<input autoComplete="off" maxLength={8} value={code} onChange={event => setCode(event.target.value)} /></label>
        <button type="submit" disabled={!canStart || !/^[A-Z0-9]{8}$/.test(joinCode)}>Vào phòng</button>
      </form>
    </>}
  </section>;
}
