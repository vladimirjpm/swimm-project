import React from 'react';
import { createPortal } from 'react-dom';
import './mobile-filters-drawer.css';

/**
 * ПЛАВАЮЩАЯ КНОПКА «FILTERS» — одна на продукт (Ф4.0 плана
 * `docs/plans/my-media-filters-plan.md`, решение Влада 07.09.2026: «одна кнопка на две
 * страницы»).
 *
 * Прибита к низу экрана, а не стоит в потоке: список фильтруют, прокрутив страницу вниз,
 * и кнопка в потоке к этому моменту давно уехала за экран. Открывает шторку
 * (`MobileFiltersDrawer`) и в открытом состоянии превращается в «Apply» — закрыть шторку
 * можно тем же пальцем, не целясь в затемнение.
 *
 * Размеры — из хендоффа `!design_handoff/design_handoff_my_media_filters` (48px, радиус 24,
 * счётчик активных фильтров кружком). Палитра — токены `--fpill-*` с фоллбеком на палитру
 * results; страница в другой теме переопределяет их у себя.
 *
 * Портал в `body` обязателен: у страниц с трансформированными предками `position: fixed`
 * считается от предка, и кнопка уезжает вместе с контентом.
 */
interface Props {
  open: boolean;
  onToggle: () => void;
  /**
   * Сколько фильтров сейчас сужают выборку. Не задано — кружка нет: на results такого
   * счётчика пока никто не считает, а рисовать ноль хуже, чем не рисовать ничего.
   */
  count?: number;
  /** `id` шторки — для `aria-controls`. */
  controls?: string;
  /**
   * Прятать кнопку, пока шторка открыта. Нужно там, где у шторки есть свой подвал
   * («Show N swims»): иначе пилюля ложится ровно на него и две кнопки делают одно и то же.
   * Без подвала (results) кнопка остаётся и работает как «Apply».
   */
  hideWhenOpen?: boolean;
  /** Класс кнопки: переопределение токенов `--fpill-*`. */
  className?: string;
}

function FiltersFab({ open, onToggle, count, controls, hideWhenOpen, className }: Props) {
  if (open && hideWhenOpen) return null;

  const node = (
    <button
      type="button"
      onClick={onToggle}
      className={`fpill${className ? ` ${className}` : ''}`}
      aria-expanded={open}
      aria-controls={controls}
      title={open ? 'Apply' : 'Filters'}
    >
      <span className="fpill__label">{open ? 'Apply' : 'Filters'}</span>
      {!open && count != null && count > 0 && <span className="fpill__count">{count}</span>}
      <span className="fpill__caret">{open ? '▼' : '▲'}</span>
    </button>
  );

  return <>{createPortal(node, document.body)}</>;
}

export default FiltersFab;
