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
//   /clubs/{id}             → club.html
//   /my-media               → media.html
//   /about                  → about.html
//   /season-best            → season-best.html (списки лучших в сезоне; всё в query)
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

  club: (id: string | number) => `/clubs/${enc(String(id))}`,
  myMedia: () => '/my-media',

  /**
   * Страница списков «лучшие в сезоне»: кто быстрее всех в связке возраст × пол ×
   * стиль × дистанция × бассейн за сезон. С неё же будут списки по возрасту, по стилю и т.д.
   *
   * Страница-заготовка: маршрут и разбор параметров работают, самих списков ещё нет.
   *
   * Единственный генератор, принимающий query: у списка нет идентичности в пути — адресом
   * его делает как раз набор фильтров, и держать их порознь значит завести второй контракт.
   * Пустые поля не пишем: `/season-best?season=2025` — законный адрес «весь сезон».
   */
  seasonBest: (q: {
    /** Год НАЧАЛА сезона, как везде на публичной стороне (2025 → «2025/26»). */
    season: number;
    /** Возраст В СЕЗОНЕ (SeasonMath.AgeInSeason), а не на дату заплыва. */
    age?: number | null;
    gender?: string | null;
    /** Ключ стиля как на клиенте: freestyle / breaststroke / individual_medley… */
    stroke?: string | null;
    /** Дистанция без «m»: «50», «100». */
    distance?: string | null;
    /** «25m» / «50m» — времена разных бассейнов несравнимы, поэтому это часть адреса. */
    poolType?: string | null;
    /** Верхняя граница возраста; у витрины задана только для хвоста «21+». */
    ageTo?: number | null;
    /** Возрастная группа мастерского протокола («25-29»); задана ⇒ срез мастерский. */
    ageGroup?: string | null;
    /** Клуб среза (`club_id`). Ссылки со страницы спортсмена его не задают — он появляется,
     *  когда срез уже сузили на самой странице. */
    clubId?: number | null;
    /** true — «по одному лучшему заплыву на пловца». */
    bestPerSwimmer?: boolean;
    /** Кого подсветить в списке; на выбор самого списка не влияет. */
    swimmerId?: number | null;
  }) => {
    const params = new URLSearchParams();
    params.set('season', String(q.season));
    if (q.age != null) params.set('age', String(q.age));
    if (q.ageTo != null) params.set('age_to', String(q.ageTo));
    if (q.ageGroup) params.set('age_group', q.ageGroup);
    if (q.gender) params.set('gender', q.gender);
    if (q.stroke) params.set('stroke', q.stroke);
    if (q.distance) params.set('distance', String(q.distance).replace(/m$/i, ''));
    if (q.poolType) params.set('pool', q.poolType);
    if (q.clubId != null) params.set('club', String(q.clubId));
    if (q.bestPerSwimmer) params.set('best', 'true');
    if (q.swimmerId != null) params.set('swimmer', String(q.swimmerId));
    return `/season-best?${params.toString()}`;
  },
};

/**
 * Фильтр страницы `/season-best`, разобранный из query. Живёт рядом с генератором
 * <c>routes.seasonBest</c> намеренно: писать и читать один адрес в разных местах значит
 * завести два контракта, которые разъедутся на первом же переименовании параметра.
 *
 * Мусор и отсутствующие значения дают null, а не бросают: это витрина, а не форма.
 * `season` без значения — тоже null, страница решает сама, что показывать по умолчанию.
 */
export interface SeasonBestQuery {
  season: number | null;
  age: number | null;
  /** Верх диапазона возраста; у витрины это хвост «21+» (age=21, age_to=99). */
  ageTo: number | null;
  /** Возрастная группа мастерского протокола; задана ⇒ страница показывает мастерский срез. */
  ageGroup: string | null;
  gender: 'male' | 'female' | null;
  stroke: string | null;
  distance: string | null;
  poolType: string | null;
  /** Клуб среза — id, а не имя: страница фильтрует по `club_id`. */
  clubId: number | null;
  /** true — «по одному лучшему заплыву на пловца» (`?best=true`). */
  bestPerSwimmer: boolean;
  swimmerId: number | null;
}

export function parseSeasonBestQuery(search: string = window.location.search): SeasonBestQuery {
  const p = new URLSearchParams(search);
  const num = (key: string) => {
    const n = Number(p.get(key));
    return Number.isFinite(n) && n > 0 ? n : null;
  };
  const gender = (p.get('gender') ?? '').toLowerCase();

  return {
    season: num('season'),
    age: num('age'),
    ageTo: num('age_to'),
    ageGroup: p.get('age_group') || null,
    gender: gender === 'male' || gender === 'female' ? gender : null,
    stroke: p.get('stroke') || null,
    distance: p.get('distance') || null,
    poolType: p.get('pool') || null,
    // Клуб и режим строк страница в адрес ПИСАЛА, но не читала — присланная ссылка
    // открывалась без выбранного клуба. Читаем оба (2026-08-26).
    clubId: num('club'),
    bestPerSwimmer: p.get('best') === 'true',
    swimmerId: num('swimmer'),
  };
}

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
  /** id клуба из /clubs/{id}. */
  clubId: number | null;
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
    clubId: null,
  };

  if (seg[0] === 'groups' && seg[1]) {
    id.groupSlug = decodeURIComponent(seg[1]);
    id.groupResults = seg[2] === 'results';
  } else if (seg[0] === 'competitions' && seg[1]) {
    id.competitionId = decodeURIComponent(seg[1]);
  } else if (seg[0] === 'swimmers' && seg[1]) {
    const n = Number(decodeURIComponent(seg[1]));
    id.swimmerId = Number.isFinite(n) && n > 0 ? n : null;
  } else if (seg[0] === 'clubs' && seg[1]) {
    const n = Number(decodeURIComponent(seg[1]));
    id.clubId = Number.isFinite(n) && n > 0 ? n : null;
  }

  return id;
}
