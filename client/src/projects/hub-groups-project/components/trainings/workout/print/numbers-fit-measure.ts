// Замер копии «листа кубов» (виды `numbers`, `numbers-two-sides`; хендофф 27.11 вторая версия, §3, §7)
// без React: правило «текст под дистанцией» и подгонка шрифта тем же `searchFontSize`, что у `large`.
// React-часть — `views/numbers-sheets.tsx`; тесты на поддельных элементах — `numbers-fit-measure.test.mjs`.
//
// Разметка копии сообщает замеру (кроме FIT_AREA / FIT_BOX / FIT_X из `sheet-fit-measure.ts`):
//   NUM_LINE_ATTR — строка сета, значение — её ключ (`numbersLineKey`);
//   NUM_DIST_ATTR / NUM_TEXT_ATTR — дистанция и текст внутри строки;
//   NUM_BELOW_ATTR — ставит замер: текст уходит второй строкой на всю ширину куба (CSS по атрибуту);
//   NUM_SET_ATTR  — куб: его рамка не должна выйти за рабочую область ни на полпикселя.

import { searchFontSize, type FitResult } from './print-fit';
import { FIT_AREA_ATTR, FIT_BOX_ATTR, FIT_X_ATTR } from './sheet-fit-measure';

export const NUM_SET_ATTR = 'data-wprint-set';
export const NUM_LINE_ATTR = 'data-wprint-line';
export const NUM_DIST_ATTR = 'data-wprint-dist';
export const NUM_TEXT_ATTR = 'data-wprint-text';
export const NUM_BELOW_ATTR = 'data-wprint-below';

/** Текст занял больше одной строки: его высота больше 1.5 высоты строки (`fit-reference.js` хендоффа). */
export const ONE_LINE_RATIO = 1.5;

/** Строка листа для замера (подмножество `HTMLElement`, чтобы тесты обходились без DOM). */
type El = Pick<HTMLElement, 'getAttribute' | 'setAttribute' | 'removeAttribute' | 'querySelector'>;

/** Высота одной строки текста, px: `line-height` элемента; «normal» — 1.2 его шрифта. */
export type LineHeightOf = (el: HTMLElement) => number;

const cssLineHeight: LineHeightOf = (el) => {
  const cs = getComputedStyle(el);
  const lh = parseFloat(cs.lineHeight);
  return Number.isFinite(lh) ? lh : parseFloat(cs.fontSize) * 1.2;
};

/**
 * Правило «текст под дистанцией» (§3, решение Влада 27.11: текст — в одну строку): сначала все
 * строки — рядом, потом замер, потом метка тем, у кого текст рядом с дистанцией занял бы две строки
 * и больше или слово не помещается в колонку. Чип нагрузки — часть текста: «חתירה [V2]» рядом
 * целиком или ниже целиком. Возвращает ключи строк с текстом под дистанцией. Строка без дистанции
 * (пояснение) правила не знает.
 */
export function applyBelowRule(lines: El[], lineHeight: LineHeightOf = cssLineHeight): string[] {
  for (const line of lines) line.removeAttribute(NUM_BELOW_ATTR);
  const below = lines.filter((line) => {
    const text = line.querySelector<HTMLElement>(`[${NUM_TEXT_ATTR}]`);
    const dist = line.querySelector<HTMLElement>(`[${NUM_DIST_ATTR}]`);
    if (!text || !dist) return false;
    const wraps = text.getBoundingClientRect().height > lineHeight(text) * ONE_LINE_RATIO;
    const tooNarrow = text.scrollWidth > text.clientWidth + 0.5;
    return wraps || tooNarrow;
  });
  for (const line of below) line.setAttribute(NUM_BELOW_ATTR, '');
  return below.map((line) => line.getAttribute(NUM_LINE_ATTR) ?? '');
}

export interface NumbersFit extends FitResult {
  /** Строки (ключи `numbersLineKey`), у которых при этом шрифте текст стоит под дистанцией. */
  below: string[];
}

/**
 * Подгонка одной копии листа кубов: на каждом пробном `f` — правило «текст под дистанцией», потом
 * «влез» — блок содержимого (шапка + кубы + полоса + подпись, в копии без растяжки) не выше рабочей
 * области, ничего не вылезло по ширине. Итог — размер и строки «под» при нём. Нет разметки — null.
 */
export function fitNumbersSheet(
  sheet: Pick<HTMLElement, 'querySelector' | 'querySelectorAll'>,
  lineHeight: LineHeightOf = cssLineHeight,
): NumbersFit | null {
  const area = sheet.querySelector<HTMLElement>(`[${FIT_AREA_ATTR}]`);
  const box = sheet.querySelector<HTMLElement>(`[${FIT_BOX_ATTR}]`);
  if (!area || !box) return null;
  const lines = Array.from(sheet.querySelectorAll<HTMLElement>(`[${NUM_LINE_ATTR}]`));
  const xs = Array.from(sheet.querySelectorAll<HTMLElement>(`[${FIT_X_ATTR}]`));
  const sets = Array.from(sheet.querySelectorAll<HTMLElement>(`[${NUM_SET_ATTR}]`));
  let below: string[] = [];
  const fits = (f: number) => {
    box.style.fontSize = `${f}px`;
    below = applyBelowRule(lines, lineHeight);
    // Дробные размеры: лист 297 мм = 1122.5 px, целые offsetHeight тут врут на полпикселя.
    if (box.getBoundingClientRect().height > area.getBoundingClientRect().height + 0.01) return false;
    if (box.scrollWidth > area.clientWidth + 1) return false;
    // Крупный шрифт сжимает колонку текста до нуля, и общие колонки (дистанция, время) выходят за
    // лист на доли пикселя — `scrollWidth` целый и этого не видит, а рамка куба обрезается.
    const a = area.getBoundingClientRect();
    if (sets.some((el) => { const r = el.getBoundingClientRect(); return r.left < a.left - 0.5 || r.right > a.right + 0.5; })) return false;
    return xs.every((el) => el.scrollWidth <= el.clientWidth + 1);
  };
  const r = searchFontSize(fits);
  fits(r.size); // строки «под» — при выбранном размере, а не при последнем пробном
  return { ...r, below };
}
