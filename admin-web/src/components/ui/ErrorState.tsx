// Vai trò file: Trạng thái lỗi có nút thử lại. Phân biệt lỗi quyền (403) với lỗi hệ thống.

import { AlertTriangle, Lock, RotateCw } from 'lucide-react';
import { ApiError } from '../../lib/api';
import { Button } from './Button';

interface ErrorStateProps {
  error: unknown;
  onRetry?: () => void;
  title?: string;
}

export function ErrorState({ error, onRetry, title }: ErrorStateProps) {
  const apiError = error instanceof ApiError ? error : null;
  const denied = apiError?.isPermission ?? false;
  const message = error instanceof Error ? error.message : 'Đã xảy ra lỗi không xác định.';

  return (
    <div className="flex flex-col items-center gap-3 px-6 py-10 text-center">
      <div className={`rounded-full border p-3 ${denied ? 'border-info/40 bg-info-bg text-info' : 'border-danger/40 bg-danger-bg text-danger'}`}>
        {denied ? <Lock size={20} /> : <AlertTriangle size={20} />}
      </div>
      <div>
        <p className="text-sm font-semibold text-ink">{title ?? (denied ? 'Bạn không có quyền xem phần này' : 'Không tải được dữ liệu')}</p>
        <p className="mx-auto mt-1 max-w-lg text-sm text-ink-muted">{message}</p>
        {apiError?.code && <p className="mt-1 font-mono text-xs text-ink-dim">{apiError.code}</p>}
      </div>
      {onRetry && !denied && (
        <Button variant="secondary" icon={<RotateCw size={15} />} onClick={onRetry}>
          Thử lại
        </Button>
      )}
    </div>
  );
}
