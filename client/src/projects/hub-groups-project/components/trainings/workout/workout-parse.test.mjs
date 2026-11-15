// Юнит-тесты разбора вставленного листа (CF7, docs/plans/trainings-coach-flow-plan.md §5.6):
// три листа из задачи `docs/tasks/trainings-coach-flow-cf7.md`. Запуск: `npm --prefix client test`.

import './ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';

const { parseWorkoutText, checkCount, checkSummary } = await import('./workout-parse.ts');
const { levelMeters } = await import('./workout-totals.ts');

const LEVELS = [11, 12, 13, 14]; // сверху вниз

const SHEET1 = `200 חימום חופשי

150X4 שנורקל
1- 50 רק ימין + 50 רק שמאל + 50 קאץ'-אפ
2- 25 כלב + 25 כלב שתי ידיים + 25 חת' מלא (כפול 2!)
3- 25X6 מנוחה 5' כמה שפחות תנועות בבריכה
4- כפות הפוכות חתירה מלא

סט 1 = כפות ופולי
25X4 חתירה בלי נשימה או מינימום 35 | 40 | 45
3 או 100X4 חתירה 2-3V 1:40 | 1:50 | 2:00 | 2:10
50X4 25 גב + 25 חתירה 2V 1:00 | 1:10 | 1:20
50 קל

סט 2 = כפות וזומרס
25X4 חזה עם רגליים פרפר 25 | 30 | 35
3 או 100X4 חתירה 2-3V 1:30 | 1:40 | 1:50 | 2:00
50X4 חתירה הלוך "ברקס" ברגליים חזור רגיל
50 קל

2.1

סט 3 = רק רמה 4+5 בלי ציוד
25X4 מעורב יציאה 35
100X2 חתירה 1V 1:50 | 2:00
50X4 חתירה גרירה עם פולי מחליפים כל 25

2.8`;

const SHEET2 = `חימום:
200 מלא סגנון חופשי
150 פולי בכל 50 – 15 מ' תחושת מים + 35 מלא
100 כפות הפוכות 25 חתירה 25 גב דגש על catch
50 רגליים סגנון בחירה
מנוחה 10' בין לבין

זומרס:
75 חתירה\\גב\\חתירה לפי 25 מנוחה 10'
50 רגליים גב 3V מנוחה 20'
25 צלילה רגליים פרפר 4V

שני סטים, 1 בלי ציוד, 1 כפות
100X3\\2 חתירה 2V יציאה 1:50-2:00-2:10 או 2:30-40
50 סגנון 3V יציאה 1:10-1:20-1:30
100X2\\1 חתירה 2V יציאה 1:50-2:00-2:10 או 2:30-40
50X2 אחד 3V, השני 4V סגנון יציאה 1:10-1:20-1:30
100 חתירה 2V יציאה 1:50-2:00-2:10 או 2:30-40
50X2 אחד 3V, השני 4V סגנון יציאה 1:10-1:20-1:30
25X2 5V יציאה 40' סגנון

2.2 – 2.6`;

const SHEET3 = `100 חתירה
100 גב\\חזה
200 חתירה פולי נשימה לפי 50: 4 – 2 – 5 – 3
100X2 פולי + כפות כמה שפחות תנועות 1:40 | 1:50 | 2:00 | 2:10
300 זומרס רגליים (100 פרפר+100 גב+100חתירה)
100X3 זומרס חתירה כמה שפחות תנועות 1:40 | 1:50 | 2:00 | 2:10
400 חתירה החלפת מוביל כל 50 מ'
100X4 חתירה 3V 1:40 | 1:50 | 2:00 | 2:10
2.0
4X(75 חתירה 2V '15 + 25 ספרינט סגנון + 25 קל)
2.5
100X5 זומרס 1פרפר | 1חתירה | 1גב | 1חזה רג' פרפר '20
3.0`;

/** Весь текст разбора одной строкой: названия, тексты, интенсивность, времена. */
function allText(w) {
  return w.sets.flatMap((s) => [s.title ?? '', ...s.lines.flatMap((l) => [
    l.text ?? '', l.intensity ?? '', String(l.distance ?? ''), String(l.reps),
    ...Object.values(l.per_level ?? {}).map((c) => c.send_off ?? ''),
  ])]).join(' ');
}

/** Каждое слово листа (буквы) есть в разборе — текст не потерян. «2V» уходит в интенсивность. */
function assertNoLostWords(source, parsed) {
  const haystack = allText(parsed.workout);
  const words = source.match(/\p{L}+/gu) ?? [];
  for (const word of words) {
    // «V» уходит в интенсивность, «X» — в повторы, «יציאה» («выход») — это сама колонка времён.
    if (word === 'V' || word === 'X' || word === 'יציאה') continue;
    assert.ok(haystack.includes(word), `потеряно слово «${word}»`);
  }
}

