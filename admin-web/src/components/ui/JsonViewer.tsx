// Vai trò file: Trình xem JSON an toàn. Dùng cho request/events của match action (jsonb thô của backend).
// Render dạng text trong <pre> — không dùng innerHTML, nên không thể chèn HTML từ dữ liệu.

import { useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';

interface JsonViewerProps {
  value: unknown;
  label?: string;
  defaultOpen?: boolean;
}

export function JsonViewer({ value, label, defaultOpen = true }: JsonViewerProps) {
  const [open, setOpen] = useState(defaultOpen);
  const text = safeStringify(value);

  return (
    <div className="rounded-lg border border-border bg-surface-2">
      <button type="button" onClick={() => setOpen(current => !current)} className="flex w-full items-center gap-1.5 px-3 py-2 text-left text-xs font-semibold text-ink-muted hover:text-ink">
        {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        {label ?? 'JSON'}
      </button>
      {open && <pre className="max-h-80 overflow-auto border-t border-border px-3 py-2 font-mono text-xs leading-relaxed text-ink-muted">{text}</pre>}
    </div>
  );
}

// safeStringify: JSON.stringify có thể ném lỗi với BigInt hoặc vòng lặp — trả về thông báo thay vì crash.
function safeStringify(value: unknown): string {
  try {
    return JSON.stringify(value, (_key, item) => (typeof item === 'bigint' ? item.toString() : item), 2) ?? 'null';
  } catch {
    return '(không đọc được nội dung)';
  }
}
