// Vai trò file: Màn Cửa hàng — danh sách hero bán, đổi giá, bật/tắt bán, đổi giá hàng loạt và lịch sử mua.
// Mọi thao tác ghi đều cần `reason` và có hộp thoại xác nhận (theo ADMIN_API_GUIDE).

import { useCallback, useMemo, useState } from 'react';
import { Filter, Percent, Receipt, RefreshCw, Search, ShoppingBag } from 'lucide-react';
import { api, buildQuery } from '../lib/api';
import { usePagedResource } from '../lib/useResource';
import { useAuth } from '../auth/AuthContext';
import { canWrite, PERMISSION_SHOP } from '../lib/permissions';
import type {
  AdminBatchPriceResultDto,
  AdminPurchasePageDto,
  AdminShopHeroDto,
  AdminShopHeroPageDto
} from '../lib/types';
import { formatCoin, formatDateTime, formatNumber } from '../lib/format';
import { Card } from '../components/ui/Card';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { TableSkeleton } from '../components/ui/Skeleton';
import { ErrorState } from '../components/ui/ErrorState';
import { EmptyState } from '../components/ui/EmptyState';
import { ConfirmDialog } from '../components/ui/ConfirmDialog';
import { useToast } from '../components/ui/Toast';

interface Filters {
  query: string;
  inShop: '' | 'true' | 'false';
}

const emptyFilters: Filters = { query: '', inShop: '' };

