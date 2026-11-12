// Vai trò file: Toast báo kết quả thao tác (thành công / lỗi). Tự ẩn sau vài giây.

import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { CheckCircle2, XCircle, X } from 'lucide-react';

type ToastTone = 'ok' | 'danger';

interface Toast {
  id: number;
  tone: ToastTone;
  message: string;
}

interface ToastContextValue {
  notify: (message: string, tone?: ToastTone) => void;
}

const ToastContext = createContext<ToastContextValue | null>(null);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);

  const notify = useCallback((message: string, tone: ToastTone = 'ok') => {
    const id = Date.now() + Math.random();
    setToasts(current => [...current, { id, tone, message }]);
    window.setTimeout(() => setToasts(current => current.filter(x => x.id !== id)), 5000);
  }, []);

  const dismiss = useCallback((id: number) => setToasts(current => current.filter(x => x.id !== id)), []);
  const value = useMemo(() => ({ notify }), [notify]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="pointer-events-none fixed bottom-4 right-4 z-50 flex w-[min(420px,calc(100vw-2rem))] flex-col gap-2" role="status" aria-live="polite">
        {toasts.map(toast => (
          <div
            key={toast.id}
            className={`pointer-events-auto flex items-start gap-2.5 rounded-lg border px-4 py-3 text-sm shadow-xl ${
              toast.tone === 'ok' ? 'border-ok/40 bg-ok-bg text-ok' : 'border-danger/40 bg-danger-bg text-danger'
            }`}
          >
            {toast.tone === 'ok' ? <CheckCircle2 size={17} className="mt-0.5 shrink-0" /> : <XCircle size={17} className="mt-0.5 shrink-0" />}
            <span className="flex-1 leading-snug">{toast.message}</span>
            <button type="button" onClick={() => dismiss(toast.id)} aria-label="Đóng thông báo" className="shrink-0 opacity-70 hover:opacity-100">
              <X size={15} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

// useToast: Hook gửi thông báo.
export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) throw new Error('useToast must be used inside ToastProvider.');
  return context;
}
