import { useEffect, useState } from 'react';

/**
 * Шов «кто сейчас занимает верх экрана».
 *
 * Компакт-бар шапки соревнования (`competition-sticky-bar.tsx`) — не единственное липкое
 * на странице: у таба Start list есть своя липкая зона фильтров, и когда она садится под
 * шапку, шапка убирается совсем (решение Влада 31.08.2026) — двух липких панелей друг на
 * друге на телефоне не остаётся места. Вернуться наверх оттуда можно кнопкой «↑», которую
 * зона показывает вместо шапки.
 *
 * Почему отдельный модуль, а не проп: бар живёт в шапке соревнования, зона — внутри
 * контента таба, и общего родителя, который знал бы про обоих, между ними нет (между ними
 * `DataSourceDDL` с его `renderHeader`). Протаскивать флаг через четыре слоя ради одного
 * булева — хуже, чем один явный шов на модуль.
 *
 * Флаг ОДИН на страницу: липкая зона в один момент времени тоже одна (таб открыт один).
 * Кто выставил — тот и снимает, обязательно в cleanup: уход с таба не должен оставить
 * шапку выключенной навсегда.
 */

let suppressed = false;
const listeners = new Set<(value: boolean) => void>();

/** Спрятать/вернуть компакт-бар шапки. Вызывать только из эффекта и снимать в cleanup. */
export function setStickyBarSuppressed(next: boolean): void {
  if (suppressed === next) return;
  suppressed = next;
  listeners.forEach((fn) => fn(next));
}

/** Спрятан ли сейчас компакт-бар шапки. */
export function useStickyBarSuppressed(): boolean {
  const [value, setValue] = useState(suppressed);
  useEffect(() => {
    // Значение могло измениться между рендером и подпиской — синхронизируемся ещё раз.
    setValue(suppressed);
    listeners.add(setValue);
    return () => { listeners.delete(setValue); };
  }, []);
  return value;
}
