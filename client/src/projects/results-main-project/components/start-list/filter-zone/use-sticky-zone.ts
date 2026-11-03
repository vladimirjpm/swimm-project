import type React from 'react';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
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
export function useStickyZone(zoneRef: React.RefObject<HTMLElement>) {
  const [stuck, setStuck] = useState(false);
  const [topbarH, setTopbarH] = useState(0);
  const sentinelRef = useRef<HTMLDivElement>(null);
  const stuckRef = useRef(false);

  // Высота зоны РАЗВЁРНУТОЙ (снята в момент прилипания) и СВЁРНУТОЙ (снята после).
  const [expandedH, setExpandedH] = useState(0);
  const [collapsedH, setCollapsedH] = useState<number | null>(null);

  useEffect(() => {
    const onScroll = () => {
      const topbar = document.querySelector('[data-app-topbar]');
      const h = Math.round(topbar?.getBoundingClientRect().height ?? 0);
      setTopbarH(h);
      const sentinel = sentinelRef.current;
      if (!sentinel) return;
      // +1px — запас на дробные пиксели зума: ровно на границе рект даёт 45.99.
      const next = sentinel.getBoundingClientRect().top <= h + 1;
      // Мерить развёрнутую можно только ДО свёртки — в том же батче, что и `stuck`.
      if (next && !stuckRef.current && zoneRef.current) {
        setExpandedH(zoneRef.current.getBoundingClientRect().height);
      }
      stuckRef.current = next;
      setStuck(next);
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

  // Свёрнутую меряем сразу после коммита, до отрисовки; дальше — если она поменяется
  // (поиск, ширина окна). Отлипли — сбрасываем.
  useLayoutEffect(() => {
    const zone = zoneRef.current;
    if (!stuck || !zone) {
      setCollapsedH(null);
      return;
    }
    const measure = () => setCollapsedH(zone.getBoundingClientRect().height);
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(zone);
    return () => ro.disconnect();
  }, [stuck, zoneRef]);

  // РАСПОРКА под прилипшей зоной: свёрнутая зона ниже развёрнутой, и без распорки на
  // столько же укорачивается страница. На коротком плане (1–2 заплыва) это петля
  // (замечание Влада 03.11.2026): прилипла → свернулась → документ короче → браузер
  // обрезал scrollY → сентинел ниже порога → отлипла → развернулась → … каждые два кадра.
  // С распоркой высота документа от `stuck` не зависит, а протокол под зоной не прыгает.
  // Пока свёрнутая не измерена (кадр коммита), резервируем ВСЮ развёрнутую высоту:
  // перебор только удлиняет документ, а недобор успел бы обрезать скролл.
  const reserve = stuck ? Math.max(0, expandedH - (collapsedH ?? 0)) : 0;

  return { stuck, sentinelRef, top, reserve };
}
