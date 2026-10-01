import React, { useMemo, useRef, useState } from 'react';
import { HelperMedia } from '../../../../../utils/helpers';
import {
  DeepSheet, SheetButton, SheetError, SheetField, SheetFooter, SHEET_FIELD_CLS,
} from '../../../../components/deep/deep-sheet';
import { sessionLabel } from '../../../use-training-sessions';
import { useTrainingsTab } from '../trainings-context';
import type { TrainingsSheetProps } from '../trainings-sheets';
import {
  formatSheetDate, guessKind, MAX_MEDIA_PER_MEMBER, sourceLabel, sourceOf, type LinkKind,
} from './link-helpers';

/**
 * Форма ссылки «Add video» (тон accent): тренировка, ссылка, тип, подпись. Сайт ничего не
 * хранит — только ссылку. Тип угадывается по адресу, пока человек не выбрал сам.
 */

const KINDS: { kind: LinkKind; label: string }[] = [
  { kind: 'video', label: 'Video' },
  { kind: 'image', label: 'Photo' },
  { kind: 'album', label: 'Album' },
];

function AddVideoSheet({ sheet, onClose }: TrainingsSheetProps<'add-video'>) {
  const { sessions } = useTrainingsTab();
  const data = sessions.data;

  // Новые сверху: дата, при равных — позднее время и свежая запись.
  const list = useMemo(() => [...(data?.sessions ?? [])].sort((a, b) =>
    b.date.localeCompare(a.date) || (b.start ?? '').localeCompare(a.start ?? '') || b.id - a.id), [data]);

  const [sessionId, setSessionId] = useState<number | null>(
    sheet.sessionId ?? list[0]?.id ?? null,
  );
  const [url, setUrl] = useState('');
  const [caption, setCaption] = useState('');
  const [kind, setKind] = useState<LinkKind>('video');
  const [kindTouched, setKindTouched] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Кадр, который не загрузился, — блок превью прячем (по адресу кадра).
  const [badThumb, setBadThumb] = useState<string | null>(null);
  const linkRef = useRef<HTMLInputElement>(null);

  const trimmed = url.trim();
  const valid = /^https:\/\//i.test(trimmed);
  const selected = list.find((s) => s.id === sessionId) ?? null;

  const onUrl = (next: string) => {
    setUrl(next);
    if (!kindTouched) setKind(guessKind(next));
  };

  const thumb = valid ? HelperMedia.resolveThumbUrl(kind, sourceOf(kind, trimmed), trimmed) : null;
  const showThumb = thumb != null && thumb !== badThumb;

  const mine = selected ? selected.media.filter((m) => m.mine).length : 0;
  const canAdd = !sessions.busy && valid && sessionId != null;

  const add = async () => {
    if (!canAdd || sessionId == null) return;
    setError(null);
    const e = await sessions.addMedia(sessionId, {
      media_type: kind,
      source_type: sourceOf(kind, trimmed),
      url: trimmed,
      caption: caption.trim() || null,
    });
    if (e) setError(e);
    else onClose();
  };

  return (
    <DeepSheet
      title="Add video"
      sub="Link to YouTube, Vimeo, Google Photos…"
      tone="accent"
      busy={sessions.busy}
      onClose={onClose}
      onSubmit={() => { void add(); }}
      initialFocusRef={linkRef}
      footer={(
        <div className="flex flex-col gap-3.5">
          <SheetError error={error} />
          <SheetFooter>
            <SheetButton tone="accent" disabled={!canAdd}>Add video</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <SheetField label="Training">
        <select
          value={sessionId ?? ''}
          onChange={(e) => setSessionId(Number(e.target.value))}
          className={`${SHEET_FIELD_CLS} cursor-pointer`}
          dir="auto"
        >
          {list.map((s) => (
            <option key={s.id} value={s.id}>{formatSheetDate(s.date)} · {sessionLabel(s)}</option>
          ))}
        </select>
      </SheetField>

      <SheetField label="Link">
        <div className="relative">
          <input
            ref={linkRef}
            type="url"
            inputMode="url"
            value={url}
            onChange={(e) => onUrl(e.target.value)}
            placeholder="https://…"
            maxLength={1000}
            className={`${SHEET_FIELD_CLS} hp-mono`}
            // Моноширинный 12px, рамка акцента (у поля формы по умолчанию — обычная).
            style={{ fontSize: 12, borderColor: 'var(--deep-accent-border)', paddingRight: 104 }}
          />
          {trimmed && (
            <span
              className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 rounded-md px-2 py-[3px] text-[10px] font-extrabold"
              style={{ background: 'var(--deep-accent-chip)', color: 'var(--deep-accent)' }}
            >
              {sourceLabel(trimmed, kind)}
            </span>
          )}
        </div>
      </SheetField>

      <div className="flex items-end gap-3">
        {showThumb && (
          <img
            src={thumb}
            referrerPolicy="no-referrer"
            alt=""
            onError={() => setBadThumb(thumb)}
            className="shrink-0 object-cover"
            style={{ width: 110, height: 70, borderRadius: 10, background: 'var(--deep-card-bg)' }}
          />
        )}
        <div className="min-w-0 flex-1">
          <SheetField label="Type" group>
            <div
              className="grid grid-cols-3 overflow-hidden"
              style={{ height: 38, borderRadius: 10, background: 'var(--deep-seg-track)' }}
              role="group"
              aria-label="Link type"
            >
              {KINDS.map((k) => {
                const on = kind === k.kind;
                return (
                  <button
                    key={k.kind}
                    type="button"
                    aria-pressed={on}
                    onClick={() => { setKind(k.kind); setKindTouched(true); }}
                    className="cursor-pointer border-0 text-[12px] font-extrabold"
                    style={on
                      ? { background: 'var(--deep-seg-active-bg)', color: 'var(--deep-seg-active-fg)', borderRadius: 10 }
                      : { background: 'transparent', color: 'var(--t-text-2)' }}
                  >
                    {k.label}
                  </button>
                );
              })}
            </div>
          </SheetField>
        </div>
      </div>

      <SheetField label="Caption">
        <input
          value={caption}
          onChange={(e) => setCaption(e.target.value)}
          placeholder="Optional"
          maxLength={200}
          dir="auto"
          className={SHEET_FIELD_CLS}
        />
      </SheetField>

      <p className="m-0 text-[11.5px] font-semibold" style={{ color: 'var(--t-text-3)' }}>
        Only the group sees it. The site keeps just the link.
        {data && !data.can_manage && ` · ${mine} of ${MAX_MEDIA_PER_MEMBER} yours`}
      </p>
    </DeepSheet>
  );
}

export default AddVideoSheet;
