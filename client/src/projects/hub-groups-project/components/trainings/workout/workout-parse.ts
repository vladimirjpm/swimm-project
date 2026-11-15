import type { Workout, WorkoutCell, WorkoutLine, WorkoutSet } from '../../../types';
import { fillByStep, formatSendOff, parseSendOff } from './workout-time';
import { levelMeters } from './workout-totals';

/**
 * Разбор вставленного листа тренера (docs/plans/trainings-coach-flow-plan.md, §5.6, CF7) — на
 * клиенте, текст на сервер не уходит. Результат всегда можно поправить в таблице CF6; чего разбор не
 * понял, остаётся текстом строки. Тесты — `workout-parse.test.mjs` (три листа из задачи CF7).
 *
 * Правила:
 * - пустая строка разделяет сеты; первая строка блока без дистанции, строка с «=» или с «:» в конце —
 *   название сета; строка из одного числа с точкой («2.1», «2.2 – 2.6») — накопленный объём в км:
 *   закрывает сет (на листе из Word черта между сетами пропадает), сумма сверяется с ней;
 * - дистанция и повторы — «100X4» (на листе дистанция первая; дистанция — кратное 25; оба кратны —
 *   подсветка), «3 או 100X4» / «100X3\2» — два варианта повторов (подсветка, ставит тренер),
 *   «4X(75 … + 25 … + 25 …)» — один повтор из частей, дистанция — сумма частей (подсветка);
 * - строка «1- …» — пункт пояснения к строке выше, текстом;
 * - времена по уровням сверху вниз: « | » (каждый элемент — время) или «יציאה 1:50-2:00-2:10»;
 *   одно значение — всем; меньше, чем уровней, — остальные по шагу (подсветка);
 * - «2V», «2-3V» — интенсивность; «'15», «'20» без «יציאה» — отдых, одно значение на все уровни;
 * - не трактуются, только подсветка: «או 2:30-40» (альтернатива), «רק רמה 4+5» (сет части уровней).
 */

/** Почему место подсвечено (одна строка о числе мест над результатом). */
export type ParseReason =
  | 'step' // времена достроены по шагу
  | 'reps' // два варианта числа повторов
  | 'distance' // дистанцию не отличить от повторов
  | 'parts' // повтор из частей «4X(…)»
  | 'alternative' // «או 2:30-40»
  | 'times' // времён больше, чем уровней
  | 'levels' // сет только для части уровней
  | 'total'; // сумма не сходится с отметкой листа

export interface ParsedWorkout {
  workout: Workout;
  /** Ключ — индекс сета. */
  setMarks: Record<number, ParseReason[]>;
  /** Ключ — «сет:строка». */
  lineMarks: Record<string, ParseReason[]>;
  /** Достроенные по шагу ячейки: «сет:строка:idУровня». */
  filledCells: string[];
  /** Отметки объёма листа: после какого сета, что написано, что насчитали (м, по уровням). */
  totals: { afterSet: number; label: string; expected: [number, number]; ok: boolean }[];
}

const LEVEL_ONLY_RX = /רק\s+רמ(?:ה|ות)|only/i;
const KM_RX = /^(\d{1,2}\.\d)(?:\s*[–—-]\s*(\d{1,2}\.\d))?$/;
const NUMBERED_RX = /^\d{1,2}\s*-\s+\S/;
const INTENSITY_RX = /(?<![\w-])(\d(?:-\d)?)\s?V(?![\w])/g;
/** Элемент списка времён: «1:40», «:35», «35», «'20», «20'». */
const TIME_ITEM = String.raw`(?:\d{0,2}:[0-5]\d|'?\d{1,3}'?)`;
const PIPE_RX = new RegExp(String.raw`(${TIME_ITEM}(?:\s*\|\s*${TIME_ITEM})+)`);
const SEND_OFF_RX = new RegExp(String.raw`יציאה\s+(${TIME_ITEM}(?:\s*-\s*\d{0,2}:[0-5]\d)*)`);
const ALT_RX = /או\s+\d{1,2}:[0-5]\d\s*-\s*\d{1,2}/;
const REST_RX = /(?:^|\s)'(\d{1,3})(?=\s|$)/;

