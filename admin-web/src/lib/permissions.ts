// Vai trò file: Quyền fine-grained của admin. Nguồn sự thật là backend (HeroChess.Api/Auth/AccountPolicies.cs).
// Lưu ý: backend KHÔNG có endpoint trả quyền của chính mình — nguồn duy nhất là GET /api/v1/admin/accounts,
// vốn cần quyền 'admin'. Nếu 403 thì quyền chưa xác định (permissions = null) và UI hiện đầy đủ,
// còn server vẫn chặn thật. Xem docs/admin/ADMIN_API_GUIDE.md mục 1.2.

export const PERMISSION_READ = 'read';
export const PERMISSION_HERO = 'hero';
export const PERMISSION_SHOP = 'shop';
export const PERMISSION_PLAYER = 'player';
export const PERMISSION_GIFTCODE = 'giftcode';
export const PERMISSION_ANALYTICS = 'analytics';
export const PERMISSION_RULESET = 'ruleset';
export const PERMISSION_AUDIT = 'audit';
export const PERMISSION_ADMIN = 'admin';
export const PERMISSION_WILDCARD = '*';

export const ALL_PERMISSIONS = [
  PERMISSION_READ,
  PERMISSION_HERO,
  PERMISSION_SHOP,
  PERMISSION_PLAYER,
  PERMISSION_GIFTCODE,
  PERMISSION_ANALYTICS,
  PERMISSION_RULESET,
  PERMISSION_AUDIT,
  PERMISSION_ADMIN
] as const;

export type Permission = (typeof ALL_PERMISSIONS)[number];

// PermissionSet: quyền đã biết của admin hiện tại; null = chưa xác định (chưa lấy được hoặc bị 403).
export type PermissionSet = ReadonlySet<string> | null;

// hasPermission: quyền ghi của một miền cũng cho đọc miền đó (khớp AccountPolicies.HasPermissionAsync).
// Với `read`: chỉ mở miền dashboard, KHÔNG mở các miền khác (khớp code backend).
export function hasPermission(permissions: PermissionSet, domain: string): boolean {
  if (permissions === null) return true; // chưa xác định → hiển thị, server mới là nơi quyết định
  if (permissions.has(PERMISSION_WILDCARD)) return true;
  if (permissions.has(domain)) return true;
  return domain === PERMISSION_READ && permissions.has(PERMISSION_READ);
}

// canWrite: quyền ghi của một miền. Backend không tách read/write trong cùng key,
// nên nếu có key của miền thì coi như được ghi miền đó (server vẫn kiểm tra lại).
export function canWrite(permissions: PermissionSet, domain: string): boolean {
  if (permissions === null) return true;
  return permissions.has(PERMISSION_WILDCARD) || permissions.has(domain);
}

// parsePermissions: chuyển mảng quyền từ API thành Set; null khi chưa xác định.
export function parsePermissions(raw: string[] | null | undefined): PermissionSet {
  if (!raw) return null;
  return new Set(raw.map(x => x.trim().toLowerCase()).filter(Boolean));
}
