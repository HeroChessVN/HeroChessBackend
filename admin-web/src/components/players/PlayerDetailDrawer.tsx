// Vai trò file: Drawer hồ sơ người chơi — hero đang sở hữu, đội hình đã lưu, lịch sử kiểm duyệt,
// lịch sử trận, biến động ví, và các thao tác cấp/thu hero.
// Chỉ thu được hero có nguồn "admin" — hero mua bằng coin hoặc starter không thu được (server trả 409).

import { useCallback, useEffect, useState } from 'react';
import { Coins, History, Package, Save, Swords, Trash2, Trophy, UserPlus } from 'lucide-react';
import { api } from '../../lib/api';
import { formatCoin, formatDateTime, formatNumber } from '../../lib/format';
import type {
  AdminHeroDto,
  AdminHeroPageDto,
  AdminPlayerDetailDto,
  AdminWalletTransactionDto
} from '../../lib/types';
import { Drawer } from '../ui/Drawer';
import { Badge } from '../ui/Badge';
import { Button } from '../ui/Button';
import { Field } from '../ui/Field';
import { Skeleton } from '../ui/Skeleton';
import { ErrorState } from '../ui/ErrorState';
import { EmptyState } from '../ui/EmptyState';
import { ConfirmDialog } from '../ui/ConfirmDialog';
import { useToast } from '../ui/Toast';

interface PlayerDetailDrawerProps {
  playerId: string;
  writable: boolean;
  onClose: () => void;
  onChanged: () => void;
}

