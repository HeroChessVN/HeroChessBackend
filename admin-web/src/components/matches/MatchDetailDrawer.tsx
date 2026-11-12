// Vai trò file: Drawer chi tiết trận — thông tin trận, phe chơi, lineup đã đóng băng, snapshot bộ luật
// và nhật ký hành động dạng JSON.
// hasReplay luôn false (backend không có deterministic replay) nên không có nút replay.

import { useCallback, useEffect, useState } from 'react';
import { FileJson, ListOrdered, Users } from 'lucide-react';
import { api } from '../../lib/api';
import { loadChessClasses } from '../../lib/chessClasses';
import { formatDateTime, formatNumber } from '../../lib/format';
import { classLabel } from '../../lib/classLabel';
import type { AdminMatchActionDto, AdminMatchDetailDto, ChessClassDto } from '../../lib/types';
import { Drawer } from '../ui/Drawer';
import { Badge, statusTone } from '../ui/Badge';
import { Skeleton } from '../ui/Skeleton';
import { ErrorState } from '../ui/ErrorState';
import { EmptyState } from '../ui/EmptyState';
import { JsonViewer } from '../ui/JsonViewer';

interface MatchDetailDrawerProps {
  matchId: string;
  onClose: () => void;
}

export function MatchDetailDrawer({ matchId, onClose }: MatchDetailDrawerProps) {
  const [detail, setDetail] = useState<AdminMatchDetailDto | null>(null);
  const [actions, setActions] = useState<AdminMatchActionDto[]>([]);
  const [classes, setClasses] = useState<ChessClassDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [actionsLoading, setActionsLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.get<AdminMatchDetailDto>(`/admin/matches/${matchId}`);
      setDetail(result);
      // Lớp quân lấy nhãn từ DB; lỗi endpoint này không được làm hỏng drawer trận.
      loadChessClasses().then(setClasses).catch(() => setClasses([]));
      // Nhật ký hành động tải riêng để trận chi tiết vẫn hiện nhanh.
      setActionsLoading(true);
      try {
        setActions(await api.get<AdminMatchActionDto[]>(`/admin/matches/${matchId}/actions?limit=200`));
      } catch {
        setActions([]);
      } finally {
        setActionsLoading(false);
      }
    } catch (cause) {
      setError(cause);
    } finally {
      setLoading(false);
    }
  }, [matchId]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <Drawer
      open
      title={detail ? `Trận ${detail.match.mode} — ${detail.match.status}` : 'Chi tiết trận'}
      subtitle={detail ? `${detail.match.id} · ${formatDateTime(detail.match.createdAt)}` : undefined}
      onClose={onClose}
      width="max-w-4xl"
    >
      {error ? (
        <ErrorState error={error} onRetry={load} />
      ) : loading || !detail ? (
        <div className="space-y-4" role="status" aria-label="Đang tải chi tiết trận">
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : (
        <div className="space-y-5">
          <section className="grid gap-3 sm:grid-cols-4">
            <Tile label="Chế độ" value={detail.match.mode} />
            <Tile label="Trạng thái" value={detail.match.status} />
            <Tile label="Kết quả" value={detail.match.result ?? '—'} />
            <Tile label="Bộ luật" value={detail.match.rulesetCode ?? '—'} />
          </section>

          <section className="flex flex-wrap gap-1.5">
            <Badge tone={statusTone(detail.match.status)}>{detail.match.status}</Badge>
            {detail.match.endReason && <Badge tone="neutral">Kết thúc: {detail.match.endReason}</Badge>}
            <Badge tone="info">Content version: {detail.contentVersion}</Badge>
            <Badge tone="neutral">{formatNumber(detail.match.actionCount)} hành động</Badge>
            {detail.firstActionAt && <Badge tone="neutral">Bắt đầu: {formatDateTime(detail.firstActionAt)}</Badge>}
            {!detail.hasReplay && (
              <Badge tone="warn" className="gap-1">
                Không có replay
              </Badge>
            )}
          </section>

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <Users size={13} /> Phe thi đấu
            </p>
            <div className="grid gap-3 sm:grid-cols-2">
              {detail.match.sides.map(side => (
                <div key={side.side} className="rounded-lg border border-border bg-surface-2 p-3">
                  <p className="flex items-center justify-between text-sm font-semibold uppercase text-ink">
                    {side.side}
                    <Badge tone={side.outcome === 'win' ? 'ok' : side.outcome === 'loss' ? 'danger' : 'neutral'}>
                      {side.outcome ?? '—'}
                    </Badge>
                  </p>
                  <p className="mt-1 text-sm text-ink">{side.displayName ?? (side.isBot ? `Bot (${side.difficulty ?? '?'})` : '—')}</p>
                  <ul className="mt-2 space-y-0.5 text-xs text-ink-muted">
                    <li>Loại: {side.participantType}{side.isBot ? ` · độ khó ${side.difficulty ?? '—'}` : ''}</li>
                    <li>Số quân: {side.heroCount}</li>
                    <li>Elo: {side.eloBefore ?? '—'} → {side.eloAfter ?? '—'}</li>
                    <li>Thưởng coin: {formatNumber(side.coinReward)}</li>
                  </ul>
                </div>
              ))}
            </div>
          </section>

          <section>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Lineup đã đóng băng</p>
            {detail.lineups.length === 0 ? (
              <EmptyState title="Không có lineup ghi nhận" />
            ) : (
              <div className="space-y-4">
                {detail.lineups.map(lineup => (
                  <div key={lineup.side} className="rounded-lg border border-border bg-surface-2 p-3">
                    <p className="text-sm font-semibold uppercase text-ink">{lineup.side}</p>
                    <div className="table-scroll mt-2">
                      <table className="data-table">
                        <thead>
                          <tr>
                            <th className="text-right">Slot</th>
                            <th>Hero</th>
                            <th>Lớp</th>
                            <th>Phe</th>
                            <th className="text-right">Setup</th>
                            <th className="text-right">Toạ độ</th>
                          </tr>
                        </thead>
                        <tbody>
                          {lineup.heroes.map(hero => (
                            <tr key={`${lineup.side}-${hero.slotNo}`}>
                              <td className="text-right tabular-nums text-ink-dim">{hero.slotNo}</td>
                              <td className="text-ink">
                                {hero.heroName ?? hero.heroCode ?? '—'}
                                {hero.heroCode && <span className="ml-1 font-mono text-xs text-ink-dim">{hero.heroCode}</span>}
                              </td>
                              <td className="text-ink-muted">{classLabel(hero.classCode, classes)}</td>
                              <td className="text-ink-muted">{hero.factionCode ?? '—'}</td>
                              <td className="text-right tabular-nums text-ink-muted">{hero.setupPoints}</td>
                              <td className="text-right tabular-nums text-ink-dim">
                                {hero.startX},{hero.startY}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                    {lineup.skills.length > 0 && (
                      <div className="mt-2 flex flex-wrap gap-1.5">
                        {lineup.skills.map(skill => (
                          <Badge key={skill.slotNo} tone="info">
                            {skill.name ?? skill.code ?? '—'}
                          </Badge>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </section>

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <FileJson size={13} /> Snapshot bộ luật
            </p>
            <JsonViewer value={detail.rulesetSnapshot} label="rulesetSnapshot" />
          </section>

          <section>
            <p className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wider text-ink-muted">
              <ListOrdered size={13} /> Nhật ký hành động ({actions.length})
            </p>
            {actionsLoading ? (
              <Skeleton className="h-32 w-full" />
            ) : actions.length === 0 ? (
              <EmptyState title="Chưa có hành động nào được ghi" />
            ) : (
              <ul className="space-y-2">
                {actions.map(action => (
                  <li key={action.sequenceNo} className="rounded-lg border border-border bg-surface-2 p-3">
                    <p className="flex flex-wrap items-center gap-2 text-xs text-ink-muted">
                      <span className="font-mono font-semibold text-ink">#{action.sequenceNo}</span>
                      <Badge tone="info">{action.kind}</Badge>
                      {action.actorSide && <Badge tone="neutral">{action.actorSide}</Badge>}
                      <span>{formatDateTime(action.committedAt)}</span>
                    </p>
                    <div className="mt-2 grid gap-2 sm:grid-cols-2">
                      <JsonViewer value={action.request} label="request" defaultOpen={false} />
                      <JsonViewer value={action.events} label="events" defaultOpen={false} />
                    </div>
                  </li>
                ))}
              </ul>
            )}
            <p className="mt-2 text-xs text-ink-dim">
              request/events là JSON thô của backend — console hiển thị nguyên trạng, không diễn giải nội dung hành động.
            </p>
          </section>
        </div>
      )}
    </Drawer>
  );
}

// Tile: Ô số liệu nhỏ.
function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border bg-surface-2 px-3 py-2.5">
      <p className="text-[0.68rem] font-semibold uppercase tracking-wider text-ink-dim">{label}</p>
      <p className="mt-1 truncate text-sm font-semibold text-ink">{value}</p>
    </div>
  );
}
