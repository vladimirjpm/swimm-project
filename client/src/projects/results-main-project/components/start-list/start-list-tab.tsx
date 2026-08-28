import React, { useCallback, useEffect, useMemo, useState } from 'react';
import UI_SwimmStyleIcon from '../../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimmerNameCell from '../../../components/mix/swimmer-name-cell/swimmer-name-cell';
import UI_SwimTime from '../../../components/mix/swim-time/swim-time';
import {
  useStartListEvent, useStartListProgramme, useStartListSearch, useStartListSwimmerAcross,
} from './use-start-list';
import { useFavoritesContext } from '../../../../hooks/favorites-context';
import {
  arriveByTime, dayLabel, eventLineLabel, formatApproxTime, mergeRelayLanes, sortEvents, swimLabel,
} from './start-list-helpers';
import type { StartListSource } from '../competition-header/types';

/**
 * Таб «Start list» карточки соревнования (С7, docs/plans/start-list-plan.md §4) — три уровня
 * приближения на ОДНОМ табе: программа дня → заплыв с дорожками → карточка пловца.
 * `?swimmer=` открывает сразу зум 3, `?heat=` — зум 2, ничего — зум 1 (решение 2).
 *
 * Мобильный — основной вид (решение 6): никакой горизонтальной прокрутки, время и номер
 * заплыва крупно. Автообновления нет (решение 7) — вместо него метка «Updated HH:MM» + Refresh.
 */

type Zoom = 'programme' | 'heat' | 'swimmer';

interface Props {
  orgCompId: number;
  /** Источники протокола (подтабы). Пусто или один — подтабы не рисуются, вид как был. */
  sources?: StartListSource[];
}

function updatedLabel(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  return `Updated ${hh}:${mm}`;
}

function RefreshBar({ updatedAt, onRefresh }: { updatedAt: string | null; onRefresh: () => void }) {
  return (
    <div className="mb-3 flex items-center justify-between gap-2 text-[11px] font-semibold opacity-70">
      <span>{updatedLabel(updatedAt)}</span>
      <button
        type="button"
        onClick={onRefresh}
        className="rounded-full border px-2.5 py-1 text-[11px] font-bold"
        style={{ borderColor: 'var(--theme-mode-border-input)' }}
      >
        ↻ Refresh
      </button>
    </div>
  );
}

/**
 * Одна строка дорожки (личная или команда-эстафета) — зум 2.
 *
 * Своя вёрстка, а НЕ общий `SwimRow`: тот собран под результат (место, медаль, очки, дуга
 * уровня, крупное время справа), и в стартовом протоколе он врёт видом — показывает то,
 * чего ещё не произошло. Здесь вопрос другой: кто на какой дорожке и с каким посевом.
 */
function LaneRow({ row }: { row: ReturnType<typeof mergeRelayLanes>[number] }) {
  if ('isRelay' in row) {
    return (
      <div className="flex items-center gap-3 border-b py-2 last:border-b-0" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
        <div className="w-8 shrink-0 text-center text-sm font-black">{row.lane}</div>
        <UI_SwimmerNameCell
          firstName=""
          isRelay
          club={row.club_name}
          relaySwimmersList={row.members.map((m, i) => ({
            order: i + 1, first_name: m.swimmer_name, last_name: '', birth_year: m.birth_year ?? undefined,
          }))}
          className="min-w-0 flex-1"
        />
        <div className="shrink-0 text-right text-xs">
          {row.seed_time ? (
            <>
              <span className="mr-1 text-[9px] font-bold uppercase opacity-60">seed</span>
              <UI_SwimTime time={row.seed_time} />
            </>
          ) : 'NT'}
        </div>
      </div>
    );
  }
  return (
    <div className="flex items-center gap-3 border-b py-2 last:border-b-0" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
      <div className="w-8 shrink-0 text-center text-sm font-black">{row.lane}</div>
      <UI_SwimmerNameCell
        firstName={row.swimmer_name}
        club={`${row.birth_year ?? ''} · ${row.club_name}`.replace(/^ · /, '')}
        className="min-w-0 flex-1"
      />
      <div className="shrink-0 text-right text-xs">
        {row.seed_time ? (
          <>
            <span className="mr-1 text-[9px] font-bold uppercase opacity-60">seed</span>
            <UI_SwimTime time={row.seed_time} />
          </>
        ) : 'NT'}
      </div>
    </div>
  );
}

