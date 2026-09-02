/**
 * Общие типы семейства `UI_H2H*` — таба H2H страницы пловца
 * (макет `!design_handoff/design_handoff_h2h/`).
 *
 * Компоненты НЕ знают про API: страница раскладывает ответ `/compare` по этим формам.
 * Так же устроен и мини-вариант идентичности — данные приходят готовыми к показу.
 */

/** Медальный набор одной стороны. */
export interface H2HMedals {
  gold: number;
  silver: number;
  bronze: number;
}

/** Кто в паре быстрее/выше. null — ничья либо сравнивать не с чем. */
export type H2HWinner = 'left' | 'right' | null;

/** Сторона сравнения для мини-карточки шапки. */
export interface H2HSwimmer {
  id: number;
  name: string;
  club?: string | null;
  /** Готовая подпись чипа: «9 y · 2017». Пусто — чип не рисуется. */
  ageLabel?: string | null;
  avatarUrl?: string | null;
}
