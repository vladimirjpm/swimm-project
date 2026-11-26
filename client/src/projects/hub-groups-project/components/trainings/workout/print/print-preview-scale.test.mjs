import '../ts-resolve-hook.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
const { previewScale, sheetSize } = await import('./print-preview-scale.ts');

test('sheetSize: книжный A4 и альбомный', () => {
  assert.deepEqual(sheetSize('portrait'), { width: 794, height: 1123 });
  assert.deepEqual(sheetSize('landscape'), { width: 1123, height: 794 });
});

test('previewScale: просторная область — натуральная величина, не крупнее', () => {
  assert.equal(previewScale(2000, 2000, 'portrait'), 1);
});

test('previewScale: узкий телефон — по ширине', () => {
  assert.ok(Math.abs(previewScale(397, 2000, 'portrait') - 0.5) < 1e-9);
});

test('previewScale: низкое окно — по высоте', () => {
  assert.ok(Math.abs(previewScale(2000, 561.5, 'portrait') - 0.5) < 1e-9);
});

test('previewScale: область не замерена — 1', () => {
  assert.equal(previewScale(0, 0, 'portrait'), 1);
});

test('previewScale: крошечная область не даёт нуль', () => {
  assert.equal(previewScale(1, 1, 'portrait'), 0.1);
});

test('previewScale: NaN (замер не удался) — 1, а не NaN в стиле', () => {
  assert.equal(previewScale(NaN, 500, 'portrait'), 1);
});