/** Зум 2 — заплыв (§4.2). */
function HeatZoom({ orgCompId, orgDisciplineId, heat, onBack, onOpenSwimmer }: {
  orgCompId: number; orgDisciplineId: number; heat: number | null;
  onBack: () => void; onOpenSwimmer: (swimmerId: number) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListEvent(orgCompId, orgDisciplineId);
  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;
  if (notFound || !data) return <div className="py-6 text-center text-sm opacity-60">No start list for this event yet.</div>;

  const heats = heat != null ? data.heats.filter((h) => h.heat === heat) : data.heats;

  return (
    <div>
      <button type="button" onClick={onBack} className="mb-2 text-xs font-bold opacity-70 hover:opacity-100">← Programme</button>
      <div className="mb-2 text-sm font-bold">{eventLineLabel(data.event)}</div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {heats.map((h) => (
        <div key={h.heat} className="mb-4 rounded-[12px] border p-3" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
          <div className="mb-2 text-xs font-extrabold">
            Heat {h.heat} · {formatApproxTime(h.start_at)}{h.round ? ` · ${h.round}` : ''}
          </div>
          {mergeRelayLanes(h.lanes).map((row) => (
            <div key={'isRelay' in row ? `r${row.lane}` : row.id} onClick={() => !('isRelay' in row) && onOpenSwimmer(row.swimmer_id)} className={'isRelay' in row ? '' : 'cursor-pointer'}>
              <LaneRow row={row} />
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

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
function SwimmerFinder({ orgCompIds, onOpenSwimmer }: {
  orgCompIds: number[]; onOpenSwimmer: (swimmerId: number) => void;
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

/**
 * Зум 3 — карточка пловца (§4.3, главный экран для родителя).
 *
 * Ходит СРАЗУ ПО ВСЕМ источникам соревнования: у составного старта (окружные протоколы)
 * заплывы одного пловца лежат в разных compID, и «покажи мне все его заплывы» иначе не
 * ответить. Внутри — группировка по дням: «в какой день» — половина вопроса.
 */
function SwimmerZoom({ orgCompIds, swimmerId, onBack, onOpenHeat, onSourcesResolved }: {
  orgCompIds: number[]; swimmerId: number;
  onBack: () => void; onOpenHeat: (orgCompId: number, orgDisciplineId: number, heat: number) => void;
  /** Источники, в которых пловец реально плывёт — родитель подсвечивает ими подтабы. */
  onSourcesResolved: (orgCompIds: number[]) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListSwimmerAcross(orgCompIds, swimmerId);

  // Отдаём наверх ровно те источники, где у пловца есть заплывы. Порядок — как в выдаче
  // (по дню), поэтому первый элемент и есть день его ПЕРВОГО старта.
  const swimSources = data?.swims.map((s) => s.org_comp_id) ?? [];
  const sourcesKey = swimSources.join(',');
  useEffect(() => {
    onSourcesResolved(sourcesKey ? sourcesKey.split(',').map(Number) : []);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sourcesKey]);

  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;
  if (notFound || !data) return <div className="py-6 text-center text-sm opacity-60">This swimmer has no entries here.</div>;

  const arrive = arriveByTime(data.first_start_at);

  // Дни в порядке выдачи (сервер уже отсортировал по дню и времени) — Map сохраняет
  // порядок вставки, поэтому отдельной сортировки тут не нужно.
  const byDay = new Map<string, typeof data.swims>();
  for (const s of data.swims) {
    const key = s.comp_date.slice(0, 10);
    (byDay.get(key) ?? byDay.set(key, []).get(key)!).push(s);
  }

  return (
    <div>
      <button type="button" onClick={onBack} className="mb-2 text-xs font-bold opacity-70 hover:opacity-100">← Programme</button>
      <div className="mb-1 text-lg font-black" dir="auto">{data.swimmer_name} · {data.birth_year} · {data.club_name}</div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {data.first_start_at && (
        <div
          className="mb-4 rounded-[12px] p-3 text-sm font-bold"
          style={{ background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border-input)' }}
        >
          ⏱ First start {formatApproxTime(data.first_start_at)}{arrive ? ` — arrive by ${arrive}` : ''}
        </div>
      )}
      {[...byDay.entries()].map(([day, swims]) => (
        <div key={day} className="mb-4">
          {/* Подпись дня рисуется всегда, даже когда день один: без неё «когда» читается
              как «сегодня», а протокол публикуют за неделю до старта. */}
          <div className="mb-1.5 text-[12px] font-extrabold uppercase tracking-wide opacity-70">
            {dayLabel(day)}
          </div>
          {swims.map((s) => (
            <div
              key={s.id}
              onClick={() => onOpenHeat(s.org_comp_id, s.org_discipline_id, s.heat)}
              className="mb-2 flex cursor-pointer items-center gap-3 rounded-[12px] border p-3"
              style={{ borderColor: 'var(--theme-mode-border-input)' }}
            >
              {/* Дисциплину рисует общий компонент стиля — он же на строке результата.
                  А вот СТРОКУ здесь строим свою: `SwimRow` собран под результат (место,
                  медаль, очки, крупное время справа) и в стартовом протоколе показывал бы
                  то, чего ещё не было. Главное число тут — ВРЕМЯ СТАРТА. */}
              <UI_SwimmStyleIcon styleName={s.style_name} className="h-10 w-10 shrink-0" />
              <div className="min-w-0 flex-1">
                <div className="text-base font-black">{formatApproxTime(s.heat_start_at)}</div>
                <div className="text-xs opacity-70">
                  {swimLabel(s.distance, s.style_name)} · heat {s.heat} · lane {s.lane}
                </div>
              </div>
              <div className="shrink-0 text-right text-xs">
                {s.seed_time ? (
                  <>
                    <span className="mr-1 text-[9px] font-bold uppercase opacity-60">seed</span>
                    <UI_SwimTime time={s.seed_time} />
                  </>
                ) : 'NT'}
              </div>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

/** Зум 1 — программа дня (§4.1), умолчание таба. */
function ProgrammeZoom({ orgCompId, onOpenEvent }: {
  orgCompId: number; onOpenEvent: (orgDisciplineId: number) => void;
}) {
  const { data, loading, notFound, refresh } = useStartListProgramme(orgCompId);
  const [search, setSearch] = useState('');
  const [dayIdx, setDayIdx] = useState(0);

  if (loading && !data) return <div className="py-6 text-center text-sm opacity-60">Loading…</div>;
  if (notFound || !data) return <div className="py-6 text-center text-sm opacity-60">No start list published for this competition yet.</div>;

  const day = data.days[dayIdx] ?? data.days[0];
  const events = day ? sortEvents(day.events) : [];
  const filtered = search.trim().length >= 2
    ? events.filter((e) => `${e.distance}${e.style_name}${e.event_category ?? ''}`.toLowerCase().includes(search.trim().toLowerCase()))
    : events;

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="text-sm font-bold">{data.comp_name} · {data.entries} entries</div>
      </div>
      <RefreshBar updatedAt={data.updated_at} onRefresh={refresh} />
      {data.days.length > 1 && (
        <div className="mb-3 flex flex-wrap gap-1.5">
          {data.days.map((d, i) => (
            <button
              key={d.date}
              type="button"
              onClick={() => setDayIdx(i)}
              className={`rounded-full border px-3 py-1 text-xs font-bold ${i === dayIdx ? 'opacity-100' : 'opacity-60'}`}
              style={{ borderColor: 'var(--theme-mode-border-input)' }}
            >
              {new Date(d.date).toLocaleDateString(undefined, { weekday: 'short', day: '2-digit', month: 'short' })}
            </button>
          ))}
        </div>
      )}
      <input
        type="search"
        placeholder="Search event / category…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        className="mb-3 w-full rounded-[10px] border px-3 py-2 text-sm"
        style={{ borderColor: 'var(--theme-mode-border-input)', background: 'var(--theme-mode-surface)' }}
      />
      <div className="divide-y" style={{ borderColor: 'var(--theme-mode-border-input)' }}>
        {filtered.map((e) => (
          <button
            key={e.org_discipline_id}
            type="button"
            onClick={() => onOpenEvent(e.org_discipline_id)}
            className="flex w-full items-center gap-3 py-2.5 text-left"
          >
            <div className="w-16 shrink-0 text-sm font-black">{formatApproxTime(e.start_at)}</div>
            <div className="w-10 shrink-0 text-center text-sm font-extrabold opacity-80">
              {e.event_number != null ? `#${e.event_number}` : '—'}
            </div>
            <div className="min-w-0 flex-1 text-sm">
              {swimLabel(e.distance, e.style_name)} {e.event_category ? `· ${e.event_category}` : ''}
            </div>
            <div className="shrink-0 text-xs opacity-70">{e.entries} entries</div>
            <div className="shrink-0 opacity-40">›</div>
          </button>
        ))}
        {filtered.length === 0 && <div className="py-4 text-center text-sm opacity-60">No events match.</div>}
      </div>
    </div>
  );
}

/**
 * Подтабы источников (§«одно соревнование — несколько compID»). Нужны там, где наш один
 * старт собран из нескольких протоколов федерации: окружные чемпионаты 8-11 лежат под
 * четырьмя compID, и без подтабов таб показывал бы один округ из четырёх.
 *
 * Подпись — дата и номер («16/02 · #2»): имена протоколов у федерации на иврите, а видимый
 * UI у нас только английский. Полное имя — в тултипе.
 */
function SourceTabs({ sources, activeOrgCompId, swimmerSources, onSelect }: {
  sources: StartListSource[]; activeOrgCompId: number;
  /** Источники, в которых плывёт ОТКРЫТЫЙ пловец. Пусто — карточка не открыта. */
  swimmerSources?: number[];
  onSelect: (orgCompId: number) => void;
}) {
  const hasSwimmer = (swimmerSources?.length ?? 0) > 0;
  return (
    <div className="mb-3 flex flex-wrap items-center gap-1.5" role="tablist" aria-label="Start list sources">
      {sources.map((s) => {
        const active = s.org_comp_id === activeOrgCompId;
        // Когда открыта карточка пловца, подтабы отвечают на «в какой день он плывёт»:
        // его дни подсвечены, чужие приглушены. Без этого дату приходится искать глазами
        // в списке заплывов, хотя она уже посчитана.
        const swims = !hasSwimmer || swimmerSources!.includes(s.org_comp_id);
        return (
          <button
            key={s.org_comp_id}
            type="button"
            role="tab"
            aria-selected={active}
            title={s.source_name ?? undefined}
            onClick={() => onSelect(s.org_comp_id)}
            className={`rounded-full border px-3 py-1 text-[11px] font-bold ${
              active ? 'opacity-100' : swims ? 'opacity-70 hover:opacity-100' : 'opacity-30 hover:opacity-60'
            }`}
            style={{
              // Выбранный день пловца обведён его же цветом (--theme-personal-accent):
              // это ответ на «когда плывёт мой», а не просто активный таб.
              borderColor: active && hasSwimmer
                ? 'var(--theme-personal-accent)'
                : 'var(--theme-mode-border-input)',
              background: active ? 'var(--theme-mode-surface-2)' : 'transparent',
              color: active && hasSwimmer ? 'var(--theme-personal-accent)' : undefined,
            }}
          >
            {s.date ? `${s.date} · ` : ''}#{s.index}
            {/* В режиме карточки счётчик — заплывы ПЛОВЦА в этом дне, а не всего источника:
                общее число заявок тут ничего не отвечает. */}
            {hasSwimmer
              ? (swims && <span className="ml-1.5 opacity-80">✓</span>)
              : s.entry_count > 0 && <span className="ml-1.5 opacity-60">{s.entry_count}</span>}
          </button>
        );
      })}
    </div>
  );
}

export default function StartListTab({ orgCompId, sources = [] }: Props) {
  // Активный источник переживает перезагрузку и пересылку ссылки — держим в ?src=.
  // Неизвестный/чужой src игнорируем: иначе подтаб «выбран», а данных под ним нет.
  const [activeSource, setActiveSource] = useState<number>(() => {
    const raw = Number(new URLSearchParams(window.location.search).get('src'));
    return sources.some((s) => s.org_comp_id === raw) ? raw : orgCompId;
  });
  const effectiveOrgCompId = sources.some((s) => s.org_comp_id === activeSource) ? activeSource : orgCompId;
  const readQuery = () => {
    const q = new URLSearchParams(window.location.search);
    const swimmer = Number(q.get('swimmer'));
    const heat = Number(q.get('heat'));
    if (Number.isFinite(swimmer) && swimmer > 0) return { zoom: 'swimmer' as Zoom, swimmerId: swimmer, orgDisciplineId: null as number | null, heat: null as number | null };
    if (Number.isFinite(heat) && heat > 0) return { zoom: 'heat' as Zoom, swimmerId: null as number | null, orgDisciplineId: heat, heat: null as number | null };
    return { zoom: 'programme' as Zoom, swimmerId: null as number | null, orgDisciplineId: null as number | null, heat: null as number | null };
  };

  const [state, setState] = useState(readQuery);

  const setUrl = useCallback((params: { swimmer?: number | null; heat?: number | null }) => {
    const url = new URL(window.location.href);
    url.searchParams.delete('swimmer');
    url.searchParams.delete('heat');
    if (params.swimmer != null) url.searchParams.set('swimmer', String(params.swimmer));
    if (params.heat != null) url.searchParams.set('heat', String(params.heat));
    window.history.replaceState(null, '', url.toString());
  }, []);

  // Источники ОТКРЫТОГО пловца — подсветка подтабов «в какой день он плывёт».
  // Сбрасываются при уходе с карточки: иначе подсветка переживёт закрытие.
  const [swimmerSources, setSwimmerSources] = useState<number[]>([]);

  // Нашли дни пловца — сразу выбираем день его ПЕРВОГО старта (первый элемент выдачи,
  // она отсортирована по дню). Ответ «когда» должен читаться сверху, а не из списка.
  const handleSwimmerSources = useCallback((ids: number[]) => {
    setSwimmerSources(ids);
    if (ids.length > 0) setActiveSource(ids[0]);
  }, []);

  const openProgramme = useCallback(() => { setUrl({}); setSwimmerSources([]); setState({ zoom: 'programme', swimmerId: null, orgDisciplineId: null, heat: null }); }, [setUrl]);
  const openEvent = useCallback((orgDisciplineId: number) => { setUrl({ heat: orgDisciplineId }); setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat: null }); }, [setUrl]);
  const openSwimmer = useCallback((swimmerId: number) => { setUrl({ swimmer: swimmerId }); setState({ zoom: 'swimmer', swimmerId, orgDisciplineId: null, heat: null }); }, [setUrl]);

  // Разворот заплыва из карточки пловца — открываем зум 2 той дисциплины. Заодно
  // переключаем ПОДТАБ: карточка собрана из всех источников, и заплыв может лежать не в
  // том, что открыт сейчас; без переключения зум 2 запросил бы дисциплину у чужого compID
  // и показал «нет данных».
  const openHeatFromSwimmer = useCallback((srcOrgCompId: number, orgDisciplineId: number, heat: number) => {
    const url = new URL(window.location.href);
    url.searchParams.set('src', String(srcOrgCompId));
    url.searchParams.set('heat', String(orgDisciplineId));
    url.searchParams.delete('swimmer');
    window.history.replaceState(null, '', url.toString());
    setActiveSource(srcOrgCompId);
    setState({ zoom: 'heat', swimmerId: null, orgDisciplineId, heat });
  }, []);

  // Смена источника всегда возвращает на зум 1: heat/swimmer принадлежат ПРОШЛОМУ
  // протоколу, и в новом их идентификаторов может не быть вовсе.
  const selectSource = useCallback((next: number) => {
    const url = new URL(window.location.href);
    url.searchParams.set('src', String(next));
    url.searchParams.delete('swimmer');
    url.searchParams.delete('heat');
    window.history.replaceState(null, '', url.toString());
    setActiveSource(next);
    setSwimmerSources([]);
    setState({ zoom: 'programme', swimmerId: null, orgDisciplineId: null, heat: null });
  }, []);

  // Все источники соревнования: поиск и карточка пловца работают по ним ЦЕЛИКОМ, а не по
  // активному подтабу — родителю всё равно, в каком окружном протоколе плывёт его ребёнок.
  const allOrgCompIds = sources.length > 0 ? sources.map((s) => s.org_comp_id) : [orgCompId];

  return (
    <div>
      {/* Панель «найти своего» — над зумами, видна всегда: это вход в таб для того, кто
          пришёл по одному вопросу «когда плывёт мой». */}
      <SwimmerFinder orgCompIds={allOrgCompIds} onOpenSwimmer={openSwimmer} />
      {sources.length > 1 && (
        <SourceTabs
          sources={sources}
          activeOrgCompId={effectiveOrgCompId}
          swimmerSources={state.zoom === 'swimmer' ? swimmerSources : undefined}
          onSelect={selectSource}
        />
      )}
      {state.zoom === 'programme' && <ProgrammeZoom orgCompId={effectiveOrgCompId} onOpenEvent={openEvent} />}
      {state.zoom === 'heat' && state.orgDisciplineId != null && (
        <HeatZoom orgCompId={effectiveOrgCompId} orgDisciplineId={state.orgDisciplineId} heat={state.heat} onBack={openProgramme} onOpenSwimmer={openSwimmer} />
      )}
      {state.zoom === 'swimmer' && state.swimmerId != null && (
        <SwimmerZoom
          orgCompIds={allOrgCompIds}
          swimmerId={state.swimmerId}
          onBack={openProgramme}
          onOpenHeat={openHeatFromSwimmer}
          onSourcesResolved={handleSwimmerSources}
        />
      )}
    </div>
  );
}
