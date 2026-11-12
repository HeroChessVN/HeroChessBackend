// Vai trò file: Trang 403 — tài khoản đã đăng nhập nhưng thiếu quyền miền.

import { Link } from 'react-router-dom';
import { Lock } from 'lucide-react';

export function ForbiddenPage() {
  return (
    <div className="grid min-h-[60vh] place-items-center">
      <div className="max-w-md text-center">
        <div className="mx-auto grid size-14 place-items-center rounded-full border border-info/40 bg-info-bg text-info">
          <Lock size={24} />
        </div>
        <h1 className="mt-4 text-lg font-semibold text-ink">Không đủ quyền</h1>
        <p className="mt-2 text-sm text-ink-muted">
          Tài khoản này không có quyền cho miền đang mở. Liên hệ quản trị viên cấp quyền nếu cần truy cập.
        </p>
        <Link to="/" className="mt-5 inline-block rounded-lg border border-border bg-panel-2 px-4 py-2 text-sm text-ink transition-colors hover:bg-navy-700">
          Về Dashboard
        </Link>
      </div>
    </div>
  );
}
