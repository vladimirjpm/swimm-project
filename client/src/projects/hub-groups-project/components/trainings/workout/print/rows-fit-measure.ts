// Замер копии «листа строк» (виды `rows`, `two-sides`; хендофф 27.11 §3, §6) без React: правило
// «текст под дистанцией» и подгонка шрифта тем же `searchFontSize`, что у `large`. React-часть —
// `views/use-rows-fit.ts`; тесты на поддельных элементах — `rows-fit-measure.test.mjs`.
//
// Разметка копии сообщает замеру (кроме FIT_AREA / FIT_BOX / FIT_X из `sheet-fit-measure.ts`):
//   ROW_LINE_ATTR — строка сета, значение — её ключ (`rowsLineKey`);
//   ROW_DIST_ATTR / ROW_TEXT_ATTR — дистанция и текст внутри строки;
//   ROW_BELOW_ATTR — ставит замер: текст уходит второй строкой на всю ширину (CSS по атрибуту);
//   ROW_SET_ATTR  — сет: его рамка не должна выйти за рабочую область ни на полпикселя.

import { searchFontSize, type FitResult } from './print-fit';
import { FIT_AREA_ATTR, FIT_BOX_ATTR, FIT_X_ATTR } from './sheet-fit-measure';

export const ROW_SET_ATTR = 'data-wprint-set';
export const ROW_LINE_ATTR = 'data-wprint-line';
export const ROW_DIST_ATTR = 'data-wprint-dist';
export const ROW_TEXT_ATTR = 'data-wprint-text';
export const ROW_BELOW_ATTR = 'data-wprint-below';

/** Текст рядом с дистанцией занял бы 3 строки и больше: его высота больше 2.2 высоты дистанции. */
export const BELOW_HEIGHT_RATIO = 2.2;

/** Строка листа для замера (подмножество `HTMLElement`, чтобы тесты обходились без DOM). */
type El = Pick<HTMLElement, 'getAttribute' | 'setAttribute' | 'removeAttribute' | 'querySelector'>;

/**
 * Правило «текст под дистанцией» (§3): сначала все строки — рядом, потом замер, потом метка тем,
 * у кого текст рядом с дистанцией занял бы 3+ строк или слово не помещается в колонку. Возвращает
 * ключи строк с текстом под дистанцией. Строка без дистанции (пояснение) правила не знает.
 */
export function applyBelowRule(lines: El[]): string[] {
  for (const line of lines) line.removeAttribute(ROW_BELOW_ATTR);
  const below = lines.filter((line) => {
    const text = line.querySelector<HTMLElement>(`[${ROW_TEXT_ATTR}]`);
    const dist = line.querySelector<HTMLElement>(`[${ROW_DIST_ATTR}]`);
    if (!text || !dist) return false;
    const tooTall = text.getBoundingClientRect().height > dist.getBoundingClientRect().height * BELOW_HEIGHT_RATIO;
    const tooNarrow = text.scrollWidth > text.clientWidth + 0.5;
    return tooTall || tooNarrow;
  });
  for (const line of below) line.setAttribute(ROW_BELOW_ATTR, '');
  return below.map((line) => line.getAttribute(ROW_LINE_ATTR) ?? '');
}

export interface RowsFit extends FitResult {
  /** Строки (ключи `rowsLineKey`), у которых при этом шрифте текст стоит под дистанцией. */
  below: string[];
}

/**
 * Подгонка одной копии листа строк: на каждом пробном `f` — правило «текст под дистанцией», потом
 * «влез» — блок содержимого (шапка + сеты + полоса, в копии без растяжки) не выше рабочей области,
 * ничего не вылезло по ширине. Итог — размер и строки «под» при нём. Нет разметки — null.
 */
export function fitRowsSheet(sheet: Pick<HTMLElement, 'querySelector' | 'querySelectorAll'>): RowsFit | null {
  const area = sheet.querySelector<HTMLElement>(`[${FIT_AREA_ATTR}]`);
  const box = sheet.querySelector<HTMLElement>(`[${FIT_BOX_ATTR}]`);
  if (!area || !box) return null;
  const lines = Array.from(sheet.querySelectorAll<HTMLElement>(`[${ROW_LINE_ATTR}]`));
  const xs = Array.from(sheet.querySelectorAll<HTMLElement>(`[${FIT_X_ATTR}]`));
  const sets = Array.from(sheet.querySelectorAll<HTMLElement>(`[${ROW_SET_ATTR}]`));
  let below: string[] = [];
  const fits = (f: number) => {
    box.style.fontSize = `${f}px`;
    below = applyBelowRule(lines);
    // Дробные размеры: лист 297 мм = 1122.5 px, целые offsetHeight тут врут на полпикселя.
    if (box.getBoundingClientRect().height > area.getBoundingClientRect().height + 0.01) return false;
    if (box.scrollWidth > area.clientWidth + 1) return false;
    // Крупный шрифт сжимает колонку текста до нуля, и общие колонки (рейка, дистанция, время)
    // выходят за лист на доли пикселя — `scrollWidth` целый и этого не видит, а рамка сета обрезается.
    const a = area.getBoundingClientRect();
    if (sets.some((el) => { const r = el.getBoundingClientRect(); return r.left < a.left - 0.5 || r.right > a.right + 0.5; })) return false;
    return xs.every((el) => el.scrollWidth <= el.clientWidth + 1);
  };
  const r = searchFontSize(fits);
  fits(r.size); // строки «под» — при выбранном размере, а не при последнем пробном
  return { ...r, below };
}
