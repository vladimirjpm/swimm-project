import { useEffect, useRef, useState } from 'react';
import type { UserMediaPublicationDto } from '../../hooks/useUserMedia';
import HelperSwimmer from '../../utils/helpers/helper-swimmer';
import { MySwimDto, SwimMediaDto } from './use-my-swims';
import { derivedCardStatus } from './components/status-styles';
import type { Seg, StatusFilter, GroupFilter } from './components/my-media-filter-panel';
import { effectiveSwimmerSelection, toggleSwimmer } from './swimmer-selection';

// Принадлежность заплыва пловцу — ТОЛЬКО через канон-хелпер (docs/relays.md: «новый
// фильтр/счётчик по пловцу — зови это, не сравнивай swimmer_id сам»): у эстафеты
// swimmer_id это одна нога, остальные ноги теряются.
export const swimBelongsTo = (s: MySwimDto, id: number) => HelperSwimmer.resultBelongsToSwimmer(s, id);

/**
 * Фильтры страницы и что после них видно: заплывы, медиа соревнований, Unlinked. Пловец и
 * сезон — тоже фильтры, но «наверху страницы»: сброс их не трогает (сезон живёт в
 * use-my-swims-season).
 *
 * Пловцы — мультиселект (решение Влада 18.11.2026, `swimmer-selection.ts`): `swimmerFilter` —
 * действующий выбор (пустой — All). При загрузке отмечены «Me» и семья (`defaultSwimmers`; null —
 * ещё не готово: избранное или заплывы грузятся); дальше выбор ведёт пользователь.
 */
