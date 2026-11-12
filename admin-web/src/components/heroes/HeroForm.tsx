// Vai trò file: Form tạo/sửa hero. Validate trước khi gửi để khớp AdminValidation của backend
// (name ≤120, code [A-Z0-9_-] ≤80, coinPrice là số nguyên không dấu phẩy, reason bắt buộc ≤500).
// Setup Points là luật legacy chưa được duyệt — hiển thị read-only, không cho chỉnh ở màn sửa.

import { useState } from 'react';
import { Save, X } from 'lucide-react';
import { api } from '../../lib/api';
import { CLASS_CODES, classLabel } from '../../lib/classLabel';
import type { AdminHeroDto, AdminHeroUpdateRequest, AdminHeroUpsertRequest } from '../../lib/types';
import { Button } from '../ui/Button';
import { Field } from '../ui/Field';
import { Drawer } from '../ui/Drawer';
import { useToast } from '../ui/Toast';

interface HeroFormProps {
  hero: AdminHeroDto | null;
  onClose: () => void;
  onSaved: () => void;
}

interface FormState {
  code: string;
  name: string;
  classCode: string;
  setupPoints: string;
  coinPrice: string;
  isEnabled: boolean;
  isInShop: boolean;
  isStarter: boolean;
  assetKey: string;
  description: string;
  reason: string;
}

const blank: FormState = {
  code: '',
  name: '',
  classCode: CLASS_CODES[0] ?? 'ADVISOR',
  setupPoints: '1',
  coinPrice: '0',
  isEnabled: true,
  isInShop: false,
  isStarter: false,
  assetKey: '',
  description: '',
  reason: ''
};

