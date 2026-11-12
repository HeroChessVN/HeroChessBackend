// Vai trò file: Màn Gift Code — danh sách mã, tạo mã mới, bật/tắt, sửa hạn dùng và lịch sử quy đổi.
// Mã để trống thì backend tự sinh 12 ký tự; mọi thao tác ghi đều cần `reason`.

import { useCallback, useEffect, useState } from 'react';
import { Copy, Gift, Plus, RefreshCw, Search, Ticket } from 'lucide-react';
import { api, buildQuery } from '../lib/api';
import { usePagedResource } from '../lib/useResource';
import { useAuth } from '../auth/AuthContext';
import { canWrite, PERMISSION_GIFTCODE } from '../lib/permissions';
import type {
  AdminGiftCodeDto,
  AdminGiftCodePageDto,
  AdminGiftCodeRedemptionPageDto,
  AdminHeroPageDto
} from '../lib/types';
import { formatDateTime, formatNumber } from '../lib/format';
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

interface Filters {
  query: string;
  active: '' | 'true' | 'false';
}

const emptyFilters: Filters = { query: '', active: '' };

type RewardKind = 'coin' | 'hero' | 'cosmetic';

export function GiftCodesPage() {
  const { permissions } = useAuth();
  const writable = canWrite(permissions, PERMISSION_GIFTCODE);
  const { notify } = useToast();

  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);

  const [creating, setCreating] = useState(false);
  const [toggling, setToggling] = useState<AdminGiftCodeDto | null>(null);
  const [reason, setReason] = useState('');
  const [editingLimits, setEditingLimits] = useState<AdminGiftCodeDto | null>(null);
  const [maxRedemptions, setMaxRedemptions] = useState('0');
  const [maxPerPlayer, setMaxPerPlayer] = useState('1');
  const [busy, setBusy] = useState(false);

  const [redemptionsFor, setRedemptionsFor] = useState<AdminGiftCodeDto | null>(null);

  const load = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      api.get<AdminGiftCodePageDto>(
        `/admin/gift-codes${buildQuery({
          query: applied.query,
          active: applied.active === '' ? undefined : applied.active,
          cursor: cursor ?? undefined,
          limit: 50
        })}`,
        signal
      ),
    [applied]
  );

  const codes = usePagedResource(load, [applied]);

  const toggleActive = async () => {
    if (!toggling) return;
    if (reason.trim().length === 0) {
      notify('Bắt buộc có lý do khi bật/tắt gift code.', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.patch(`/admin/gift-codes/${toggling.id}`, { isActive: !toggling.isActive, reason: reason.trim() });
      notify(`Đã ${toggling.isActive ? 'tắt' : 'bật'} gift code ${toggling.code}.`);
      setToggling(null);
      setReason('');
      codes.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không đổi được trạng thái gift code.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const saveLimits = async () => {
    if (!editingLimits) return;
    if (!/^\d+$/.test(maxRedemptions) || Number(maxRedemptions) <= 0) {
      notify('Tổng lượt quy đổi phải là số nguyên dương.', 'danger');
      return;
    }
    if (!/^\d+$/.test(maxPerPlayer) || Number(maxPerPlayer) <= 0) {
      notify('Số lượt mỗi người chơi phải là số nguyên dương.', 'danger');
      return;
    }
    if (reason.trim().length === 0) {
      notify('Bắt buộc có lý do khi sửa cấu hình gift code.', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.patch(`/admin/gift-codes/${editingLimits.id}`, {
        maxRedemptions: Number(maxRedemptions),
        maxPerPlayer: Number(maxPerPlayer),
        reason: reason.trim()
      });
      notify(`Đã cập nhật cấu hình gift code ${editingLimits.code}.`);
      setEditingLimits(null);
      setReason('');
      codes.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không sửa được cấu hình gift code.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-5">
      <Card
        title={
          <span className="flex items-center gap-2">
            <Gift size={18} className="text-gold-500" />
            Danh sách gift code
          </span>
        }
        description={`${formatNumber(codes.total)} mã · để trống mã khi tạo để backend tự sinh mã 12 ký tự.`}
        actions={
          <>
            <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={codes.reload}>
              Làm mới
            </Button>
            {writable && (
              <Button variant="primary" icon={<Plus size={16} />} onClick={() => setCreating(true)}>
                Tạo gift code
              </Button>
            )}
          </>
        }
      >
        <form
          className="mb-4 grid gap-3 sm:grid-cols-3"
          onSubmit={event => {
            event.preventDefault();
            setApplied(filters);
          }}
        >
          <div>
            <label className="field-label" htmlFor="gift-search">
              Tìm kiếm
            </label>
            <div className="relative">
              <Search size={15} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-dim" />
              <input
                id="gift-search"
                className="input pl-8 font-mono"
                placeholder="Mã gift code"
                value={filters.query}
                onChange={event => setFilters(current => ({ ...current, query: event.target.value }))}
              />
            </div>
          </div>
          <div>
            <label className="field-label" htmlFor="gift-active">
              Trạng thái
            </label>
            <select
              id="gift-active"
              className="input"
              value={filters.active}
              onChange={event => setFilters(current => ({ ...current, active: event.target.value as Filters['active'] }))}
            >
              <option value="">Tất cả</option>
              <option value="true">Đang bật</option>
              <option value="false">Đang tắt</option>
            </select>
          </div>
          <div className="flex items-end gap-2">
            <Button type="submit" variant="primary" className="flex-1">
              Lọc
            </Button>
            <Button
              type="button"
              variant="ghost"
              onClick={() => {
                setFilters(emptyFilters);
                setApplied(emptyFilters);
              }}
            >
              Xoá
            </Button>
          </div>
        </form>

        {codes.error ? (
          <ErrorState error={codes.error} onRetry={codes.reload} />
        ) : codes.loading ? (
          <TableSkeleton rows={6} columns={6} />
        ) : codes.items.length === 0 ? (
          <EmptyState
            title="Chưa có gift code nào"
            description="Tạo mã đầu tiên để phát thưởng cho người chơi."
            action={
              writable ? (
                <Button variant="secondary" icon={<Plus size={15} />} onClick={() => setCreating(true)}>
                  Tạo gift code
                </Button>
              ) : undefined
            }
          />
        ) : (
          <>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Mã</th>
                    <th>Phần thưởng</th>
                    <th className="text-right">Lượt quy đổi</th>
                    <th className="text-right">Mỗi người</th>
                    <th>Hạn dùng</th>
                    <th>Trạng thái</th>
                    <th className="text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {codes.items.map(code => (
                    <tr key={code.id}>
                      <td className="font-mono text-ink">{code.code}</td>
                      <td className="text-ink-muted">
                        {code.rewardKind === 'coin' ? `${formatNumber(code.rewardQuantity)} coin` : `${code.rewardKind} ×${formatNumber(code.rewardQuantity)}`}
                      </td>
                      <td className="text-right tabular-nums text-ink">
                        {formatNumber(code.redemptionCount)}/{formatNumber(code.maxRedemptions)}
                      </td>
                      <td className="text-right tabular-nums text-ink-muted">{formatNumber(code.maxPerPlayer)}</td>
                      <td className="whitespace-nowrap text-ink-muted">
                        {code.expiresAt ? formatDateTime(code.expiresAt) : 'Không hết hạn'}
                      </td>
                      <td>
                        <Badge tone={statusTone(code.isActive ? 'active' : 'disabled')}>
                          {code.isActive ? 'Đang bật' : 'Đã tắt'}
                        </Badge>
                      </td>
                      <td className="text-right">
                        <div className="flex justify-end gap-1.5">
                          <Button variant="ghost" onClick={() => setRedemptionsFor(code)}>
                            Lịch sử
                          </Button>
                          {writable && (
                            <>
                              <Button
                                variant="secondary"
                                onClick={() => {
                                  setEditingLimits(code);
                                  setMaxRedemptions(String(code.maxRedemptions));
                                  setMaxPerPlayer(String(code.maxPerPlayer));
                                  setReason('');
                                }}
                              >
                                Hạn dùng
                              </Button>
                              <Button variant={code.isActive ? 'danger' : 'secondary'} onClick={() => setToggling(code)}>
                                {code.isActive ? 'Tắt' : 'Bật'}
                              </Button>
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {codes.nextCursor && (
              <div className="mt-4 flex justify-center">
                <Button variant="secondary" onClick={codes.loadMore} loading={codes.loadingMore}>
                  Tải thêm
                </Button>
              </div>
            )}
          </>
        )}
      </Card>

      {creating && <GiftCodeForm onClose={() => setCreating(false)} onSaved={() => codes.reload()} />}

      {redemptionsFor && (
        <RedemptionsDrawer code={redemptionsFor} onClose={() => setRedemptionsFor(null)} />
      )}

      <ConfirmDialog
        open={toggling !== null}
        title={toggling?.isActive ? `Tắt gift code ${toggling?.code ?? ''}` : `Bật gift code ${toggling?.code ?? ''}`}
        description="Mã bị tắt sẽ không quy đổi được nữa."
        confirmLabel={toggling?.isActive ? 'Tắt gift code' : 'Bật gift code'}
        tone={toggling?.isActive ? 'danger' : 'primary'}
        loading={busy}
        onCancel={() => setToggling(null)}
        onConfirm={toggleActive}
      >
        <label className="field-label" htmlFor="gift-toggle-reason">
          Lý do <span className="text-danger">*</span>
        </label>
        <textarea
          id="gift-toggle-reason"
          className="input min-h-20"
          value={reason}
          onChange={event => setReason(event.target.value)}
          placeholder="Ví dụ: dừng phát do lỗi cấu hình phần thưởng"
        />
      </ConfirmDialog>

      <ConfirmDialog
        open={editingLimits !== null}
        title={`Hạn dùng — ${editingLimits?.code ?? ''}`}
        description="Không cho giảm tổng lượt quy đổi xuống dưới số lượt đã quy đổi (server trả 409)."
        confirmLabel="Lưu"
        tone="primary"
        loading={busy}
        onCancel={() => setEditingLimits(null)}
        onConfirm={saveLimits}
      >
        <div className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-2">
            <Field label="Tổng lượt quy đổi" required>
              {props => (
                <input {...props} className="input tabular-nums" value={maxRedemptions} onChange={event => setMaxRedemptions(event.target.value)} />
              )}
            </Field>
            <Field label="Số lượt mỗi người chơi" required>
              {props => (
                <input {...props} className="input tabular-nums" value={maxPerPlayer} onChange={event => setMaxPerPlayer(event.target.value)} />
              )}
            </Field>
          </div>
          <Field label="Lý do" required>
            {props => <textarea {...props} className="input min-h-20" value={reason} onChange={event => setReason(event.target.value)} />}
          </Field>
        </div>
      </ConfirmDialog>
    </div>
  );
}

// GiftCodeForm: Form tạo gift code. Chỉ có loại "coin" dùng được ngay — chọn hero/cosmetic cần nhập id,
// vì backend không có endpoint liệt kê cosmetic cho admin.
function GiftCodeForm({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const { notify } = useToast();
  const [code, setCode] = useState('');
  const [kind, setKind] = useState<RewardKind>('coin');
  const [quantity, setQuantity] = useState('500');
  const [heroId, setHeroId] = useState('');
  const [cosmeticId, setCosmeticId] = useState('');
  const [startsAt, setStartsAt] = useState('');
  const [expiresAt, setExpiresAt] = useState('');
  const [maxRedemptions, setMaxRedemptions] = useState('1000');
  const [maxPerPlayer, setMaxPerPlayer] = useState('1');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [heroes, setHeroes] = useState<{ id: string; name: string; code: string }[]>([]);

  useEffect(() => {
    // Danh mục hero cần quyền 'hero'; thiếu quyền thì chỉ tạo được gift code loại coin.
    api
      .get<AdminHeroPageDto>('/admin/heroes?limit=200')
      .then(page => setHeroes(page.items.map(hero => ({ id: hero.id, name: hero.name, code: hero.code }))))
      .catch(() => setHeroes([]));
  }, []);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!/^\d+$/.test(quantity) || Number(quantity) <= 0) {
      notify('Số lượng phần thưởng phải là số nguyên dương.', 'danger');
      return;
    }
    if (!/^\d+$/.test(maxRedemptions) || Number(maxRedemptions) <= 0) {
      notify('Tổng lượt quy đổi phải là số nguyên dương.', 'danger');
      return;
    }
    if (!/^\d+$/.test(maxPerPlayer) || Number(maxPerPlayer) <= 0) {
      notify('Số lượt mỗi người chơi phải là số nguyên dương.', 'danger');
      return;
    }
    if (kind === 'hero' && heroId === '') {
      notify('Chọn hero cần trao thưởng.', 'danger');
      return;
    }
    if (kind === 'cosmetic' && cosmeticId.trim() === '') {
      notify('Nhập id cosmetic cần trao thưởng.', 'danger');
      return;
    }
    if (reason.trim().length === 0) {
      notify('Bắt buộc có lý do khi tạo gift code.', 'danger');
      return;
    }
    setSaving(true);
    try {
      const created = await api.post<AdminGiftCodeDto>('/admin/gift-codes', {
        code: code.trim() || null,
        rewardKind: kind,
        rewardQuantity: Number(quantity),
        rewardHeroId: kind === 'hero' ? heroId : null,
        rewardCosmeticId: kind === 'cosmetic' ? cosmeticId.trim() : null,
        startsAt: startsAt ? new Date(startsAt).toISOString() : null,
        expiresAt: expiresAt ? new Date(expiresAt).toISOString() : null,
        maxRedemptions: Number(maxRedemptions),
        maxPerPlayer: Number(maxPerPlayer),
        isActive: true,
        reason: reason.trim()
      });
      notify(`Đã tạo gift code ${created.code}.`);
      onSaved();
      onClose();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không tạo được gift code.', 'danger');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      open
      title="Tạo gift code"
      subtitle="Để trống mã để backend tự sinh mã 12 ký tự."
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose} disabled={saving}>
            Hủy
          </Button>
          <Button variant="primary" icon={<Plus size={15} />} onClick={submit} loading={saving}>
            Tạo gift code
          </Button>
        </div>
      }
    >
      <form className="space-y-4" onSubmit={submit}>
        <Field label="Mã" hint="Tùy chọn. Để trống để backend sinh mã.">
          {props => <input {...props} className="input font-mono" value={code} onChange={event => setCode(event.target.value)} />}
        </Field>

        <Field label="Loại phần thưởng" required>
          {props => (
            <select {...props} className="input" value={kind} onChange={event => setKind(event.target.value as RewardKind)}>
              <option value="coin">Coin</option>
              <option value="hero">Hero</option>
              <option value="cosmetic">Cosmetic</option>
            </select>
          )}
        </Field>

        <Field label="Số lượng" required hint="Số coin, hoặc số lượng hero/cosmetic kèm theo.">
          {props => <input {...props} className="input tabular-nums" value={quantity} onChange={event => setQuantity(event.target.value)} />}
        </Field>

        {kind === 'hero' && (
          <Field label="Hero" required hint={heroes.length === 0 ? 'Cần quyền "hero" để xem danh mục hero.' : undefined}>
            {props => (
              <select {...props} className="input" value={heroId} onChange={event => setHeroId(event.target.value)}>
                <option value="">— Chọn hero —</option>
                {heroes.map(hero => (
                  <option key={hero.id} value={hero.id}>
                    {hero.name} ({hero.code})
                  </option>
                ))}
              </select>
            )}
          </Field>
        )}

        {kind === 'cosmetic' && (
          <Field label="Id cosmetic" required hint="Backend chưa có endpoint liệt kê cosmetic cho admin — nhập id trực tiếp.">
            {props => <input {...props} className="input font-mono" value={cosmeticId} onChange={event => setCosmeticId(event.target.value)} />}
          </Field>
        )}

        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Bắt đầu" hint="Để trống = bắt đầu ngay.">
            {props => <input {...props} type="datetime-local" className="input" value={startsAt} onChange={event => setStartsAt(event.target.value)} />}
          </Field>
          <Field label="Hết hạn" hint="Để trống = không hết hạn.">
            {props => <input {...props} type="datetime-local" className="input" value={expiresAt} onChange={event => setExpiresAt(event.target.value)} />}
          </Field>
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <Field label="Tổng lượt quy đổi" required>
            {props => <input {...props} className="input tabular-nums" value={maxRedemptions} onChange={event => setMaxRedemptions(event.target.value)} />}
          </Field>
          <Field label="Số lượt mỗi người chơi" required>
            {props => <input {...props} className="input tabular-nums" value={maxPerPlayer} onChange={event => setMaxPerPlayer(event.target.value)} />}
          </Field>
        </div>

        <Field label="Lý do" required hint="Bắt buộc, tối đa 500 ký tự.">
          {props => <textarea {...props} className="input min-h-20" value={reason} onChange={event => setReason(event.target.value)} />}
        </Field>
      </form>
    </Drawer>
  );
}

// RedemptionsDrawer: Lịch sử quy đổi của một gift code.
function RedemptionsDrawer({ code, onClose }: { code: AdminGiftCodeDto; onClose: () => void }) {
  const [page, setPage] = useState<AdminGiftCodeRedemptionPageDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    api
      .get<AdminGiftCodeRedemptionPageDto>(`/admin/gift-codes/${code.id}/redemptions?limit=100`)
      .then(result => {
        if (!cancelled) setPage(result);
      })
      .catch(cause => {
        if (!cancelled) setError(cause);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [code.id]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(code.code);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };

  return (
    <Drawer open title={`Quy đổi — ${code.code}`} subtitle={`${formatNumber(code.redemptionCount)} lượt đã quy đổi`} onClose={onClose} width="max-w-2xl">
      <div className="space-y-4">
        <div className="flex items-center gap-2">
          <code className="flex-1 rounded-lg border border-border bg-surface-2 px-3 py-2 font-mono text-sm text-ink">{code.code}</code>
          <Button variant="secondary" icon={<Copy size={15} />} onClick={copy}>
            {copied ? 'Đã copy' : 'Copy'}
          </Button>
        </div>

        {error ? (
          <ErrorState error={error} />
        ) : loading ? (
          <TableSkeleton rows={4} columns={4} />
        ) : !page || page.items.length === 0 ? (
          <EmptyState title="Chưa có lượt quy đổi nào" icon={<Ticket size={20} />} />
        ) : (
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Thời điểm</th>
                  <th>Người chơi</th>
                  <th>Phần thưởng</th>
                  <th className="text-right">Số dư sau</th>
                </tr>
              </thead>
              <tbody>
                {page.items.map(row => (
                  <tr key={row.id}>
                    <td className="whitespace-nowrap text-ink-muted">{formatDateTime(row.redeemedAt)}</td>
                    <td className="text-ink">{row.playerDisplayName ?? '—'}</td>
                    <td className="text-ink-muted">
                      {row.rewardKind === 'coin' ? `${formatNumber(row.rewardQuantity)} coin` : `${row.rewardKind} ×${formatNumber(row.rewardQuantity)}`}
                    </td>
                    <td className="text-right tabular-nums text-ink">
                      {row.coinBalanceAfter === null ? '—' : formatNumber(row.coinBalanceAfter)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </Drawer>
  );
}
