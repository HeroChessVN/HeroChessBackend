// Vai trò file: Bảng định tuyến của AdminWeb. Mỗi route gắn miền quyền để ẩn/hiện theo tài khoản admin.

import { HashRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './auth/AuthContext';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { AppLayout } from './components/layout/AppLayout';
import { ToastProvider } from './components/ui/Toast';
import { LoginPage } from './pages/LoginPage';
import { ForbiddenPage } from './pages/ForbiddenPage';
import { NotFoundPage } from './pages/NotFoundPage';
import { DashboardPage } from './pages/DashboardPage';
import { HeroesPage } from './pages/HeroesPage';
import { ShopPage } from './pages/ShopPage';
import { PlayersPage } from './pages/PlayersPage';
import { GiftCodesPage } from './pages/GiftCodesPage';
import { MatchesPage } from './pages/MatchesPage';
import { AnalyticsPage } from './pages/AnalyticsPage';
import { AdminAccountsPage } from './pages/AdminAccountsPage';
import { AuditPage } from './pages/AuditPage';
import {
  PERMISSION_ADMIN,
  PERMISSION_ANALYTICS,
  PERMISSION_AUDIT,
  PERMISSION_GIFTCODE,
  PERMISSION_HERO,
  PERMISSION_PLAYER,
  PERMISSION_READ,
  PERMISSION_SHOP
} from './lib/permissions';

export function App() {
  return (
    // HashRouter: backend phục vụ static file không có SPA fallback, nên deep-link kiểu /admin/heroes
    // sẽ trả 404. Hash router giữ console chạy được cả khi serve từ wwwroot mà không cần sửa backend.
    <HashRouter>
      <AuthProvider>
        <ToastProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route
              element={
                <ProtectedRoute>
                  <AppLayout />
                </ProtectedRoute>
              }
            >
              <Route
                index
                element={
                  <ProtectedRoute domain={PERMISSION_READ}>
                    <DashboardPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="heroes"
                element={
                  <ProtectedRoute domain={PERMISSION_HERO}>
                    <HeroesPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="shop"
                element={
                  <ProtectedRoute domain={PERMISSION_SHOP}>
                    <ShopPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="players"
                element={
                  <ProtectedRoute domain={PERMISSION_PLAYER}>
                    <PlayersPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="gift-codes"
                element={
                  <ProtectedRoute domain={PERMISSION_GIFTCODE}>
                    <GiftCodesPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="matches"
                element={
                  <ProtectedRoute domain={PERMISSION_ANALYTICS}>
                    <MatchesPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="analytics"
                element={
                  <ProtectedRoute domain={PERMISSION_ANALYTICS}>
                    <AnalyticsPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="admin"
                element={
                  <ProtectedRoute domain={PERMISSION_ADMIN}>
                    <AdminAccountsPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="audit"
                element={
                  <ProtectedRoute domain={PERMISSION_AUDIT}>
                    <AuditPage />
                  </ProtectedRoute>
                }
              />
              <Route path="403" element={<ForbiddenPage />} />
            </Route>
            <Route path="*" element={<NotFoundPage />} />
          </Routes>
        </ToastProvider>
      </AuthProvider>
    </HashRouter>
  );
}

// Outlet được dùng bên trong AppLayout để hiển thị route con.