export function HeroForm({ hero, onClose, onSaved }: HeroFormProps) {
  const { notify } = useToast();
  const [form, setForm] = useState<FormState>(() =>
    hero
      ? {
          code: hero.code,
          name: hero.name,
          classCode: hero.classCode,
          setupPoints: String(hero.setupPoints),
          coinPrice: hero.coinPrice,
          isEnabled: hero.isEnabled,
          isInShop: hero.isInShop,
          isStarter: hero.isStarter,
          assetKey: hero.assetKey ?? '',
          description: hero.description ?? '',
          reason: ''
        }
      : blank
  );
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({});
  const [saving, setSaving] = useState(false);

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => setForm(current => ({ ...current, [key]: value }));

  const validate = (): boolean => {
    const next: Partial<Record<keyof FormState, string>> = {};
    const name = form.name.trim();
    if (!name) next.name = 'Bắt buộc có tên hiển thị.';
    else if (name.length > 120) next.name = 'Tối đa 120 ký tự.';

    const code = form.code.trim();
    if (code && (!/^[A-Za-z0-9_-]{1,80}$/.test(code))) next.code = 'Chỉ chữ, số, "-" và "_" (tối đa 80 ký tự).';

    if (!/^\d+$/.test(form.coinPrice)) next.coinPrice = 'Giá phải là số nguyên không dấu phẩy.';
    if (!/^\d+$/.test(form.setupPoints)) next.setupPoints = 'Setup Points phải là số nguyên không âm.';
    else if (form.classCode !== 'GENERAL' && Number(form.setupPoints) < 1)
      next.setupPoints = 'Chỉ hero lớp Tướng quân (GENERAL) mới được có 0 điểm setup; lớp khác cần tối thiểu 1.';
    if (!hero && form.reason.trim().length === 0) next.reason = 'Bắt buộc có lý do.';
    if (form.reason.trim().length > 500) next.reason = 'Tối đa 500 ký tự.';

    setErrors(next);
    return Object.keys(next).length === 0;
  };

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!validate()) return;
    setSaving(true);
    try {
      if (hero) {
        // Sửa: chỉ gửi trường thay đổi — null nghĩa là giữ nguyên theo AdminHeroUpdateRequest.
        const payload: AdminHeroUpdateRequest = {
          name: form.name.trim(),
          isEnabled: form.isEnabled,
          assetKey: form.assetKey.trim() || null,
          description: form.description.trim() || null,
          reason: form.reason.trim() || null
        };
        await api.patch(`/admin/heroes/${hero.id}`, payload);
        notify(`Đã cập nhật hero ${form.name.trim()}.`);
      } else {
        const payload: AdminHeroUpsertRequest = {
          code: form.code.trim() || null,
          name: form.name.trim(),
          classCode: form.classCode,
          setupPoints: Number(form.setupPoints),
          coinPrice: form.coinPrice,
          isEnabled: form.isEnabled,
          isInShop: form.isInShop,
          isStarter: form.isStarter,
          assetKey: form.assetKey.trim() || null,
          description: form.description.trim() || null,
          reason: form.reason.trim() || null
        };
        const created = await api.post<AdminHeroDto>('/admin/heroes', payload);
        notify(`Đã tạo hero ${created.name} (${created.code}).`);
      }
      onSaved();
    } catch (cause) {
      notify(cause instanceof Error ? cause.message : 'Không lưu được hero.', 'danger');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Drawer
      open
      title={hero ? `Sửa hero ${hero.name}` : 'Tạo hero mới'}
      subtitle={hero ? hero.code : 'Để trống mã để backend tự sinh mã ngẫu nhiên.'}
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" icon={<X size={15} />} onClick={onClose} disabled={saving}>
            Hủy
          </Button>
          <Button variant="primary" icon={<Save size={15} />} onClick={submit} loading={saving}>
            {hero ? 'Lưu thay đổi' : 'Tạo hero'}
          </Button>
        </div>
      }
    >
      <form className="space-y-4" onSubmit={submit}>
        {!hero && (
          <Field label="Mã hero" hint="Tùy chọn. Chỉ chữ, số, '-' và '_'." error={errors.code}>
            {props => <input {...props} className="input font-mono" value={form.code} onChange={event => set('code', event.target.value)} />}
          </Field>
        )}

        <Field label="Tên hiển thị" required error={errors.name}>
          {props => <input {...props} className="input" value={form.name} onChange={event => set('name', event.target.value)} />}
        </Field>

        <Field label="Lớp quân" required error={errors.classCode}>
          {props => (
            <select
              {...props}
              className="input"
              value={form.classCode}
              onChange={event => {
                const classCode = event.target.value;
                // Luật DB: class khác GENERAL phải có setupPoints >= 1 — nâng giá trị ngay để form hợp lệ.
                const setupPoints = classCode !== 'GENERAL' && Number(form.setupPoints) < 1 ? '1' : form.setupPoints;
                setForm(current => ({ ...current, classCode, setupPoints }));
              }}
            >
              {CLASS_CODES.map(code => (
                <option key={code} value={code}>
                  {classLabel(code)} ({code})
                </option>
              ))}
            </select>
          )}
        </Field>

        <Field
          label="Setup Points"
          required
          error={errors.setupPoints}
          hint={hero ? 'Luật legacy read-only: hiển thị để tham chiếu, không chỉnh được từ console.' : 'Số điểm setup ban đầu. Lớp khác Tướng quân cần tối thiểu 1.'}
        >
          {props => (
            <input
              {...props}
              className="input tabular-nums"
              value={form.setupPoints}
              readOnly={Boolean(hero)}
              disabled={Boolean(hero)}
              onChange={event => set('setupPoints', event.target.value)}
            />
          )}
        </Field>

        <Field label="Giá coin" required error={errors.coinPrice} hint="Số nguyên. Giá được sửa ở màn Cửa hàng sau khi tạo.">
          {props => <input {...props} className="input tabular-nums" value={form.coinPrice} readOnly={Boolean(hero)} disabled={Boolean(hero)} onChange={event => set('coinPrice', event.target.value)} />}
        </Field>

        <div className="grid gap-3 sm:grid-cols-3">
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={form.isEnabled} onChange={event => set('isEnabled', event.target.checked)} />
            Đang bật
          </label>
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={form.isInShop} onChange={event => set('isInShop', event.target.checked)} />
            Trong cửa hàng
          </label>
          <label className="flex items-center gap-2 text-sm text-ink">
            <input type="checkbox" checked={form.isStarter} onChange={event => set('isStarter', event.target.checked)} />
            Hero starter
          </label>
        </div>

        <Field label="Asset key" hint="Khoá asset nội bộ, ví dụ 'dev/special-elephant'. Không phải URL ảnh.">
          {props => <input {...props} className="input font-mono" value={form.assetKey} onChange={event => set('assetKey', event.target.value)} />}
        </Field>

        <Field label="Mô tả">
          {props => <textarea {...props} className="input min-h-24" value={form.description} onChange={event => set('description', event.target.value)} />}
        </Field>

        <Field label="Lý do" required={!hero} error={errors.reason} hint="Bắt buộc khi tạo; khuyến nghị ghi khi sửa. Tối đa 500 ký tự.">
          {props => <textarea {...props} className="input min-h-20" value={form.reason} onChange={event => set('reason', event.target.value)} />}
        </Field>
      </form>
    </Drawer>
  );
}
