// Подгонка шрифта листа вида `large` по живому DOM (docs/plans/workout-print-plan.md §3.5, §9).
//
// Тонкая обёртка над чистым `print-fit.ts`: все варианты раскладки («главный справа», кубы по 1–3
// в ряду) рисуются скрытыми копиями листа той же разметки (`use-probe-fit.ts` — портал, шрифты),
// у каждой подбирается наибольший основной шрифт `searchFontSize`, затем `chooseLayout` выбирает
// раскладку. Флаг «не влезло на полу» — `choice.overflow`.
//
// Разметку копии (атрибуты FIT_*) и сам замер без React — см. `sheet-fit-measure.ts`.

import { useCallback, useMemo, type ReactNode } from 'react';
import type { LayoutChoice } from './print-fit';
import { fitVariants, variantKey, type SheetVariant } from './sheet-fit-measure';
import { useProbeFit } from './use-probe-fit';

export type { SheetVariant } from './sheet-fit-measure';

const sameChoice = (a: LayoutChoice | null, b: LayoutChoice | null) =>
  a === b ||
  (!!a && !!b && a.layout === b.layout && a.size === b.size && a.overflow === b.overflow &&
    (a.layout !== 'cubes' || b.layout !== 'cubes' || a.columns === b.columns));

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
  const byKey = useMemo(() => new Map(variants.map((v) => [variantKey(v), v])), [variants]);
  const keys = useMemo(() => [...byKey.keys()], [byKey]);
  const render = useCallback((key: string) => renderSheet(byKey.get(key)!), [byKey, renderSheet]);
  const measure = useCallback(
    (get: (key: string) => HTMLElement | null) => fitVariants(variants, (v) => get(variantKey(v))),
    [variants],
  );
  const { result, probe } = useProbeFit(keys, render, measure, sameChoice, fitKey);
  return { choice: result, probe };
}
