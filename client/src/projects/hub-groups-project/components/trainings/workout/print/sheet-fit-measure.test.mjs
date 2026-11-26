// Юнит-тесты замера листа печати без браузера (docs/plans/workout-print-plan.md §3.5): поддельная
// «копия листа», у которой высота содержимого растёт с основным шрифтом, а куб может вылезти по
// ширине. Запуск: `npm --prefix client test`.

import '../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const { sheetFits, fitVariants, sheetVariants, variantKey, FIT_AREA_ATTR, FIT_BOX_ATTR, FIT_X_ATTR } = await import('./sheet-fit-measure.ts');
const { F_FLOOR, F_MAX, F_READABLE } = await import('./print-fit.ts');

const AREA_H = 1046.5;

/**
 * Поддельный лист: высота содержимого = `perPx` · f (+ ступенька переноса), ширина куба при f —
 * `cubeW(f)` против 300 px. Возвращает элемент с `querySelector(All)` по атрибутам FIT_*.
 */
function fakeSheet({ perPx, cubeW = () => 100, noBox = false }) {
  const box = {
    style: { fontSize: '' },
    get f() { return parseFloat(this.style.fontSize); },
    getBoundingClientRect() { return { height: perPx * this.f }; },
    get scrollWidth() { return 700; },
  };
  const area = { clientWidth: 718, getBoundingClientRect: () => ({ height: AREA_H }) };
  const cube = { get scrollWidth() { return cubeW(box.f); }, clientWidth: 300 };
  return {
    querySelector: (sel) => (sel === `[${FIT_AREA_ATTR}]` ? area : sel === `[${FIT_BOX_ATTR}]` && !noBox ? box : null),
    querySelectorAll: (sel) => (sel === `[${FIT_X_ATTR}]` ? [cube] : []),
  };
}

test('sheetFits: по высоте области; по ширине куба; нет разметки — null', () => {
  const fits = sheetFits(fakeSheet({ perPx: 30 }));
  assert.equal(fits(34), true); // 1020 ≤ 1046.5
  assert.equal(fits(35), false); // 1050 > 1046.5
  const wide = sheetFits(fakeSheet({ perPx: 1, cubeW: (f) => f * 10 }));
  assert.equal(wide(30), true);
  assert.equal(wide(31), false, 'куб 310 px в 300 px — не влезло, хотя по высоте место есть');
  assert.equal(sheetFits(fakeSheet({ perPx: 1, noBox: true })), null);
});

test('fitVariants: «главный справа» крупнее порога — она; ниже порога и кубы крупнее — кубы', () => {
  const sheets = { main: fakeSheet({ perPx: 28 }), 'cubes-1': fakeSheet({ perPx: 40 }), 'cubes-2': fakeSheet({ perPx: 30 }) };
  const variants = [{ layout: 'main' }, { layout: 'cubes', columns: 1 }, { layout: 'cubes', columns: 2 }];
  const choice = fitVariants(variants, (v) => sheets[variantKey(v)]);
  assert.equal(choice.layout, 'main');
  assert.ok(choice.size >= 37 && choice.size <= 37.5, String(choice.size));

  const small = { main: fakeSheet({ perPx: 70 }), 'cubes-1': fakeSheet({ perPx: 50 }), 'cubes-2': fakeSheet({ perPx: 40 }) };
  const c2 = fitVariants(variants, (v) => small[variantKey(v)]);
  assert.ok(70 * F_READABLE > AREA_H, 'главная ниже 14 pt');
  assert.equal(c2.layout, 'cubes');
  assert.equal(c2.columns, 2);
});

test('fitVariants: ничего не влезает и на полу — кубы по 3, overflow', () => {
  const huge = fakeSheet({ perPx: 1000 });
  const variants = [{ layout: 'cubes', columns: 1 }, { layout: 'cubes', columns: 2 }, { layout: 'cubes', columns: 3 }];
  const c = fitVariants(variants, () => huge);
  assert.deepEqual(c, { layout: 'cubes', size: F_FLOOR, overflow: true, columns: 3 });
});

test('fitVariants: мало текста — упирается в F_MAX; копий ещё нет — null', () => {
  const tiny = fakeSheet({ perPx: 1 });
  assert.equal(fitVariants([{ layout: 'cubes', columns: 1 }], () => tiny).size, F_MAX);
  assert.equal(fitVariants([{ layout: 'cubes', columns: 1 }], () => null), null);
});

test('sheetVariants: «главный справа» — только когда есть; кубов в ряду не больше, чем сетов', () => {
  const cubes = (n) => Array.from({ length: n }, (_, i) => ({ no: i + 1 }));
  const keys = (m) => sheetVariants(m).map(variantKey);
  assert.deepEqual(keys({ mainLayout: null, cubes: cubes(5) }), ['cubes-1', 'cubes-2', 'cubes-3']);
  assert.deepEqual(keys({ mainLayout: { left: [], right: [] }, cubes: cubes(5) }), ['main', 'cubes-1', 'cubes-2', 'cubes-3']);
  assert.deepEqual(keys({ mainLayout: null, cubes: cubes(2) }), ['cubes-1', 'cubes-2']);
  assert.deepEqual(keys({ mainLayout: null, cubes: [] }), ['cubes-1']);
});
