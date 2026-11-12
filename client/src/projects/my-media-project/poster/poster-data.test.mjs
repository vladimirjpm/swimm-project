// Юнит-тест данных плаката. Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildPosterData, posterAgeLabel, posterBadge, posterFileName, posterRow, posterSeasonLabel,
  posterSummary, posterSwims, posterTime,
} from './poster-data.ts';

const swim = (over = {}) => ({
  id: 1, date: '2026-11-05', place: 1, medal: true, styleName: 'breaststroke', distanceLabel: '50m',
  isRelay: false, time: '00:49.61', failed: false, mark: null, ...over,
});

const source = (swims) => ({
  competitionName: 'אליפות אילת הפתוחה',
  tile: { letter: 'K', ageGroup: '8-11', season: '❄️', isChampionship: false },
  emblem: { kind: 'empty' },
  firstDay: '2026-11-05',
  swimmerName: 'ברנצב סבינה',
  clubName: 'הפועל דולפין נתניה',
  swims,
});

test('время: без ведущих нулей', () => {
  assert.equal(posterTime('00:49.61'), '49.61');
  assert.equal(posterTime('01:37.05'), '1:37.05');
  assert.equal(posterTime('00:09.50'), '9.50');
  assert.equal(posterTime('17:02.40'), '17:02.40');
  assert.equal(posterTime('00:00:49.61'), '49.61');
});

test('метка: коротко и словами', () => {
  assert.deepEqual(posterBadge({ kind: 'record', record: 'age' }), { kind: 'record', short: 'REC', long: 'Age record' });
  assert.deepEqual(posterBadge({ kind: 'sb' }), { kind: 'sb', short: 'SB', long: 'SB' });
  assert.deepEqual(posterBadge({ kind: 'pb' }), { kind: 'pb', short: 'PB', long: 'PB' });
  assert.equal(posterBadge(null), null);
});

test('строка сезона и возраста', () => {
  assert.equal(posterSeasonLabel('❄️', '2026'), 'חורף 2026');
  assert.equal(posterSeasonLabel('☀️', '2026'), 'קיץ 2026');
  assert.equal(posterSeasonLabel(null, '2026'), '2026');
  assert.equal(posterAgeLabel('8-11'), 'גילאי 8-11');
  assert.equal(posterAgeLabel(null), null);
});

test('заплывы плаката: без DNS/DQ, по порядку дней, порядок внутри дня сохраняется', () => {
  const swims = posterSwims([
    swim({ id: 1, date: '2026-11-06' }),
    swim({ id: 2, date: '2026-11-05' }),
    swim({ id: 3, date: '2026-11-05', failed: true }),
    swim({ id: 4, date: '2026-11-05' }),
  ]);
  assert.deepEqual(swims.map((s) => s.id), [2, 4, 1]);
});

test('место ≠ медаль: предварительный заплыв — место есть, медали нет', () => {
  assert.deepEqual(posterRow(swim({ place: 1, medal: false })).place, { value: 1, medal: false });
  assert.equal(posterRow(swim({ place: null, medal: false })).place, null);
});

test('Р76: у помеченной строки метки нет, время и место — как в протоколе', () => {
  const row = posterRow(swim({ mark: null, place: 2, medal: true, time: '00:40.08' }));
  assert.equal(row.badge, null);
  assert.equal(row.timeText, '40.08');
  assert.deepEqual(row.place, { value: 2, medal: true });
});

test('счёт медалей: только вручённые, по всем заплывам с результатом', () => {
  const summary = posterSummary([
    swim({ id: 1, place: 1, medal: true, mark: { kind: 'record', record: 'age' } }),
    swim({ id: 2, place: 1, medal: false }),
    swim({ id: 3, place: 2, medal: true }),
    swim({ id: 4, place: 3, medal: true }),
    swim({ id: 5, place: 5, medal: false }),
  ]);
  assert.deepEqual(summary, { gold: 1, silver: 1, bronze: 1, swimsLabel: '5 swims', recordsLabel: '1 age record' });
});

test('итог: один заплыв, рекорды разных видов, без рекордов', () => {
  assert.equal(posterSummary([swim()]).swimsLabel, '1 swim');
  assert.equal(posterSummary([swim()]).recordsLabel, null);
  const mixed = [swim({ mark: { kind: 'record', record: 'age' } }), swim({ id: 2, mark: { kind: 'record', record: 'national' } })];
  assert.equal(posterSummary(mixed).recordsLabel, '2 records');
  const same = [swim({ mark: { kind: 'record', record: 'age' } }), swim({ id: 2, mark: { kind: 'record', record: 'age' } })];
  assert.equal(posterSummary(same).recordsLabel, '2 age records');
});

test('сборка: строки — только отмеченные, счёт — по всем; год и подписи', () => {
  const data = buildPosterData(
    source([swim({ id: 1 }), swim({ id: 2, place: 2 }), swim({ id: 3, failed: true })]),
    new Set([2, 3]),
  );
  assert.deepEqual(data.rows.map((r) => r.id), [2]);
  assert.equal(data.summary.swimsLabel, '2 swims');
  assert.equal(data.summary.gold, 1);
  assert.equal(data.year, '2026');
  assert.equal(data.seasonLabel, 'חורף 2026');
  assert.equal(data.ageLabel, 'גילאי 8-11');
});

test('имя файла', () => {
  assert.equal(posterFileName('2026-11-05', 'B'), 'swimhub-poster-2026-11-05-B');
});
