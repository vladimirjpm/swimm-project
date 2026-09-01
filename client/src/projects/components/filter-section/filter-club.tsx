import React, { useState } from 'react';
import UI_ClubDetails from '../mix/club-details/club-details';
import { useFilterHost } from './filter-host';
import FilterCard from './filter-card';

/**
 * Клуб. Список и метрики приходят готовыми из хоста (Ф3) — он же знает, откуда их брать:
 * на results в full-режиме сводку считает клиент по выборке, в paged-режиме (фаза 3.4) её
 * отдаёт сервер, и там же включается поиск, потому что список длинный.
 *
 * Значение фильтра — `value` опции, а не её подпись: у results это имя клуба, у страницы с
 * идентификаторами будет club_id. Компонент возвращает `value` как есть.
 */
const FilterClub: React.FC = () => {
  const { values, set, options } = useFilterHost();
  const clubs = options.clubs;
  const [clubQuery, setClubQuery] = useState('');

  if (!clubs) return null;

  const club = values.club ?? 'all';
  // Сводка — ПОДПИСЬ выбранного клуба, а не его значение: у страницы с идентификаторами
  // значение это club_id, и в шапке карточки стояло бы «417».
  const clubLabel = clubs.items.find((c) => c.value === club)?.label ?? club;
  const query = clubQuery.trim().toLowerCase();
  const shown =
    clubs.searchable && query
      ? clubs.items.filter((c) => c.label.toLowerCase().includes(query))
      : clubs.items;
  const hasStats = clubs.items.some((c) => c.stats);
  const isActive = club !== 'all';

  return (
    <FilterCard title="Club" summary={isActive ? clubLabel : 'All'} isActive={isActive}>
      <div className="flex flex-col gap-2">
        <button
          className={`fseg self-start ${club === 'all' ? 'fseg-active' : ''}`}
          onClick={() => set({ club: 'all' })}
        >
          all
        </button>
        {hasStats && (
          <div className="text-[11px] text-[var(--theme-mode-text-muted)]">
            ⭐ rating · 🏊 swimmers · ✅ events
          </div>
        )}
        {clubs.searchable && (
          <input
            type="text"
            value={clubQuery}
            onChange={(e) => setClubQuery(e.target.value)}
            placeholder="Search club…"
            className="rounded-lg px-2.5 py-1.5 text-[13px] outline-none"
            style={{
              background: 'var(--theme-mode-input-bg)',
              border: '1px solid var(--theme-mode-border-input)',
              color: 'var(--theme-mode-text)',
            }}
          />
        )}
        {/* Список клубов отдельным контейнером: страница, которой нужен предел высоты,
            вешает его на `filter-club-list` — и «all» с полем поиска остаются на месте,
            а не уезжают вместе с прокруткой. На results правила с этим классом нет. */}
        <div className="filter-club-list flex flex-col gap-2">
          {shown.map((option) =>
            option.stats ? (
              <UI_ClubDetails
                key={option.value}
                club={option.label}
                isSelected={club === option.value}
                onSelect={() => set({ club: option.value })}
                gold={option.stats.gold}
                silver={option.stats.silver}
                bronze={option.stats.bronze}
                swimmerCount={option.stats.swimmerCount}
                successfulCount={option.stats.successfulCount}
                points={option.stats.points}
              />
            ) : (
              // Хост без метрик (страница, где очки и медали по клубу не считаются) —
              // простая строка вместо карточки клуба; справа необязательная приписка.
              <button
                key={option.value}
                className={`fseg flex w-full items-center justify-between gap-2 text-left ${
                  club === option.value ? 'fseg-active' : ''
                }`}
                onClick={() => set({ club: option.value })}
              >
                <span className="truncate">{option.label}</span>
                {option.note && <span className="opacity-60">{option.note}</span>}
              </button>
            ),
          )}
        </div>
      </div>
    </FilterCard>
  );
};

export default FilterClub;
