import { useEffect, useState } from 'react';
import type { ShowcaseSeasonNotice } from '../../utils/helpers/season-helper';
import { H2H_PARAM } from '../../utils/routes';

/**
 * Данные табов страницы спортсмена (docs/plans/athlete-page-plan.md §3).
 * По хуку на таб — грузим лениво, когда таб открыли: страница целиком это пять запросов,
 * а открывают обычно один-два.
 *
 * Сервер отдаёт всё уже посчитанным (дельты, личники, медали): на клиенте арифметики
 * сезона нет и быть не должно — она живёт в SeasonAggregator/SwimmerPageBuilder.
 */

/** Признак качества времени — то же, что понимает UI_SwimTime. */
export interface SwimQualityDto {
  kind: 'protocol' | 'record';
  reason?: string | null;
}

export interface CompetitionRef {
  id: number;
  eventId?: number | null;
  name: string;
  isChampionship: boolean;
}

export interface MedalCounts {
  gold: number;
  silver: number;
  bronze: number;
}

export interface SwimmerCompetition {
  competitionId: number;
  eventId?: number | null;
  name: string;
  date: string;
  isChampionship: boolean;
  /** winter | summer | openwater | null — роль старта в сезоне. */
  kind?: string | null;
  poolType: string;
  waterKind: string;
  swims: number;
  points: number;
  medals: MedalCounts;
  bestPlace?: number | null;
}

export interface SwimmerSummary {
  season: number | null;
  label: string;
  points: number;
  medals: MedalCounts;
  swims: number;
  events: number;
  competitionCount: number;
  personalBests: number;
  competitions: SwimmerCompetition[];
}

export interface SwimmerBestTime {
  disciplineKey: string;
  styleId: number;
  stroke?: string | null;
  distance: string;
  poolType: string;
  waterKind: string;
  time?: string | null;
  timeMs?: number | null;
  quality?: SwimQualityDto | null;
  points?: number | null;
  place?: number | null;
  /** 'prelim' | 'final' | null — место prelim-заплыва рисуется без медали. */
  heatType?: string | null;
  ageInSeason?: number | null;
  splits?: string | null;
  date: string;
  competition: CompetitionRef;
  resultId: number;
  isCareerBest: boolean;
  /** Мастерс-старт: у разряда своя таблица нормативов с возрастными полосами. */
  isMasters: boolean;
}

export interface SwimmerPersonalBest {
  disciplineKey: string;
  styleId: number;
  stroke?: string | null;
  distance: string;
  poolType: string;
  time?: string | null;
  timeMs?: number | null;
  quality?: SwimQualityDto | null;
  points?: number | null;
  date: string;
  competition: CompetitionRef;
  resultId: number;
  holdsClubBest: boolean;
  deltaToClubBestMs?: number | null;
  holdsNationalAgeRecord: boolean;
  deltaToNationalAgeRecordMs?: number | null;
  nationalAgeRecordTime?: string | null;
  nationalAgeRecordQuality?: SwimQualityDto | null;
  nationalAgeKey?: string | null;
  /**
   * Чем мерили: «age 14», «masters 45-49», «open». У взрослых эталон — мастерская полоса
   * или ОТКРЫТЫЙ рекорд страны, и подпись «national age record» там была бы неправдой.
   */
  nationalRecordScope?: string | null;
}

/**
 * Место в одной дисциплине среди сверстников — пловцов того же года рождения и пола.
 * Строк результатов тут нет: они уже пришли из `/best-times` за тот же сезон, и клеятся
 * по `disciplineKey`. Второй набор тех же строк означал бы два «лучших времени сезона».
 */
export interface SwimmerDisciplineRank {
  disciplineKey: string;
  /** 1 — быстрейший в группе (тогда же строка получает бейдж SB). */
  rank: number;
  /** Сколько сверстников плавало эту дисциплину в сезоне, включая самого. */
  peerCount: number;
  timeMs: number;
  leaderTimeMs: number;
  gapToLeaderMs: number;
}

