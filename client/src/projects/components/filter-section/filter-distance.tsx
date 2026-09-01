import React from 'react';
import { useFilterHost } from './filter-host';

/**
 * Дистанции выбранного стиля. Отдельного заголовка нет — колонка живёт внутри карточки
 * «Swimming Style», поэтому без выбранного стиля компонент не рисует ничего.
 *
 * Дистанция — СТРОКА (у эстафет она «4X50»), сравнение и запись идут строкой.
 */
const FilterDistance: React.FC = () => {
  const { values, set, options, isAvailable } = useFilterHost();
  const styleName = values.style_name;

  if (!styleName) return null;

  const styleLens =
    options.styles.find((style) => style.style_name === styleName)?.style_len ||
    [];

  return (
    <div className="pl-3 border-l border-dashed border-[var(--theme-mode-border-input)]">
      <div className="flex flex-col gap-2">
        {/* Пустая ячейка на уровне кнопки "All" в колонке стилей, чтобы 50m совпал со строкой freestyle */}
        <button className="fseg invisible" disabled aria-hidden="true" tabIndex={-1}>
          All
        </button>
        {styleLens.map((len) => {
          const disabled = isAvailable ? !isAvailable('distance', len) : false;
          return (
            <button
              key={len}
              disabled={disabled}
              className={`fseg ${String(values.style_len) === String(len) ? 'fseg-active' : ''}`}
              onClick={() => !disabled && set({ style_len: len })}
            >
              {len}m
            </button>
          );
        })}
      </div>
    </div>
  );
};

export default FilterDistance;
