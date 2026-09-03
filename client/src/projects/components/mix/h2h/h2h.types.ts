/**
 * Общие типы семейства `UI_H2H*` — таба H2H страницы пловца
 * (макет `!design_handoff/design_handoff_h2h/`).
 *
 * Компоненты НЕ знают про API: страница раскладывает ответ `/compare` по этим формам.
 * Так же устроен и мини-вариант идентичности — данные приходят готовыми к показу.
 */

/**
 * Рекорды стороны по классам. Одной цифрой они не складываются: у подростка десяток
 * возрастных ступеней одного достижения, у взрослого — один национальный.
 */
export interface H2HRecordCounts {
  national: number;
  age: number;
  masters: number;
}

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
  /** male | female — от него зависит дефолтный портрет, когда своего фото нет. */
  gender?: string | null;
  /** alpha-3 или alpha-2; пусто — домашняя страна (флаг рисуется всегда). */
  countryCode?: string | null;
}

/**
 * Слот стороны сравнения: занят пловцом либо пуст. Это ДАННЫЕ, а не режим экрана —
 * `UI_H2HCompare` не знает слов «таб» и «страница».
 *
 * «Сторону нельзя сменить» (в табе левый — хозяин страницы) выражается через `onClear: null`,
 * а не через флаг варианта: иначе каждый новый контекст добавлял бы ветку внутрь компонента.
 */
export type H2HSlot =
  | {
      kind: 'swimmer';
      swimmer: H2HSwimmer;
      /** null — избранное недоступно (гость): сердечко не рисуется. */
      isFavorite?: boolean | null;
      onToggleFavorite?: () => void;
      /** null — слот несменяемый; иначе кнопка сброса стороны. */
      onClear?: (() => void) | null;
    }
  | {
      kind: 'empty';
      /** Подпись слота; по умолчанию — «выбери соперника» из макета. */
      label?: string;
      onPick?: () => void;
    };
