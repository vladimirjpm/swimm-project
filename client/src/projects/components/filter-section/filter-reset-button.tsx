import React from 'react';
import { useFilterHost } from './filter-host';

/** Сброс фильтров. Что именно сбрасывается — знает хост страницы (Ф3), кнопка только жмётся. */
const FilterResetButton: React.FC = () => {
  const { reset } = useFilterHost();

  return (
    <button
      className="self-start px-4 py-[9px] rounded-[10px] text-[13px] font-bold text-white bg-[#e63946] hover:bg-[#d12d3a] transition-colors"
      onClick={reset}
    >
      Reset Filters
    </button>
  );
};

export default FilterResetButton;
