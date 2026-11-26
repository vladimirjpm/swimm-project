// Юнит-тесты модели листа печати (docs/plans/workout-print-plan.md, §3.2–3.6, §8.1).
// Фикстуры — архив листов тренера `docs/workouts/sheets/` (26.11 — Intermediate, 22.11 — другой лист).
// Запуск: `npm --prefix client test`.

import '../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const { toSheetModel, restLabel, sheetDir, splitMain, isMainSet } = await import('./print-sheet-model.ts');
const { workoutForLevel } = await import('../workout-for-level.ts');
const { levelMeters, setMeters } = await import('../workout-totals.ts');

const sheet = (name) => JSON.parse(readFileSync(new URL(`../../../../../../../../docs/workouts/sheets/${name}.json`, import.meta.url), 'utf8'));

const INT = 37; // Intermediate в листах 22.11 / 26.11 (ключи per_level: 36–39)
const LEVEL = { rank: 2, name: 'Intermediate' };
const source = (workout, levelId = INT) => ({ workout, levelId, level: LEVEL, groupName: 'דולפין נתניה מאסטרס' });

/** Лист 26.11 в версии уровня; `mainNos` — номера сетов (с 1), отмеченные главными. */
function sheet2611({ mainNos = [] } = {}) {
  const full = sheet('2026-11-26-1945');
  full.sets.forEach((set, i) => { if (mainNos.includes(i + 1)) set.main = true; });
  return workoutForLevel(full, INT);
}

const plainSet = (title, main = false, lines = [{ reps: 1, distance: 100, text: 'free', intensity: null, per_level: null }]) =>
  ({ title, repeat: 1, level_ids: null, lines, ...(main ? { main: true } : {}) });

test('restLabel: «rest :10» → «R:10», «Rest:30» → «R:30», «rest 0:30» → «R:0:30»; остальное как есть', () => {
  assert.equal(restLabel('rest :10'), 'R:10');
  assert.equal(restLabel('Rest:30'), 'R:30');
  assert.equal(restLabel('rest 0:30'), 'R:0:30');
  assert.equal(restLabel('rest :30'), 'R:30');
  assert.equal(restLabel('1:45'), '1:45');
  assert.equal(restLabel(':35'), ':35');
  assert.equal(restLabel('R:20'), 'R:20');
  assert.equal(restLabel('rest'), 'rest');
  assert.equal(restLabel(null), '');
  assert.equal(restLabel(undefined), '');
});

test('sheetDir: иврит → rtl; английский → ltr; смешанный — по большинству', () => {
  assert.equal(sheetDir(sheet2611()), 'rtl');
  assert.equal(sheetDir(workoutForLevel(sheet('2026-11-22-2000'), INT)), 'rtl');
  const line = (text) => ({ reps: 1, distance: 100, text, intensity: null, per_level: null });
  const en = { sets: [plainSet('Warm-up', false, [line('free'), line('kick 2V'), line('pull')])] };
  assert.equal(sheetDir(en), 'ltr');
  const mixedHe = { sets: [plainSet('x', false, [line('חתירה'), line('גב'), line('free')])] };
  assert.equal(sheetDir(mixedHe), 'rtl');
  const mixedEn = { sets: [plainSet('x', false, [line('חתירה'), line('free'), line('kick')])] };
  assert.equal(sheetDir(mixedEn), 'ltr');
  assert.equal(sheetDir({ sets: [plainSet('x', false, [line('חתירה'), line('free')])] }), 'ltr', 'поровну — ltr');
  assert.equal(sheetDir({ sets: [] }), 'ltr');
  // у строки нет букв — решают названия
  assert.equal(sheetDir({ sets: [plainSet('מרכזי', false, [line('')])] }), 'rtl');
});

test('лист 26.11 без отметки: нет главного → раскладки «главный справа» нет, кубы по порядку', () => {
  const m = toSheetModel(source(sheet2611()));
  assert.equal(m.mainLayout, null);
  assert.deepEqual(m.cubes.map((c) => c.no), [1, 2, 3, 4, 5]);
  assert.equal(m.dir, 'rtl');
  assert.ok(m.cubes.every((c) => !c.main));
});

test('лист 26.11: итог 2.5 km, метры сетов = setMeters, «2×» у сета с repeat', () => {
  const workout = sheet2611();
  const m = toSheetModel(source(workout));
  assert.equal(m.header.kmLabel, '2.5 km');
  assert.equal(m.totalMeters, levelMeters(workout, INT));
  assert.equal(m.totalMeters, 2500);
  m.cubes.forEach((c, i) => assert.equal(c.meters, setMeters(workout.sets[i], INT)));
  assert.equal(m.cubes[0].metersLabel, '300 m');
  const centre = m.cubes[3];
  assert.equal(centre.repeat, 2);
  assert.equal(centre.metersLabel, '1100 m');
  assert.deepEqual(m.header.level, LEVEL);
  assert.equal(m.header.groupName, 'דולפין נתניה מאסטרס');
});

