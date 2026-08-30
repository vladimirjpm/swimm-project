// Чистые функции стартового протокола — вынесены отдельно от компонентов (правило теста:
// «добавляешь чистую функцию — вынеси в отдельный модуль»). Тестового раннера в client/ нет
// (см. package.json), поэтому модуль просто держат чистым: без React, без fetch, без DOM.

import type { StartListEvent, StartListSwim } from './types';

/**
 * За сколько приезжать до начала разминки (шаг Т8, §2 хендоффа). Не эвристика: время самой
 * разминки берётся из справки о старте, которую админ вводит по регламенту, а эти 30 минут
 * — правило витрины, одно на все соревнования.
 */
export const WARM_UP_LEAD_MINUTES = 30;

/** «≈14:45» по местному времени браузера; null/невалидная дата → «time TBA» (заплыву время
 *  ещё не назначили — это норма, не ошибка данных). */
export function formatApproxTime(iso: string | null | undefined): string {
  if (!iso) return 'time TBA';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return 'time TBA';
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  return `≈${hh}:${mm}`;
}

/** «08:00» — час без «≈»: разминка назначена регламентом, а не плавает вместе с заплывами. */
export function formatExactTime(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return null;
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

/**
 * «Приезжать к» — начало разминки минус <see cref="WARM_UP_LEAD_MINUTES"/>.
 *
 * ⚠ Считается ТОЛЬКО от разминки. Прежняя версия брала первый старт минус 45 минут — число
 * было выдумано, а витрина показывала его как точное; теперь без введённой разминки блока
 * просто нет (шаг Т8).
 */
export function arriveByWarmUp(warmUpIso: string | null | undefined): string | null {
  if (!warmUpIso) return null;
  const d = new Date(warmUpIso);
  if (Number.isNaN(d.getTime())) return null;
  return formatExactTime(new Date(d.getTime() - WARM_UP_LEAD_MINUTES * 60_000).toISOString());
}

/**
 * Порядок событий программы (footgun из задания): по `start_at`, а события без времени —
 * следом, по номеру заплыва (`event_number`) — иначе они всплывают в начало дня.
 */
export function sortEvents(events: StartListEvent[]): StartListEvent[] {
  return [...events].sort((a, b) => {
    const at = a.start_at ? Date.parse(a.start_at) : null;
    const bt = b.start_at ? Date.parse(b.start_at) : null;
    if (at != null && bt != null) return at - bt;
    if (at != null) return -1; // время есть — вперёд
    if (bt != null) return 1;
    return (a.event_number ?? 0) - (b.event_number ?? 0);
  });
}

/**
 * Дистанция для показа. У эстафеты источник печатает «4X50», и приписывать к ней «m»
 * нельзя — получается «4X50m». Единственное место, где решается этот вопрос: то же
 * форматирование нужно в ленте программы, в заплыве, в карточке пловца и в двух списках
 * «ближайших», а пять копий одной строки в этом проекте уже разъезжались (см. SwimRow).
 */
export function distanceLabel(distance: string): string {
  return /x/i.test(distance) ? distance : `${distance}m`;
}

/**
 * Название стиля для показа: ключ справочника приходит с подчёркиванием
 * (`individual_medley`), а на витрине проекта это «individual medley».
 *
 * Та же одна строка, что в `swimRowStrokeLabel` (components/swim-row). Импортировать её
 * оттуда нельзя: этот модуль намеренно чистый — без React, без DOM, — а тот тянет за собой
 * весь компонент строки. Если правило когда-нибудь усложнится, его место — общий модуль,
 * а не третья копия.
 */
export function strokeLabel(styleName: string): string {
  return styleName.replace(/_/g, ' ');
}

/** Дистанция + стиль одной строкой: «50m freestyle», «4X50 individual medley». */
export function swimLabel(distance: string, styleName: string): string {
  return `${distanceLabel(distance)} ${strokeLabel(styleName)}`;
}

/** Дисциплина программы: «50m freestyle» и все её заплывы по возрастам. */
export interface ProgrammeGroup {
  /** `50|freestyle` — дистанция и стиль, ключ группировки. */
  key: string;
  distance: string;
  styleName: string;
  /** События этой дисциплины в порядке времени (см. `sortEvents`). */
  events: StartListEvent[];
  /** Сумма заявок по всем возрастам дисциплины. */
  entries: number;
  /** Время первого события — по нему группа стоит в ленте дня. */
  startAt: string | null;
}

/**
 * Программа дня, сгруппированная по дисциплине (решение Влада 30.08.2026).
 *
 * Плоский список по времени — это восемьдесят почти одинаковых строк, в которых «50m
 * freestyle» повторяется восемь раз подряд, по разу на возраст. Родитель ищет в нём
 * дисциплину глазами. Сгруппированный: «50m freestyle» один раз, внутри — во сколько и
 * какая группа плывёт.
 *
 * Порядок групп — по ПЕРВОМУ событию, то есть хронологический: день не должен перестать
 * читаться сверху вниз только потому, что строки собрали в пачки. Внутри группы порядок
 * тот же `sortEvents` (без времени — в хвост по номеру заплыва).
 */
export function groupEventsByDiscipline(events: StartListEvent[]): ProgrammeGroup[] {
  const groups = new Map<string, ProgrammeGroup>();

  for (const e of sortEvents(events)) {
    const key = `${e.distance}|${e.style_name}`;
    let group = groups.get(key);
    if (!group) {
      group = {
        key,
        distance: e.distance,
        styleName: e.style_name,
        events: [],
        entries: 0,
        startAt: e.start_at,
      };
      groups.set(key, group);
    }
    group.events.push(e);
    group.entries += e.entries;
  }

  return [...groups.values()];
}

/** Строка ленты зума 1 (макет §4.1): «≈HH:MM · #номер · дистанция + стиль · категория». */
export function eventLineLabel(e: StartListEvent): string {
  const num = e.event_number != null ? `#${e.event_number}` : '#—';
  const cat = e.event_category ?? e.age_band ?? '';
  return [num, swimLabel(e.distance, e.style_name), cat].filter(Boolean).join(' · ');
}

/** Одна строка-команда эстафеты: имена всех ног в порядке дорожки/id. */
export interface RelayTeamRow {
  isRelay: true;
  heat: number;
  lane: number;
  club_name: string;
  members: StartListSwim[];
  seed_time: string | null;
  quality: string;
}

export type HeatRow = StartListSwim | RelayTeamRow;

/**
 * Склейка ног эстафеты (footgun из задания): у эстафеты четыре строки с одинаковыми
 * `heat`/`lane` и разными пловцами — это ОДНА команда, а не четыре дорожки. Личные заплывы
 * проходят насквозь без изменений.
 */
export function mergeRelayLanes(rows: StartListSwim[]): HeatRow[] {
  const result: HeatRow[] = [];
  const seen = new Set<string>();
  for (const row of rows) {
    if (!row.is_relay) {
      result.push(row);
      continue;
    }
    const key = `${row.heat}:${row.lane}:${row.club_id}`;
    if (seen.has(key)) continue;
    seen.add(key);
    const members = rows.filter(
      (r) => r.is_relay && r.heat === row.heat && r.lane === row.lane && r.club_id === row.club_id
    );
    result.push({
      isRelay: true,
      heat: row.heat,
      lane: row.lane,
      club_name: row.club_name,
      members,
      seed_time: row.seed_time,
      quality: row.quality,
    });
  }
  return result;
}

/** Поиск среди участников этого соревнования (~500 человек): подстрока с двух букв,
 *  без ранжирования — ищет и по русскому/английскому имени, и по клубу. */
export function matchesSearch(swim: StartListSwim, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (q.length < 2) return true;
  return (
    swim.swimmer_name.toLowerCase().includes(q) ||
    swim.club_name.toLowerCase().includes(q)
  );
}

/**
 * День соревнования короткой подписью — «Mon 16 Feb». Формат один на весь стартовый
 * протокол: он стоит и в подписи дня программы, и в выдаче поиска, и в шапке группы
 * заплывов карточки пловца, а три разных формата одной даты на одном экране читаются
 * как три разные даты.
 */
export function dayLabel(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  return d.toLocaleDateString(undefined, { weekday: 'short', day: '2-digit', month: 'short' });
}