export interface SwimmerSeasonRanks {
  season: number | null;
  label: string;
  /** Возраст в сезоне; null — года рождения нет в базе, мест не будет. */
  age?: number | null;
  gender?: string | null;
  /** Подпись группы («girls 9») — её обязан показать UI рядом с местом. */
  groupLabel?: string | null;
  /** «Новый сезон откроется после зимнего чемпионата»; null — сезон открыт. */
  season_notice?: ShowcaseSeasonNotice | null;
  rows: SwimmerDisciplineRank[];
}

export interface SwimmerProgressPoint {
  date: string;
  time?: string | null;
  timeMs?: number | null;
  isPb: boolean;
  quality?: SwimQualityDto | null;
  points?: number | null;
  place?: number | null;
  /** 'prelim' | 'final' | null — место prelim-заплыва рисуется без медали. */
  heatType?: string | null;
  ageInSeason?: number | null;
  /** Мастерс-старт: у разряда своя таблица нормативов с возрастными полосами. */
  isMasters?: boolean;
  season: number;
  competition: CompetitionRef;
  resultId: number;
}

export interface SwimmerProgress {
  disciplineKey: string;
  styleId: number;
  stroke?: string | null;
  distance: string;
  poolType: string;
  points: SwimmerProgressPoint[];
}

/** Строка выдачи поиска пловцов (селектор соперника таба H2H). */
export interface SwimmerSearchHit {
  id: number;
  /** Имя уже выбрано сервером по правилу проекта: иврит, английский — фоллбеком. */
  name: string;
  /** 0 — года рождения в базе нет. */
  birthYear: number;
  gender?: string | null;
  clubName?: string | null;
}

/** Одна сторона сравнения — мини-карточка шапки и её статы. */
export interface SwimmerCompareSide {
  id: number;
  name: string;
  birthYear?: number | null;
  gender?: string | null;
  clubName?: string | null;
  /** Возраст в сезоне сравнения; null — режим карьеры или года рождения нет. */
  ageInSeason?: number | null;
  /** Сколько дисциплин пловец возглавляет среди сверстников; за карьеру всегда 0. */
  seasonBests: number;
  medals: MedalCounts;
  /** Лучшие очки FINA за один заплыв периода. */
  bestPoints: number;
  /**
   * Рекорды по классам: национальный, возрастные ступени, мастерские полосы. ⚠ Всегда за
   * карьеру: у записи справочника нет сезона, и карусель на эти цифры не влияет.
   */
  records?: { national: number; age: number; masters: number };
}

/** Лучший заплыв одной стороны на одной дистанции за период сравнения. */
export interface SwimmerCompareSwim {
  time?: string | null;
  timeMs?: number | null;
  /** Признак качества времени — пуст, пока в сравнение идут только зачётные заплывы. */
  quality?: SwimQualityDto | null;
  points?: number | null;
  date: string;
  competition?: CompetitionRef | null;
  resultId: number;
  /** Бейдж SB: быстрейший среди сверстников в этой дисциплине (только внутри сезона). */
  isSeasonBest?: boolean;
  /**
   * Рекорд, который бьёт это время: national / age / masters, либо null. Класс ОДИН —
   * старший (national > age > masters): два бейджа на одном времени не показываем.
   */
  record?: { kind: 'national' | 'age' | 'masters'; scope?: string | null } | null;
}

/** Пара времён одной дистанции в ОДНОМ бассейне — единица сравнения. */
export interface SwimmerComparePool {
  poolType: string;
  mine?: SwimmerCompareSwim | null;
  rival?: SwimmerCompareSwim | null;
  /** «Моё минус соперника», мс: отрицательное — быстрее хозяин страницы. null — плавал один. */
  deltaMs?: number | null;
}

/**
 * Строка сравнения — стиль × дистанция, бассейны внутри. `key` — стиль|дистанция БЕЗ пола
 * (в отличие от `disciplineKey` остальных табов): иначе у разнополой пары не совпала бы ни
 * одна дистанция. Бассейна в ключе тоже нет: «50 брасс» — одна дистанция, но 25м и 50м
 * сравниваются порознь, каждый своей парой времён.
 */