test('лист 26.11: названия — свои или «Set N» по номеру у тренера', () => {
  const m = toSheetModel(source(sheet2611()));
  assert.deepEqual(m.cubes.map((c) => c.title), ['Set 1', 'שנורקל', 'כפות', 'מרכזי · סט שני עם זומרס', 'Set 5']);
});

test('лист 26.11: «rest :30» уезжает в «R:30», ячейка у строки без иврита на месте', () => {
  const m = toSheetModel(source(sheet2611()));
  const cells = m.cubes.flatMap((c) => c.lines.map((l) => l.cell));
  assert.ok(cells.includes('R:10') && cells.includes('R:30'));
  assert.ok(cells.every((c) => !/rest/i.test(c)), cells.join('|'));
  const centre = m.cubes[3];
  const row = centre.lines.find((l) => l.head === '2×50');
  assert.equal(row.cell, 'R:30');
  assert.equal(row.textDir, 'ltr', 'строка без иврита — свой bidi внутри подписи');
  assert.equal(m.cellChars, 4, 'самая длинная ячейка листа — «R:30»');
});

test('лист 26.11 с главным ④: правая колонка — главный, слева кубы и указатель «➜ ④» после ③ и перед ⑤', () => {
  const m = toSheetModel(source(sheet2611({ mainNos: [4] })));
  assert.deepEqual(m.mainLayout.right.map((c) => c.no), [4]);
  assert.ok(m.mainLayout.right[0].main);
  assert.deepEqual(m.mainLayout.left.map((i) => (i.kind === 'cube' ? i.cube.no : `➜${i.nos.join(',')}`)), [1, 2, 3, '➜4', 5]);
  // номера кубов не зависят от колонки
  assert.deepEqual(m.cubes.map((c) => c.no), [1, 2, 3, 4, 5]);
});

test('несколько главных (В17): все в правой колонке по порядку; подряд идущие — одним указателем', () => {
  const sets = [plainSet('a'), plainSet('b', true), plainSet('c'), plainSet('d', true), plainSet('e', true), plainSet('f')];
  const m = toSheetModel(source({ sets }, null));
  assert.deepEqual(m.mainLayout.right.map((c) => c.no), [2, 4, 5]);
  assert.deepEqual(
    m.mainLayout.left.map((i) => (i.kind === 'cube' ? i.cube.no : `➜${i.nos.join(',')}`)),
    [1, '➜2', 3, '➜4,5', 6],
  );
});

test('главный первым или последним — указатель всё равно стоит на его месте', () => {
  const first = toSheetModel(source({ sets: [plainSet('a', true), plainSet('b')] }, null));
  assert.deepEqual(first.mainLayout.left.map((i) => i.kind), ['pointer', 'cube']);
  const last = toSheetModel(source({ sets: [plainSet('a'), plainSet('b', true)] }, null));
  assert.deepEqual(last.mainLayout.left.map((i) => i.kind), ['cube', 'pointer']);
});

test('все сеты главные или сет один → нет раскладки «главный справа» (кубы)', () => {
  assert.equal(toSheetModel(source({ sets: [plainSet('a', true), plainSet('b', true)] }, null)).mainLayout, null);
  assert.equal(toSheetModel(source({ sets: [plainSet('a', true)] }, null)).mainLayout, null);
  assert.equal(toSheetModel(source({ sets: [plainSet('a')] }, null)).mainLayout, null);
  assert.equal(splitMain([]), null);
});

test('isMainSet: читает main безопасно, без поля в типе', () => {
  assert.equal(isMainSet({ title: 'x', repeat: 1, lines: [] }), false);
  assert.equal(isMainSet({ title: 'x', repeat: 1, lines: [], main: false }), false);
  assert.equal(isMainSet({ title: 'x', repeat: 1, lines: [], main: true }), true);
  // main доезжает через версию уровня (`...set`)
  const full = { sets: [{ ...plainSet('a'), main: true }, plainSet('b')] };
  assert.equal(isMainSet(workoutForLevel(full, INT).sets[0]), true);
});

test('уровень, не плывущий главный сет: главного нет → кубы', () => {
  const full = { sets: [plainSet('a'), { ...plainSet('main', true), level_ids: [1] }, plainSet('c')] };
  const m = toSheetModel(source(full, INT));
  assert.equal(m.mainLayout, null);
  assert.deepEqual(m.cubes.map((c) => c.no), [1, 3], 'полный workout: пропуск — дырка в нумерации, как у «Set N»');
  assert.deepEqual(m.cubes.map((c) => c.title), ['a', 'c']);
  // тот же workout для уровня, который сет плывёт, — главный есть
  assert.deepEqual(toSheetModel(source(full, 1)).mainLayout.right.map((c) => c.no), [2]);
});

