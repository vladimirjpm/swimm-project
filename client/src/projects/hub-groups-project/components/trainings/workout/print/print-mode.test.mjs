// Юнит-тесты механики печати (Ш2, docs/plans/workout-print-plan.md §3.2, §9) на поддельном DOM:
// корень — прямой ребёнок body, класс на <html>, @page ставится перед печатью и убирается на
// afterprint и при закрытии, повторная печать, закрытие без следов.
// Запуск: `npm --prefix client test`.

import test from 'node:test';
import assert from 'node:assert/strict';
import { openPrintSession, installPageStyle, pageRule, PAGE_STYLE_ID, PRINT_ON_CLASS, PRINT_ROOT_ID } from './print-mode.ts';

/** Минимум DOM, которым пользуется print-mode.ts. */
function fakeWindow() {
  const byId = new Map();
  const makeEl = (tag) => {
    const el = {
      tagName: tag.toUpperCase(),
      parent: null,
      children: [],
      textContent: '',
      _id: '',
      get id() { return this._id; },
      set id(v) { this._id = v; byId.set(v, this); },
      appendChild(child) { child.parent = this; this.children.push(child); return child; },
      remove() {
        if (!this.parent) return;
        this.parent.children = this.parent.children.filter((c) => c !== this);
        this.parent = null;
        if (byId.get(this._id) === this) byId.delete(this._id);
      },
    };
    return el;
  };
  const classes = new Set();
  const doc = {
    documentElement: { classList: { add: (c) => classes.add(c), remove: (c) => classes.delete(c), contains: (c) => classes.has(c) } },
    head: makeEl('head'),
    body: makeEl('body'),
    createElement: makeEl,
    getElementById: (id) => byId.get(id) ?? null,
  };
  const listeners = new Map();
  const win = {
    document: doc,
    printCalls: 0,
    print() { this.printCalls++; },
    addEventListener(type, fn) { (listeners.get(type) ?? listeners.set(type, new Set()).get(type)).add(fn); },
    removeEventListener(type, fn) { listeners.get(type)?.delete(fn); },
    dispatch(type) { for (const fn of [...(listeners.get(type) ?? [])]) fn(); },
    listenerCount: () => [...listeners.values()].reduce((n, s) => n + s.size, 0),
  };
  return { win, doc };
}

const pageStyle = (doc) => doc.getElementById(PAGE_STYLE_ID);

test('pageRule: A4 без полей, ориентация', () => {
  assert.equal(pageRule('portrait'), '@page { size: A4 portrait; margin: 0; }');
  assert.equal(pageRule('landscape'), '@page { size: A4 landscape; margin: 0; }');
});

test('сеанс: корень — прямой ребёнок body, класс на <html>, @page ещё нет', () => {
  const { win, doc } = fakeWindow();
  const s = openPrintSession(win);
  assert.equal(s.root.id, PRINT_ROOT_ID);
  assert.equal(s.root.parent, doc.body);
  assert.ok(doc.documentElement.classList.contains(PRINT_ON_CLASS));
  assert.equal(pageStyle(doc), null, '@page ставится только перед печатью');
});

test('print(): ставит @page в head и зовёт window.print', () => {
  const { win, doc } = fakeWindow();
  const s = openPrintSession(win);
  s.print();
  assert.equal(win.printCalls, 1);
  const style = pageStyle(doc);
  assert.ok(style);
  assert.equal(style.parent, doc.head);
  assert.equal(style.textContent, pageRule('portrait'));
});

test('afterprint убирает @page; повторная печать ставит снова', () => {
  const { win, doc } = fakeWindow();
  const s = openPrintSession(win);
  s.print();
  win.dispatch('afterprint');
  assert.equal(pageStyle(doc), null);
  assert.ok(doc.documentElement.classList.contains(PRINT_ON_CLASS), 'окно ещё открыто — класс остаётся');
  s.print();
  assert.equal(win.printCalls, 2);
  assert.ok(pageStyle(doc));
});

test('повторная печать без afterprint (Android: print() возвращается сразу) — один стиль', () => {
  const { win, doc } = fakeWindow();
  const s = openPrintSession(win);
  s.print();
  s.print();
  assert.equal(doc.head.children.filter((c) => c.id === PAGE_STYLE_ID).length, 1);
});

test('Ctrl+P при открытом окне: beforeprint ставит @page', () => {
  const { win, doc } = fakeWindow();
  openPrintSession(win, 'landscape');
  win.dispatch('beforeprint');
  assert.equal(pageStyle(doc).textContent, pageRule('landscape'));
});

test('close(): ни корня, ни класса, ни @page, ни слушателей; повторный close и print — ничего', () => {
  const { win, doc } = fakeWindow();
  const s = openPrintSession(win);
  s.print();
  s.close();
  assert.equal(doc.getElementById(PRINT_ROOT_ID), null);
  assert.equal(doc.body.children.length, 0);
  assert.ok(!doc.documentElement.classList.contains(PRINT_ON_CLASS));
  assert.equal(pageStyle(doc), null);
  assert.equal(win.listenerCount(), 0);
  s.close();
  s.print();
  assert.equal(win.printCalls, 1);
  win.dispatch('beforeprint');
  assert.equal(pageStyle(doc), null, 'после закрытия Ctrl+P печатает страницу как раньше');
});

test('новый сеанс забирает забытый корень, а не плодит второй', () => {
  const { win, doc } = fakeWindow();
  openPrintSession(win);
  const s2 = openPrintSession(win);
  assert.equal(doc.body.children.filter((c) => c.id === PRINT_ROOT_ID).length, 1);
  assert.equal(doc.getElementById(PRINT_ROOT_ID), s2.root);
});

test('installPageStyle: обновляет ориентацию в том же <style>', () => {
  const { doc } = fakeWindow();
  installPageStyle(doc, 'portrait');
  installPageStyle(doc, 'landscape');
  assert.equal(doc.head.children.length, 1);
  assert.equal(pageStyle(doc).textContent, pageRule('landscape'));
});
