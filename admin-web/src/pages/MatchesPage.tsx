// Vai trò file: Màn Trận đấu — danh sách trận (ranked / vs bot), chi tiết phe, lineup đã đóng băng,
// snapshot bộ luật và nhật ký hành động dạng JSON.
// hasReplay luôn false — không có nút replay. request/events là jsonb thô nên hiển thị an toàn, không diễn giải.

import { useCallback, useState } from 'react';
import { Filter, RefreshCw, Swords } from 'lucide-react';
import { api, buildQuery } from '../lib/api';
import { usePagedResource } from '../lib/useResource';
import type { AdminMatchPageDto } from '../lib/types';
import { formatDateTime, formatNumber } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge, statusTone } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { TableSkeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';
import { MatchDetailDrawer } from '../components/matches/MatchDetailDrawer';

interface Filters {
  mode: '' | 'ranked' | 'bot';
  status: string;
}

const emptyFilters: Filters = { mode: '', status: '' };

export function MatchesPage() {
  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const load = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      api.get<AdminMatchPageDto>(
        `/admin/matches${buildQuery({
          mode: applied.mode === '' ? undefined : applied.mode,
          status: applied.status === '' ? undefined : applied.status,
          cursor: cursor ?? undefined,
          limit: 50
        })}`,
        signal
      ),
    [applied]
  );

  const matches = usePagedResource(load, [applied]);

  return (
    <div className="space-y-5">
      <Card
        title={
          <span className="flex items-center gap-2">
            <Swords size={18} className="text-gold-500" />
            Danh sách trận đấu
          </span>
        }
        description={`${formatNumber(matches.total)} trận · số quân mỗi phe là số thực tế nên hỗ trợ kịch bản bất đối xứng.`}
        actions={
          <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={matches.reload}>
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
            <label className="field-label" htmlFor="match-mode">
              Chế độ
            </label>
            <select
              id="match-mode"
              className="input"
              value={filters.mode}
              onChange={event => setFilters(current => ({ ...current, mode: event.target.value as Filters['mode'] }))}
            >
              <option value="">Tất cả</option>
              <option value="ranked">Ranked</option>
              <option value="bot">Vs bot</option>
            </select>
          </div>
          <div>
            <label className="field-label" htmlFor="match-status">
              Trạng thái
            </label>
            <select
              id="match-status"
              className="input"
              value={filters.status}
              onChange={event => setFilters(current => ({ ...current, status: event.target.value }))}
            >
              <option value="">Tất cả</option>
              <option value="active">Đang chạy</option>
              <option value="completed">Đã kết thúc</option>
              <option value="abandoned">Bỏ dở</option>
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

        {matches.error ? (
          <ErrorState error={matches.error} onRetry={matches.reload} />
        ) : matches.loading ? (
          <TableSkeleton rows={6} columns={6} />
        ) : matches.items.length === 0 ? (
          <EmptyState title="Không có trận nào khớp bộ lọc" />
        ) : (
          <>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Thời điểm</th>
                    <th>Chế độ</th>
                    <th>Phe</th>
                    <th>Trạng thái</th>
                    <th className="text-right">Hành động</th>
                    <th className="text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {matches.items.map(match => (
                    <tr key={match.id}>
                      <td className="whitespace-nowrap text-ink-muted">{formatDateTime(match.createdAt)}</td>
                      <td>
                        <Badge tone={match.mode === 'ranked' ? 'gold' : 'info'}>{match.mode}</Badge>
                      </td>
                      <td>
                        <div className="space-y-0.5">
                          {match.sides.map(side => (
                            <p key={side.side} className="text-xs text-ink-muted">
                              <span className="font-semibold uppercase text-ink-dim">{side.side}</span>{' '}
                              {side.displayName ?? (side.isBot ? `Bot (${side.difficulty ?? '?'})` : '—')} · {side.heroCount} quân
                            </p>
                          ))}
                        </div>
                      </td>
                      <td>
                        <Badge tone={statusTone(match.status)}>{match.status}</Badge>
                        {match.result && <p className="mt-0.5 text-xs text-ink-dim">{match.result}</p>}
                      </td>
                      <td className="text-right tabular-nums text-ink-muted">{formatNumber(match.actionCount)}</td>
                      <td className="text-right">
                        <Button variant="ghost" onClick={() => setSelectedId(match.id)}>
                          Chi tiết
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {matches.nextCursor && (
              <div className="mt-4 flex justify-center">
                <Button variant="secondary" onClick={matches.loadMore} loading={matches.loadingMore}>
                  Tải thêm
                </Button>
              </div>
            )}
          </>
        )}
      </Card>

      {selectedId && <MatchDetailDrawer matchId={selectedId} onClose={() => setSelectedId(null)} />}
    </div>
  );
}
