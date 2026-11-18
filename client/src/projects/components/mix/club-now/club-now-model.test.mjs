// Юнит-тест подсказки «now: …» у клуба, напечатанного под прежним названием (Р97).
// Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { clubNowLabel, mergeClubNow } from './club-now-model.ts';

const HAPOEL = 'הפועל עירוני הוד השרון';
const ALUMA = 'אלומה עירוני הוד השרון';

test('строка с club_now — подсказка текущим названием', () => {
  assert.equal(clubNowLabel(HAPOEL, ALUMA), ALUMA);
});

test('напечатано текущее или текущего нет — подсказки нет', () => {
  assert.equal(clubNowLabel(ALUMA, ALUMA), null);
  assert.equal(clubNowLabel(HAPOEL, null), null);
  assert.equal(clubNowLabel(HAPOEL, '  '), null);
});

test('карта Overview по id клуба; поле строки важнее карты', () => {
  const map = mergeClubNow({ 1454: ALUMA }, null, { 7: 'X' });
  assert.equal(clubNowLabel(HAPOEL, undefined, 1454, map), ALUMA);
  // Та же строка, но клуб в протоколе уже под текущим названием — не подсказываем.
  assert.equal(clubNowLabel(ALUMA, undefined, 1454, map), null);
  assert.equal(clubNowLabel(HAPOEL, 'Explicit', 1454, map), 'Explicit');
  assert.equal(clubNowLabel(HAPOEL, undefined, 999, map), null);
  assert.equal(map.size, 2);
});
