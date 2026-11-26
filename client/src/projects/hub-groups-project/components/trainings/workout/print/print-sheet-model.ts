import type { Workout, WorkoutLine, WorkoutSet } from '../../../../types';
import { cellLabel, intensityKeys, lineDir, repsDistanceLabel } from '../workout-format';
import { setSwumBy } from '../workout-for-level';
import { formatKm, formatMeters, levelMeters, setMeters } from '../workout-totals';
import type {
  MainLayoutModel, SheetCube, SheetDir, SheetLeftItem, SheetLine, SheetModel, WorkoutPrintSource,
} from './print-types';

/**
 * Модель листа печати (docs/plans/workout-print-plan.md, §3.2–3.6): `WorkoutPrintSource` → шапка,
 * кубы сетов, направление листа, деление на главные / прочие. Чистые функции без DOM; тесты —
 * `print-sheet-model.test.mjs`.
 */

/** Метка «главный сет» (Ш1б добавляет `main?` в `WorkoutSet`); читаем безопасно, пока тип не обновлён. */
export function isMainSet(set: WorkoutSet): boolean {
  return (set as { main?: boolean }).main === true;
}

/**
 * Отдых на листе: «rest :30» / «Rest:30» → «R:30», «rest 0:30» → «R:0:30» (В3). Всё остальное
 * («1:45», «:35», уже «R:20») — как есть.
 */
export function restLabel(sendOff: string | null | undefined): string {
  const text = (sendOff ?? '').trim();
  const m = /^rest\s*(.*)$/i.exec(text);
  if (!m) return text;
  const rest = m[1].trim();
  if (rest.startsWith(':')) return `R${rest}`;
  if (/^\d/.test(rest)) return `R:${rest}`;
  return text;
}

const HEBREW_OR_ARABIC = /[֐-׿؀-ۿ]/;
const LATIN = /[A-Za-z]/;

/**
 * Направление листа: строк с ивритом больше, чем строк с латиницей (среди строк с текстом) → `rtl`,
 * иначе `ltr` (§3.6). Строка с иврита и латиницей вместе — ивритская (как `lineDir`). Строк с
 * буквами нет вовсе — решают названия сетов.
 */
export function sheetDir(workout: Workout): SheetDir {
  let he = 0;
  let latin = 0;
  const count = (text: string | null | undefined) => {
    if (!text) return;
    if (HEBREW_OR_ARABIC.test(text)) he += 1;
    else if (LATIN.test(text)) latin += 1;
  };
  for (const set of workout.sets) for (const line of set.lines) count(line.text);
  if (he === 0 && latin === 0) for (const set of workout.sets) count(set.title);
  return he > latin ? 'rtl' : 'ltr';
}

const chipLabel = (key: string) => (key === 'sprint' ? 'Sprint' : key.toUpperCase());

function sheetLine(line: WorkoutLine, levelKey: string | null): SheetLine {
  const own = levelKey != null ? line.per_level?.[levelKey] : undefined;
  const text = (line.text ?? '').trim();
  const intensity = (line.intensity ?? '').trim();
  const keys = intensityKeys(intensity);
  return {
    head: repsDistanceLabel(own?.reps ?? line.reps, line.distance),
    text,
    textDir: lineDir(text),
    chips: keys ? keys.map(chipLabel) : null,
    intensityText: !keys && intensity ? intensity : null,
    cell: own ? cellLabel({ ...own, send_off: restLabel(own.send_off) }) : '',
  };
}

const hasContent = (l: SheetLine) => !!(l.head || l.text || l.chips || l.intensityText || l.cell);

/**
 * Кубы листа по порядку выполнения. Сеты, которых уровень не плывёт, пропускаются, а номера
 * остаются тренерскими (дырка в нумерации, как у «Set N» в `workoutForLevel`).
 */
function sheetCubes(source: WorkoutPrintSource): SheetCube[] {
  const { workout, levelId, setNumbers } = source;
  const levelKey = levelId == null ? null : String(levelId);
  const cubes: SheetCube[] = [];
  workout.sets.forEach((set, i) => {
    if (!setSwumBy(set, levelId)) return;
    const no = setNumbers?.[i] ?? i + 1;
    const meters = setMeters(set, levelId);
    cubes.push({
      no,
      title: set.title?.trim() || `Set ${no}`,
      repeat: set.repeat,
      meters,
      metersLabel: meters > 0 ? formatMeters(meters) : null,
      main: isMainSet(set),
      lines: set.lines.map((line) => sheetLine(line, levelKey)).filter(hasContent),
    });
  });
  return cubes;
}

/**
 * Раскладка «главный справа» (§3.3): правая колонка — главные по порядку, левая — остальные; на
 * месте главных (подряд идущие — одним указателем) в левой колонке стоит «➜ ④». Нужны и главный,
 * и не главный сет; иначе — null (кубы со стрелками, §3.4).
 */
export function splitMain(cubes: SheetCube[]): MainLayoutModel | null {
  const right = cubes.filter((c) => c.main);
  if (right.length === 0 || right.length === cubes.length) return null;
  const left: SheetLeftItem[] = [];
  for (const cube of cubes) {
    if (!cube.main) {
      left.push({ kind: 'cube', cube });
      continue;
    }
    const last = left[left.length - 1];
    if (last?.kind === 'pointer') last.nos.push(cube.no);
    else left.push({ kind: 'pointer', nos: [cube.no] });
  }
  return { right, left };
}

export function toSheetModel(source: WorkoutPrintSource): SheetModel {
  const cubes = sheetCubes(source);
  const totalMeters = levelMeters(source.workout, source.levelId);
  const cellChars = Math.max(0, ...cubes.flatMap((c) => c.lines.map((l) => l.cell.length)));
  return {
    dir: sheetDir(source.workout),
    header: { groupName: source.groupName, level: source.level, kmLabel: formatKm(totalMeters) },
    note: source.workout.note?.trim() || null,
    cubes,
    cellChars,
    totalMeters,
    mainLayout: splitMain(cubes),
  };
}
