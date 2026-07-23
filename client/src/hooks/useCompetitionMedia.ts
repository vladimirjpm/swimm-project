import { useEffect, useState } from 'react';
import { GalleryItem } from '../utils/interfaces/results';

/**
 * Видимое зрителю медиа заплывов соревнования/события (этап 4 media-visibility-model):
 * своё (владельцу) + одобренные публикации групп (public — всем, members — членам).
 * GET /api/media/results per-viewer, поэтому без общего кэша; аноним получает public-слой.
 * Возвращает map result_id → GalleryItem[] для подмешивания в res.gallery строк таблицы.
 */
export function useCompetitionMedia(sourceParams?: Record<string, string>): Map<number, GalleryItem[]> {
  const [byResultId, setByResultId] = useState<Map<number, GalleryItem[]>>(() => new Map());

  const competitionId = sourceParams?.competitionId;
  const eventId = sourceParams?.eventId;
  const groupSlug = sourceParams?.group;

  useEffect(() => {
    // Только числовые id: селектор может держать competitionId=last до резолва.
    // group-режим (?group=slug) — слаг, сервер сам скоупит по ростеру группы.
    const isNum = (v?: string) => !!v && /^\d+$/.test(v);
    if (!isNum(competitionId) && !isNum(eventId) && !groupSlug) {
      setByResultId(new Map());
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
      .then((list: { result_id: number; media_type: string; source_type: string; url: string }[]) => {
        if (cancelled) return;
        const map = new Map<number, GalleryItem[]>();
        for (const item of list) {
          const arr = map.get(item.result_id) ?? [];
          arr.push({
            type: item.media_type === 'image' ? 'image' : 'video',
            sourceType: item.source_type as GalleryItem['sourceType'],
            url: item.url,
          });
          map.set(item.result_id, arr);
        }
        setByResultId(map);
      })
      .catch(() => {
        if (!cancelled) setByResultId(new Map());
      });

    return () => {
      cancelled = true;
    };
  }, [competitionId, eventId, groupSlug]);

  return byResultId;
}
