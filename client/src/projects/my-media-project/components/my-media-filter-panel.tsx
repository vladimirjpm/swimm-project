import React from 'react';
import {
  FilterHostProvider,
  type FilterHost,
} from '../../components/filter-section/filter-host';
import FilterCard from '../../components/filter-section/filter-card';
import FilterSwimmingStyle from '../../components/filter-section/filter-swimming-style';
import DeepSeasonCarousel from '../../components/deep/season-carousel';
import type { CardStatus } from './status-styles';

/** Есть ли у заплыва видео. */
export type Seg = 'all' | 'with' | 'without';
export type StatusFilter = CardStatus | 'all';
/** Куда медиа поднято: конкретная группа, 'none' — ни в одну (личное), 'all' — не фильтруем. */
export type GroupFilter = number | 'all' | 'none';

/** Ключи карточек. По ним полоса выбранных фильтров раскрывает нужную (`openCards`). */
export type MediaCardKey =
  | 'season' | 'swimmers' | 'video' | 'style'
  | 'competition' | 'date' | 'group' | 'status';

/** Карточки, раскрытые при первом показе (хендофф: Swimmers, Video, Swimming Style). */
export const DEFAULT_OPEN_CARDS: MediaCardKey[] = ['swimmers', 'video', 'style'];

export interface GroupFilterOptions {
  groups: { id: number; name: string; count: number }[];
  notShared: number;
  total: number;
}

/**
 * Панель фильтров `/my-media` — **та же панель, что на results и `/season-best`**
 * (Ф3–Ф4 плана `docs/plans/my-media-filters-plan.md`, вид — по хендоффу
 * `!design_handoff/design_handoff_my_media_filters`).
 *
 * Панель — это список карточек: выключить фильтр = убрать строку JSX. Общими компонентами
 * рисуются стиль с дистанцией (`FilterSwimmingStyle` + `FilterDistance` внутри него) и
 * карусель сезонов; остальные карточки — свои, этих фильтров нет в общей модели.
 *
 * Своей палитры у панели нет: она приезжает переопределением токенов `--fc-*` / `--fseg-*`
 * на `.my-media-filters` (`my-media.css`) — тот же приём, что у `/season-best`.
 *
 * **Раскрытость карточек держит страница** (`openCards`): по клику в полосе выбранных
 * фильтров нужно раскрыть карточку того фильтра, по которому щёлкнули, а значит состояние
 * не может жить внутри карточки. Открытых может быть несколько — это не аккордеон.
 */
interface Props {
  host: FilterHost;
  /** Сезоны для карусели: год начала + подпись «2025/26». */
  seasons: { season: number; label: string }[];
  /** Выбранный сезон; null — ∞ «все сезоны». */
  season: number | null;
  onSeason: (v: number | null) => void;
  swimmers: { id: number; name: string; count: number }[];
  swimmerFilter: number | 'all';
  onSwimmer: (v: number | 'all') => void;
  /** Всего заплывов до фильтра по пловцу — счётчик строки «All». */
  totalSwims: number;
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
  openCards: Set<MediaCardKey>;
  onCardOpenChange: (key: MediaCardKey, open: boolean) => void;
  /** Сколько фильтров сужают выборку — цифра у кнопки сброса. */
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
  seasons,
  season,
  onSeason,
  swimmers,
  swimmerFilter,
  onSwimmer,
  totalSwims,
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
  openCards,
  onCardOpenChange,
  activeCount,
}: Props) {
  const competition = competitionOptions.find((c) => c.id === competitionFilter);
  const group =
    groupFilter === 'none'
      ? 'Not shared'
      : groupOptions.groups.find((g) => g.id === groupFilter)?.name;
  const swimmer = swimmers.find((s) => s.id === swimmerFilter);

  const dateSummary =
    dateFrom && dateTo ? `${dateFrom} – ${dateTo}` : dateFrom || dateTo || 'All';

  const card = (key: MediaCardKey) => ({
    open: openCards.has(key),
    onOpenChange: (open: boolean) => onCardOpenChange(key, open),
  });

  return (
    <FilterHostProvider host={host}>
      <div className="mmf">
        {/* Сезон — не фильтр общей модели, а другой запрос к серверу. Выбирается каруселью
            (решение Влада 07.09.2026), а в полосе над списком стоит только индикатор. */}
        <FilterCard
          title="Season"
          summary={season == null ? 'All seasons' : seasons.find((s) => s.season === season)?.label}
          isActive={season == null}
          {...card('season')}
        >
          {/* Класс темы карусели НЕ ставим: он уже есть выше — на корне страницы (сайдбар) и на
              самой шторке (портал). Прибитый здесь `theme-deep` пережил Ф5 и держал карусель
              тёмной на светлой странице: цифра сезона приезжала cyan со свечением. */}
          <div className="mmf__season">
            <DeepSeasonCarousel seasons={seasons} season={season} onSeason={onSeason} />
          </div>
        </FilterCard>

        <FilterCard
          title="Swimmers"
          summary={swimmer ? swimmer.name : 'All'}
          isActive={swimmerFilter !== 'all'}
          {...card('swimmers')}
        >
          <div className="mmf__list flex flex-col gap-2">
            <button
              type="button"
              className={seg(swimmerFilter === 'all')}
              onClick={() => onSwimmer('all')}
            >
              All · {totalSwims}
            </button>
            {swimmers.map((s) => (
              <button
                key={s.id}
                type="button"
                className={`${seg(swimmerFilter === s.id)} mmf__swimmer`}
                onClick={() => onSwimmer(s.id)}
              >
                <span className="mmf__initial" aria-hidden="true">
                  {s.name.trim().charAt(0).toUpperCase()}
                </span>
                {/* dir только на имени: без изоляции иврит уводит счётчик влево. */}
                <span dir="auto" className="mmf__swimmer-name">{s.name}</span>
                <span className="mmf__count">{s.count}</span>
              </button>
            ))}
          </div>
        </FilterCard>

        <FilterCard
          title="Video"
          summary={segValue === 'all' ? 'All' : SEG_LABEL[segValue]}
          isActive={segValue !== 'all'}
          {...card('video')}
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

        <FilterSwimmingStyle {...card('style')} />

        {competitionOptions.length > 1 && (
          <FilterCard
            title="Competition"
            summary={competition ? competition.name : 'All'}
            isActive={competitionFilter !== 'all'}
            {...card('competition')}
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
          {...card('date')}
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
            {...card('group')}
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

        {/* Статус есть у ЛЮБОГО медиа: «private» это «нет ни одной публикации», и для фото
            он значит ровно то же, что для видео. Раньше карточка показывалась только вместе
            с сегментом «With video» — из-за этого фильтр считали несуществующим. */}
        <FilterCard
          title="Publication status"
          summary={statusFilter === 'all' ? 'All' : statusFilter}
          isActive={statusFilter !== 'all'}
          {...card('status')}
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

        {/* Сброс — под карточками, а не в шапке панели: он про всё сразу, и место ему
            там, где список фильтров кончился. Сезон и пловца НЕ трогает (хендофф). */}
        {activeCount > 0 && (
          <button type="button" className="mmf__reset" onClick={host.reset}>
            Reset all · {activeCount}
          </button>
        )}
      </div>
    </FilterHostProvider>
  );
}

export default MyMediaFilterPanel;
