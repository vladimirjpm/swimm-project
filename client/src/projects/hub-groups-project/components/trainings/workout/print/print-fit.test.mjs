// Юнит-тесты геометрии и подгонки листа печати (docs/plans/workout-print-plan.md, §3.4–3.5, §8.1).
// Запуск: `npm --prefix client test`.

import '../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const {
  A4_HEIGHT, A4_WIDTH, COLUMN_WIDTH, CONTENT_HEIGHT, CONTENT_WIDTH, F_FLOOR, F_MAX, F_READABLE, F_STEP,
  chooseLayout, cubeGrid, cubeWidth, layoutCaption, pickCubeColumns, pxToPt, searchFontSize, sheetsCaption,
} = await import('./print-fit.ts');

test('размеры листа: A4 794×1123, рабочая область 718×1047, колонка 347; пороги 8 / 14 / 72 pt', () => {
  assert.equal(A4_WIDTH, 794);
  assert.equal(A4_HEIGHT, 1123);
  assert.equal(CONTENT_WIDTH, 718);
  assert.equal(CONTENT_HEIGHT, 1047);
  assert.equal(COLUMN_WIDTH, 347);
  assert.equal(Math.round(pxToPt(F_MAX)), 72);
  assert.equal(Math.round(pxToPt(F_READABLE)), 14);
  assert.equal(Math.round(pxToPt(F_FLOOR)), 8);
  // три куба в ряду: два промежутка по 1.6em
  assert.equal(cubeWidth(1, 20), 718);
  assert.equal(cubeWidth(3, 10), (718 - 2 * 16) / 3);
});

/** Поддельный замер: высота растёт со «ступенькой» (перенос строки); влезает при высоте ≤ limit. */
const stepped = (limit, perPx = 3, jump = 20, every = 8) => (f) => perPx * f + jump * Math.floor(f / every) <= limit;

test('searchFontSize: самый крупный подходящий размер, с точностью шага', () => {
  const fits = stepped(300);
  const { size, overflow } = searchFontSize(fits);
  assert.equal(overflow, false);
  assert.ok(fits(size), 'найденный размер влезает');
  assert.ok(!fits(size + F_STEP), 'а на шаг больше — уже нет');
});

test('searchFontSize: никогда не возвращает неподходящий — при любом пределе', () => {
  for (let limit = 100; limit <= 1500; limit += 37) {
    const fits = stepped(limit);
    const r = searchFontSize(fits);
    if (!r.overflow) assert.ok(fits(r.size), `limit ${limit}: ${r.size}`);
    assert.ok(r.size >= F_FLOOR && r.size <= F_MAX);
  }
});

test('searchFontSize: упирается в F_MAX, если влезает всё', () => {
  let calls = 0;
  const r = searchFontSize(() => { calls += 1; return true; });
  assert.deepEqual(r, { size: F_MAX, overflow: false });
  assert.ok(calls <= 2, 'пол и потолок проверяются сразу, без поиска');
});

test('searchFontSize: ничего не подходит → пол и флаг «не влезло»', () => {
  assert.deepEqual(searchFontSize(() => false), { size: F_FLOOR, overflow: true });
});

test('searchFontSize: ~10 замеров, а не перебор', () => {
  let calls = 0;
  searchFontSize((f) => { calls += 1; return 10 * f <= 700; });
  assert.ok(calls <= 12, `замеров: ${calls}`);
});

test('searchFontSize: немонотонный замер — всё равно возвращает влезающий размер', () => {
  // «Дыра» в середине: поиск мог бы попасть в неё; страховка уводит вниз.
  const fits = (f) => f <= 40 && !(f > 30 && f < 33);
  const r = searchFontSize(fits);
  assert.ok(fits(r.size), `размер ${r.size}`);
});

test('searchFontSize: свои floor/max/step', () => {
  assert.deepEqual(searchFontSize((f) => f <= 25.3, { floor: 10, max: 40, step: 1 }), { size: 25, overflow: false });
});

const main = (size, overflow = false) => ({ size, overflow });
const cubes = (size, columns = 2, overflow = false) => ({ size, overflow, columns });

test('chooseLayout: есть главный и f_main ≥ 14 pt → «главный справа»', () => {
  const c = chooseLayout(main(30), cubes(40));
  assert.equal(c.layout, 'main');
  assert.equal(c.size, 30);
  assert.equal(chooseLayout(main(F_READABLE), cubes(40)).layout, 'main', 'ровно 14 pt ещё читаемо');
});

test('chooseLayout: главного нет (или все главные, или сет один) → кубы', () => {
  const c = chooseLayout(null, cubes(24, 3));
  assert.deepEqual(c, { layout: 'cubes', size: 24, overflow: false, columns: 3 });
});

test('chooseLayout: f_main < 14 pt, кубы крупнее → кубы', () => {
  assert.equal(chooseLayout(main(15), cubes(18)).layout, 'cubes');
});

test('chooseLayout: f_main < 14 pt, но кубы не крупнее → всё же «главный справа»', () => {
  assert.equal(chooseLayout(main(15), cubes(15)).layout, 'main');
  assert.equal(chooseLayout(main(15), cubes(12)).layout, 'main');
});

test('chooseLayout: «главный справа» не влезла даже на полу, кубы влезли → кубы', () => {
  assert.equal(chooseLayout(main(F_FLOOR, true), cubes(F_FLOOR + 3)).layout, 'cubes');
  assert.equal(chooseLayout(main(F_FLOOR, true), cubes(F_FLOOR, 3, true)).layout, 'main', 'обе не влезли — главная');
});

