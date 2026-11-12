// Юнит-тест общих данных обложки и плаката. Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { printDistance, printEmblem, topMark } from './print-data.ts';

test('topMark: одна метка, приоритет рекорд > SB > PB', () => {
  assert.deepEqual(topMark({ record: { kind: 'age' }, sb: true, pb: true }), { kind: 'record', record: 'age' });
  assert.deepEqual(topMark({ record: null, sb: true, pb: true }), { kind: 'sb' });
  assert.deepEqual(topMark({ record: null, sb: false, pb: true }), { kind: 'pb' });
  assert.equal(topMark({ record: null, sb: false, pb: false }), null);
});

test('printEmblem: значок серии важнее кубка; без значка кубок только у чемпионата', () => {
  assert.deepEqual(printEmblem('/api/series-emblems/1', true), { kind: 'image', url: '/api/series-emblems/1' });
  assert.deepEqual(printEmblem(null, true), { kind: 'cup' });
  assert.deepEqual(printEmblem(null, false), { kind: 'empty' });
});

test('printDistance: метры у личной дистанции, эстафета как в протоколе', () => {
  assert.equal(printDistance('100'), '100m');
  assert.equal(printDistance('4X50'), '4X50');
});
