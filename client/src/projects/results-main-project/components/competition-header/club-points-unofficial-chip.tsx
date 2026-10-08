import React from 'react';

// Чип «Unofficial» у клубного зачёта: официального зачёта у соревнования нет, а очки
// посчитаны НАМИ по шкале правила (обычно автоподбор — шкала чемпионата Израиля). По духу
// Р76 (docs/data-integrity.md): цифры протокола показываем как есть, а наш вывод из них
// подписываем. Флаг — club_points_unofficial из /api/competition-overview; правила — в
// docs/competition-overview-cards.md (раздел Top clubs).
//
// Один компонент на два места — карточку Top clubs и таб Clubs: подпись и пояснение
// обязаны совпадать, иначе два экрана про одно соревнование скажут разное.

export const UNOFFICIAL_POINTS_TITLE =
  'This competition has no official club standings. The points are our own estimate, '
  + 'scored from the official places.';

export default function ClubPointsUnofficialChip({ className = '' }: { className?: string }) {
  return (
    <span
      className={`whitespace-nowrap rounded-full px-2 py-[2px] text-[11px] font-extrabold ${className}`}
      style={{
        background: 'color-mix(in srgb, var(--theme-mode-text-muted) 12%, transparent)',
        color: 'var(--theme-mode-text-secondary)',
        border: '1px solid color-mix(in srgb, var(--theme-mode-text-muted) 30%, transparent)',
      }}
      title={UNOFFICIAL_POINTS_TITLE}
      aria-label={`Unofficial. ${UNOFFICIAL_POINTS_TITLE}`}
    >
      Unofficial
    </span>
  );
}