test('setNumbers: номера тренера, когда дырку уже сделал отсев (версия уровня с сервера)', () => {
  const m = toSheetModel({ ...source({ sets: [plainSet('a'), plainSet('c')] }, INT), setNumbers: [1, 3] });
  assert.deepEqual(m.cubes.map((c) => c.no), [1, 3]);
  assert.equal(toSheetModel(source({ sets: [{ ...plainSet(null) }] }, null)).cubes[0].title, 'Set 1');
});

test('свои повторы уровня (cell.reps): в подписи, в ячейке и в метрах', () => {
  const line = { reps: 4, distance: 100, text: 'חתירה', intensity: null, per_level: { [INT]: { reps: 3, send_off: '1:45' }, 1: { reps: null, send_off: '1:30' } } };
  const workout = { sets: [{ title: 'x', repeat: 2, level_ids: null, lines: [line] }] };
  const m = toSheetModel(source(workout));
  assert.equal(m.cubes[0].lines[0].head, '3×100');
  assert.equal(m.cubes[0].lines[0].cell, '3× 1:45');
  assert.equal(m.cubes[0].meters, 600, '2 × 3 × 100');
  assert.equal(m.totalMeters, 600);
  assert.equal(m.cellChars, 7);
  // другой уровень — свои 4×100 и своё время
  const other = toSheetModel(source(workout, 1));
  assert.equal(other.cubes[0].lines[0].head, '4×100');
  assert.equal(other.cubes[0].lines[0].cell, '1:30');
  // дорожка без уровня — ячеек нет
  assert.equal(toSheetModel(source(workout, null)).cubes[0].lines[0].cell, '');
});

test('ширина ячейки — по самой длинной ячейке листа после «R:»', () => {
  const mk = (cell) => ({ reps: 1, distance: 50, text: 'x', intensity: null, per_level: { [INT]: cell } });
  const workout = { sets: [plainSet('a', false, [mk({ send_off: 'rest :30' }), mk({ send_off: '1:45' })]), plainSet('b', false, [mk({ reps: 3, send_off: '2:10' })])] };
  const m = toSheetModel(source(workout));
  assert.deepEqual(m.cubes.map((c) => c.lines.map((l) => l.cell)), [['R:30', '1:45'], ['3× 2:10']]);
  assert.equal(m.cellChars, 7);
  assert.equal(toSheetModel(source({ sets: [plainSet('a')] }, null)).cellChars, 0);
});

test('нагрузка: «1-3V» → три чипа, «2V» → один, «sprint» → один, непонятная → текст', () => {
  const mk = (intensity) => ({ reps: 1, distance: 50, text: 'x', intensity, per_level: null });
  const m = toSheetModel(source({ sets: [plainSet('a', false, [mk('1-3V'), mk('2V'), mk('sprint'), mk('easy'), mk(null)])] }, null));
  const [a, b, c, d, e] = m.cubes[0].lines;
  assert.deepEqual(a.chips, ['V1', 'V2', 'V3']);
  assert.deepEqual(b.chips, ['V2']);
  assert.deepEqual(c.chips, ['Sprint']);
  assert.equal(d.chips, null);
  assert.equal(d.intensityText, 'easy');
  assert.equal(e.chips, null);
  assert.equal(e.intensityText, null);
});

test('лист 26.11: чип «V2» у строк с нагрузкой 2V; строки без текста и данных не печатаются', () => {
  const m = toSheetModel(source(sheet2611()));
  assert.deepEqual(m.cubes[2].lines[0].chips, ['V2']);
  const empty = { reps: 1, distance: null, text: '  ', intensity: null, per_level: null };
  const withEmpty = toSheetModel(source({ sets: [plainSet('a', false, [empty, { ...empty, text: 'קל' }])] }, null));
  assert.equal(withEmpty.cubes[0].lines.length, 1);
});

test('заметка тренера и пустой workout', () => {
  assert.equal(toSheetModel(source({ note: '  Fins today  ', sets: [plainSet('a')] }, null)).note, 'Fins today');
  assert.equal(toSheetModel(source({ note: null, sets: [plainSet('a')] }, null)).note, null);
  const empty = toSheetModel(source({ note: 'x', sets: [] }, null));
  assert.deepEqual(empty.cubes, []);
  assert.equal(empty.header.kmLabel, '0.0 km');
});

test('лист 22.11 (другой лист): модель собирается, номера по порядку, метры сходятся', () => {
  const workout = workoutForLevel(sheet('2026-11-22-2000'), INT);
  const m = toSheetModel(source(workout));
  assert.equal(m.cubes.length, workout.sets.length);
  assert.deepEqual(m.cubes.map((c) => c.no), workout.sets.map((_, i) => i + 1));
  assert.equal(m.totalMeters, levelMeters(workout, INT));
  assert.ok(m.cubes.every((c) => c.lines.length > 0));
  assert.ok(m.cubes.flatMap((c) => c.lines).every((l) => !/rest/i.test(l.cell)));
});
