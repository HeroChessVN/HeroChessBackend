// Vai trò file: Dashboard tổng quan — KPI từ /admin/stats, cân bằng từ /admin/analytics/balance,
// bot từ /admin/analytics/bot. Mọi số liệu đến thẳng từ API; không tự tính toán hay bịa biểu đồ.

import { useMemo, useState } from 'react';
import { Activity, Bot, Coins, LayoutDashboard, RefreshCw, Swords, Trophy, Users } from 'lucide-react';
import { api } from '../lib/api';
import { useResource } from '../lib/useResource';
import { useAuth } from '../auth/AuthContext';
import { hasPermission, PERMISSION_ANALYTICS } from '../lib/permissions';
import type { AdminBalanceAnalyticsDto, AdminBotAnalyticsDto, AdminStatsDto } from '../lib/types';
import { formatDateTime, formatNumber, formatPercent } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { CardSkeleton, Skeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';

type Mode = 'ranked' | 'bot';

export function DashboardPage() {
  const [mode, setMode] = useState<Mode>('ranked');
  const { permissions } = useAuth();
  // Panel cân bằng/bot cần quyền 'analytics'; quyền 'read' chỉ mở KPI tổng quan.
  const canSeeAnalytics = hasPermission(permissions, PERMISSION_ANALYTICS);

  const stats = useResource<AdminStatsDto>(signal => api.get('/admin/stats', signal), []);
  // Không gọi API analytics nếu tài khoản thiếu quyền — tránh 403 gây nhiễu giao diện.
  const balance = useResource<AdminBalanceAnalyticsDto | null>(
    signal => (canSeeAnalytics ? api.get(`/admin/analytics/balance?mode=${mode}`, signal) : Promise.resolve(null)),
    [mode, canSeeAnalytics]
  );
  const bot = useResource<AdminBotAnalyticsDto | null>(
    signal => (canSeeAnalytics ? api.get('/admin/analytics/bot', signal) : Promise.resolve(null)),
    [canSeeAnalytics]
  );

  const reloadAll = () => {
    stats.reload();
    balance.reload();
    bot.reload();
  };

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold tracking-tight text-ink">
            <LayoutDashboard size={19} className="text-gold-500" />
            Tổng quan vận hành
          </h2>
          <p className="mt-0.5 text-sm text-ink-muted">
            {stats.data ? `Số liệu tạo lúc ${formatDateTime(stats.data.generatedAt)}` : 'Đang tải số liệu…'}
          </p>
        </div>
        <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={reloadAll}>
          Làm mới
        </Button>
      </div>

      {stats.error ? (
        <Card>
          <ErrorState error={stats.error} onRetry={stats.reload} />
        </Card>
      ) : stats.loading ? (
        <CardSkeleton count={4} />
      ) : stats.data ? (
        <KpiGrid stats={stats.data} />
      ) : null}

      {canSeeAnalytics && (
        <div className="grid gap-5 xl:grid-cols-2">
          <BalancePanel mode={mode} onModeChange={setMode} data={balance.data} loading={balance.loading} error={balance.error} onRetry={balance.reload} />
          <BotPanel data={bot.data} loading={bot.loading} error={bot.error} onRetry={bot.reload} />
        </div>
      )}
    </div>
  );
}

// KpiGrid: Các thẻ số liệu tổng quan.
function KpiGrid({ stats }: { stats: AdminStatsDto }) {
  const cards = [
    {
      label: 'Người chơi',
      value: formatNumber(stats.totalPlayers),
      hint: `${formatNumber(stats.activePlayers)} đang hoạt động · ${formatNumber(stats.disabledPlayers)} bị khoá`,
      icon: Users
    },
    {
      label: 'Hero',
      value: formatNumber(stats.totalHeroes),
      hint: `${formatNumber(stats.enabledHeroes)} đang bật · ${formatNumber(stats.inShopHeroes)} trong cửa hàng`,
      icon: Swords
    },
    {
      label: 'Trận ranked',
      value: formatNumber(stats.rankedMatches),
      hint: `${formatNumber(stats.totalMatches)} trận tổng · ${formatNumber(stats.activeMatches)} đang chạy`,
      icon: Trophy
    },
    {
      label: 'Trận vs bot',
      value: formatNumber(stats.botMatches),
      hint: `${formatNumber(stats.completedMatches)} trận đã kết thúc`,
      icon: Bot
    },
    {
      label: 'Tổng coin lưu thông',
      value: formatNumber(stats.totalCoinBalance),
      hint: 'Số dư tổng của mọi ví người chơi',
      icon: Coins
    },
    {
      label: 'Tài khoản admin',
      value: formatNumber(stats.adminCount),
      hint: 'Đang có quyền truy cập console',
      icon: Activity
    }
  ];

  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {cards.map(card => {
        const Icon = card.icon;
        return (
          <div key={card.label} className="card p-5">
            <div className="flex items-center justify-between gap-2">
              <p className="text-xs font-semibold uppercase tracking-wider text-ink-muted">{card.label}</p>
              <Icon size={16} className="text-gold-600" />
            </div>
            <p className="mt-2 text-2xl font-bold tracking-tight text-ink">{card.value}</p>
            <p className="mt-1 text-xs text-ink-dim">{card.hint}</p>
          </div>
        );
      })}
    </div>
  );
}

