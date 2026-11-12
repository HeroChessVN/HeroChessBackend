// Vai trò file: Drawer chi tiết hero — thông tin, skin, skill liên kết, số chủ sở hữu, doanh thu,
// lịch sử đổi giá và gán phe. Skill chỉ xem: không có endpoint sửa hiệu ứng gameplay.

import { useCallback, useEffect, useState } from 'react';
import { Coins, Crown, Layers, ScrollText, Tag, Users } from 'lucide-react';
import { api } from '../../lib/api';
import { formatCoin, formatDateTime, formatNumber } from '../../lib/format';
import { classLabel } from '../../lib/classLabel';
import type { AdminFactionDto, AdminHeroDetailDto } from '../../lib/types';
import { Drawer } from '../ui/Drawer';
import { Badge, statusTone } from '../ui/Badge';
import { Button } from '../ui/Button';
import { Field } from '../ui/Field';
import { Skeleton } from '../ui/Skeleton';
import { ErrorState } from '../ui/ErrorState';
import { EmptyState } from '../ui/EmptyState';
import { useToast } from '../ui/Toast';

interface HeroDetailDrawerProps {
  heroId: string;
  writable: boolean;
  onClose: () => void;
  onChanged: () => void;
}

export function HeroDetailDrawer({ heroId, writable, onClose, onChanged }: HeroDetailDrawerProps) {
  const { notify } = useToast();
  const [detail, setDetail] = useState<AdminHeroDetailDto | null>(null);
  const [factions, setFactions] = useState<AdminFactionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [factionId, setFactionId] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [heroDetail, factionList] = await Promise.all([
        api.get<AdminHeroDetailDto>(`/admin/heroes/${heroId}`),
        api.get<AdminFactionDto[]>('/admin/factions')
      ]);
      setDetail(heroDetail);
      setFactions(factionList);
      setFactionId(heroDetail.hero.factionCodes.length > 0 ? (factionList.find(x => x.code === heroDetail.hero.factionCodes[0])?.id ?? '') : '');
    } catch (cause) {
      setError(cause);
    } finally {
      setLoading(false);
    }
  }, [heroId]);

  useEffect(() => {
    void load();
  }, [load]);

  const assignFaction = async () => {
    if (reason.trim().length === 0) {
      notify('Bắt buộc có lý do khi gán phe.', 'danger');
      return;
    }
    setSaving(true);
    try {
      await api.put(`/admin/heroes/${heroId}/faction`, { factionId: factionId || null, reason: reason.trim() });
      notify('Đã cập nhật phe của hero.');
      setReason('');
      await load();
      onChanged();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không gán được phe.', 'danger');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer open title={detail?.hero.name ?? 'Chi tiết hero'} subtitle={detail?.hero.code} onClose={onClose} width="max-w-3xl">
      {error ? (
        <ErrorState error={error} onRetry={load} />
      ) : loading || !detail ? (
        <div className="space-y-4" role="status" aria-label="Đang tải chi tiết hero">
          <Skeleton className="h-6 w-1/3" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-40 w-full" />
        </div>
      ) : (
        <div className="space-y-5">
          <section className="grid gap-3 sm:grid-cols-2">
            <InfoTile icon={Tag} label="Lớp" value={classLabel(detail.hero.classCode)} hint={detail.hero.classCode} />
            <InfoTile icon={Crown} label="Phe" value={detail.hero.factionCodes.length > 0 ? detail.hero.factionCodes.join(', ') : 'Chưa gán'} />
            <InfoTile icon={Coins} label="Giá coin" value={formatCoin(detail.hero.coinPrice)} />
            <InfoTile icon={Users} label="Số chủ sở hữu" value={formatNumber(detail.ownerCount)} />
            <InfoTile icon={Layers} label="Doanh thu" value={formatNumber(detail.totalRevenue)} hint="Tổng giá trị bán ra" />
            <InfoTile icon={ScrollText} label="Setup Points" value={String(detail.hero.setupPoints)} hint="Luật legacy read-only" />
          </section>

          <section>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Trạng thái</p>
            <div className="flex flex-wrap gap-1.5">
              <Badge tone={detail.hero.isEnabled ? 'ok' : 'danger'}>{detail.hero.isEnabled ? 'Đang bật' : 'Đang tắt'}</Badge>
              <Badge tone={detail.hero.isInShop ? 'gold' : 'neutral'}>{detail.hero.isInShop ? 'Trong cửa hàng' : 'Không bán'}</Badge>
              {detail.hero.isStarter && <Badge tone="info">Starter</Badge>}
              {detail.hero.isTestFixture && <Badge tone="warn">Fixture test</Badge>}
              {detail.hero.traitCode && <Badge tone="neutral">Trait: {detail.hero.traitCode}</Badge>}
            </div>
          </section>

          {detail.hero.description && (
            <section>
              <p className="mb-1 text-xs font-semibold uppercase tracking-wider text-ink-muted">Mô tả</p>
              <p className="text-sm leading-relaxed text-ink-muted">{detail.hero.description}</p>
            </section>
          )}

          {writable && (
            <section className="rounded-lg border border-border bg-surface-2 p-4">
              <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-ink-muted">Gán phe</p>
              <div className="grid gap-3 sm:grid-cols-2">
                <Field label="Phe (tối đa 1 phe mỗi hero)">
                  {props => (
                    <select {...props} className="input" value={factionId} onChange={event => setFactionId(event.target.value)}>
                      <option value="">— Bỏ phe —</option>
                      {factions.map(faction => (
                        <option key={faction.id} value={faction.id}>
                          {faction.name} ({faction.code}) · {faction.heroCount} hero
                        </option>
                      ))}
                    </select>
                  )}
                </Field>
                <Field label="Lý do" required>
                  {props => <textarea {...props} className="input min-h-20" value={reason} onChange={event => setReason(event.target.value)} />}
                </Field>
              </div>
              <div className="mt-3 flex justify-end">
                <Button variant="primary" onClick={assignFaction} loading={saving}>
                  Lưu phe
                </Button>
              </div>
            </section>
          )}

          <section>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Skill liên kết (chỉ xem)</p>
            {detail.skills.length === 0 ? (
              <EmptyState title="Hero chưa gắn skill nào" />
            ) : (
              <ul className="space-y-1.5">
                {detail.skills.map(skill => (
                  <li key={skill.id} className="flex flex-wrap items-center gap-2 rounded-lg border border-border bg-surface-2 px-3 py-2">
                    <span className="font-medium text-ink">{skill.name}</span>
                    <span className="font-mono text-xs text-ink-dim">{skill.code}</span>
                    <Badge tone={skill.status === 'LIVE' ? 'ok' : 'warn'}>{skill.status}</Badge>
                    {skill.factionCode && <Badge tone="neutral">{skill.factionCode}</Badge>}
                    {skill.maxUses !== null && <span className="text-xs text-ink-muted">Tối đa {skill.maxUses} lượt</span>}
                    {skill.cooldownTurns !== null && <span className="text-xs text-ink-muted">Hồi chiêu {skill.cooldownTurns} lượt</span>}
                    <span className="ml-auto text-xs text-ink-dim">key: {skill.implementationKey}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Skin</p>
            {detail.cosmetics.length === 0 ? (
              <EmptyState title="Chưa có skin cho hero này" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Tên</th>
                      <th>Mã</th>
                      <th className="text-right">Giá</th>
                      <th>Trạng thái</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.cosmetics.map(cosmetic => (
                      <tr key={cosmetic.id}>
                        <td className="font-medium text-ink">{cosmetic.name}</td>
                        <td className="font-mono text-xs text-ink-dim">{cosmetic.code}</td>
                        <td className="text-right tabular-nums">{formatCoin(cosmetic.coinPrice)}</td>
                        <td>
                          <Badge tone={cosmetic.isEnabled ? statusTone('active') : statusTone('disabled')}>{cosmetic.isEnabled ? 'Bật' : 'Tắt'}</Badge>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-ink-muted">Lịch sử đổi giá</p>
            {detail.priceHistory.length === 0 ? (
              <EmptyState title="Chưa có thay đổi giá nào" />
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Thời điểm</th>
                      <th className="text-right">Giá cũ</th>
                      <th className="text-right">Giá mới</th>
                      <th>Lý do</th>
                    </tr>
                  </thead>
                  <tbody>
                    {detail.priceHistory.map(change => (
                      <tr key={change.id}>
                        <td className="whitespace-nowrap text-ink-muted">{formatDateTime(change.changedAt)}</td>
                        <td className="text-right tabular-nums text-ink-dim">{formatCoin(change.coinPriceBefore)}</td>
                        <td className="text-right tabular-nums text-ink">{formatCoin(change.coinPriceAfter)}</td>
                        <td className="text-ink-muted">{change.reason ?? '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      )}
    </Drawer>
  );
}

// InfoTile: Ô thông tin nhỏ trong phần đầu drawer.
function InfoTile({
  icon: Icon,
  label,
  value,
  hint
}: {
  icon: typeof Tag;
  label: string;
  value: string;
  hint?: string;
}) {
  return (
    <div className="rounded-lg border border-border bg-surface-2 px-3 py-2.5">
      <p className="flex items-center gap-1.5 text-[0.68rem] font-semibold uppercase tracking-wider text-ink-dim">
        <Icon size={12} />
        {label}
      </p>
      <p className="mt-1 text-sm font-semibold text-ink">{value}</p>
      {hint && <p className="text-[0.68rem] text-ink-dim">{hint}</p>}
    </div>
  );
}