export function ShopPage() {
  const { permissions } = useAuth();
  const writable = canWrite(permissions, PERMISSION_SHOP);
  const { notify } = useToast();

  const [filters, setFilters] = useState<Filters>(emptyFilters);
  const [applied, setApplied] = useState<Filters>(emptyFilters);

  // Đối tượng đang sửa giá / bật-tắt bán.
  const [editing, setEditing] = useState<AdminShopHeroDto | null>(null);
  const [price, setPrice] = useState('');
  const [inShop, setInShop] = useState(false);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);

  // Đổi giá hàng loạt: chỉ các dòng được tick mới được gửi.
  const [bulkOpen, setBulkOpen] = useState(false);
  const [selected, setSelected] = useState<Record<string, string>>({});
  const [bulkReason, setBulkReason] = useState('');
  const [bulkResults, setBulkResults] = useState<AdminBatchPriceResultDto['results'] | null>(null);

  const load = useCallback(
    (cursor: string | null, signal: AbortSignal) =>
      api.get<AdminShopHeroPageDto>(
        `/admin/shop/heroes${buildQuery({
          query: applied.query,
          inShop: applied.inShop === '' ? undefined : applied.inShop,
          cursor: cursor ?? undefined,
          limit: 50
        })}`,
        signal
      ),
    [applied]
  );

  const heroes = usePagedResource(load, [applied]);

  const purchases = usePagedResource(
    useCallback(
      (cursor: string | null, signal: AbortSignal) =>
        api.get<AdminPurchasePageDto>(`/admin/shop/purchases${buildQuery({ cursor: cursor ?? undefined, limit: 25 })}`, signal),
      []
    ),
    []
  );

  const selectedCount = Object.keys(selected).length;

  const openEdit = (item: AdminShopHeroDto) => {
    setEditing(item);
    setPrice(item.hero.coinPrice);
    setInShop(item.hero.isInShop);
    setReason('');
  };

  const saveEdit = async () => {
    if (!editing) return;
    if (!/^\d+$/.test(price)) {
      notify('Giá phải là số nguyên không âm.', 'danger');
      return;
    }
    if (reason.trim().length === 0) {
      notify('Bắt buộc có lý do khi đổi giá hoặc bật/tắt bán.', 'danger');
      return;
    }
    setBusy(true);
    try {
      await api.patch(`/admin/shop/heroes/${editing.hero.id}`, {
        coinPrice: price,
        isInShop: inShop,
        reason: reason.trim()
      });
      notify(`Đã cập nhật cửa hàng cho hero ${editing.hero.name}.`);
      setEditing(null);
      heroes.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không cập nhật được cửa hàng.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const toggleInShop = (item: AdminShopHeroDto) => {
    openEdit(item);
    setInShop(!item.hero.isInShop);
  };

  const runBatch = async () => {
    const items = Object.entries(selected).map(([heroId, coinPrice]) => ({ heroId, coinPrice }));
    if (items.length === 0) {
      notify('Chọn ít nhất một hero để đổi giá hàng loạt.', 'danger');
      return;
    }
    if (bulkReason.trim().length === 0) {
      notify('Bắt buộc có lý do khi đổi giá hàng loạt.', 'danger');
      return;
    }
    const invalid = items.find(item => !/^\d+$/.test(item.coinPrice));
    if (invalid) {
      notify(`Giá của hero ${invalid.heroId.slice(0, 8)}… không phải số nguyên không âm.`, 'danger');
      return;
    }
    setBusy(true);
    try {
      const result = await api.patch<AdminBatchPriceResultDto>('/admin/shop/prices', { items, reason: bulkReason.trim() });
      setBulkResults(result.results);
      const failed = result.results.filter(row => !row.succeeded);
      notify(
        failed.length === 0
          ? `Đã đổi giá ${result.results.length} hero.`
          : `Đổi giá ${result.results.length - failed.length}/${result.results.length} hero thành công, ${failed.length} dòng lỗi.`,
        failed.length === 0 ? 'ok' : 'danger'
      );
      setSelected({});
      heroes.reload();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không đổi được giá hàng loạt.', 'danger');
    } finally {
      setBusy(false);
    }
  };

  const bulkRows = useMemo(
    () => heroes.items.filter(item => item.hero.id in selected).map(item => ({ item, coinPrice: selected[item.hero.id] ?? item.hero.coinPrice })),
    [heroes.items, selected]
  );

  return (
    <div className="space-y-5">
      <Card
        title={
          <span className="flex items-center gap-2">
            <ShoppingBag size={18} className="text-gold-500" />
            Danh sách hero trong cửa hàng
          </span>
        }
        description={`${formatNumber(heroes.total)} hero trong danh mục · đổi giá ảnh hưởng fingerprint nội dung, trận đang chọn lineup có thể nhận CONTENT_VERSION_CHANGED.`}
        actions={
          <>
            <Button variant="secondary" icon={<RefreshCw size={15} />} onClick={heroes.reload}>
              Làm mới
            </Button>
            {writable && (
              <Button
                variant="primary"
                icon={<Percent size={15} />}
                onClick={() => {
                  setBulkResults(null);
                  setBulkReason('');
                  setBulkOpen(true);
                }}
              >
                Đổi giá hàng loạt{selectedCount > 0 ? ` (${selectedCount})` : ''}
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
            <label className="field-label" htmlFor="shop-search">
              Tìm kiếm
            </label>
            <div className="relative">
              <Search size={15} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-dim" />
              <input
                id="shop-search"
                className="input pl-8"
                placeholder="Tên hoặc mã hero"
                value={filters.query}
                onChange={event => setFilters(current => ({ ...current, query: event.target.value }))}
              />
            </div>
          </div>
          <div>
            <label className="field-label" htmlFor="shop-in-shop">
              Trong cửa hàng
            </label>
            <select
              id="shop-in-shop"
              className="input"
              value={filters.inShop}
              onChange={event => setFilters(current => ({ ...current, inShop: event.target.value as Filters['inShop'] }))}
            >
              <option value="">Tất cả</option>
              <option value="true">Đang bán</option>
              <option value="false">Không bán</option>
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
          <EmptyState title="Không có hero nào khớp bộ lọc" description="Thử xoá bộ lọc để xem toàn bộ danh mục hero." />
        ) : (
          <>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    {writable && <th className="w-10">Chọn</th>}
                    <th>Hero</th>
                    <th className="text-right">Giá</th>
                    <th className="text-right">Lượt mua</th>
                    <th>Trạng thái</th>
                    <th className="text-right">Thao tác</th>
                  </tr>
                </thead>
                <tbody>
                  {heroes.items.map(item => (
                    <tr key={item.hero.id}>
                      {writable && (
                        <td>
                          <input
                            type="checkbox"
                            aria-label={`Chọn ${item.hero.name}`}
                            checked={item.hero.id in selected}
                            onChange={event =>
                              setSelected(current => {
                                const next = { ...current };
                                if (event.target.checked) next[item.hero.id] = item.hero.coinPrice;
                                else delete next[item.hero.id];
                                return next;
                              })
                            }
                          />
                        </td>
                      )}
                      <td>
                        <p className="font-medium text-ink">{item.hero.name}</p>
                        <p className="font-mono text-xs text-ink-dim">{item.hero.code}</p>
                      </td>
                      <td className="text-right tabular-nums text-ink">{formatCoin(item.hero.coinPrice)}</td>
                      <td className="text-right tabular-nums text-ink-muted">{formatNumber(item.purchaseCount)}</td>
                      <td>
                        <div className="flex flex-wrap gap-1.5">
                          <Badge tone={item.hero.isInShop ? 'gold' : 'neutral'}>{item.hero.isInShop ? 'Đang bán' : 'Không bán'}</Badge>
                          <Badge tone={item.purchasable ? 'ok' : 'warn'}>{item.purchasable ? 'Mua được' : 'Không mua được'}</Badge>
                          {!item.hero.isEnabled && <Badge tone="danger">Hero tắt</Badge>}
                        </div>
                      </td>
                      <td className="text-right">
                        {writable && (
                          <div className="flex justify-end gap-1.5">
                            <Button variant="secondary" onClick={() => openEdit(item)}>
                              Sửa giá
                            </Button>
                            <Button variant={item.hero.isInShop ? 'danger' : 'ghost'} onClick={() => toggleInShop(item)}>
                              {item.hero.isInShop ? 'Ngừng bán' : 'Bán lại'}
                            </Button>
                          </div>
                        )}
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

      <Card
        title={
          <span className="flex items-center gap-2">
            <Receipt size={18} className="text-gold-500" />
            Lịch sử mua
          </span>
        }
        description={`${formatNumber(purchases.total)} giao dịch mua hero · nguồn từ ledger coin, không phải bảng đếm phụ.`}
      >
        {purchases.error ? (
          <ErrorState error={purchases.error} onRetry={purchases.reload} />
        ) : purchases.loading ? (
          <TableSkeleton rows={4} columns={4} />
        ) : purchases.items.length === 0 ? (
          <EmptyState title="Chưa có giao dịch mua nào" />
        ) : (
          <>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Thời điểm</th>
                    <th>Người chơi</th>
                    <th>Hero</th>
                    <th className="text-right">Số tiền</th>
                  </tr>
                </thead>
                <tbody>
                  {purchases.items.map(row => (
                    <tr key={row.id}>
                      <td className="whitespace-nowrap text-ink-muted">{formatDateTime(row.createdAt)}</td>
                      <td className="text-ink">{row.playerDisplayName ?? '—'}</td>
                      <td className="font-mono text-xs text-ink-muted">{row.heroCode ?? '—'}</td>
                      <td className="text-right tabular-nums text-ink">{formatCoin(row.amount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {purchases.nextCursor && (
              <div className="mt-4 flex justify-center">
                <Button variant="secondary" onClick={purchases.loadMore} loading={purchases.loadingMore}>
                  Tải thêm
                </Button>
              </div>
            )}
          </>
        )}
      </Card>

      {/* Sửa giá / bật-tắt bán một hero */}
      <ConfirmDialog
        open={editing !== null}
        title={editing ? `Cập nhật cửa hàng — ${editing.hero.name}` : ''}
        description="Giá mới áp dụng ngay. Trận đang chọn lineup có thể nhận CONTENT_VERSION_CHANGED."
        confirmLabel="Lưu"
        tone="primary"
        loading={busy}
        onCancel={() => setEditing(null)}
        onConfirm={saveEdit}
      >
        <div className="space-y-3">
          <div>
            <label className="field-label" htmlFor="shop-price">
              Giá coin
            </label>
            <input id="shop-price" className="input tabular-nums" value={price} onChange={event => setPrice(event.target.value)} />
          </div>
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={inShop} onChange={event => setInShop(event.target.checked)} />
            Đang bán trong cửa hàng
          </label>
          <div>
            <label className="field-label" htmlFor="shop-reason">
              Lý do <span className="text-danger">*</span>
            </label>
            <textarea
              id="shop-reason"
              className="input min-h-20"
              value={reason}
              onChange={event => setReason(event.target.value)}
              placeholder="Ví dụ: giảm giá đợt tháng 11"
            />
            <p className="mt-1 text-xs text-ink-dim">Bắt buộc, tối đa 500 ký tự.</p>
          </div>
        </div>
      </ConfirmDialog>

      {/* Đổi giá hàng loạt */}
      <ConfirmDialog
        open={bulkOpen}
        title="Đổi giá hàng loạt"
        description="Mỗi dòng được xử lý độc lập — dòng lỗi không làm mất các dòng đã đổi thành công."
        confirmLabel="Đổi giá"
        tone="danger"
        loading={busy}
        onCancel={() => {
          setBulkOpen(false);
          setBulkResults(null);
        }}
        onConfirm={runBatch}
      >
        <div className="space-y-3">
          {bulkRows.length === 0 ? (
            <p className="text-sm text-ink-muted">Chưa chọn hero nào. Tick ở cột "Chọn" của bảng phía trên.</p>
          ) : (
            <ul className="max-h-56 space-y-2 overflow-y-auto">
              {bulkRows.map(row => (
                <li key={row.item.hero.id} className="flex items-center gap-2">
                  <span className="min-w-0 flex-1 truncate text-sm text-ink">{row.item.hero.name}</span>
                  <input
                    className="input w-28 tabular-nums"
                    aria-label={`Giá mới cho ${row.item.hero.name}`}
                    value={row.coinPrice}
                    onChange={event =>
                      setSelected(current => ({ ...current, [row.item.hero.id]: event.target.value }))
                    }
                  />
                </li>
              ))}
            </ul>
          )}
          <div>
            <label className="field-label" htmlFor="bulk-reason">
              Lý do <span className="text-danger">*</span>
            </label>
            <textarea
              id="bulk-reason"
              className="input min-h-20"
              value={bulkReason}
              onChange={event => setBulkReason(event.target.value)}
              placeholder="Ví dụ: đồng giá đợt sale tháng 11"
            />
          </div>
          {bulkResults && bulkResults.length > 0 && (
            <ul className="max-h-40 space-y-1 overflow-y-auto rounded-lg border border-border bg-surface-2 p-2 text-xs">
              {bulkResults.map(row => (
                <li key={row.heroId} className={row.succeeded ? 'text-ok' : 'text-danger'}>
                  {row.succeeded ? '✓' : '✗'} {row.heroId.slice(0, 8)}… {row.succeeded ? formatCoin(row.coinPrice) : row.errorMessage ?? row.errorCode}
                </li>
              ))}
            </ul>
          )}
        </div>
      </ConfirmDialog>
    </div>
  );
}