// BalancePanel: Báo cáo cân bằng theo mode. `value` null = chưa đủ dữ liệu — hiện rõ, không tự tính.
function BalancePanel({
  mode,
  onModeChange,
  data,
  loading,
  error,
  onRetry
}: {
  mode: Mode;
  onModeChange: (mode: Mode) => void;
  data: AdminBalanceAnalyticsDto | null;
  loading: boolean;
  error: unknown;
  onRetry: () => void;
}) {
  return (
    <Card
      title="Thống kê cân bằng"
      description="Tỉ lệ chọn và tỉ lệ thắng theo hero, phe, skill."
      actions={
        <div className="flex rounded-lg border border-border bg-surface-2 p-0.5" role="tablist" aria-label="Chọn chế độ">
          {(['ranked', 'bot'] as Mode[]).map(item => (
            <button
              key={item}
              type="button"
              role="tab"
              aria-selected={mode === item}
              onClick={() => onModeChange(item)}
              className={`rounded-md px-3 py-1 text-xs font-semibold transition-colors ${mode === item ? 'bg-gold-500/15 text-gold-400' : 'text-ink-muted hover:text-ink'}`}
            >
              {item === 'ranked' ? 'Ranked' : 'Bot'}
            </button>
          ))}
        </div>
      }
    >
      {error ? (
        <ErrorState error={error} onRetry={onRetry} />
      ) : loading ? (
        <div className="space-y-3" role="status" aria-label="Đang tải dữ liệu">
          <Skeleton className="h-4 w-1/3" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-4 w-1/4" />
          <Skeleton className="h-24 w-full" />
        </div>
      ) : !data || (data.heroes.length === 0 && data.factions.length === 0) ? (
        <EmptyState
          title="Chưa có trận đấu để phân tích"
          description={`Chế độ "${mode}" chưa có trận đã kết thúc. Số liệu sẽ xuất hiện khi có trận ranked hoặc trận vs bot.`}
        />
      ) : (
        <BalanceContent data={data} />
      )}
    </Card>
  );
}

