// Vai trò file: Trang đăng nhập admin. Gọi POST /api/v1/auth/login?useCookies=false và lưu phiên.

import { useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { LogIn, ShieldAlert } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { ApiError } from '../lib/api';
import { Button } from '../components/ui/Button';

export function LoginPage() {
  const { authenticated, ready, login } = useAuth();
  const location = useLocation();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  if (ready && authenticated) {
    const from = (location.state as { from?: string } | null)?.from;
    return <Navigate to={from ?? '/'} replace />;
  }

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await login(email.trim(), password);
    } catch (cause) {
      setError(cause instanceof ApiError ? cause.message : 'Không thể đăng nhập. Vui lòng thử lại.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="grid min-h-screen place-items-center bg-surface px-4 py-10">
      <div className="w-full max-w-md">
        <div className="mb-6 text-center">
          <span className="mx-auto grid size-14 place-items-center rounded-2xl bg-gold-500/15 text-3xl text-gold-500" aria-hidden>
            ♞
          </span>
          <h1 className="mt-4 text-xl font-bold tracking-tight text-ink">Hero Chess Operations</h1>
          <p className="mt-1 text-sm text-ink-muted">Đăng nhập bằng tài khoản quản trị viên</p>
        </div>

        <form className="card p-6" onSubmit={submit}>
          {error && (
            <div className="mb-4 flex items-start gap-2 rounded-lg border border-danger/40 bg-danger-bg px-3 py-2.5 text-sm text-danger" role="alert">
              <ShieldAlert size={16} className="mt-0.5 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          <div className="space-y-4">
            <div>
              <label className="field-label" htmlFor="email">
                Email
              </label>
              <input
                id="email"
                type="email"
                className="input"
                value={email}
                onChange={event => setEmail(event.target.value)}
                autoComplete="username"
                required
                autoFocus
              />
            </div>
            <div>
              <label className="field-label" htmlFor="password">
                Mật khẩu
              </label>
              <input
                id="password"
                type="password"
                className="input"
                value={password}
                onChange={event => setPassword(event.target.value)}
                autoComplete="current-password"
                required
              />
            </div>
          </div>

          <Button type="submit" variant="primary" className="mt-6 w-full" loading={loading} icon={<LogIn size={16} />}>
            Đăng nhập
          </Button>
        </form>

        <p className="mt-4 text-center text-xs text-ink-dim">
          Phiên đăng nhập chỉ tồn tại trong tab này. Mọi thao tác quản trị đều được ghi nhật ký.
        </p>
      </div>
    </div>
  );
}
