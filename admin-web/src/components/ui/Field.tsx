// Vai trò file: Trường nhập liệu có nhãn, gợi ý và thông báo lỗi. Dùng chung cho form admin.

import { useId } from 'react';
import type { ReactNode } from 'react';

interface FieldProps {
  label: string;
  hint?: ReactNode;
  error?: string | null;
  required?: boolean;
  children: (props: { id: string; 'aria-describedby': string | undefined; 'aria-invalid': boolean }) => ReactNode;
}

export function Field({ label, hint, error, required = false, children }: FieldProps) {
  const id = useId();
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined;

  return (
    <div>
      <label className="field-label" htmlFor={id}>
        {label}
        {required && <span className="ml-1 text-danger">*</span>}
      </label>
      {children({ id, 'aria-describedby': describedBy, 'aria-invalid': Boolean(error) })}
      {error ? (
        <p id={`${id}-error`} className="mt-1 text-xs text-danger">
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className="mt-1 text-xs text-ink-dim">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