test('pickCubeColumns: самый крупный шрифт; при равенстве — меньше кубов в ряду', () => {
  assert.equal(pickCubeColumns([cubes(20, 1), cubes(26, 2), cubes(22, 3)]).columns, 2);
  assert.equal(pickCubeColumns([cubes(24, 1), cubes(24, 2), cubes(24, 3)]).columns, 1);
  assert.equal(pickCubeColumns([cubes(20, 1), cubes(24, 2), cubes(24, 3)]).columns, 2);
});

test('pickCubeColumns: не влезло нигде — k = 3 (обрезано меньше всего); влезло хоть где-то — он, а не пол', () => {
  const floor = (k) => cubes(10.7, k, true);
  assert.deepEqual(pickCubeColumns([floor(1), floor(2), floor(3)]), floor(3));
  assert.equal(pickCubeColumns([floor(1), floor(2)]).columns, 2);
  assert.equal(pickCubeColumns([floor(1), floor(2), cubes(10.7, 3)]).columns, 3);
  assert.equal(pickCubeColumns([floor(1), cubes(11, 2), cubes(12, 3)]).columns, 3);
});

test('layoutCaption: подписи предпросмотра', () => {
  assert.equal(layoutCaption({ layout: 'main', size: 37.5, overflow: false }), 'Main on the right · 28 pt');
  assert.equal(layoutCaption({ layout: 'cubes', size: 29.5, overflow: false, columns: 3 }), 'Blocks · 3 per row · 22 pt');
});

// ── cubeGrid ──────────────────────────────────────────────────────────────────────

const kinds = (g) => g.arrows.map((a) => a.kind);

test('cubeGrid: 5 кубов, k = 3, rtl — змейка от правого верхнего угла: ①②③ справа налево, ④⑤ слева направо', () => {
  const g = cubeGrid(5, 3, 'rtl');
  // индексы 0..4 = ①..⑤; в ряду — физические колонки слева направо
  assert.deepEqual(g.rows, [[2, 1, 0], [3, 4, null]]);
  assert.deepEqual(kinds(g), ['left', 'left', 'down', 'right']);
  // ▼ — под ③ (левая колонка первого ряда), горизонтальные — в промежутках
  assert.deepEqual(g.arrows[2], { from: 2, to: 3, kind: 'down', row: 0, col: 0 });
  assert.deepEqual(g.arrows[0], { from: 0, to: 1, kind: 'left', row: 0, col: 1 });
  assert.deepEqual(g.arrows[1], { from: 1, to: 2, kind: 'left', row: 0, col: 0 });
  assert.deepEqual(g.arrows[3], { from: 3, to: 4, kind: 'right', row: 1, col: 0 });
});

test('cubeGrid: ltr — зеркально', () => {
  const g = cubeGrid(5, 3, 'ltr');
  assert.deepEqual(g.rows, [[0, 1, 2], [null, 4, 3]]);
  assert.deepEqual(kinds(g), ['right', 'right', 'down', 'left']);
  assert.deepEqual(g.arrows[2], { from: 2, to: 3, kind: 'down', row: 0, col: 2 });
});

test('cubeGrid: ▼ всегда под кубом, откуда стрелка, и над тем, куда; горизонтальные — между соседями', () => {
  for (const dir of ['rtl', 'ltr']) {
    for (const k of [1, 2, 3]) {
      for (let n = 1; n <= 10; n += 1) {
        const g = cubeGrid(n, k, dir);
        assert.equal(g.arrows.length, n - 1);
        const at = (idx) => g.rows.flatMap((r, ri) => r.map((v, ci) => (v === idx ? { ri, ci } : null))).find(Boolean);
        for (const a of g.arrows) {
          const from = at(a.from);
          const to = at(a.to);
          if (a.kind === 'down') {
            assert.equal(to.ri, from.ri + 1);
            assert.equal(to.ci, from.ci, `${dir} n=${n} k=${k}: стрелка вниз не по прямой`);
            assert.equal(a.col, from.ci);
          } else {
            assert.equal(to.ri, from.ri);
            assert.equal(Math.abs(to.ci - from.ci), 1, 'соседи в ряду');
            assert.equal(a.kind, to.ci < from.ci ? 'left' : 'right');
          }
        }
      }
    }
  }
});

test('cubeGrid: k = 1 — только стрелки вниз; один куб — без стрелок', () => {
  const g = cubeGrid(4, 1, 'rtl');
  assert.deepEqual(g.rows, [[0], [1], [2], [3]]);
  assert.deepEqual(kinds(g), ['down', 'down', 'down']);
  assert.deepEqual(cubeGrid(1, 3, 'rtl').arrows, []);
  assert.deepEqual(cubeGrid(1, 3, 'rtl').rows, [[null, null, 0]], 'одинокий куб — у стороны начала (справа)');
  assert.deepEqual(cubeGrid(1, 2, 'ltr').rows, [[0, null]]);
  assert.deepEqual(cubeGrid(0, 3, 'rtl'), { columns: 3, rows: [], arrows: [] });
});

test('cubeGrid: неполный нижний ряд заполняется от стороны начала ряда', () => {
  // 4 куба, k = 3, rtl: второй ряд идёт слева направо и заполнен слева
  assert.deepEqual(cubeGrid(4, 3, 'rtl').rows, [[2, 1, 0], [3, null, null]]);
  // 7 кубов, k = 3: третий ряд — снова справа налево, один куб справа
  assert.deepEqual(cubeGrid(7, 3, 'rtl').rows, [[2, 1, 0], [3, 4, 5], [null, null, 6]]);
});

test('sheetsCaption: подпись вида с подгонкой по листам — «Numbers · 34 pt», «Two sides · 50 pt / 57 pt»', () => {
  assert.equal(sheetsCaption('Numbers', [45.2]), 'Numbers · 34 pt');
  assert.equal(sheetsCaption('Two sides', [66.2, 76.2]), 'Two sides · 50 pt / 57 pt');
});
