// Юнит-тест подгонки сетки строк плаката. Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { fitGrid } from './poster-fit.ts';

const area = (over = {}) => ({
  count: 5, width: 600, height: 500, cell: { width: 600, height: 100 }, gap: { x: 20, y: 0 },
  columns: [1, 2], maxScale: 1.5, ...over,
});

test('макетное число строк — масштаб 1, одна колонка', () => {
  assert.deepEqual(fitGrid(area()), { columns: 1, rows: 5, scale: 1 });
});

test('строк больше — они ужимаются по высоте', () => {
  const fit = fitGrid(area({ count: 8 }));
  assert.equal(fit.columns, 1);
  assert.equal(fit.scale, 0.625);
});

test('широкая область: две колонки, когда так заметно крупнее', () => {
  assert.deepEqual(fitGrid(area({ count: 8, width: 1220 })), { columns: 2, rows: 4, scale: 1 });
});

test('масштаб близкий — меньше колонок: строки заполняют высоту', () => {
  // 6 строк: одна колонка — 500/600, две — 1 по высоте, но ширина даёт 600/1220.
  const fit = fitGrid(area({ count: 6, width: 1100, height: 600 }));
  assert.equal(fit.columns, 1);
});

test('масштаб близкий — ровная сетка лучше сетки с пустыми местами', () => {
  const fit = fitGrid(area({ count: 10, width: 1000, height: 400, cell: { width: 200, height: 200 }, gap: { x: 0, y: 0 }, columns: [4, 5], maxScale: 1 }));
  assert.deepEqual(fit, { columns: 5, rows: 2, scale: 1 });
});

test('потолок увеличения и не больше колонок, чем ячеек', () => {
  assert.deepEqual(fitGrid(area({ count: 1, width: 2000, height: 2000 })), { columns: 1, rows: 1, scale: 1.5 });
});

test('зазоры масштабируются вместе с ячейкой', () => {
  assert.equal(fitGrid(area({ gap: { x: 0, y: 25 } })).scale, 500 / (5 * 100 + 4 * 25));
});
