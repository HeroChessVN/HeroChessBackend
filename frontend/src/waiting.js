// Vai trò file: Hàm thuần cho màn chờ trận; không gọi API, không giữ state.
import { sideLabel } from './records.js';

// Trạng thái hiển thị của một phe trong màn chờ.
export const sideState = side => (side.confirmed ? 'confirmed' : side.selectedLineup ? 'picked' : 'waiting');

// sideStateLabel: Nhãn tiếng Việt cho trạng thái phe; bot dùng nhãn riêng.
export const sideStateLabel = side => {
  if (side.isBot) return side.confirmed ? 'Bot đã sẵn sàng' : 'Bot đang chuẩn bị';
  return { confirmed: 'Đã xác nhận', picked: 'Đã chọn đội hình', waiting: 'Đang chờ chọn' }[sideState(side)];
};

// ownSideOf: Phe của người đang gọi API; ưu tiên khớp playerId hồ sơ, không có thì fallback theo tên phe.
export function ownSideOf(sides, playerId, side) {
  const list = sides || [];
  return list.find(item => playerId && item.player?.playerId === playerId)
    || list.find(item => side && item.side === side) || null;
};

// opponentSideOf: Phe còn lại, tức đối thủ của người đang gọi API; null khi chưa xác định phe mình.
export function opponentSideOf(sides, own) {
  if (!own) return null;
  return (sides || []).find(item => item !== own) || null;
}

// selectionDeadline: Hạn chọn đội hình = thời điểm tạo trận + cửa sổ chọn; null khi không có mốc tạo trận.
// ponytail: cửa sổ 120s lấy theo mặc định của MatchRuntime:SelectionSeconds; nếu server đổi thì FE cần truyền giá trị này vào.
export const SELECTION_SECONDS = 120;

export function selectionDeadline(matchCreatedAt) {
  const created = Date.parse(matchCreatedAt || '');
  if (!Number.isFinite(created)) return null;
  return created + SELECTION_SECONDS * 1000;
}

// remainingSeconds: Số giây còn lại để chọn đội hình; null khi không có deadline.
export function remainingSeconds(deadlineMs, nowMs) {
  if (deadlineMs == null) return null;
  return Math.max(0, Math.ceil((deadlineMs - nowMs) / 1000));
}

// lineupView: Ghép đội hình đã chọn với catalog để có tên hero/skill; null khi phe chưa chọn.
export function lineupView(selectedLineup, catalog) {
  if (!selectedLineup) return null;
  const heroes = (selectedLineup.entries || []).map(entry => {
    const hero = catalog?.heroes?.find(item => item.id === entry.heroId);
    return {
      slotNo: entry.slotNo,
      name: hero?.name || 'Hero',
      classCode: hero?.classCode || '',
      setupPoints: hero?.setupPoints ?? null
    };
  });
  const skills = (selectedLineup.skills || []).map(skill => {
    const info = catalog?.teamSkills?.find(item => item.id === skill.skillId);
    return { slotNo: skill.slotNo, name: info?.name || 'Skill', implementationKey: info?.implementationKey || '' };
  });
  return { id: selectedLineup.id, name: selectedLineup.name, totalSp: selectedLineup.totalSp, heroes, skills };
}

// sideTitle: Tên hiển thị của phe; bot không có hồ sơ nên hiện "Bot".
export const sideTitle = side => (side.isBot ? 'Bot' : side.player?.displayName || 'Người chơi');

// sideMeta: Dòng thông tin phụ (phe, Elo, ảnh, khung) của một phe.
export function sideMeta(side) {
  const parts = [sideLabel(side.side)];
  if (side.player) parts.push(`Elo ${side.player.elo}`);
  if (side.player?.avatarName) parts.push(`Ảnh: ${side.player.avatarName}`);
  if (side.player?.frameName) parts.push(`Khung: ${side.player.frameName}`);
  return parts.join(' · ');
}
