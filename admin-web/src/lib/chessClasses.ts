// Vai trò file: Nạp danh sách lớp quân từ DB (hero_chess.chess_class) qua endpoint admin.
// Nếu endpoint lỗi (backend cũ chưa có /admin/chess-classes) thì dùng FALLBACK_CLASSES để console vẫn chạy được.

import { api } from './api';
import { FALLBACK_CLASSES } from './classLabel';
import type { ChessClassDto } from './types';

// DEFAULT_CLASS_CODE: Lớp quân mặc định của hero mới — quân Sĩ, không cho chọn khác.
export const DEFAULT_CLASS_CODE = 'ADVISOR';

export async function loadChessClasses(signal?: AbortSignal): Promise<ChessClassDto[]> {
  try {
    const list = await api.get<ChessClassDto[]>('/admin/chess-classes', signal);
    return list.length > 0 ? list : FALLBACK_CLASSES;
  } catch {
    return FALLBACK_CLASSES;
  }
}
