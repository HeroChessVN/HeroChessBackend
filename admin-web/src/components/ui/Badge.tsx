// Vai trò file: Badge trạng thái nhỏ dùng chung (bật/tắt, trạng thái trận, loại thưởng).

import type { ReactNode } from 'react';

type Tone = 'neutral' | 'ok' | 'warn' | 'danger' | 'info' | 'gold';

const tones: Record<Tone, string> = {
  neutral: 'bg-panel-2 text-ink-muted border-border',
  ok: 'bg-ok-bg text-ok border-ok/30',
  warn: 'bg-warn-bg text-warn border-warn/30',
  danger: 'bg-danger-bg text-danger border-danger/30',
  info: 'bg-info-bg text-info border-info/30',
  gold: 'bg-gold-700/25 text-gold-400 border-gold-600/40'
};

interface BadgeProps {
  tone?: Tone;
  children: ReactNode;
  className?: string;
}

export function Badge({ tone = 'neutral', children, className = '' }: BadgeProps) {
  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${tones[tone]} ${className}`}>
      {children}
    </span>
  );
}

// statusTone: Ánh xạ trạng thái nghiệp vụ sang màu badge.
export function statusTone(status: string): Tone {
  switch (status.toLowerCase()) {
    case 'active':
    case 'completed':
    case 'live':
      return 'ok';
    case 'disabled':
    case 'banned':
    case 'expired':
    case 'cancelled':
      return 'danger';
    case 'pending':
    case 'running':
    case 'draft':
      return 'warn';
    case 'settled':
      return 'info';
    default:
      return 'neutral';
  }
}
