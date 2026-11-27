// Юнит-тесты листов «листа кубов» (виды numbers / numbers-two-sides, хендофф 27.11 вторая версия,
// §3, §6, §8). Запуск: `npm --prefix client test`.

import '../../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const { splitTwoSides, numbersPages, legendParts, numbersLineKey } = await import('./numbers-pages.ts');
const { toSheetModel } = await import('../print-sheet-model.ts');
const { workoutForLevel } = await import('../../workout-for-level.ts');

const INT = 37;
const sheet = (name) => JSON.parse(readFileSync(new URL(`../../../../../../../../../docs/workouts/sheets/${name}.json`, import.meta.url), 'utf8'));
const source = (workout, levelId = INT) => ({ workout, levelId, level: { rank: 2, name: 'Intermediate' }, groupName: 'G' });
const plainSet = (title, main = false, extra = {}) => ({
  title, repeat: 1, level_ids: null,
  lines: [{ reps: 1, distance: 100, text: 'free', intensity: null, per_level: null }],
  ...(main ? { main: true } : {}), ...extra,
});
const model = (sets, levelId = null) => toSheetModel(source({ sets }, levelId));
/** Как `available` вида numbers-two-sides (`twoSidesAvailable`): делится ли версия уровня. */
const available = (sets, levelId = null) => splitTwoSides(model(sets, levelId).cubes) != null;

test('Two sides available: нет главного → false; главный первый → false; главный второй → true', () => {
  assert.equal(available([plainSet('a'), plainSet('b')]), false);
  assert.equal(available([plainSet('a', true), plainSet('b')]), false);
  assert.equal(available([plainSet('a'), plainSet('b', true)]), true);
  assert.equal(available([]), false);
});

test('Two sides available: уровень, который не плывёт главный сет, → false', () => {
  const sets = [plainSet('a'), plainSet('main', true, { level_ids: [1] }), plainSet('c')];
  assert.equal(available(sets, INT), false);
  assert.equal(available(sets, 1), true);
  // главный — второй у тренера, но уровень не плывёт первый сет: у него главный первый
  assert.equal(available([plainSet('a', false, { level_ids: [1] }), plainSet('m', true)], INT), false);
});

test('splitTwoSides: деление по ПЕРВОМУ главному — лицо до него, оборот с ним и всё после', () => {
  const s = splitTwoSides(model([plainSet('a'), plainSet('b'), plainSet('m1', true), plainSet('c'), plainSet('m2', true)]).cubes);
  assert.deepEqual(s.front.map((c) => c.no), [1, 2]);
  assert.deepEqual(s.back.map((c) => c.no), [3, 4, 5]);
  assert.equal(s.mainNo, 3);
});

test('numbersPages: numbers — один лист без метки и полосы; two sides — «1 / 2» с полосой к главному и «2 / 2»', () => {
  const full = sheet('2026-11-26-1945');
  full.sets[3].main = true;
  const m = toSheetModel(source(workoutForLevel(full, INT)));
  const [one] = numbersPages(m, 'numbers');
  assert.deepEqual(one.cubes.map((c) => c.no), [1, 2, 3, 4, 5]);
  assert.equal(one.pageMark, null);
  assert.equal(one.nextMainNo, null);
  const [front, back] = numbersPages(m, 'numbers-two-sides');
  assert.deepEqual(front.cubes.map((c) => c.no), [1, 2, 3]);
  assert.equal(front.pageMark, '1 / 2');
  assert.equal(front.nextMainNo, 4);
  assert.deepEqual(back.cubes.map((c) => c.no), [4, 5]);
  assert.equal(back.pageMark, '2 / 2');
  assert.equal(back.nextMainNo, null);
  const noMain = toSheetModel(source(workoutForLevel(sheet('2026-11-26-1945'), INT)));
  assert.equal(numbersPages(noMain, 'numbers-two-sides'), null, 'без main — вида нет');
});

