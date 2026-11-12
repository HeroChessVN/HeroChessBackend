// Vai trò file: Bảo vệ route — chỉ tài khoản admin đã đăng nhập mới vào được console.
// Kiểm tra quyền miền chỉ để ẩn/hiện; server vẫn là nơi quyết định thật (403 sẽ hiện trang từ chối).

import { Navigate, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from './AuthContext';
import { hasPermission, type PermissionSet } from '../lib/permissions';
import { ForbiddenPage } from '../pages/ForbiddenPage';

interface ProtectedRouteProps {
  children: ReactNode;
  domain?: string;
}

export function ProtectedRoute({ children, domain }: ProtectedRouteProps) {
  const { ready, authenticated, permissions } = useAuth();
  const location = useLocation();

  if (!ready) {
    return (
      <div className="grid min-h-screen place-items-center bg-surface">
        <div className="flex flex-col items-center gap-3 text-ink-muted">
          <span className="size-6 animate-spin rounded-full border-2 border-gold-500 border-t-transparent" aria-hidden />
          <p className="text-sm">Đang khôi phục phiên đăng nhập…</p>
        </div>
      </div>
    );
  }

  if (!authenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }

  if (domain && !hasPermission(permissions as PermissionSet, domain)) {
    return <ForbiddenPage />;
  }

  return <>{children}</>;
}
