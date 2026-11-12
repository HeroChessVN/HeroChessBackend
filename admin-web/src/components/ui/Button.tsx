// Vai trò file: Nút dùng chung. Biến thể theo mức độ ưu tiên; `danger` dùng cho thao tác phá huỷ dữ liệu.

import type { ButtonHTMLAttributes, ReactNode } from 'react';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  loading?: boolean;
  icon?: ReactNode;
}

const variants: Record<Variant, string> = {
  primary: 'bg-gold-500 text-navy-950 hover:bg-gold-400 border border-gold-600 font-semibold',
  secondary: 'bg-panel-2 text-ink hover:bg-navy-700 border border-border',
  ghost: 'bg-transparent text-ink-muted hover:text-ink hover:bg-panel-2 border border-transparent',
  danger: 'bg-danger-bg text-danger hover:bg-[#5a2f33] border border-[#7d4045] font-semibold'
};

export function Button({ variant = 'secondary', loading = false, icon, children, className = '', disabled, ...rest }: ButtonProps) {
  return (
    <button
      type="button"
      disabled={disabled || loading}
      className={`inline-flex items-center justify-center gap-2 rounded-lg px-3.5 py-2 text-sm transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${variants[variant]} ${className}`}
      {...rest}
    >
      {loading ? <span className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent" aria-hidden /> : icon}
      {children}
    </button>
  );
}