export function PlayerDetailDrawer({ playerId, writable, onClose, onChanged }: PlayerDetailDrawerProps) {
  const { notify } = useToast();
  const [detail, setDetail] = useState<AdminPlayerDetailDto | null>(null);
  const [transactions, setTransactions] = useState<AdminWalletTransactionDto[]>([]);
  const [heroes, setHeroes] = useState<AdminHeroDto[]>([]);
  const [heroesDenied, setHeroesDenied] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);

  const [grantHeroId, setGrantHeroId] = useState('');
  const [grantReason, setGrantReason] = useState('');
  const [revoking, setRevoking] = useState<{ heroId: string; heroName: string } | null>(null);
  const [revokeReason, setRevokeReason] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const playerDetail = await api.get<AdminPlayerDetailDto>(`/admin/players/${playerId}`);
      setDetail(playerDetail);
      setTransactions(await api.get<AdminWalletTransactionDto[]>(`/admin/players/${playerId}/transactions`));
      // Danh mục hero cần quyền 'hero'; admin chỉ có quyền 'player' sẽ bị 403 — ẩn form cấp hero.
      try {
        const catalog = await api.get<AdminHeroPageDto>('/admin/heroes?limit=200');
        setHeroes(catalog.items);
        setHeroesDenied(false);
      } catch {
        setHeroes([]);
        setHeroesDenied(true);
      }
    } catch (cause) {
      setError(cause);
    } finally {
      setLoading(false);
    }
  }, [playerId]);

  useEffect(() => {
    void load();
  }, [load]);

  const grantHero = async () => {
    if (grantHeroId === '') {
      notify('Chọn hero cần cấp.', 'danger');
      return;
    }
    if (grantReason.trim().length === 0) {
      notify('Bắt buộc có lý do khi cấp hero.', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.post(`/admin/players/${playerId}/heroes`, { heroId: grantHeroId, reason: grantReason.trim() });
      notify('Đã cấp hero cho người chơi.');
      setGrantHeroId('');
      setGrantReason('');
      await load();
      onChanged();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không cấp được hero.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const revokeHero = async () => {
    if (!revoking) return;
    if (revokeReason.trim().length === 0) {
      notify('Bắt buộc có lý do khi thu hero.', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.delete(
        `/admin/players/${playerId}/heroes/${revoking.heroId}?reason=${encodeURIComponent(revokeReason.trim())}`
      );
      notify(`Đã thu hero ${revoking.heroName}.`);
      setRevoking(null);
      setRevokeReason('');
      await load();
      onChanged();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không thu được hero.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Drawer
      open
      title={detail?.player.displayName ?? 'Hồ sơ người chơi'}
      subtitle={detail?.player.email ?? undefined}
      onClose={onClose}
      width="max-w-3xl"
    >
      {error ? (
        <ErrorState error={error} onRetry={load} />
      ) : loading || !detail ? (
        <div className="space-y-4" role="status" aria-label="Đang tải hồ sơ người chơi">
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-32 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : (
        <div className="space-y-5">
          <section className="grid gap-3 sm:grid-cols-4">
            <Tile label="Elo" value={formatNumber(detail.player.elo)} />
            <Tile label="Trận (T/H/B)" value={`${detail.player.wins}/${detail.player.draws}/${detail.player.losses}`} />
            <Tile label="Số dư" value={formatCoin(detail.player.coinBalance)} />
            <Tile label="Tạo lúc" value={formatDateTime(detail.player.createdAt)} />
          </section>

          {writable && (
            <section className="rounded-lg border border-border bg-surface-2 p-4">
              <p className="mb-3 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
                <UserPlus size={13} /> Cấp hero
              </p>
              {heroesDenied ? (
                <p className="text-sm text-ink-muted">
                  Cần quyền <span className="font-mono text-xs">hero</span> để xem danh mục hero. Liên hệ quản trị viên cấp quyền nếu cần cấp hero.
                </p>
              ) : (
                <div className="grid gap-3 sm:grid-cols-2">
                  <Field label="Hero" required>
                    {props => (
                      <select {...props} className="input" value={grantHeroId} onChange={event => setGrantHeroId(event.target.value)}>
                        <option value="">— Chọn hero —</option>
                        {heroes.map(hero => (
                          <option key={hero.id} value={hero.id}>
                            {hero.name} ({hero.code})
                          </option>
                        ))}
                      </select>
                    )}
                  </Field>
                  <Field label="Lý do" required>
                    {props => (
                      <textarea
                        {...props}
                        className="input min-h-20"
                        value={grantReason}
                        onChange={event => setGrantReason(event.target.value)}
                        placeholder="Ví dụ: bù lỗi thiếu hero do bug 16/11"
                      />
                    )}
                  </Field>
                </div>
              )}
              {!heroesDenied && (
                <div className="mt-3 flex justify-end">
                  <Button variant="primary" icon={<Save size={15} />} onClick={grantHero} loading={busy}>
                    Cấp hero
                  </Button>
                </div>
              )}
            </section>
          )}

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <Package size={13} /> Hero đang sở hữu ({detail.heroes.length})
            </p>
            {detail.heroes.length === 0 ? (
              <EmptyState title="Người chơi chưa sở hữu hero nào" />
            ) : (
              <ul className="space-y-1.5">
                {detail.heroes.map(hero => (
                  <li key={hero.heroId} className="flex flex-wrap items-center gap-2 rounded-lg border border-border bg-surface-2 px-3 py-2">
                    <span className="font-medium text-ink">{hero.heroName}</span>
                    <span className="font-mono text-xs text-ink-dim">{hero.heroCode}</span>
                    <Badge tone={hero.acquiredVia === 'admin' ? 'gold' : 'neutral'}>{hero.acquiredVia}</Badge>
                    <span className="text-xs text-ink-dim">{formatDateTime(hero.acquiredAt)}</span>
                    {writable && hero.acquiredVia === 'admin' && (
                      <Button
                        variant="danger"
                        className="ml-auto"
                        icon={<Trash2 size={14} />}
                        onClick={() => {
                          setRevoking({ heroId: hero.heroId, heroName: hero.heroName });
                          setRevokeReason('');
                        }}
                      >
                        Thu
                      </Button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <Swords size={13} /> Đội hình đã lưu ({detail.lineups.length})
            </p>
            {detail.lineups.length === 0 ? (
              <EmptyState title="Chưa lưu đội hình nào" />
            ) : (
              <ul className="space-y-1.5">
                {detail.lineups.map(lineup => (
                  <li key={lineup.id} className="flex flex-wrap items-center gap-2 rounded-lg border border-border bg-surface-2 px-3 py-2 text-sm">
                    <span className="font-medium text-ink">{lineup.name}</span>
                    <span className="text-ink-muted">{lineup.entryCount} quân</span>
                    <span className="text-xs text-ink-dim">rev {lineup.revision}</span>
                    <span className="ml-auto text-xs text-ink-dim">{formatDateTime(lineup.updatedAt)}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <Trophy size={13} /> Trận đấu gần đây ({detail.matches.length})
            </p>
            {detail.matches.length === 0 ? (
              <EmptyState title="Chưa có trận đấu nào" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Thời điểm</th>
                      <th>Chế độ</th>
                      <th>Trạng thái</th>
                      <th>Kết quả</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.matches.map(match => (
                      <tr key={match.matchId}>
                        <td className="whitespace-nowrap text-ink-muted">{formatDateTime(match.createdAt)}</td>
                        <td className="text-ink-muted">{match.mode}</td>
                        <td>
                          <Badge tone={match.status === 'completed' ? 'ok' : 'neutral'}>{match.status}</Badge>
                        </td>
                        <td className="text-ink">{match.result ?? '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <Coins size={13} /> Biến động ví
            </p>
            {transactions.length === 0 ? (
              <EmptyState title="Chưa có giao dịch ví nào" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Thời điểm</th>
                      <th>Loại</th>
                      <th className="text-right">Thay đổi</th>
                      <th className="text-right">Số dư sau</th>
                    </tr>
                  </thead>
                  <tbody>
                    {transactions.map(row => (
                      <tr key={row.id}>
                        <td className="whitespace-nowrap text-ink-muted">{formatDateTime(row.createdAt)}</td>
                        <td className="text-ink-muted">{row.kind}</td>
                        <td className={`text-right tabular-nums ${row.amount >= 0 ? 'text-ok' : 'text-danger'}`}>
                          {row.amount >= 0 ? '+' : ''}
                          {formatCoin(row.amount)}
                        </td>
                        <td className="text-right tabular-nums text-ink">{formatCoin(row.balanceAfter)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <History size={13} /> Lịch sử kiểm duyệt ({detail.moderation.length})
            </p>
            {detail.moderation.length === 0 ? (
              <EmptyState title="Chưa có thao tác kiểm duyệt nào" />
            ) : (
              <ul className="space-y-1.5">
                {detail.moderation.map(row => (
                  <li key={row.id} className="rounded-lg border border-border bg-surface-2 px-3 py-2 text-sm">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge tone="info">{row.action}</Badge>
                      <span className="text-xs text-ink-dim">{formatDateTime(row.createdAt)}</span>
                    </div>
                    <p className="mt-1 text-ink-muted">{row.reason ?? '—'}</p>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      )}

      <ConfirmDialog
        open={revoking !== null}
        title={`Thu hero ${revoking?.heroName ?? ''}`}
        description="Chỉ thu được hero do admin cấp. Hero mua bằng coin hoặc starter sẽ bị từ chối bởi server."
        confirmLabel="Thu hero"
        tone="danger"
        loading={busy}
        onCancel={() => setRevoking(null)}
        onConfirm={revokeHero}
      >
        <label className="field-label" htmlFor="revoke-reason">
          Lý do <span className="text-danger">*</span>
        </label>
        <textarea
          id="revoke-reason"
          className="input min-h-20"
          value={revokeReason}
          onChange={event => setRevokeReason(event.target.value)}
          placeholder="Ví dụ: cấp nhầm trong đợt test"
        />
      </ConfirmDialog>
    </Drawer>
  );
}

// Tile: Ô số liệu nhỏ.
function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border bg-surface-2 px-3 py-2.5">
      <p className="text-[0.68rem] font-semibold uppercase tracking-wider text-ink-dim">{label}</p>
      <p className="mt-1 text-sm font-semibold text-ink">{value}</p>
    </div>
  );
}
