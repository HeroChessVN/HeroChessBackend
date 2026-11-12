// Vai trò file: HTTP client cho Admin API. Base URL tương đối ('/api/v1') để đi qua Vite proxy /
// cùng-origin static hosting — backend không bật CORS nên không được gọi cross-origin.
// Xử lý 401 (refresh token rồi thử lại, hoặc đăng xuất) và 403 (không đủ quyền).

const BASE_URL = (import.meta.env.VITE_ADMIN_API_URL as string | undefined)?.replace(/\/$/, '') ?? '/api/v1';

export class ApiError extends Error {
  readonly status: number;
  readonly code: string | null;

  constructor(status: number, code: string | null, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
  }

  // isPermission: 403 = đã đăng nhập nhưng thiếu quyền miền (không phải lỗi hệ thống).
  get isPermission() {
    return this.status === 403;
  }

  // isUnauthorized: 401 = token hết hạn hoặc không hợp lệ.
  get isUnauthorized() {
    return this.status === 401;
  }
}

// TokenProvider: hook gắn vào AuthContext để client luôn lấy token mới nhất và gọi khi logout.
let accessToken = '';
let onUnauthorized: (() => void) | null = null;
let refreshHandler: (() => Promise<string | null>) | null = null;

export function configureAuth(options: {
  getAccessToken: () => string;
  onUnauthorized: () => void;
  refresh: () => Promise<string | null>;
}): void {
  accessToken = options.getAccessToken();
  onUnauthorized = options.onUnauthorized;
  refreshHandler = options.refresh;
}

export function setAccessToken(token: string): void {
  accessToken = token;
}

// buildQuery: Ghép query param, bỏ qua giá trị null/undefined/rỗng.
export function buildQuery(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue;
    search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

async function parseError(response: Response): Promise<ApiError> {
  let code: string | null = null;
  let message = `${response.status} ${response.statusText}`;
  try {
    const body = (await response.json()) as { code?: string; message?: string; title?: string; detail?: string };
    code = body.code ?? null;
    message = body.message ?? body.title ?? body.detail ?? message;
  } catch {
    // Response không phải JSON (lỗi gateway) — giữ message mặc định.
  }
  return new ApiError(response.status, code, message);
}

// request: Gọi API kèm Bearer token. Tự refresh 1 lần khi 401 rồi thử lại yêu cầu.
async function request<T>(method: string, path: string, body?: unknown, signal?: AbortSignal): Promise<T> {
  const send = async (): Promise<Response> => {
    const headers: Record<string, string> = {};
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    return fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal
    });
  };

  let response = await send();
  if (response.status === 401 && refreshHandler) {
    const fresh = await refreshHandler();
    if (fresh) {
      response = await send();
    } else {
      onUnauthorized?.();
      throw await parseError(response);
    }
  }
  if (!response.ok) throw await parseError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => request<T>('GET', path, undefined, signal),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body),
  patch: <T>(path: string, body?: unknown) => request<T>('PATCH', path, body),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body),
  delete: <T>(path: string) => request<T>('DELETE', path)
};

// login: POST /api/v1/auth/login?useCookies=false — nhận token trong body thay vì cookie.
export async function login(email: string, password: string) {
  const response = await fetch(`${BASE_URL}/auth/login?useCookies=false`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password })
  });
  if (!response.ok) throw await parseError(response);
  return (await response.json()) as {
    tokenType: string;
    accessToken: string;
    expiresIn: number;
    refreshToken: string;
  };
}

// refresh: POST /api/v1/auth/refresh — làm mới access token.
export async function refresh(refreshToken: string) {
  const response = await fetch(`${BASE_URL}/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken })
  });
  if (!response.ok) throw await parseError(response);
  return (await response.json()) as {
    tokenType: string;
    accessToken: string;
    expiresIn: number;
    refreshToken: string;
  };
}
