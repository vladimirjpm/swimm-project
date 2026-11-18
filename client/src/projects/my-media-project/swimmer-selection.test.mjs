// Юнит-тесты мультиселекта пловцов My media (решение Влада 18.11.2026). Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  defaultSwimmerSelection, effectiveSwimmerSelection, swimmerSelectionLabel, toggleSwimmer,
} from './swimmer-selection.ts';

test('при загрузке — Me и семья, у кого есть заплывы, в порядке списка', () => {
  assert.deepEqual(defaultSwimmerSelection([1, 2, 3, 4], 2, new Set([4, 9])), [2, 4]);
  // Нет ни Me, ни семьи — All.
  assert.deepEqual(defaultSwimmerSelection([1, 2], null, new Set()), []);
  // Me без заплывов в сезоне не отмечается.
  assert.deepEqual(defaultSwimmerSelection([1, 2], 7, new Set([2])), [2]);
});

test('действующий выбор — только те, у кого есть заплывы; никого — All', () => {
  assert.deepEqual(effectiveSwimmerSelection([2, 7], [1, 2, 3]), [2]);
  assert.deepEqual(effectiveSwimmerSelection([7], [1, 2, 3]), []);
  assert.deepEqual(effectiveSwimmerSelection([], [1, 2]), []);
});

test('отметить и снять; порядок списка, отсутствующие в сезоне не теряются', () => {
  assert.deepEqual(toggleSwimmer([3], 1, [1, 2, 3]), [1, 3]);
  assert.deepEqual(toggleSwimmer([1, 3], 1, [1, 2, 3]), [3]);
  assert.deepEqual(toggleSwimmer([9], 2, [1, 2, 3]), [2, 9]);
});

test('подпись выбора', () => {
  assert.equal(swimmerSelectionLabel([]), 'All');
  assert.equal(swimmerSelectionLabel(['Dana']), 'Dana');
  assert.equal(swimmerSelectionLabel(['Dana', 'Noa']), 'Dana, Noa');
  assert.equal(swimmerSelectionLabel(['Dana', 'Noa', 'Gil']), 'Dana + 2');
});