/** «'20», «20'», «35», «1:40» → секунды. */
function itemSeconds(raw: string): number | null {
  return parseSendOff(raw.replace(/'/g, ''));
}

const tidy = (s: string) => s.replace(/\s{2,}/g, ' ').replace(/^[\s,:–-]+|[\s,]+$/g, '').trim();

interface LineParse {
  line: WorkoutLine;
  reasons: ParseReason[];
  /** Времена по уровням сверху вниз (как на листе) — раскладываются по уровням позже. */
  times: string[];
  rest: string | null;
}

/** Повторы × дистанция в начале строки. */
function leadingReps(text: string): { reps: number; distance: number | null; rest: string; reasons: ParseReason[] } {
  // «4X(75 … + 25 … + 25 …)» — один повтор из частей.
  const parts = /^(\d{1,2})\s*[Xx×]\s*\((.*)\)\s*$/.exec(text);
  if (parts) {
    const sum = parts[2].split('+')
      .map((p) => /^\s*(\d{2,4})/.exec(p)?.[1])
      .map(Number)
      .filter((n) => n > 0 && n % 25 === 0)
      .reduce((a, b) => a + b, 0);
    return { reps: Number(parts[1]), distance: sum || null, rest: `(${parts[2].trim()})`, reasons: ['parts'] };
  }
  // «3 או 100X4» — повторы «3 или 4».
  const either = /^(\d{1,2})\s+או\s+(\d{2,4})\s*[Xx×]\s*(\d{1,2})(?!\d)/.exec(text);
  if (either) {
    return { reps: Number(either[3]), distance: Number(either[2]), rest: text.slice(either[0].length), reasons: ['reps'] };
  }
  // «100X4», «100X3\2», «4x100».
  const pair = /^(\d{1,4})\s*[Xx×]\s*(\d{1,4})(?:\s*[\\/]\s*(\d{1,2}))?(?!\d)/.exec(text);
  if (pair) {
    const a = Number(pair[1]);
    const b = Number(pair[2]);
    const reasons: ParseReason[] = pair[3] ? ['reps'] : [];
    let distance = a;
    let reps = b;
    if (a % 25 !== 0 && b % 25 === 0) { distance = b; reps = a; }
    else if (a % 25 === 0 && b % 25 === 0) reasons.push('distance');
    return { reps, distance, rest: text.slice(pair[0].length), reasons };
  }
  const single = /^(\d{2,4})(?![\d.:])/.exec(text);
  if (single && Number(single[1]) % 25 === 0) {
    return { reps: 1, distance: Number(single[1]), rest: text.slice(single[0].length), reasons: [] };
  }
  return { reps: 1, distance: null, rest: text, reasons: [] };
}

function parseLine(raw: string): LineParse {
  const text = raw.trim();
  if (NUMBERED_RX.test(text)) {
    return { line: { reps: 1, distance: null, text, intensity: null, per_level: null }, reasons: [], times: [], rest: null };
  }

  const lead = leadingReps(text);
  let rest = lead.rest;
  const reasons = [...lead.reasons];
  let times: string[] = [];
  let restTime: string | null = null;

  // Внутри «4X(…)» ничего не вынимаем: части и отдых в них — текст строки.
  if (!reasons.includes('parts')) {
    if (ALT_RX.test(rest)) reasons.push('alternative');

    const pipe = PIPE_RX.exec(rest);
    const items = pipe ? pipe[1].split('|').map((s) => s.trim()) : [];
    if (pipe && items.every((i) => itemSeconds(i) != null)) {
      times = items.map((i) => formatSendOff(itemSeconds(i)!));
      rest = rest.replace(pipe[1], ' ');
    } else {
      const sendOff = SEND_OFF_RX.exec(rest);
      const list = sendOff ? sendOff[1].split('-').map((s) => s.trim()) : [];
      if (sendOff && list.every((i) => itemSeconds(i) != null)) {
        times = list.map((i) => formatSendOff(itemSeconds(i)!));
        rest = rest.replace(sendOff[0], ' ');
      } else {
        const restMatch = REST_RX.exec(rest);
        if (restMatch) {
          restTime = `rest ${formatSendOff(Number(restMatch[1]))}`;
          rest = rest.replace(restMatch[0], ' ');
        }
      }
    }
  }

  // Интенсивность — когда она одна; «אחד 3V, השני 4V» остаётся текстом.
  let intensity: string | null = null;
  if (!reasons.includes('parts')) {
    const found = [...rest.matchAll(INTENSITY_RX)];
    if (found.length === 1) {
      intensity = `${found[0][1]}V`;
      rest = rest.replace(found[0][0], ' ');
    }
  }

  return {
    line: { reps: lead.reps, distance: lead.distance, text: tidy(rest) || null, intensity, per_level: null },
    reasons,
    times,
    rest: restTime,
  };
}

/** Есть ли в строке дистанция (иначе — кандидат в название сета). */
const hasDistance = (raw: string) => {
  const t = raw.trim();
  return !NUMBERED_RX.test(t) && leadingReps(t).distance != null;
};

const isTitle = (raw: string, firstInBlock: boolean) => {
  const t = raw.trim();
  if (NUMBERED_RX.test(t)) return false;
  if (t.includes('=') || /:\s*$/.test(t)) return true;
  return firstInBlock && !hasDistance(t);
};

/**
 * Разобрать лист. `levelIds` — уровни группы сверху вниз (по рангу): времена листа раскладываются
 * в этом порядке. Уровней нет — времена остаются текстом строки.
 */
export function parseWorkoutText(source: string, levelIds: number[]): ParsedWorkout {
  const sets: WorkoutSet[] = [];
  const setMarks: Record<number, ParseReason[]> = {};
  const lineMarks: Record<string, ParseReason[]> = {};
  const filledCells: string[] = [];
  const totals: ParsedWorkout['totals'] = [];

  let current: WorkoutSet | null = null;
  let firstInBlock = true;

  const mark = (bucket: Record<string | number, ParseReason[]>, key: string | number, reason: ParseReason) => {
    const list = bucket[key] ?? (bucket[key] = []);
    if (!list.includes(reason)) list.push(reason);
  };
  const close = () => { current = null; firstInBlock = true; };
  const open = (title: string | null) => {
    current = { title, repeat: 1, level_ids: null, lines: [] };
    sets.push(current);
    if (title && LEVEL_ONLY_RX.test(title)) mark(setMarks, sets.length - 1, 'levels');
    return current;
  };

  for (const rawLine of source.replace(/\r\n?/g, '\n').split('\n')) {
    const text = rawLine.replace(/[‎‏]/g, '').trim();
    if (text === '' || /^[-–—_=*]{3,}$/.test(text)) { close(); continue; }

    const km = KM_RX.exec(text);
    if (km) {
      const lo = Math.round(Number(km[1]) * 1000);
      const hi = Math.round(Number(km[2] ?? km[1]) * 1000);
      totals.push({ afterSet: sets.length - 1, label: text, expected: [lo, hi], ok: false });
      close();
      continue;
    }

    if (isTitle(text, firstInBlock || current == null)) {
      open(text.replace(/:\s*$/, ''));
      firstInBlock = false;
      continue;
    }

    const set: WorkoutSet = current ?? open(null);
    firstInBlock = false;
    const s = sets.length - 1;
    const l = set.lines.length;
    const parsed = parseLine(text);
    const line = parsed.line;
    parsed.reasons.forEach((r) => mark(lineMarks, `${s}:${l}`, r));

    if (levelIds.length > 0 && (parsed.times.length > 0 || parsed.rest)) {
      const per: Record<string, WorkoutCell> = {};
      if (parsed.rest) {
        levelIds.forEach((id) => { per[String(id)] = { send_off: parsed.rest }; });
      } else if (parsed.times.length === 1) {
        levelIds.forEach((id) => { per[String(id)] = { send_off: parsed.times[0] }; });
      } else {
        const given = parsed.times.slice(0, levelIds.length);
        if (parsed.times.length > levelIds.length) {
          mark(lineMarks, `${s}:${l}`, 'times');
          line.text = tidy(`${line.text ?? ''} ${parsed.times.slice(levelIds.length).join(' | ')}`);
        }
        const { values, filled } = fillByStep([...given, ...levelIds.slice(given.length).map(() => '')]);
        levelIds.forEach((id, i) => {
          if (values[i]) per[String(id)] = { send_off: values[i] };
          if (filled[i]) filledCells.push(`${s}:${l}:${id}`);
        });
        if (filled.some(Boolean)) mark(lineMarks, `${s}:${l}`, 'step');
      }
      line.per_level = Object.keys(per).length > 0 ? per : null;
    } else if (parsed.times.length > 0 || parsed.rest) {
      line.text = tidy(`${line.text ?? ''} ${parsed.rest ?? parsed.times.join(' | ')}`);
    }

    set.lines.push(line);
  }

  const workout: Workout = { note: null, sets };

  // Отметки объёма: накопленные метры до этого сета — у какого-нибудь уровня (или у всех без уровня).
  const ids: (number | null)[] = levelIds.length > 0 ? levelIds : [null];
  for (const t of totals) {
    const upTo: Workout = { sets: sets.slice(0, t.afterSet + 1) };
    const sums = ids.map((id) => levelMeters(upTo, id));
    const near = (m: number) => sums.some((x) => Math.abs(x - m) < 50);
    t.ok = near(t.expected[0]) && near(t.expected[1]);
    if (!t.ok && t.afterSet >= 0) mark(setMarks, t.afterSet, 'total');
  }

  return { workout, setMarks, lineMarks, filledCells, totals };
}

/** Сколько мест проверить: подсвеченные строки и сеты, по разу. */
export function checkCount(p: ParsedWorkout): number {
  return Object.keys(p.lineMarks).length + Object.keys(p.setMarks).length;
}

const REASON_TEXT: Record<ParseReason, [string, string]> = {
  step: ['line continued by step', 'lines continued by step'],
  reps: ['line with two rep counts', 'lines with two rep counts'],
  distance: ['line where distance and reps look alike', 'lines where distance and reps look alike'],
  parts: ['line made of parts', 'lines made of parts'],
  alternative: ['line with an alternative time', 'lines with an alternative time'],
  times: ['line with more times than levels', 'lines with more times than levels'],
  levels: ['set for some levels only', 'sets for some levels only'],
  total: ['set where the sheet total differs', 'sets where the sheet total differs'],
};

/** Одна строка над результатом: «5 places to check: 2 lines continued by step, 1 set for some levels only». */
export function checkSummary(p: ParsedWorkout): string | null {
  const total = checkCount(p);
  if (total === 0) return null;
  const counts = new Map<ParseReason, number>();
  [...Object.values(p.lineMarks), ...Object.values(p.setMarks)].flat()
    .forEach((r) => counts.set(r, (counts.get(r) ?? 0) + 1));
  const parts = [...counts].map(([r, n]) => `${n} ${REASON_TEXT[r][n === 1 ? 0 : 1]}`);
  return `${total} ${total === 1 ? 'place' : 'places'} to check: ${parts.join(', ')}.`;
}

/** Человеческое пояснение одной метки — подсказка `title` у подсвеченного места. */
export function reasonHint(reasons: ParseReason[]): string {
  return `Check: ${reasons.map((r) => REASON_TEXT[r][0]).join('; ')}`;
}
