// Юнит-тесты листов «листа строк» (виды rows / two-sides, хендофф 27.11 §3–§4, §8).
// Запуск: `npm --prefix client test`.

import '../../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const { splitTwoSides, rowsPages, setRuled, setHasTitleRow, rowsLineKey } = await import('./rows-pages.ts');
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
/** Как `available` вида two-sides (`twoSidesAvailable`): делится ли версия уровня. */
const available = (sets, levelId = null) => splitTwoSides(model(sets, levelId).cubes) != null;

test('two-sides available: нет главного → false; главный первый → false; главный второй → true', () => {
  assert.equal(available([plainSet('a'), plainSet('b')]), false);
  assert.equal(available([plainSet('a', true), plainSet('b')]), false);
  assert.equal(available([plainSet('a'), plainSet('b', true)]), true);
  assert.equal(available([]), false);
});

test('two-sides available: уровень, который не плывёт главный сет, → false', () => {
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

test('rowsPages: rows — один лист без метки и полосы; two-sides — «1 / 2» с полосой к главному и «2 / 2»', () => {
  const full = sheet('2026-11-26-1945');
  full.sets[3].main = true;
  const m = toSheetModel(source(workoutForLevel(full, INT)));
  const [rows] = rowsPages(m, 'rows');
  assert.deepEqual(rows.cubes.map((c) => c.no), [1, 2, 3, 4, 5]);
  assert.equal(rows.pageMark, null);
  assert.equal(rows.nextMainNo, null);
  const [front, back] = rowsPages(m, 'two-sides');
  assert.deepEqual(front.cubes.map((c) => c.no), [1, 2, 3]);
  assert.equal(front.pageMark, '1 / 2');
  assert.equal(front.nextMainNo, 4);
  assert.deepEqual(back.cubes.map((c) => c.no), [4, 5]);
  assert.equal(back.pageMark, '2 / 2');
  assert.equal(back.nextMainNo, null);
  const noMain = toSheetModel(source(workoutForLevel(sheet('2026-11-26-1945'), INT)));
  assert.equal(rowsPages(noMain, 'two-sides'), null, 'без main — вида нет');
});

test('rowsPages: заметка тренера — только на первом листе', () => {
  const m = toSheetModel(source({ note: 'Fins', sets: [plainSet('a'), plainSet('m', true)] }, null));
  assert.equal(rowsPages(m, 'rows')[0].note, 'Fins');
  assert.deepEqual(rowsPages(m, 'two-sides').map((p) => p.note), ['Fins', null]);
});

test('setRuled: линия у не первого, не главного и не сразу после главного', () => {
  const cubes = model([plainSet('a'), plainSet('b'), plainSet('m', true), plainSet('c'), plainSet('d'), plainSet('m2', true), plainSet('m3', true)]).cubes;
  assert.deepEqual(cubes.map((_, i) => setRuled(cubes, i)), [false, true, false, false, true, false, false]);
});

test('setHasTitleRow: своё название, повтор или main; «Set N» — нет строки заголовка', () => {
  const cubes = model([plainSet(null), plainSet('Drill'), { ...plainSet(null), repeat: 2 }, plainSet(null, true)]).cubes;
  assert.deepEqual(cubes.map(setHasTitleRow), [false, true, true, true]);
  const levelVersion = toSheetModel(source(workoutForLevel({ sets: [plainSet(null)] }, INT)));
  assert.equal(levelVersion.cubes[0].title, 'Set 1');
  assert.equal(setHasTitleRow(levelVersion.cubes[0]), false);
  assert.equal(rowsLineKey(cubes[1], 0), '2.0');
});
