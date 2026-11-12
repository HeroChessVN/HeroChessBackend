// Vai trò file: Màn Nhật ký thao tác — đọc admin_audit_log theo cursor, lọc theo action / loại đối tượng /
// định danh đối tượng / actor / khoảng thời gian. Dữ liệu trước–sau hiển thị dạng JSON an toàn.

import { useCallback, useState } from 'react';
import { Filter, RefreshCw, ScrollText, Search } from 'lucide-react';
import { api, buildQuery } from '../lib/api';
import { usePagedResource } from '../lib/useResource';
import type { AdminAuditDto, AdminAuditPageDto } from '../lib/types';
import { formatDateTime } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { TableSkeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';
import { JsonViewer } from '../components/ui/JsonViewer';

interface Filters {
  action: string;
  entityType: string;
  entityId: string;
  actorUserId: string;
  from: string;
  to: string;
}

const emptyFilters: Filters = { action: '', entityType: '', entityId: '', actorUserId: '', from: '', to: '' };

export function AuditPage() {
  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);

  const load = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      api.get<AdminAuditPageDto>(
        `/admin/audit${buildQuery({
          action: applied.action,
          entityType: applied.entityType,
          entityId: applied.entityId,
          actorUserId: applied.actorUserId,
          from: applied.from ? new Date(applied.from).toISOString() : undefined,
          to: applied.to ? new Date(applied.to).toISOString() : undefined,
          cursor: cursor ?? undefined,
          limit: 25
        })}`,
        signal
      ),
    [applied]
  );

  const audit = usePagedResource(load, [applied]);

  return (
    <div className="space-y-5">
      <Card
        title={
          <span className="flex items-center gap-2">
            <ScrollText size={18} className="text-gold-500" />
            Nhật ký thao tác quản trị
          </span>
        }
        description="Mọi thao tác ghi đều được ghi nhật ký kèm actor, dữ liệu trước–sau và lý do."
        actions={
          <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={audit.reload}>
            Làm mới
          </Button>
        }
      >
        <form
          className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4"
          onSubmit={event => {
            event.preventDefault();
            setApplied(filters);
          }}
        >
          <div>
            <label className="field-label" htmlFor="audit-action">
              Action
            </label>
            <div className="relative">
              <Search size={15} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-dim" />
              <input
                id="audit-action"
                className="input pl-8 font-mono text-xs"
                placeholder="player.ban"
                value={filters.action}
                onChange={event => setFilters(current => ({ ...current, action: event.target.value }))}
              />
            </div>
          </div>
          <div>
            <label className="field-label" htmlFor="audit-entity-type">
              Loại đối tượng
            </label>
            <input
              id="audit-entity-type"
              className="input font-mono text-xs"
              placeholder="player"
              value={filters.entityType}
              onChange={event => setFilters(current => ({ ...current, entityType: event.target.value }))}
            />
          </div>
          <div>
            <label className="field-label" htmlFor="audit-entity-id">
              Id đối tượng
            </label>
            <input
              id="audit-entity-id"
              className="input font-mono text-xs"
              placeholder="uuid"
              value={filters.entityId}
              onChange={event => setFilters(current => ({ ...current, entityId: event.target.value }))}
            />
          </div>
          <div>
            <label className="field-label" htmlFor="audit-actor">
              Id admin thực hiện
            </label>
            <input
              id="audit-actor"
              className="input font-mono text-xs"
              placeholder="uuid"
              value={filters.actorUserId}
              onChange={event => setFilters(current => ({ ...current, actorUserId: event.target.value }))}
            />
          </div>
          <div>
            <label className="field-label" htmlFor="audit-from">
              Từ ngày
            </label>
            <input
              id="audit-from"
              type="datetime-local"
              className="input"
              value={filters.from}
              onChange={event => setFilters(current => ({ ...current, from: event.target.value }))}
            />
          </div>
          <div>
            <label className="field-label" htmlFor="audit-to">
              Đến ngày
            </label>
            <input
              id="audit-to"
              type="datetime-local"
              className="input"
              value={filters.to}
              onChange={event => setFilters(current => ({ ...current, to: event.target.value }))}
            />
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

        {audit.error ? (
          <ErrorState error={audit.error} onRetry={audit.reload} />
        ) : audit.loading ? (
          <TableSkeleton rows={8} columns={5} />
        ) : audit.items.length === 0 ? (
          <EmptyState title="Không có bản ghi nhật ký khớp bộ lọc" />
        ) : (
          <>
            <ul className="space-y-2">
              {audit.items.map((row: AdminAuditDto) => (
                <li key={row.id} className="rounded-lg border border-border bg-surface-2 p-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge tone="info">{row.action}</Badge>
                    <Badge tone="neutral">{row.entityType}</Badge>
                    <span className="font-mono text-xs text-ink-dim">{row.entityId}</span>
                    <span className="ml-auto text-xs text-ink-dim">{formatDateTime(row.createdAt)}</span>
                  </div>
                  <p className="mt-1 text-xs text-ink-muted">
                    Actor: <span className="font-mono">{row.actorUserId}</span>
                  </p>
                  {row.reason && <p className="mt-1 text-sm text-ink">Lý do: {row.reason}</p>}
                  {(row.beforeData !== null || row.afterData !== null) && (
                    <div className="mt-2 grid gap-2 sm:grid-cols-2">
                      <JsonViewer value={row.beforeData} label="before" defaultOpen={false} />
                      <JsonViewer value={row.afterData} label="after" defaultOpen={false} />
                    </div>
                  )}
                </li>
              ))}
            </ul>

            {audit.nextCursor && (
              <div className="mt-4 flex justify-center">
                <Button variant="secondary" onClick={audit.loadMore} loading={audit.loadingMore}>
                  Tải thêm
                </Button>
              </div>
            )}
          </>
        )}
      </Card>
    </div>
  );
}
