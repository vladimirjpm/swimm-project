// Механика печати листа workout (Ш2, docs/plans/workout-print-plan.md §2 Р1, §3.2, §3.7, §9).
//
// Пока открыт предпросмотр, живёт «сеанс печати»:
// - `<div id="wprint-root">` — прямой ребёнок `<body>`, в него порталом рисуется окно с листом;
// - класс `wprint-on` на `<html>` — по нему правило `@media print` в `workout-print.css` прячет
//   всё, кроме `#wprint-root` (лист не может жить глубже: правило спрятало бы его вместе с предком);
// - `<style id="wprint-page">` с `@page { size: A4 portrait; margin: 0 }` — ставится перед печатью
//   (кнопкой или Ctrl+P — `beforeprint`), снимается на `afterprint` и при закрытии.
//   `@page` нельзя ограничить областью страницы: забытый стиль сломал бы печать всего сайта.
//
// Почему `@page` не держим весь сеанс, а ставим на каждую печать: Android Chrome возвращается из
// `print()` сразу, iOS шлёт `afterprint` с задержкой — снимать стиль сразу после `print()` нельзя,
// а ставить заново перед каждой печатью — дёшево и делает повторную печать независимой от того,
// дошёл ли прошлый `afterprint`.
//
// Модуль без React и без глобалов на импорте: окно передаётся параметром — так его гоняет
// `print-mode.test.mjs` на поддельном DOM. React-обёртка — `use-print-session.ts`.

export const PRINT_ROOT_ID = 'wprint-root';
export const PRINT_ON_CLASS = 'wprint-on';
export const PAGE_STYLE_ID = 'wprint-page';

export type PrintOrientation = 'portrait' | 'landscape';

/** Текст правила страницы. Поле 0 убирает колонтитулы браузера (адрес, дата); отступ — внутри листа. */
export function pageRule(orientation: PrintOrientation): string {
  return `@page { size: A4 ${orientation}; margin: 0; }`;
}

/** Ставит (или обновляет) `<style id="wprint-page">`. Повторный вызов не плодит копий. */
export function installPageStyle(doc: Document, orientation: PrintOrientation): void {
  let style = doc.getElementById(PAGE_STYLE_ID);
  if (!style) {
    style = doc.createElement('style');
    style.id = PAGE_STYLE_ID;
    doc.head.appendChild(style);
  }
  const css = pageRule(orientation);
  if (style.textContent !== css) style.textContent = css;
}

export function removePageStyle(doc: Document): void {
  doc.getElementById(PAGE_STYLE_ID)?.remove();
}

export interface PrintSession {
  /** Куда рисовать окно предпросмотра с листом (порталом). */
  readonly root: HTMLElement;
  /** Печать листа: ставит `@page` и зовёт `window.print()`. Можно звать сколько угодно раз. */
  print(): void;
  /** Убирает всё: корень, класс, `@page`, слушатели. Повторный вызов — ничего не делает. */
  close(): void;
}

/**
 * Открывает сеанс печати. Один сеанс на страницу: если от прошлого остался корень (не закрыт
 * при горячей перезагрузке), он забирается заново, а не дублируется.
 */
export function openPrintSession(win: Window, orientation: PrintOrientation = 'portrait'): PrintSession {
  const doc = win.document;
  doc.getElementById(PRINT_ROOT_ID)?.remove();
  const root = doc.createElement('div');
  root.id = PRINT_ROOT_ID;
  doc.body.appendChild(root);
  doc.documentElement.classList.add(PRINT_ON_CLASS);

  const onBefore = () => installPageStyle(doc, orientation);
  const onAfter = () => removePageStyle(doc);
  win.addEventListener('beforeprint', onBefore);
  win.addEventListener('afterprint', onAfter);

  let closed = false;
  return {
    root,
    print() {
      if (closed) return;
      installPageStyle(doc, orientation);
      win.print();
    },
    close() {
      if (closed) return;
      closed = true;
      win.removeEventListener('beforeprint', onBefore);
      win.removeEventListener('afterprint', onAfter);
      removePageStyle(doc);
      doc.documentElement.classList.remove(PRINT_ON_CLASS);
      root.remove();
    },
  };
}
