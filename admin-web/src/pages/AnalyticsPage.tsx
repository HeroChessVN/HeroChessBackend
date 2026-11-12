// Vai trò file: Màn Phân tích — tỉ lệ chọn/thắng theo hero, hiệu suất theo phe, tần suất dùng skill,
// tỉ lệ thắng bot theo độ khó và theo bộ luật. Mọi số liệu đến thẳng từ API.
// winRate/pickRate null = chưa đủ dữ liệu → hiển thị "chưa đủ dữ liệu", không hiện 0%.

import { useState } from 'react';
import { BarChart3, Bot, Percent, RefreshCw, Swords, TrendingUp } from 'lucide-react';
import { api } from '../lib/api';
import { useResource } from '../lib/useResource';
import type { AdminBalanceAnalyticsDto, AdminBotAnalyticsDto } from '../lib/types';
import { formatNumber, formatPercent } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { Skeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';

type Mode = 'ranked' | 'bot';

export function AnalyticsPage() {
  const [mode, setMode] = useState<Mode>('ranked');

  const balance = useResource<AdminBalanceAnalyticsDto>(signal => api.get(`/admin/analytics/balance?mode=${mode}`, signal), [mode]);
  const bot = useResource<AdminBotAnalyticsDto>(signal => api.get('/admin/analytics/bot', signal), []);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold tracking-tight text-ink">
            <BarChart3 size={19} className="text-gold-500" />
            Phân tích cân bằng
          </h2>
          <p className="mt-0.5 text-sm text-ink-muted">
            Số liệu tính từ trận đã kết thúc. Cỡ mẫu luôn hiển thị cạnh mọi tỉ lệ.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <div className="flex rounded-lg border border-border bg-surface-2 p-0.5" role="tablist" aria-label="Chọn chế độ">
            {(['ranked', 'bot'] as Mode[]).map(item => (
              <button
                key={item}
                type="button"
                role="tab"
                aria-selected={mode === item}
                onClick={() => setMode(item)}
                className={`rounded-md px-3 py-1 text-xs font-semibold transition-colors ${mode === item ? 'bg-gold-500/15 text-gold-400' : 'text-ink-muted hover:text-ink'}`}
              >
                {item === 'ranked' ? 'Ranked' : 'Bot'}
              </button>
            ))}
          </div>
          <Button
            variant="secondary"
            icon={<RefreshCw size={15} />}
            onClick={() => {
              balance.reload();
              bot.reload();
            }}
          >
            Làm mới
          </Button>
        </div>
      </div>

      {balance.error ? (
        <Card>
          <ErrorState error={balance.error} onRetry={balance.reload} />
        </Card>
      ) : balance.loading ? (
        <Card>
          <Skeleton className="h-64 w-full" />
        </Card>
      ) : balance.data ? (
        <>
          <Card
            title={
              <span className="flex items-center gap-2">
                <TrendingUp size={17} className="text-gold-500" />
                Tổng quan {mode === 'ranked' ? 'ranked' : 'vs bot'}
              </span>
            }
            description={`${formatNumber(balance.data.matchCount)} trận · ${formatNumber(balance.data.playerMatchCount)} lượt thi đấu của người chơi.`}
          >
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Metric label="Số trận" value={formatNumber(balance.data.matchCount)} />
              <Metric
                label="Lợi thế đi trước"
                value={formatPercent(balance.data.firstMoveAdvantage.value)}
                hint={`cỡ mẫu ${formatNumber(balance.data.firstMoveAdvantage.sampleSize)}`}
              />
              <Metric
                label="Thời lượng TB"
                value={
                  balance.data.averageDurationSeconds === null
                    ? 'chưa đủ dữ liệu'
                    : `${formatNumber(Math.round(balance.data.averageDurationSeconds * 10) / 10)}s`
                }
              />
              <Metric label="Skill đã dùng" value={formatNumber(balance.data.skills.length)} />
            </div>
          </Card>

          <Card
            title={
              <span className="flex items-center gap-2">
                <Swords size={17} className="text-gold-500" />
                Tỉ lệ chọn và tỉ lệ thắng theo hero
              </span>
            }
            description="Tỉ lệ chọn = số lần chọn / tổng lượt thi đấu. Tỉ lệ thắng null = chưa đủ dữ liệu."
          >
            {balance.data.heroes.length === 0 ? (
              <EmptyState title="Chưa có trận đã kết thúc để tính tỉ lệ chọn hero" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Hero</th>
                      <th>Phe</th>
                      <th className="text-right">Lượt chọn</th>
                      <th className="text-right">Trận</th>
                      <th className="text-right">Tỉ lệ chọn</th>
                      <th className="text-right">Tỉ lệ thắng</th>
                    </tr>
                  </thead>
                  <tbody>
                    {balance.data.heroes.map(hero => (
                      <tr key={hero.heroId}>
                        <td className="text-ink">
                          {hero.heroName}
                          <span className="ml-1 font-mono text-xs text-ink-dim">{hero.heroCode}</span>
                        </td>
                        <td className="text-ink-muted">{hero.factionCode ?? '—'}</td>
                        <td className="text-right tabular-nums text-ink-muted">{formatNumber(hero.picks)}</td>
                        <td className="text-right tabular-nums text-ink-muted">{formatNumber(hero.matches)}</td>
                        <td className="text-right tabular-nums text-ink">{formatPercent(hero.pickRate)}</td>
                        <td className="text-right tabular-nums text-ink">
                          {hero.winRate === null ? <span className="text-ink-dim">chưa đủ dữ liệu</span> : formatPercent(hero.winRate)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <Card title="Hiệu suất theo phe" description="Tỉ lệ thắng tính trên số trận có kết quả của phe đó.">
            {balance.data.factions.length === 0 ? (
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
                    {balance.data.factions.map(faction => (
                      <tr key={faction.factionCode}>
                        <td className="font-medium text-ink">{faction.factionCode}</td>
                        <td className="text-right tabular-nums">{formatNumber(faction.matches)}</td>
                        <td className="text-right tabular-nums text-ok">{formatNumber(faction.wins)}</td>
                        <td className="text-right tabular-nums text-ink-muted">{formatNumber(faction.draws)}</td>
                        <td className="text-right tabular-nums text-danger">{formatNumber(faction.losses)}</td>
                        <td className="text-right tabular-nums">
                          {faction.winRate === null ? <span className="text-ink-dim">chưa đủ dữ liệu</span> : formatPercent(faction.winRate)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <Card title="Tần suất dùng skill" description="Số lượt dùng thực tế theo skill đã xuất hiện trong trận.">
            {balance.data.skills.length === 0 ? (
              <EmptyState title="Chưa có skill nào được sử dụng" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Skill</th>
                      <th>Mã</th>
                      <th className="text-right">Số lượt dùng</th>
                    </tr>
                  </thead>
                  <tbody>
                    {balance.data.skills.map(skill => (
                      <tr key={skill.skillId}>
                        <td className="text-ink">{skill.skillName}</td>
                        <td className="font-mono text-xs text-ink-dim">{skill.skillCode}</td>
                        <td className="text-right tabular-nums text-ink">{formatNumber(skill.uses)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </>
      ) : null}

      <Card
        title={
          <span className="flex items-center gap-2">
            <Bot size={17} className="text-gold-500" />
            Tỉ lệ thắng bot
          </span>
        }
        description="Số liệu bot tách riêng khỏi cân bằng PvP."
      >
        {bot.error ? (
          <ErrorState error={bot.error} onRetry={bot.reload} />
        ) : bot.loading ? (
          <Skeleton className="h-48 w-full" />
        ) : !bot.data || bot.data.botMatches === 0 ? (
          <EmptyState title="Chưa có trận vs bot" description="Chưa có trận nào kết thúc với bot nên chưa có số liệu." />
        ) : (
          <div className="space-y-5">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Metric label="Trận vs bot" value={formatNumber(bot.data.botMatches)} />
              <Metric label="Bot thắng" value={formatNumber(bot.data.botWins)} />
              <Metric label="Người chơi thắng" value={formatNumber(bot.data.playerWins)} />
              <Metric label="Tỉ lệ thắng bot" value={formatPercent(bot.data.botWinRate)} />
            </div>

            <div>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Theo độ khó</p>
              {bot.data.byDifficulty.length === 0 ? (
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
                      {bot.data.byDifficulty.map(row => (
                        <tr key={row.difficulty}>
                          <td className="font-medium text-ink">{row.difficulty}</td>
                          <td className="text-right tabular-nums">{formatNumber(row.matches)}</td>
                          <td className="text-right tabular-nums text-ok">{formatNumber(row.wins)}</td>
                          <td className="text-right tabular-nums text-ink-muted">{formatNumber(row.draws)}</td>
                          <td className="text-right tabular-nums text-danger">{formatNumber(row.losses)}</td>
                          <td className="text-right tabular-nums">
                            {row.winRate === null ? <span className="text-ink-dim">chưa đủ dữ liệu</span> : formatPercent(row.winRate)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            <div>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Theo bộ luật</p>
              {bot.data.byRuleset.length === 0 ? (
                <EmptyState title="Chưa có dữ liệu bộ luật" />
              ) : (
                <div className="table-scroll">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Bộ luật</th>
                        <th className="text-right">Trận</th>
                        <th className="text-right">Bot thắng</th>
                        <th className="text-right">Hòa</th>
                        <th className="text-right">Tỉ lệ thắng bot</th>
                      </tr>
                    </thead>
                    <tbody>
                      {bot.data.byRuleset.map(row => (
                        <tr key={row.rulesetId}>
                          <td className="font-medium text-ink">{row.rulesetCode ?? '—'}</td>
                          <td className="text-right tabular-nums">{formatNumber(row.matches)}</td>
                          <td className="text-right tabular-nums text-ok">{formatNumber(row.wins)}</td>
                          <td className="text-right tabular-nums text-ink-muted">{formatNumber(row.draws)}</td>
                          <td className="text-right tabular-nums">
                            {row.winRate === null ? <span className="text-ink-dim">chưa đủ dữ liệu</span> : formatPercent(row.winRate)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            <p className="flex flex-wrap items-center gap-1.5 text-xs text-ink-dim">
              <Badge tone="neutral">Lưu ý</Badge>
              Backend không lưu latency hay telemetry bot-move, nên console không hiển thị các biểu đồ đó.
            </p>
          </div>
        )}
      </Card>

      <Card
        title={
          <span className="flex items-center gap-2">
            <Percent size={17} className="text-gold-500" />
            Cách đọc số liệu
          </span>
        }
      >
        <ul className="space-y-1.5 text-sm text-ink-muted">
          <li>• Tỉ lệ trả về trên thang 0–100 (22.22 nghĩa là 22.22%), không nhân thêm lần nữa.</li>
          <li>• Giá trị null = chưa đủ dữ liệu — hiển thị "chưa đủ dữ liệu" thay vì 0%.</li>
          <li>• Mọi tỉ lệ đi kèm cỡ mẫu (số trận) để biết mức độ tin cậy.</li>
        </ul>
      </Card>
    </div>
  );
}

// Metric: Ô số nhỏ.
function Metric({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="rounded-lg border border-border bg-surface-2 px-3 py-2.5">
      <p className="text-[0.68rem] font-semibold uppercase tracking-wider text-ink-dim">{label}</p>
      <p className="mt-1 text-lg font-semibold tabular-nums text-ink">{value}</p>
      {hint && <p className="text-[0.68rem] text-ink-dim">{hint}</p>}
    </div>
  );
}
