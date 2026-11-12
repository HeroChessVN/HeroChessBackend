// Vai trò file: Màn Người chơi — danh sách, tìm kiếm, lọc trạng thái, ban/mở khoá, điều chỉnh ví, cấp/thu hero.
// Thao tác nhạy cảm đều qua hộp thoại xác nhận và bắt buộc có `reason`.

import { useCallback, useState } from 'react';
import { Ban, Filter, RefreshCw, Search, UserCheck, Users, Wallet } from 'lucide-react';
import { api, buildQuery } from '../lib/api';
import { usePagedResource } from '../lib/useResource';
import { useAuth } from '../auth/AuthContext';
import { canWrite, PERMISSION_PLAYER } from '../lib/permissions';
import type { AdminPlayerDto, AdminPlayerPageDto } from '../lib/types';
import { formatCoin, formatNumber } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge, statusTone } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { TableSkeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';
import { ConfirmDialog } from '../components/ui/ConfirmDialog';
import { PlayerDetailDrawer } from '../components/players/PlayerDetailDrawer';
import { useToast } from '../components/ui/Toast';

interface Filters {
  query: string;
  status: '' | 'active' | 'disabled';
}

const emptyFilters: Filters = { query: '', status: '' };

type DialogKind = 'ban' | 'coins' | null;

export function PlayersPage() {
  const { permissions } = useAuth();
  const writable = canWrite(permissions, PERMISSION_PLAYER);
  const { notify } = useToast();

  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const [dialog, setDialog] = useState<DialogKind>(null);
  const [target, setTarget] = useState<AdminPlayerDto | null>(null);
  const [reason, setReason] = useState('');
  const [amount, setAmount] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      api.get<AdminPlayerPageDto>(
        `/admin/players${buildQuery({
          query: applied.query,
          status: applied.status === '' ? undefined : applied.status,
          cursor: cursor ?? undefined,
          limit: 50
        })}`,
        signal
      ),
    [applied]
  );

  const players = usePagedResource(load, [applied]);

  const openBan = (player: AdminPlayerDto) => {
    setTarget(player);
    setDialog('ban');
    setReason('');
  };

  const openCoins = (player: AdminPlayerDto) => {
    setTarget(player);
    setDialog('coins');
    setAmount('');
    setReason('');
  };

  const submitBan = async () => {
    if (!target) return;
    if (reason.trim().length === 0) {
      notify('Bắt buộc có lý do khi ban hoặc mở khoá tài khoản.', 'danger');
      return;
    }
    const nextStatus = target.status === 'disabled' ? 'active' : 'disabled';
    setBusy(true);
    try {
      await api.patch(`/admin/players/${target.id}/status`, { status: nextStatus, reason: reason.trim() });
      notify(
        nextStatus === 'disabled'
          ? `Đã ban tài khoản ${target.displayName}. Người chơi sẽ bị đăng xuất khỏi phiên hiện tại.`
          : `Đã mở khoá tài khoản ${target.displayName}.`
      );
      setDialog(null);
      players.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không đổi được trạng thái tài khoản.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const submitCoins = async () => {
    if (!target) return;
    if (!/^-?\d+$/.test(amount)) {
      notify('Số tiền phải là số nguyên (âm để trừ).', 'danger');
      return;
    }
    if (Number(amount) === 0) {
      notify('Số tiền điều chỉnh không được bằng 0.', 'danger');
      return;
    }
    if (reason.trim().length === 0) {
      notify('Bắt buộc có lý do khi điều chỉnh coin.', 'danger');
      return;
    }
    setBusy(true);
    try {
      const result = await api.post<{ balanceBefore: string; balanceAfter: string }>(`/admin/players/${target.id}/coins`, {
        amount: amount.trim(),
        reason: reason.trim()
      });
      notify(
        `Số dư ${target.displayName}: ${formatCoin(result.balanceBefore)} → ${formatCoin(result.balanceAfter)}.`
      );
      setDialog(null);
      players.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không điều chỉnh được coin.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-5">
      <Card
        title={
          <span className="flex items-center gap-2">
            <Users size={18} className="text-gold-500" />
            Danh sách người chơi
          </span>
        }
        description={`${formatNumber(players.total)} tài khoản · tìm theo tên hiển thị hoặc email.`}
        actions={
          <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={players.reload}>
            Làm mới
          </Button>
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
            <label className="field-label" htmlFor="player-search">
              Tìm kiếm
            </label>
            <div className="relative">
              <Search size={15} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-dim" />
              <input
                id="player-search"
                className="input pl-8"
                placeholder="Tên hiển thị hoặc email"
                value={filters.query}
                onChange={event => setFilters(current => ({ ...current, query: event.target.value }))}
              />
            </div>
          </div>
          <div>
            <label className="field-label" htmlFor="player-status">
              Trạng thái
            </label>
            <select
              id="player-status"
              className="input"
              value={filters.status}
              onChange={event => setFilters(current => ({ ...current, status: event.target.value as Filters['status'] }))}
            >
              <option value="">Tất cả</option>
              <option value="active">Đang hoạt động</option>
              <option value="disabled">Đã khoá</option>
            </select>
          </div>
          <div className="flex items-end gap-2">
            <Button type="submit" variant="primary" icon={<Filter size={15} />} className="flex-1">
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

        {players.error ? (
          <ErrorState error={players.error} onRetry={players.reload} />
        ) : players.loading ? (
          <TableSkeleton rows={6} columns={6} />
        ) : players.items.length === 0 ? (
          // Không có dữ liệu giả: rỗng là rỗng thật từ API.
          <EmptyState
            title={applied.query || applied.status ? 'Không có người chơi nào khớp bộ lọc' : 'Chưa có người chơi'}
            description={
              applied.query || applied.status
                ? 'Thử xoá bộ lọc để xem toàn bộ người chơi.'
                : 'Database hiện chưa có tài khoản nào có vai trò người chơi.'
            }
          />
        ) : (
          <>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Người chơi</th>
                    <th className="text-right">Elo</th>
                    <th className="text-right">Trận</th>
                    <th className="text-right">Số dư</th>
                    <th className="text-right">Hero</th>
                    <th>Trạng thái</th>
                    <th className="text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {players.items.map(player => (
                    <tr key={player.id}>
                      <td>
                        <button
                          type="button"
                          onClick={() => setSelectedId(player.id)}
                          className="text-left font-medium text-ink hover:text-gold-400"
                        >
                          {player.displayName}
                        </button>
                        <p className="text-xs text-ink-dim">{player.email ?? '—'}</p>
                      </td>
                      <td className="text-right tabular-nums text-ink-muted">{formatNumber(player.elo)}</td>
                      <td className="text-right tabular-nums text-ink-muted">
                        {formatNumber(player.wins)}/{formatNumber(player.draws)}/{formatNumber(player.losses)}
                      </td>
                      <td className="text-right tabular-nums text-ink">{formatCoin(player.coinBalance)}</td>
                      <td className="text-right tabular-nums text-ink-muted">{formatNumber(player.ownedHeroes)}</td>
                      <td>
                        <Badge tone={statusTone(player.status)}>{player.status === 'active' ? 'Hoạt động' : 'Đã khoá'}</Badge>
                      </td>
                      <td className="text-right">
                        <div className="flex justify-end gap-1.5">
                          <Button variant="ghost" onClick={() => setSelectedId(player.id)}>
                            Chi tiết
                          </Button>
                          {writable && (
                            <>
                              <Button variant="secondary" icon={<Wallet size={14} />} onClick={() => openCoins(player)}>
                                Coin
                              </Button>
                              <Button
                                variant={player.status === 'disabled' ? 'secondary' : 'danger'}
                                icon={player.status === 'disabled' ? <UserCheck size={14} /> : <Ban size={14} />}
                                onClick={() => openBan(player)}
                              >
                                {player.status === 'disabled' ? 'Mở khoá' : 'Ban'}
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

            {players.nextCursor && (
              <div className="mt-4 flex justify-center">
                <Button variant="secondary" onClick={players.loadMore} loading={players.loadingMore}>
                  Tải thêm
                </Button>
              </div>
            )}
          </>
        )}
      </Card>

      <ConfirmDialog
        open={dialog === 'ban'}
        title={target?.status === 'disabled' ? `Mở khoá tài khoản ${target?.displayName ?? ''}` : `Ban tài khoản ${target?.displayName ?? ''}`}
        description={
          target?.status === 'disabled'
            ? 'Người chơi sẽ đăng nhập lại được ngay.'
            : 'Tài khoản bị ban không đăng nhập được. Người chơi đang mở phiên sẽ phải đăng xuất.'
        }
        confirmLabel={target?.status === 'disabled' ? 'Mở khoá' : 'Ban tài khoản'}
        tone={target?.status === 'disabled' ? 'primary' : 'danger'}
        loading={busy}
        onCancel={() => setDialog(null)}
        onConfirm={submitBan}
      >
        <label className="field-label" htmlFor="ban-reason">
          Lý do <span className="text-danger">*</span>
        </label>
        <textarea
          id="ban-reason"
          className="input min-h-20"
          value={reason}
          onChange={event => setReason(event.target.value)}
          placeholder="Ví dụ: phát hiện gian lận rank 12/11"
        />
        <p className="mt-1 text-xs text-ink-dim">Bắt buộc, tối đa 500 ký tự. Lý do được ghi vào nhật ký thao tác.</p>
      </ConfirmDialog>

      <ConfirmDialog
        open={dialog === 'coins'}
        title={`Điều chỉnh coin — ${target?.displayName ?? ''}`}
        description="Số dư sau điều chỉnh không được âm. Giao dịch được ghi vào ledger ví."
        confirmLabel="Điều chỉnh"
        tone="danger"
        loading={busy}
        onCancel={() => setDialog(null)}
        onConfirm={submitCoins}
      >
        <div className="space-y-3">
          <div>
            <label className="field-label" htmlFor="coin-amount">
              Số tiền <span className="text-danger">*</span>
            </label>
            <input
              id="coin-amount"
              className="input tabular-nums"
              inputMode="numeric"
              placeholder="500 hoặc -200"
              value={amount}
              onChange={event => setAmount(event.target.value)}
            />
            <p className="mt-1 text-xs text-ink-dim">Số dư hiện tại: {formatCoin(target?.coinBalance ?? null)}</p>
          </div>
          <div>
            <label className="field-label" htmlFor="coin-reason">
              Lý do <span className="text-danger">*</span>
            </label>
            <textarea
              id="coin-reason"
              className="input min-h-20"
              value={reason}
              onChange={event => setReason(event.target.value)}
              placeholder="Ví dụ: bù lỗi server sập lúc 16/11"
            />
          </div>
        </div>
      </ConfirmDialog>

      {selectedId && (
        <PlayerDetailDrawer
          playerId={selectedId}
          writable={writable}
          onClose={() => setSelectedId(null)}
          onChanged={() => players.reload()}
        />
      )}
    </div>
  );
}
