import React from 'react';
import { rootActions, useAppDispatch, useAppSelector } from '../../../../store/store';
import { useResultsLoadMode } from '../../../../hooks/useResultsLoadMode';
import { PAGE_CONTAINER } from '../../../../utils/layout';
import './combine-bar.css';

// Полоса «Combine All Results» под табами шапки соревнования
// (design_handoff_competition_overview_combine, вариант 12b). Заменила мелкий тумблер
// в правом краю строки табов и его мобильный дубль в фильтрах Swims: это единственное
// место в UI, объясняющее режим, поэтому у полосы есть подпись-пояснение.
//
// Условия видимости НЕ менялись: пересчёт требует полного датасета события, поэтому
// в paged-режиме недоступен; showCombineAllResults приходит с результатами соревнования.
// Вся полоса — один control role="switch", текст полосы служит ему label (вложенной
// кнопки внутри кликабельной области нет — тумблер справа декоративный).

export default function CombineBar() {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.filterSelected);
  const showCombineAllResults = useAppSelector((state) => state.showCombineAllResults);
  const mode = useResultsLoadMode();
  const isActive = !!filters.is_recalculated;

  if (mode === 'paged' || !showCombineAllResults) return null;

  const toggle = () => {
    dispatch(
      rootActions.updateState({
        filterSelected: { ...filters, is_recalculated: !isActive },
      }),
    );
  };

  return (
    // Фон полосы — край-в-край, содержимое — в общем контейнере страницы (WIDTH-1920).
    <button
      type="button"
      role="switch"
      aria-checked={isActive}
      aria-label="Combine All Results — one combined ranking across all age groups"
      onClick={toggle}
      className="combine-bar"
    >
      <span className="combine-bar__shimmer" aria-hidden="true" />
      <span className={`${PAGE_CONTAINER} combine-bar__inner`}>
        <span className="combine-bar__icon" aria-hidden="true">⚡</span>
        <span className="combine-bar__text">
          <span className="combine-bar__title">Combine All Results</span>
          <span className="combine-bar__hint">
            One combined ranking across all age groups
          </span>
        </span>
        <span className="combine-bar__state">
          <span>{isActive ? 'On' : 'Off'}</span>
          <span className="combine-bar__toggle" aria-hidden="true" />
        </span>
      </span>
    </button>
  );
}
