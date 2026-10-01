import React, { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { HelperMedia } from '../../../../../utils/helpers';
import ConfirmDialog from '../../../../components/confirm-dialog/confirm-dialog';
import { sessionLabel } from '../../../use-training-sessions';
import { useTrainingsTab } from '../trainings-context';
import type { TrainingsSheetProps } from '../trainings-sheets';
import { formatSheetDate, sourceLabel } from './link-helpers';

/**
 * Лайтбокс ссылок тренировки (`lightbox`) — ВСЕГДА тёмный в обеих темах: на корне портала класс
 * `theme-deep` жёстко (не `useDeepThemeClass`), роли `--deep-*` берутся из него. Это не
 * `DeepSheet`: на весь экран, без поверхности-карточки.
 *
 * Список — ссылки тренировки без альбомов (альбом открывается наружу, как в ленте). Листание:
 * свайп по горизонтали (порог 50px), стрелки ← → и точки; `Esc` закрывает. Автора сервер не
 * отдаёт (правило приватности ленты группы), поэтому «Added by you» — только у своих ссылок.
 */

const SWIPE_PX = 50;

function TrainingLightbox({ sheet, onClose }: TrainingsSheetProps<'lightbox'>) {
  const { sessions } = useTrainingsTab();
  const session = sessions.data?.sessions.find((s) => s.id === sheet.sessionId) ?? null;

  const [index, setIndex] = useState(sheet.index);
  // Только что удалённые: список перечитывается хуком с задержкой, а убранная ссылка не должна мигать.
  const [removed, setRemoved] = useState<number[]>([]);
  const [confirming, setConfirming] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);
  const touchX = useRef<number | null>(null);

  const items = useMemo(
    () => (session?.media ?? []).filter((m) => m.media_type !== 'album' && !removed.includes(m.id)),
    [session, removed],
  );
  const current = items.length > 0 ? Math.min(Math.max(index, 0), items.length - 1) : -1;
  const item = current >= 0 ? items[current] : null;

  // Последнюю ссылку удалили (или тренировка пропала) — показывать нечего.
  useEffect(() => {
    if (items.length === 0) onClose();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items.length]);

  const move = (delta: number) => setIndex(Math.min(Math.max(current + delta, 0), items.length - 1));
  const live = useRef({ move, onClose, confirming });
  live.current = { move, onClose, confirming };

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      // Поверх лайтбокса стоит подтверждение удаления — Esc и стрелки теперь его, не наши.
      if (live.current.confirming) return;
      if (e.key === 'Escape') live.current.onClose();
      else if (e.key === 'ArrowLeft') live.current.move(-1);
      else if (e.key === 'ArrowRight') live.current.move(1);
    };
    window.addEventListener('keydown', onKey);
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    closeRef.current?.focus();
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = prev;
    };
  }, []);

  if (!session || !item) return null;

  const label = sourceLabel(item.url, item.media_type);
  const embedUrl = item.media_type === 'video' ? HelperMedia.embedUrl(item.source_type, item.url) : null;
  const meta = [item.mine ? 'Added by you' : null, label].filter(Boolean).join(' · ');
  const openLabel = label === 'Link' ? 'Open link ↗' : label === 'Photo' ? 'Open photo ↗' : `Open on ${label} ↗`;

  const player = item.media_type === 'image' ? (
    <img
      key={item.id}
      src={HelperMedia.directImageUrl(item.url)}
      referrerPolicy="no-referrer"
      alt={item.caption ?? ''}
      className="h-full w-full object-contain"
    />
  ) : embedUrl ? (
    <iframe
      key={item.id}
      src={embedUrl}
      title={item.caption || 'Video'}
      className="h-full w-full border-0"
      allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
      allowFullScreen
      sandbox="allow-scripts allow-same-origin allow-presentation allow-popups"
      referrerPolicy="strict-origin-when-cross-origin"
    />
  ) : (
    <div className="flex h-full w-full items-center justify-center">
      <a
        href={item.url}
        target="_blank"
        rel="noopener noreferrer"
        className="rounded-[12px] border px-5 py-3 text-[13px] font-extrabold no-underline"
        style={{ borderColor: 'var(--deep-accent-border)', color: 'var(--deep-accent)' }}
      >
        Open ↗
      </a>
    </div>
  );

  return createPortal(
    <>
    <div
      className="theme-deep fixed inset-0 z-[125] flex flex-col"
      style={{ background: 'var(--deep-page-bg)', color: 'var(--deep-text)' }}
      role="dialog"
      aria-modal="true"
      aria-label="Training videos"
      onTouchStart={(e) => { touchX.current = e.touches[0]?.clientX ?? null; }}
      onTouchEnd={(e) => {
        const from = touchX.current;
        touchX.current = null;
        const to = e.changedTouches[0]?.clientX;
        if (from == null || to == null) return;
        if (to - from <= -SWIPE_PX) move(1);
        else if (to - from >= SWIPE_PX) move(-1);
      }}
    >
      <div className="flex shrink-0 items-center gap-2.5 px-3.5 pb-2.5" style={{ paddingTop: 'calc(18px + env(safe-area-inset-top))' }}>
        <button
          ref={closeRef}
          type="button"
          aria-label="Close"
          onClick={onClose}
          className="inline-flex h-10 w-10 shrink-0 cursor-pointer items-center justify-center rounded-full border-0 text-[15px] font-extrabold"
          style={{ background: 'var(--deep-seg-track)', color: 'var(--deep-text)' }}
        >
          ✕
        </button>
        <div className="min-w-0 flex-1">
          <div dir="auto" className="truncate text-[13px] font-extrabold"><bdi>{sessionLabel(session)}</bdi></div>
          <div className="hp-mono text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
            {formatSheetDate(session.date)} · {current + 1} / {items.length}
          </div>
        </div>
      </div>

      <div className="flex min-h-0 flex-1 flex-col justify-center gap-3.5 overflow-y-auto pb-5">
        <div
          className="mx-auto aspect-video"
          style={{ width: 'min(100%, 960px)', background: 'var(--deep-card-bg)' }}
        >
          {player}
        </div>
        <div className="mx-auto flex w-full flex-col gap-1 px-4" style={{ maxWidth: 960 }}>
          {item.caption && <div dir="auto" className="text-[15px] font-extrabold">{item.caption}</div>}
          <div className="text-[12px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>{meta}</div>
        </div>
        {items.length > 1 && (
          <div className="flex justify-center" role="group" aria-label="Pick a link">
            {items.map((m, i) => (
              <button
                key={m.id}
                type="button"
                aria-label={`${i + 1} of ${items.length}`}
                aria-current={i === current}
                onClick={() => setIndex(i)}
                className="flex h-[18px] cursor-pointer items-center border-0 bg-transparent px-[3px]"
              >
                <span
                  className="block"
                  style={{
                    height: 6,
                    width: i === current ? 18 : 6,
                    borderRadius: 3,
                    background: i === current ? 'var(--deep-accent)' : 'var(--deep-text-ghost)',
                  }}
                />
              </button>
            ))}
          </div>
        )}
      </div>

      <div
        className="shrink-0 border-t px-3.5 pt-3"
        style={{ borderColor: 'var(--deep-card-border)', paddingBottom: 'calc(20px + env(safe-area-inset-bottom))' }}
      >
        {/* Кнопки — по ширине плеера: на десктопе во весь экран они уезжали к краям. */}
        <div className="mx-auto flex w-full gap-2" style={{ maxWidth: 960 }}>
        <a
          href={item.url}
          target="_blank"
          rel="noopener noreferrer"
          className="flex h-12 flex-1 items-center justify-center rounded-[12px] border text-[13px] font-extrabold no-underline"
          style={{ borderColor: 'var(--deep-accent-border)', color: 'var(--deep-accent)' }}
        >
          {openLabel}
        </a>
        {item.can_delete && (
          <button
            type="button"
            onClick={() => setConfirming(true)}
            className="h-12 cursor-pointer rounded-[12px] border bg-transparent px-4 text-[13px] font-extrabold"
            style={{ borderColor: 'var(--deep-danger)', color: 'var(--deep-danger)' }}
          >
            Delete
          </button>
        )}
        </div>
      </div>
    </div>
    {confirming && (
      <ConfirmDialog
        title={item.mine ? 'Remove your link?' : 'Remove this link for everyone?'}
        confirmLabel="Remove"
        busyLabel="Removing…"
        onConfirm={async () => {
          const error = await sessions.removeMedia(session.id, item.id);
          if (!error) {
            setRemoved((r) => [...r, item.id]);
            setConfirming(false);
          }
          return { success: !error, error: error ?? undefined };
        }}
        onClose={() => setConfirming(false)}
      >
        <p className="mt-3 text-[13px] text-[var(--t-text-2)]">
          {item.mine ? 'The group will no longer see it.' : 'Someone else added it — the group will no longer see it.'}
        </p>
      </ConfirmDialog>
    )}
    </>,
    document.body,
  );
}

export default TrainingLightbox;
