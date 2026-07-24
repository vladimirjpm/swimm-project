import React, { useCallback, useMemo, useState } from 'react';
import AddLinkModal, { AddLinkSwimmerOption } from '../../../my-media-project/components/add-link-modal';
import { addUserMedia, AllUserMediaDto } from '../../../my-media-project/use-all-my-media';
import type { CompetitionOverview } from './types';

// Оркестрация «Add media» для шапки соревнования (hero-кнопка + пустое состояние
// таба Media) — общий флоу, чтобы обе точки входа открывали один и тот же попап.
// Пловцы для пикера грузятся ЛЕНИВО (только по клику), из useAllMyMedia/GET /api/me/media
// (media-page.md §5) — фаворитов здесь намеренно нет (см. отчёт о выполнении).

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
      const r = await fetch('/api/me/media', { credentials: 'include' });
      const list: AllUserMediaDto[] = r.ok ? await r.json() : [];
      const byId = new Map<number, AddLinkSwimmerOption>();
      for (const m of list) {
        if (!byId.has(m.swimmer_id)) {
          byId.set(m.swimmer_id, { id: m.swimmer_id, name: m.swimmer_name, hint: 'has media' });
        }
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