export function useSwimFilters(params: {
  swims: MySwimDto[];
  competitionMedia: SwimMediaDto[];
  unlinkedMedia: SwimMediaDto[];
  publicationsByMedia: Map<number, UserMediaPublicationDto[]>;
  /** Пловцы с заплывами в сезоне, в порядке списка панели. */
  swimmerIds: number[];
  /** Начальный выбор (Me + семья); null — данные ещё грузятся. Применяется один раз. */
  defaultSwimmers: number[] | null;
}) {
  const { swims, competitionMedia, unlinkedMedia, publicationsByMedia, swimmerIds, defaultSwimmers } = params;

  const [selectedSwimmers, setSelectedSwimmers] = useState<number[]>([]);
  const swimmersInitialized = useRef(false);
  useEffect(() => {
    if (swimmersInitialized.current || defaultSwimmers == null) return;
    swimmersInitialized.current = true;
    setSelectedSwimmers(defaultSwimmers);
  }, [defaultSwimmers]);
  const swimmerFilter = effectiveSwimmerSelection(selectedSwimmers, swimmerIds);
  const toggleSwimmerFilter = (id: number) => {
    swimmersInitialized.current = true;
    setSelectedSwimmers((prev) => toggleSwimmer(effectiveSwimmerSelection(prev, swimmerIds), id, swimmerIds));
  };
  const clearSwimmerFilter = () => {
    swimmersInitialized.current = true;
    setSelectedSwimmers([]);
  };
  const [seg, setSeg] = useState<Seg>('all');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [groupFilter, setGroupFilter] = useState<GroupFilter>('all');
  const [competitionFilter, setCompetitionFilter] = useState<number | 'all'>('all');
  const [styleFilter, setStyleFilter] = useState<string | 'all'>('all');
  const [distanceFilter, setDistanceFilter] = useState<string | 'all'>('all');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');

  // Медиа «в группе» при любом статусе заявки (pending/approved/rejected) — статус сужает отдельный фильтр.
  const mediaMatchesGroup = (mediaId: number) => {
    if (groupFilter === 'all') return true;
    const pubs = publicationsByMedia.get(mediaId) ?? [];
    if (groupFilter === 'none') return pubs.length === 0;
    return pubs.some((p) => p.target_id === groupFilter);
  };

  /**
   * Статус ЛЮБОГО медиа, а не только видео (09.09.2026, запрос Влада «отвяжи от with video»).
   * Раньше фильтр жил под сегментом «With video» и внутри ещё раз резал по `media_type`, из-за
   * чего «private» — то есть «нет ни одной публикации» — нельзя было спросить про фото, хотя
   * для фото это ровно такой же осмысленный статус.
   */
  const mediaMatchesStatus = (mediaId: number) =>
    statusFilter === 'all'
    || derivedCardStatus(publicationsByMedia.get(mediaId) ?? []) === statusFilter;

  // ── Фильтрация ────────────────────────────────────────────────────────────
  const bySwimmer = swimmerFilter.length === 0
    ? swims
    : swims.filter((s) => swimmerFilter.some((id) => swimBelongsTo(s, id)));

  const segCount = (k: Seg) =>
    bySwimmer.filter((s) => {
      const v = s.media.some((m) => m.media_type === 'video');
      return k === 'all' || (k === 'with' ? v : !v);
    }).length;

  const filtered = bySwimmer.filter((s) => {
    const hasVideo = s.media.some((m) => m.media_type === 'video');
    if (seg === 'with' && !hasVideo) return false;
    if (seg === 'without' && hasVideo) return false;
    if (competitionFilter !== 'all' && s.competition_id !== competitionFilter) return false;
    if (styleFilter !== 'all' && s.style !== styleFilter) return false;
    if (distanceFilter !== 'all' && s.distance !== distanceFilter) return false;
    if (groupFilter !== 'all' && !s.media.some((m) => mediaMatchesGroup(m.id))) return false;
    if (dateFrom && s.date < dateFrom) return false;
    if (dateTo && s.date > dateTo) return false;
    if (statusFilter !== 'all' && !s.media.some((m) => mediaMatchesStatus(m.id))) return false;
    return true;
  });

  // Оба списка слушают ТЕ ЖЕ фильтры, что и заплывы: иначе выбранный «pending» сужал бы
  // верхнюю часть страницы и молча оставлял нетронутыми карточки соревнований и Unlinked.
  const mediaVisible = (id: number) => mediaMatchesGroup(id) && mediaMatchesStatus(id);
  const visibleCompetitionMedia = competitionMedia.filter((m) => mediaVisible(m.id));
  const visibleUnlinkedMedia = unlinkedMedia.filter((m) => mediaVisible(m.id));

  // Сколько фильтров ПАНЕЛИ сужают выборку: цифра на кнопке «Filters» и признак для кнопки
  // сброса. Пловец и сезон не в счёт — они живут наверху страницы и всегда на виду.
  const activeFilterCount =
    [competitionFilter, styleFilter, distanceFilter].filter((v) => v !== 'all').length +
    (dateFrom || dateTo ? 1 : 0) +
    (seg !== 'all' ? 1 : 0) +
    (groupFilter !== 'all' ? 1 : 0) +
    (statusFilter !== 'all' ? 1 : 0);

  // Пловца и сезон сброс НЕ трогает (хендофф): это не сужение выборки, а ответ на вопрос
  // «чьи заплывы и за какой сезон я смотрю» — сбросить их значит показать чужое.
  const clearAll = () => {
    setSeg('all');
    setStatusFilter('all');
    setGroupFilter('all');
    setCompetitionFilter('all');
    setStyleFilter('all');
    setDistanceFilter('all');
    setDateFrom('');
    setDateTo('');
  };

  return {
    swimmerFilter, toggleSwimmerFilter, clearSwimmerFilter,
    seg, setSeg,
    statusFilter, setStatusFilter,
    groupFilter, setGroupFilter,
    competitionFilter, setCompetitionFilter,
    styleFilter, setStyleFilter,
    distanceFilter, setDistanceFilter,
    dateFrom, setDateFrom,
    dateTo, setDateTo,
    bySwimmer, segCount, filtered,
    visibleCompetitionMedia, visibleUnlinkedMedia,
    activeFilterCount, clearAll,
  };
}