test('numbersPages: заметка тренера — только на первом листе', () => {
  const m = toSheetModel(source({ note: 'Fins', sets: [plainSet('a'), plainSet('m', true)] }, null));
  assert.equal(numbersPages(m, 'numbers')[0].note, 'Fins');
  assert.deepEqual(numbersPages(m, 'numbers-two-sides').map((p) => p.note), ['Fins', null]);
});

/** Сет со строками-временами уровня INT: `times` — значения send_off. */
const timedSet = (title, times, main = false) => ({
  title, repeat: 1, level_ids: null, ...(main ? { main: true } : {}),
  lines: times.map((t) => ({ reps: 1, distance: 50, text: 'x', intensity: null, per_level: t == null ? null : { [INT]: { reps: null, send_off: t } } })),
});
const legend = (sets, dir = 'rtl') => legendParts(toSheetModel(source({ sets })).cubes, dir).map((p) => `${p.sign} = ${p.word}`);

test('legendParts: только отдых → R; только выходы → @; оба → R, потом @; времён нет → подписи нет', () => {
  assert.deepEqual(legend([timedSet('a', ['rest :20', 'rest :10'])]), ['R = מנוחה']);
  assert.deepEqual(legend([timedSet('a', ['2:10'])]), ['@ = יציאה']);
  assert.deepEqual(legend([timedSet('a', ['1:55', 'rest :20'])]), ['R = מנוחה', '@ = יציאה']);
  assert.deepEqual(legend([timedSet('a', [null, null])]), []);
});

test('legendParts: язык — по направлению листа', () => {
  assert.deepEqual(legend([timedSet('a', ['rest :20', '2:10'])], 'ltr'), ['R = rest', '@ = send-off']);
});

test('numbersPages: подпись — для каждой стороны отдельно', () => {
  const m = toSheetModel(source({ sets: [timedSet('a', ['rest :20']), timedSet('m', ['2:10'], true)] }));
  assert.deepEqual(numbersPages(m, 'numbers')[0].legend.map((p) => p.sign), ['R', '@']);
  assert.deepEqual(numbersPages(m, 'numbers-two-sides').map((p) => p.legend.map((x) => x.sign)), [['R'], ['@']]);
});

test('лист 26.11: в Numbers только отдых — «R = מנוחה»', () => {
  const m = toSheetModel(source(workoutForLevel(sheet('2026-11-26-1945'), INT)));
  assert.equal(m.dir, 'rtl');
  assert.deepEqual(numbersPages(m, 'numbers')[0].legend, [{ sign: 'R', word: 'מנוחה' }]);
});

test('numbersLineKey: номер сета · индекс строки', () => {
  assert.equal(numbersLineKey(model([plainSet('a'), plainSet('b')]).cubes[1], 0), '2.0');
});

test('lineTextSpan: без дистанции — и её колонка; без времени — и колонка времени', async () => {
  const { lineTextSpan } = await import('./numbers-pages.ts');
  assert.deepEqual(lineTextSpan({ head: '3×100', time: 'R:10' }), { wide: false, toEnd: false });
  assert.deepEqual(lineTextSpan({ head: '200', time: '' }), { wide: false, toEnd: true });
  assert.deepEqual(lineTextSpan({ head: '', time: '@1:55' }), { wide: true, toEnd: false });
  assert.deepEqual(lineTextSpan({ head: '', time: '' }), { wide: true, toEnd: true });
});

test('свои повторы уровня — в дистанции («3×100» у уровня с reps: 3), время — без «3×»', () => {
  const line = { reps: 4, distance: 100, text: 'חתירה', intensity: null, per_level: { [INT]: { reps: 3, send_off: '1:45' } } };
  const m = toSheetModel(source({ sets: [{ title: 'x', repeat: 1, level_ids: null, lines: [line] }] }));
  const [page] = numbersPages(m, 'numbers');
  assert.equal(page.cubes[0].lines[0].head, '3×100');
  assert.equal(page.cubes[0].lines[0].time, '@1:45');
});
