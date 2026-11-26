// Юнит-тесты чистых функций workout (CF6, docs/plans/trainings-coach-flow-plan.md §5.5).
// Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { fillByStep, formatSendOff, parseSendOff } from './workout-time.ts';
import { formatKm, kmRange, levelMeters } from './workout-totals.ts';
import { workoutForLevel } from './workout-for-level.ts';
import { cellLabel, intensityKeys, levelAbbr, lineDir, lineSummary, parseCellLabel, parseRepsDistance, setAudience } from './workout-format.ts';

const ADV = 1, INT = 2, DEV = 3, BEG = 4;

const line = (reps, distance, text, times = [], intensity = null) => ({
  reps, distance, text, intensity,
  per_level: Object.fromEntries(times.map((t, i) => [String(i + 1), parseCellLabel(t)]).filter(([, c]) => c.send_off || c.reps)),
});

/** Первый лист тренера (задача CF6, «Примеры»), введённый руками: уровни 1–4 сверху вниз. */
const sheet1 = {
  note: null,
  sets: [
    { title: 'Warm-up', repeat: 1, level_ids: null, lines: [line(1, 200, 'חימום חופשי')] },
    {
      title: 'Snorkel', repeat: 1, level_ids: null, lines: [
        line(4, 150, 'שנורקל'),
        line(1, null, "1- 50 רק ימין + 50 רק שמאל + 50 קאץ'-אפ"),
        line(1, null, '2- 25 כלב + 25 כלב שתי ידיים'),
      ],
    },
    {
      title: 'סט 1 = כפות ופולי', repeat: 1, level_ids: null, lines: [
        line(4, 25, 'חתירה בלי נשימה', [':35', ':40', ':45', ':50']),
        line(4, 100, 'חתירה', ['1:40', '1:50', '2:00', '3× 2:10'], '2-3V'),
        line(4, 50, '25 גב + 25 חתירה', ['1:00', '1:10', '1:20'], '2V'),
        line(1, 50, 'קל'),
      ],
    },
    {
      title: 'סט 2 = כפות וזומרס', repeat: 1, level_ids: null, lines: [
        line(4, 25, 'חזה עם רגליים פרפר', ['25', '30', '35']),
        line(4, 100, 'חתירה', ['1:30', '1:40', '1:50', '3× 2:00'], '2-3V'),
        line(4, 50, 'חתירה הלוך "ברקס"'),
        line(1, 50, 'קל'),
      ],
    },
    {
      title: 'סט 3 = רק רמה 4+5', repeat: 1, level_ids: [ADV], lines: [
        line(4, 25, 'מעורב', ['35']),
        line(2, 100, 'חתירה', ['1:50'], '1V'),
        line(4, 50, 'חתירה גרירה'),
      ],
    },
  ],
};

test('лист 1: 2.8 км у верхнего уровня, 2.1 км у уровня с 3×100', () => {
  assert.equal(levelMeters(sheet1, ADV), 2800);
  assert.equal(formatKm(levelMeters(sheet1, ADV)), '2.8 km');
  assert.equal(levelMeters(sheet1, BEG), 2100);
  assert.equal(formatKm(levelMeters(sheet1, BEG)), '2.1 km');
  assert.equal(levelMeters(sheet1, INT), 2300);
  assert.equal(kmRange([ADV, INT, DEV, BEG].map((id) => levelMeters(sheet1, id))), '2.1–2.8 km');
});

test('повтор сета умножает его метры', () => {
  const w = { sets: [{ repeat: 2, level_ids: null, lines: [line(3, 100, 'free')] }] };
  assert.equal(levelMeters(w, ADV), 600);
});

test('дорожка без уровня — только сеты для всех', () => {
  assert.equal(levelMeters(sheet1, null), 2300); // 200 + 600 + 750 + 750, без своих повторов
});

test('версия уровня: Intermediate видит свои времена и не видит сет «только Advanced»', () => {
  const mine = workoutForLevel(sheet1, INT);
  assert.equal(mine.sets.length, 4);
  assert.ok(!mine.sets.some((s) => s.title.includes('סט 3')));
  const row = mine.sets[2].lines[1];
  assert.deepEqual(Object.keys(row.per_level), ['2']);
  assert.equal(row.per_level['2'].send_off, '1:50');
  assert.equal(workoutForLevel(sheet1, ADV).sets.length, 5);
  assert.equal(workoutForLevel(null, ADV), null);
});

test('время выхода: разбор и запись', () => {
  assert.equal(parseSendOff('1:40'), 100);
  assert.equal(parseSendOff(':35'), 35);
  assert.equal(parseSendOff('35'), 35);
  assert.equal(parseSendOff('2:30-40'), null);
  assert.equal(parseSendOff('rest :20'), null);
  assert.equal(formatSendOff(50), ':50');
  assert.equal(formatSendOff(130), '2:10');
});

