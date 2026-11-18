// Юнит-тест правила «плитка соревнования содержательна». Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { hasTileContent } from './competition-tile-content.ts';

const tile = (over = {}) => ({ letter: '', ageGroup: null, season: null, isChampionship: false, ...over });

test('есть буква категории или возрастная группа — плитка содержательна', () => {
  assert.equal(hasTileContent(tile({ letter: 'K', ageGroup: '8-11' })), true);
  assert.equal(hasTileContent(tile({ letter: 'M' })), true);
  assert.equal(hasTileContent(tile({ ageGroup: '8-11' })), true);
});

test('только значок сезона и/или кубок — плитку не показываем', () => {
  assert.equal(hasTileContent(tile()), false);
  assert.equal(hasTileContent(tile({ season: 'winter' })), false);
  assert.equal(hasTileContent(tile({ isChampionship: true })), false);
  assert.equal(hasTileContent(tile({ season: 'summer', isChampionship: true })), false);
});

test('переключатель: значок серии побеждает, иначе плитка, иначе значок сезона', async () => {
  const { competitionTileKind } = await import('./competition-tile-content.ts');
  assert.equal(competitionTileKind(tile({ emblemUrl: '/api/series-emblems/1/ab', season: 'winter' })), 'emblem');
  assert.equal(competitionTileKind(tile({ emblemUrl: '/api/series-emblems/1/ab', letter: 'Y', ageGroup: '11-14' })), 'emblem');
  assert.equal(competitionTileKind(tile({ letter: 'Y', ageGroup: '11-14' })), 'tile');
  assert.equal(competitionTileKind(tile({ season: 'winter' })), 'mark');
  assert.equal(competitionTileKind(tile({ isChampionship: true })), 'mark');
  assert.equal(competitionTileKind(tile()), 'none');
  assert.equal(competitionTileKind(tile({ emblemUrl: '' })), 'none');
});
