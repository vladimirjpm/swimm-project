import React from 'react';
import type { MySwimDto } from '../use-my-swims';

// Точка входа плаката для карточки My media: кнопка и хост диалога. Это всё, что
// `swim-list.tsx` знает о плакате. Сам диалог — ленивый чанк: шрифты и `html-to-image` страница
// грузит только по нажатию кнопки. Чистых правил здесь нет — только «есть ли из чего делать».

const PosterDialog = React.lazy(() => import('./poster-dialog'));

/** Плакат делается из заплывов с результатом: одних DNS / DQ для него мало. */
export const canMakePoster = (swims: MySwimDto[]): boolean => swims.some((s) => !s.time_fail);

/** Кнопка `Poster` в шапке карточки соревнования (только когда выбран один пловец). */
export function PosterButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title="Make a poster with this swimmer's results at the competition"
      className="hp-mono inline-flex min-h-11 flex-none cursor-pointer items-center gap-1.5 rounded-[8px] border border-[var(--t-accent-border)] bg-transparent px-3 text-[11px] font-extrabold text-[var(--t-accent)]"
    >
      <svg width="10" height="12" viewBox="0 0 10 12" aria-hidden="true">
        <rect x="0.75" y="0.75" width="8.5" height="10.5" rx="1.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
        <path d="M3 4h4M3 6.5h4" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" />
      </svg>
      Poster
    </button>
  );
}

/** Диалог плаката одного пловца на соревновании. */
export function PosterDialogHost(props: {
  /** Заплывы карточки; на плакат идут заплывы пловца `swimmerId`. */
  swims: MySwimDto[];
  swimmerId: number;
  swimmerName: string;
  clubName: string | null;
  /** Первый день карточки соревнования, yyyy-MM-dd. */
  firstDay: string;
  onClose: () => void;
}) {
  return (
    <React.Suspense fallback={null}>
      <PosterDialog {...props} />
    </React.Suspense>
  );
}
