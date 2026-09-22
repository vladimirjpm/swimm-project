import { useEffect, useState } from 'react';

/** Зачётная группа возрастной лестницы (Kids/Young/Juniors/Adults/Masters). */
export interface SwimmerAgeGroup {
  code: string;
  label: string;
  badge?: string | null;
}

/** Признак качества времени — то же, что понимает UI_SwimTime. */
export interface SwimQuality {
  kind: 'protocol' | 'record';
  reason?: string | null;
}

/**
 * Официальный рекорд, который держит пловец (строка справочника, где он записан держателем).
 * Это НЕ то же, что `holdsNationalAgeRecord` у личника: там сравнение по времени с рекордом
 * своей ступени, здесь — сам справочник.
 */
export interface SwimmerHeldRecord {
  regionType: string;
  regionCode: string;
  category: string;
  ageKey: string;
  gender: string;
  poolType: string;
  stroke: string;
  distance: string;
  time: string;
  date?: string | null;
  quality?: SwimQuality | null;
  /**
   * Рекорд проплыт первым этапом эстафеты — пометка админа или эстафета с промежуточными в
   * базе. Без подписи дата и время рекорда не находятся среди личных заплывов пловца.
   */
  relayLeadOff?: boolean;
  /** Эстафетный рекорд: команда-держатель (клуб из справочника) — слева вместо пловца. */
  relayTeam?: string | null;
  /** Состав эстафетного рекорда строкой «имя, имя, имя, имя». */
  relayHolders?: string | null;
  /**
   * Где проплыт рекорд — сервер нашёл среди заплывов пловца (время, дисциплина, бассейн,
   * дата ±1 день); в справочнике поля нет. null — рекорд до наших данных или заграничный.
   */
  meet?: {
    competitionId: number;
    eventId?: number | null;
    name: string;
    isChampionship: boolean;
    resultId: number;
  } | null;
  /**
   * Мировой эталон — правая сторона карточки рекорда: мировой рекорд мастерс ТОЙ ЖЕ ступени
   * или World Junior Record, если возраст рекорда в полосе WJR (`kind`). null — эталона нет:
   * возраст вне полосы (10–13, 18 у девушек, 14 у юношей) или абсолют страны.
   */
  worldRecord?: {
    /** От вида зависит подпись: «Masters WR» или «World Junior» — не «WR» (WJR-план §2-2). */
    kind?: 'masters' | 'junior';
    /** Полоса WJR «14-17» / «15-18», только у junior: одно время — против нескольких ступеней. */
    band?: string | null;
    time: string;
    date?: string | null;
    holder?: string | null;
    /** alpha-3 — флаг рисует UI_FlagEmoji. */
    countryCode?: string | null;
    /** Качество записи мирового справочника — И11: время показано вместе с ним. */
    quality?: SwimQuality | null;
  } | null;
}

/** Сезон для карусели. Ровно у одного `isDisplayDefault` — витринный (см. season-boundary-rule). */
export interface SwimmerSeasonOption {
  season: number;
  label: string;
  isCurrent: boolean;
  isDisplayDefault: boolean;
  swims: number;
}

/** Профиль пловца (GET /api/swimmers/{id}). camelCase — как отдаёт SwimmerProfileDto. */
export interface SwimmerProfile {
  id: number;
  fullName: string;
  firstName: string;
  lastName: string;
  firstNameEn: string;
  lastNameEn: string;
  birthYear: number;
  gender?: string | null;
  clubId?: number | null;
  clubName?: string | null;
  countryCode?: string | null;
  countryName?: string | null;
  avatarUrl?: string | null;
  origin: string;

  // Шапка страницы спортсмена (этап A2). Попап-карточка эти поля игнорирует.
  ageInSeason?: number | null;
  ageGroup?: SwimmerAgeGroup | null;
  programs?: string[];
  recordsHeld?: number;
  /** Сами рекорды — из них же считается recordsHeld. */
  records?: SwimmerHeldRecord[];
  seasons?: SwimmerSeasonOption[];
}

type State =
  | { status: 'loading' }
  | { status: 'notfound' }
  | { status: 'error' }
  | { status: 'ok'; profile: SwimmerProfile };

/**
 * Профиль пловца по id для страницы swimmer.html?swimmer=&lt;id&gt;.
 * 404 → 'notfound' (id вне справочника), сеть/500 → 'error'.
 */
export function useSwimmerProfile(id: number | null): State {
  const [state, setState] = useState<State>({ status: 'loading' });

  useEffect(() => {
    if (id == null || !Number.isFinite(id) || id <= 0) {
      setState({ status: 'notfound' });
      return;
    }
    let alive = true;
    setState({ status: 'loading' });
    (async () => {
      try {
        const r = await fetch(`/api/swimmers/${id}`);
        if (!alive) return;
        if (r.status === 404) { setState({ status: 'notfound' }); return; }
        if (!r.ok) { setState({ status: 'error' }); return; }
        const profile: SwimmerProfile = await r.json();
        setState({ status: 'ok', profile });
      } catch {
        if (alive) setState({ status: 'error' });
      }
    })();
    return () => { alive = false; };
  }, [id]);

  return state;
}
