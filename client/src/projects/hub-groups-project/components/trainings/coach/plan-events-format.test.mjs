// Юнит-тесты строк ленты «Since publishing» (CF8, docs/plans/trainings-coach-flow-plan.md).
// Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { changesCount, changesLabel, eventText, eventTime } from './plan-events-format.ts';

const base = {
  id: 1, at: '2026-11-17T19:50:00Z', swimmer_id: 5, name: 'עדי', name_en: 'Adi', from_lane: null, to_lane: null,
  on_lanes: null, by_coach: false, actor_name: 'Dana', by_me: false,
};
const line = (e) => { const t = eventText({ ...base, ...e }); return `${t.before}${t.name}${t.after}`; };

test('публикация: кто и сколько на дорожках', () => {
  assert.equal(line({ kind: 'published', swimmer_id: null, name: '', on_lanes: 8, by_me: true }), 'You published · 8 on lanes');
  assert.equal(line({ kind: 'published', swimmer_id: null, name: '', on_lanes: 8 }), 'Dana published · 8 on lanes');
});

test('ответы: сам человек или тренер за него', () => {
  assert.equal(line({ kind: 'going', to_lane: 3 }), 'עדי answered Going → lane 3');
  assert.equal(line({ kind: 'going', to_lane: 0 }), 'עדי answered Going → not on a lane yet');
  assert.equal(line({ kind: 'going', to_lane: 2, by_coach: true, by_me: true }), 'You marked עדי going → lane 2');
  assert.equal(line({ kind: 'cant_come', from_lane: 1 }), "עדי can't come → off lane 1");
  assert.equal(line({ kind: 'cant_come', from_lane: 0, by_coach: true }), "Dana marked עדי can't come → off the plan");
});

test('переносы тренера', () => {
  assert.equal(line({ kind: 'moved', from_lane: 1, to_lane: 4, by_me: true }), 'You moved עדי lane 1 → lane 4');
  assert.equal(line({ kind: 'moved', from_lane: 2, to_lane: 0, by_me: true }), 'You moved עדי lane 2 → Unassigned');
  assert.equal(line({ kind: 'moved', from_lane: 2, to_lane: null, by_me: true }), 'You took עדי off the plan');
  assert.equal(line({ kind: 'moved', from_lane: null, to_lane: 3, actor_name: null }), 'Someone put עדי on lane 3');
  // Нет ивритского имени — английское.
  assert.equal(line({ kind: 'moved', from_lane: null, to_lane: 0, name: '', by_me: true }), 'You put Adi in Unassigned');
});

test('счётчик изменений не считает публикацию', () => {
  const events = [{ ...base, kind: 'going' }, { ...base, kind: 'moved' }, { ...base, kind: 'published' }];
  assert.equal(changesCount(events), 2);
  assert.equal(changesLabel(2), '2 changes');
  assert.equal(changesLabel(1), '1 change');
  assert.equal(changesLabel(0), 'No changes');
});

test('время по Израилю; не сегодня — с днём недели', () => {
  const now = new Date('2026-11-17T20:30:00Z'); // 22:30 в Израиле, вторник
  assert.equal(eventTime('2026-11-17T19:50:00Z', now), '21:50');
  assert.equal(eventTime('2026-11-16T05:40:00Z', now), 'Mon 07:40');
});
