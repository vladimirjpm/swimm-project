import { useCallback, useSyncExternalStore } from 'react';

/**
 * Совпадает ли медиазапрос сейчас — с подпиской на смену (поворот экрана, ресайз окна).
 *
 * Нужен там, где телефонный и десктопный виды — РАЗНЫЕ деревья, а не одно дерево с разными
 * классами (таб Trainings группы: на телефоне переключатель видов, на десктопе две колонки).
 * Скрыть лишнее дерево CSS-ом нельзя: оно всё равно смонтируется и сходит за своими данными.
 */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback((onChange: () => void) => {
    const mq = window.matchMedia(query);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, [query]);
  return useSyncExternalStore(subscribe, () => window.matchMedia(query).matches, () => false);
}

/** Десктоп страниц семьи deep — та же граница, что у липкой полосы и шапки сущности. */
export const DEEP_DESKTOP_QUERY = '(min-width: 960px)';