export interface SwimmerCompareRow {
  key: string;
  styleId: number;
  stroke?: string | null;
  distance: string;
  pools: SwimmerComparePool[];
}

export interface SwimmerCompare {
  season: number | null;
  label: string;
  mine: SwimmerCompareSide;
  rival: SwimmerCompareSide;
  rows: SwimmerCompareRow[];
  /** Сколько пар «дистанция × бассейн» плавали оба — только на них есть разрыв. */
  sharedCount: number;
  mineFaster: number;
  rivalFaster: number;
  ties: number;
  /**
   * Сезон, за который посчитаны SB. В режиме ∞ он НЕ равен периоду сравнения: места среди
   * сверстников живут внутри сезона, поэтому за карьеру считаются за витринный — и подпись
   * обязана это сказать, иначе цифра читается как «за всё время».
   */
  seasonBestSeason?: number | null;
  seasonBestLabel?: string | null;
}

/**
 * Значение `?season=` для запроса: null (режим All) → «all», иначе год начала сезона.
 * Отдельная функция, потому что «сезон не выбран» и «сезон ещё не известен» — разные
 * состояния, и путать их значит грузить карьеру вместо сезона на первом кадре.
 */
const seasonParam = (season: number | null) => (season == null ? 'all' : String(season));

interface Loaded<T> {
  data: T | null;
  loading: boolean;
  error: boolean;
}

/**
 * Кэш ответов на время жизни страницы + склейка одновременных запросов по одному адресу.
 *
 * Зачем (замеры 2026-08-25):
 * • ОДИН адрес просили ДВА хука. `best-times?season=all` в режиме ∞ нужен и табу Results,
 *   и разряду в шапке — уходило 4 запроса на загрузку вместо одного.
 * • Возврат на таб перезапрашивал: Progress → Best time → Progress давал три запроса,
 *   хотя данные не менялись.
 * • В dev каждый запрос удваивает StrictMode — склейка гасит и это.
 *
 * Кэш модульный, а не в сторе, СОЗНАТЕЛЬНО: переходы между пловцами — обычные ссылки
 * (сборка multi-page, SPA-роутера нет), поэтому стор умирает на навигации ровно так же,
 * а запись в единый `rootSlice` перерисовывала бы всех подписчиков. Радиус — этот файл.
 *
 * ⚠ Данные страницы пловца только читаются. Появятся мутации — потребуется инвалидация,
 * сейчас её сознательно нет.
 */
const CACHE_TTL_MS = 60_000;   // столько же, сколько сервер разрешает браузеру (max-age=60)

const responseCache = new Map<string, { at: number; data: unknown }>();
const inFlight = new Map<string, Promise<unknown>>();

/** Свежий ответ из кэша либо undefined. undefined ≠ null: null — это законное значение. */
function readCache<T>(url: string): T | undefined {
  const hit = responseCache.get(url);
  if (hit === undefined) return undefined;
  if (Date.now() - hit.at > CACHE_TTL_MS) { responseCache.delete(url); return undefined; }
  return hit.data as T;
}

function loadShared<T>(url: string): Promise<T> {
  const running = inFlight.get(url);
  if (running) return running as Promise<T>;

  const started = (async () => {
    const r = await fetch(url);
    if (!r.ok) throw new Error(`HTTP ${r.status}`);
    const data = (await r.json()) as T;
    responseCache.set(url, { at: Date.now(), data });
    return data;
  })();

  inFlight.set(url, started);
  // Снимаем ОБА исхода: одиночный `.catch` оставил бы необработанное отклонение у ветки
  // очистки, а падение сети не должно всплывать в консоли отдельной ошибкой.
  const forget = () => { if (inFlight.get(url) === started) inFlight.delete(url); };
  started.then(forget, forget);

  return started;
}

