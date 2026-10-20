import React from 'react';

/*
 * Хуки раскладки календаря: какой лейаут рисовать (десктоп 9a или телефон) и где кончается
 * липкий топбар. Без данных календаря — их можно брать в любой блок.
 */

/** Ширина, с которой календарь — двухколоночный лейаут 9a (хендофф competitions_home-3, `lg`). */
const DESKTOP_QUERY = '(min-width: 1024px)';

/** Окно ≥ 1024px. Подписан на смену ширины — лейаут переключается без перезагрузки. */
export function useIsDesktop(): boolean {
  const [matches, setMatches] = React.useState(() => window.matchMedia(DESKTOP_QUERY).matches);
  React.useEffect(() => {
    const mq = window.matchMedia(DESKTOP_QUERY);
    const on = () => setMatches(mq.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, []);
  return matches;
}

/** Высота липкого топбара сайта (`data-app-topbar`); нет его — 0. Меряем, а не хардкодим 46px. */
export function topbarHeight(): number {
  return document.querySelector<HTMLElement>('[data-app-topbar]')?.offsetHeight ?? 0;
}

/**
 * То же как состояние — для `top` липких блоков; пересчёт на resize (топбар бывает в 2 строки).
 * Меряем в layout-эффекте, не в инициализаторе: во время первого рендера топбара ещё нет в DOM.
 */
export function useTopbarHeight(): number {
  const [h, setH] = React.useState(0);
  React.useLayoutEffect(() => {
    const on = () => setH(topbarHeight());
    on();
    window.addEventListener('resize', on);
    return () => window.removeEventListener('resize', on);
  }, []);
  return h;
}

/**
 * Прокрутить окно так, чтобы элемент встал под топбар (с зазором `gap`).
 * `onlyIfAbove` — только если верх элемента ушёл под топбар (пользователь прокрутил ниже):
 * смена дня/месяца не должна дёргать страницу, когда блок и так виден.
 * `prefers-reduced-motion` — без анимации.
 */
export function scrollUnderTopbar(el: HTMLElement, { gap = 12, onlyIfAbove = false } = {}): void {
  const offset = topbarHeight() + gap;
  const top = el.getBoundingClientRect().top;
  if (onlyIfAbove && top >= offset) return;
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  window.scrollTo({ top: top + window.scrollY - offset, behavior: reduced ? 'auto' : 'smooth' });
}
