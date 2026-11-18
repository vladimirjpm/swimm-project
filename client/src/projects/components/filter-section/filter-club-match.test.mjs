// Юнит-тест поиска клуба в фильтре по прежнему названию. Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { matchesClubQuery, clubFilterLabel } from './filter-club-match.ts';

const ALUMA = { label: 'אלומה עירוני הוד השרון', aliases: ['הפועל עירוני הוד השרון', 'Hapoel Hod Hasharon'] };

test('находит по текущему названию', () => {
  assert.equal(matchesClubQuery(ALUMA, 'אלומה'), true);
});

test('находит по прежнему названию, и ивритскому, и английскому (без регистра)', () => {
  assert.equal(matchesClubQuery(ALUMA, 'הפועל עירוני'), true);
  assert.equal(matchesClubQuery(ALUMA, 'hapoel hod'), true);
});

test('чужое название не находит; клуб без прежних названий — только по подписи', () => {
  assert.equal(matchesClubQuery(ALUMA, 'מכבי'), false);
  assert.equal(matchesClubQuery({ label: 'מכבי חיפה' }, 'הפועל'), false);
});

test('пустой запрос — подходит всё', () => {
  assert.equal(matchesClubQuery(ALUMA, '  '), true);
});

test('подпись фильтра — текущее название, напечатанное и прежние — синонимы поиска', () => {
  const { label, aliases } = clubFilterLabel('הפועל עירוני הוד השרון', 'אלומה עירוני הוד השרון',
    ['הפועל עירוני הוד השרון', 'Hapoel Hod Hasharon']);
  assert.equal(label, 'אלומה עירוני הוד השרון');
  assert.deepEqual(aliases, ['הפועל עירוני הוד השרון', 'Hapoel Hod Hasharon']);
  assert.equal(matchesClubQuery({ label, aliases }, 'הפועל'), true);
});

test('клуб не переименован — подпись как есть, синонимов нет', () => {
  assert.deepEqual(clubFilterLabel('מכבי חיפה', null), { label: 'מכבי חיפה', aliases: undefined });
  assert.deepEqual(clubFilterLabel('מכבי חיפה', 'מכבי חיפה'), { label: 'מכבי חיפה', aliases: undefined });
});
