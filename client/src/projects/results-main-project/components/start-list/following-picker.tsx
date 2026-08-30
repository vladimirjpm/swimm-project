import React, { useMemo, useState } from 'react';
import { useFavoritesContext } from '../../../../hooks/favorites-context';
import { useStartListSearch } from './use-start-list';
import { dayLabel } from './start-list-helpers';
import { planSummary, swimmerDays } from './plan-model';
import type { StartListClub, StartListSwimmer } from './types';
import type { StartListPlan } from './use-start-list-plan';

/**
 * Экран S1 «Following» — вход в личный план (шаг Т5, макет `Start List.dc.html`, секция 3a).
 *
 * Отвечает на один вопрос: ЗА КЕМ следить на этом старте. Несколько избранных сразу плюс
 * клуб целиком — раньше режим был один и жёсткий: «primary favorite», один пловец.
 *
 * Что здесь важно и не переизобретается:
 *
 * 1. **Избранные — это ДЕФОЛТ состава, а не сам состав** (решение Влада 29.08.2026).
 *    Отметить можно кого угодно, включая чужой клуб, и обратно в избранное это НЕ пишется:
 *    «посмотреть чужой клуб на одном старте» не должно молча менять профиль.
 * 2. **Состав живёт per-соревнование** (`useStartListPlan`, Т3): у гостя в localStorage,
 *    у залогиненного в профиле.
 * 3. Строки избранных показывают ЕГО ДНИ («Sun 15 · Thu 19») — на составном старте это
 *    половина ответа «когда плывёт мой», и её видно ещё до открытия карточки.
 */
export default function FollowingPicker({
  orgCompIds, plan, swimmers, clubs, rowIds, favClubIds: favClubIdList, primarySwimmerId,
  loading, onChange, onShowPlan,
}: {
  orgCompIds: number[];
  /** ДЕЙСТВУЮЩИЙ состав: сохранённый план либо дефолт из избранного (useEffectivePlan). */
  plan: StartListPlan;
  swimmers: Record<number, StartListSwimmer>;
  clubs: StartListClub[];
  /** Кого рисуем строками: избранные + добавленные поиском. */
  rowIds: number[];
  favClubIds: number[];
  primarySwimmerId: number | null;
  loading: boolean;
  /** Состав уходит наверх ЦЕЛИКОМ: первая же правка материализует дефолт вместе с ней. */
  onChange: (next: StartListPlan) => void;
  onShowPlan: () => void;
}) {
  const { isAuthenticated, favorites } = useFavoritesContext();
  const [query, setQuery] = useState('');
  const [allClubs, setAllClubs] = useState(false);

  const favClubIds = new Set(favClubIdList);
  const swimmersLoading = loading;
  const clubsLoading = loading;
  const { data: hits, loading: searching } = useStartListSearch(orgCompIds, query);

  const selectedSwimmers = new Set(plan.swimmer_ids);
  const selectedClubs = new Set(plan.club_ids);

  const toggle = (list: number[], id: number) =>
    list.includes(id) ? list.filter((x) => x !== id) : [...list, id];
  const onToggleSwimmer = (id: number) =>
    onChange({ ...plan, swimmer_ids: toggle(plan.swimmer_ids, id) });
  const onToggleClub = (id: number) =>
    onChange({ ...plan, club_ids: toggle(plan.club_ids, id) });

  // Клубы: сперва избранные и уже выбранные, потом остальные по числу заплывов (сервер
  // отдаёт их уже в этом порядке). Свёрнутый список — первые пять: у чемпионата клубов
  // под сотню, и пикер иначе превращается в справочник.
  const orderedClubs = useMemo(() => {
    const list = clubs;
    const mine = list.filter((c) => favClubIds.has(c.club_id) || selectedClubs.has(c.club_id));
    const rest = list.filter((c) => !favClubIds.has(c.club_id) && !selectedClubs.has(c.club_id));
    return { mine, rest };
  }, [clubs, [...favClubIds].join(','), plan.club_ids.join(',')]);

  const shownRest = allClubs ? orderedClubs.rest : orderedClubs.rest.slice(0, 5);
  const summary = planSummary(plan);

  return (
    // Цвет текста — парным токеном поверхности (правило парных токенов, client/CLAUDE.md):
    // строки пикера стоят на карточках Deep, а не на фоне страницы, и наследованный цвет
    // в dark оказывался тёмным по тёмному.
    <div className="pb-24" style={{ color: 'var(--deep-text)' }}>
      <SectionTitle
        title="Favorites"
        note={plan.swimmer_ids.length > 0 ? `${plan.swimmer_ids.length} selected` : undefined}
      />

      {!isAuthenticated && rowIds.length === 0 && (
        <p className="mb-3 text-[12px] opacity-70">
          Sign in to keep favorites — or find a swimmer by name below and add them to this meet only.
        </p>
      )}

      {swimmersLoading && rowIds.length > 0 && (
        <div className="py-2 text-sm opacity-60">Loading…</div>
      )}

      {rowIds.map((id) => {
        const swimmer = swimmers[id];
        const days = swimmerDays(swimmer);
        const name = swimmer?.swimmer_name
          ?? favorites.find((f) => f.swimmer_id === id)?.swimmer_name
          ?? `#${id}`;
        // Пловца может не быть в протоколе вовсе — тогда отмечать его нечем: плана он не
        // наполнит, а строка «выбран, но не плывёт» читается как ошибка данных.
        const entered = swimmer != null;

        return (
          <PickRow
            key={id}
            selected={selectedSwimmers.has(id)}
            disabled={!entered}
            onClick={() => entered && onToggleSwimmer(id)}
            title={`${id === primarySwimmerId ? '⭐ ' : ''}${name}`}
            note={entered
              ? days.map(dayLabel).join(' · ')
              : 'not entered here'}
          />
        );
      })}

      {/* Добавить не-избранного: поиск по имени внутри соревнования. В избранное это НЕ
          пишется — состав живёт только у этого старта. */}
      <div className="mt-2">
        <input
          type="search"
          placeholder="+ Add a swimmer by name…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          className="w-full rounded-[10px] border px-3 py-2 text-sm"
          style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)' }}
          dir="auto"
        />
        {query.trim().length >= 2 && (
          <div className="mt-2 overflow-hidden rounded-[10px] border" style={{ borderColor: 'var(--deep-card-border)' }}>
            {searching && !hits && <div className="p-3 text-sm opacity-60">Searching…</div>}
            {hits?.length === 0 && <div className="p-3 text-sm opacity-60">Nobody with that name is entered here.</div>}
            {hits?.filter((h) => !rowIds.includes(h.swimmer_id)).map((h) => (
              <button
                key={h.swimmer_id}
                type="button"
                onClick={() => { onToggleSwimmer(h.swimmer_id); setQuery(''); }}
                className="flex w-full items-center gap-3 border-b px-3 py-2 text-left last:border-b-0"
                style={{ borderColor: 'var(--deep-card-border)' }}
              >
                <div className="min-w-0 flex-1">
                  <div className="truncate text-sm font-bold" dir="auto">{h.swimmer_name}</div>
                  <div className="truncate text-[11px] opacity-70" dir="auto">
                    {[h.birth_year, h.club_name].filter(Boolean).join(' · ')}
                  </div>
                </div>
                <div className="shrink-0 text-[11px] font-bold opacity-80">
                  {h.days.map(dayLabel).join(' · ')}
                </div>
              </button>
            ))}
          </div>
        )}
      </div>

      <SectionTitle
        title="Follow a whole club"
        note={plan.club_ids.length > 0 ? `${plan.club_ids.length} selected` : undefined}
      />
      <p className="mb-2 text-[11px] opacity-70">Every swimmer of the club lands in your plan.</p>

      {clubsLoading && clubs.length === 0 && <div className="py-2 text-sm opacity-60">Loading…</div>}

      {[...orderedClubs.mine, ...shownRest].map((c) => (
        <PickRow
          key={c.club_id}
          selected={selectedClubs.has(c.club_id)}
          club
          onClick={() => onToggleClub(c.club_id)}
          title={c.club_name}
          note={`${c.swimmers} swimmers · ${c.entries} swims`}
        />
      ))}

      {!allClubs && orderedClubs.rest.length > shownRest.length && (
        <button
          type="button"
          onClick={() => setAllClubs(true)}
          className="mt-1 text-[12px] font-bold"
          style={{ color: 'var(--deep-accent)' }}
        >
          All {clubs.length} clubs…
        </button>
      )}

      {/* CTA — «показать мой план». Пока состав пуст, показывать нечего. */}
      <button
        type="button"
        onClick={onShowPlan}
        disabled={summary === null}
        className="mt-5 w-full rounded-[12px] px-3 py-3 text-[13px] font-black disabled:opacity-40"
        style={{ background: 'var(--deep-accent)', color: 'var(--deep-accent-ink)' }}
      >
        {summary ? `Show my plan — ${summary}` : 'Pick someone to follow'}
      </button>
    </div>
  );
}

