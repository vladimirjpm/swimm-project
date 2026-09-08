/**
 * Адрес витрины: якорь секции + состояние вида, всё в hash.
 *
 *   #content-box-md
 *   #c-ui-swimm-style-icon?mode=dark&theme=competition-blue&pc=ui-swimm-style-icon
 *                          &p.styleType=icon-len&p.lenPlacement=below
 *
 * Почему hash, а не query: страница одна и статическая, hash меняется без перезагрузки и
 * не уезжает на сервер. Разбор свой, а не `getElementById(location.hash)`: якорь у нас
 * склеен с параметрами через `?`, штатный скролл браузера по такому хешу не сработает.
 *
 * Ключи:
 *   anchor  — id секции, куда скроллить (и что подсвечено в сайдбаре)
 *   mode    — light | dark (ось data-mode приложения)
 *   theme   — значение data-theme (training-* / competition-*)
 *   bg, w   — фон и ширина сцены превью
 *   pc      — id компонента, к которому относятся p.* (prop component)
 *   p.<имя> — значение пропа этого компонента
 *
 * `pc` отделён от `anchor` намеренно: якорь ползёт за скроллом, а пропсы принадлежат
 * тому компоненту, который последним крутили. Без этого поделиться ссылкой на конкретное
 * состояние компонента можно было бы только не прокручивая страницу.
 */

export interface UiKitUrlState {
  anchor: string;
  params: Record<string, string>;
}

export const PROP_PREFIX = 'p.';

export function readUrlState(): UiKitUrlState {
  const raw = window.location.hash.replace(/^#/, '');
  const qIdx = raw.indexOf('?');
  const anchor = qIdx === -1 ? raw : raw.slice(0, qIdx);
  const params: Record<string, string> = {};
  if (qIdx !== -1) {
    new URLSearchParams(raw.slice(qIdx + 1)).forEach((value, key) => {
      params[key] = value;
    });
  }
  return { anchor: decodeURIComponent(anchor), params };
}

/**
 * Последний адрес, который написали мы сами. Нужен, чтобы отличить свою запись от чужой:
 * вставленную в адресную строку ссылку надо ПРИМЕНИТЬ, а не затереть обратно.
 */
let lastWritten = '';

export function lastWrittenHash(): string {
  return lastWritten;
}

/** Кириллица в значениях: браузер отдаёт hash процент-кодированным, а пишем мы как есть. */
export function sameHash(a: string, b: string): boolean {
  const norm = (h: string) => {
    try {
      return decodeURIComponent(h);
    } catch {
      return h;
    }
  };
  return norm(a) === norm(b);
}

export function writeUrlState({ anchor, params }: UiKitUrlState): void {
  const search = new URLSearchParams();
  // Порядок фиксируем: иначе один и тот же вид даёт разные ссылки и их неудобно сравнивать.
  Object.keys(params)
    .sort()
    .forEach((key) => {
      const value = params[key];
      if (value !== '' && value != null) search.set(key, value);
    });
  const query = search.toString();
  const hash = `#${anchor}${query ? `?${query}` : ''}`;
  lastWritten = hash;
  if (sameHash(hash, window.location.hash)) return;
  // replaceState, а не location.hash = …: скролл-спай меняет якорь десятки раз за один
  // прокрут, и каждый такой шаг иначе ложился бы в историю браузера.
  window.history.replaceState(null, '', hash);
}

/** Значения пропсов компонента из URL (`p.*`), если URL адресует именно его. */
export function propsFromUrl(
  componentId: string,
  { params }: UiKitUrlState
): Record<string, string> {
  if (params.pc !== componentId) return {};
  const out: Record<string, string> = {};
  Object.entries(params).forEach(([key, value]) => {
    if (key.startsWith(PROP_PREFIX)) out[key.slice(PROP_PREFIX.length)] = value;
  });
  return out;
}
