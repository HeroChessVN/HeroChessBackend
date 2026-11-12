// Vai trò file: Drawer trượt bên phải dùng cho chi tiết Hero và các hồ sơ chi tiết.

import { useEffect } from 'react';
import type { ReactNode } from 'react';
import { X } from 'lucide-react';

interface DrawerProps {
  open: boolean;
  title: ReactNode;
  subtitle?: ReactNode;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  width?: string;
}

export function Drawer({ open, title, subtitle, onClose, children, footer, width = 'max-w-2xl' }: DrawerProps) {
  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    // Khoá cuộn nền khi drawer mở để tránh cuộn trang phía sau.
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = previous;
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-navy-950/70" role="dialog" aria-modal="true">
      <button type="button" className="flex-1 cursor-default" aria-label="Đóng" onClick={onClose} />
      <aside className={`flex h-full w-full ${width} flex-col border-l border-border bg-panel shadow-2xl`}>
        <header className="flex items-start justify-between gap-3 border-b border-border px-5 py-4">
          <div className="min-w-0">
            <h2 className="truncate text-base font-semibold text-ink">{title}</h2>
            {subtitle && <p className="mt-0.5 text-sm text-ink-muted">{subtitle}</p>}
          </div>
          <button type="button" onClick={onClose} className="rounded-lg p-1.5 text-ink-muted transition-colors hover:bg-panel-2 hover:text-ink" aria-label="Đóng">
            <X size={18} />
          </button>
        </header>
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {footer && <footer className="border-t border-border px-5 py-3">{footer}</footer>}
      </aside>
    </div>
  );
}
