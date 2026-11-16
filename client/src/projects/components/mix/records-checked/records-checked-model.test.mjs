// Юнит-тест подписи «checked …, updated …» у справочника рекордов. Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { checkedLines, lineText, checkedAgo } from './records-checked-model.ts';

const NOW = Date.parse('2026-11-16T12:00:00Z');
const LABELS = { 'wa-masters': 'World Aquatics masters', worldrecords: 'World Aquatics' };

test('обе даты — «checked …, updated …»', () => {
  const [line] = checkedLines(
    [{ source: 'wa-masters', checkedAt: '2026-11-16T09:00:00Z', changedAt: '2026-09-16T11:47:44Z' }],
    ['wa-masters'], LABELS);
  assert.equal(line.label, 'World Aquatics masters');
  assert.equal(lineText(line, NOW), 'checked today, updated 16 Sep 2026');
});

test('сверки с совпавшими данными нет, но справочник менялся — только «updated»', () => {
  const [line] = checkedLines(
    [{ source: 'worldrecords', checkedAt: null, changedAt: '2026-09-22T07:43:12Z' }],
    ['worldrecords'], LABELS);
  assert.equal(lineText(line, NOW), 'updated 22 Sep 2026');
});

test('без обеих дат источник не подписывается; порядок — как в sources', () => {
  const lines = checkedLines([
    { source: 'worldrecords', checkedAt: '2026-11-15T09:00:00Z', changedAt: null },
    { source: 'wa-masters', checkedAt: null, changedAt: null },
  ], ['wa-masters', 'worldrecords'], LABELS);
  assert.deepEqual(lines.map((l) => l.source), ['worldrecords']);
  assert.equal(lineText(lines[0], NOW), 'checked yesterday');
});

test('давность: дни, потом дата', () => {
  assert.equal(checkedAgo('2026-11-13T12:00:00Z', NOW), '3 days ago');
  assert.equal(checkedAgo('2026-09-01T12:00:00Z', NOW), '1 Sep 2026');
});
