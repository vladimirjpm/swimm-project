import { useMemo, useState } from 'react';
import { MySwimDto, MySwimsResponse, SwimMediaDto, toggleLike } from './use-my-swims';

/**
 * Реакции ❤: оптимистичные оверрайды поверх ответа /api/me/swims. Тоггл сразу меняет
 * счётчик локально, сервер подтверждает/поправляет асинхронно (docs/media-page.md).
 * Наружу — три списка медиа страницы уже с оверрайдами.
 */
export function useLikeOverrides(data: MySwimsResponse) {
  const [likeOverrides, setLikeOverrides] = useState<Map<number, { count: number; mine: boolean }>>(new Map());

  const applyMediaOverride = (m: SwimMediaDto): SwimMediaDto => {
    const o = likeOverrides.get(m.id);
    return o ? { ...m, likes_count: o.count, my_like: o.mine } : m;
  };

  const swims: MySwimDto[] = useMemo(
    () => data.swims.map((s) => ({ ...s, media: s.media.map(applyMediaOverride) })),
    [data.swims, likeOverrides] // eslint-disable-line react-hooks/exhaustive-deps
  );
  const competitionMedia = useMemo(
    () => data.competition_media.map(applyMediaOverride),
    [data.competition_media, likeOverrides] // eslint-disable-line react-hooks/exhaustive-deps
  );
  const unlinkedMedia = useMemo(
    () => data.unlinked_media.map(applyMediaOverride),
    [data.unlinked_media, likeOverrides] // eslint-disable-line react-hooks/exhaustive-deps
  );

  const onToggleLike = async (m: SwimMediaDto) => {
    const next = !m.my_like;
    setLikeOverrides((prev) => new Map(prev).set(m.id, { count: m.likes_count + (next ? 1 : -1), mine: next }));
    const state = await toggleLike(m.id, next);
    if (state) setLikeOverrides((prev) => new Map(prev).set(m.id, state));
  };

  return { swims, competitionMedia, unlinkedMedia, onToggleLike };
}
