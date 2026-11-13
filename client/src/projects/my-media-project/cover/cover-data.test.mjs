// Юнит-тест данных обложки. Запуск: `npm --prefix client test` (встроенный раннер Node —
// `node --test`; `.ts` он читает сам, срезая типы, поэтому `cover-data.ts` держит только
// импорты типов). Своего тестового фреймворка у клиента нет, и заводить его ради чистых
// функций незачем.

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildCoverData, coverBadge, coverDay, coverFileName, coverRange,
} from './cover-data.ts';

const source = (over = {}) => ({
  competitionName: 'אליפות אילת הפתוחה',
  tile: { letter: 'K', ageGroup: '8-11', season: 'winter', isChampionship: false },
  emblem: { kind: 'empty' },
  date: '2026-11-05',
  firstDay: '2026-11-05',
  lastDay: '2026-11-06',
  place: 1,
  medal: true,
  styleName: 'individual_medley',
  distanceLabel: '100m',
  time: '01:37.05',
  failLabel: null,
  mark: null,
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

test('подпись метки: словами, а не сокращением (какая метка одна — topMark в print-data)', () => {
  assert.equal(coverBadge({ kind: 'record', record: 'age' }), 'AGE RECORD');
  assert.equal(coverBadge({ kind: 'record', record: 'national' }), 'NATIONAL RECORD');
  assert.equal(coverBadge({ kind: 'sb' }), 'SEASON BEST');
  assert.equal(coverBadge({ kind: 'pb' }), 'PERSONAL BEST');
  assert.equal(coverBadge(null), null);
});

test('место ≠ медаль: число сохраняется, медаль — отдельным признаком', () => {
  assert.deepEqual(buildCoverData(source({ place: 1, medal: false })).place, { value: 1, medal: false });
  assert.deepEqual(buildCoverData(source({ place: 3, medal: true })).place, { value: 3, medal: true });
  assert.equal(buildCoverData(source({ place: null, medal: false })).place, null);
});

test('Р76: у помеченной строки меток нет, время и место — как в протоколе', () => {
  // `swimMarks` у строки `Under review` отдаёт пустые метки — обложка их не додумывает.
  const data = buildCoverData(source({ mark: null, place: 2, medal: true, time: '00:40.08' }));
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
