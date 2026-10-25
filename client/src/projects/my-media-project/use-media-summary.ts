import { useMemo } from 'react';
import type { UserMediaPublicationDto } from '../../hooks/useUserMedia';
import { MySwimDto, SwimMediaDto } from './use-my-swims';
import { derivedCardStatus, type CardStatus } from './components/status-styles';

/**
 * Справочники и сводки страницы, которые НЕ зависят от фильтров: публикации по медиа,
 * соревнования сезона, сводка «что у меня есть», группы «куда я поднял медиа».
 */
export function useMediaSummary(params: {
  swims: MySwimDto[];
  competitionMedia: SwimMediaDto[];
  unlinkedMedia: SwimMediaDto[];
  publications: UserMediaPublicationDto[];
}) {
  const { swims, competitionMedia, unlinkedMedia, publications } = params;

  const publicationsByMedia = useMemo(() => {
    const map = new Map<number, typeof publications>();
    for (const p of publications) {
      const list = map.get(p.user_media_id) ?? [];
      list.push(p);
      map.set(p.user_media_id, list);
    }
    return map;
  }, [publications]);

  // ── Справочники фильтров (из заплывов сезона) ─────────────────────────────
  const competitionOptions = useMemo(() => {
    const byId = new Map<number, { name: string; isChampionship: boolean }>();
    for (const s of swims) {
      if (!byId.has(s.competition_id)) {
        byId.set(s.competition_id, { name: s.competition_name, isChampionship: s.is_championship });
      }
    }
    return Array.from(byId.entries()).map(([id, c]) => ({ id, ...c }));
  }, [swims]);

  // ── Группы: «куда я поднял медиа» ─────────────────────────────────────────
  // Публикации приходят по ВСЕМУ моему медиа (все сезоны), а страница сезонная,
  // поэтому список чипов берём из публикаций, а счётчики — по медиа этой страницы.
  const pageMediaIds = useMemo(() => {
    const ids: number[] = [];
    for (const sw of swims) for (const m of sw.media) ids.push(m.id);
    for (const m of competitionMedia) ids.push(m.id);
    for (const m of unlinkedMedia) ids.push(m.id);
    return ids;
  }, [swims, competitionMedia, unlinkedMedia]);

  /**
   * Сводка «что у меня есть» — карточка-инфо в шапке панели, слева от баннера модерации.
   * Считается по ВСЕМУ медиа страницы, а не по видимому: списки сужают фильтры, а сводка
   * должна стоять на месте — иначе выбранный «pending» покажет сам себя и нули во всём
   * остальном. Дедуп по id: одно медиа висит на нескольких заплывах эстафеты (docs/relays.md).
   */
  const mediaStats = useMemo(() => {
    const byId = new Map<number, SwimMediaDto>();
    for (const s of swims) for (const m of s.media) byId.set(m.id, m);
    for (const m of competitionMedia) byId.set(m.id, m);
    for (const m of unlinkedMedia) byId.set(m.id, m);
    const status: Record<CardStatus, number> = { private: 0, pending: 0, published: 0, rejected: 0 };
    let videos = 0;
    for (const m of byId.values()) {
      if (m.media_type === 'video') videos += 1;
      status[derivedCardStatus(publicationsByMedia.get(m.id) ?? [])] += 1;
    }
    return { total: byId.size, videos, photos: byId.size - videos, status };
  }, [swims, competitionMedia, unlinkedMedia, publicationsByMedia]);

  const groupOptions = useMemo(() => {
    const names = new Map<number, string>();
    for (const p of publications) if (!names.has(p.target_id)) names.set(p.target_id, p.target_name);
    const counts = new Map<number, number>();
    let notShared = 0;
    for (const id of pageMediaIds) {
      const pubs = publicationsByMedia.get(id) ?? [];
      if (pubs.length === 0) { notShared += 1; continue; }
      const seen = new Set<number>();
      for (const p of pubs) {
        if (seen.has(p.target_id)) continue;
        seen.add(p.target_id);
        counts.set(p.target_id, (counts.get(p.target_id) ?? 0) + 1);
      }
    }
    const groups = Array.from(names, ([id, name]) => ({ id, name, count: counts.get(id) ?? 0 }))
      .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
    return { groups, notShared, total: pageMediaIds.length };
  }, [publications, publicationsByMedia, pageMediaIds]);

  return { publicationsByMedia, competitionOptions, mediaStats, groupOptions };
}
