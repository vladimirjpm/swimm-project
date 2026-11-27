// Скрытые копии листа печати для замера (docs/plans/workout-print-plan.md §3.5, §9) — общая часть
// подгонки всех видов: `use-sheet-fit.ts` (large), `views/numbers-sheets.tsx` (numbers, numbers-two-sides).
//
// Копии — порталом прямо в `<body>`: не внутри окна предпросмотра, где `transform: scale` исказил
// бы замер, а при печати `body > :not(#wprint-root)` их прячет (плюс `.wprint-noprint`). Класс
// копий тот же `.wprint-sheet` (та же ширина 210 мм — меряем фактическую, а не 794 px).
//
// Шрифты: первый замер — сразу в `useLayoutEffect` (он же заставляет браузер начать грузить
// начертания, которые видит в копиях), повторный — после `document.fonts.ready` и на каждый
// `loadingdone`: замер по фоллбеку дал бы лист, который при печати вылезет за край.

import { createElement, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';

const PROBE_STYLE = {
  position: 'fixed',
  left: '-10000px',
  top: 0,
  visibility: 'hidden',
  pointerEvents: 'none',
} as const;

export interface ProbeFit<R> {
  /** Итог замера; null — до первого замера. */
  result: R | null;
  /** Скрытые копии — вызывающий кладёт в свою разметку как есть (это портал). */
  probe: ReactNode;
}

/**
 * `keys` — копии листа (по одной на вариант раскладки или на лист вида), `renderProbe(key)` — сама
 * копия. `measure(get)` меряет копии (`get(key)` — обёртка копии в DOM) и отдаёт итог; `same` —
 * не перерисовывать, если итог не изменился. Перемер — при смене `fitKey` или набора `keys`.
 */
export function useProbeFit<R>(
  keys: string[],
  renderProbe: (key: string) => ReactNode,
  measure: (get: (key: string) => HTMLElement | null) => R | null,
  same: (a: R | null, b: R | null) => boolean,
  fitKey: unknown,
): ProbeFit<R> {
  const [result, setResult] = useState<R | null>(null);
  const holders = useRef(new Map<string, HTMLElement>());
  // Свежие measure / same без перезапуска эффекта: перемер решают только `fitKey` и `keys`.
  const latest = useRef({ measure, same });
  latest.current = { measure, same };
  const keysId = keys.join('|');

  useLayoutEffect(() => {
    let alive = true;
    const run = () => {
      if (!alive) return;
      const next = latest.current.measure((key) => holders.current.get(key) ?? null);
      setResult((prev) => (latest.current.same(prev, next) ? prev : next));
    };
    run();
    const fonts = typeof document !== 'undefined' ? document.fonts : undefined;
    fonts?.ready.then(run, () => undefined);
    fonts?.addEventListener?.('loadingdone', run);
    return () => {
      alive = false;
      fonts?.removeEventListener?.('loadingdone', run);
    };
  }, [fitKey, keysId]);

  const probe =
    typeof document === 'undefined'
      ? null
      : createPortal(
          createElement(
            'div',
            { className: 'wprint-noprint', 'aria-hidden': true, style: PROBE_STYLE },
            keys.map((key) =>
              createElement(
                'div',
                {
                  key,
                  ref: (el: HTMLDivElement | null) => {
                    if (el) holders.current.set(key, el);
                    else holders.current.delete(key);
                  },
                },
                renderProbe(key),
              ),
            ),
          ),
          document.body,
        );

  return { result, probe };
}
