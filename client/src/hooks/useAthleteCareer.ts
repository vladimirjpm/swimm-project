import { useEffect, useState } from 'react';

/** Лучшее время по стилю за карьеру (GET /api/athletes/career). */
export interface CareerBest {
  stroke: string;
  distance: string;
  time: string;
  points: number;
  pool: string;
  competition: string;
  /** Дата соревнования, формат "DD/MM/YYYY" (как в остальных результатах). */
  date: string;
  position: number | null;
  gender: string;
  eventStyleAge: string;
  ageGroup: string;
  isMasters: boolean;
  /** Award-eligible ли то соревнование — без этого место 1/2/3 не медаль (см. HelperMedal). */
  isAward: boolean;
}

/** Один медальный заплыв за карьеру — за что дали место 1/2/3. */
export interface MedalDetail {
  position: 1 | 2 | 3;
  note: string;
  competition: string;
  date: string;
}

/** Карьерные (all-time) данные спортсмена. */
export interface AthleteCareer {
  competitions: number;
  races: number;
  since: number;
  totalPoints: number;
  gold: number;
  silver: number;
  bronze: number;
  bestByStyle: CareerBest[];
  medals: MedalDetail[];
}

/** Нет данных / ещё грузится / ошибка сети → нули (карточка показывает 0). */
export const EMPTY_CAREER: AthleteCareer = {
  competitions: 0,
  races: 0,
  since: 0,
  totalPoints: 0,
  gold: 0,
  silver: 0,
  bronze: 0,
  bestByStyle: [],
  medals: [],
};

// Кэш по имени на сессию — карточку открывают многократно.
const careerCache = new Map<string, Promise<AthleteCareer>>();

const loadCareer = (name: string): Promise<AthleteCareer> => {
  let p = careerCache.get(name);
  if (!p) {
    p = fetch(`/api/athletes/career?name=${encodeURIComponent(name)}`)
      .then((r) => (r.ok ? (r.json() as Promise<AthleteCareer>) : EMPTY_CAREER))
      .catch(() => EMPTY_CAREER);
    careerCache.set(name, p);
  }
  return p;
};

/** Карьерные данные спортсмена с сервера; пока грузится или данных нет — нули. */
export function useAthleteCareer(name?: string): AthleteCareer {
  const [career, setCareer] = useState<AthleteCareer>(EMPTY_CAREER);

  useEffect(() => {
    setCareer(EMPTY_CAREER);
    if (!name || name === 'all') return;
    let alive = true;
    loadCareer(name).then((c) => {
      if (alive) setCareer(c);
    });
    return () => {
      alive = false;
    };
  }, [name]);

  return career;
}
