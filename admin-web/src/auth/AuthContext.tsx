// Vai trò file: Nguồn sự thật về phiên đăng nhập. Cung cấp user, quyền, hàm login/logout và refresh token.

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import * as api from '../lib/api';
import { clearSession, readSession, toSession, writeSession, type Session } from '../lib/session';
import { parsePermissions } from '../lib/permissions';
import type { MeDto } from '../lib/types';

interface AuthContextValue {
  ready: boolean;
  authenticated: boolean;
  user: MeDto | null;
  permissions: ReturnType<typeof parsePermissions>;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<Session>(() => readSession());
  const [ready, setReady] = useState(false);
  const refreshInFlight = useRef<Promise<string | null> | null>(null);

  const logout = useCallback(() => {
    clearSession();
    api.setAccessToken('');
    setSession(readSession());
  }, []);

  // loadProfile: Lấy /api/v1/me và quyền của tài khoản.
  // Quyền lấy từ GET /api/v1/admin/accounts (nguồn duy nhất hiện có) — endpoint cần quyền 'admin',
  // nên admin bị hạn chế sẽ nhận 403 và quyền được để null (chưa xác định) thay vì chặn cả giao diện.
  const loadProfile = useCallback(async (accessToken: string): Promise<{ user: MeDto | null; permissions: ReturnType<typeof parsePermissions> }> => {
    api.setAccessToken(accessToken);
    let user: MeDto | null = null;
    try {
      user = await api.api.get<MeDto>('/me');
    } catch {
      user = null;
    }
    let permissions: ReturnType<typeof parsePermissions> = null;
    try {
      const accounts = await api.api.get<{ items: { id: string; permissions: string[] }[] }>('/admin/accounts');
      const mine = accounts.items.find(x => x.id === user?.userId);
      permissions = parsePermissions(mine?.permissions ?? null);
    } catch {
      permissions = null;
    }
    return { user, permissions };
  }, []);

  const refresh = useCallback(async (): Promise<string | null> => {
    if (refreshInFlight.current) return refreshInFlight.current;
    const current = readSession();
    if (!current.refreshToken) return null;
    const task = (async () => {
      try {
        const tokens = await api.refresh(current.refreshToken);
        api.setAccessToken(tokens.accessToken);
        const next = toSession(tokens, current.user, current.permissions);
        writeSession(next);
        setSession(next);
        return tokens.accessToken;
      } catch {
        logout();
        return null;
      } finally {
        refreshInFlight.current = null;
      }
    })();
    refreshInFlight.current = task;
    return task;
  }, [logout]);

  // configureAuth: Gắn client HTTP với phiên hiện tại.
  useEffect(() => {
    api.configureAuth({
      getAccessToken: () => readSession().accessToken,
      onUnauthorized: logout,
      refresh
    });
  }, [logout, refresh]);

  // bootstrap: Khôi phục phiên từ sessionStorage và làm mới token nếu cần.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const stored = readSession();
      if (!stored.accessToken) {
        setReady(true);
        return;
      }
      api.setAccessToken(stored.accessToken);
      try {
        const profile = await loadProfile(stored.accessToken);
        if (cancelled) return;
        const next = { ...stored, user: profile.user, permissions: profile.permissions };
        writeSession(next);
        setSession(next);
      } catch {
        if (!cancelled) logout();
      } finally {
        if (!cancelled) setReady(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [loadProfile, logout]);

  const signIn = useCallback(
    async (email: string, password: string) => {
      const tokens = await api.login(email, password);
      api.setAccessToken(tokens.accessToken);
      const profile = await loadProfile(tokens.accessToken);
      const next = toSession(tokens, profile.user, profile.permissions);
      writeSession(next);
      setSession(next);
    },
    [loadProfile]
  );

  const value = useMemo<AuthContextValue>(
    () => ({
      ready,
      authenticated: session.accessToken !== '',
      user: session.user,
      permissions: session.permissions,
      login: signIn,
      logout
    }),
    [ready, session, signIn, logout]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

// useAuth: Hook truy cập phiên đăng nhập.
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider.');
  return context;
}
