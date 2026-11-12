// Vai trò file: Khung ứng dụng — sidebar cố định (desktop) / overlay (tablet), header và vùng nội dung.

import { useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../../auth/AuthContext';
import { Sidebar } from './Sidebar';
import { Header } from './Header';

export function AppLayout() {
  const { permissions } = useAuth();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const location = useLocation();

  return (
    <div className="flex min-h-screen bg-surface">
      {/* Sidebar cố định trên desktop */}
      <aside className="hidden w-64 shrink-0 border-r border-navy-700 lg:block">
        <div className="sticky top-0 h-screen">
          <Sidebar permissions={permissions} />
        </div>
      </aside>

      {/* Sidebar overlay cho tablet/mobile */}
      {sidebarOpen && (
        <div className="fixed inset-0 z-40 flex lg:hidden">
          <button type="button" className="flex-1 cursor-default bg-navy-950/70" aria-label="Đóng menu" onClick={() => setSidebarOpen(false)} />
          <div className="w-64 shrink-0 border-r border-navy-700">
            <Sidebar permissions={permissions} onNavigate={() => setSidebarOpen(false)} />
          </div>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <Header title={titleFor(location.pathname)} onOpenSidebar={() => setSidebarOpen(true)} />
        <main className="flex-1 px-4 py-5 sm:px-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

// titleFor: Tiêu đề header theo đường dẫn hiện tại.
function titleFor(pathname: string): string {
  if (pathname.startsWith('/heroes')) return 'Quản lý Hero';
  if (pathname.startsWith('/shop')) return 'Quản lý Cửa hàng';
  if (pathname.startsWith('/players')) return 'Quản lý Người chơi';
  if (pathname.startsWith('/gift-codes')) return 'Quản lý Gift Code';
  if (pathname.startsWith('/matches')) return 'Trình xem Trận đấu';
  if (pathname.startsWith('/analytics')) return 'Phân tích';
  if (pathname.startsWith('/admin')) return 'Tài khoản Admin';
  if (pathname.startsWith('/audit')) return 'Nhật ký thao tác';
  return 'Tổng quan';
}
