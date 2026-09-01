/**
 * Shared types and helpers for the filter-section components.
 */

export type FilterData = {
  pool_type: string[];
  gender: string[];
  style_list: {
    style_name: string;
    /** Строки, а не числа: в `filter-data.js` дистанции лежат как «50», «1500», «4X50».
     *  Тип, обещавший числа, скрывал эстафеты — `Number('4X50')` даёт NaN. */
    style_len: string[];
  }[];
};

/**
 * Read filter_data injected into window by public/data/filter-data.js.
 */
export const getFilterData = (): FilterData | null => {
  const raw = (window as any).filter_data;
  if (!raw) {
    console.error('filter_data is not loaded from window');
    return null;
  }
  return raw as FilterData;
};
