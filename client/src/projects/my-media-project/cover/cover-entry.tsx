import React from 'react';
import type { MySwimDto } from '../use-my-swims';

// Точка входа обложки YouTube для карточки My media: кнопка и хост диалога. Это всё, что
// `swim-list.tsx` знает об обложке. Сам диалог — ленивый чанк: шрифты обложки и `html-to-image`
// страница грузит только по нажатию кнопки, а не при каждом открытии /my-media.

const CoverDialog = React.lazy(() => import('./cover-dialog'));

/** Кнопка `YouTube cover` в панели медиа результата. */
export function CoverButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title="Make a 1280 × 720 thumbnail for this result"
      className="hp-mono inline-flex min-h-9 cursor-pointer items-center gap-1.5 rounded-[8px] border border-[var(--t-accent-border)] bg-transparent px-3 text-[11px] font-extrabold text-[var(--t-accent)]"
    >
      <svg width="9" height="9" viewBox="0 0 12 12" aria-hidden="true">
        <path d="M3 1.5v9l7.5-4.5z" fill="currentColor" />
      </svg>
      YouTube cover
    </button>
  );
}

/** Диалог обложки для заплыва; дни соревнования нужны раскладке C (`5–6 NOV`). */
export function CoverDialogHost(props: {
  swim: MySwimDto;
  swimmerName: string;
  /** Первый и последний день карточки соревнования, yyyy-MM-dd. */
  firstDay: string;
  lastDay: string;
  onClose: () => void;
}) {
  return (
    <React.Suspense fallback={null}>
      <CoverDialog {...props} />
    </React.Suspense>
  );
}
