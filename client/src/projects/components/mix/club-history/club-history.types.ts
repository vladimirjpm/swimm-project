/**
 * История клубов пловца — строка на (сезон × клуб), новые сезоны сверху. Приходит с сервера
 * готовой (`clubHistory` в профиле `/api/swimmers/{id}` и в сторонах `/compare`): правила —
 * клуб из строки результата, без псевдоклубов и эстафет, склеенный = приёмник — живут в
 * одном месте, серверном `SwimmerClubHistory` (план swimmer-club-change, Ф2). Клиент их не
 * повторяет, только рисует.
 */
export interface ClubSeason {
  /** Год начала сезона: 2025 = «2025/26». */
  season: number;
  seasonLabel: string;
  clubId: number;
  /** Название как в базе (иврит) — показывается по умолчанию. */
  name: string;
  /** Английское — фоллбек, когда ивритского нет. */
  nameEn?: string | null;
  swims: number;
  /** yyyy-MM-dd */
  firstDate: string;
  lastDate: string;
  /** Части сезона, где он плыл за клуб: winter — 25м, summer — 50м. */
  courses?: string[];
  /**
   * Прежнее название клуба, под которым он выступал в этой строке: клуб с тех пор
   * переименован (план swimmer-club-change §5, Н8). Строка подписана текущим, окно мелко
   * добавляет «then: …». null — заплывы под текущим названием.
   */
  thenName?: string | null;
  thenNameEn?: string | null;
}

/** Прежнее название строки для витрины: иврит по умолчанию, английский — фоллбек. null — его нет. */
export const clubSeasonThenName = (club: ClubSeason): string | null =>
  club.thenName || club.thenNameEn || null;

/** Настоящих клубов за карьеру — значок «несколько клубов» стоит при двух и больше (К4). */
export const clubHistoryCount = (history?: ClubSeason[] | null): number =>
  new Set((history ?? []).map((h) => h.clubId)).size;

/** Название для витрины: иврит по умолчанию, английский — фоллбек (правило 28.08.2026). */
export const clubSeasonName = (club: ClubSeason): string => club.name || club.nameEn || `#${club.clubId}`;

/** «05 Nov 2026» — дата старта в окне истории. */
export function clubHistoryDate(iso: string): string {
  const d = new Date(`${iso}T00:00:00`);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
}