function BalanceContent({ data }: { data: AdminBalanceAnalyticsDto }) {
  const topHeroes = useMemo(() => [...data.heroes].sort((a, b) => b.picks - a.picks).slice(0, 8), [data.heroes]);
  const topFactions = useMemo(() => [...data.factions].sort((a, b) => b.matches - a.matches).slice(0, 8), [data.factions]);

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Metric label="Số trận" value={formatNumber(data.matchCount)} />
        <Metric
          label="Lợi thế đi trước"
          value={formatPercent(data.firstMoveAdvantage.value)}
          hint={`cỡ mẫu ${formatNumber(data.firstMoveAdvantage.sampleSize)}`}
        />
        <Metric
          label="Thời lượng TB"
          value={data.averageDurationSeconds === null ? '—' : `${formatNumber(Math.round(data.averageDurationSeconds * 10) / 10)}s`}
        />
        <Metric label="Skill đã dùng" value={formatNumber(data.skills.length)} />
      </div>

      <div>
        <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Hero được chọn nhiều nhất</p>
        {topHeroes.length === 0 ? (
          <EmptyState title="Chưa có dữ liệu chọn hero" />
        ) : (
          <ul className="space-y-1.5">
            {topHeroes.map(hero => (
              <li key={hero.heroId} className="flex items-center gap-3 text-sm">
                <span className="w-32 shrink-0 truncate text-ink">{hero.heroName}</span>
                <span className="h-2 flex-1 overflow-hidden rounded-full bg-surface-2">
                  <span className="block h-full rounded-full bg-gold-600/70" style={{ width: `${Math.min(100, hero.pickRate ?? 0)}%` }} />
                </span>
                <span className="w-16 shrink-0 text-right tabular-nums text-ink-muted">{formatPercent(hero.pickRate)}</span>
                <span className="w-20 shrink-0 text-right tabular-nums text-ink-dim">WR {formatPercent(hero.winRate)}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div>
        <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Hiệu suất theo phe</p>
        {topFactions.length === 0 ? (
          <EmptyState title="Chưa có dữ liệu phe" />
        ) : (
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Phe</th>
                  <th className="text-right">Trận</th>
                  <th className="text-right">Thắng</th>
                  <th className="text-right">Hòa</th>
                  <th className="text-right">Thua</th>
                  <th className="text-right">Tỉ lệ thắng</th>
                </tr>
              </thead>
              <tbody>
                {topFactions.map(faction => (
                  <tr key={faction.factionCode}>
                    <td className="font-medium text-ink">{faction.factionCode}</td>
                    <td className="text-right tabular-nums">{formatNumber(faction.matches)}</td>
                    <td className="text-right tabular-nums text-ok">{formatNumber(faction.wins)}</td>
                    <td className="text-right tabular-nums text-ink-muted">{formatNumber(faction.draws)}</td>
                    <td className="text-right tabular-nums text-danger">{formatNumber(faction.losses)}</td>
                    <td className="text-right tabular-nums">{formatPercent(faction.winRate)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

// BotPanel: Hiệu suất bot — tách riêng khỏi cân bằng PvP (theo ADMIN_API_GUIDE).
function BotPanel({ data, loading, error, onRetry }: { data: AdminBotAnalyticsDto | null; loading: boolean; error: unknown; onRetry: () => void }) {
  return (
    <Card title="Phân tích bot" description="Tỉ lệ thắng của bot theo độ khó và theo bộ luật.">
      {error ? (
        <ErrorState error={error} onRetry={onRetry} />
      ) : loading ? (
        <div className="space-y-3" role="status" aria-label="Đang tải dữ liệu">
          <Skeleton className="h-4 w-1/3" />
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-32 w-full" />
        </div>
      ) : !data || data.botMatches === 0 ? (
        <EmptyState title="Chưa có trận vs bot" description="Chưa có trận nào kết thúc với bot nên chưa có số liệu để hiển thị." />
      ) : (
        <div className="space-y-5">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Metric label="Trận vs bot" value={formatNumber(data.botMatches)} />
            <Metric label="Bot thắng" value={formatNumber(data.botWins)} />
            <Metric label="Người chơi thắng" value={formatNumber(data.playerWins)} />
            <Metric label="Tỉ lệ thắng bot" value={formatPercent(data.botWinRate)} />
          </div>

          <div>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Theo độ khó</p>
            {data.byDifficulty.length === 0 ? (
              <EmptyState title="Chưa có dữ liệu độ khó" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Độ khó</th>
                      <th className="text-right">Trận</th>
                      <th className="text-right">Thắng</th>
                      <th className="text-right">Hòa</th>
                      <th className="text-right">Thua</th>
                      <th className="text-right">Tỉ lệ thắng</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.byDifficulty.map(row => (
                      <tr key={row.difficulty}>
                        <td className="font-medium text-ink">{row.difficulty}</td>
                        <td className="text-right tabular-nums">{formatNumber(row.matches)}</td>
                        <td className="text-right tabular-nums text-ok">{formatNumber(row.wins)}</td>
                        <td className="text-right tabular-nums text-ink-muted">{formatNumber(row.draws)}</td>
                        <td className="text-right tabular-nums text-danger">{formatNumber(row.losses)}</td>
                        <td className="text-right tabular-nums">{formatPercent(row.winRate)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <div>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Theo bộ luật</p>
            {data.byRuleset.length === 0 ? (
              <EmptyState title="Chưa có dữ liệu bộ luật" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Bộ luật</th>
                      <th className="text-right">Trận</th>
                      <th className="text-right">Bot thắng</th>
                      <th className="text-right">Tỉ lệ thắng bot</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.byRuleset.map(row => (
                      <tr key={row.rulesetId}>
                        <td className="font-medium text-ink">{row.rulesetCode ?? '—'}</td>
                        <td className="text-right tabular-nums">{formatNumber(row.matches)}</td>
                        <td className="text-right tabular-nums text-ok">{formatNumber(row.wins)}</td>
                        <td className="text-right tabular-nums">{formatPercent(row.winRate)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          <p className="flex items-start gap-1.5 text-xs text-ink-dim">
            <Badge tone="neutral">Lưu ý</Badge>
            Backend không lưu latency hay telemetry bot-move, nên console không hiển thị các biểu đồ đó.
          </p>
        </div>
      )}
    </Card>
  );
}

// Metric: Ô số nhỏ trong panel.
function Metric({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="rounded-lg border border-border bg-surface-2 px-3 py-2.5">
      <p className="text-[0.68rem] font-semibold uppercase tracking-wider text-ink-dim">{label}</p>
      <p className="mt-1 text-lg font-semibold tabular-nums text-ink">{value}</p>
      {hint && <p className="text-[0.68rem] text-ink-dim">{hint}</p>}
    </div>
  );
}
