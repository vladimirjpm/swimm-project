// Юнит-тесты выбора дорожки превью (режим «👁 View» управляющего). Запуск: `npm --prefix client test`.
import test from 'node:test';
import assert from 'node:assert/strict';
import { ownLaneNo, previewLaneNo } from './lane-preview.ts';

const lanes = [
  { lane_no: 1, people: [] },
  { lane_no: 2, people: [{}, {}] },
  { lane_no: 3, people: [{}, { is_me: true }] },
];

test('своя дорожка — где is_me', () => {
  assert.equal(ownLaneNo(lanes), 3);
  assert.equal(ownLaneNo([{ lane_no: 1, people: [{}] }]), null);
});

test('превью: выбранная, иначе своя, иначе первая с людьми, иначе первая', () => {
  assert.equal(previewLaneNo(lanes, 2), 2);
  assert.equal(previewLaneNo(lanes, null), 3);
  assert.equal(previewLaneNo(lanes, 7), 3, 'выбранной дорожки больше нет — своя');
  assert.equal(previewLaneNo(lanes.slice(0, 2), null), 2);
  assert.equal(previewLaneNo([{ lane_no: 1, people: [] }], null), 1);
  assert.equal(previewLaneNo([], 2), null);
});
