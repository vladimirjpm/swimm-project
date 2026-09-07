import React from 'react';
import {
  FilterHostProvider,
  type FilterHost,
} from '../../components/filter-section/filter-host';
import FilterCard from '../../components/filter-section/filter-card';
import FilterSwimmingStyle from '../../components/filter-section/filter-swimming-style';
import type { CardStatus } from './status-styles';

/** Есть ли у заплыва видео. */
export type Seg = 'all' | 'with' | 'without';
export type StatusFilter = CardStatus | 'all';
/** Куда медиа поднято: конкретная группа, 'none' — ни в одну (личное), 'all' — не фильтруем. */
export type GroupFilter = number | 'all' | 'none';

export interface GroupFilterOptions {
  groups: { id: number; name: string; count: number }[];
  notShared: number;
  total: number;
}

/**
 * Панель фильтров `/my-media` — **та же панель, что на results и `/season-best`**
 * (Ф3 плана `docs/plans/my-media-filters-plan.md`).
 *
 * Панель — это список карточек: выключить фильтр = убрать строку JSX. Общими компонентами
 * рисуются стиль с дистанцией (`FilterSwimmingStyle` + `FilterDistance` внутри него), они
 * ходят за значениями в хост страницы (`useMyMediaFilterHost`). Остальные пять карточек —
 * свои: этих фильтров нет в общей модели и заводить их там незачем.
 *
 * Своей палитры у панели нет: она приезжает переопределением токенов `--fc-*` / `--fseg-*`
 * на `.my-media-filters` (`my-media.css`) — тот же приём, что у `/season-best`.
 *
 * Чипов пловцов здесь НЕТ намеренно: «чьи заплывы я смотрю» — это идентичность экрана,
 * а не сужение выборки, и она остаётся наверху страницы вместе с каруселью сезонов
 * (сезон вообще не клиентский фильтр — он меняет запрос к серверу).
 */
interface Props {
  host: FilterHost;
  seg: Seg;
  onSeg: (v: Seg) => void;
  segCount: (k: Seg) => number;
  statusFilter: StatusFilter;
  onStatus: (v: StatusFilter) => void;
  groupFilter: GroupFilter;
  onGroup: (v: GroupFilter) => void;
  groupOptions: GroupFilterOptions;
  competitionFilter: number | 'all';
  onCompetition: (v: number | 'all') => void;
  competitionOptions: { id: number; name: string }[];
  dateFrom: string;
  dateTo: string;
  onDateFrom: (v: string) => void;
  onDateTo: (v: string) => void;
  /** Сколько фильтров сейчас сужают выборку — от нуля зависит только кнопка сброса. */
  activeCount: number;
}

const SEG_LABEL: Record<Seg, string> = {
  all: 'All swims',
  with: 'With video',
  without: 'Without video',
};

const STATUSES: StatusFilter[] = ['all', 'private', 'pending', 'published', 'rejected'];

const seg = (active: boolean) => `fseg${active ? ' fseg-active' : ''}`;

