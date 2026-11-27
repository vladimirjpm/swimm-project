// Юнит-тесты «какой план можно открыть» (27.11.2026). Запуск: `npm --prefix client test`.
import '../workout/ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const { feedPlanKey, feedSessionKey, planViewer, viewablePast } = await import('./viewable-plans.ts');

const past = (id) => ({ session: { id, date: id.slice(0, 10), start: null, end: null } });
const plan = (id, status) => ({ date: id.slice(0, 10), session_id: id, status, lane_count: 8 });

const list = [past('2026-11-26-1945'), past('2026-11-14-1000'), past('2026-11-08-2000')];
const plans = [plan('2026-11-26-1945', 'published'), plan('2026-11-14-1000', 'draft')];
const ids = (l) => l.map((p) => p.session.id);

test('участник: список сервера как есть (там только опубликованные)', () => {
  assert.deepEqual(ids(viewablePast(list, null, 'member')), ids(list));
});

test('«👁 View» тренера: только опубликованный план', () => {
  assert.deepEqual(ids(viewablePast(list, plans, 'preview')), ['2026-11-26-1945']);
});

test('тренер: любой план, черновик тоже; занятие только с ответами — нет', () => {
  assert.deepEqual(ids(viewablePast(list, plans, 'manager')), ['2026-11-26-1945', '2026-11-14-1000']);
});

test('список не пришёл — null; планы не пришли — тренеру пусто', () => {
  assert.equal(viewablePast(null, plans, 'member'), null);
  assert.deepEqual(viewablePast(list, null, 'manager'), []);
});

test('planViewer: управляющий важнее «👁 View»', () => {
  assert.equal(planViewer(true, false), 'manager');
  assert.equal(planViewer(false, true), 'preview');
  assert.equal(planViewer(false, false), 'member');
});

test('feedSessionKey: дата + HHmm, без времени — null', () => {
  assert.equal(feedSessionKey({ date: '2026-11-26', start: '19:45' }), '2026-11-26-1945');
  assert.equal(feedSessionKey({ date: '2026-11-26', start: '7:05' }), '2026-11-26-0705');
  assert.equal(feedSessionKey({ date: '2026-11-26T00:00:00', start: '19:45:00' }), '2026-11-26-1945');
  assert.equal(feedSessionKey({ date: '2026-11-26', start: null }), null);
});

test('feedPlanKey: со временем — точно по ключу; без времени — по дате, если план в день один', () => {
  const keys = new Set(['2026-11-26-1945', '2026-11-17-2000', '2026-11-15-0700', '2026-11-15-2000']);
  assert.equal(feedPlanKey({ date: '2026-11-26', start: '19:45' }, keys), '2026-11-26-1945');
  assert.equal(feedPlanKey({ date: '2026-11-26', start: '07:00' }, keys), null);
  assert.equal(feedPlanKey({ date: '2026-11-17', start: null }, keys), '2026-11-17-2000');
  assert.equal(feedPlanKey({ date: '2026-11-15', start: null }, keys), null);
  assert.equal(feedPlanKey({ date: '2026-11-12', start: null }, keys), null);
});
