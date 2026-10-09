import React from 'react';

/**
 * Панель выбора поверх контента (кнопка «Change» в шапке) и мобильное меню категорий:
 * открытие, закрытие по Esc / клику вне, геометрия десктопного дропдауна, блок прокрутки
 * страницы под мобильным sheet.
 */
export const useSelectorPanel = (isMobile: boolean, wrapRef: React.RefObject<HTMLDivElement>) => {
  // Панель поверх контента при выбранном соревновании (кнопка «Change» в шапке)
  const [panelOpen, setPanelOpen] = React.useState(false);
  // Мобильное меню категорий (вариант 9b: строка «Category: … ▾» вместо табов)
  const [catMenuOpen, setCatMenuOpen] = React.useState(false);

  // Закрытие по Esc (всегда) и по клику вне (только desktop-дропдаун —
  // у мобильного sheet эту роль играет backdrop).
  React.useEffect(() => {
    if (!panelOpen) return;
    const onDown = (e: MouseEvent) => {
      if (isMobile) return;
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) setPanelOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPanelOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [panelOpen, isMobile]);

  // Панель закрылась → сворачиваем и меню категорий
  React.useEffect(() => {
    if (!panelOpen) setCatMenuOpen(false);
  }, [panelOpen]);

  /**
   * Геометрия десктопного дропдауна в координатах ВЬЮПОРТА (см. комментарий у разметки).
   *
   * `top` — под тем, что реально занимает верх экрана: под hero шапки, пока он виден, и
   * под липкой шапкой (`--comp-sticky-chrome-h`, её публикует компакт-бар соревнования),
   * когда hero уехал. Hero ищем по `data-comp-hero`, а не по «первому ребёнку»: разметку
   * шапки даёт чужой компонент через renderHeader, и опираться на её структуру нельзя.
   *
   * Пересчитываем на скролл и resize, пока панель открыта: она висит над страницей, и её
   * место меняется вместе с шапкой.
   */
  const [dropBox, setDropBox] = React.useState<
    { top: number; left: number; width: number; maxHeight: number } | null
  >(null);
  React.useLayoutEffect(() => {
    if (!panelOpen || isMobile) { setDropBox(null); return undefined; }
    const measure = () => {
      const wrap = wrapRef.current;
      if (!wrap) return;
      const wrapRect = wrap.getBoundingClientRect();
      const hero = wrap.querySelector('[data-comp-hero]');
      const heroBottom = hero ? hero.getBoundingClientRect().bottom : wrapRect.bottom;
      const chrome = parseFloat(
        getComputedStyle(document.documentElement).getPropertyValue('--comp-sticky-chrome-h'),
      ) || 0;
      const top = Math.max(heroBottom, chrome) + 10;
      setDropBox({
        top,
        left: wrapRect.left,
        width: wrapRect.width,
        // Нижний отступ 16px — чтобы панель не липла к краю экрана. Пол в 240px: на совсем
        // низком окне лучше дать панели вылезти, чем схлопнуть её в полоску.
        maxHeight: Math.max(240, window.innerHeight - top - 16),
      });
    };
    measure();
    window.addEventListener('scroll', measure, { passive: true });
    window.addEventListener('resize', measure);
    return () => {
      window.removeEventListener('scroll', measure);
      window.removeEventListener('resize', measure);
    };
  }, [panelOpen, isMobile]);

  // Body scroll lock, пока открыт мобильный sheet
  React.useEffect(() => {
    if (!(panelOpen && isMobile)) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [panelOpen, isMobile]);

  return { panelOpen, setPanelOpen, catMenuOpen, setCatMenuOpen, dropBox };
};
