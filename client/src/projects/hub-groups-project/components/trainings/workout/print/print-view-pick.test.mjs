import '../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const { availableViews, pickView, readSavedView, saveView, PRINT_VIEW_STORAGE_KEY } = await import('./print-view-pick.ts');

const Sheet = () => null;
const view = (id, extra = {}) => ({ id, label: id, orientation: 'portrait', Sheet, ...extra });
const source = (levels) => ({ workout: { sets: [] }, levelId: null, level: null, groupName: 'G', levels });

test('availableViews: без available — годится всегда, порядок реестра', () => {
  const list = availableViews({ b: view('b'), a: view('a') }, source(1));
  assert.deepEqual(list.map((v) => v.id), ['b', 'a']);
});

test('availableViews: available решает по source', () => {
  const views = { large: view('large'), all: view('all', { available: (s) => s.levels > 1 }) };
  assert.deepEqual(availableViews(views, source(1)).map((v) => v.id), ['large']);
  assert.deepEqual(availableViews(views, source(3)).map((v) => v.id), ['large', 'all']);
});

test('pickView: нужный, если годный; иначе первый; пусто — null', () => {
  const list = [view('large'), view('duplex')];
  assert.equal(pickView(list, 'duplex').id, 'duplex');
  assert.equal(pickView(list, 'gone').id, 'large');
  assert.equal(pickView(list, null).id, 'large');
  assert.equal(pickView([], 'large'), null);
});

test('pickView: нужного нет или он недоступен — вид по умолчанию, если годен; иначе первый', () => {
  const list = [view('rows'), view('large'), view('two-sides')];
  assert.equal(pickView(list, 'gone', 'large').id, 'large');
  assert.equal(pickView(list, null, 'large').id, 'large');
  assert.equal(pickView(list, 'two-sides', 'large').id, 'two-sides');
  assert.equal(pickView([view('rows')], 'two-sides', 'large').id, 'rows', 'и по умолчанию недоступен — первый годный');
});

test('readSavedView / saveView: запомненный вид; пусто, нет хранилища или оно бросает — null и без ошибок', () => {
  const map = new Map();
  const storage = { getItem: (k) => map.get(k) ?? null, setItem: (k, v) => map.set(k, v) };
  assert.equal(readSavedView(storage), null);
  saveView(storage, 'rows');
  assert.equal(map.get(PRINT_VIEW_STORAGE_KEY), 'rows');
  assert.equal(readSavedView(storage), 'rows');
  assert.equal(readSavedView(null), null);
  assert.doesNotThrow(() => saveView(undefined, 'rows'));
  const broken = { getItem: () => { throw new Error('private'); }, setItem: () => { throw new Error('quota'); } };
  assert.equal(readSavedView(broken), null);
  assert.doesNotThrow(() => saveView(broken, 'rows'));
});
