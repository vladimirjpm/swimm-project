import React, { useCallback, useMemo, useState } from 'react';
import AddLinkModal, { AddLinkSwimmerOption } from '../../../my-media-project/components/add-link-modal';
import { addUserMedia, AllUserMediaDto } from '../../../my-media-project/use-all-my-media';
import type { FavoriteDto } from '../../../../hooks/useFavorites';
import type { CompetitionOverview } from './types';

// Оркестрация «Add media» для шапки соревнования (hero-кнопка + пустое состояние
// таба Media) — общий флоу, чтобы обе точки входа открывали один и тот же попап.
// Пловцы для пикера грузятся ЛЕНИВО (только по клику) из ДВУХ источников и мёржатся
// по swimmer_id: GET /api/me/media (media-page.md §5) + GET /api/me/favorites. Без
// фаворитов юзер без единого медиа получал пустой список → Save становился no-op
// (handoff-competition-header.md §3). Медиа приоритетнее (hint 'has media'), фавориты
// добивают остальных (hint 'favorite').

function isNumStr(v?: string): v is string {
  return !!v && /^\d+$/.test(v);
}

interface Params {
  sourceParams?: Record<string, string>;
  overview: CompetitionOverview | null;
  title: string;
  refresh: () => void;
}

export function useCompetitionAddMedia({ sourceParams, overview, title, refresh }: Params) {
  const [open, setOpen] = useState(false);
  const [loadingSwimmers, setLoadingSwimmers] = useState(false);
  const [swimmers, setSwimmers] = useState<AddLinkSwimmerOption[]>([]);

  // competitionId источника, если он числовой; иначе — первый день (для ?eventId=).
  const competitionId = useMemo<number | null>(() => {
    if (isNumStr(sourceParams?.competitionId)) return Number(sourceParams!.competitionId);
    const firstDay = overview?.days?.[0]?.competition_id;
    return firstDay != null ? firstDay : null;
  }, [sourceParams, overview]);

  const canAdd = competitionId != null;

  const openModal = useCallback(async () => {
    if (!canAdd || loadingSwimmers) return;
    setLoadingSwimmers(true);
    try {
      const [mediaRes, favRes] = await Promise.all([
        fetch('/api/me/media', { credentials: 'include' }),
        fetch('/api/me/favorites', { credentials: 'include' }),
      ]);
      const mediaList: AllUserMediaDto[] = mediaRes.ok ? await mediaRes.json() : [];
      const favList: FavoriteDto[] = favRes.ok ? await favRes.json() : [];

      const byId = new Map<number, AddLinkSwimmerOption>();
      // Медиа приоритетнее — заносим первыми, чтобы hint 'has media' не перебивался.
      for (const m of mediaList) {
        if (!byId.has(m.swimmer_id)) {
          byId.set(m.swimmer_id, { id: m.swimmer_id, name: m.swimmer_name, hint: 'has media' });
        }
      }
      // Фавориты-пловцы добивают список (у кого ещё нет медиа).
      for (const f of favList) {
        if (f.target_type !== 'swimmer' || f.swimmer_id == null || byId.has(f.swimmer_id)) continue;
        byId.set(f.swimmer_id, {
          id: f.swimmer_id,
          name: f.swimmer_name ?? `#${f.swimmer_id}`,
          hint: 'favorite',
        });
      }
      setSwimmers(Array.from(byId.values()));
    } finally {
      setLoadingSwimmers(false);
      setOpen(true);
    }
  }, [canAdd, loadingSwimmers]);

  const closeModal = useCallback(() => setOpen(false), []);

  const handleSave = useCallback(
    async (input: Parameters<typeof addUserMedia>[0]) => {
      const item = await addUserMedia(input);
      if (item) {
        refresh();
        return true;
      }
      return false;
    },
    [refresh],
  );

  const modalNode = open && competitionId != null ? (
    <AddLinkModal
      swimmers={swimmers}
      initialSwimmerId={swimmers[0]?.id}
      fixedCompetitionId={competitionId}
      contextLabel={
        <span>
          <b>Competition media</b> 📎
          <span dir="auto" className="ml-2 text-[rgba(203,224,240,0.55)]">{title}</span>
        </span>
      }
      onClose={closeModal}
      onSave={handleSave}
    />
  ) : null;

  return { canAdd, openModal, loadingSwimmers, modalNode };
}
