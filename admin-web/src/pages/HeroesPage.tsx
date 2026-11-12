// Vai trò file: Trang quản lý Hero — danh sách, tìm kiếm, lọc, phân trang cursor, tạo/sửa/bật-tắt/gán phe.
// Mọi thao tác ghi đều gửi `reason` bắt buộc và có hộp thoại xác nhận.

import { useCallback, useMemo, useState } from 'react';
import { Filter, Plus, RefreshCw, Search, Swords } from 'lucide-react';
import { api, buildQuery } from '../lib/api';
import { loadChessClasses } from '../lib/chessClasses';
import { usePagedResource, useResource } from '../lib/useResource';
import { useAuth } from '../auth/AuthContext';
import { canWrite, PERMISSION_HERO } from '../lib/permissions';
import type { AdminFactionDto, AdminHeroDto, AdminHeroPageDto, ChessClassDto } from '../lib/types';
import { formatCoin } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { TableSkeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';
import { HeroForm } from '../components/heroes/HeroForm';
import { HeroDetailDrawer } from '../components/heroes/HeroDetailDrawer';
import { ConfirmDialog } from '../components/ui/ConfirmDialog';
import { useToast } from '../components/ui/Toast';
import { classLabel } from '../lib/classLabel';

interface Filters {
  query: string;
  classCode: string;
  factionCode: string;
  enabled: '' | 'true' | 'false';
  inShop: '' | 'true' | 'false';
}

const emptyFilters: Filters = { query: '', classCode: '', factionCode: '', enabled: '', inShop: '' };

export function HeroesPage() {
  const { permissions } = useAuth();
  const writable = canWrite(permissions, PERMISSION_HERO);
  const { notify } = useToast();

  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editing, setEditing] = useState<AdminHeroDto | 'new' | null>(null);
  const [toggling, setToggling] = useState<AdminHeroDto | null>(null);
  const [toggleReason, setToggleReason] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      api.get<AdminHeroPageDto>(
        `/admin/heroes${buildQuery({
          query: applied.query,
          classCode: applied.classCode,
          factionCode: applied.factionCode,
          enabled: applied.enabled === '' ? undefined : applied.enabled,
          inShop: applied.inShop === '' ? undefined : applied.inShop,
          cursor: cursor ?? undefined,
          limit: 50
        })}`,
        signal
      ),
    [applied]
  );

  const heroes = usePagedResource(load, [applied]);
  const factions = useResource<AdminFactionDto[]>(signal => api.get<AdminFactionDto[]>('/admin/factions', signal), []);
  // Lớp quân lấy nguyên từ DB; nhãn hiển thị theo name_vi của bảng chess_class.
  const classes = useResource<ChessClassDto[]>(signal => loadChessClasses(signal), []);

  const classCodes = useMemo(() => {
    const codes = new Set<string>();
    for (const hero of heroes.items) codes.add(hero.classCode);
    return [...codes].sort();
  }, [heroes.items]);

  const toggleEnabled = async () => {
    if (!toggling) return;
    // Reason bắt buộc: nếu thiếu thì audit log sẽ ghi dòng rỗng và không truy vết được.
    if (toggleReason.trim().length === 0) {
      notify('Bắt buộc có lý do trước khi đổi trạng thái hero.', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.patch(`/admin/heroes/${toggling.id}`, { isEnabled: !toggling.isEnabled, reason: toggleReason.trim() });
      notify(`Đã ${toggling.isEnabled ? 'tắt' : 'bật'} hero ${toggling.name}.`);
      setToggling(null);
      setToggleReason('');
      heroes.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không đổi được trạng thái hero.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-5">
      <Card
        title={
          <span className="flex items-center gap-2">
            <Swords size={18} className="text-gold-500" />
            Danh sách Hero
          </span>
        }
        description={`${heroes.total} hero trong danh mục · phân trang bằng cursor, không dùng offset.`}
        actions={
          <>
            <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={heroes.reload}>
              Làm mới
            </Button>
            {writable && (
              <Button variant="primary" icon={<Plus size={16} />} onClick={() => setEditing('new')}>
                Tạo hero
              </Button>
            )}
          </>
        }
      >
        <form
          className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-5"
          onSubmit={event => {
            event.preventDefault();
            setApplied(filters);
          }}
        >
          <div className="sm:col-span-2 xl:col-span-1">
            <label className="field-label" htmlFor="hero-search">
              Tìm kiếm
            </label>
            <div className="relative">
              <Search size={15} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-dim" />
              <input
                id="hero-search"
                className="input pl-8"
                placeholder="Tên hoặc mã hero"
                value={filters.query}
                onChange={event => setFilters(current => ({ ...current, query: event.target.value }))}
              />
            </div>
          </div>
          <div>
            <label className="field-label" htmlFor="hero-class">
              Lớp
            </label>
            <select id="hero-class" className="input" value={filters.classCode} onChange={event => setFilters(current => ({ ...current, classCode: event.target.value }))}>
              <option value="">Tất cả</option>
              {classCodes.map(code => (
                <option key={code} value={code}>
                  {classLabel(code, classes.data)}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="field-label" htmlFor="hero-faction">
              Phe
            </label>
            <select id="hero-faction" className="input" value={filters.factionCode} onChange={event => setFilters(current => ({ ...current, factionCode: event.target.value }))}>
              <option value="">Tất cả</option>
              {(factions.data ?? []).map(faction => (
                <option key={faction.id} value={faction.code}>
                  {faction.name} ({faction.code})
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="field-label" htmlFor="hero-enabled">
              Trạng thái
            </label>
            <select id="hero-enabled" className="input" value={filters.enabled} onChange={event => setFilters(current => ({ ...current, enabled: event.target.value as Filters['enabled'] }))}>
              <option value="">Tất cả</option>
              <option value="true">Đang bật</option>
              <option value="false">Đang tắt</option>
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

        {heroes.error ? (
          <ErrorState error={heroes.error} onRetry={heroes.reload} />
        ) : heroes.loading ? (
          <TableSkeleton rows={6} columns={6} />
        ) : heroes.items.length === 0 ? (
          <EmptyState
            title="Không có hero nào khớp bộ lọc"
            description="Thử xoá bộ lọc hoặc tạo hero mới."
            action={
              writable ? (
                <Button variant="secondary" icon={<Plus size={15} />} onClick={() => setEditing('new')}>
                  Tạo hero
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
                    <th>Hero</th>
                    <th>Lớp</th>
                    <th>Phe</th>
                    <th className="text-right">Setup Points</th>
                    <th className="text-right">Giá</th>
                    <th>Trạng thái</th>
                    <th className="text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {heroes.items.map(hero => (
                    <tr key={hero.id}>
                      <td>
                        <button type="button" onClick={() => setSelectedId(hero.id)} className="text-left font-medium text-ink hover:text-gold-400">
                          {hero.name}
                        </button>
                        <p className="font-mono text-xs text-ink-dim">{hero.code}</p>
                      </td>
                      <td className="text-ink-muted">{classLabel(hero.classCode, classes.data)}</td>
                      <td className="text-ink-muted">{hero.factionCodes.length > 0 ? hero.factionCodes.join(', ') : '—'}</td>
                      <td className="text-right tabular-nums text-ink-muted">{hero.setupPoints}</td>
                      <td className="text-right tabular-nums text-ink">{formatCoin(hero.coinPrice)}</td>
                      <td>
                        <div className="flex flex-wrap gap-1.5">
                          <Badge tone={hero.isEnabled ? 'ok' : 'danger'}>{hero.isEnabled ? 'Bật' : 'Tắt'}</Badge>
                          {hero.isInShop && <Badge tone="gold">Trong shop</Badge>}
                          {hero.isStarter && <Badge tone="info">Starter</Badge>}
                          {hero.isTestFixture && <Badge tone="warn">Fixture</Badge>}
                        </div>
                      </td>
                      <td className="text-right">
                        <div className="flex justify-end gap-1.5">
                          <Button variant="ghost" onClick={() => setSelectedId(hero.id)}>
                            Chi tiết
                          </Button>
                          {writable && (
                            <Button variant="secondary" onClick={() => setEditing(hero)}>
                              Sửa
                            </Button>
                          )}
                          {writable && (
                            <Button variant={hero.isEnabled ? 'danger' : 'secondary'} onClick={() => setToggling(hero)}>
                              {hero.isEnabled ? 'Tắt' : 'Bật'}
                            </Button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {heroes.nextCursor && (
              <div className="mt-4 flex justify-center">
                <Button variant="secondary" onClick={heroes.loadMore} loading={heroes.loadingMore}>
                  Tải thêm
                </Button>
              </div>
            )}
          </>
        )}
      </Card>

      {selectedId && (
        <HeroDetailDrawer
          heroId={selectedId}
          writable={writable}
          onClose={() => setSelectedId(null)}
          onChanged={() => heroes.reload()}
        />
      )}

      {editing && (
        <HeroForm
          hero={editing === 'new' ? null : editing}
          classes={classes.data ?? []}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            heroes.reload();
          }}
        />
      )}

      <ConfirmDialog
        open={toggling !== null}
        title={toggling?.isEnabled ? `Tắt hero "${toggling.name}"` : `Bật hero "${toggling?.name ?? ''}"`}
        description="Thay đổi này có hiệu lực ngay với người chơi và được ghi vào nhật ký thao tác."
        confirmLabel={toggling?.isEnabled ? 'Tắt hero' : 'Bật hero'}
        tone={toggling?.isEnabled ? 'danger' : 'primary'}
        loading={busy}
        onCancel={() => {
          setToggling(null);
          setToggleReason('');
        }}
        onConfirm={toggleEnabled}
      >
        <label className="field-label" htmlFor="toggle-reason">
          Lý do <span className="text-danger">*</span>
        </label>
        <textarea
          id="toggle-reason"
          className="input min-h-20"
          value={toggleReason}
          onChange={event => setToggleReason(event.target.value)}
          placeholder="Ví dụ: cân bằng lại độ mạnh sau patch 1.4"
        />
        <p className="mt-1 text-xs text-ink-dim">Bắt buộc, tối đa 500 ký tự.</p>
      </ConfirmDialog>
    </div>
  );
}
