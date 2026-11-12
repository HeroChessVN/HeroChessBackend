// Vai trò file: Màn Tài khoản Admin — danh sách admin, phân quyền fine-grained và tạo tài khoản mới.
// Đổi quyền thay toàn bộ danh sách quyền; luôn có hộp thoại xác nhận trước khi ghi.

import { useState } from 'react';
import { KeyRound, RefreshCw, ShieldCheck, UserPlus } from 'lucide-react';
import { api } from '../lib/api';
import { useResource } from '../lib/useResource';
import { useAuth } from '../auth/AuthContext';
import { canWrite, PERMISSION_ADMIN, PERMISSION_WILDCARD } from '../lib/permissions';
import type { AdminAccountDto, AdminAccountPageDto, AdminPermissionDto } from '../lib/types';
import { formatDateTime } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge, statusTone } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { Field } from '../components/ui/Field';
import { TableSkeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';
import { Drawer } from '../components/ui/Drawer';
import { ConfirmDialog } from '../components/ui/ConfirmDialog';
import { useToast } from '../components/ui/Toast';

export function AdminAccountsPage() {
  const { permissions } = useAuth();
  const writable = canWrite(permissions, PERMISSION_ADMIN);
  const { notify } = useToast();

  const accounts = useResource<AdminAccountPageDto>(signal => api.get('/admin/accounts', signal), []);
  const catalog = useResource<AdminPermissionDto[]>(signal => api.get('/admin/permissions', signal), []);

  const [editing, setEditing] = useState<AdminAccountDto | null>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const [wildcard, setWildcard] = useState(false);
  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState(false);

  const openPermissions = (account: AdminAccountDto) => {
    setEditing(account);
    setWildcard(account.permissions.includes(PERMISSION_WILDCARD));
    setSelected(account.permissions.filter(item => item !== PERMISSION_WILDCARD));
  };

  const savePermissions = async () => {
    if (!editing) return;
    if (!wildcard && selected.length === 0) {
      notify('Chọn ít nhất một quyền, hoặc bật "toàn quyền".', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.put(`/admin/accounts/${editing.id}/permissions`, {
        permissions: wildcard ? [PERMISSION_WILDCARD] : selected
      });
      notify(`Đã cập nhật quyền của ${editing.displayName}.`);
      setEditing(null);
      accounts.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không đổi được quyền.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-5">
      <Card
        title={
          <span className="flex items-center gap-2">
            <ShieldCheck size={18} className="text-gold-500" />
            Tài khoản quản trị
          </span>
        }
        description="Không có dòng quyền = toàn quyền (tương thích ngược). Đổi quyền thay toàn bộ danh sách."
        actions={
          <>
            <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={accounts.reload}>
              Làm mới
            </Button>
            {writable && (
              <Button variant="primary" icon={<UserPlus size={16} />} onClick={() => setCreating(true)}>
                Tạo tài khoản
              </Button>
            )}
          </>
        }
      >
        {accounts.error ? (
          <ErrorState error={accounts.error} onRetry={accounts.reload} />
        ) : accounts.loading ? (
          <TableSkeleton rows={4} columns={4} />
        ) : !accounts.data || accounts.data.items.length === 0 ? (
          <EmptyState title="Chưa có tài khoản quản trị nào" />
        ) : (
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Tài khoản</th>
                  <th>Email</th>
                  <th>Quyền</th>
                  <th>Trạng thái</th>
                  <th className="text-right">Thao tác</th>
                </tr>
              </thead>
              <tbody>
                {accounts.data.items.map(account => (
                  <tr key={account.id}>
                    <td>
                      <p className="font-medium text-ink">{account.displayName}</p>
                      <p className="text-xs text-ink-dim">Tạo {formatDateTime(account.createdAt)}</p>
                    </td>
                    <td className="text-ink-muted">{account.email ?? '—'}</td>
                    <td>
                      <div className="flex flex-wrap gap-1.5">
                        {account.permissions.includes(PERMISSION_WILDCARD) ? (
                          <Badge tone="gold">Toàn quyền</Badge>
                        ) : (
                          account.permissions.map(permission => (
                            <Badge key={permission} tone="info">
                              {permission}
                            </Badge>
                          ))
                        )}
                      </div>
                    </td>
                    <td>
                      <Badge tone={statusTone(account.status)}>{account.status}</Badge>
                    </td>
                    <td className="text-right">
                      {writable && (
                        <Button variant="secondary" icon={<KeyRound size={14} />} onClick={() => openPermissions(account)}>
                          Phân quyền
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {creating && (
        <CreateAccountForm
          catalog={catalog.data ?? []}
          onClose={() => setCreating(false)}
          onSaved={() => accounts.reload()}
        />
      )}

      <ConfirmDialog
        open={editing !== null}
        title={`Phân quyền — ${editing?.displayName ?? ''}`}
        description="Danh sách quyền bị thay toàn bộ. Tài khoản mất quyền sẽ không truy cập được các miền tương ứng ngay."
        confirmLabel="Lưu quyền"
        tone="danger"
        loading={busy}
        onCancel={() => setEditing(null)}
        onConfirm={savePermissions}
      >
        <div className="space-y-3">
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={wildcard} onChange={event => setWildcard(event.target.checked)} />
            Toàn quyền (wildcard)
          </label>
          {!wildcard && (
            <div className="space-y-1.5">
              {(catalog.data ?? []).map(permission => (
                <label key={permission.key} className="flex items-start gap-2 text-sm text-ink">
                  <input
                    type="checkbox"
                    checked={selected.includes(permission.key)}
                    onChange={event =>
                      setSelected(current =>
                        event.target.checked ? [...current, permission.key] : current.filter(item => item !== permission.key)
                      )
                    }
                  />
                  <span>
                    <span className="font-mono text-xs text-gold-400">{permission.key}</span>
                    <span className="block text-xs text-ink-muted">{permission.description}</span>
                  </span>
                </label>
              ))}
            </div>
          )}
          <p className="text-xs text-ink-dim">
            Thay đổi được ghi vào nhật ký thao tác của backend (action <span className="font-mono">admin.permissions.update</span>).
          </p>
        </div>
      </ConfirmDialog>
    </div>
  );
}

// CreateAccountForm: Tạo tài khoản admin mới. Mật khẩu phải đạt luật Identity — server trả lỗi nếu không.
function CreateAccountForm({
  catalog,
  onClose,
  onSaved
}: {
  catalog: AdminPermissionDto[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const { notify } = useToast();
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [wildcard, setWildcard] = useState(false);
  const [selected, setSelected] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!email.trim() || !email.includes('@')) {
      notify('Email không hợp lệ.', 'danger');
      return;
    }
    if (displayName.trim().length === 0) {
      notify('Bắt buộc có tên hiển thị.', 'danger');
      return;
    }
    if (password.length === 0) {
      notify('Bắt buộc có mật khẩu.', 'danger');
      return;
    }
    if (!wildcard && selected.length === 0) {
      notify('Chọn ít nhất một quyền, hoặc bật "toàn quyền".', 'danger');
      return;
    }
    setSaving(true);
    try {
      await api.post('/admin/accounts', {
        email: email.trim(),
        displayName: displayName.trim(),
        password,
        permissions: wildcard ? [PERMISSION_WILDCARD] : selected
      });
      notify(`Đã tạo tài khoản admin ${displayName.trim()}.`);
      onSaved();
      onClose();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không tạo được tài khoản admin.', 'danger');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      open
      title="Tạo tài khoản admin"
      subtitle="Mật khẩu phải đạt luật Identity của hệ thống."
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose} disabled={saving}>
            Hủy
          </Button>
          <Button variant="primary" icon={<UserPlus size={15} />} onClick={submit} loading={saving}>
            Tạo tài khoản
          </Button>
        </div>
      }
    >
      <form className="space-y-4" onSubmit={submit}>
        <Field label="Email" required>
          {props => <input {...props} type="email" className="input" value={email} onChange={event => setEmail(event.target.value)} />}
        </Field>
        <Field label="Tên hiển thị" required>
          {props => <input {...props} className="input" value={displayName} onChange={event => setDisplayName(event.target.value)} />}
        </Field>
        <Field label="Mật khẩu" required hint="Không gửi lên server sau khi lưu — hệ thống tự hash.">
          {props => <input {...props} type="password" className="input" value={password} onChange={event => setPassword(event.target.value)} />}
        </Field>

        <label className="flex items-center gap-2 text-sm text-ink">
          <input type="checkbox" checked={wildcard} onChange={event => setWildcard(event.target.checked)} />
          Toàn quyền (wildcard)
        </label>
        {!wildcard && (
          <div className="space-y-1.5">
            {catalog.map(permission => (
              <label key={permission.key} className="flex items-start gap-2 text-sm text-ink">
                <input
                  type="checkbox"
                  checked={selected.includes(permission.key)}
                  onChange={event =>
                    setSelected(current =>
                      event.target.checked ? [...current, permission.key] : current.filter(item => item !== permission.key)
                    )
                  }
                />
                <span>
                  <span className="font-mono text-xs text-gold-400">{permission.key}</span>
                  <span className="block text-xs text-ink-muted">{permission.description}</span>
                </span>
              </label>
            ))}
          </div>
        )}
      </form>
    </Drawer>
  );
}
