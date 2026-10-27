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

// sideLabel: Tên phe hiển thị.
export const sideLabel = side => ({ red: 'Đỏ', black: 'Đen' }[side] ?? side ?? '—');

// playerName: Tên người chơi trong hồ sơ; bot không có hồ sơ nên hiện "Bot".
export const playerName = player => player?.displayName || 'Bot';

// playerAvatar: URL ảnh đại diện; chỉ có khi người chơi đã tải ảnh từ máy.
export const playerAvatar = player => player?.avatarUrl || null;

// playerFrame: Mã khung đại diện để client dựng ảnh khung.
export const playerFrame = player => player?.frameName || null;

// resultLabel: Kết quả trận theo phe đang xem.
export function resultLabel(result, side) {
  if (!result) return '—';
  if (result === 'draw') return 'Hòa';
  if (result === `${side}_win`) return 'Thắng';
  return 'Thua';
}

// endReasonLabel: Lý do kết thúc trận.
export const endReasonLabel = reason =>
  ({ checkmate: 'Chiếu hết', resign: 'Đầu hàng', timeout: 'Hết giờ', stalemate: 'Hòa do bế tắc', draw: 'Hòa', agreement: 'Thỏa thuận' }[reason] ?? reason ?? '—');
