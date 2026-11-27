import type { SheetCube, SheetDir, SheetLine, SheetModel, SheetTimeKind } from '../print-types';

/**
 * Листы «листа кубов» — видов `numbers` и `numbers-two-sides` (хендофф дизайнера 27.11.2026, вторая
 * версия; docs/plans/workout-print-plan.md §4.3). Чистые функции без DOM; тесты — `numbers-pages.test.mjs`.
 */

export interface TwoSidesSplit {
  /** Лицо: сеты до первого главного. */
  front: SheetCube[];
  /** Оборот: первый главный и всё после него. */
  back: SheetCube[];
  /** Номер первого главного — в полосе «Next ④ Main set … page 2». */
  mainNo: number;
}

/**
 * Деление по ПЕРВОМУ главному — только галочка `main`, без эвристик (решение Влада 27.11).
 * Главного нет или он первый — делить нечего: null, вид `numbers-two-sides` недоступен.
 */
export function splitTwoSides(cubes: SheetCube[]): TwoSidesSplit | null {
  const i = cubes.findIndex((c) => c.main);
  if (i <= 0) return null;
  return { front: cubes.slice(0, i), back: cubes.slice(i), mainNo: cubes[i].no };
}

/** Часть подписи внизу листа: «R = מנוחה», «@ = יציאה» (знак — моноширинным, как времена). */
export interface LegendPart {
  sign: 'R' | '@';
  word: string;
}

const LEGEND_WORDS: Record<SheetDir, Record<SheetTimeKind, string>> = {
  rtl: { rest: 'מנוחה', 'send-off': 'יציאה' },
  ltr: { rest: 'rest', 'send-off': 'send-off' },
};

/**
 * Подпись внизу листа (хендофф 27.11 §3, §10; Влад 27.11: «напиши внизу не крупно, что R = מנוחה»):
 * `R = …` — если на ЭТОМ листе есть время отдыха, `@ = …` — если есть время выхода; в таком порядке.
 * Язык — по направлению листа. Времён нет — подписи нет (пустой список).
 */
export function legendParts(cubes: SheetCube[], dir: SheetDir): LegendPart[] {
  const kinds = new Set(cubes.flatMap((c) => c.lines.map((l) => l.timeKind)));
  const parts: LegendPart[] = [];
  if (kinds.has('rest')) parts.push({ sign: 'R', word: LEGEND_WORDS[dir].rest });
  if (kinds.has('send-off')) parts.push({ sign: '@', word: LEGEND_WORDS[dir]['send-off'] });
  return parts;
}

/** Один лист кубов = одна напечатанная страница. */
export interface NumbersPage {
  /** Ключ листа и его копии замера: 'numbers', 'front', 'back'. */
  key: string;
  cubes: SheetCube[];
  /** Метка страницы в шапке: «1 / 2»; null — лист один. */
  pageMark: string | null;
  /** Полоса внизу «Next ④ Main set … page 2» — номер главного; null — полосы нет. */
  nextMainNo: number | null;
  /** Заметка тренера — только на первом листе. */
  note: string | null;
  /** Подпись про «R» и «@» — своя у каждого листа. */
  legend: LegendPart[];
}

export type NumbersViewKind = 'numbers' | 'numbers-two-sides';

/** Листы вида: `numbers` — один со всеми сетами; `numbers-two-sides` — лицо и оборот (не делится — null). */
export function numbersPages(model: Pick<SheetModel, 'cubes' | 'note' | 'dir'>, kind: NumbersViewKind): NumbersPage[] | null {
  const page = (key: string, cubes: SheetCube[], rest: Omit<NumbersPage, 'key' | 'cubes' | 'legend'>): NumbersPage => ({
    key, cubes, ...rest, legend: legendParts(cubes, model.dir),
  });
  if (kind === 'numbers') return [page('numbers', model.cubes, { pageMark: null, nextMainNo: null, note: model.note })];
  const split = splitTwoSides(model.cubes);
  if (!split) return null;
  return [
    page('front', split.front, { pageMark: '1 / 2', nextMainNo: split.mainNo, note: model.note }),
    page('back', split.back, { pageMark: '2 / 2', nextMainNo: null, note: null }),
  ];
}

/** Ключ строки на листе (для правила «текст под дистанцией»): номер сета · индекс строки. */
export const numbersLineKey = (cube: SheetCube, index: number) => `${cube.no}.${index}`;

/**
 * Какие колонки берёт текст строки рядом с дистанцией (§3): без дистанции (пояснение) — и колонку
 * дистанции (`wide`), без времени — и колонку времени (`toEnd`). Под дистанцией (`NUM_BELOW_ATTR`)
 * текст и так идёт во всю ширину куба.
 */
export function lineTextSpan(line: Pick<SheetLine, 'head' | 'time'>): { wide: boolean; toEnd: boolean } {
  return { wide: !line.head, toEnd: !line.time };
}
