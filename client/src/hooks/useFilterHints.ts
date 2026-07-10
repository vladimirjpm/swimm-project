import { useEffect, useState } from 'react';

export type FilterHintField = 'style' | 'distance' | 'club' | 'competition' | 'name';

/**
 * GET /api/results/filter-hints — источник опций фильтров в paged-режиме (контракт 3.2 §4),
 * т.к. в этом режиме на клиенте нет полного датасета соревнования. Debounce 300мс на q —
 * не бьём API каждым символом ввода. enabled=false (full-режим) — фетч не выполняется вовсе,
 * компонент просто передаёт mode === 'paged'.
 */
export function useFilterHints(field: FilterHintField, q: string, limit = 20, enabled = true): string[] {
  const [hints, setHints] = useState<string[]>([]);

  useEffect(() => {
    if (!enabled) {
      setHints([]);
      return;
    }
    let alive = true;
    const timer = setTimeout(() => {
      const qs = new URLSearchParams({ field, limit: String(limit) });
      if (q) qs.set('q', q);
      fetch(`/api/results/filter-hints?${qs}`, { credentials: 'same-origin' })
        .then((r) => (r.ok ? (r.json() as Promise<string[]>) : []))
        .then((data) => {
          if (alive) setHints(data);
        })
        .catch(() => {
          if (alive) setHints([]);
        });
    }, 300);

    return () => {
      alive = false;
      clearTimeout(timer);
    };
  }, [field, q, limit, enabled]);

  return hints;
}
