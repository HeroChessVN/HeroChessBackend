// Vai trò file: Hook tải dữ liệu dùng chung: loading, error, retry. Bỏ qua response của request đã bị huỷ.

import { useCallback, useEffect, useRef, useState } from 'react';

interface ResourceState<T> {
  data: T | null;
  error: unknown;
  loading: boolean;
  reload: () => void;
}

export function useResource<T>(loader: (signal: AbortSignal) => Promise<T>, deps: unknown[]): ResourceState<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);
  const [nonce, setNonce] = useState(0);
  const loaderRef = useRef(loader);
  loaderRef.current = loader;

  useEffect(() => {
    const controller = new AbortController();
    let cancelled = false;
    setLoading(true);
    setError(null);
    loaderRef
      .current(controller.signal)
      .then(result => {
        if (cancelled) return;
        setData(result);
        setLoading(false);
      })
      .catch(cause => {
        if (cancelled || controller.signal.aborted) return;
        setError(cause);
        setLoading(false);
      });
    return () => {
      cancelled = true;
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce]);

  const reload = useCallback(() => setNonce(current => current + 1), []);
  return { data, error, loading, reload };
}

interface Page<T> {
  items: T[];
  nextCursor: string | null;
  // total: endpoint audit không trả tổng — để optional, fallback về số dòng đã tải.
  total?: number;
}

interface PagedState<T> {
  items: T[];
  nextCursor: string | null;
  total: number;
  error: unknown;
  loading: boolean;
  loadingMore: boolean;
  reload: () => void;
  loadMore: () => void;
}

// usePagedResource: Phân trang keyset — nối chuỗi cursor, không dùng offset.
// `loader` nhận cursor (null cho trang đầu) và trả về đúng shape { items, nextCursor, total }.
export function usePagedResource<T>(loader: (cursor: string | null, signal: AbortSignal) => Promise<Page<T>>, deps: unknown[]): PagedState<T> {
  const [items, setItems] = useState<T[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [nonce, setNonce] = useState(0);
  const loaderRef = useRef(loader);
  loaderRef.current = loader;

  useEffect(() => {
    const controller = new AbortController();
    let cancelled = false;
    setLoading(true);
    setError(null);
    loaderRef
      .current(null, controller.signal)
      .then(page => {
        if (cancelled) return;
        setItems(page.items);
        setNextCursor(page.nextCursor);
        setTotal(page.total ?? page.items.length);
        setLoading(false);
      })
      .catch(cause => {
        if (cancelled || controller.signal.aborted) return;
        setError(cause);
        setLoading(false);
      });
    return () => {
      cancelled = true;
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, nonce]);

  const loadMore = useCallback(() => {
    if (!nextCursor || loadingMore) return;
    setLoadingMore(true);
    loaderRef
      .current(nextCursor, new AbortController().signal)
      .then(page => {
        setItems(current => [...current, ...page.items]);
        setNextCursor(page.nextCursor);
        setTotal(current => page.total ?? current + page.items.length);
        setLoadingMore(false);
      })
      .catch(cause => {
        setError(cause);
        setLoadingMore(false);
      });
  }, [nextCursor, loadingMore]);

  const reload = useCallback(() => {
    setItems([]);
    setNextCursor(null);
    setNonce(current => current + 1);
  }, []);

  return { items, nextCursor, total, error, loading, loadingMore, reload, loadMore };
}
