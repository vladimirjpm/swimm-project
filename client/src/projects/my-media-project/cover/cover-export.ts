import { toBlob } from 'html-to-image';
import { coverFontEmbedCss } from './cover-fonts';
import { COVER_H, COVER_W } from './cover-layouts';

// Экспорт обложки: DOM → canvas → PNG 1280×720, целиком в браузере (решение Влада
// 12.11.2026) — на сервер ничего не уходит и там ничего не хранится.
//
// `html-to-image` клонирует узел с вычисленными стилями, кладёт его в SVG `foreignObject` и
// рисует на canvas. Отсюда два требования:
//  • шрифты — внутри картинки: `fontEmbedCSS` с data-URL (`coverFontEmbedCss`). Без него
//    библиотека обходит все таблицы стилей страницы (в том числе чужие, Google Fonts) и
//    вшивает что найдёт — долго и не то;
//  • картинки — со своего источника: фон, иконка стиля, логотип клуба лежат в `public/`,
//    значок серии отдаёт наш `/api/series-emblems/…`, фото пловца — data-URL. Чужая картинка
//    «пачкает» canvas, и `toBlob` падает.

/** Лимит YouTube на обложку при загрузке С ТЕЛЕФОНА; с компьютера — 50 МБ (справка, 12.11.2026). */
export const YOUTUBE_MOBILE_LIMIT_BYTES = 2 * 1024 * 1024;

/** Снимает узел обложки в PNG. Заглушки предпросмотра (`data-cover-skip`) в файл не попадают. */
export async function renderCoverPng(node: HTMLElement): Promise<Blob> {
  const blob = await toBlob(node, {
    width: COVER_W,
    height: COVER_H,
    pixelRatio: 1,
    fontEmbedCSS: await coverFontEmbedCss(),
    filter: (el) => !(el instanceof HTMLElement && el.dataset.coverSkip),
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