test('по шагу: «35 | 40 | 45» при четырёх уровнях — четвёртое :50 с пометкой', () => {
  assert.deepEqual(fillByStep(['35', '40', '45', '']), { values: ['35', '40', '45', ':50'], filled: [false, false, false, true] });
  assert.deepEqual(fillByStep(['1:40', '1:50', '', '']).values, ['1:40', '1:50', '2:00', '2:10']);
  // Одно время — шага нет.
  assert.deepEqual(fillByStep(['1:40', '', '']).filled, [false, false, false]);
  // Уже введённое дальше не трогается.
  assert.deepEqual(fillByStep(['1:00', '1:10', '', '2:00', '']).values, ['1:00', '1:10', '1:20', '2:00', '2:10']);
});

test('поле «повторы × дистанция» и ячейка уровня', () => {
  assert.deepEqual(parseRepsDistance('4x100'), { reps: 4, distance: 100 });
  assert.deepEqual(parseRepsDistance('4 × 25'), { reps: 4, distance: 25 });
  assert.deepEqual(parseRepsDistance('100X4'), { reps: 4, distance: 100 });
  assert.deepEqual(parseRepsDistance('200'), { reps: 1, distance: 200 });
  assert.equal(parseRepsDistance('abc'), null);
  assert.deepEqual(parseCellLabel('3× 2:10'), { reps: 3, send_off: '2:10' });
  assert.deepEqual(parseCellLabel(' 1:40 '), { reps: null, send_off: '1:40' });
  assert.equal(cellLabel({ reps: 3, send_off: '2:10' }), '3× 2:10');
  assert.equal(lineSummary({ reps: 4, distance: 100, text: 'free', intensity: '2-3V' }), '4×100 free · 2-3V');
});

test('подписи уровней и сетов', () => {
  assert.equal(levelAbbr('Advanced'), 'Adv');
  assert.equal(levelAbbr('Fast'), 'Fast');
  const levels = [{ id: ADV, name: 'Advanced' }, { id: INT, name: 'Intermediate' }, { id: DEV, name: 'Developing' }];
  assert.equal(setAudience({ level_ids: null }, levels), 'Everyone');
  assert.equal(setAudience({ level_ids: [ADV] }, levels), 'Advanced only');
  assert.equal(setAudience({ level_ids: [ADV, INT] }, levels), 'Adv, Int');
});

test('направление строки: иврит — rtl, латиница и пусто — ltr', () => {
  assert.equal(lineDir("(75 חתירה 2V '15 + 25 ספרינט סגנון + 25 קל)"), 'rtl');
  assert.equal(lineDir('free 2-3V'), 'ltr');
  assert.equal(lineDir(null), 'ltr');
  assert.equal(lineDir(''), 'ltr');
});

test('версия уровня: сет без названия — «Set N» по номеру у тренера, пропуск не сдвигает номера', () => {
  const plain = (levelIds) => ({ title: null, repeat: 1, level_ids: levelIds, lines: [line(1, 100, 'x')] });
  const w = { note: null, sets: [plain(null), plain([ADV]), plain(null)] };
  assert.deepEqual(workoutForLevel(w, INT).sets.map((s) => s.title), ['Set 1', 'Set 3']);
  assert.deepEqual(workoutForLevel(w, ADV).sets.map((s) => s.title), ['Set 1', 'Set 2', 'Set 3']);
  assert.equal(workoutForLevel(sheet1, ADV).sets[0].title, 'Warm-up', 'своё название не трогаем');
});

test('версия уровня сохраняет main: у плывущих главных метка есть, у уровня без главного — нет', () => {
  const set = (levelIds, main) => ({ title: null, repeat: 1, level_ids: levelIds, ...(main ? { main: true } : {}), lines: [line(1, 100, 'x')] });
  const w = { note: null, sets: [set(null, true), set(null, false), set([ADV], true)] };
  assert.deepEqual(workoutForLevel(w, ADV).sets.map((s) => !!s.main), [true, false, true]);
  assert.deepEqual(workoutForLevel(w, INT).sets.map((s) => !!s.main), [true, false]);
  const onlyAdvMain = { note: null, sets: [set(null, false), set([ADV], true)] };
  assert.ok(workoutForLevel(onlyAdvMain, INT).sets.every((s) => !s.main), 'уровень без главного сета видит версию без метки');
});

test('нагрузка → чипы шкалы V0–V5', () => {
  assert.deepEqual(intensityKeys('3V'), ['v3']);
  assert.deepEqual(intensityKeys('V4'), ['v4']);
  assert.deepEqual(intensityKeys('2-3V'), ['v2', 'v3']);
  assert.deepEqual(intensityKeys('Sprint'), ['sprint']);
  assert.equal(intensityKeys('easy'), null);
  assert.equal(intensityKeys(null), null);
  assert.equal(intensityKeys('3-2V'), null);
});
