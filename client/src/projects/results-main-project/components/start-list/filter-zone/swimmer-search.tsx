import React, { useRef, useState } from 'react';
import { useStartListSearch } from '../use-start-list';
import { dayLabel, formatApproxTime } from '../start-list-helpers';
import type { FilterZoneMode } from './types';

/**
 * Строка 2 зоны, поиск пловца по имени с выпадающей выдачей.
 *
 * Выдача висит ПРЯМО ПОД полем, абсолютно и поверх протокола (замечание Влада 21.10.2026):
 * раньше она шла блоком после всей карточки зоны, далеко от поля, и раздвигала ленту; а
 * в потоке она ломала бы высоту липкой зоны.
 */
export default function SwimmerSearch({ mode, orgCompIds, planSwimmerIds, onPick }: {
  mode: FilterZoneMode;
  /** Поиск идёт по ВСЕМ источникам сразу: родителю всё равно, в каком протоколе его ребёнок. */
  orgCompIds: number[];
  /** В режиме плана выдача сужается до этих пловцов — поле обещает поиск «в моём плане». */
  planSwimmerIds: ReadonlySet<number>;
  onPick: (swimmerId: number) => void;
}) {
  const plan = mode === 'plan';
  const [query, setQuery] = useState('');
  const { data: rawHits, loading } = useStartListSearch(orgCompIds, query);
  const inputRef = useRef<HTMLInputElement>(null);

  // В плане ищем среди своих: поле обещает «in my plan», и общая выдача по старту врала бы.
  const hits = plan && rawHits ? rawHits.filter((h) => planSwimmerIds.has(h.swimmer_id)) : rawHits;

  // Выдачу ОБЯЗАТЕЛЬНО схлопываем и снимаем фокус (баг 31.08.2026): раскрытый список
  // закрывал карточку пловца, а клавиатура — то, ради чего человек и нажимал.
  const pick = (swimmerId: number) => {
    setQuery('');
    inputRef.current?.blur();
    onPick(swimmerId);
  };

  return (
    <div className="relative min-w-[180px] flex-1">
      <input
        ref={inputRef}
        type="search"
        placeholder={plan ? 'Find a swimmer in my plan…' : 'Find a swimmer by name — when do they swim?'}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        className="w-full rounded-[10px] border px-3 py-2 text-[13px]"
        style={{
          borderColor: plan ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
          background: plan ? 'var(--theme-personal-badge-bg)' : 'var(--deep-card-bg-raised)',
          color: 'var(--deep-text)',
        }}
        dir="auto"
      />

      {query.trim().length >= 2 && (
        <div
          className="absolute left-0 right-0 top-full z-40 mt-1 max-h-[50vh] overflow-y-auto rounded-[10px] border shadow-lg"
          style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)' }}
        >
          {loading && !hits && <div className="p-3 text-sm opacity-60">Searching…</div>}
          {hits?.length === 0 && (
            <div className="p-3 text-sm opacity-60">
              {plan ? 'Nobody with that name is in your plan.' : 'Nobody with that name is entered here.'}
            </div>
          )}
          {hits?.map((h) => (
            <button
              key={h.swimmer_id}
              type="button"
              onClick={() => pick(h.swimmer_id)}
              className="flex w-full items-center gap-3 border-b px-3 py-2 text-left last:border-b-0"
              style={{ borderColor: 'var(--deep-divider)' }}
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
