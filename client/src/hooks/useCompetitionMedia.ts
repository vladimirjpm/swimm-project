import { useCallback, useEffect, useState } from 'react';
import { GalleryItem } from '../utils/interfaces/results';

export interface CompetitionMediaItem extends GalleryItem {
  /** null — медиа уровня «соревнование» (не привязано к заплыву). */
  result_id: number | null;
}

export interface UseCompetitionMediaResult {
  byResultId: Map<number, GalleryItem[]>;
  /** Плоский список всего видимого медиа (включая competition-level) — таб Media, бейдж-счётчик. */
  items: CompetitionMediaItem[];
  /** Перезапросить медиа для текущих параметров (напр. после добавления видео со строки). */
  refresh: () => void;
}

/**
 * Видимое зрителю медиа заплывов соревнования/события (этап 4 media-visibility-model):
 * своё (владельцу) + одобренные публикации групп (public — всем, members — членам).
 * GET /api/media/results per-viewer, поэтому без общего кэша; аноним получает public-слой.
 * Возвращает { byResultId, refresh } — map result_id → GalleryItem[] для подмешивания
 * в res.gallery строк таблицы + ручной триггер повторной загрузки.
 */
export function useCompetitionMedia(sourceParams?: Record<string, string>): UseCompetitionMediaResult {
  const [byResultId, setByResultId] = useState<Map<number, GalleryItem[]>>(() => new Map());
  const [items, setItems] = useState<CompetitionMediaItem[]>([]);
  // Бампаем, чтобы форсировать перезапуск эффекта без изменения sourceParams.
  const [refreshToken, setRefreshToken] = useState(0);

  const competitionId = sourceParams?.competitionId;
  const eventId = sourceParams?.eventId;
  const groupSlug = sourceParams?.group;

  useEffect(() => {
    // Только числовые id: селектор может держать competitionId=last до резолва.
    // group-режим (?group=slug) — слаг, сервер сам скоупит по ростеру группы.
    const isNum = (v?: string) => !!v && /^\d+$/.test(v);
    if (!isNum(competitionId) && !isNum(eventId) && !groupSlug) {
      setByResultId(new Map());
      setItems([]);
      return;
    }

    let cancelled = false;
    const query = isNum(eventId)
      ? `eventId=${eventId}`
      : isNum(competitionId)
        ? `competitionId=${competitionId}`
        : `group=${encodeURIComponent(groupSlug!)}`;

    fetch(`/api/media/results?${query}`, { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : []))
      .then((list: { result_id: number | null; media_type: string; source_type: string; url: string }[]) => {
        if (cancelled) return;
        const map = new Map<number, GalleryItem[]>();
        const flat: CompetitionMediaItem[] = [];
        for (const item of list) {
          const gi: GalleryItem = {
            type: item.media_type === 'image' ? 'image' : 'video',
            sourceType: item.source_type as GalleryItem['sourceType'],
            url: item.url,
          };
          flat.push({ ...gi, result_id: item.result_id });
          // competition-level медиа (result_id == null) в карту заплывов не попадает
          if (item.result_id == null) continue;
          const arr = map.get(item.result_id) ?? [];
          arr.push(gi);
          map.set(item.result_id, arr);
        }
        setByResultId(map);
        setItems(flat);
      })
      .catch(() => {
        if (!cancelled) {
          setByResultId(new Map());
          setItems([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [competitionId, eventId, groupSlug, refreshToken]);

  const refresh = useCallback(() => {
    setRefreshToken((t) => t + 1);
  }, []);

  return { byResultId, items, refresh };
}
