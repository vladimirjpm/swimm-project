import type { Workout, WorkoutLine, WorkoutSet } from '../../../../types';
import { cellLabel, intensityKeys, lineDir, repsDistanceLabel } from '../workout-format';
import { setSwumBy } from '../workout-for-level';
import { formatKm, formatMeters, levelMeters, setMeters } from '../workout-totals';
import type {
  MainLayoutModel, SheetCube, SheetDir, SheetLeftItem, SheetLine, SheetModel, SheetRichText, SheetTextPart, SheetTimeKind,
  WorkoutPrintSource,
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

/**
 * Время уровня на печатном листе (хендофф 27.11 §10, решение Влада 27.11: «использовать этот символ
 * для времени выхода»): отдых — как у `restLabel` («rest :20» → «R:20»; уже «R:20» — тоже отдых),
 * любое другое непустое значение — время выхода со знаком «@» без пробела: «2:10» → «@2:10». Свой
 * «@» тренера второго не получает. Только печать: на сайте время показывается как раньше.
 */
export function sendOffLabel(sendOff: string | null | undefined): { label: string; kind: SheetTimeKind | null } {
  const label = restLabel(sendOff);
  if (!label) return { label: '', kind: null };
  if (/^R:/i.test(label)) return { label, kind: 'rest' };
  return { label: label.startsWith('@') ? label : `@${label}`, kind: 'send-off' };
}

/** Слова, которые в названии главного сета значат «главный»: их часть названия встаёт в рамку (§4). */
const MAIN_WORD = /^(מרכזי|main|main set)$/i;

/**
 * Название сета для подписи куба (хендофф 27.11 §4, решение Влада 27.11: «מרכזי сделай в бордере как
 * Main, а сам Main убери»). У главного: части названия через « · »; часть «מרכזי» / «Main» /
 * «Main set» — слово в рамке, остальные — текст следом. Такой части нет (или названия нет) — в рамке
 * `Main`, название целиком следом. У не главного название не разбирается.
 */
export function mainTitleParts(ownTitle: string | null, main: boolean): { word: string | null; rest: string | null } {
  if (!main) return { word: null, rest: ownTitle };
  if (!ownTitle) return { word: 'Main', rest: null };
  const parts = ownTitle.split('·').map((p) => p.trim()).filter(Boolean);
  const at = parts.findIndex((p) => MAIN_WORD.test(p));
  if (at < 0) return { word: 'Main', rest: ownTitle };
  const rest = parts.filter((_, i) => i !== at).join(' · ');
  return { word: parts[at], rest: rest || null };
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

/**
 * Уровень нагрузки внутри текста тренера (хендофф 27.11 §5, решения Влада 27.11: «все V1, V2 — в
 * бордере», «4V это V4», `1-3V` в тексте — «один раз на V3»): `4V` / `V4`, перед ним может стоять
 * «1-». Цифра — шкала 0…5, как у `intensityKeys`; «Valve», «2Vx», «50V» не трогаются.
 */
const TEXT_LEVEL = /(?<![A-Za-z0-9])(\d+-)?(?:([0-5])V|V([0-5]))(?![A-Za-z0-9])/g;

/** Текст строки → куски «текст / чип уровня (с приставкой «1-»)». Уровней нет — один кусок текста. */
export function textLevelParts(text: string): SheetTextPart[] {
  const parts: SheetTextPart[] = [];
  let at = 0;
  for (const m of text.matchAll(TEXT_LEVEL)) {
    const start = m.index ?? 0;
    if (start > at) parts.push({ kind: 'text', text: text.slice(at, start) });
    parts.push({ kind: 'level', label: `V${m[2] ?? m[3]}`, prefix: m[1] ?? '' });
    at = start + m[0].length;
  }
  if (at < text.length) parts.push({ kind: 'text', text: text.slice(at) });
  return parts;
}

/**
 * Текст строки для всех видов: уровни чипами; чипы поля `intensity` — только если в тексте уровня
 * нет (без дублей: «הכל 2V» + поле «2V» → один [V2]). Направление — по буквам, оставшимся после
 * уровней; букв нет («1-4V, 1-1V») — направление листа, иначе в ивритском листе такая строка одна
 * развернулась бы слева направо.
 */
export function richText(text: string, intensityChips: string[] | null, sheet: SheetDir): SheetRichText {
  const parts = textLevelParts(text);
  const hasLevel = parts.some((p) => p.kind === 'level');
  const letters = parts.map((p) => (p.kind === 'text' ? p.text : '')).join('');
  const dir: SheetDir = HEBREW_OR_ARABIC.test(letters) ? 'rtl' : LATIN.test(letters) ? 'ltr' : sheet;
  return { parts, dir, chips: hasLevel ? null : intensityChips };
}

function sheetLine(line: WorkoutLine, levelKey: string | null, dir: SheetDir): SheetLine {
  const own = levelKey != null ? line.per_level?.[levelKey] : undefined;
  const text = (line.text ?? '').trim();
  const intensity = (line.intensity ?? '').trim();
  const keys = intensityKeys(intensity);
  const chips = keys ? keys.map(chipLabel) : null;
  const time = sendOffLabel(own?.send_off);
  return {
    head: repsDistanceLabel(own?.reps ?? line.reps, line.distance),
    text,
    textDir: lineDir(text),
    chips,
    intensityText: !keys && intensity ? intensity : null,
    cell: own ? cellLabel({ ...own, send_off: time.label || null }) : '',
    time: time.label,
    timeKind: time.kind,
    rich: richText(text, chips, dir),
  };
}

const hasContent = (l: SheetLine) => !!(l.head || l.text || l.chips || l.intensityText || l.cell);

/**
 * Кубы листа по порядку выполнения. Сеты, которых уровень не плывёт, пропускаются, а номера
 * остаются тренерскими (дырка в нумерации, как у «Set N» в `workoutForLevel`).
 */
function sheetCubes(source: WorkoutPrintSource, dir: SheetDir): SheetCube[] {
  const { workout, levelId, setNumbers } = source;
  const levelKey = levelId == null ? null : String(levelId);
  const cubes: SheetCube[] = [];
  workout.sets.forEach((set, i) => {
    if (!setSwumBy(set, levelId)) return;
    const no = setNumbers?.[i] ?? i + 1;
    const meters = setMeters(set, levelId);
    const title = set.title?.trim() || null;
    // «Set N» подставляет версия уровня вместо пустого названия (`workoutForLevel`, серверный `ForLevel`).
    const ownTitle = title && title !== `Set ${no}` ? title : null;
    const main = isMainSet(set);
    const { word, rest } = mainTitleParts(ownTitle, main);
    cubes.push({
      no,
      title: title || `Set ${no}`,
      ownTitle,
      mainWord: word,
      titleRest: rest,
      repeat: set.repeat,
      meters,
      metersLabel: meters > 0 ? formatMeters(meters) : null,
      main,
      lines: set.lines.map((line) => sheetLine(line, levelKey, dir)).filter(hasContent),
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
  const dir = sheetDir(source.workout);
  const cubes = sheetCubes(source, dir);
  const totalMeters = levelMeters(source.workout, source.levelId);
  const cellChars = Math.max(0, ...cubes.flatMap((c) => c.lines.map((l) => l.cell.length)));
  return {
    dir,
    header: { groupName: source.groupName, level: source.level, kmLabel: formatKm(totalMeters) },
    note: source.workout.note?.trim() || null,
    cubes,
    cellChars,
    totalMeters,
    mainLayout: splitMain(cubes),
  };
}
