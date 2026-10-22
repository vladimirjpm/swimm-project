import { useEffect, useRef, useState } from 'react';
import { setStickyBarSuppressed } from '../../competition-header/sticky-chrome';

/**
 * Прилипла ли зона фильтров. Отсюда три следствия сразу (решения Влада 31.08.2026):
 *
 * 1. чипы сессий и строка людей сворачиваются в ОДНУ текстовую строку — пока зона стоит
 *    на своём месте, это органы управления, а когда висит поверх протокола, это справка;
 * 2. компакт-бар шапки соревнования гаснет совсем (`setStickyBarSuppressed`) — двум
 *    липким панелям друг на друге на телефоне места нет;
 * 3. внизу появляется кнопка «↑» — короткий путь наверх, раз шапки на экране больше нет.
 *
 * Меряем по СЕНТИНЕЛУ — пустому блоку в обычном потоке прямо перед зоной, — а не по самой
 * зоне: у прилипшей зоны `rect.top` навсегда равен смещению прилипания, и она никогда не
 * отлипла бы. Порог — высота топбара, а не `--comp-sticky-chrome-h`: переменная зависит от
 * того, виден ли бар, а бар мы этим же флагом и гасим — вышли бы качели на каждом кадре.
 */
export function useStickyZone() {
  const [stuck, setStuck] = useState(false);
  const [topbarH, setTopbarH] = useState(0);
  const sentinelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const onScroll = () => {
      const topbar = document.querySelector('[data-app-topbar]');
      const h = Math.round(topbar?.getBoundingClientRect().height ?? 0);
      setTopbarH(h);
      const sentinel = sentinelRef.current;
      if (!sentinel) return;
      // +1px — запас на дробные пиксели зума: ровно на границе рект даёт 45.99.
      setStuck(sentinel.getBoundingClientRect().top <= h + 1);
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll);
    return () => {
      window.removeEventListener('scroll', onScroll);
      window.removeEventListener('resize', onScroll);
    };
  }, []);

  // Гасим шапку, пока зона прилипла. Снимать ОБЯЗАТЕЛЬНО в cleanup: уход с таба (или экран
  // «протокол не опубликован», где зоны нет вовсе) не должен оставить шапку выключенной.
  useEffect(() => {
    setStickyBarSuppressed(stuck);
    return () => setStickyBarSuppressed(false);
  }, [stuck]);

  // Где прилипать. `--comp-sticky-chrome-h` публикует компакт-бар шапки соревнования, но
  // на странице ПРЕДСТОЯЩЕГО старта (`/competitions/upcoming/…`) шапки нет — переменная не
  // задана, и зона липла к `top: 0`, уезжая под топбар вместе с «All programme / My plan»
  // (замечание Влада 22.10.2026). Запасной отступ — высота топбара.
  const top = `var(--comp-sticky-chrome-h, ${topbarH}px)`;

  return { stuck, sentinelRef, top };
}
