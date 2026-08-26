// ⚠ Копия типа, которую сейчас никто не импортирует: рабочий живёт в
// `projects/components/filter-section/filter-types.ts`. Правишь один — правь оба.
export interface FilterData {
  pool_type: string[];
  gender: string[];
  style_list: SwimmingStyle[];
}

export interface SwimmingStyle {
  style_name: string;
  /** Строки — как в `filter-data.js` («50», «4X50»). */
  style_len: string[];
}
