// Vai trò file: Hàm thuần để hiển thị dữ liệu trận/ranking; không gọi API, không giữ state.
export const outcomeLabel = outcome => ({ win: 'Thắng', loss: 'Thua', draw: 'Hòa' }[outcome] ?? '—');
export const outcomeClass = outcome => ({ win: 'win', loss: 'loss', draw: 'draw' }[outcome] ?? '');

export const modeLabel = mode => ({ bot: 'Bot', ranked: 'PvP' }[mode] ?? mode ?? '—');

export const statusLabel = status =>
  ({ selecting: 'Chọn đội hình', active: 'Đang chơi', completed: 'Đã kết thúc', cancelled: 'Đã hủy' }[status] ?? status ?? '—');

export function durationLabel(seconds) {
  if (seconds == null || !Number.isFinite(seconds)) return '—';
  const total = Math.max(0, Math.round(seconds));
  const minutes = Math.floor(total / 60);
  return minutes > 0 ? `${minutes} phút ${total % 60}s` : `${total}s`;
}

export function eloLabel(delta) {
  if (delta == null) return '—';
  return delta > 0 ? `+${delta}` : `${delta}`;
}

export const eloClass = delta => (delta == null ? '' : delta > 0 ? 'up' : delta < 0 ? 'down' : '');

export const recordLabel = entry => `${entry.wins}T · ${entry.draws}H · ${entry.losses}B`;

export function rankBadge(rank) {
  if (rank == null) return '—';
  return rank === 1 ? '🥇' : rank === 2 ? '🥈' : rank === 3 ? '🥉' : `#${rank}`;
}

// query: Dựng query string từ object, bỏ qua giá trị null/rỗng; giữ nguyên key đã có trong path.
export function query(params) {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value == null || value === '') continue;
    search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}
