import { useState } from 'react';
import type { HubGroupInput, HubGroupLinkInput } from '../my-groups-types';
import { inputCls, btnCls, btnDangerCls } from './my-groups-shared';

export function GroupInputForm({
  initial, onSubmit, onCancel, submitLabel, recommendApproval,
}: {
  initial: HubGroupInput;
  onSubmit: (input: HubGroupInput) => Promise<{ success: boolean; error?: string | null }>;
  onCancel: () => void;
  submitLabel: string;
  /** Подсказка (не форс): официальной группе с members-контентом лучше режим заявок. */
  recommendApproval?: boolean;
}) {
  const [form, setForm] = useState<HubGroupInput>(initial);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const setField = <K extends keyof HubGroupInput>(key: K, value: HubGroupInput[K]) =>
    setForm((f) => ({ ...f, [key]: value }));

  const setLink = (i: number, field: keyof HubGroupLinkInput, value: string) =>
    setForm((f) => {
      const links = [...f.links];
      links[i] = { ...links[i], [field]: value };
      return { ...f, links };
    });

  const addLink = () => setForm((f) => ({ ...f, links: [...f.links, { kind: 'whatsapp', url: '' }] }));
  const removeLink = (i: number) => setForm((f) => ({ ...f, links: f.links.filter((_, idx) => idx !== i) }));

  const submit = async () => {
    setSaving(true);
    setError(null);
    const result = await onSubmit(form);
    setSaving(false);
    if (!result.success) setError(result.error ?? 'Could not save');
  };

  return (
    <div className="flex flex-col gap-3">
      <input className={inputCls} placeholder="Name" value={form.name}
        onChange={(e) => setField('name', e.target.value)} />
      <input className={inputCls} placeholder="Name (English)" value={form.nameEn ?? ''}
        onChange={(e) => setField('nameEn', e.target.value)} />
      <textarea className={inputCls} placeholder="Description" rows={2} value={form.description ?? ''}
        onChange={(e) => setField('description', e.target.value)} />
      <input className={inputCls} placeholder="City / pool" value={form.location ?? ''}
        onChange={(e) => setField('location', e.target.value)} />
      {/* Alpha-3 код World Aquatics (ISR/GER/…) — как в БД; резолв FK делает сервер. */}
      <input className={inputCls} placeholder="Country code (ISR)" maxLength={3} value={form.country ?? ''}
        onChange={(e) => setField('country', e.target.value.toUpperCase())} />
      <input className={inputCls} placeholder="Icon (link)" value={form.iconUrl ?? ''}
        onChange={(e) => setField('iconUrl', e.target.value)} />

      <div className="flex flex-col gap-2">
        {form.links.map((l, i) => (
          <div key={i} className="flex gap-2">
            <select className={`${inputCls} max-w-[130px]`} value={l.kind}
              onChange={(e) => setLink(i, 'kind', e.target.value)}>
              <option value="whatsapp">WhatsApp</option>
              <option value="telegram">Telegram</option>
              <option value="instagram">Instagram</option>
              <option value="site">Site</option>
            </select>
            <input className={inputCls} placeholder="URL" value={l.url}
              onChange={(e) => setLink(i, 'url', e.target.value)} />
            <button type="button" className={btnDangerCls} onClick={() => removeLink(i)}>✕</button>
          </div>
        ))}
        <button type="button" className={btnCls} onClick={addLink}>+ Link</button>
      </div>

      <label className="flex items-center gap-2 text-[13px] font-bold text-[var(--t-text-2)]">
        <input type="checkbox" checked={form.isPublic}
          onChange={(e) => setField('isPublic', e.target.checked)} />
        Public group
      </label>
      {/* Приватная (§6-6): закрыта не-участникам, но не спрятана от них насовсем — по ссылке
          они видят «members only» и могут подать заявку. */}
      {!form.isPublic && (
        <p className="m-0 text-[11.5px] italic text-[var(--t-text-3)]">
          Private: only members see the roster, results and media. Everyone else gets a “members only”
          page and can only request to join; the group is not listed in the catalog.
        </p>
      )}

      <label className="flex items-center gap-2 text-[13px] font-bold text-[var(--t-text-2)]">
        Joining:
        <select className={`${inputCls} max-w-[220px]`} value={form.joinPolicy ?? 'open'}
          onChange={(e) => setField('joinPolicy', e.target.value as 'open' | 'approval')}>
          <option value="open">open</option>
          <option value="approval">by request (coach approves)</option>
        </select>
      </label>
      {recommendApproval && (form.joinPolicy ?? 'open') === 'open' && (
        <p className="m-0 text-[11.5px] italic text-[var(--t-warn)]">
          In an official group, members-only content (reviews, training) is visible to everyone who
          joins — we recommend the “by request” mode.
        </p>
      )}

      {error && <p className="text-[12.5px] font-bold text-[var(--t-danger)]">{error}</p>}

      <div className="flex gap-2">
        <button type="button" className={btnCls} disabled={saving || !form.name.trim()} onClick={submit}>
          {saving ? '…' : submitLabel}
        </button>
        <button type="button" className={btnCls} onClick={onCancel}>Cancel</button>
      </div>
    </div>
  );
}
