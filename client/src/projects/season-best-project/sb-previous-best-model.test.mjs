// Юнит-тест карточки «лучший прошлого сезона» на /season-best. Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { deltaLabel, isSameSlice, leadersByGender, previousBestLines } from './sb-previous-best-model.ts';

test('подпись разницы всегда со знаком', () => {
  assert.equal(deltaLabel(-420), '−0.42');
  assert.equal(deltaLabel(150), '+0.15');
  assert.equal(deltaLabel(0), '=');
});

const row = (gender, time_ms, id = time_ms) => ({ gender, time_ms, id });

test('лидер пола — первая строка этого пола (сервер уже отсортировал по времени)', () => {
  const leaders = leadersByGender([row('male', 30000), row('female', 31000), row('male', 30500)]);
  assert.equal(leaders.male.time_ms, 30000);
  assert.equal(leaders.female.time_ms, 31000);
});

test('пол не выбран — строка на каждый пол, разница «нынешний − прошлый»', () => {
  const lines = previousBestLines(
    [row('male', 29600), row('female', 31500)],
    [row('male', 30000), row('female', 31000)],
    null,
  );
  assert.deepEqual(lines.map((l) => [l.gender, l.deltaMs]), [['male', -400], ['female', 500]]);
});

test('пол выбран — строка одна', () => {
  const lines = previousBestLines([row('female', 31500)], [row('male', 30000), row('female', 31000)], 'female');
  assert.deepEqual(lines.map((l) => l.gender), ['female']);
});

test('в прошлом сезоне пола не было — строки нет; в этом нет — разницы нет', () => {
  assert.deepEqual(previousBestLines([row('male', 30000)], [row('female', 31000)], 'male'), []);
  const [line] = previousBestLines([], [row('male', 30000)], 'male');
  assert.equal(line.deltaMs, null);
});

const echo = (over = {}) => ({
  season: 2024, style: 'breaststroke', distance: '50', pool_type: null,
  gender: null, age: 10, age_to: null, age_group: null, ...over,
});
const wanted = (over = {}) => ({
  season: 2024, style: 'breaststroke', distance: '50', poolType: null,
  gender: null, age: 10, ageTo: null, ageGroup: null, ...over,
});

test('ответ про тот же срез узнаётся, «50m» и «50» — одна дистанция', () => {
  assert.equal(isSameSlice(echo(), wanted()), true);
  assert.equal(isSameSlice(echo(), wanted({ distance: '50m' })), true);
});

test('устаревший ответ (другой сезон, возраст, пол) — не наш', () => {
  assert.equal(isSameSlice(echo(), wanted({ season: 2025 })), false);
  assert.equal(isSameSlice(echo(), wanted({ age: 11 })), false);
  assert.equal(isSameSlice(echo(), wanted({ gender: 'male' })), false);
  assert.equal(isSameSlice(echo({ pool_type: '25m' }), wanted()), false);
});