function SectionTitle({ title, note }: { title: string; note?: string }) {
  return (
    <div className="mt-4 mb-2 flex items-baseline justify-between gap-2">
      <span className="text-[11px] font-black uppercase tracking-wide opacity-70">{title}</span>
      {note && (
        <span className="text-[11px] font-bold" style={{ color: 'var(--theme-personal-accent)' }}>{note}</span>
      )}
    </div>
  );
}

/**
 * Строка выбора. Выбранный пловец золотой (`--theme-personal-*` — «моё» во всём проекте),
 * выбранный клуб — cyan темы Deep («мы»); хардкодов цвета из макета в коде нет.
 */
function PickRow({ selected, disabled, club, title, note, onClick }: {
  selected: boolean;
  disabled?: boolean;
  club?: boolean;
  title: string;
  note?: string;
  onClick: () => void;
}) {
  const accent = club ? 'var(--deep-accent)' : 'var(--theme-personal-accent)';
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={selected}
      className="mb-1.5 flex w-full items-center gap-2.5 rounded-[12px] border px-3 py-2.5 text-left disabled:opacity-45"
      style={{
        background: selected
          ? (club ? 'var(--deep-accent-soft)' : 'var(--theme-personal-bg)')
          : 'var(--deep-card-bg)',
        borderColor: selected
          ? (club ? 'var(--deep-accent-border)' : 'var(--theme-personal-border)')
          : 'var(--deep-card-border)',
      }}
    >
      {/* Галочку рисуем ТОЛЬКО у выбранного: прозрачный «✓» на невыбранных читается
          скринридером как отметка, хотя её нет (состояние несёт aria-pressed). */}
      <span
        aria-hidden
        className="flex h-5 w-5 shrink-0 items-center justify-center rounded-[6px] border text-[12px] font-black"
        style={{
          background: selected ? accent : 'transparent',
          borderColor: selected ? accent : 'var(--deep-card-border)',
          color: 'var(--deep-accent-ink)',
        }}
      >
        {selected ? '✓' : ''}
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-[14px] font-bold" dir="auto" style={{ color: selected ? accent : undefined }}>
          {title}
        </span>
        {note && <span className="block truncate text-[11px] opacity-70">{note}</span>}
      </span>
    </button>
  );
}