/** Общая загрузка JSON с кэшем, склейкой и отменой по смене входа. `null` в url — запрос не нужен. */
function useJson<T>(url: string | null): Loaded<T> {
  const [state, setState] = useState<Loaded<T>>(() => {
    const cached = url != null ? readCache<T>(url) : undefined;
    return { data: cached ?? null, loading: url != null && cached === undefined, error: false };
  });

  useEffect(() => {
    if (url == null) {
      setState({ data: null, loading: false, error: false });
      return;
    }

    // Из кэша отдаём СИНХРОННО: иначе возврат на таб моргал бы «Loading…» на кадр.
    const cached = readCache<T>(url);
    if (cached !== undefined) {
      setState({ data: cached, loading: false, error: false });
      return;
    }

    let alive = true;
    // Данные НЕ обнуляем: при смене сезона старая панель остаётся на экране и заменяется
    // на месте — иначе страница прыгала бы на каждый клик по карусели (урок клуба).
    setState((s) => ({ ...s, loading: true, error: false }));
    loadShared<T>(url).then(
      (data) => { if (alive) setState({ data, loading: false, error: false }); },
      () => { if (alive) setState({ data: null, loading: false, error: true }); },
    );
    return () => { alive = false; };
  }, [url]);

  return state;
}

export const useSwimmerSummary = (id: number | null, season: number | null, enabled = true) =>
  useJson<SwimmerSummary>(
    id != null && enabled ? `/api/swimmers/${id}/summary?season=${seasonParam(season)}` : null);

export const useSwimmerBestTimes = (id: number | null, season: number | null, enabled = true) =>
  useJson<SwimmerBestTime[]>(
    id != null && enabled ? `/api/swimmers/${id}/best-times?season=${seasonParam(season)}` : null);

export const useSwimmerPersonalBests = (id: number | null, poolType: string, enabled = true) =>
  useJson<SwimmerPersonalBest[]>(
    id != null && enabled ? `/api/swimmers/${id}/personal-bests?poolType=${poolType}` : null);

/**
 * Места среди сверстников за сезон. За карьеру (`season = null`) не запрашиваем: сравнение
 * живёт внутри одного сезона, а выборка когорты недешёвая.
 */
export const useSwimmerSeasonRanks = (id: number | null, season: number | null, enabled = true) =>
  useJson<SwimmerSeasonRanks>(
    id != null && season != null && enabled
      ? `/api/swimmers/${id}/season-ranks?season=${seasonParam(season)}`
      : null);

/**
 * Таб H2H: сравнение с выбранным пловцом за тот же период, что показывают остальные табы.
 * Без соперника запроса нет — панель до выбора показывает только поиск.
 */
export const useSwimmerCompare = (
  id: number | null, rivalId: number | null, season: number | null, enabled = true,
) =>
  useJson<SwimmerCompare>(
    id != null && rivalId != null && enabled
      ? `/api/swimmers/${id}/compare?${H2H_PARAM.b}=${rivalId}&season=${seasonParam(season)}`
      : null);

/**
 * Подсказки поиска пловцов. Запрос уходит через паузу после последней буквы: селектор
 * дёргается на каждый символ, а ILIKE «%x%» по всем именам — не тот запрос, который стоит
 * слать по десять раз на слово. Ответы кэширует общий кэш файла, поэтому возврат к уже
 * набранному запросу мгновенный.
 */
export function useSwimmerSearch(query: string, debounceMs = 250) {
  const [debounced, setDebounced] = useState('');

  useEffect(() => {
    const q = query.trim();
    // Один символ сервер и так не ищет — не тратим на него запрос.
    if (q.length < 2) { setDebounced(''); return; }
    const timer = setTimeout(() => setDebounced(q), debounceMs);
    return () => clearTimeout(timer);
  }, [query, debounceMs]);

  return useJson<SwimmerSearchHit[]>(
    debounced ? `/api/swimmers/search?q=${encodeURIComponent(debounced)}` : null);
}

export const useSwimmerProgress = (id: number | null, disciplineKey: string | null) =>
  useJson<SwimmerProgress>(
    id != null && disciplineKey
      ? `/api/swimmers/${id}/progress?disciplineKey=${encodeURIComponent(disciplineKey)}`
      : null);
