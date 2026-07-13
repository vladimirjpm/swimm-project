import React from 'react';
import HighlightCard from './highlight-card';
import type { HubGroupHighlight } from './types';

// Лента хайлайтов: рендерит массив КАК ЕСТЬ — состав и порядок задаёт сервер.
// ≥lg — grid repeat(4, 1fr); <lg — горизонтальный скролл (flex + overflow-x).
// Пустая лента → null: шапка схлопывается до Top + Tabs (обратная совместимость).

export default function GroupHighlights({ highlights }: { highlights?: HubGroupHighlight[] }) {
  if (!highlights?.length) return null;
  return (
    <div
      className="flex gap-2 overflow-x-auto px-3 py-2.5 lg:grid lg:grid-cols-4 lg:overflow-visible"
      style={{ background: 'var(--theme-mode-surface)' }}
    >
      {highlights.map((h, i) => (
        <HighlightCard key={`${h.type}-${i}`} highlight={h} />
      ))}
    </div>
  );
}
