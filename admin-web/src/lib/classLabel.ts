// Vai trò file: Nhãn tiếng Việt + danh sách lớp quân. Nguồn chuẩn là bảng hero_chess.chess_class
// (GET /api/v1/admin/chess-classes). Bảng fallback chỉ dùng khi backend chưa có endpoint mới.

import type { ChessClassDto } from './types';

// FALLBACK_CLASSES: Ảnh chụp của hero_chess.chess_class, dùng khi gọi /admin/chess-classes lỗi.
export const FALLBACK_CLASSES: ChessClassDto[] = [
  { code: 'ADVISOR', nameVi: 'Sĩ', baseSp: 1, requiredCount: 2, baseMovementCode: 'xiangqi.advisor' },
  { code: 'CANNON', nameVi: 'Pháo', baseSp: 6, requiredCount: 2, baseMovementCode: 'xiangqi.cannon' },
  { code: 'ELEPHANT', nameVi: 'Tượng', baseSp: 2, requiredCount: 2, baseMovementCode: 'xiangqi.elephant' },
  { code: 'GENERAL', nameVi: 'Tướng', baseSp: 0, requiredCount: 1, baseMovementCode: 'xiangqi.general' },
  { code: 'HORSE', nameVi: 'Mã', baseSp: 4, requiredCount: 2, baseMovementCode: 'xiangqi.horse' },
  { code: 'ROOK', nameVi: 'Xe', baseSp: 7, requiredCount: 2, baseMovementCode: 'xiangqi.rook' },
  { code: 'SOLDIER', nameVi: 'Tốt', baseSp: 1, requiredCount: 5, baseMovementCode: 'xiangqi.soldier' }
];

const fallback: Record<string, string> = Object.fromEntries(FALLBACK_CLASSES.map(x => [x.code, x.nameVi]));

// classLabel: Dịch mã lớp sang nhãn. Ưu tiên name_vi từ DB; mã lạ giữ nguyên để không mất thông tin.
export function classLabel(code: string, classes?: ChessClassDto[] | null): string {
  const fromDb = classes?.find(x => x.code === code)?.nameVi;
  return fromDb ?? fallback[code.toUpperCase()] ?? code;
}
