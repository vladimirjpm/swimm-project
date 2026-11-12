import { toBlob } from 'html-to-image';
import { printFontEmbedCss } from './print-fonts';

// Экспорт картинки (обложка, плакат): DOM → canvas → PNG, целиком в браузере (решение Влада
// 12.11.2026) — на сервер ничего не уходит и там ничего не хранится.
//
// `html-to-image` клонирует узел с вычисленными стилями, кладёт его в SVG `foreignObject` и
// рисует на canvas. Отсюда два требования:
//  • шрифты — внутри картинки: `fontEmbedCSS` с data-URL (`printFontEmbedCss`). Без него
//    библиотека обходит все таблицы стилей страницы (в том числе чужие, Google Fonts) и
//    вшивает что найдёт — долго и не то;
//  • картинки — со своего источника: фон, иконка стиля, логотип клуба лежат в `public/`,
//    значок серии отдаёт наш `/api/series-emblems/…`, фото пловца — data-URL. Чужая картинка
//    «пачкает» canvas, и `toBlob` падает.

export interface PrintSize {
  width: number;
  height: number;
  /** Множитель пикселей: 2 — файл вдвое больше по каждой стороне (печать). */
  pixelRatio?: number;
}

/** Атрибут узла, который виден в предпросмотре, но в файл не попадает (рамка «выберите фото»). */
export const PRINT_SKIP_ATTR = { 'data-print-skip': '1' } as const;

/** Снимает узел в PNG заданного размера. */
export async function renderPrintPng(node: HTMLElement, size: PrintSize): Promise<Blob> {
  const blob = await toBlob(node, {
    width: size.width,
    height: size.height,
    pixelRatio: size.pixelRatio ?? 1,
    fontEmbedCSS: await printFontEmbedCss(),
    filter: (el) => !(el instanceof HTMLElement && el.dataset.printSkip),
  });
  if (!blob) throw new Error('The browser could not render the image');
  return blob;
}

/** Отдаёт файл пользователю обычным скачиванием. */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  // Не сразу: браузер читает blob уже после клика.
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
