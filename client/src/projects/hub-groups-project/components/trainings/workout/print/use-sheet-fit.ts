// Подгонка шрифта листа печати по живому DOM (docs/plans/workout-print-plan.md §3.5, §9).
//
// Тонкая обёртка над чистым `print-fit.ts`: все варианты раскладки («главный справа», кубы по 1–3
// в ряду) рисуются скрытыми копиями листа той же разметки и того же класса `.wprint-sheet` (та же
// ширина 210 мм — меряем фактическую, а не 794 px), у каждой подбирается наибольший основной
// шрифт `searchFontSize`, затем `chooseLayout` выбирает раскладку. Флаг «не влезло на полу» —
// `choice.overflow`.
//
// Копии — порталом прямо в `<body>`: не внутри окна предпросмотра, где `transform: scale` исказил
// бы замер, а при печати `body > :not(#wprint-root)` их прячет (плюс `.wprint-noprint`).
//
// Шрифты: первый замер — сразу в `useLayoutEffect` (он же заставляет браузер начать грузить
// начертания, которые видит в копиях), повторный — после `document.fonts.ready` и на каждый
// `loadingdone`: замер по фоллбеку дал бы лист, который при печати вылезет за край.
//
// Разметку копии (атрибуты FIT_*) и сам замер без React — см. `sheet-fit-measure.ts`.

import { createElement, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import type { LayoutChoice } from './print-fit';
import { fitVariants, variantKey, type SheetVariant } from './sheet-fit-measure';

export type { SheetVariant } from './sheet-fit-measure';

const sameChoice = (a: LayoutChoice | null, b: LayoutChoice | null) =>
  a === b ||
  (!!a && !!b && a.layout === b.layout && a.size === b.size && a.overflow === b.overflow &&
    (a.layout !== 'cubes' || b.layout !== 'cubes' || a.columns === b.columns));

const PROBE_STYLE = {
  position: 'fixed',
  left: '-10000px',
  top: 0,
  visibility: 'hidden',
  pointerEvents: 'none',
} as const;

export interface SheetFit {
  /** Выбранная раскладка и шрифт; null — до первого замера. */
  choice: LayoutChoice | null;
  /** Скрытые копии для замера — вызывающий кладёт в свою разметку как есть (это портал). */
  probe: ReactNode;
}

/**
 * `renderSheet(v)` — полный лист `.wprint-sheet` в варианте `v` с атрибутами `FIT_*`, БЕЗ своего
 * `font-size` на блоке содержимого (его ставит замер). Перемер — при смене `fitKey` или `variants`.
 */
export function useSheetFit(variants: SheetVariant[], renderSheet: (v: SheetVariant) => ReactNode, fitKey: unknown): SheetFit {
  const [choice, setChoice] = useState<LayoutChoice | null>(null);
  const holders = useRef(new Map<string, HTMLElement>());

  useLayoutEffect(() => {
    let alive = true;
    const run = () => {
      if (!alive) return;
      const next = fitVariants(variants, (v) => holders.current.get(variantKey(v)) ?? null);
      setChoice((prev) => (sameChoice(prev, next) ? prev : next));
    };
    run();
    const fonts = typeof document !== 'undefined' ? document.fonts : undefined;
    fonts?.ready.then(run, () => undefined);
    fonts?.addEventListener?.('loadingdone', run);
    return () => {
      alive = false;
      fonts?.removeEventListener?.('loadingdone', run);
    };
  }, [fitKey, variants]);

  const probe =
    typeof document === 'undefined'
      ? null
      : createPortal(
          createElement(
            'div',
            { className: 'wprint-noprint', 'aria-hidden': true, style: PROBE_STYLE },
            variants.map((v) => {
              const key = variantKey(v);
              return createElement(
                'div',
                {
                  key,
                  ref: (el: HTMLDivElement | null) => {
                    if (el) holders.current.set(key, el);
                    else holders.current.delete(key);
                  },
                },
                renderSheet(v),
              );
            }),
          ),
          document.body,
        );

  return { choice, probe };
}
