import React, { useState } from 'react';
import { useFavoritesContext } from '../../../../hooks/favorites-context';
import { useStartListSearch } from './use-start-list';
import { dayLabel, formatApproxTime } from './start-list-helpers';

/**
 * Панель «найти своего» над зумами таба (запрос Влада 28.08.2026): поиск пловца по имени
 * плюс чипы избранных для залогиненного.
 *
 * Зачем отдельно от программы дня: программа отвечает на «что плывут», а родителю нужно
 * «когда плывёт МОЙ». Раньше это можно было узнать, только пролистав ленту и открыв нужный
 * заплыв, — а у соревнования из четырёх окружных протоколов лента вчетверо длиннее.
 *
 * Поиск идёт по ВСЕМ источникам соревнования сразу и показывает дни: у составного старта
 * ответ «в какой день» так же важен, как «во сколько».
 */
export default function SwimmerFinder({ orgCompIds, onOpenSwimmer }: {
  orgCompIds: number[];
  onOpenSwimmer: (swimmerId: number) => void;
}) {
  const { isAuthenticated, favorites, primarySwimmerId } = useFavoritesContext();
  const [query, setQuery] = useState('');
  const { data: hits, loading } = useStartListSearch(orgCompIds, query);

  // Избранные-пловцы (клубы в эту панель не идут: у клуба нет «во сколько плывёт»).
  // Primary — первым: это «я», и он нужен чаще остальных.
  const favSwimmers = favorites
    .filter((f) => f.swimmer_id != null)
    .sort((a, b) => Number(b.swimmer_id === primarySwimmerId) - Number(a.swimmer_id === primarySwimmerId));

  return (
    <div className="mb-3">
      {isAuthenticated && favSwimmers.length > 0 && (
        // Цвета — те же токены --theme-personal-*, что у полосы «FAVORITES» в шапке
        // соревнования: одна и та же вещь на странице обязана выглядеть одинаково.
        <div
          className="mb-2 flex flex-wrap items-center gap-1.5 rounded-[10px] p-2.5"
          style={{
            background: 'var(--theme-personal-bg)',
            border: '1px solid var(--theme-personal-border)',
          }}
        >
          <span
            className="rounded-full px-2 py-0.5 text-[10px] font-extrabold uppercase tracking-wide"
            style={{ background: 'var(--theme-personal-badge-bg)', color: 'var(--theme-personal-accent)' }}
          >
            ❤ Favorites
          </span>
          {favSwimmers.map((f) => (
            <button
              key={f.swimmer_id}
              type="button"
              onClick={() => onOpenSwimmer(f.swimmer_id as number)}
              className="rounded-full px-2.5 py-1 text-[12px] font-bold"
              style={{
                background: 'var(--theme-personal-badge-bg)',
                color: 'var(--theme-personal-accent)',
              }}
              dir="auto"
            >
              {f.swimmer_id === primarySwimmerId ? '⭐ ' : ''}{f.swimmer_name}
            </button>
          ))}
        </div>
      )}

      <input
        type="search"
        placeholder="Find a swimmer by name — when do they swim?"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        className="w-full rounded-[10px] border px-3 py-2 text-sm"
        style={{ borderColor: 'var(--theme-mode-border-input)', background: 'var(--theme-mode-surface)' }}
        dir="auto"
      />

      {query.trim().length >= 2 && (
        <div
          className="mt-2 overflow-hidden rounded-[10px] border"
          style={{ borderColor: 'var(--theme-mode-border-input)' }}
        >
          {loading && !hits && <div className="p-3 text-sm opacity-60">Searching…</div>}
          {hits?.length === 0 && <div className="p-3 text-sm opacity-60">Nobody with that name is entered here.</div>}
          {hits?.map((h) => (
            <button
              key={h.swimmer_id}
              type="button"
              onClick={() => onOpenSwimmer(h.swimmer_id)}
              className="flex w-full items-center gap-3 border-b px-3 py-2 text-left last:border-b-0"
              style={{ borderColor: 'var(--theme-mode-border-input)' }}
            >
              <div className="min-w-0 flex-1">
                <div className="truncate text-sm font-bold" dir="auto">{h.swimmer_name}</div>
                <div className="truncate text-[11px] opacity-70" dir="auto">
                  {[h.birth_year, h.club_name].filter(Boolean).join(' · ')}
                </div>
              </div>
              <div className="shrink-0 text-right text-[11px] opacity-80">
                {/* Дни — то, ради чего поиск и заведён: у составного старта пловец плывёт
                    в свой день, и лента программы этого не подсказывает. */}
                <div className="font-bold">{h.days.map(dayLabel).join(' · ')}</div>
                <div className="opacity-70">
                  {h.swims} {h.swims === 1 ? 'swim' : 'swims'}
                  {h.first_start_at ? ` · ${formatApproxTime(h.first_start_at)}` : ''}
                </div>
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
