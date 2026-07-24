import { useEffect, useState } from 'react';
import { GalleryItem } from '../../utils/interfaces/results';

/**
 * Видимое зрителю медиа пловца для галереи на странице пловца
 * (GET /api/swimmers/{id}/media, per-viewer): аноним — approved public; залогиненный —
 * плюс своё и members своих групп. Пусто/сеть-ошибка → [].
 */
export function useSwimmerMedia(swimmerId: number | null): GalleryItem[] {
  const [items, setItems] = useState<GalleryItem[]>([]);

  useEffect(() => {
    if (swimmerId == null || swimmerId <= 0) { setItems([]); return; }
    let cancelled = false;
    fetch(`/api/swimmers/${swimmerId}/media`, { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : []))
      .then((list: { media_type: string; source_type: string; url: string }[]) => {
        if (cancelled) return;
        setItems(
          list.map((i) => ({
            type: i.media_type === 'image' ? 'image' : 'video',
            sourceType: i.source_type as GalleryItem['sourceType'],
            url: i.url,
          })),
        );
      })
      .catch(() => { if (!cancelled) setItems([]); });
    return () => { cancelled = true; };
  }, [swimmerId]);

  return items;
}
