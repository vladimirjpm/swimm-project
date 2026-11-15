import { useSyncExternalStore } from 'react';

/**
 * Слот содержимого в нижней панели инструментов каркаса (`.deep-tabs-tools`, на телефоне —
 * закреплённая панель «Coach tools» у низа экрана). Решение Влада 14.11.2026
 * (docs/plans/trainings-coach-flow-plan.md, CF3): активному табу иногда нужна своя строка в ЭТОЙ ЖЕ
 * панели (статус плана и Publish рабочего стола тренера) — вторую фиксированную панель не заводим.
 *
 * Узел слота рисует `DeepTabs` над кнопками инструментов и отдаёт сюда; таб кладёт в него
 * содержимое порталом (`createPortal(node, useDockSlot())`). Пустой слот места не занимает, а
 * отступ страницы снизу растёт сам (`.deep-entity-main--dock:has(...)` в deep-theme.css).
 * Панель закреплена только на телефоне (`DEEP_DOCK_QUERY`): шире слот скрыт, и содержимое таб
 * показывает у себя.
 */

/** Та же граница, что у закреплённой панели в deep-theme.css. */
export const DEEP_DOCK_QUERY = '(max-width: 639px)';

let slot: HTMLElement | null = null;
const listeners = new Set<() => void>();

/** Ref-колбэк узла слота — ставит `DeepTabs`. */
export function setDockSlotNode(node: HTMLElement | null): void {
  if (slot === node) return;
  slot = node;
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}

/** Узел слота нижней панели; `null` — панели на странице нет (у зрителя нет инструментов). */
export function useDockSlot(): HTMLElement | null {
  return useSyncExternalStore(subscribe, () => slot, () => null);
}

/**
 * Высота закреплённой панели сейчас, px (со слотом и безопасной зоной); 0 — панели нет или она не
 * закреплена. Нужна тому, что всплывает НАД панелью (тост «Undo»).
 */
export function dockHeight(): number {
  const dock = slot?.parentElement;
  if (!dock || window.getComputedStyle(dock).position !== 'fixed') return 0;
  return dock.offsetHeight;
}
