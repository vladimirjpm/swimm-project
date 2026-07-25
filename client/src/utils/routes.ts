// Единая точка генерации и разбора «чистых» URL приложения.
//
// Контракт путей (root-absolute, синхронен с серверным rewrite в
// server/Swimm.API/Program.cs и dev-rewrite в client/vite.config.js):
//
//   /                       → home.html
//   /results                → results_main.html (дефолтные результаты; ?cat= в query)
//   /competitions           → competitions.html (список)
//   /competitions/{id}      → results_main.html (соревнование; id = число | 'last')
//   /groups                 → groups.html (список)
//   /groups/{slug}          → groups.html (страница группы)
//   /groups/{slug}/results  → results_main.html (результаты ростера группы)
//   /swimmers/{id}          → swimmer.html
//   /my-media               → media.html
//   /about                  → about.html
//
// ПРАВИЛО: в путь идёт только идентичность ресурса. Состояние вида
// (tab, filter, club, swim, eventId, cat, loadMode, themes) остаётся в query —
// его выставляют сами страницы через url.searchParams, routes.ts его не трогает.

const enc = encodeURIComponent;

/** Генераторы ссылок. Все возвращают root-absolute путь ('/…'). */
export const routes = {
  home: () => '/',
  about: () => '/about',

  results: () => '/results',
  competition: (id: string | number) => `/competitions/${enc(String(id))}`,
  competitionsList: () => '/competitions',

  groupsList: () => '/groups',
  group: (slug: string) => `/groups/${enc(slug)}`,
  groupResults: (slug: string) => `/groups/${enc(slug)}/results`,

  swimmer: (id: string | number) => `/swimmers/${enc(String(id))}`,
  myMedia: () => '/my-media',
};

/** Идентичность, вытащенная из текущего pathname (без query/hash). */
export interface RouteIdentity {
  /** Слаг группы: и для /groups/{slug}, и для /groups/{slug}/results. */
  groupSlug: string | null;
  /** true только для /groups/{slug}/results (режим результатов ростера). */
  groupResults: boolean;
  /** id соревнования из /competitions/{id} ('last' допустим). */
  competitionId: string | null;
  /** id пловца из /swimmers/{id}. */
  swimmerId: number | null;
}

/**
 * Разбор идентичности из pathname. Терпим к dev-префиксу /swimm-project и к
 * старому виду с .html (?group=/…) — старьё возвращает null-поля, чтобы вызывающий
 * код мог упасть на legacy-чтение из query во время переходного периода.
 */
export function parseRoute(pathname: string = window.location.pathname): RouteIdentity {
  // Снимаем возможный dev-префикс и завершающий слэш, нормализуем.
  const path = pathname.replace(/^\/swimm-project/, '').replace(/\/+$/, '') || '/';
  const seg = path.split('/').filter(Boolean); // ['groups','slug','results']

  const id: RouteIdentity = {
    groupSlug: null,
    groupResults: false,
    competitionId: null,
    swimmerId: null,
  };

  if (seg[0] === 'groups' && seg[1]) {
    id.groupSlug = decodeURIComponent(seg[1]);
    id.groupResults = seg[2] === 'results';
  } else if (seg[0] === 'competitions' && seg[1]) {
    id.competitionId = decodeURIComponent(seg[1]);
  } else if (seg[0] === 'swimmers' && seg[1]) {
    const n = Number(decodeURIComponent(seg[1]));
    id.swimmerId = Number.isFinite(n) && n > 0 ? n : null;
  }

  return id;
}
