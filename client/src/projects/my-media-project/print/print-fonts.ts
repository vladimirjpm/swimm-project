// Шрифты «печатной» картинки My media — обложки YouTube и плаката (хендофф §2.2): Heebo 700/800/900 (иврит),
// Montserrat 900 и 900 italic (цифры, время, год), Open Sans 700/800 (подписи).
//
// Файлы — из npm-пакетов `@fontsource/*`, Vite кладёт их в сборку как свои ассеты. Не Google
// Fonts ссылкой: картинку собирает canvas, а ему шрифт нужен ВНУТРИ картинки (data-URL) —
// чужой источник в экспорт не попадёт, и картинка молча выйдет системным шрифтом.
//
// Список один на два места: `ensurePrintFonts` регистрирует шрифты на странице (предпросмотр),
// `printFontEmbedCss` отдаёт те же файлы как `@font-face` с data-URL (экспорт). Разойтись им
// нельзя — иначе предпросмотр и файл были бы набраны по-разному.
//
// Модуль грузится только вместе с диалогом обложки или плаката (ленивый чанк): страница `/my-media`
// за шрифты не платит, пока диалог не открыли.

import heeboHebrew700 from '@fontsource/heebo/files/heebo-hebrew-700-normal.woff2?url';
import heeboLatin700 from '@fontsource/heebo/files/heebo-latin-700-normal.woff2?url';
import heeboHebrew800 from '@fontsource/heebo/files/heebo-hebrew-800-normal.woff2?url';
import heeboHebrew900 from '@fontsource/heebo/files/heebo-hebrew-900-normal.woff2?url';
import heeboLatin800 from '@fontsource/heebo/files/heebo-latin-800-normal.woff2?url';
import heeboLatin900 from '@fontsource/heebo/files/heebo-latin-900-normal.woff2?url';
import montserrat900 from '@fontsource/montserrat/files/montserrat-latin-900-normal.woff2?url';
import montserrat900Italic from '@fontsource/montserrat/files/montserrat-latin-900-italic.woff2?url';
import openSans700 from '@fontsource/open-sans/files/open-sans-latin-700-normal.woff2?url';
import openSans800 from '@fontsource/open-sans/files/open-sans-latin-800-normal.woff2?url';

/** Диапазоны — как в CSS пакетов `@fontsource` (подмножества `hebrew` и `latin`). */
const HEBREW = 'U+0307-0308,U+0590-05FF,U+200C-2010,U+20AA,U+25CC,U+FB1D-FB4F';
const LATIN =
  'U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,'
  + 'U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD';

interface PrintFontFile {
  family: string;
  weight: string;
  style: 'normal' | 'italic';
  url: string;
  range: string;
}

/**
 * Имена семейств — с префиксом `Print`: на странице могут оказаться одноимённые шрифты из
 * другого источника (или установленные в системе), и тогда было бы не понять, каким набран
 * предпросмотр. Свои имена гарантируют: картинка набрана ровно этими файлами.
 */
export const PRINT_FONT = {
  hebrew: "'Print Heebo', sans-serif",
  digits: "'Print Montserrat', 'Print Heebo', sans-serif",
  text: "'Print Open Sans', 'Print Heebo', sans-serif",
} as const;

const FONTS: PrintFontFile[] = [
  { family: 'Print Heebo', weight: '700', style: 'normal', url: heeboHebrew700, range: HEBREW },
  { family: 'Print Heebo', weight: '700', style: 'normal', url: heeboLatin700, range: LATIN },
  { family: 'Print Heebo', weight: '800', style: 'normal', url: heeboHebrew800, range: HEBREW },
  { family: 'Print Heebo', weight: '900', style: 'normal', url: heeboHebrew900, range: HEBREW },
  { family: 'Print Heebo', weight: '800', style: 'normal', url: heeboLatin800, range: LATIN },
  { family: 'Print Heebo', weight: '900', style: 'normal', url: heeboLatin900, range: LATIN },
  { family: 'Print Montserrat', weight: '900', style: 'normal', url: montserrat900, range: LATIN },
  { family: 'Print Montserrat', weight: '900', style: 'italic', url: montserrat900Italic, range: LATIN },
  { family: 'Print Open Sans', weight: '700', style: 'normal', url: openSans700, range: LATIN },
  { family: 'Print Open Sans', weight: '800', style: 'normal', url: openSans800, range: LATIN },
];

let registered: Promise<void> | null = null;

/**
 * Регистрирует шрифты на странице и ждёт, пока все файлы загрузятся. Загружаем ВСЕ сразу, а
 * не «по мере появления текста»: подгонка кегля (`FitText`) меряет ширину строки, и замер до
 * загрузки шрифта был бы замером системного.
 */
export function ensurePrintFonts(): Promise<void> {
  registered ??= Promise.all(FONTS.map(async (f) => {
    const face = new FontFace(f.family, `url(${f.url}) format('woff2')`, {
      weight: f.weight, style: f.style, unicodeRange: f.range,
    });
    document.fonts.add(face);
    await face.load();
  })).then(() => undefined);
  // Сорвалась загрузка (сеть) — следующая попытка начнёт заново, а не вернёт старую ошибку.
  registered.catch(() => { registered = null; });
  return registered;
}

function toDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}

let embedCss: Promise<string> | null = null;

/** `@font-face` тех же файлов с data-URL — их экспорт вшивает в картинку. */
export function printFontEmbedCss(): Promise<string> {
  embedCss ??= Promise.all(FONTS.map(async (f) => {
    const response = await fetch(f.url);
    if (!response.ok) throw new Error(`Font ${f.family} ${f.weight} failed to load`);
    const data = await toDataUrl(await response.blob());
    return `@font-face{font-family:'${f.family}';font-weight:${f.weight};font-style:${f.style};`
      + `src:url(${data}) format('woff2');unicode-range:${f.range};}`;
  })).then((rules) => rules.join('\n'));
  embedCss.catch(() => { embedCss = null; });
  return embedCss;
}