function MyMediaFilterPanel({
  host,
  seg: segValue,
  onSeg,
  segCount,
  statusFilter,
  onStatus,
  groupFilter,
  onGroup,
  groupOptions,
  competitionFilter,
  onCompetition,
  competitionOptions,
  dateFrom,
  dateTo,
  onDateFrom,
  onDateTo,
  activeCount,
}: Props) {
  const competition = competitionOptions.find((c) => c.id === competitionFilter);
  const group =
    groupFilter === 'none'
      ? 'Not shared'
      : groupOptions.groups.find((g) => g.id === groupFilter)?.name;

  const dateSummary =
    dateFrom && dateTo ? `${dateFrom} – ${dateTo}` : dateFrom || dateTo || 'All';

  return (
    <FilterHostProvider host={host}>
      <div className="mmf">
        <div className="mmf__head">
          <span className="mmf__title">Filters</span>
          {activeCount > 0 && (
            <button type="button" className="mmf__reset" onClick={host.reset}>
              Reset
            </button>
          )}
        </div>

        <FilterCard
          title="Video"
          summary={segValue === 'all' ? 'All' : SEG_LABEL[segValue]}
          isActive={segValue !== 'all'}
          defaultOpen
        >
          <div className="flex flex-col gap-2">
            {(['all', 'with', 'without'] as Seg[]).map((k) => (
              <button
                key={k}
                type="button"
                className={seg(segValue === k)}
                // Статус публикации осмыслен только у заплывов с видео — уходя с «With
                // video», снимаем его, иначе он сузил бы выборку невидимо.
                onClick={() => onSeg(k)}
              >
                {SEG_LABEL[k]} · {segCount(k)}
              </button>
            ))}
          </div>
        </FilterCard>

        <FilterSwimmingStyle />

        {competitionOptions.length > 1 && (
          <FilterCard
            title="Competition"
            summary={competition ? competition.name : 'All'}
            isActive={competitionFilter !== 'all'}
          >
            <div className="mmf__list flex flex-col gap-2">
              <button
                type="button"
                className={seg(competitionFilter === 'all')}
                onClick={() => onCompetition('all')}
              >
                All
              </button>
              {competitionOptions.map((c) => (
                <button
                  key={c.id}
                  type="button"
                  // dir на самой строке: названия соревнований ивритские, и без изоляции
                  // они утаскивают направление всей кнопки.
                  dir="auto"
                  className={seg(competitionFilter === c.id)}
                  onClick={() => onCompetition(c.id)}
                >
                  {c.name}
                </button>
              ))}
            </div>
          </FilterCard>
        )}

        <FilterCard
          title="Date range"
          summary={dateSummary}
          isActive={!!(dateFrom || dateTo)}
        >
          <div className="flex items-center gap-1.5">
            <input
              type="date"
              value={dateFrom}
              onChange={(e) => onDateFrom(e.target.value)}
              className="mmf__date"
              aria-label="From date"
            />
            <span className="mmf__dash">–</span>
            <input
              type="date"
              value={dateTo}
              onChange={(e) => onDateTo(e.target.value)}
              className="mmf__date"
              aria-label="To date"
            />
          </div>
        </FilterCard>

        {groupOptions.groups.length > 0 && (
          <FilterCard
            title="Shared with"
            summary={group ?? 'All'}
            isActive={groupFilter !== 'all'}
          >
            <div className="mmf__list flex flex-col gap-2">
              <button
                type="button"
                className={seg(groupFilter === 'all')}
                onClick={() => onGroup('all')}
              >
                All · {groupOptions.total}
              </button>
              {groupOptions.groups.map((g) => (
                <button
                  key={g.id}
                  type="button"
                  dir="auto"
                  className={seg(groupFilter === g.id)}
                  title={
                    g.count === 0
                      ? 'Nothing from this season is shared with this group'
                      : undefined
                  }
                  onClick={() => onGroup(g.id)}
                >
                  👥 {g.name} · {g.count}
                </button>
              ))}
              {groupOptions.notShared > 0 && (
                <button
                  type="button"
                  className={seg(groupFilter === 'none')}
                  onClick={() => onGroup('none')}
                >
                  Not shared · {groupOptions.notShared}
                </button>
              )}
            </div>
          </FilterCard>
        )}

        {/* Статус заявки есть только у видео — карточка появляется вместе с сегментом. */}
        {segValue === 'with' && (
          <FilterCard
            title="Publication status"
            summary={statusFilter === 'all' ? 'All' : statusFilter}
            isActive={statusFilter !== 'all'}
          >
            <div className="flex flex-wrap gap-2">
              {STATUSES.map((k) => (
                <button
                  key={k}
                  type="button"
                  className={seg(statusFilter === k)}
                  onClick={() => onStatus(k)}
                >
                  {k === 'all' ? 'All' : k[0].toUpperCase() + k.slice(1)}
                </button>
              ))}
            </div>
          </FilterCard>
        )}
      </div>
    </FilterHostProvider>
  );
}

export default MyMediaFilterPanel;
