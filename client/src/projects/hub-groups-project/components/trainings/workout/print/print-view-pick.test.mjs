import '../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const { availableViews, pickView } = await import('./print-view-pick.ts');

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
