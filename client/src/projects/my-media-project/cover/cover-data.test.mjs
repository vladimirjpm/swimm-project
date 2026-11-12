// Юнит-тест данных обложки. Запуск: `npm --prefix client test` (встроенный раннер Node —
// `node --test`; `.ts` он читает сам, срезая типы, поэтому `cover-data.ts` держит только
// импорты типов). Своего тестового фреймворка у клиента нет, и заводить его ради чистых
// функций незачем.

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildCoverData, coverBadge, coverDay, coverDistance, coverEmblem, coverFileName, coverRange,
} from './cover-data.ts';

const NO_MARKS = { record: null, sb: false, pb: false };

const source = (over = {}) => ({
  competitionName: 'אליפות אילת הפתוחה',
  tile: { letter: 'K', ageGroup: '8-11', season: '❄️', isChampionship: false },
  emblemUrl: null,
  isChampionship: false,
  date: '2026-11-05',
  firstDay: '2026-11-05',
  lastDay: '2026-11-06',
  place: 1,
  medal: true,
  styleName: 'individual_medley',
  distance: '100',
  time: '01:37.05',
  failLabel: null,
  marks: NO_MARKS,
  swimmerName: 'סבינה ברנצב',
  clubName: 'הפועל דולפין נתניה',
  ...over,
});

test('дата: день заплыва и дни соревнования', () => {
  assert.equal(coverDay('2026-11-05'), '5 NOV');
  assert.equal(coverRange('2026-11-05', '2026-11-05'), '5 NOV');
  assert.equal(coverRange('2026-11-05', '2026-11-06'), '5–6 NOV');
  assert.equal(coverRange('2026-10-30', '2026-11-01'), '30 OCT–1 NOV');
});

test('метка одна: рекорд > SB > PB', () => {
  assert.equal(coverBadge({ record: { kind: 'age' }, sb: true, pb: true }), 'AGE RECORD');
  assert.equal(coverBadge({ record: { kind: 'national' }, sb: false, pb: false }), 'NATIONAL RECORD');
  assert.equal(coverBadge({ record: null, sb: true, pb: true }), 'SEASON BEST');
  assert.equal(coverBadge({ record: null, sb: false, pb: true }), 'PERSONAL BEST');
  assert.equal(coverBadge(NO_MARKS), null);
});

test('значок: значок серии важнее кубка, кубок — только у чемпионата', () => {
  assert.deepEqual(coverEmblem('/api/series-emblems/1/abc', true), { kind: 'image', url: '/api/series-emblems/1/abc' });
  assert.deepEqual(coverEmblem(null, true), { kind: 'cup' });
  assert.deepEqual(coverEmblem(null, false), { kind: 'empty' });
});

test('дистанция: метры дописываются числу, эстафета — как в протоколе', () => {
  assert.equal(coverDistance('100'), '100m');
  assert.equal(coverDistance('4X50'), '4X50');
});

test('место ≠ медаль: число сохраняется, медаль — отдельным признаком', () => {
  assert.deepEqual(buildCoverData(source({ place: 1, medal: false })).place, { value: 1, medal: false });
  assert.deepEqual(buildCoverData(source({ place: 3, medal: true })).place, { value: 3, medal: true });
  assert.equal(buildCoverData(source({ place: null, medal: false })).place, null);
});

test('Р76: у помеченной строки меток нет, время и место — как в протоколе', () => {
  // `swimMarks` у строки `Under review` отдаёт пустые метки — обложка их не додумывает.
  const data = buildCoverData(source({ marks: NO_MARKS, place: 2, medal: true, time: '00:40.08' }));
  assert.equal(data.badge, null);
  assert.equal(data.timeText, '00:40.08');
  assert.deepEqual(data.place, { value: 2, medal: true });
});

test('снятый: вместо времени — пометка протокола', () => {
  assert.equal(buildCoverData(source({ failLabel: 'DQ', time: '' })).timeText, 'DQ');
});

test('сборка: даты, год, пустое название клуба', () => {
  const data = buildCoverData(source({ clubName: '' }));
  assert.equal(data.dayLabel, '5 NOV');
  assert.equal(data.rangeLabel, '5–6 NOV');
  assert.equal(data.year, '2026');
  assert.equal(data.clubName, null);
});

test('имя файла: без пробелов и чужих знаков', () => {
  assert.equal(coverFileName('2026-11-05', '100', 'IM', 'A'), 'swimhub-cover-2026-11-05-100IM-A');
  assert.equal(coverFileName('2026-11-05', '4X50', 'free', 'C'), 'swimhub-cover-2026-11-05-4X50free-C');
});