test('лист 1: сеты и строки, без потери текста', () => {
  const p = parseWorkoutText(SHEET1, LEVELS);
  assert.deepEqual(p.workout.sets.map((s) => s.title), [null, null, 'סט 1 = כפות ופולי', 'סט 2 = כפות וזומרס', 'סט 3 = רק רמה 4+5 בלי ציוד']);
  assert.deepEqual(p.workout.sets.map((s) => s.lines.length), [1, 5, 4, 4, 3]);
  // Пункты «1- …» — текстом, без дистанции.
  assert.equal(p.workout.sets[1].lines[1].distance, null);
  assert.deepEqual([p.workout.sets[1].lines[0].reps, p.workout.sets[1].lines[0].distance], [4, 150]);
  assertNoLostWords(SHEET1, p);
});

test('лист 1: «35 | 40 | 45» при четырёх уровнях — четвёртое :50 с подсветкой', () => {
  const p = parseWorkoutText(SHEET1, LEVELS);
  const line = p.workout.sets[2].lines[0];
  assert.deepEqual(LEVELS.map((id) => line.per_level[id].send_off), [':35', ':40', ':45', ':50']);
  assert.ok(p.filledCells.includes('2:0:14'));
  assert.ok(p.lineMarks['2:0'].includes('step'));
  // «3 או 100X4» — два варианта повторов: подсветка, 4 по умолчанию.
  const either = p.workout.sets[2].lines[1];
  assert.deepEqual([either.reps, either.distance, either.intensity], [4, 100, '2-3V']);
  assert.ok(p.lineMarks['2:1'].includes('reps'));
  // «רק רמה 4+5» не трактуется — подсветка сета.
  assert.ok(p.setMarks[4].includes('levels'));
  assert.equal(p.workout.sets[4].level_ids, null);
  // «2.8» сходится у верхнего уровня; «2.1» — только после ручных 3× (подсветка).
  assert.equal(levelMeters(p.workout, 11), 2800);
  assert.deepEqual(p.totals.map((t) => t.ok), [false, true]);
  assert.ok(checkCount(p) > 0);
  assert.match(checkSummary(p), /places to check/);
});

test('лист 2: «1:50-2:00-2:10» после «יציאה» — по трём уровням, «או 2:30-40» подсвечено', () => {
  const p = parseWorkoutText(SHEET2, LEVELS);
  assert.deepEqual(p.workout.sets.map((s) => s.title), ['חימום', 'זומרס', 'שני סטים, 1 בלי ציוד, 1 כפות']);
  const line = p.workout.sets[2].lines[0];
  assert.deepEqual(LEVELS.slice(0, 3).map((id) => line.per_level[id].send_off), ['1:50', '2:00', '2:10']);
  assert.equal(line.per_level[14].send_off, '2:20');
  assert.deepEqual([line.reps, line.distance, line.intensity], [3, 100, '2V']);
  assert.ok(p.lineMarks['2:0'].includes('alternative'));
  assert.ok(p.lineMarks['2:0'].includes('reps'));
  assert.match(line.text, /או 2:30-40/);
  // «40'» после «יציאה» — :40 всем уровням.
  const last = p.workout.sets[2].lines.at(-1);
  assert.deepEqual(LEVELS.map((id) => last.per_level[id].send_off), [':40', ':40', ':40', ':40']);
  assertNoLostWords(SHEET2, p);
});

test('лист 3: три сета, суммы 2000 / 500 / 500 сходятся с 2.0 / 2.5 / 3.0', () => {
  const p = parseWorkoutText(SHEET3, LEVELS);
  assert.equal(p.workout.sets.length, 3);
  assert.ok(p.workout.sets.every((s) => s.title == null), '«2.0» не стал названием сета');
  const meters = p.workout.sets.map((s) => levelMeters({ sets: [s] }, 11));
  assert.deepEqual(meters, [2000, 500, 500]);
  assert.deepEqual(p.totals.map((t) => t.ok), [true, true, true]);
  // «4X(…)» — 4 × 125 с подсветкой, текст скобки сохранён.
  const parts = p.workout.sets[1].lines[0];
  assert.deepEqual([parts.reps, parts.distance], [4, 125]);
  assert.ok(p.lineMarks['1:0'].includes('parts'));
  assert.match(parts.text, /75 חתירה 2V '15/);
  // «1פרפר | 1חתירה …» — текст, не времена; «'20» — отдых всем уровням.
  const z = p.workout.sets[2].lines[0];
  assert.match(z.text, /1פרפר \| 1חתירה \| 1גב \| 1חזה/);
  assert.deepEqual(LEVELS.map((id) => z.per_level[id].send_off), ['rest :20', 'rest :20', 'rest :20', 'rest :20']);
  // «200 … לפי 50: 4 – 2 – 5 – 3» — не времена и не название.
  assert.equal(p.workout.sets[0].lines[2].per_level, null);
  assertNoLostWords(SHEET3, p);
});

test('без уровней времена остаются текстом строки', () => {
  const p = parseWorkoutText('100X4 free 1:40 | 1:50', []);
  const line = p.workout.sets[0].lines[0];
  assert.equal(line.per_level, null);
  assert.match(line.text, /1:40 \| 1:50/);
});
