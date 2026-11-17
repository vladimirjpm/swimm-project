// Юнит-тесты ключа плана дорожек (CF5, docs/plans/trainings-coach-flow-plan.md §5.1): план — у занятия.
// Запуск: `npm --prefix client test`.

import './trainings/workout/ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
// Динамически — после хука: модуль импортирует соседа без расширения.
const {
  formatPlanKey, isSessionKey, keyDate, keyStart, pickDefaultKey, planKey, shortPlanKey,
} = await import('./lane-plan-key.ts');

test('ключ занятия и голая дата различаются', () => {
  assert.equal(isSessionKey('2026-11-15-2000'), true);
  assert.equal(isSessionKey('2026-11-15'), false);
  assert.equal(keyDate('2026-11-15-2000'), '2026-11-15');
  assert.equal(keyStart('2026-11-15-0700'), '07:00');
  assert.equal(keyStart('2026-11-15'), null);
});

test('ключ из ответа API: занятие, у плана дня — дата', () => {
  assert.equal(planKey({ date: '2026-11-15', session_id: '2026-11-15-2000' }), '2026-11-15-2000');
  assert.equal(planKey({ date: '2026-11-15', session_id: null }), '2026-11-15');
  assert.equal(planKey({ date: '2026-11-15' }), '2026-11-15');
});

test('подписи: время занятия рядом с датой', () => {
  assert.equal(formatPlanKey('2026-11-15-2000'), 'Sun, Nov 15 · 20:00');
  assert.equal(formatPlanKey('2026-11-15'), 'Sun, Nov 15');
  assert.equal(shortPlanKey('2026-11-15-0700'), 'Sun 15 · 07:00');
});

test('ключи сортируются строкой: день, утро, вечер, следующий день', () => {
  const keys = ['2026-11-16', '2026-11-15-2000', '2026-11-15', '2026-11-15-0700'];
  assert.deepEqual([...keys].sort(), ['2026-11-15', '2026-11-15-0700', '2026-11-15-2000', '2026-11-16']);
});

test('план по умолчанию: ближайшее занятие с сегодня, иначе последний, иначе сегодня', () => {
  // Список — новыми сверху, как отдаёт сервер.
  const plans = [
    { date: '2026-11-17', session_id: '2026-11-17-2000', status: 'published', lane_count: 6 },
    { date: '2026-11-15', session_id: '2026-11-15-2000', status: 'published', lane_count: 6 },
    { date: '2026-11-15', session_id: '2026-11-15-0700', status: 'published', lane_count: 6 },
    { date: '2026-11-10', session_id: null, status: 'published', lane_count: 4 },
  ];
  assert.equal(pickDefaultKey(plans, '2026-11-15'), '2026-11-15-0700');
  assert.equal(pickDefaultKey(plans, '2026-11-20'), '2026-11-17-2000');
  assert.equal(pickDefaultKey(plans.slice(3), '2026-11-20'), '2026-11-10');
  assert.equal(pickDefaultKey([], '2026-11-20'), '2026-11-20');
});
