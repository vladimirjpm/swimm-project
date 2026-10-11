import { useState } from 'react';
import type { UserMediaPublicationDto } from '../../hooks/useUserMedia';
import HelperSwimmer from '../../utils/helpers/helper-swimmer';
import { MySwimDto, SwimMediaDto } from './use-my-swims';
import { derivedCardStatus } from './components/status-styles';
import type { Seg, StatusFilter, GroupFilter } from './components/my-media-filter-panel';

// Принадлежность заплыва пловцу — ТОЛЬКО через канон-хелпер (docs/relays.md: «новый
// фильтр/счётчик по пловцу — зови это, не сравнивай swimmer_id сам»): у эстафеты
// swimmer_id это одна нога, остальные ноги теряются.
export const swimBelongsTo = (s: MySwimDto, id: number) => HelperSwimmer.resultBelongsToSwimmer(s, id);

/**
 * Фильтры страницы и что после них видно: заплывы, медиа соревнований, Unlinked. Пловец и
 * сезон — тоже фильтры, но «наверху страницы»: сброс их не трогает (сезон живёт в
 * use-my-swims-season).
 */
export function useSwimFilters(params: {
  swims: MySwimDto[];
  competitionMedia: SwimMediaDto[];
  unlinkedMedia: SwimMediaDto[];
  publicationsByMedia: Map<number, UserMediaPublicationDto[]>;
}) {
  const { swims, competitionMedia, unlinkedMedia, publicationsByMedia } = params;

  const [swimmerFilter, setSwimmerFilter] = useState<number | 'all'>('all');
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
  const bySwimmer = swimmerFilter === 'all' ? swims : swims.filter((s) => swimBelongsTo(s, swimmerFilter));

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
    swimmerFilter, setSwimmerFilter,
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
