import { useCallback, useMemo, useState } from 'react';
import type { PosterLayout, PosterSwimSource } from '../poster-data';
import { POSTER_LIMIT, clampSelection, defaultSelection, toggleSelection } from '../poster-selection';

/**
 * Галочки «какие заплывы на плакате». Пока пользователь ничего не трогал — выбор по умолчанию
 * для текущей раскладки (у раскладок разный предел строк); после первой галочки — его выбор,
 * урезанный под предел раскладки. Правила — чистые функции `poster-selection.ts`.
 */
export function usePosterSelection(swims: PosterSwimSource[], layout: PosterLayout) {
  const [manual, setManual] = useState<Set<number> | null>(null);
  const limit = POSTER_LIMIT[layout];

  const selected = useMemo(
    () => (manual ? clampSelection(swims, manual, limit) : defaultSelection(swims, layout)),
    [swims, manual, limit, layout],
  );
  const toggle = useCallback((id: number) => setManual(toggleSelection(selected, id, limit)), [selected, limit]);

  return { selected, toggle, limit, full: selected.size >= limit };
}
