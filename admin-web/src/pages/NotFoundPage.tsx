// Vai trò file: Trang 404 — đường dẫn không tồn tại trong console.

import { Link } from 'react-router-dom';
import { Compass } from 'lucide-react';

export function NotFoundPage() {
  return (
    <div className="grid min-h-screen place-items-center bg-surface px-4">
      <div className="max-w-md text-center">
        <div className="mx-auto grid size-14 place-items-center rounded-full border border-border bg-panel-2 text-gold-500">
          <Compass size={24} />
        </div>
        <h1 className="mt-4 text-lg font-semibold text-ink">Không tìm thấy trang</h1>
        <p className="mt-2 text-sm text-ink-muted">Đường dẫn này không có trong Game Operations Console.</p>
        <Link to="/" className="mt-5 inline-block rounded-lg border border-border bg-panel-2 px-4 py-2 text-sm text-ink transition-colors hover:bg-navy-700">
          Về Dashboard
        </Link>
      </div>
    </div>
  );
}
