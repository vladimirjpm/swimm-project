// Замер копии листа печати без React (docs/plans/workout-print-plan.md §3.5): влезает ли лист при
// основном шрифте `f`, и подгонка всех вариантов раскладки. React-часть — `use-sheet-fit.ts`;
// тесты на поддельных элементах — `sheet-fit-measure.test.mjs`.
//
// Разметка копии сообщает замеру три вещи атрибутами:
//   FIT_AREA_ATTR — рабочая область листа (внутри отступа 10 мм);
//   FIT_BOX_ATTR  — блок содержимого; ему ставится `font-size`, всё остальное в `em`;
//   FIT_X_ATTR    — блоки, которые не должны вылезать по ширине (кубы).

import {
  chooseLayout, CUBE_COLUMN_OPTIONS, pickCubeColumns, searchFontSize,
  type CubeColumns, type CubesFit, type FitResult, type LayoutChoice,
} from './print-fit';
import type { SheetModel } from './print-types';

export const FIT_AREA_ATTR = 'data-wprint-fit-area';
export const FIT_BOX_ATTR = 'data-wprint-fit-box';
export const FIT_X_ATTR = 'data-wprint-fit-x';

export type SheetVariant = { layout: 'main' } | { layout: 'cubes'; columns: CubeColumns };

/** Что мерить: «главный справа» — если она есть у листа; кубы — по 1…3 в ряду, не больше сетов. */
export function sheetVariants(model: Pick<SheetModel, 'mainLayout' | 'cubes'>): SheetVariant[] {
  const out: SheetVariant[] = model.mainLayout ? [{ layout: 'main' }] : [];
  const maxColumns = Math.max(1, model.cubes.length);
  for (const columns of CUBE_COLUMN_OPTIONS) if (columns <= maxColumns) out.push({ layout: 'cubes', columns });
  return out;
}

export const variantKey = (v: SheetVariant) => (v.layout === 'main' ? 'main' : `cubes-${v.columns}`);

/** Влезает ли копия листа при основном шрифте `f`; null — в копии нет нужной разметки. */
export function sheetFits(sheet: HTMLElement): ((f: number) => boolean) | null {
  const area = sheet.querySelector<HTMLElement>(`[${FIT_AREA_ATTR}]`);
  const box = sheet.querySelector<HTMLElement>(`[${FIT_BOX_ATTR}]`);
  if (!area || !box) return null;
  const xs = Array.from(sheet.querySelectorAll<HTMLElement>(`[${FIT_X_ATTR}]`));
  return (f) => {
    box.style.fontSize = `${f}px`;
    // Дробные размеры: лист 297 мм = 1122.5 px, целые offsetHeight тут врут на полпикселя.
    if (box.getBoundingClientRect().height > area.getBoundingClientRect().height + 0.01) return false;
    if (box.scrollWidth > area.clientWidth + 1) return false;
    return xs.every((el) => el.scrollWidth <= el.clientWidth + 1);
  };
}

/** Подгонка всех вариантов и выбор раскладки; null — ни одной копии ещё нет в DOM. */
export function fitVariants(variants: SheetVariant[], sheetOf: (v: SheetVariant) => HTMLElement | null): LayoutChoice | null {
  let main: FitResult | null = null;
  const cubes: CubesFit[] = [];
  for (const v of variants) {
    const el = sheetOf(v);
    const fits = el && sheetFits(el);
    if (!fits) continue;
    const r = searchFontSize(fits);
    if (v.layout === 'main') main = r;
    else cubes.push({ ...r, columns: v.columns });
  }
  if (cubes.length === 0) return main ? { layout: 'main', ...main } : null;
  return chooseLayout(main, pickCubeColumns(cubes));
}
