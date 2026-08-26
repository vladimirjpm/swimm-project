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
