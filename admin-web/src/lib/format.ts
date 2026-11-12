// Vai trò file: Định dạng dữ liệu hiển thị. Tiền luôn là chuỗi ở API — không ép Number để giữ chính xác bigint.

const numberFormat = new Intl.NumberFormat('vi-VN');
const dateTimeFormat = new Intl.DateTimeFormat('vi-VN', {
  dateStyle: 'medium',
  timeStyle: 'short'
});

// formatCoin: Định dạng chuỗi tiền có phân tách nghìn mà không chuyển sang number.
// Nhóm nghìn bằng regex để giữ nguyên chữ số gốc (giá trị lớn hơn 2^53 vẫn đúng).
export function formatCoin(value: string | number | null | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  const text = String(value);
  const negative = text.startsWith('-');
  const digits = negative ? text.slice(1) : text;
  if (!/^\d+$/.test(digits)) return text;
  return `${negative ? '-' : ''}${digits.replace(/\B(?=(\d{3})+(?!\d))/g, '.')}`;
}

// formatNumber: Dùng cho số đếm (không phải tiền) — an toàn Number.
export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  return numberFormat.format(value);
}

// formatPercent: value là phần trăm 0..100 (backend đã nhân 100 trước khi trả về);
// null = chưa đủ dữ liệu.
export function formatPercent(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  return `${value.toFixed(1)}%`;
}

// formatDateTime: ISO string → giờ Việt Nam; null → '—'.
export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  return dateTimeFormat.format(date);
}

// formatRelative: Khoảng thời gian tương đối so với hiện tại, dùng cho "cập nhật lúc ...".
export function formatRelative(value: string | null | undefined): string {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '—';
  const seconds = Math.round((Date.now() - date.getTime()) / 1000);
  const steps: [number, string][] = [
    [60, 'giây'],
    [3600, 'phút'],
    [86400, 'giờ'],
    [604800, 'ngày'],
    [2629800, 'tuần'],
    [31557600, 'tháng']
  ];
  const labels = ['giây', 'phút', 'giờ', 'ngày', 'tuần', 'tháng'];
  let unit = 0;
  let rest = seconds;
  while (unit < steps.length - 1 && Math.abs(rest) >= steps[unit]![0]) {
    rest = Math.round(rest / steps[unit]![0]);
    unit += 1;
  }
  return `${rest} ${labels[unit]} trước`;
}

// initials: Chữ cái đầu của tên hiển thị, dùng cho monogram thay ảnh nhân vật.
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  if (parts.length === 1) return parts[0]!.slice(0, 2).toUpperCase();
  return `${parts[0]![0] ?? ''}${parts[parts.length - 1]![0] ?? ''}`.toUpperCase();
}
