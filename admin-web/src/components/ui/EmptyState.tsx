// Vai trò file: Trạng thái rỗng — không có dữ liệu trong bộ lọc hiện tại (không phải lỗi).

import type { ReactNode } from 'react';
import { Inbox } from 'lucide-react';

interface EmptyStateProps {
  title: string;
  description?: ReactNode;
  action?: ReactNode;
  icon?: ReactNode;
}

export function EmptyState({ title, description, action, icon }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 px-6 py-12 text-center">
      <div className="rounded-full border border-border bg-panel-2 p-3 text-ink-dim">{icon ?? <Inbox size={20} />}</div>
      <div>
        <p className="text-sm font-semibold text-ink">{title}</p>
        {description && <p className="mx-auto mt-1 max-w-md text-sm text-ink-muted">{description}</p>}
      </div>
      {action}
    </div>
  );
}
