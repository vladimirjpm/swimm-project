import React, { useMemo, useState } from 'react';
import UI_SwimmerGallery from '../../../../components/mix/swimmer-gallery/swimmer-gallery';
import { HelperMedia } from '../../../../../utils/helpers';
import { GalleryItem } from '../../../../../utils/interfaces/results';
import type { CompetitionMediaItem } from '../../../../../hooks/useCompetitionMedia';

// Карточка модуля Media (9d): заголовок + грид 4 кубика 16:9 (первые 3 — превью, 4-й — «＋N»
// остатка при >4 медиа) + персональная золотая полоса «My media» (только залогиненному).
//
// ПРАВИЛА КАРТОЧКИ — docs/competition-overview-cards.md (раздел Media) + docs/media-page.md
// (модель Sys_UserMedia, видимость публикаций, footguns эстафет).
// Реализация по спеке dc.html секция 9d (sc-if is9dMedia): паттерн кубика переиспользован
// из ../competition-media.tsx (MediaTile: превью через HelperMedia.resolveThumbUrl,
// play-оверлей для видео), лайтбокс UI_SwimmerGallery в контролируемом режиме.

interface Props {
  items: CompetitionMediaItem[];
  isAuthenticated: boolean;
  onAddMedia?: () => void;
  onOpenTab(tab: 'swims' | 'clubs' | 'media' | 'records'): void;
}

const MAX_TILES = 4;

function MediaTile({ item, onClick }: { item: CompetitionMediaItem; onClick: () => void }) {
  const thumb = HelperMedia.resolveThumbUrl(item.type, item.sourceType ?? 'other', item.url ?? '');
  return (
    <button
      type="button"
      onClick={onClick}
      className="group relative aspect-video overflow-hidden rounded-[10px] border"
      style={{ borderColor: 'var(--theme-mode-border)', background: 'var(--theme-mode-surface)' }}
    >
      {thumb ? (
        <img src={thumb} alt="" className="h-full w-full object-cover transition-transform group-hover:scale-105" />
      ) : (
        <div
          className="flex h-full w-full items-center justify-center text-[22px]"
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
          <span className="flex h-8 w-8 items-center justify-center rounded-full bg-black/55 text-white">
            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
              <path d="M8 5v14l11-7z" />
            </svg>
          </span>
        </span>
      )}
    </button>
  );
}

export default function ModuleCardMedia({ items, isAuthenticated, onAddMedia, onOpenTab }: Props) {
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  const gallery: GalleryItem[] = useMemo(
    () => items.map((i) => ({ type: i.type, sourceType: i.sourceType, url: i.url })),
    [items],
  );

  // Если >4 медиа — показываем первые 3 кубика + «＋N» остатка; иначе — все кубики (≤4).
  const hasOverflow = items.length > MAX_TILES;
  const visibleTiles = hasOverflow ? items.slice(0, MAX_TILES - 1) : items;
  const overflowCount = items.length - visibleTiles.length;

  return (
    <section className="ov2-card ov2-module--media" data-module="media">
      <div className="flex items-baseline gap-[10px]">
        <div className="ov2-card__title">Media</div>
        <span className="text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
          {items.length} items
        </span>
        <span className="flex-1" />
        <button type="button" onClick={() => onOpenTab('media')} className="ov2-card__link">
          Media tab →
        </button>
      </div>

      <div className="mt-[10px] grid grid-cols-2 gap-[10px] lg:grid-cols-4">
        {visibleTiles.map((item, idx) => (
          <MediaTile key={`${item.url}-${idx}`} item={item} onClick={() => setOpenIndex(idx)} />
        ))}
        {hasOverflow && (
          <button
            type="button"
            onClick={() => onOpenTab('media')}
            className="relative aspect-video overflow-hidden rounded-[10px] border flex items-center justify-center text-[15px] font-extrabold"
            style={{ borderColor: 'var(--theme-mode-border)', background: 'var(--theme-mode-surface)', color: 'var(--m-solid)' }}
          >
            ＋{overflowCount}
          </button>
        )}
      </div>

      <UI_SwimmerGallery gallery={gallery} openIndex={openIndex} onClose={() => setOpenIndex(null)} popupSize="lg" />

      {isAuthenticated && (
        <div
          className="mt-[10px] flex flex-wrap items-center gap-[10px] rounded-[10px] px-3 py-[10px]"
          style={{ background: 'var(--theme-personal-bg)', border: '1px solid var(--theme-personal-border)' }}
        >
          <span
            className="rounded-[999px] px-[10px] py-[3px] text-[11px] font-extrabold"
            style={{ background: 'var(--theme-personal-badge-bg)', color: 'var(--theme-personal-accent)' }}
          >
            My media
          </span>
          <span className="flex-1" />
          {onAddMedia && (
            <button
              type="button"
              onClick={onAddMedia}
              className="rounded-[8px] px-3 py-[5px] text-[12px] font-extrabold"
              style={{
                border: '1px solid var(--theme-personal-border)',
                color: 'var(--theme-personal-accent)',
                background: 'var(--theme-mode-surface)',
              }}
            >
              ＋ Add video / photo
            </button>
          )}
        </div>
      )}
    </section>
  );
}
