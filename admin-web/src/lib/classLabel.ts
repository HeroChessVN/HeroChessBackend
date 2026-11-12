// Vai trò file: Nhãn tiếng Việt cho mã lớp hero. Mã lớp lấy từ seed catalog thực tế
// (Hero_Chess_DB_Prototype_v0_1/02_seed_catalog.sql): ADVISOR, GENERAL, ELEPHANT, HORSE, ROOK, CANNON, SOLDIER.

const labels: Record<string, string> = {
  ADVISOR: 'Quân sư',
  GENERAL: 'Tướng quân',
  ELEPHANT: 'Tượng',
  HORSE: 'Mã',
  ROOK: 'Xe',
  CANNON: 'Pháo',
  SOLDIER: 'Binh'
};

// classLabel: Dịch mã lớp sang nhãn; mã lạ giữ nguyên để không mất thông tin.
export function classLabel(code: string): string {
  return labels[code.toUpperCase()] ?? code;
}

// CLASS_CODES: Danh sách lớp hợp lệ dùng cho form tạo hero.
export const CLASS_CODES = Object.keys(labels);
