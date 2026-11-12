// Юнит-тест выбора заплывов плаката. Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { POSTER_LIMIT, clampSelection, defaultSelection, toggleSelection } from './poster-selection.ts';

const swim = (id, over = {}) => ({
  id, date: '2026-11-05', place: 5, medal: false, styleName: 'freestyle', distanceLabel: '50m',
  isRelay: false, time: '00:40.00', failed: false, mark: null, ...over,
});
const all = (swims) => new Set(swims.map((s) => s.id));
const ids = (set) => [...set].sort((a, b) => a - b);

test('помещаются все — все отмечены', () => {
  const swims = [swim(1), swim(2), swim(3)];
  assert.deepEqual(ids(defaultSelection(swims, 'A')), [1, 2, 3]);
});

test('сверх предела: сперва снимаются заплывы без медали и метки, с поздних', () => {
  const swims = [swim(1), swim(2, { medal: true, place: 1 }), swim(3), swim(4, { mark: { kind: 'pb' } }), swim(5)];
  assert.deepEqual(ids(clampSelection(swims, all(swims), 4)), [1, 2, 3, 4]);
  assert.deepEqual(ids(clampSelection(swims, all(swims), 3)), [1, 2, 4]);
  assert.deepEqual(ids(clampSelection(swims, all(swims), 1)), [2]);
});

test('предел раскладки: A — 8, B — 9, C — 10', () => {
  const swims = Array.from({ length: 12 }, (_, i) => swim(i + 1));
  assert.equal(defaultSelection(swims, 'A').size, POSTER_LIMIT.A);
  assert.equal(defaultSelection(swims, 'B').size, 9);
  assert.equal(defaultSelection(swims, 'C').size, 10);
});

test('галочка: снять можно всегда, поставить — пока есть место', () => {
  assert.deepEqual(ids(toggleSelection(new Set([1, 2]), 2, 2)), [1]);
  assert.deepEqual(ids(toggleSelection(new Set([1]), 2, 2)), [1, 2]);
  assert.deepEqual(ids(toggleSelection(new Set([1, 2]), 3, 2)), [1, 2]);
});
