// Юнит-тесты замера «листа строк» без браузера (хендофф 27.11 §3, §6): поддельные строки, у которых
// высота текста рядом с дистанцией и ширина слова зависят от шрифта. Запуск: `npm --prefix client test`.

import '../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const { applyBelowRule, fitRowsSheet, ROW_BELOW_ATTR, ROW_DIST_ATTR, ROW_LINE_ATTR, ROW_SET_ATTR, ROW_TEXT_ATTR } = await import('./rows-fit-measure.ts');
const { FIT_AREA_ATTR, FIT_BOX_ATTR, FIT_X_ATTR } = await import('./sheet-fit-measure.ts');
const { F_FLOOR, F_MAX } = await import('./print-fit.ts');

/**
 * Поддельная строка: `textLines(below)` — сколько строк займёт текст (рядом / под дистанцией),
 * `wordW` — ширина самого длинного слова против колонки 100 px (рядом) / 300 px (под).
 */
function fakeLine(key, { textLines = () => 1, wordW = 50, dist = true } = {}) {
  const attrs = new Map([[ROW_LINE_ATTR, key]]);
  const below = () => attrs.has(ROW_BELOW_ATTR);
  const text = {
    getBoundingClientRect: () => ({ height: 10 * textLines(below()) }),
    get scrollWidth() { return wordW; },
    get clientWidth() { return below() ? 300 : 100; },
  };
  const distEl = { getBoundingClientRect: () => ({ height: 10 }) };
  return {
    getAttribute: (n) => attrs.get(n) ?? null,
    setAttribute: (n, v) => attrs.set(n, v),
    removeAttribute: (n) => attrs.delete(n),
    querySelector: (sel) => (sel === `[${ROW_TEXT_ATTR}]` ? text : sel === `[${ROW_DIST_ATTR}]` && dist ? distEl : null),
    isBelow: below,
  };
}

test('applyBelowRule: 3+ строк рядом или слово шире колонки — под дистанцию; 2 строки — рядом', () => {
  const two = fakeLine('1.0', { textLines: () => 2 });
  const three = fakeLine('1.1', { textLines: (b) => (b ? 1 : 3) });
  const wide = fakeLine('1.2', { wordW: 150 });
  const noDist = fakeLine('1.3', { textLines: () => 5, dist: false });
  assert.deepEqual(applyBelowRule([two, three, wide, noDist]), ['1.1', '1.2']);
  assert.equal(two.isBelow(), false);
  assert.equal(three.isBelow(), true);
  assert.equal(noDist.isBelow(), false, 'пояснение без дистанции правила не знает');
});

test('applyBelowRule: пересчёт с нуля на каждом шрифте — метка снимается, если стало помещаться', () => {
  let lines = 3;
  const l = fakeLine('2.0', { textLines: () => lines });
  assert.deepEqual(applyBelowRule([l]), ['2.0']);
  lines = 1;
  assert.deepEqual(applyBelowRule([l]), []);
  assert.equal(l.isBelow(), false);
});

/**
 * Поддельный лист: высота содержимого = perPx·f; строка уходит под дистанцию при f > `belowFrom`;
 * правый край сета — `setRightAt(f)` против области 0…718.
 */
function fakeSheet({ perPx, belowFrom = Infinity, wordAt = () => 50, setRightAt = () => 718 }) {
  const box = {
    style: { fontSize: '' },
    get f() { return parseFloat(this.style.fontSize); },
    getBoundingClientRect() { return { height: perPx * this.f }; },
    scrollWidth: 700,
  };
  const area = { clientWidth: 718, getBoundingClientRect: () => ({ height: 1046.5, left: 0, right: 718 }) };
  const line = fakeLine('1.0', { textLines: (b) => (b ? 1 : box.f > belowFrom ? 3 : 1) });
  const word = { get scrollWidth() { return wordAt(box.f); }, clientWidth: 300 };
  const set = { getBoundingClientRect: () => ({ left: 0, right: setRightAt(box.f) }) };
  const all = { [ROW_LINE_ATTR]: [line], [FIT_X_ATTR]: [word], [ROW_SET_ATTR]: [set] };
  return {
    querySelector: (sel) => (sel === `[${FIT_AREA_ATTR}]` ? area : sel === `[${FIT_BOX_ATTR}]` ? box : null),
    querySelectorAll: (sel) => all[sel.slice(1, -1)] ?? [],
  };
}

test('fitRowsSheet: сет вылез за область на доли пикселя (колонка текста сжата до нуля) — не влезло', () => {
  const r = fitRowsSheet(fakeSheet({ perPx: 1, setRightAt: (f) => (f > 60 ? 718.7 : 718.3) }));
  assert.equal(r.size, 59.7);
});

test('fitRowsSheet: наибольший размер по высоте; строки «под» — при ВЫБРАННОМ размере', () => {
  const r = fitRowsSheet(fakeSheet({ perPx: 30, belowFrom: 20 }));
  assert.equal(r.size, 34.7); // шаг 0.5 от 10.7: 34.7·30 = 1041 ≤ 1046.5, 35.2·30 = 1056 — нет
  assert.equal(r.overflow, false);
  assert.deepEqual(r.below, ['1.0']);
  const small = fitRowsSheet(fakeSheet({ perPx: 60, belowFrom: 20 }));
  assert.ok(small.size < 20, String(small.size));
  assert.deepEqual(small.below, [], 'мелкий шрифт — текст рядом');
});

test('fitRowsSheet: слово не влезает и под дистанцией — шрифт меньше; короткая — F_MAX; огромная — пол с overflow', () => {
  const word = fitRowsSheet(fakeSheet({ perPx: 1, wordAt: (f) => f * 10 }));
  assert.ok(word.size <= 30 && word.size > 29, String(word.size));
  assert.equal(fitRowsSheet(fakeSheet({ perPx: 1 })).size, F_MAX);
  const huge = fitRowsSheet(fakeSheet({ perPx: 1000 }));
  assert.equal(huge.size, F_FLOOR);
  assert.equal(huge.overflow, true);
  assert.equal(fitRowsSheet({ querySelector: () => null, querySelectorAll: () => [] }), null);
});
