// Vai trò file: Quản lý phiên đăng nhập admin (access token + refresh token) và quyền của tài khoản.
// Token lưu trong sessionStorage: mất khi đóng tab, không ghi rõ vào ổ đĩa như localStorage.

import { parsePermissions, type PermissionSet } from './permissions';
import type { MeDto, TokenResponse } from './types';

const STORAGE_KEY = 'hero-chess-admin-session';

export interface Session {
  accessToken: string;
  refreshToken: string;
  expiresAt: number;
  user: MeDto | null;
  permissions: PermissionSet;
}

const emptySession: Session = {
  accessToken: '',
  refreshToken: '',
  expiresAt: 0,
  user: null,
  permissions: null
};

// read: Đọc phiên đang lưu; parse lỗi thì coi như chưa đăng nhập.
export function readSession(): Session {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return emptySession;
    const parsed = JSON.parse(raw) as Partial<Session>;
    if (!parsed.accessToken || !parsed.refreshToken) return emptySession;
    return {
      accessToken: parsed.accessToken,
      refreshToken: parsed.refreshToken,
      expiresAt: parsed.expiresAt ?? 0,
      user: parsed.user ?? null,
      permissions: parsePermissions((parsed as { permissionKeys?: string[] }).permissionKeys ?? null)
    };
  } catch {
    return emptySession;
  }
}

// write: Lưu phiên. Quyền lưu dạng mảng string vì Set không JSON được.
export function writeSession(session: Session): void {
  const payload = {
    accessToken: session.accessToken,
    refreshToken: session.refreshToken,
    expiresAt: session.expiresAt,
    user: session.user,
    permissionKeys: session.permissions ? [...session.permissions] : null
  };
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
}

// clear: Xoá phiên khi đăng xuất hoặc token hết hạn.
export function clearSession(): void {
  sessionStorage.removeItem(STORAGE_KEY);
}

// isExpired: Access token hết hạn khi quá thời hạn đã lưu.
export function isExpired(session: Session, skewMs = 30_000): boolean {
  if (session.expiresAt === 0) return false;
  return Date.now() + skewMs >= session.expiresAt;
}

// toSession: Dựng Session từ response của Identity.
export function toSession(tokens: TokenResponse, user: MeDto | null, permissions: PermissionSet): Session {
  return {
    accessToken: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    expiresAt: Date.now() + tokens.expiresIn * 1000,
    user,
    permissions
  };
}
