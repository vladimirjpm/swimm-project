// Юнит-тесты workout прошедшего занятия по уровням (26.11.2026). Запуск: `npm --prefix client test`.
import '../workout/ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const { lanesLabel, pastLevelWorkouts } = await import('./past-workout-levels.ts');

const w = (title) => ({ note: null, sets: [{ title, repeat: null, level_ids: null, lines: [] }] });
const fast = { id: 1, rank: 1, name: 'Fast', color: null };
const slow = { id: 2, rank: 2, name: 'Slow', color: null };
const lane = (no, level, workout) => ({ lane_no: no, level, level_workout: workout, people: [] });

test('одна карточка на уровень, дорожки уровня собраны', () => {
  const lanes = [lane(3, slow, w('S')), lane(1, fast, w('F')), lane(2, fast, w('F'))];
  const list = pastLevelWorkouts(lanes, null);
  assert.deepEqual(list.map((l) => [l.key, l.laneNos]), [['1', [1, 2]], ['2', [3]]]);
  assert.equal(list.some((l) => l.mine), false);
});

test('свой уровень — первым, даже если дорожка не первая', () => {
  const lanes = [lane(1, fast, w('F')), lane(2, fast, w('F')), lane(3, slow, w('S'))];
  const list = pastLevelWorkouts(lanes, 3);
  assert.deepEqual(list.map((l) => [l.key, l.mine]), [['2', true], ['1', false]]);
  // Своя — вторая дорожка уровня: уровень всё равно «мой».
  assert.equal(pastLevelWorkouts(lanes, 2)[0].mine, true);
});

test('дорожка без workout пропускается, без уровня — ключ none', () => {
  const lanes = [lane(1, null, w('All')), lane(2, slow, null), lane(3, slow, { note: null, sets: [] })];
  assert.deepEqual(pastLevelWorkouts(lanes, 1).map((l) => [l.key, l.mine]), [['none', true]]);
});

test('подпись дорожек', () => {
  assert.equal(lanesLabel([2]), 'Lane 2');
  assert.equal(lanesLabel([1, 2]), 'Lanes 1, 2');
});
