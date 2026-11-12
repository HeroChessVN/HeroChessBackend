// Vai trò file: Header trên cùng — tiêu đề trang, thông tin tài khoản admin và nút đăng xuất.

import { useState } from 'react';
import { LogOut, Menu, ShieldCheck } from 'lucide-react';
import { useAuth } from '../../auth/AuthContext';
import { initials } from '../../lib/format';

interface HeaderProps {
  title: string;
  subtitle?: string;
  onOpenSidebar: () => void;
}

export function Header({ title, subtitle, onOpenSidebar }: HeaderProps) {
  const { user, logout } = useAuth();
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <header className="flex items-center justify-between gap-4 border-b border-border bg-surface-2 px-4 py-3 sm:px-6">
      <div className="flex min-w-0 items-center gap-3">
        <button type="button" className="rounded-lg p-2 text-ink-muted hover:bg-panel-2 hover:text-ink lg:hidden" onClick={onOpenSidebar} aria-label="Mở menu">
          <Menu size={19} />
        </button>
        <div className="min-w-0">
          <h1 className="truncate text-base font-semibold tracking-tight text-ink sm:text-lg">{title}</h1>
          {subtitle && <p className="truncate text-xs text-ink-muted sm:text-sm">{subtitle}</p>}
        </div>
      </div>

      <div className="relative flex shrink-0 items-center gap-2">
        <button
          type="button"
          onClick={() => setMenuOpen(current => !current)}
          className="flex items-center gap-2 rounded-lg border border-border bg-panel px-2.5 py-1.5 transition-colors hover:border-border-strong"
          aria-haspopup="menu"
          aria-expanded={menuOpen}
        >
          <span className="grid size-7 place-items-center rounded-full bg-gold-500/20 text-xs font-bold text-gold-400">
            {initials(user?.displayName ?? 'Admin')}
          </span>
          <span className="hidden max-w-40 truncate text-sm text-ink sm:block">{user?.displayName ?? 'Admin'}</span>
        </button>

        {menuOpen && (
          <>
            <button type="button" className="fixed inset-0 z-10 cursor-default" aria-label="Đóng menu" onClick={() => setMenuOpen(false)} />
            <div className="absolute right-0 top-full z-20 mt-2 w-60 rounded-lg border border-border bg-panel p-1.5 shadow-xl" role="menu">
              <div className="border-b border-border px-3 py-2">
                <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-ink-dim">
                  <ShieldCheck size={13} /> Quản trị viên
                </p>
                <p className="mt-1 truncate text-sm text-ink">{user?.displayName}</p>
                <p className="truncate text-xs text-ink-muted">
                  {user?.userId} · {user?.role}
                </p>
              </div>
              <button
                type="button"
                role="menuitem"
                onClick={() => {
                  setMenuOpen(false);
                  logout();
                }}
                className="mt-1 flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm text-ink-muted transition-colors hover:bg-panel-2 hover:text-ink"
              >
                <LogOut size={15} />
                Đăng xuất
              </button>
            </div>
          </>
        )}
      </div>
    </header>
  );
}
