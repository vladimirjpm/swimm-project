// Юнит-тест подписи прежних названий клуба («Formerly: …»). Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { formerYears, formerName, formerNameLabel } from './club-former-names-model.ts';

test('годы: диапазон, один год, только один край, ничего', () => {
  assert.equal(formerYears(2024, 2026), '2024–2026');
  assert.equal(formerYears(2025, 2025), '2025');
  assert.equal(formerYears(2024, null), '2024');
  assert.equal(formerYears(null, 2026), '2026');
  assert.equal(formerYears(null, null), null);
});

test('название — иврит, английское только фоллбеком', () => {
  assert.equal(formerName({ name: 'הפועל עירוני הוד השרון', name_en: 'Hapoel Hod Hasharon' }), 'הפועל עירוני הוד השרון');
  assert.equal(formerName({ name: '', name_en: 'Hapoel Hod Hasharon' }), 'Hapoel Hod Hasharon');
});

test('подпись с годами и без', () => {
  assert.equal(formerNameLabel({ name: 'הפועל', from_year: 2024, to_year: 2026 }), 'הפועל (2024–2026)');
  assert.equal(formerNameLabel({ name: 'הפועל' }), 'הפועל');
});
