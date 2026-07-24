import React, { useMemo, useState } from 'react';
import UI_SwimmerGallery from '../../../components/mix/swimmer-gallery/swimmer-gallery';
import { HelperMedia } from '../../../../utils/helpers';
import { GalleryItem } from '../../../../utils/interfaces/results';
import type { CompetitionMediaItem } from '../../../../hooks/useCompetitionMedia';

// Таб Media шапки соревнования: грид «кубиков» 16:9 (превью + play-триангл для видео),
// клик → лайтбокс UI_SwimmerGallery (контролируемый режим, один экземпляр на всю сетку —
// паттерн results-table). Источник данных — useCompetitionMedia(sourceParams).items,
// уже включает competition-level медиа (result_id: null).

interface Props {
  items: CompetitionMediaItem[];
  isAuthenticated: boolean;
  /** undefined — Add media недоступен (competitionId ещё не резолвлен). */
  canAddMedia: boolean;
  onAddMedia: () => void;
  addingMedia?: boolean;
}

function MediaTile({ item, onClick }: { item: CompetitionMediaItem; onClick: () => void }) {
  const thumb = HelperMedia.resolveThumbUrl(item.type, item.sourceType ?? 'other', item.url ?? '');
  return (
    <button
      type="button"
      onClick={onClick}
      className="group relative aspect-video overflow-hidden rounded-[12px] border"
      style={{ borderColor: 'var(--theme-mode-border)', background: 'var(--theme-mode-surface)' }}
    >
      {thumb ? (
        <img src={thumb} alt="" className="h-full w-full object-cover transition-transform group-hover:scale-105" />
      ) : (
        <div
          className="flex h-full w-full items-center justify-center text-[28px]"
          style={{ color: 'var(--theme-mode-text-muted)' }}
        >
          {item.type === 'image' ? '🖼' : '▶'}
        </div>
      )}
      {item.type === 'video' && (
        <span
          className="absolute inset-0 flex items-center justify-center"
          style={{ background: 'rgba(0,0,0,0.15)' }}
        >
          <span className="flex h-9 w-9 items-center justify-center rounded-full bg-black/55 text-white">
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M8 5v14l11-7z" />
            </svg>
          </span>
        </span>
      )}
    </button>
  );
}

export default function CompetitionMedia({ items, isAuthenticated, canAddMedia, onAddMedia, addingMedia }: Props) {
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  const gallery: GalleryItem[] = useMemo(
    () => items.map((i) => ({ type: i.type, sourceType: i.sourceType, url: i.url })),
    [items],
  );

  if (items.length === 0) {
    return (
      <div
        className="mt-4 flex min-h-[160px] flex-col items-center justify-center gap-3 rounded-[14px] px-4 text-center"
        style={{
          border: '1px dashed var(--theme-mode-border-input)',
          background: 'var(--theme-mode-surface)',
          color: 'var(--theme-mode-text-muted)',
        }}
      >
        <div className="text-[13.5px] font-semibold">No media yet</div>
        {isAuthenticated ? (
          canAddMedia && (
            <button
              type="button"
              onClick={onAddMedia}
              disabled={addingMedia}
              className="rounded-[10px] px-3.5 py-2 text-[12.5px] font-extrabold shadow-sm transition-opacity hover:opacity-90 disabled:opacity-50"
              style={{ background: 'var(--theme-primary)', color: 'var(--theme-mode-accent-text)' }}
            >
              ＋ Add the first video / photo
            </button>
          )
        ) : (
          <div className="text-[12.5px] font-semibold">Sign in to add media</div>
        )}
      </div>
    );
  }

  return (
    <div className="mt-4">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {items.map((item, idx) => (
          <MediaTile key={`${item.url}-${idx}`} item={item} onClick={() => setOpenIndex(idx)} />
        ))}
      </div>
      <UI_SwimmerGallery gallery={gallery} openIndex={openIndex} onClose={() => setOpenIndex(null)} popupSize="lg" />
    </div>
  );
}
