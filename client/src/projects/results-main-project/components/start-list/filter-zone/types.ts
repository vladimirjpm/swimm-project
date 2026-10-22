// Типы зоны фильтров стартового протокола (5d/5dm). Отдельным файлом: их берут и части
// зоны, и чистая модель (`plan-model.buildSessions`), и таб — без тяги React-разметки.

export type FilterZoneMode = 'all' | 'plan';

/**
 * Сессия = один ДЕНЬ одного протокола федерации (`buildSessions` в plan-model). У окружных
 * чемпионатов протоколов несколько на старт, а один протокол бывает на несколько дней
 * (compID 16854: 23 и 24.10) — поэтому ключ пара, а не compID.
 */
export interface FilterZoneSession {
  /** `sessionKey(orgCompId, day)` — идентичность чипа. */
  key: string;
  orgCompId: number;
  /** yyyy-MM-dd дня; null — программа не загружена, сессия = протокол целиком. */
  day: string | null;
  /** Номер протокола в подписи; null — протокол один, номер не пишем. */
  index: number | null;
  /** dd/MM; null — даты у привязки нет. */
  date: string | null;
  /** yyyy-MM-dd — из неё берётся день недели. */
  dateIso: string | null;
  /** Имя протокола у федерации — идёт в заголовок-счётчик активной сессии. */
  sourceName: string | null;
  /** Заявок в сессии — счётчик чипа в режиме All. */
  entries: number;
  /** Моих заплывов в сессии — счётчик чипа в режиме My plan. */
  mine: number;
}

export interface FilterZonePerson {
  id: number;
  name: string;
  /** Пловец-фаворит из профиля: в All он золотой, обычный — серый. */
  favorite?: boolean;
}

export interface FilterZoneClub {
  id: number;
  name: string;
  swimmers: number;
}
